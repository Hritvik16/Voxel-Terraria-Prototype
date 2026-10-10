# Engine phase plan

2026-10-09 · task 44 (planning only). Markdown copy of the Claude Doc
https://claude.ai/code/artifact/62d9391e-b05a-42d0-83c2-34d9bfeb04f7 -- the doc is
the version the owner comments on. **STATUS: APPROVED 2026-10-09 (Q36 "a"): Q1 a, Q2 a, Q3 compare all three,
Q4 = plug in FSR1 / STP / FSR3-port and compare (tasks 47-48, not our own upsampler), Q5-Q8 a.**

## In short

The engine phase is about 30 worker tasks (plus about 8 retries), in 23 steps. Start with three small, visible wins. Then free memory **before** adding anything that needs more. Then build the LOD shells and far edits, then fluids, then lighting. Each step is one change.

- **Biggest gap: generation speed (E3).** Today, full detail cannot keep up even at walking speed. Shells shrink the full-detail area from 128 m to about 52-70 m, but at 60 m/s generation is still short (estimate). So the owner picks a top speed (Q1) and what the screen shows when generation falls behind (Q2).
- **Memory first (E10).** The tg2 world peaked at 3,343 MB on 2026-10-09, before any player or objects. Fluid tiles (~550 MB) and oversized cascade pools (~200 MB extra) are the first things to reclaim. Shrinking the 0.1 m window after the shells land is the big saving.
- **Nothing below is approved.** Section 5 has eight multiple-choice questions. Each one starts with the recommended answer. Answer "all recommended" to approve the plan as written.

Sources: `ROADMAP.md`, `KNOWN_ISSUES.md`, `tools/overnight/BUGS.md`, task handoffs 16b-43, and `AMENDMENT_8_13_PALETTE_BRICKS.md`. Nothing new was measured for this plan. A number marked **(est.)** is my estimate, not a measurement.

## 1. Targets

No backlog item is met today. Frame-time numbers from before 2026-10-04 are **old**: they predate map v4 and steps B3-B10, so treat them as magnitude only. "Warm" means warm steady state (one discarded warm-up launch, then ABBA arms).

| # | Today (number, date, source) | Wanted | Proven by |
| --- | --- | --- | --- |
| E1 Resolution | Interactive play: 480x270 + FSR 1.0 (ROADMAP, 2026-10-08). Walk at 960x540: 31.5-33.9 ms native; 12.0-12.2 ms at 480 (old: 2026-10-01 ABBA, `Logs/tg2world_20261001_18*.txt`). TAA + checkerboard at 960: 19.6 ms (old: 2026-10-03, task 08). | 1080p output, upscaled over time from 540 or 720 rows (Q3, Q4); 60 fps. | Stills, plus the `MODE=shimmer` metric and task 07's TAA risk set. Frame rate is measured at milestone step 7 (warm `MODE=walk` ABBA). |
| E2 LOD shells | Tiers 0.1/0.2/0.4 m end at 64/128/290 m (`LODConfig.cs:32,64`). But voxels stop at the residency edge, 128-141 m (task 16b), and the vertical band is 89.6 m. A 2.5D far band (0.8-12.8 m columns) runs to ~6.5 km. | Nested cube shells in every direction, each starting where its voxel is 1 px. At 540 rows (60° FOV), a voxel of V metres is 1 px at 516·V m: 0.1 m at 52 m, 0.4 m at 206 m, 1.6 m at 825 m, 12.8 m at 6.6 km. That is 8 shells. At 720 rows every distance is 1.33x farther. | A new "largest voxel on screen" pose metric (pass: ≤ 1 px). Cascade/clipmap validator tests. Pose contact sheet. |
| E3 Streaming | Warm capacity 23-37 chunks/s at 4 workers (task 28, 2026-10-04, v3 walk). Task 30 (2026-10-08) shares the column pass: 1.76x faster in an isolated EditMode check, never measured in-stream. Worst case at 60 m/s with today's 128 m detail radius needs 2,285/s, and the prefetch margin is 0 s (task 16b). | Full detail keeps up at the top speed (Q1) with ≥ 0.5 s margin. When it can't, the Q2 policy applies. Level flight at 60 m/s after shells needs ~160 full-cost chunks/s (est.). | `MODE=stream` section 7, warm, at milestone step 16. |
| E4 Frame time | Walk at 960x540 ~32 ms (old: 2026-10-01): shadows ~10 ms, fluid ~2.5 ms, ~5 ms fixed, ~27 ms scales with pixels. While the camera moves, 8-10% of frames take over 33 ms (2026-09-26, not fixed). | p50 ≤ 16.7 ms at the E1 setting. Proposed: ≤ 1% of frames over 33 ms. | Warm `MODE=walk` ABBA, plus `run-acceptance-rig.sh` Gate C "frame total p50/p99" (milestones 7 and 23). |
| E5 Far edits | An edit outside loaded chunks is refused (ROADMAP). | The edit is stored as an operation, applied when the area generates, and the far band/shells update at once. | EditMode tests: op round-trip, apply-on-generate gives the same result as a near edit, save/load. One far-crater screenshot. |
| E6 Fluid tick | One tick takes ~7 frames: 7.25 frames measured (`FLUID_TICK_DIAGNOSIS.md`, 2026-09-24). Fluid speed follows frame rate. | A fixed tick rate, independent of frame rate. | A test showing the same ticks/s at two frame rates. Fluid rigs green. |
| E7 Fluid range | Fluid simulates only within 128 m. The coast wants 3,845 tiles against a pool of 1,024 (0.5 MiB each), and the sea beyond ~61 m sleeps (task 39, 2026-10-09). | Active fluid keeps simulating wherever it is, at a lower rate far away. Far rivers use render-only flow. | `MODE=fluidprobe` at a far pose, plus `MODE=poolprobe` at the coast. |
| E8 Levelling | Whole-cell CA: a pour can leave a mound, and cells sleep after 8 still ticks (ROADMAP). | Water always settles flat. | EditMode pour tests: a basin's surface ends within 1 cell of flat. Lake-rest tests. |
| E9 Lighting | 32 point lights (the furthest are dropped), a few metres of torch reach, no far lights, no bounce. No point-light occlusion, so torches light through walls (`Raymarch.compute:1856`; BUGS #4). | At least Terraria in 3D: unlimited coloured lights, lights seen across the island at night, dark caves, some GI. | `run-phase7-rig.sh` with a new torch-behind-wall gate. `run-phase7-lighting-ab.sh` for frame rate (quoted per 8.12 §3.2-3.3). Night poses. |
| E10 Memory | Peak 3,343 MB, map v4 (`Logs/tg2world_20261009_210348_mem.txt`, 2026-10-09). Last per-owner ledger: 3,259 MB (old: 2026-10-01, 1.0 M pool). | Proposed: engine ≤ 2,500 MB, leaving ≥ 500 MB for the game under the 3 GB cap. | `-memledger` plus the peak footprint over poses, walk and rig. |

**Targets that conflict:**

- E9 (more rays per pixel) and E2 (more shells to march) both fight E4 (16.7 ms). Today's 960x540 path is already ~2x over budget without them.
- E1 at 720 rows costs ~1.78x the traced pixels. It also pushes every E2 shell 1.33x farther, so shells cost ~1.78x more memory and generation (est.).
- E2, E7 and E9 all add memory while E10 is already over the cap. Section 2 reclaims memory first.
- E7 needs 0.1 m CPU chunks wherever fluid moves, but E2's coarse shells hold no CPU copy. Answer: step 14 keeps a small resident patch around awake fluid.
- E3 and the owner's "never see a voxel coarser than the screen" cannot both hold at 60 m/s with today's generator (Q1, Q2).

## 2. Memory budget

Proposed: the engine fits in ~2,000 MB (est.), leaving ≥ 500 MB for the game (player, objects, NPCs, audio, UI) and ~500 MB of margin under 3,000 MB. The "today" column is the only per-owner ledger on file (old: 2026-10-01, `Logs/tg2world_20261001_162217.txt`, peak 3,259 MB). It was taken with the 1.0 M-slot brick pool, which is now 750k.

| Owner | Today (MB) | Target (MB, est.) | How |
| --- | --- | --- | --- |
| CPU 0.1 m brick pool | 488 (~384 at today's 750k cap, derived) | 170 | Shrink the 0.1 m window to the detail radius plus a margin (~90 m, not 128 m): about half the bricks (step 14). |
| GPU mirror of it + CPU clipmap staging | 635 + 148 | 300 | Follows the CPU pool (the mirror is a verbatim copy, §3.7). |
| Cascade tiers 1-2 (GPU + native + managed) | 221 + 211 + 46 | 0 | Replaced by the shells. Until then, caps come down to 2x the measured need (step 6, about -200 MB, 8.13 §8). |
| New LOD shells (E2) | 0 | 250 | 8 shells, generated on the CPU and kept on the GPU only (ROADMAP estimate 150-250 MB). |
| Far band | 27 + 12 | 40 | Unchanged. It may retire if the shells reach the horizon. |
| Fluid (GPU tiles + managed) | 549 + 43 | 250 | Tiles only for awake fluid; resting sea and lakes are plain terrain (step 5). Far patches for E7 come out of this budget. |
| Light grid (E9) | 0 | 100 | New. Size set in the light-grid amendment (step 20). |
| Managed heap | 359 | 350 | Unchanged. |
| Unity runtime, textures, code, unattributed | ~500 | 500 | Unchanged. |
| **Engine total** | **3,259** | **~1,960** | |

**Reclaim first (ROADMAP tension 2).** Steps 5 and 6 come before anything that adds memory.

1. **Fluid tiles for awake fluid only** (step 5): about -250 to -350 MB (est.). Also fixes BUGS #3, the coast filling the pool.
2. **Cascade caps from a measured `PeakUsed`** (step 6): about -200 MB (8.13 §8; its 3.5-5x caps were a first sizing).
3. **Shells replace the cascades** (step 11): -478 MB of cascades, +250 MB of shells (est.).
4. **Shrink the 0.1 m window** (step 14): about -400 to -600 MB, CPU and GPU together (est.). This needs far edits (E5) first, or edits beyond the window would be refused.
5. **Palette bricks (8.13)**: the owner deferred these (Q24 (c)). At today's window they cut tier-0 bricks from 625 to 119 MB at need, CPU and GPU together. After step 14 the saving is roughly half that (est.). Decide with the ledger after step 14 (Q8).

After steps 5-6 the world should sit near 2,800-2,900 MB (est.): under the cap, but with little room until steps 11 and 14.

## 3. Order

The order is: three quick wins, then reclaim memory, check frame rate once, shells and far edits, fluids, lighting, and a final milestone. Each row is one task with one change. S means one session, M one to two, L three or more (split into a, b, c). Rows marked **[measure]** are performance tasks. They run only because a decision depends on their result, and that decision is written in the row.

| # | Step (one change) | Acceptance | Size | Needs |
| --- | --- | --- | --- | --- |
| 1 | **One compiled world-map generator path.** Burst in both the Editor and the player, compiled synchronously, so EditMode map tests, `PlayMetricsReport` and review renders show the world the IL2CPP player builds (Q26 (a)). The first commit merges `gemini/lava-torch-light` (bd60a30: lava ignores torch light). | A test shows the Editor's seed-42 v4 `WorldMap.Height` FNV equals the player's (`B387F054A29997D4`, task 32c). `SUITE=all` is green, with re-pinned hashes listed in the handoff. Metrics report and review renders regenerated. | M | -- |
| 2 | **Torch shadow rays (E9a, BUGS #4).** The nearest N point lights per pixel trace an occlusion ray, and the rest are scaled by the traced share (the SVO's form). Emissive bleed follows Q5. | New `run-phase7-rig.sh` gate: a torch behind a wall leaves the far side dark. Screenshot pair, before and after. No frame-time claim. | S-M | 1 |
| 3 | **Upsample over time (E1).** Render at the Q3 rows and resolve to 1080p output by extending our TAA (Q4). TAA becomes on by default in play. Checkerboard stays an option. | Stills, the `MODE=shimmer` metric, and task 07's TAA risk set (edits, fluid, turns, seams) all pass. | M | Q3, Q4 |
| 4 | **Memory ledger prints `PeakUsed`/`InUse` per pool** (the parked task 31 / 8.13 T4 ledger half), plus one ledger run over poses and the walk. These are memory counts, not timing. | The ledger table goes into the handoff. It sets the caps in steps 5 and 6. | S | -- |
| 5 | **Fluid tiles for awake fluid only** (BUGS #3, "pool full"). Resting sea, lakes and supported sand cost nothing until disturbed. The wake queue no longer drops a placed box's wakes. | `MODE=poolprobe` at the coast: no refused tiles, and placed water flows. `run-fluid-tiled.sh` passes. Ledger before and after. | M | 4 |
| 6 | **Cascade caps from the measured `PeakUsed`** (8.13 T6). No format change. | No cascade exhaustion over poses, walk and rig. Ledger shows about -200 MB. | S | 4 |
| 7 | **[measure] Frame-time milestone 1.** Warm `MODE=walk` ABBA at the Q3 setting, with steps 2-3 in. | Decides: if p50 ≤ 16.7 ms, go on to step 9. If not, step 8 cuts shadow cost first (~10 ms of 32 in the old 2026-10-01 split; Phase 7 §10's shadow-rung flag). | M | 2, 3 |
| 8 | **Shadow cost or shadow-dots fix** (BUGS #1, `later/38-bug-shadow-dots.md`). If step 7 is red, this becomes the one shadow-cost change, and the dots move to the next task. | Dots gone at the low-sun poses (screenshots). If it is a cost change, one warm A/B. | S-M | 7 |
| 9 | **Amendment 8.14 draft: LOD shells and an edit-op log** (planning only, STOP_FOR_OWNER). Shell sizes come from Q3. Shells are generated on the CPU with Burst and uploaded. The far hand-over is no longer tied to residency. It also covers the window shrink, an op log beside D.1, and fluid patches. | Owner approves. | M | Q3 |
| 10 | **Coarse-first generation.** Generate a chunk directly at 0.2-12.8 m voxels from the world map, on the one Burst path. | Solid/air agreement with the downsampled 0.1 m chunk is ≥ a threshold set in step 9. Renders side by side. | M | 1, 9 |
| 11 | **Shells: storage, upload and raymarch** in every direction, each starting at 1 px. They replace cascade tiers 1-2. The far band stays beyond the last shell. 11a storage and upload; 11b raymarch and seams, which also fixes BUGS #2 (ledge crescent, `later/40-...`); 11c vertical. | New pose metric: largest voxel ≤ 1 px. Validator tests green. Pose contact sheet looked at. | L | 10 |
| 12 | **Hand-over decoupled from residency, with a prefetch margin.** Retire `-detailspeed` (tension 5). | Section 7 latency line: margin > 0 by construction. Poses unchanged at rest. | M | 11 |
| 13 | **Far edits as operations (E5).** An edit anywhere is stored as an operation in the save, applied when its area generates, and redrawn in the shells and far band at once. 13a store and apply; 13b far redraw. | E5 tests (section 1). Old saves load unchanged. | L | 9, 11 |
| 14 | **Shrink the 0.1 m window to the detail radius plus a margin.** Chunks around awake fluid stay resident as a small patch (tension 4). | Ledger shows about -400 to -600 MB (est.). Acceptance-rig gates C and D green. Edits beyond the window still work (via 13). | M | 12, 13, 5 |
| 15 | **Generation speed:** whole bricks skip per-voxel work when the column heights prove them all stone or all air. | A byte-identical test, and an isolated EditMode timing check (as task 30 did). | M | 10 |
| 16 | **[measure] Streaming milestone 2.** Warm `MODE=stream`, walk and fly at the Q1 top speed. | Decides: if met, streaming is done. If short, the next step is the Q2 policy: either the GPU-generation amendment or a game-side speed rule. | M | 12, 14, 15 |
| 17 | **Fixed-rate fluid tick (E6)**, using one of the options in `FLUID_TICK_DIAGNOSIS.md`. | Same ticks/s at two frame rates (test). Fluid rigs green. | M-L | 5 |
| 18 | **Levelling rule (E8):** no sleep while lower ground is near. Afterwards, check BUGS #5 (river steps into a lake). If it remains, it is a terrain fix, and that is the owner's call. | Pour tests settle flat. Lake-rest tests green. | M | 17 |
| 19 | **Far fluid (E7):** active fluid beyond the window keeps a resident patch and ticks at a lower rate. Far rivers get render-only flow. 19a patches; 19b render-only flow. | `MODE=fluidprobe` at a far pose shows movement. Ledger stays within the fluid budget. | L | 14, 17 |
| 20 | **Light propagation grid (E9b).** Starts with a short amendment (Phase 7 lighting). Unlimited coloured lights, dark caves, light around corners. 20a amendment and grid; 20b injection and sampling; 20c sky light. | Phase 7 rig correctness, and night and cave screenshots. Frame rate via `run-phase7-lighting-ab.sh` (reduced arms). | L | 6, 14, Q5 |
| 21 | **Far lights (E9c):** lights visible across the island at night, drawn in the shells and far band. | Night far-view screenshots. | M | 11, 20 |
| 22 | **Some GI (E9d):** one bounce from the light grid, plus sky. | Screenshots, plus the A/B frame rate. | M-L | 20 |
| 23 | **[measure] Engine-phase milestone.** Every rig warm, `SUITE=all`, the 3 GB peak, and screenshots. | Every E1-E10 row in section 1 is checked. | M | all |

The first three steps are Q6's recommendation. Steps 17-18 (fluids) do not depend on the shells, so they can move ahead if the owner wants fluids sooner.

## 4. Amendments needed

None of these is approved. Rows 1-3 go into one draft, 8.14 (step 9). The others are written by the step that needs them, and that step stops for the owner before any code.

| # | What changes | Why | Step |
| --- | --- | --- | --- |
| 1 | **8.9 LOD bounds become shells.** 3 tiers (64/128/290 m) become about 8 cube shells, each starting at 1 px for the Q3 rows, including vertically. This touches `LODConfig`, the ring sizes (still powers of two) and the 8.10 cascade tiers. It also replaces 8.11 (a draft, never adopted). | E2. Its distances depend on E1 (tension 6). | 9 |
| 2 | **CPU as sole terrain writer: kept.** Shells are generated on the CPU (Burst) and uploaded; the GPU only stores them. The GPU never writes terrain. If step 16 is still short, a later amendment could let the GPU build render-only shells from seed plus op log. The CPU would still hold the truth, but GPU and Burst floats would then need to agree bit-exactly. | Tension 3. Keeping it costs generation speed (Q2). | 9 (16) |
| 3 | **An edit-op log beside D.1.** A new save section of operations ("sphere r at x,y,z"), applied on generation. D.1 deltas keep their frozen format. The precedence rule: ops first, then D.1 for chunks edited while resident. | E5 and the ROADMAP note. A redesign next to a frozen API. | 9 |
| 4 | **Residency rules for fluid patches.** Chunks outside the detail window stay resident around awake fluid. Check whether this fits the frozen §4.4 state-transition table as it is. If it needs a new state, that is a redesign and gets flagged. | E7 and tension 4. | 14, 19 |
| 5 | **Fluid tick contract.** Phase 5's decision 11 (a 6-frame CA tick) becomes a fixed rate. The 7.2 tiled active set holds awake fluid only. | E6, E7, BUGS #3. | 5, 17 |
| 6 | **A Phase 7 lighting addition:** a light grid next to the 32 point lights, plus occlusion rays. 8.12's gate is re-run at the new internal resolution. | E9. | 2, 20 |
| 7 | **Resolution and budget:** the design rows become the Q3 answer (8.9 used 540). The frame budget is 16.7 ms at that setting. | E1 and E4. | 3 |

Not amendments: `-detailspeed` is retired (it is code only). The one-generator-path change re-pins EditMode map hashes, which is a test change, not a contract change.

## 5. Questions for the owner

Option (a) is the recommended answer in every question. A reply of "all (a)" approves the plan as written.

1. **Q1. Design top speed: one number.** The only number on record is 60 m/s, the brief's late-game flight. This is not about how movement works.
    1. (a) **60 m/s.** It is the number on record. Cost: level flight needs ~3x today's generation even after shells (est.), so Q2 decides what is shown meanwhile.
    2. (b) 30 m/s. Half the generation requirement.
    3. (c) 15 m/s. Likely met by shells plus steps 10 and 15 (est.). Fast travel would then need another mechanism, such as a teleport.
2. **Q2. When generation falls behind at top speed, what does the screen show?**
    1. (a) **The next coarser shell, until full detail arrives.** The ground looks blockier for under ~1 s at most. The 1-px rule holds at walk and run speeds. Cheapest, and needs no amendment.
    2. (b) Never fall behind: later, the game limits speed near the ground. This is a game-design rule, decided after this phase.
    3. (c) Make generation ~10x faster by having the GPU build the render shells. Cost: L, plus amendment row 2 and bit-exact GPU/Burst noise. Do this only if step 16 shows a gap that (a) can't hide.
3. **Q3. E1 internal resolution.**
    1. (a) **540 rows fixed** (960x540), upsampled to 1080p. Shells are sized for 540. It is the closest to 16.7 ms on file (19.6 ms with TAA + checkerboard, old: 2026-10-03). 720 can be revisited at the milestone.
    2. (b) Dynamic 540-720. Shells must be sized for 720, which costs ~1.78x the shell memory and generation (est.). Frame-time headroom to use it is unlikely.
    3. (c) 720 fixed. Needs about a 3x frame-time cut from today (est.).
4. **Q4. Upscaler.**
    1. (a) **Extend our own TAA into TAA-upsampling.** We already own the jitter, reprojection and history (task 06), and it works with a compute raymarch.
    2. (b) URP STP. Built in, but it expects URP's own depth and motion vectors, which our compute raymarch doesn't produce. Integration risk.
    3. (c) MetalFX. Needs a native plugin, and it is Mac only.
5. **Q5. Torch through walls and light propagation (E9).**
    1. (a) **Both, in this order:** shadow rays for the nearest few torches first (step 2, like the SVO; fixes the leak soon), then a light grid (step 20) for unlimited coloured light, dark caves and light around corners. Emissive bleed ON by default, as in the SVO. Cost: a few rays per lit pixel, plus ~100 MB for the grid (est.).
    2. (b) Shadow rays only. Cheapest, but lights stay capped at 32, with no light around corners.
    3. (c) Light grid only. Softer, blockier light with no sharp torch shadows, and the leak stays until step 20.
6. **Q6. The first 3 tasks.**
    1. (a) **1 generator path (+ lava merge), 2 torch shadows, 3 TAA-upsampling.** These are small, visible wins, and memory reclaim (steps 4-6) follows straight after.
    2. (b) Memory first: 1 generator path, then fluid tiles, then cascade caps.
    3. (c) Shells first: 1 generator path, then the 8.14 draft, then coarse-first generation.
7. **Q7. Coast fills the fluid pool (BUGS #3, Q31 part 1).**
    1. (a) **Only awake fluid takes tiles.** Resting sea, lakes and supported beach sand are plain terrain until disturbed. Saves ~250-350 MB (est.).
    2. (b) A 4x bigger pool. About +1.5 GB, which breaks the cap.
    3. (c) A smaller simulation radius (e.g. 64 m). Moving water then freezes closer to the player.
8. **Q8. Palette bricks (8.13, deferred at Q24 (c)).**
    1. (a) **Keep them deferred.** Decide with the ledger after step 14, when the 0.1 m window is smaller and the saving is about half (est.).
    2. (b) Do them now: ~6 tasks (8.13 T1-T6), saving ~500 MB CPU + GPU at today's window (8.13 §8, at need).
    3. (c) Drop them.

## 6. Usage estimate

Plan for about **29 worker tasks plus about 8 retries or follow-ups, so ~37 sessions**, plus supervisor turns and three owner stops (this plan, 8.14 at step 9, and the light-grid amendment at step 20). Answering Q8 (b) adds about 6 more.

- **Pace:** the terrain phase ran about 35 queue tasks between 2026-10-03 and 2026-10-09. Engine tasks are larger (five L steps), so expect roughly 2-3 weeks of queue time, longer when the weekly limit binds.
- **Heaviest in usage:** steps 11, 13, 19 and 20, which split into 10 sessions between them.
- **Long in wall time but light in tokens:** the three **[measure]** steps (7, 16, 23). Each one is a warm-up launch plus ABBA arms.
- **Natural pause points**, if the week runs short: after step 8 (quick wins plus memory), after step 16 (shells and streaming), and after step 19 (fluids).
