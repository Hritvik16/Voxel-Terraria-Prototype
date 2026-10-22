# 110-perf-frame-budget-split: DONE (items 1-2 complete; item 3 only partly possible without a code change, see below)

Verdict: **GPU-bound.** CPU floor is 5-7 ms (`tiny_gate`) against frames of 43-61 ms; present wait is 0.0 everywhere.
Code unchanged, no commits except this file. Setting: shells ON (`-shells 1 -shellworkers 3`), FSR3 540 rows, quality preset, far band + far shadows,
`-farshade 1 -seethrough 1 -riverlook 1`, noon, AC power for every launch (walk ON 2 flipped to battery in its last seconds; its numbers match walk ON 1).
Driver: `Logs/overnight/110-driver.sh` (+ `.out`). **Warm-up log: `Logs/tg2world_20261022_121916.txt`.** Warm steady state: warm-up 12:19-12:24, then ON1 12:25-13:01, ON2 13:03-13:39, walks 13:39-13:49, no gap > 2 min.

## Table 1: steady split, saved ms vs FULL (positive = the arm's thing costs this), launch 1 / launch 2
Logs: `Logs/costsplit_2026-10-22_122517.txt` (L1), `Logs/costsplit_2026-10-22_130301.txt` (L2). 15 s timed, ABBA with FULL between.
FULL p50: walk_near 60.5 / 60.9 ms (p99 110.6 / 109.6); walk_open 43.2 / 42.9 ms (p99 80.9 / 83.9).
| arm (what is OFF) | walk_near L1 / L2 | walk_open L1 / L2 |
|---|---|---|
| shells_off (no shells) | +13.0 / +13.8 | +13.7 / +13.4 |
| far_off (far band; also cuts the shell march) | +14.9 / +15.5 | +18.2 / +17.2 |
| shellmarch_off (shell primary march) | +12.8 / +12.8 | +18.7 / +18.8 |
| shellshadow_off | +1.3 / +0.7 | +1.4 / +1.2 |
| skyexit_off (sky-ray exit off, i.e. task 99's saving) | -3.5 / -1.6 | -3.3 / -3.7 |
| sun_off (sun shadows) | +16.6 / +14.8 | +6.2 / +5.9 |
| torch_off | +0.7 / -0.4 | -0.6 / -0.2 |
| fshade_off (far shading) | -1.0 / -1.0 | +1.4 / +0.6 |
| fshadow_off (far shadows) | +2.4 / +1.5 | +0.7 / 0.0 |
| farprim_off | +0.7 / +1.0 | +0.9 / -0.2 |
| fluid_off | -0.1 / 0.0 | -0.7 / -0.2 |
| hud_off | +0.6 / -0.1 | +0.5 / +0.3 |
| ups_none (FSR3 pass; 540 rows rendered either way) | +5.5 / +4.7 | +6.1 / +5.2 |
| tiny_gate (256x144, no upscaler = CPU + fixed floor) | frame 7.1 ms | frame 5.0 ms |
Noise: walk_near FULL range 7.3 / 5.3 ms (arms under ~3 ms are not resolved there); walk_open range 2.1 / 1.1 ms.
Not run: tiers_off (shells draw instead of tiers 1-2), see-through off (no "off" arm exists; FULL has it ON, only `see_*` arms exist and they force ON).

## Table 2: moving (MODE=walk circle, 3 min, `-tod 12 -memledger -frametiming`, shells ON)
| launch | log | p50 | p99 | frames > 33 ms | peak MB |
|---|---|---|---|---|---|
| 1 | `Logs/tg2world_20261022_133936.txt` | 40.97 ms | 98.70 ms | 77.57% | 2417 |
| 2 | `Logs/tg2world_20261022_134432.txt` | 41.03 ms | 99.44 ms | 72.04% (ended on battery) | 2304 |
FrameTimingManager (L1): cpu main p50 40.96 ms; GPU p50 110.7 ms (inflated ~2.6x on this Mac, relative only).

## Table 3: resolution sensitivity, PARTLY DONE
`-renderrows 720` / 360 cannot be set in the cost split: `CostApply` hardcodes `UpscalerSelect.Apply(..., 540, ...)`, so the flag is ignored.
I did not change code (task rule). What the existing arms say: frame at 540 rows 60.7 / 43.1 ms (near/open); the 7%-pixel `tiny_gate` frame 7.1 / 5.0 ms,
so about 53 / 38 ms of the frame scales with pixels (a linear fit from two points, not a measurement at other rows). The FSR3 pass itself is ~5-6 ms.
Follow-up for 111 or a tiny task: read `-renderrows` in `CostApply`, then run FULL at 360 / 720.

## The 5 largest costs (mean of both launches, ms)
1. walk_open, shellmarch_off: 18.8 (shell primary march)
2. walk_open, far_off: 17.7 (far band; overlaps #1, since far_off also cuts the shell march to 1 iter/ray)
3. walk_near, sun_off: 15.7 (sun shadows; only 6.0 at walk_open)
4. walk_near, far_off: 15.2 (same overlap with shell march: shellmarch_off 12.8)
5. walk_near/open, shells_off: 13.4-13.9 (whole shell path)
Next smaller: FSR3 pass 5-6, sun shadows at walk_open 6. Everything else is < 3 ms.
Moving is no better than steady: 41 ms p50, 72-78% of frames > 33 ms (a 30 fps target needs ~-8 ms, p99 ~-65 ms).

## For the doc
- Steady FULL p50: walk_near 60.7 ms, walk_open 43.0 ms (`Logs/costsplit_2026-10-22_*.txt`); moving 41.0 ms p50 / 99 ms p99, 72-78% > 33 ms, peak 2.3-2.4 GB.
- Largest: shell march 12.8-18.8, far band 15-18, sun shadows 6-16, shells overall 13-14, FSR3 5-6; sky-ray exit already saves 1.6-3.7.
- Image (not committed) `Screenshots/110/walk_near_full.png`: walk_near at FULL, looking into a sandstone/terracotta slope at close range (HUD overlays visible, 27 fps readout).
- Image `Screenshots/110/walk_open_full.png`: walk_open at FULL, sky above ~half the frame, terraced desert hills and a snow patch at lower left (45 ms readout). Sun is behind the camera in both.
