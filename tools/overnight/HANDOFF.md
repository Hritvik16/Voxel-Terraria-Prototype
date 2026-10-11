# 57-engine-7-frame-cost-split: DONE -- the play frame is GPU-bound; sun shadow rays are the biggest single cut (14.2 ms near a wall, 4.2 ms on an open view), then the FSR 3 pass (6 ms).

## What changed (branch `engine-phase`, commit 303e109; capture-only, no default or look change)
- `Assets/Game/PlaygroundCapture.CostSplit.cs`: `-upscalerclips -costsplit`, which reuses task 51's harness.
  - Setup: fullscreen 1920x1080, quality preset, far band + far shadows, noon with the clock paused. Same flags as `./run-play.sh`, except FSR 3 is pinned at 540 rows.
  - FULL is play's frame: FSR 3 at 540, HUD + minimap shown, fluid simulating.
  - Each arm switches off ONE thing through existing static switches.
  - Run order: F, then X X F for each arm (ABBA, 2 FULL runs around each pair). Each run is 3 s settle + 20 s timed, in real time, with no capture.
  - Streaming is pinned at the middle of the path, so no frame streams.
- Two 8 s arcs of MODE=walk's circle (r 150 m round the start pose, 5 m/s, eye 1.6 m, level look):
  - `walk_near`: heading straight away from the sun. It faces a badlands canyon wall 10-30 m away.
  - `walk_open`: the most open of the circle's 16 points that has the sun behind or beside. It looks over the island from a ridge, about 45% sky.
  - Why both: the smoke run showed the view sets the cost (52 vs 28 ms). The two arcs bracket what a walk sees.
- Added a `tiny_gate` DIAGNOSTIC arm (no upscaler, 256x144 march). It shows the CPU + fixed-cost floor.
- Also: `run-cost-split.sh` (BUILD_ONLY / SKIP_BUILD, as run-upscaler-realrate.sh) and `tools/costsplit-sheet.py` (3x3 contact sheet).

## Evidence (WARM STEADY STATE)
- Warm-up: `Logs/tg2world_20261010_211902.txt` (MODE=stream MOTION=walk, discarded). The measured launch started 45 s later.
- Measured: `Logs/costsplit_2026-10-10_212527.txt` (copy: `Screenshots/57/costsplit.txt`).
  - On AC, 100% before and after; owner hold no/no.
  - 0 streaming frames and 0 far-busy frames on every run.
- Noise floor: 9 FULL runs per path, p50 range 1.4 ms (near) and 1.6 ms (open). The FULL pair around each arm differs by 0.1-1.0 ms.
- Fast suite PASS 641 FAIL 0 SKIP 14, 0 `error CS` (`Logs/overnight/57_fast2.out`).
- Smoke runs (2 s and 1 s windows) are not evidence. They led to the two-path design.

| arm | near p50/p99 ms | saved | open p50/p99 ms | saved |
|---|---|---|---|---|
| FULL (fsr3 540) | 49.4 / 58.9 | - | 30.4 / 36.2 | - |
| sun_off (rung 3: voxel + far sun shadows) | 35.2 / 42.3 | **+14.2** | 26.8 / 30.9 | **+4.2** |
| ups_none (FSR 3 -> plain blit, 540) | 43.1 / 53.8 | **+6.0** | 24.1 / 30.1 | **+6.2** |
| far_off (far band march) | 47.7 / 57.5 | +1.8 | 26.6 / 32.1 | +3.8 |
| tiers_off (tier 0 only, rays end at 128 m; changes the picture) | 49.3 / 59.4 | +0.3 | 27.2 / 32.9 | +2.9 |
| hud_off (HUD + minimap) | 48.8 / 58.0 | +1.0 | 29.6 / 34.9 | +1.2 |
| torch_off (task 46 rays; no torch in view) | 48.8 / 58.3 | +0.7 | 29.6 / 35.2 | +0.9 |
| fluid_off | 49.3 / 59.9 | -0.2 (0) | 29.9 / 36.1 | -0.1 (0) |
| tiny_gate (diagnostic: floor) | 6.3 / 10.4 | (+43.3) | 3.5 / 6.8 | (+26.8) |

- **GPU-bound.** With the march at 7% of its pixels the frame drops to 3.5-6.3 ms, so frame time follows GPU pixel work.
  - Unity's `cpuMainThreadFrameTime` equals wall time in every arm because the main thread waits on the GPU.
  - Metal reports present-wait as 0, and the render thread takes 2-3 ms. The CPU timer cannot split this; tiny_gate does.
- **Not explained by any one arm:** about 19 ms (near) and 8 ms (open). That is the primary voxel march + shading at 960x540; switching it off is not a single-pass change.
- **Task 51's ~100 ms hitches did not show up:** p99 is 59 / 36 ms with fluid on, and fluid costs 0. Likely cleared by tasks 52-56 (not isolated).
- torch_off saves 0.7-0.9 ms with no torch in view, slightly above the noise. Worth a look (rays toward far-off lights?), not a priority.

## Ranking (what to cut, for step 8's ONE cost change)
1. **Sun shadow rays: 14.2 / 4.2 ms.** My default for step 8: `-roofskip 1` (RaymarchFeature.ShadowRoofSkip). It is already built, exact by construction (changes cost, not pixels), and has never been measured. The next option is rung 1 (half-res beyond LOD1).
2. FSR 3 pass: a fixed ~6 ms at 1080p output (task 51: fsr1_540 ran 4-5 ms faster than fsr3_dyn on walk and spin).
3. Far band 1.8-3.8 ms and tiers 0.3-2.9 ms: these grow with an open view.
4. HUD + minimap about 1 ms, torch rays about 0.8 ms, fluid 0.

## Screenshots looked at
- `Screenshots/57/walk_near_sheet.png`: 3x3, one tile per arm at walk_near's first pose. Every tile is the same canyon wall with the play HUD, sun at 78 deg behind the camera.
  - `sun_off` is flat-lit; `tiny_gate` (bottom right) is blocky; `hud_off` (bottom middle) shows the frame without the panel.
- `Screenshots/57/walk_open_sheet.png`: the same layout for walk_open.
  - The view looks along a sunlit ridge to the sea on the left.
  - `far_off` (middle left): the far sea and hills turn to sky.

## Not done / next
- Not run: other times of day (a low sun makes shadow rays longer) and spin/fly.
- Next: step 8's ONE cost change, picked from the ranking above, then a warm A/B on this same `run-cost-split.sh` FULL arm.

## For the doc
- Play frame (FSR 3 at 540, warm, walk at noon): 49.4 ms facing a near wall, 30.4 ms on an open ridge view (`Logs/costsplit_2026-10-10_212527.txt`). Noise is 1.4-1.6 ms.
- Sun shadows off saves 14.2 / 4.2 ms (the biggest single item).
- FSR 3 -> plain blit saves 6.0 / 6.2 ms.
- Far band off saves 1.8 / 3.8 ms; tiers 1-2 off saves 0.3 / 2.9 ms. HUD ~1 ms, torch rays ~0.8 ms, fluid 0.
- **GPU-bound:** with the march at 256x144 the frame takes 3.5-6.3 ms. The CPU is not the limit.
- Image: `Screenshots/57/walk_near_full.png`. The FULL walk pose at noon, sun behind the camera, play HUD on. A badlands canyon wall fills most of the frame and the sky is top right. This is the 49 ms view.
- Image: `Screenshots/57/walk_near_sheet.png`. One tile per arm, same pose. Compare `full` (top left) with `sun_off` (top middle): at a 78 deg sun the picture barely changes, yet the frame is 14 ms faster.
