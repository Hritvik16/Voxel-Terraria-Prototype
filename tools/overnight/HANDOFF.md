# 99-shell-sky-ray-exit: DONE. Exact sky-ray exit, ON by default; p50 -4.7 ms (walk_near) / -2.6 ms (walk_open). Rule 1 applied.

## What changed (commit `2b63964`, branch engine-phase)
- `Raymarch.compute`: `ShellMarch(..., int skyY)`. A ray with step.y > 0 and voxel.y >= skyY ends as `SHELL_END_SKY` (no hit) at the top of the iteration.
  - `ShellSkyY(primary)` = `_ShellTopY`. For a primary ray with the far band on, it is max(that, ceil(far max height) + 2).
  - Why this is exact: on a rising ray voxel.y never falls (leaps only add step x counts >= 0), so the ray reads only air from there on.
  - The far march after a shell no-hit starts at the shells' end t. `FarMarch`'s first test (D.y >= 0 and p.y >= maxH) passes at the exit's t and at every later t, so it returns the same empty result from the early t as from the late one.
  - Shadow rays use the shells' top alone (only HIT matters for them).
  - Diagnostics: ShadowStats 62-64 (sky rays, their iterations, exits). Debug view 12 shows sky exits in blue.
- **Bound choice: ONE value for all shells**, not per shell or per x/z region. The exit stops the whole rest of the ray, so the bound has to cover everything still ahead, not just the current column. In play the far band's max height (1,100.0 m) decides anyway; the shells' top is 1,100.8 m.
- `ShellStore.SolidTopY` + `ChunkSolidTopY`: the top of the highest brick that is not uniform air (dense bricks and fluids count). It is raised in `Store` before anything uploads (fresh stores and `replace: true` redraws; ops and edit summaries come through Store). It is never lowered (evictions, aborts, lower redraws). A refused chunk does not raise it. `ShellStreamer.SolidTopY` = the max over shells.
- `RaymarchFeature.ShellSkyExit`: `-shellskyexit 0|1`, **default ON** (`0` = the old march). Sets `_ShellTopY` / `_ShellSkyExit`. STATS_LEN 62 -> 65.
- CPU twin: `ShellRayReference.March(..., skyY)`, `END_SKY`, `PrimarySkyY`.
- Cost split: arms `skyexit_on` / `skyexit_off`, and a `[shell ..] sky rays` counter line. The far line's "cap" is relabelled "cap / sky exit" (SKY ends fall into that bucket). Docs: KNOWN_ISSUES "SHELLS COST", CLAUDE.md (Task 99 line, index line).
- New `ShellSkyExitTests` (5), on real ShellStores (shells 1-4) holding a synthetic world: ground, a near tower capped with water, a taller far peak.
  - Bound >= every stored solid/fluid cell, at most one brick above it.
  - 2,400 random rays x 3 skyY shapes (far off / far above / far below the shells' top) + their shadows: hits identical field for field; exits are no-hits ending later without the exit, and above the far max height.
  - Rays grazing just under / at / over the bound (level, 1e-7, falling).
  - Store raises / never lowers (uniform, dense-with-one-water-voxel redraw, eviction, air chunk, refused chunk, uniform solid chunk).
  - Mutation (bound one brick low): 218 of 600 hits lost, so the test can fail.

## Evidence (AC power, charging 41 -> 44%; no owner pause; warm steady state)
- Warm-up (discarded): `Logs/tg2world_20261017_101838.txt`. Then ONE launch, `Logs/costsplit_2026-10-17_102453.txt` (copy in `Screenshots/99/`).
  - Order per pose: F X X F, F = exit OFF, X = `skyexit_on`.
  - Flags: `-shells 1 -shellworkers 3 -shellwait 120 -farshade 1 -seethrough 1 -riverlook 1`, fsr3 540, quality.
  - Driver: `Screenshots/99/99-driver.sh`.

| pose | arm | p50 ms (runs) | p99 ms | iters / sky ray | sky rays ended by exit | px changed (raw) |
|---|---|---|---|---|---|---|
| walk_near | OFF | 63.6 (63.2, 64.1) | 111.4 | 94.3 | 0 | - |
| walk_near | ON | 58.9 (59.0, 58.6) | 105.6 | 82.2 | 100% | **0** |
| walk_open | OFF | 46.6 (46.3, 46.9) | 88.0 | 39.7 | 0 | - |
| walk_open | ON | 44.0 (44.2, 43.9) | 80.6 | 29.9 | 89% | **0** |

- Saved p50: **+4.7 ms walk_near (noise 0.9), +2.6 ms walk_open (noise 0.6)**. Shell iterations per pixel: 5.50 -> 4.79 and 30.34 -> 24.88. Hit rays are unchanged (51.8 and 105.9 iterations per ray).
- Rule applied: saves >= 3 ms at walk_near and is faster at walk_open, and it is exact. So **default ON**.
- Identity checks:
  - No-upscaler `_raw` stills, ON vs OFF: 0 px at both poses. raw vs raw2 (10 frames apart): 0 px.
  - The FSR 3 `_clean` / `_sunvis` stills differ (21-48% / 0.02% of px). That is FSR 3's temporal jitter, which differs between any two frames. The raw stills (shadows included) are the identity check.
- Fill at measure time: walk_near s1-s4 complete, s5 218/481, s6-s7 0 stored (missing 461); walk_open all but 36 stored. Tests: filter run ShellSkyExit + ShellMarchCache + ShellAirMask + ShellHandOver all pass. **Fast suite PASS 842 FAIL 0 SKIP 14** (`Logs/overnight/99-fast.out`). Player build: 0 shader errors.

## Left / next
- 93b's re-run (queue, `Requires: 99`) runs next, as the task file says.
- Rule 1 needs no note about where the rest of the cost is, but for 93b:
  - walk_near's sky rays still take 82 iterations climbing ~145 m from the eye (~956 m) to the bound (1,100 m, the world ceiling).
  - walk_open's cost left is the HIT rays (106 iterations each, 6% of px) and the air-chunk steps (47% of iterations).

## For the doc
Task 99: rising shell rays now stop once they are above everything the shells and the far band hold. The image is exactly the same, and the frame is faster.
- Frame p50, exit OFF -> ON: **63.6 -> 58.9 ms** (walk_near, noise 0.9) and **46.6 -> 44.0 ms** (walk_open, noise 0.6). p99 111 -> 106 and 88 -> 81 ms (`Logs/costsplit_2026-10-17_102453.txt`).
- Shell iterations per sky ray: **94.3 -> 82.2** (walk_near) and **39.7 -> 29.9** (walk_open).
- Image: **0 px** differ (no-upscaler stills, both poses).
- Images:
  - `Screenshots/99/sheet_99.jpg`: 4 rows (walk_near OFF, ON, walk_open OFF, ON). Columns: no-upscaler beauty, shell-march step heat, far-start view.
    - Heat colours: blue <= 24 steps, green 25-48, yellow/red more, black = tier 0.
    - Compare rows 3 and 4, middle: walk_open's mid-sky band goes from green to blue.
    - Compare rows 3 and 4, right: the sky that started the far band "ABSENT" (yellow) now starts it at the sky exit (light blue). Only a yellow band stays along the horizon.
    - In walk_near (rows 1-2) only the top-right sky patch marches the shells. It stays mostly red/yellow: those rays climb ~145 m before the exit.
    - The beauty column is the same in ON and OFF.
  - `Screenshots/99/walk_near_clean_off_on.jpg`: walk_near at noon with the sun behind the camera, FSR 3, exit OFF left / ON right. The same canyon wall and sky; nothing to tell them apart.
