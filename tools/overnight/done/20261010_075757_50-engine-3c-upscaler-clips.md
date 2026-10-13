# 50-engine-3c-upscaler-clips

Approval: owner, 2026-10-10 in chat. Task 48's stills and contact sheets "aren't enough for me to pick",
and the owner wants "something automated so I don't have to manually play". This task makes MOTION CLIPS
of each upscaler on identical camera paths so the owner can pick Q38 by watching them. It is a visual
capture task, NOT a performance task (PREAMBLE rule 10): no frame-time claims, no warm-up.
Queued by the supervisor 2026-10-10. Branch `engine-phase`. Builds on task 48's harness
(`PlaygroundCapture.UpscalerCompare.cs`, `run-upscaler-compare.sh`; read 48's HANDOFF in
`tools/overnight/blocked/48-engine-3b-upscaler-compare.HANDOFF.md`). Do NOT change any default.

## Goal
One automated launch records short clips per arm on the SAME scripted camera paths, then encodes them to
MP4 videos the owner can watch side by side. The supervisor turns them into a viewing page.

Arms (6): `native` (1920x1080, no AA, the reference), `taa_540`, `fsr1_540`, `stp_540`, `fsr3_dyn`,
`fsr3_720`. (dyn sat at 540 on every pose in task 48; record it as dyn anyway and log the rows it used.)

Paths (each ~8 s of output at 30 fps = 240 frames; tg2 map v4, quality preset + far shadows as in 48):
1. `walk_cliff` -- walk at 5 m/s, eye 1.6 m, along the foot of near_cliff with the cliff edge and ridge in view.
2. `pan_horizon` -- slow 20 deg/s pan across the horizon pose (distant flicker).
3. `spin_stop` -- 4 s of 180 deg/s turn, then a dead stop held 4 s (smear, ghosting, how fast it settles).
4. `fly` -- 40 m/s straight flight ~30 m above terrain over varied ground.
5. `night_torches` -- slow pan across the night_torches pose.
6. `water` -- slow pan across a lake / sea edge in spawn_day (water surface flicker).

## Rules for identical frames
- Fixed timestep (`Time.captureFramerate = 30`, or the harness's equivalent): every arm gets exactly the
  same camera pose on frame N. Temporal upscalers need their history, so render every frame in sequence;
  do NOT skip frames between captures.
- Stream each path ONCE, settled (deficit 0 along the whole path) before any arm records, so terrain
  pop-in is never mistaken for an upscaler artifact. Then run all arms in-process on it (as 48 did).
  Check the log: report the deficit and the resident-chunk count per path; a path that streams mid-clip
  is re-recorded or dropped, and HANDOFF says which.
- Reset each upscaler's history at the start of each arm x path, plus a 30-frame lead-in not recorded.

## Output (what the owner watches)
For each path, encoded H.264 MP4 at 30 fps, high quality (CRF ~18 or about 12-20 Mbit/s):
- `grid_<path>.mp4`: 3x2 grid of all 6 arms, each tile 960x540 (output 2880x1080), a label burned into
  each tile's corner (arm name).
- `crop_<path>.mp4`: 3x2 grid of 1:1 pixel crops (each 640x360 cut from the 1920x1080 output, NOT
  rescaled) on the part of the frame where edges and detail are (cliff edge, ridge silhouette, torch
  light), labelled. This is where flicker and smear actually show.
- `<path>__<arm>.mp4`: each arm alone at 1920x1080, for flipping between two at full size.
Encoding: ffmpeg is NOT installed and do not install anything. Use a small Swift script with
AVFoundation (`AVAssetWriter`, H.264; `/usr/bin/swift` is present) in `tools/`, e.g.
`tools/frames-to-mp4.swift`. Fallback only if that fails: animated WebP via Pillow (12.3, WebP present).
Compose grids and crops with Pillow (`tools/upscaler-clip-grids.py`). Put the final files in
`Screenshots/Engine03/clips/` and keep them uncommitted (videos stay out of git); total under ~400 MB.

## Disk (97% full, ~7 GB free)
Write raw frames as JPEG q95 (not PNG) in a temp folder, encode path by path, and DELETE each path's raw
frames once its MP4s are written and checked. Peak temporary use under ~3 GB. Delete the player-data
folder the run creates (frames, `UpscalerCompare/...`) after the numbers are in the log.
Do NOT delete task 48's stills in `Screenshots/Engine03/compare/` (the owner has not answered yet).

## Steps
1. Add the clip mode (e.g. `-upscalerclips`, a `run-upscaler-clips.sh` wrapper holding the Unity lock).
   Fast EditMode suite green before committing code.
2. Build and run once. Check every MP4 opens (`mdls`/`afinfo` or AVFoundation reading its duration and
   frame count = 240), and LOOK at a few extracted frames per grid (first frame, mid-spin, after the stop)
   to confirm: same view in every tile, labels right, no black or garbled tiles.
3. Write down what you see per path, in plain words, arm by arm (smear in spin_stop, flicker on the
   ridge in pan_horizon, how fast each settles after the stop, torch glow, water). Plain observations only;
   the owner decides.

## Report for the owner (`## For the doc` in HANDOFF)
The list of MP4 paths (grid / crop / single), their sizes, and per path 1-3 lines of what to watch for
and at which second. Plus 2-3 still frames (PNG, extracted from the crop grids, e.g. mid-spin and 1 s
after the stop) with captions naming the tile and what to notice. No frame-time numbers.
