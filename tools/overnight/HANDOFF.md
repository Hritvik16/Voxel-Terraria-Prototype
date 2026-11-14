# 197-bug-pits-return-zero-refused: DONE. The pits had two more causes. Tier 0 now hands those columns to the shells too: 0 pits in 3 proof launches (default ON). One side effect at a lake shore (below).
## Owner's question first: "terrain not loading on time?"
Partly. The pit chunks were not late from the generator. They had loaded, then were taken out of tier 0 again, in two ways:
1. **Retry gap.** The full GPU body pool refuses a chunk (task 194). When ONE slot frees, `RetryGpuRefused` clears the whole refused set and
   re-queues those chunks. The re-upload then waits in the budgeted dirty queue (up to ~1,250 chunks), and the slot still holds the
   refusal's air. With refused = 0, 194's hand-over was off. Probe q1, jungle_high: refused 0, 192 resident chunks missing, hand-over 0.
2. **Valve eviction.** The CPU pool's LRU valve evicts visible chunks (28-41k valve evictions per survey run; pool peak 495k of 570k, valve
   484.5k), and they reload. The eviction's clear is air, and 194 never hands a non-resident column over. Probe h1: the pit chunks were
   `ABSENT, stage Unloaded`.
Ruled out by the probe (every frame): GPU clipmap vs CPU mirror 0 differing; packed air mip 0 differing; store AIR chunks the generator
says are solid: 0.
## What changed (commit 3702aa7; no pool / eviction / layout / API / shader change)
- `TerrainClipmap.Tier0LacksChunk` (new `TerrainClipmap.SlotProbe.cs`): a resident chunk whose slot does not hold its own write.
- `FarField.ColumnState`: hand-over bit = refused, OR resident and lacking, OR not resident where shell 1's PUBLISHED directory holds it
  (`ShellStreamer.PublishedHolds`, outside its hole). Inside the hole those columns stay tier 0's (194's lost-houses rule).
- `FarField.HandOverColumns > 0` now gates `_Tier0Refused` (was GpuPoolRefusedNow > 0). The shader is unchanged (no recompile).
- `FarField.LackHandOver` / survey arm suffix `_lh0` = 194's rule, for before shots.
- Capture probe `-mirrorprobe 1` (`PlaygroundCapture.MirrorProbe.cs`):
  - Reads at END of frame. Uploads run in `Phase4Bootstrapper.LateUpdate`, after the survey coroutine, so my first probe (p1) read one
    pass early.
  - Lines: `[mirror]`, `[gpu]`, `[gen]`, then the same chunks again 5 s later.
- Tests: `Tier0LackHandOverTests` (6). They cover the retry gap with a real budgeted pass, its pit ray (old rule keeps the floor, new rule
  ends at the column entry), landing, first upload vs an air slot, an evicted column in vs out of the shell hole, and a foreign slot.
## Evidence
- Tests: filter run PASS 24 (`Logs/overnight/197/filter2.out`). Fast suite **PASS 1011 FAIL 0 SKIP 20** (`Logs/overnight/197/fast.out`).
- Launches: r1 (repro), p1 (probe one pass early), q1 / g1 / h1 (diagnosis builds), f1-f3 (final proof). Each has `look_*.out` and
  `player_*.log` in `Logs/overnight/197/`, shots in `Screenshots/197/<run>/`.
- jungle_high, fixed-rule frame in f1 / f2 / f3: hand-over columns 139 / 135 / 129, refused 0 / 0 / 0. These frames would pit under 194's
  rule. **No pits in any f-frame**, and forest / town_eye / badlands match their `_lh0` frames.
- No timing arms (rule 10). Rough cost: settle frames ~215-255 ms in both arms, the same as before. The pool churn above is the slow part.
## Side effects (owner should know)
- At a lake shore a handed-over column shows the SHELL's copy, which can differ from live tier 0: a vertical blue water face plus a grey
  rock face at jungle_high's right edge (f1, f2; small in f3). Before this fix, those frames had a pit there instead.
- Where the shells lack a handed-over column, the far band shows through (194's known limitation, now for more columns).
## Second case (195 badlands open sea): NOT reproduced (6 launches with that pose; the wall draws in every frame)
- That launch had 0 refusals (pool 183k / 254k), and the wall's columns lay in shell 1's hole (cam chunk 121,226, hole x 240-245 /
  z 450-455; the wall is at 244,452). So this rule would not hand them over. Likely a late load near the camera, unconfirmed.
- Rerun with `-mirrorprobe 1` when it shows again.
## Next (no follow-up queued; these need the owner)
- The churn is a pool / eviction matter (STOP rule): the GPU body pool is full at every pose, and the CPU valve evicts visible chunks.
  Option A: retry only the refused chunks that fit. Option B: keep in-view chunks out of the valve.
- Probe gap: `[mirror]` counts only on-screen chunk centres, so its ABSENT lists can miss chunks whose centre is off screen.
## For the doc
- Pit frames with refused = 0 at jungle_high: the 194 rule hands over 0 columns; the 197 rule hands over 129-139 columns, and 0 pits in
  3 / 3 launches (`Logs/overnight/197/look_f{1,2,3}.out`, `[mirror] box` lines).
- Causes, by probe: retry gap up to 214 resident chunks missing per frame; valve evictions 28-41k per run. GPU vs CPU mismatch 0, store vs
  generator mismatch 0.
- Fast suite PASS 1011 FAIL 0 SKIP 20 (+6 tests).
- Images (open `Screenshots/197/viewer.html`: Space flips before / after, arrows change pair, Z = full size):
  - `Screenshots/197/v/1_retry_before.png` / `_after.png`: jungle_high, same launch. Look at the middle. Before: a 2-chunk-deep box pit
    (grey floor, brown rim, floating crowns). After: forest.
  - `Screenshots/197/v/2_evicted_before.png` / `_after.png`: before = the first fix without the evicted rule. The pit is nearer the camera
    (valve-evicted chunks). After: forest.
  - `Screenshots/197/v/3_water_after.png`: the side effect. Look at the right edge: a vertical blue water face and a grey rock face at the
    lake shore.
