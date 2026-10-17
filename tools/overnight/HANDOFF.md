STOP_FOR_OWNER -- task 93b5: the EXACT air-brick mask works (0 px differ, shell iterations -55% / -29%) but makes frames 1-3 ms
SLOWER, so I shipped it OFF (`-shellmask 1`). A mask that saves time cannot be bit-exact the way the task asked (stop rule). The fast
version is one closed-form leap per empty brick group: -1.5 / -2.0 ms (walk_near / walk_open), 0 px differ in both stills, but its t
rounds differently at the group's exit. Question: (a) ship that one-leap version as is (CPU twin + a test with a t tolerance), ~2 ms;
(b) RECOMMENDED: make it exact by construction: every shell-march leap computes t as (exit plane - start) x tDelta, so one group
leap ends exactly where the last brick leap would. Same ~2 ms; mask on = off byte for byte; shell frames vs today may differ by an
LSB (0 px expected); (c) leave the mask off. Whatever is picked, the shells' +12-15 ms is mostly NOT air-brick steps (see below).
93b (Requires 93b5) should wait for this answer.

# 93b5 shell air-brick mask: built + exact + tested; measured SLOWER as an exact loop, so OFF by default

## What changed (commit 70ddcf5, engine-phase)
- `ShellStore`: a 64-bit mask per page (bit g = 4x4x4-brick group g has a handle that is not uniform air; `GroupMask` / `GroupOf`,
  shared with the tests). It is built from the page staging in `Store` (generated chunks, op/summary redraws, fill output: all of them go through Store)
  and uploaded with the page, before its directory entry. It sits in the TAIL of the shared page buffer (`MaskBase`), not in a buffer of its own,
  because one more binding would bring the kernel nearer Metal's 31-buffer limit. Size: High 4,171 pages x 8 B = **33 KB**, Ultra **62 KB**.
  `ShellBytes.MASK`, layout/ledger bytes, budget +8 B per page (ShellStorageTests' exact-budget numbers updated).
- `Raymarch.compute` `ShellMarch`: for an air brick of a paged chunk whose group bit is 0, an inner loop takes the SAME one-brick
  `LeapSpanReseedClosedForm` calls with no reads. It stops at the group edge, or at any brick where the tier-0 band, the distance shell or the clamp
  would act. This is exact by construction: one closed-form leap over the group would round t differently, because tExit = tMax + (n-1) x tDelta depends on the path.
  It runs only with `_ShellAirMask` (`RaymarchFeature.ShellAirMask`, `-shellmask 1`, default OFF). Diagnostics in the far-shading kernel:
  `_DebugShellSkip` bit 3 (`shellmask_off`) and bit 4 (`shellgroup`: one non-exact leap per group, only where the whole group is in
  shell k's band and outside tier 0's), and ShadowStats 60-61 (groups / bricks crossed; STATS_LEN 62).
- CPU twin `ShellRayReference.March(useMask)` + `IMaskView` (GroupMask, tier-0 layers). `ShellMarchCacheTests.RaySet` extracted for reuse.
- New `ShellAirMaskTests` (3). KNOWN_ISSUES "SHELLS COST" (new AIR-BRICK MASK paragraph) + its CLAUDE.md index line; CLAUDE.md Task 93b5 line.

## Evidence (AC power, warm steady state)
- Tests: filter 17/17 (ShellAirMask, ShellMarchCache, ShellHandOver, ShellOrder, ShellStorage). (1) Each of 12 real tg2 chunks
  (shells 1-3, generated / filled, each also op-redrawn via `replace: true`) has an uploaded mask equal to a scan of its uploaded page; every clear group is air
  voxel for voxel (352 empty / 416 non-empty groups). (2) 2,376 primary + 687 shadow rays: mask on = off in every field, 0 capped; iterations 69%.
  (3) Mutation (bits cleared on non-air groups): 24 rays differ, caught. **Fast suite PASS 814 FAIL 0 SKIP 14** (`Logs/overnight/93b5-fast.out`).
- Run 1 (`Logs/costsplit_2026-10-17_012419.txt`) is INVALID: the owner paused at 01:24 during it, and only walk_near ran.
- Run 2: warm-up `Logs/tg2world_20261017_022453.txt`, then `Logs/costsplit_2026-10-17_023103.txt` (F X X F, X = shellmask_off, mask on in FULL):
  shell iterations/pixel **5.53 -> 2.49** (walk_near), **30.33 -> 21.68** (walk_open). Primary sky ray at walk_near: 94.9 -> 42.7 iterations.
  p50 off -> on: **65.0 -> 67.8** / **48.0 -> 48.6 ms** (noise 0.1); p99 186 -> 216 / 81 -> 101.
- Run 3: warm-up `Logs/tg2world_20261017_024643.txt`, then `Logs/costsplit_2026-10-17_025258.txt` (+ shellgroup arm). p50 on / off / shellgroup:
  walk_near **66.7 / 65.4 / 63.9**, walk_open **49.3 / 48.0 / 46.0** ms (noise 1.2-1.7 / 1.2-1.4).
  Why the exact loop does not pay: it still does every brick leap (1.56 M / 4.31 M a frame), and SIMD lanes split between two loops.
  Even skipping those leaps entirely (shellgroup) saves only ~2 ms of the shells' +12-15, so most of the march's cost is NOT air-brick steps
  (left: tier-0 leaps 23%, uniform-air-chunk leaps 29-48%, the per-iteration distance/tier-0 checks).
- Identity: no-upscaler stills `*_raw.png`: on vs off **0 px differ** at both poses (runs 2 and 3), shellgroup vs off 0 px, raw vs raw2 0 px.

## Not done
- No decision taken between (a)/(b)/(c). The mask data stays built and uploaded (33 KB) either way.
- 93b's re-run waits (Requires 93b5).

## For the doc
Task 93b5: an air-brick mask for shell chunks. It halves the march's steps but does not make frames faster.
- Shell iterations per pixel: **5.53 -> 2.49** (walk_near, -55%), **30.33 -> 21.68** (walk_open, -29%), the estimate was -40-50 / -25-30% (`Logs/costsplit_2026-10-17_023103.txt`).
- Frame p50, exact mask off -> on: **65.0 -> 67.8** / **48.0 -> 48.6 ms** (run 2), **65.4 -> 66.7** / **48.0 -> 49.3** (run 3): slower, so it ships OFF.
- One non-exact leap per empty group (diagnostic): **65.4 -> 63.9** / **48.0 -> 46.0 ms**, 0 px differ (`Logs/costsplit_2026-10-17_025258.txt`).
- GPU memory: +33 KB (High) / +62 KB (Ultra). Image: byte-identical with the mask on or off.
Images:
- `Screenshots/93b5/walk_open_heat_off_on.jpg`: shell-march step heat, mask OFF left / ON right. Look at the distant ridges on the right: they are red (96+ steps) with the
  mask off and mostly yellow/green (25-96) with it on. Only the ridge tops stay red. The sky is blue/green in both. Black = tier 0 drew it.
- `Screenshots/93b5/walk_near_heat_off_on.jpg`: the same at walk_near. The only shell-marched patch is the top-right sky, red with the mask off and green/yellow with it on.
- `Screenshots/93b5/walk_open_beauty.jpg`: walk_open at noon, no upscaler (desert ridges, sea and snow at left). It is identical with the mask on or off. There is nothing to see by design.
