# 51-engine-3d-upscaler-fps-realrate-clips

Requires: 50-engine-3c-upscaler-clips

Approval: owner, 2026-10-10 in chat: "The whole point is to compare both fps and visual quality." Task 50's
clips run every arm at a fixed 30 fps, so they show quality on identical frames but hide each arm's real
frame rate, and they understate temporal smear for arms that run slower (task 48's dyn smoothing saw
34-62 ms/frame, i.e. the camera moves up to ~2x further per frame in real play). This task measures each
arm's frame rate and re-records the MOTION clips at that real rate, so the owner sees speed and quality
together. This IS a performance task: PREAMBLE rule 9 (warm steady state) applies; rule 10 does not forbid it.
Queued by the supervisor 2026-10-10. Branch `engine-phase`. Reuse task 50's `-upscalerclips` code, paths and
encoder (read 50's HANDOFF first). Do NOT change any default.

Arms: the same 6 as task 50 (`native`, `taa_540`, `fsr1_540`, `stp_540`, `fsr3_dyn`, `fsr3_720`).
Paths: the three motion paths from 50: `walk_cliff`, `spin_stop`, `fly`.

## Lighting (owner, 2026-10-10: "why are some of the pics so dark and taken in bad lighting conditions?")
Task 48's near_cliff and horizon stills faced INTO a low sun: the terrain is a near-black silhouette, so
the owner could judge nothing but the outline. For this task:
- Day paths at midday (as `-startview` / the M key in `./run-play.sh`), with the sun BEHIND or beside the
  camera, so the terrain faces are lit and the detail (voxel faces, grass, rock bands) is visible. If a path
  must turn through the sun (spin_stop), start and stop facing away from it.
- Before recording, check each path's first frame: mean luma of the terrain part of the frame above ~70/255
  and the sky under ~1/3 of the frame. Re-aim the path (keep its purpose: cliff edge, ridge, speed) if not;
  say in HANDOFF what you changed.
- If task 50's `walk_cliff` or `pan_horizon` clips came out backlit or dark (check its HANDOFF and a frame),
  ALSO re-record those two at the fixed 30 fps in the new light into `Screenshots/Engine03/clips/` with the
  suffix `_lit`, so the quality comparison has a well-lit version too.
- night_torches stays at night (it tests torch light), but expose it as `./run-play.sh` does at night.

## Part 1 -- frame rate per arm (warm steady state)
- ONE warm-up launch first, numbers discarded (`MODE=stream MOTION=walk ./run-tg2world.sh`, ~6 min); then the
  measured launch straight after (no idle gap over ~2 min). Report the warm-up's log path.
- In the measured launch: stream each path once, settled (deficit 0 along it), BEFORE timing. Then, per path,
  run the arms in the order A B C D E F F E D C B A (ABBA-style, each arm twice), each for 3 s settle + 20 s
  timed, camera driven by REAL time (`Time.unscaledDeltaTime`, NOT a fixed step), and NO capture of any kind
  while timing (no readbacks, no frame saving, no HUD, no screenshots). Same window size and render preset as
  task 50's clips (quality + far shadows), and the same as `./run-play.sh` uses if that differs, say which.
- Per arm x path: frame time p50 / p95 / p99 (ms), fps at p50, and for `fsr3_dyn` the rows it ran at.
  Use the two passes to show spread; if they differ by more than 10%, say so and do not average them away.
- Power: on AC, log `pmset -g batt` before and after; note any owner pause line.

## Part 2 -- real-rate clips
- Per arm x path, record the clip with a FIXED step equal to that arm's measured p50 frame time on that path
  (`Time.captureDeltaTime = p50 / 1000`), so the camera moves exactly as far per frame as in real play,
  without the capture slowing the game. Same lead-in and history reset as task 50.
- Encode for real-time playback: output 60 fps, each arm's frame held until its next frame is due
  (sample-and-hold), so an arm at 20 fps visibly steps and every clip lasts its real 8 s. Burn the label
  "<arm>  <fps> fps (<p50> ms)" into each tile.
- Outputs in `Screenshots/Engine03/clips_realrate/`: `grid_<path>.mp4` (3x2, 960x540 tiles), `crop_<path>.mp4`
  (3x2 1:1 crops, same crop rects as task 50), uncommitted, total under ~300 MB.
- Check: every MP4 opens, durations right; look at extracted frames mid-spin and 1 s after the stop.

## Disk
Same rules as task 50: JPEG temp frames, delete per path after encoding, peak temp under ~3 GB, delete the
player-data run folders after the numbers are in the log. Do not delete task 48's or 50's outputs.

## Report for the owner (`## For the doc` in HANDOFF)
1. A table: arm | walk fps (p50 ms / p95 ms) | spin fps | fly fps | dyn rows | notes. Say plainly which arms
   reach 30 fps and 60 fps on this Mac and which do not.
2. The MP4 paths, and per path 1-3 lines on what changed versus task 50's fixed-30 clips (more smear? stepping?).
3. 2 still frames (mid-spin from crop_spin_stop, real-rate vs task 50's fixed-30) with captions.
