STOP_FOR_OWNER -- task 166: ship a fix for FSR 3's softening in play?
**Recommendation: (a) ship F2, `-fsr3sc 10`.** It turns off FSR's AUTO "shading change" detection, which fires on our STILL terrain. It is crisper at 432 and 540, with no added flicker or smear in 159's bursts. Cost estimate: ~0 ms (one multiply in one FSR pass; not measured). Risk: real lighting changes (sun motion, torches, fluids) are no longer flagged by FSR. FSR's colour clamp still limits ghosting, but nothing here tested a MOVING light. A play QA with the clock running, near water, is advised before it becomes the default.
(b) F3 = F2 + RCAS sharpness 1.0 (`-fsr3sc 10 -fsr3sharp 10`): crisper still, about native's edge contrast, but a little more still-frame shimmer (0.36 -> 0.46). (c) F1 sharpness 1.0 alone. (d) Keep today's settings.
The dark block-edge lines do NOT go away with any fix: they are anti-aliasing (see below), so only removing AA would remove them.

# 166-look-fsr3-softening: DONE (cause found, 3 fixes behind flags, play unchanged)

## Inputs FSR 3 is given (all checked)
- Motion vectors: 0 on a still camera (FSR debug view, gray panel). Depth is reversed-Z with EnableDepthInverted (the port sets it). Near/far are right, and no disocclusion is flagged.
- Jitter: correct. Flipping the ray jitter makes walk_open worse (mean |dlum| vs native 3.88 -> 5.88), and so does zero jitter (5.37).
- Colour: LDR, tonemapped in the shader, exposure 1, so no HDR/exposure input is needed. No texture sampling, so no mip bias. The HUD is drawn after FSR. RCAS was on at 0.8.
- MISSING: reactive and transparency/composition masks. They matter only for fluids/transparents, not these edges. Not added.

## The causes
1. **Dark outlines = AA, not a bug.** They are already there with FSR at 1080 rows (no upscale) and RCAS off. Averaged over 16x16 px blocks, FSR 1080 is within 0.77 levels of native (2.53 per pixel). The light is redistributed, not darkened (mean shift -0.2 to -0.8 levels). The darker 0.1 m step faces are sub-pixel tall, so they blend with the tops. Native's point sampling draws them as hard stairs.
2. **Softness = FSR's shading-change detection.** On still terrain, the jittered samples of sub-pixel steps differ from frame to frame. FSR reads that as "the lighting changed", zeroes accumulation there and tightens the history clamp. The debug view shows the green mask over the whole mid-distance terrain; with sc10 it is gone (`Screenshots/166/full/walk_open_fsr3_432_dbg.png` vs `..._sc10_dbg.png`, top-middle panel).

## What changed (`0b43c28`, engine-phase)
- `RaymarchFeature.Upscalers.cs`: new capture/diagnostic statics `Fsr3ShadingChangeCut`, `Fsr3DebugView`, `Fsr3RayJitterSign`, `Fsr3ZeroJitter`.
- Vendored FSR: `ffx_fsr3upscaler_prepare_reactivity.h` gets the `_VeShadingChangeCut` uniform (unset = 0 = upstream). The debug view is now built into release players. Both are recorded in the FSR README.
- `UpscalerSelect.Fsr3KnobsFromArgs` adds `-fsr3sharp N` / `-fsr3sc N`. `Playground.cs` calls it on every run, play included; absent flags = today.
- `PlaygroundCapture.RowsLook.cs`: arm tokens `_sNN _scNN _dbg _jf _j0`, plus the `1080` and `none432` arms. `tools/fsr166.py` builds the table, crops and bursts. Paragraph added to `TASKS.md`.

## Evidence (tg2 High, noon, clock paused; capture only, no frame times)
- Fast suite PASS 981 FAIL 0 SKIP 16 (`Logs/overnight/166_fast2.out`).
- Diagnostic launch: `Logs/lookreview_20261102_043103.txt`, table `Screenshots/166/diag/fsr166.txt`. Fix launch: `Logs/lookreview_20261102_044656.txt`, table `Screenshots/166/fix/fsr166.txt`.
- Table (mean |dlum| vs native / % px > 8 / gradient ratio vs native, where < 1 = softer):

| pose | today 432 | F1 432_s10 | F2 432_sc10 | F3 432_sc10_s10 | 540 today | 540 F2 | 540 F3 |
|---|---|---|---|---|---|---|---|
| walk_near | 1.00/2.0%/1.13 | 1.03/2.0%/1.34 | 1.02/2.0%/1.15 | 1.01/2.0%/1.35 | 0.91/1.7%/1.17 | 0.91/1.7%/1.18 | 0.95/1.8%/1.42 |
| walk_open | 3.89/10.5%/0.80 | 4.04/11.2%/0.94 | 4.07/11.1%/0.87 | 4.17/11.9%/0.99 | 3.46/9.2%/0.87 | 3.61/9.6%/0.92 | 3.82/10.8%/1.06 |
| high | 2.85/9.3%/0.63 | 2.81/9.0%/0.87 | 2.79/8.7%/0.75 | 2.87/9.5%/0.95 | 2.55/7.8%/0.74 | 2.51/7.5%/0.83 | 2.63/8.5%/1.08 |
| cliff | 1.58/2.9%/0.79 | 1.60/3.0%/1.04 | 1.61/2.9%/0.86 | 1.62/3.0%/1.06 | 1.48/2.6%/0.88 | 1.47/2.6%/0.93 | 1.53/2.8%/1.18 |
| torch | 1.15/1.9%/0.65 | 1.13/1.8%/0.86 | 1.13/1.8%/0.77 | 1.15/1.8%/0.93 | 1.02/1.6%/0.76 | 1.00/1.5%/0.83 | 1.06/1.5%/1.04 |

- Mean |dlum| is NOT a quality score here, because native is aliased. A sharper AA image can score worse; the gradient ratio is the sharpness measure.
- Bursts at walk_open (159's flicker; frame 0 vs native frame 0):
  - still 0.36 / F2 0.34 / F3 0.46
  - walk 19.82 / 19.83 / 20.52 (native 21.84)
  - turn 18.52 / 18.49 / 19.28
  - vs native under motion 3.89 / 3.90 / 3.97
  - F2 adds no ghosting or shimmer; F3 adds a little.

## Not done
- No moving-light / fluid QA of F2 (all poses had a paused clock).
- No reactive mask for fluids.
- An intermediate cut such as sc7 was not shot.

## For the doc
- Sharpness at 432, gradient ratio vs native, today -> F2: walk_open 0.80 -> 0.87, high 0.63 -> 0.75, torch 0.65 -> 0.77, cliff 0.79 -> 0.86. With F3: 0.99 / 0.95 / 0.93 / 1.06 (`Screenshots/166/fix/fsr166.txt`).
- Still-camera flicker at walk_open: 0.36 today -> 0.34 F2 -> 0.46 F3 (0-255).
- Dark outlines are AA: FSR at 1080 with RCAS off matches native within 0.77 levels over 16 px blocks (2.53 per pixel) (`Screenshots/166/diag/fsr166.txt`).
- Jitter flipped / zero: walk_open 3.88 -> 5.88 / 5.37 (proves the jitter is right).
- `Screenshots/166/zoom4x_walk_open.png`: 4x zoom, native (top left) / today 432 (top right) / F2 (bottom left) / F3 (bottom right). Look at the step edges in the centre: F2 and F3 hold the stair outlines that today's 432 smears.
- `Screenshots/166/zoom4x_high.png`: same layout at the high pose. Look at the thin dark crevices: they are blurred at today's 432, firmer in F2, firmest in F3.
- Full-size 1920x1080, `Screenshots/166/full/<pose>_<arm>.png`: arms native, fsr3_432 (today), _s10 (F1), _sc10 (F2), _sc10_s10 (F3), fsr3_540 / _sc10 / _sc10_s10, plus `<pose>_fsr3_1080_s0_diag.png` (FSR without upscale: the dark lines are already there). The 2x crops of the darkest edges are `Screenshots/166/fix/fsr166_crops_<pose>.png` (row 1 = native, 432 arms; row 2 = 540 arms).
