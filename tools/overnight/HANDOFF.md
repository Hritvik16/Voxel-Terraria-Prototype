# 50-engine-3c-upscaler-clips: DONE. 6 paths x 6 arms recorded on identical fixed-step frames; 48 MP4s in `Screenshots/Engine03/clips/` (uncommitted).

(Q38 is still the owner's pick. No default changed. A visual task: no frame-time numbers.)

## What changed (branch `engine-phase`: 5c2eca3, 5edd63a, 17dd406, c54fe00)
- `Assets/Game/PlaygroundCapture.UpscalerClips.cs` (`-upscalerclips`, `-clippaths a,b`): the poses are computed once from the tg2 map. Each path is 30 lead-in + 240 recorded frames at `Time.captureFramerate = 30`. Arms run in process via 48's `ApplyCompareArm`. History is reset per arm x path with 2 native frames + URP `resetHistory`.
  Streaming never reaches a recorded frame: on a chunk change, or while loads/uploads are pending, the camera is switched OFF (so no upscaler history moves), the streamer gets the pose, and the run waits.
  `far.Busy` is NOT waited for, because it only clears in the render pass. Those frames are counted instead.
- `run-upscaler-clips.sh`: fullscreen 1920x1080, holds the Unity lock. A background loop encodes each finished path (`tools/upscaler-clips-encode.sh`).
- `tools/frames-to-mp4.swift` (AVFoundation H.264: encode / info / frame), `tools/upscaler-clip-grids.py` (3x2 grid + 1:1 crops).
- `Playground.cs` / `PlaygroundCapture.cs`: flag wiring only.

## Evidence
- Fast suite PASS 634 FAIL 0 SKIP 14 (`Logs/overnight/50_fast4.out`, after the last code change).
- Logs: `Logs/upscalerclips_2026-10-10_110613.txt` (all paths) and `..._115738.txt` (walk_cliff re-record). Encoder logs: `..._encode.txt`. Combined copy: `Screenshots/Engine03/clips/upscalerclips.txt`.
- **Every arm of every path: deficit at render 0 on all 240 frames, 0 wait timeouts, 0 frames in rock, 0 re-records.**
  - Resident chunks: walk 3675, pan/spin/water/night 3087, fly 3087 -> 3562 (about 47 camera-off waits per arm, ~70 s).
  - Far band still baking while a frame rendered: walk 7 frames, fly 15, others 0. I saw no pop in the frames I looked at.
- Every MP4 was checked with AVFoundation: 240 frames, 8.000 s, 30 fps.
- I looked at extracted frames (first/mid/last native per path; crop grids at 2.0/4.0/5.0 s). Every tile shows the same view, labels are right, and there are no black or garbled tiles.
- Two aborted runs, both fixed:
  - Run 1: windowed, the surface jumped to 2880x1800. Fix: fullscreen, plus a size check on every frame.
  - Run 1 also hit `far.Busy` wait timeouts. Logs: `Logs/overnight/50_run1_aborted.txt` and `50_run2_aborted.txt`.
- **dyn caveat:** fsr3_dyn's controller timed capture-slowed frames and the fast camera-off frames. Rows were 540 on the still paths, 540-720 on walk_cliff and 684-720 on fly. This is NOT what dyn would do in play.
- Crop windows (`crop_<path>.json`):
  - Automatic: spin_stop (slope edge on the red peak), fly (sand mid-ground).
  - Set by hand after looking (`crop_<path>.xy`): walk_cliff 640,0 (ridge), pan_horizon 640,240 (peak silhouette), water 640,460 (lakes + sea horizon), night_torches 640,400 (torch ring).
- Disk: raw JPEGs peaked at 4.9 GB, over the ~3 GB plan, because I kept them for the crop re-cuts. 29 GB was free. All raw frames and player data folders are deleted. Clips total **549 MB** (over ~400): the six singles per path (10 Mbit/s, ~10 MB each) are 2/3 of it.

## What I saw (plain observations; the owner decides)
- spin_stop, frame-to-frame luma change while stopped (whole frame, 0-255; t 3.97-4.13 / 4.5-5 / 6-8 s):
  native 0.01/0.01/0.01, fsr1 0/0/0, taa 1.77/1.54/1.53, stp 2.78/1.62/1.53, fsr3_dyn 1.27/0.86/0.86, fsr3_720 1.33/0.86/0.87.
  fsr3 settles within ~0.2 s. taa and stp keep shimmering at rest.
- **taa** smears in every moving clip: water pan, fly, night pan. 1 s after the spin stops it is still soft, with a hazy band at the slope foot.
- **fsr1**: chevron moiré on the sand, black speckle on the snow and sea, and a greyer sky (48's tone shift).
- **stp**: clean and coherent, a little soft.
- **fsr3_720**: the sharpest moving arm. fsr3_dyn showed stronger moiré rings than fsr3_720 in the fly crop at 4.0 s.

## Unfinished / next
- Nothing open in the task. The supervisor builds the viewing page, then the owner answers Q38.
- To save disk, the per-arm singles can go; the grid/crop files carry the comparison.

## For the doc
- Videos in `Screenshots/Engine03/clips/` (all 240 frames / 8 s / 30 fps):
  - `grid_<path>.mp4`: 2880x1080, 17-19 MB.
  - `crop_<path>.mp4`: 1920x720, 1:1 crops, 11-17 MB.
  - `<path>__<arm>.mp4`: 1920x1080, 6-11 MB.
  - Paths: walk_cliff, pan_horizon, spin_stop, fly, water, night_torches. Arms: native, taa_540, fsr1_540, stp_540, fsr3_dyn, fsr3_720. Total 549 MB.
- What to watch, per path:
  - walk_cliff: the ridge line against the sky at 0-2 s and 6-8 s (crop). At 3-5 s the view faces the dark, shadowed wall.
  - pan_horizon: the peak silhouette crawling across the crop at 2-6 s; distant ridge flicker.
  - spin_stop: 0-4 s spinning (the same view returns every 2 s), dead stop at 4.0 s. Watch 4.0-5.0 s for how fast each tile goes still.
  - fly: the sand moiré, and taa's streaks, from 1 s on.
  - water: lakes and sea at 2-6 s. taa blurs; fsr1 speckles the snow.
  - night_torches: the torch glow and sand at 3-5 s.
- Numbers: deficit at render 0/240 on every arm x path. Still-frame luma change after the stop: fsr3 0.86, taa 1.53, stp 1.53 (from 2.78 at the stop), native/fsr1 0.
- `Screenshots/Engine03/clips/stills/spin_stop_crop_t5.0_1s_after_stop.png`: 1 s after the stop. Top middle (taa): soft, hazy band at the slope foot. Top right (fsr1): chevron moiré on the sand. Bottom row (stp, fsr3 dyn, fsr3 720): crisp.
- `Screenshots/Engine03/clips/stills/water_crop_t4.0_midpan.png`: mid-pan. taa (top middle) blurs the lakes and the cliff. fsr1 (top right) puts black speckle on the snow and sea. fsr3 (bottom middle/right) is sharp and clean.
- `Screenshots/Engine03/clips/stills/fly_crop_t4.0.png`: at 40 m/s. taa (top middle) warps the sand into streaks. fsr1 (top right) shows blotches. stp (bottom left) is soft but coherent. fsr3_dyn (bottom middle) has moiré rings; fsr3_720 (bottom right) has fewer.
