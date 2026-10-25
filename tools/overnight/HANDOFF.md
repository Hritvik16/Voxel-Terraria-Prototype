# 146-perf-milestone-30fps: DONE -- goal NOT met: warm walk p50 44.7 ms (45.7 / 43.6), 82-89% of frames > 33 ms; gap -14.7 ms p50

Decision-table row 3 (> 3 ms over): gap per stage + ranked cuts below; owner picks. No code change; `PERF_30FPS_PLAN.md` top note marks section 3's steps.

## Runs (warm steady state, AC 100% throughout, no owner pause, back to back 11:44-12:25, build 147e0e8)
- Warm-up (discarded) `MODE=stream MOTION=walk` `Logs/tg2world_20261025_114432.txt`; walk 1 `MODE=playwalk` (5 min) `Logs/tg2world_20261025_114953.txt`
- Cost split: `SKIP_BUILD=1 EXTRA="$PLAY_SETTINGS_FLAGS -renderrows 540 -shellwait 120 -costtimed 15 -costarms shells_off,far_off,sun_off,fshade_off,ups_none,hud_off,tiny_gate" ./run-cost-split.sh`
  -> `Logs/costsplit_2026-10-25_115751.txt` (same flags as task 110's baseline driver, plus play's other defaults; FULL range 2.8 / 0.4 ms over 8 runs)
- Walk 2 (repeat) `Logs/tg2world_20261025_121758.txt`. HUD + minimap (hud_off) ~0 ms at both poses.

## Before (2026-10-22, task 110) -> now
| | 10-22 | now |
|---|---|---|
| walk p50 | 41.0 / 41.0 | **45.7 / 43.6** |
| walk p95 / p99 | n/a / 98.7, 99.4 | 73.0, 74.5 / 97.0, 95.6 (FSR 3 dyn stayed at 540 rows 100%) |
| walk > 33 ms | 77.6 / 72.0% | **88.6 / 81.6%** |
| walk_near FULL p50 (p99) | 60.5-60.9 (110) | **61.0** (109) |
| walk_open FULL p50 (p99) | 42.9-43.2 (81) | **36.1** (58) |

**The walk got WORSE (+3.7 ms p50) while the steady poses held (near) or improved (open, -7 ms).** On the same leg (0.5-2.5 min means)
10-22 was 43.0-43.2 ms, now 45.7-47.4, so it is not the longer 5-min run. NOT isolated; candidates: shells hold more while walking
(`[shells] held` s2 600-1,060 vs 546-643, s3 17-27 vs 0: plan section 1 predicted moving cost rises toward steady as shells fill),
task 133's near shadows through the shells (+1.45 ms walk at sun 30 deg, +6.1 near noon), and playwalk's extra defaults (-underwater, -minimap).

## Gap per stage (steady, saved p50 ms = FULL - arm; NOT additive)
| stage (arm) | walk_near 10-22 -> now | walk_open 10-22 -> now |
|---|---|---|
| CPU + fixed floor (tiny_gate 256x144, no upscaler) | 7.1 -> 7.1 | 5.0 -> 4.4 |
| sun shadows (sun_off) | 17.1 -> **17.2** | 6.0 -> 5.5 |
| shell path vs tier 0 + far band (shells_off) | 11.4 -> 8.8 | 13.5 -> 5.5 |
| far band, also stops the shell march (far_off) | 12.5 -> 6.7 | 18.2 -> 7.9 |
| FSR 3 pass (ups_none) | 6.8 -> 5.7 | 6.8 -> 5.4 |
| far shading (fshade_off) | -4.5 -> -5.5 | +1.4 -> **-8.5** (OFF = the pre-134 fat kernel: not a cut) |
Target 30: near needs -31.0, open -6.1, walk -14.7 p50, and p95 ~74 -> 33 for the 5% line. GPU-bound (present wait 0).
The biggest stage at the slow views is now sun shadows (17 ms near); 132/134 cut the shell path roughly in half.

## Ranked remaining cuts (exact first; ms = est. unless marked measured)
1. **Shell column height bound** (137's design, `Screenshots/137/height_bound_design.txt`; 28 KB GPU table = layout addition, owner): near sun
   rays stop climbing to the shells' 1,100 m top. Ceiling = 133's measured cost: <= 6.1 near / ~1 open / ~1.5 walk. Exact.
2. **R2 ray compaction** (PERF_SHELL_MARCH_PLAN; R1 done; no layout) / 3. **R5 chunk-state bitmap** (KB buffer, owner): exact, not costed, ceiling = shells_off 8.8 / 5.5.
4. **Rows below 540** (dyn floor 432 + 30 ms target; owner, below the 540 minimum): est. ~14 near / ~8 open / walk ~8-11
   (pixel part FULL - tiny_gate 53.9 / 31.7 x 0.36 x 0.7). Largest single lever. No stills below 540 yet.
5. **Shells OFF in play** (undo 93c, owner): measured 8.8 / 5.5 steady (walk less, shells part-empty). Since 93d it draws tier 0 + far band, no tiers 1-2.
6. **STP / FSR 1 instead of FSR 3** (owner, Q38): FSR 3 pass measured 5.7 / 5.4; STP -2.7 / -3.1, FSR 1 -4.9 / -5.4 (task 51, older build).
Spent / not cuts: column sky leap (116, ~0), Medium (117, ~0), shadow clamp (118, no-op), reuse 8 (124, ~0), hole leap (137, ~0),
spike fix (123, p99 unchanged), sun off (17.2 / 5.5: no cheaper variant left), far band off (removes far terrain), far shading off (slower).
**Sum:** 4 + 1 + 6(STP) reaches walk ~30-31 p50 (est.); near views stay ~38 ms, so the 5% line is NOT reached without also 5, or rows ~360.
Screenshots looked at (`...` = `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap`): walk_near_full / walk_open_full (below) and `.../Sustained/2026-10-25_121937_tg2playwalk_20261025_121758/99_end_pose.png` (a close sandstone
slope): terrain as expected, no holes or black bands.

## Next
Owner picks from the ranked cuts (recommend 1 then 2: exact; then decide 4 with stills at 432 / 540). Unexplained +3.7 ms walk regression vs
10-22: worth one isolation arm (`-nearshadowshells 0` on playwalk) inside whichever perf task comes next, not as its own task.

## For the doc
- Walk p50 41.0 -> **44.7 ms** (45.7 / 43.6), > 33 ms 72-78% -> **82-89%**, p99 99 -> 96 ms (`Logs/tg2world_20261025_{114953,121758}.txt`; warm-up `Logs/tg2world_20261025_114432.txt`)
- Steady walk_near 60.7 -> **61.0 ms**, walk_open 43.0 -> **36.1 ms** (`Logs/costsplit_2026-10-25_115751.txt`)
- Shell path cost 11.4 / 13.5 -> 8.8 / 5.5 ms (near / open); sun shadows 17.1 -> 17.2 ms near = now the biggest stage
- Gap to 30 fps: -14.7 ms walk p50, -31 ms near; ranked cuts above (exact first)
- Image: `.../UpscalerClips/2026-10-25_115751/stills/walk_near_full.png` -- the slow view (61 ms): a canyon wall filling the frame, sky top-right.
- Image: `.../UpscalerClips/2026-10-25_115751/stills/walk_open_full.png` -- the open view (36 ms): desert hills to the horizon under half a frame of sky.
