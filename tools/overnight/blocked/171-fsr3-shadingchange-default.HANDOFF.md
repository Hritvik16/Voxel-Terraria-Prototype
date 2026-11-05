STOP_FOR_OWNER -- task 171: F2 (`-fsr3sc 10`) ghosts behind a MOVING light. Play default NOT flipped. How to go on?
- (a) Keep today's FSR 3 (sc 0). This is the current state; nothing changes.
- (b) Ship sc10 anyway: crisper still terrain (task 166). Cost: a doubled image plus a bright speckle trail behind moving lights (moving torch tested; flowing lava is the in-play case, NOT tested). Also 1 extra frame of lag when a torch is lit or put out.
- (c) **Recommended:** a new task gives FSR 3 a reactive mask for the pixels the shading-change detector was protecting, then reruns this QA with sc10. Mask = emissive + fluid pixels + pixels whose voxel changed this frame. 166 already found the mask missing. Keeps the crisper terrain without the ghost. Needs your OK (design work).
- (d) A partial cut (e.g. sc5): one capture-only launch with `-fsrlivearms native,432,432_sc5,432_sc10`. Probably in between on both counts.

# 171-fsr3-shadingchange-default: QA done, NEW GHOSTING found -> default NOT changed (task step 3)

## What changed (`9fd2f63`, engine-phase; capture-only, play unchanged)
- `Assets/Game/PlaygroundCapture.FsrLive.cs` (new): `-lookreview -fsrlive 1`. Bursts of consecutive frames per arm: native / fsr3_432 / 432_sc10 / 540 / 540_sc10. Everything that moves is stepped per FRAME, so native frame k is the truth for arm frame k.
  - **Sun** at walk_open from 16 h: clock still, play's 1200 s day, and the rigs' 120 s day.
  - **Slide:** a 1.5 m water cube moving 0.1 m/frame.
  - **Pour:** a 2x2x3 m water column with the fluid sim RUNNING. Not frame-exact; removed afterwards.
  - **Night torches** (task 151 scene): one torch lit and put out (frame before + 6 after), and one torch moving 0.1 m/frame.
- `LookReview.cs`: the hook. `tools/fsr171.py`: numbers + 4x sheets. `TASKS.md`: task 171 paragraph.

## Evidence (capture only, no frame times)
- Launch: `Logs/lookreview_20261105_093519.txt` (High). Shots: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/2026-11-05_093929_high/`. Table: `Screenshots/171/fsr171.txt`.
- Shell fill at the sun pose after the 40 s cap: 3,375 missing (s3-s7). Far shells were unfilled; the near field, where the tests happen, was full.
- The pour really flowed: fronts fell 1.7 m, and water spread out of the 8 m box (12,000 -> 10,415 voxels in it); all 12,000 removed afterwards.
- Numbers below are error vs native inside the change mask (pixels native itself changes by > 12 levels), today -> sc10:

| scene | 432 | 540 | verdict |
|---|---|---|---|
| sun, play day | 20.5 -> 21.3 | 21.7 -> 23.3 | clean, same look |
| sun, 120 s day | 26.9 -> 27.1 | 25.9 -> 25.9 | clean |
| water cube slide | 5.36 -> 5.42 | 4.69 -> 5.33 | both trail the cube about equally |
| torch moving | **17.2 -> 47.2** | **12.8 -> 49.7** | **new ghosting with sc10** |
| torch lit, frame 1 | 13.0 -> 16.4 | 11.4 -> 15.9 | +1 frame of lag; equal from frame 2 |
| torch out, frame 1 | 6.8 -> 11.6 | 5.7 -> 12.5 | +1 frame of lag; equal from frame 2 |

- Whole-frame error barely moves: sun still 2.99 -> 3.12. The problem is local to moving or changing light.
- Pour, judged by eye (not frame-exact): both arms look alike. sc10 shows some brighter light-blue blotches at the water's edge, but the water states differ between arms, so this is not conclusive.
- Fast suite: PASS 997 FAIL 0 SKIP 16 (`Logs/overnight/171_fast.out`).

## Why it ghosts
- Our motion vectors are camera-only, so a moving torch or a light change has no motion vector.
- With sc10, FSR's only defence there is its colour clamp. The shading-change detection that sc10 turns off was what restarted accumulation where the torch had been.

## Not done
- No lava, which is the in-play moving emissive.
- No reactive mask.
- No sc5 arm.

## For the doc
- Error in the moving-light region, today -> sc10: moving torch 17.2 -> 47.2 at 432 (12.8 -> 49.7 at 540). Sun moving at the 120 s day 26.9 -> 27.1 (no change). Water cube 5.36 -> 5.42 (`Screenshots/171/fsr171.txt`).
- Torch put out, frame 1 after: 6.8 -> 11.6 (frame 2: 5.6 -> 5.3, caught up).
- `Screenshots/171/fsr171_torch_move.png`: THE PROBLEM. 4x zoom; row 1 native / today 432 / sc10 432, row 2 the 540 arms. Look at the torch, bottom right: sc10 draws it twice and leaves bright speckles along its path to the left. Today's arm shows one torch with a soft edge.
- `Screenshots/171/fsr171_torch_off.png`: the frame after the torch goes out, same layout. The lit ground should be dark, as in native. Today keeps a soft glow; sc10 keeps a brighter, blotchy one.
- `Screenshots/171/fsr171_sun_fast.png`: the clean case. The sun moves 0.1 deg/frame and the shadow edges look the same in today and sc10.
- `Screenshots/171/fsr171_slide_full.png`: whole frame, today 432 (left) | sc10 432 (right). Look at the water cube's left (trailing) side: both show a faint smear, about equal.
