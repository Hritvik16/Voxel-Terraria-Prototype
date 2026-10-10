# 51-engine-3d-upscaler-fps-realrate-clips: DONE. Frame rate per arm timed (warm, ABBA, 0 streaming frames); 3 real-rate clips + 2 lit 30 fps clips. No default changed.

## What changed (branch `engine-phase`: 0624152, 3fe865a, e71ec70)
- New `Assets/Game/PlaygroundCapture.UpscalerRealRate.cs`, run as `-upscalerclips -realrate` by the new `run-upscaler-realrate.sh`. Each step:
  - **Light:** noon, sun at 78 deg elevation toward +Z. The eyes stand on the sun's side of the peak and look at it. A path is kept if its first frame has terrain luma >= 70 and sky <= 1/3 (a CPU ray per cell against the map).
  - **Timing:** per path, the arms run A..F then F..A, each 3 s settle + 20 s timed. The camera follows the path in real time. No capture, no HUD, fluid on.
  - **Clips:** recorded at `captureDeltaTime` = the arm's pooled p50. fsr3_dyn is held at its timed rows (`UpscalerSelect.HoldDynRows`).
  - **Encode:** 60 fps sample-and-hold. `tools/upscaler-clip-grids.py` has a real-rate mode; `tools/upscaler-realrate-encode.sh` drives it.
- `Phase4Bootstrapper.DebugStreamCentre`: a measurement seam, null (off) by default. Timing pins streaming at the path's middle pose.
  - Why: in the first launch the window recentred on every chunk and Y layer the walk crossed, so every timed frame streamed (resident 3591 -> 2650; walk taa 59 then 81 ms). Log: `Logs/overnight/51_run1_streaming_aborted.txt`, "while streaming" evidence only.
- `-raterecord <path>:<arm>=<ms>,...` re-records single arms. During fly's fsr1 and stp recordings the Metal surface jumped to 2880x1800 (Player.log RecreateSurface, as in task 50), so I re-recorded those two arms and merged them.

## Evidence
- Fast suite PASS 634 FAIL 0 SKIP 14 after the last change (`Logs/overnight/51_fast3.out`).
- **Warm steady state.** Warm-up `Logs/tg2world_20261010_125624.txt`, then the measured launch at once (45 s lock gap). On AC, 100% charged before and after; owner hold no/no.
- Measured log: `Logs/upscalerrealrate_2026-10-10_130258.txt`, copied to `Screenshots/Engine03/clips_realrate/upscalerrealrate.txt`. Fly re-record: `Logs/upscalerrealrate_fly_rerecord.txt`.
- **Timing quality:**
  - Streaming frames (deficit > 0) while timing: 0 on every arm. Resident stayed at 3087.
  - Pass 1 vs pass 2 p50 within 2% except fly: taa 13.2/14.6 (10%), fsr1 12.6/14.1 (12%, over 10%: not averaged away, shown pooled), native 6%, stp 6%.
- **Fly caveat:** the fly path reaches 200 m from the pinned centre, past the voxel window, so the far band draws its ends while timing. Real 40 m/s flight streams all the time (KNOWN_ISSUES streaming), so real fly runs slower.
- **Spikes in every arm:** p95 is 140-160 ms on walk/spin even for 540-row arms, about +100 ms on top of p50 whatever the upscaler. Mean fps is far below p50 fps (fly fsr1 39 mean vs 74 at p50).
  - Not isolated; fluid was on (6-frame tick), the first suspect. The clips use p50, so they show the smooth part, not the hitches.
- **Clips:** every recorded frame had deficit 0, 0 timeouts, 0 in rock, and was 1920x1080 after the re-record.
  - Real-rate MP4s: 480 frames, 8.000 s, 60 fps each. Lit MP4s: 240 frames, 8.000 s, 30 fps each.
- **Light check:**
  - walk_cliff: cliff 3 of 10 with pitch +25 deg, luma 73, sky 11% (task 50's: mean luma 30 and 19, backlit; its pan_horizon had a dark peak at 4 s, so both got `_lit` re-records).
  - spin_stop: r0.45_d0 pitch +15, luma 77, sky 0%. fly: r0.45_d0, luma 74, sky 1%. pan_horizon_lit: r0.65_d-30, luma 76, sky 28%.
- Frames I looked at:
  - walk grid at 2.0 s: the snow slope is sunlit, but the cliff wall is still grey and shaded (a 78 deg sun barely lights a wall). Also spin at 2.0/5.0 s, fly at 4.0 s, lit walk/pan.
- Disk: raw frames peaked at 4.7 GB (fly's refused 3.8 GB was held for the re-record), over the ~3 GB plan; 22 GB was free. All raw frames and run folders are deleted. `clips_realrate/` is 138 MB; the 4 `_lit` MP4s are 68 MB. Per-arm singles were skipped (`NOSINGLES=1`).

## Not done / next
- night_torches not re-recorded (not this task's path). A `-raterecord` run overwrites `clips_realrate/upscalerrealrate.txt` (restored by hand).
- Suggested follow-up (owner's call, not queued because it is a measurement): find the cause of the ~100 ms hitches every few frames (fluid tick?). They cost more than any upscaler choice.
- `tools/overnight/PAUSE` exists (14:04, not mine). The queue will stop after this task.

## For the doc
| arm | walk fps (p50 / p95 ms) | spin fps (p50 / p95) | fly fps (p50 / p95) | dyn rows | notes |
|---|---|---|---|---|---|
| native 1080p | 5.9 (170 / 271) | 7.3 (138 / 236) | 25.0 (40 / 144) | - | slowest by ~3x |
| taa_540 | 16.9 (59 / 156) | 22.9 (44 / 141) | 71.7 (13.9 / 64) | - | fly passes 13.2 / 14.6 |
| fsr1_540 | 17.3 (58 / 160) | 23.4 (43 / 143) | 74.4 (13.4 / 57) | - | fastest; fly passes differ 12% |
| stp_540 | 16.7 (60 / 163) | 22.1 (45 / 146) | 63.8 (15.7 / 54) | - | |
| fsr3_dyn | 16.0 (62 / 163) | 20.7 (48 / 153) | 54.4 (18.4 / 98) | 540; fly 540-576 | sat at 540 |
| fsr3_720 | 10.4 (96 / 195) | 13.3 (75 / 174) | 37.3 (26.8 / 125) | - | 720 rows cost +36 ms on walk |

**Plainly: on this Mac no arm reaches 30 fps at eye level** (walk and spin). The 540-row arms run 16-23 fps there, fsr3_720 10-13, native 6-7.
Only the fly view, looking down at ground 30 m below, is faster: the 540 arms reach 30 at p50 (54-74), fsr3_720 37, native 25. Their p50 is over 60 for taa, fsr1 and stp, but mean fps on fly is only 31-39 because of the hitches, so **nothing holds 60**.

Videos in `Screenshots/Engine03/clips_realrate/` (`grid_<path>.mp4` 3x2 960x540 tiles, 24 MB; `crop_<path>.mp4` 1:1 crops, 20 MB). Each tile is labelled "<arm>  <fps> fps (<ms> ms)".
- **walk_cliff:** a new, sunlit cliff and view. native visibly steps (each frame held ~0.17 s). The 540 arms step about 17 times a second and move 0.3 m per frame (task 50: 0.17 m), so expect more smear than in task 50.
- **spin_stop:** a new view (lit sand and lake). At ~22 fps the 540 arms turn 8 deg per frame (task 50: 6 deg); native turns 25 deg per frame and jumps.
- **fly:** the 540 arms run 54-74 fps, so they move 0.5-0.7 m per frame against task 50's 1.3 m. They should smear less than in task 50; native is close to task 50.
- Well-lit 30 fps versions of task 50's dark paths: `Screenshots/Engine03/clips/{grid,crop}_{walk_cliff,pan_horizon}_lit.mp4`.

Images:
- `Screenshots/Engine03/clips_realrate/stills/spin_stop_crop_t2.0_midspin.png`: real rate, mid-spin, the far lake shore.
  - Top middle (taa): the bright shoreline band is smeared into a soft blur.
  - Top right (fsr1): broken bright fragments and speckled water. Bottom row (stp, fsr3): crisper.
- `Screenshots/Engine03/clips_realrate/stills/task50_fixed30_spin_stop_crop_t2.0_midspin.png`: task 50's fixed-30 mid-spin, a different (old, dimmer) view of a sand slope. Compare how much taa (top middle) blurs the dune ripples there with the real-rate still above.
- `Screenshots/Engine03/clips_realrate/stills/lit_native_walk_t1.0_pan_t4.0.png`: the new light, native. Left (walk_cliff_lit): sunlit snow terraces, cliff wall still shaded grey. Right (pan_horizon_lit): peak, sand and river fully lit.
