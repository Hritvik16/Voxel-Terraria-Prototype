# 201a-bug-gpu-body-pool-overflow: DONE. Sealed bricks (no ray can reach them) take no GPU body: 0 refusals at all 5 poses, same pool, same picture.
## What changed (commit 417f1dd)
- ACCOUNT: `-gpupoolprobe 1` (capture only, `PlaygroundCapture.GpuPoolProbe.cs`): per survey pose, the box's dense bodies by source / kind / hidden / ring / layer / duplicates.
- FIX: `TerrainClipmap.Sealed.cs`. At each box chunk upload a Burst scan marks the INNER bricks (1..14 on every axis) whose voxels AND 1-voxel shell are all
  seal-solid (not air, fluid, torch, glass, river lip). They are staged as a uniform handle of their voxel 0's material: no GPU slot, no body upload.
  Why it is exact: a tier-0 ray passes air / clear fluid and stops at the first differing voxel, stepping face to face, so it always stops on the shell first;
  neighbour reads (AO, emissive bleed, river lip) only touch voxels next to air / fluid; shadow / torch / height reads test non-zero. Inner only = the shell
  sits in the same chunk, so a refused / evicted neighbour (air on the GPU) cannot expose it. Nothing is cached: the mask is recomputed at every upload.
  CPU side unchanged; fluid terrain directory unchanged; pool off (rigs) never runs it. `-gpuseal 0` = the old upload.
- Why this design is the efficient one (rule 11a): it removes the biggest exact share (26-37 % of bodies) with ~2 MB of CPU masks and no new GPU data. It also
  uploads fewer bytes. Rejected: a bigger pool (owner), dedup by content hash (8-39 %, needs hashing + refcount by content on every upload), border bricks (need neighbour state).
- Validators use `ExpectedGpuHandle`; ledger `[mem]` line adds `in use N, sealed N`. Capture-only flags: survey arm suffix `_gs0` / `_gs1` (box restaged with the seal OFF / ON),
  `-gpusealcmp 1` (residency, shells, far band and river time held during a pose's arms). TASKS.md, KNOWN_ISSUES (top entry), CLAUDE.md index line.
## Accounting (bodies; `Screenshots/201a/table201a.txt`; seed 42, noon, lookwait 40 s, cap 254,400)
| pose | before: in use / free / refused now / refused total | `-trees 0 -towns 0` (old upload) in use / refused total | after: in use / free / refused now | sealed | tree / ore bodies before |
|---|---|---|---|---|---|
| forest | 254,382 / 18 / 16 / 8,783 | 249,735 / 0 | 223,025 / 31,375 / 0 | 77,462 | 86,875 / 2,341 |
| jungle_high | 233,935 / 20,465 / 0 / 11,201 | 253,413 / 0 | 186,045 / 68,355 / 0 | 75,567 | 57,036 / 1,214 |
| town_eye | 253,441 / 959 / 0 / 13,995 | 254,400 / 65 | 141,698 / 112,702 / 0 | 132,758 | 8,485 / 10,552 |
| badlands_wall | 194,456 / 59,944 / 0 / 14,109 | 192,088 / 65 | 145,350 / 109,050 / 0 | 49,106 | 2,412 / 17,520 |
| s_redwood | 253,638 / 762 / 116 / 21,570 | 253,460 / 8,419 | 205,616 / 48,784 / 0 | 115,312 | 4,662 / 16,909 |
- What fills it (before): terrain 54-92 %, trees 1-37 % (forest 37 %), ores 0.5-9 %, fluid 0-36 % (badlands = sea). Kinds: one-value 0 (tg2 already collapses them), surface 35-55 %,
  solid-mixed 36-65 %. Hidden (sealed by the probe's test): inner 25-37 %, incl. chunk-border bricks 34-57 %. Duplicates 8-39 %. Towns: no visible share (the no-trees/towns arm is trees-dominated).
- Refusal TOTAL after the fix: 132, all during the start-up load before the first pose (pool peak 254,399 then); it did not grow at any pose (before: +2.4 k to +7.5 k per pose, 21,904 at the end).
## Evidence
- Identity (`Screenshots/201a/*_exact3.png`, `Logs/overnight/201a_diff.py`): no-upscaler 1080p stills, one launch, pool 420 k so neither arm refuses, residency held:
  seal ON (restaged) vs OFF (restaged) vs ON again = **0 px** at forest, town_eye and s_redwood (all three pairs). Earlier tries without the residency hold differed by streaming only.
- Tests: `SealedBrickTests` (9: 14^3 inner bricks, none on the border; one air / water / glass / torch voxel unseals exactly 8 bricks; oracle chunk with caves / pond / glass / torch:
  GPU view = CPU at every voxel next to a non-solid one; fits a pool it overflowed; switch off / off mid-run). Filter runs + `SUITE=all` **PASS 1096 FAIL 0 SKIP 30** (`Logs/overnight/201a/suite_all.out`).
- Memory (rule 11b, ledger lines of the launches above): process NOW per pose before 2,421-2,453 MB, after 2,410-2,440 MB. Process PEAK 2,616 -> 2,673 MB (start-up transient before
  the first pose; the `-trees 0 -towns 0` arm on the OLD upload peaked at 2,725, so +57 is inside the launch-to-launch spread; the fix itself adds <= 2.3 MB of CPU masks).
  CPU pool peak 492,744 -> 492,685 (valve 484,500 both). GPU pool cap unchanged (124 MB). Over the 2,504 MB in the task file in BOTH arms (this pose set includes 200's gallery).
- Rough cost (rule 10, no arms): survey settle frames p50 156/243/167/238/134 -> 190/251/177/242/159 ms per pose; the after arm draws the formerly refused chunks in tier 0. The scan was not timed.
- Logs: `Logs/overnight/201a/{player_on,player_sealed,player_notrees,player_exact3}.log`, `look_*.out`; scripts `Logs/overnight/201a_{account,after}.sh`.
## Not done / limits
- The 132 start-up refusals (spawn load, before the first pose) were not investigated. Chunk-border sealed bricks (17-58 k more bodies) and byte duplicates (8-39 %) are the next levers if 210's trees need room.
- 207 (retry only what fits) is still worth doing: with free bodies now the churn mostly stops, but a full pool can still happen at start-up.
## Next step
Supervisor: pictures to the doc; 207/208, then 210 (headroom per pose below).

## For the doc
- GPU body pool refusals over 5 poses: **21,904 -> 132** (all 132 at start-up; **0** at every pose, refused-now 0 everywhere) (`Screenshots/201a/lookreview_{on,sealed}.txt`).
- Free bodies (headroom for task 210's trees) per pose: forest 18 -> **31,375**, jungle_high 20,465 -> **68,355**, town_eye 959 -> **112,702**, badlands 59,944 -> **109,050**, redwood 762 -> **48,784** (512 B each).
- Bodies the GPU skips because no ray can reach them: **49-133 k per pose** (26-37 % of the box); pool size unchanged (254,400 = 124 MB).
- Picture unchanged: seal ON vs OFF **0 px** different at 3 poses (raw 1080p stills). Whole suite PASS 1096 FAIL 0.
- Memory: process now per pose 2,421-2,453 -> 2,410-2,440 MB; peak 2,616 -> 2,673 MB (start-up transient, launch spread up to 2,725 on the old code).
- Images (`Screenshots/201a/`):
  - `pair_s_redwood_spacecol.jpg` -- 200's redwood pose, LEFT before / RIGHT after: left shows ONE redwood, a second as floating leaf bits and grey walls in the foreground (refused chunks); right shows all three redwoods and plain ground.
  - `pair_forest.jpg` -- 195a's forest pose, LEFT before / RIGHT after: the same view (the hand-over hid the 16 refused chunks there); look for no change.
  - `s_redwood_spacecol_native_sf0_gs0_exact3.png` vs `s_redwood_spacecol_native_sf0_gs1_exact3.png` -- the identity pair (seal OFF / ON): pixel for pixel the same.
