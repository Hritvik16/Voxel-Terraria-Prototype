# 174-fsr3-reactive-mask: DONE -- FSR 3 reactive mask built; clean QA, so PLAY now defaults to `-fsr3sc 10 -fsr3reactive 1`

## What changed (`b542dcd`, engine-phase)
- `Raymarch.compute`: CSMain's TAA block writes `TaaDynW` only when `_TaaEnabled & 2` (no new uniform).
  - It holds, per pixel in display levels: the dynamic light's share (point lights / torch flood + emissive bleed).
  - For a fluid / lava / emitter pixel it holds the whole pixel's luminance instead.
- `VoxelUpscaleInputs.compute`, new kernel `UpscaleReactive` -> FSR 3's reactive mask. FSR 3.1 feeds this mask into its shading-change channel, which `-fsr3sc` does not cut. The method, and why:
  - (1) **Light/colour test.** The gap between this frame's 3x3 [min,max] and last frame's 3x3, reprojected by camera motion; 0 when the two ranges overlap. Mask = saturate((gap - 0.5)/1.5) display levels; one scale for every scene.
  - Why ranges: version 1 compared the pixel's own value with last frame's range. It flagged ~5 % of a STILL torch-lit frame: under jitter, ~1-2 px voxels flip between faces that get different light (the same aliasing that fires FSR's own detector, task 166). A still scene draws from the same set of values in both frames, so the ranges overlap.
  - (2) **New-voxel test.** In a chunk the mirror rewrote this frame, a surface key (voxel + material) not seen in last frame's 3x3 -> mask 1. It applies only where a voxel is >= 2 px, or the voxel is an emitter (torch / lava / sludge). Reason for the emitter exception: a lava front 45 m away moves < 1 px per frame, which no neighbourhood test can see.
- `RaymarchFeature(.Upscalers).cs`: ping-pong buffers, the pass, binding the mask to FSR. `-fsr3reactive 0|1` (`UpscalerSelect`).
- Capture arm tokens: `rm`, plus diagnostics `rm1` / `rm2` (one test each).
- **Play default** (`Playground.cs`, `IsPlaySettingsRun` only): sc10 + mask. `-fsr3sc 0 -fsr3reactive 0` = the old FSR 3. Rigs and capture are unchanged.
- QA: `-fsrlive 1 -fsrlivescenes task174` (`PlaygroundCapture.FsrLive.cs`), with new scenes:
  - lava streams advancing 0.1 m/frame, aimed by a view ray so the front is in frame: `lava_near` (7 m) and `lava_far` (45 m);
  - flood-ON repeats of the moving torch and both lava scenes (`_f`).
- `tools/fsr174.py`: table, 4x sheets, full-size viewer. `TASKS.md`: task 174 paragraph.

## Evidence (capture only; no frame times)
- Final launch: `Logs/lookreview_20261105_160834.txt`. Shots: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/2026-11-05_161332_high/`. Table: `Screenshots/174/fsr174.txt`.
- Earlier launches (version 1 and the far-only lava): `Logs/lookreview_20261105_151516.txt`, `..._155521.txt`.
- Shells were not all filled after the 40 s cap (s3-s7 missing, about 2,000-3,000). The near field, where the tests happen, was full.
- Error in the change mask vs native, today / sc10 / sc10+mask, at 432 (540 in brackets):

| scene | today | sc10 | sc10 + mask |
|---|---|---|---|
| torch moving | 17.3 (14.4) | 51.6 (53.9) | **8.9 (7.7)** |
| torch moving, flood on | 18.8 (16.3) | 51.4 (55.0) | **9.1 (8.4)** |
| torch lit, frame 1 | 12.2 (10.8) | 17.4 (14.6) | **9.8 (7.8)** |
| torch out, frame 1 | 6.2 (5.7) | 11.2 (12.9) | **4.6 (4.3)** |
| lava near | 13.2 (10.2) | 18.5 (18.4) | **7.4 (6.1)** |
| lava far | 19.1 (23.9) | 21.7 (20.3) | **15.7 (13.8)** |
| lava near, flood on | 15.2 (13.3) | 22.1 (20.9) | **8.5 (7.5)** |
| lava far, flood on | 25.3 (29.5) | 23.3 (31.2) | **12.5 (10.3)** |

- Caveats on the table:
  - Lava far covers only ~400 px (0.02 % of the frame).
  - The `_f` scenes are not strictly frame-exact (the flood pool rebuilds asynchronously).
- Still sun, sharpness vs native (gradient ratio): today 0.80 / sc10 0.85 / sc10+mask 0.87 at 432 (0.87 / 0.92 / 0.94 at 540). Night torch camp: 0.65 / 0.78 / 0.81.
- Mask coverage, from FSR's debug view (top-middle panel, % px):
  - sun_still and torch_still: **0.000** in every frame;
  - torch on/off: 1.3 % on the change frame only, 0 after;
  - moving torch: 0.07 %; lava near: ~0.3 %.
- sc10 vs sc10+mask on the still sun differ by 0.5 levels, although the mask is 0 on every pixel there. My guess: each arm is captured at a different point of FSR's jitter cycle. NOT proven (no A/A pair was shot).
- Fast suite: PASS 997 FAIL 0 SKIP 16 (`Logs/overnight/174_fast.out`).
- Rough cost (look-stage rule, not measured):
  - CSMain: +1 float write per pixel (FSR path only).
  - `UpscaleReactive`: 18 float reads + 1 R16 write per render pixel (~0.4 M px at 540 rows); key reads only in rewritten chunks.
  - Memory: 2 x 4 B x render px (~4 MB at 960x540) + a 1 MB R16 texture.
- Note: CSMain's code changed. TAA-off images could shift by float recompile (task 08's warning). No gate checks image bytes.

## Not done / next
- The play default flipped on capture evidence only. Worth one look in play (`./run-play.sh BUILD=1`): walk past torches and lava at night.
- The water slide and pour (171's scenes) were not re-shot. Water is in the mask's "whole pixel" class.
- A perf milestone should include the mask pass.

## For the doc
- Moving torch, error in the moving-light region at 432: today 17.3 -> sc10 51.6 -> **sc10 + mask 8.9** (540: 14.4 / 53.9 / **7.7**) (`Screenshots/174/fsr174.txt`).
- Near lava front at 432: 13.2 / 18.5 / **7.4**. Torch put out, frame 1 after: 6.2 / 11.2 / **4.6**.
- Still sun sharpness vs native: 0.80 / 0.85 / **0.87**. The mask is 0 on every pixel of the still scenes.
- Full-size viewer: `Screenshots/174/viewer.html`. ←/→ = scene, Space = arm (native, today, sc10, sc10+mask at 432/540, debug view), 3/4 = zoom on the sheet's window. Each scene's caption says what to look at.
- `Screenshots/174/fsr174_torch_move.png`: 4x zoom. Row 1 = native | today 432 | sc10 432 | sc10+mask 432; row 2 = the 540 arms. Look at the torch, bottom right: sc10 draws it twice with a speckle trail; sc10+mask shows one clean torch, sharper than today.
- `Screenshots/174/fsr174_lava_near.png`: same layout. Look at the lava stream's edges: sc10 leaves dark holes and stale rock in the new lava; sc10+mask is clean.
- `Screenshots/174/fsr174_sun_still.png`: same layout, still terrain. Look at the step outlines: sc10 and sc10+mask look the same and are crisper than today.
