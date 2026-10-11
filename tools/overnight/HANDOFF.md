# 56-engine-8-shadow-dots: DONE -- the owner's sun-shadow "dots" were false shadows from temporal shadow reuse (BUGS #1, case (a)); fixed in the shadow path.

## Diagnosis
- Play runs with temporal shadow reuse ON (`RenderPreset.Apply` defaults `-tshadow 1`; play's FSR 3 default keeps it; the owner's Quality preset too).
- Each frame 3 of 4 thread-group tiles (8x8 gate pixels) skip the sun ray and reuse last frame's value.
  - The lookup reprojected the hit voxel's CENTRE, a point inside the solid. On a top face seen from walking height, it lands about 1.5 voxels nearer, on the NEIGHBOUR's face.
  - It then accepted the neighbour on a depth window (2% + 2 voxels).
  - Result: a lattice of small lit-in-dark and dark-in-light squares on stepped ground at a low sun. It shows on a still camera too.
- Ruled out with the CPU oracle (`ShadowRayReference.Trace`):
  - origin, bias, soft term, half-res: with `-tshadow 0`, the GPU and oracle agree to 0.01-0.53%.
  - travel LOD: it is 0 in config.
- The owner's photos match: small axis-aligned squares on grass, cross shapes and triangles on sand.

## The fix (commit ade1b22, branch engine-phase; a bug fix, no API or layout change)
- `Raymarch.compute`: reproject the pixel's own surface point (as TAA does) and try the nearest history pixel plus 3 neighbours.
  - Reuse only on an exact match of `ShadowOriginKey(voxel + normal)`. TraceShadowRay's result depends on that cell alone.
  - The history is now `uint2` (sunVis bits, key).
- `RaymarchFeature.cs`: fill the history buffers with key 0xFFFFFFFF on creation (the checkerboard's unwritten half must never match). Dropped the now-unused `_PrevCamVox`.
- New `-shadowdots` capture (`PlaygroundCapture.ShadowDots.cs`, `run-shadow-dots.sh`, `TAG=before|after`).
  - Picks 3 poses from the map where a 6 deg sun reaches the ground: 2 sun-facing slopes and 1 flat at sea level.
  - Waits for deficit 0, shoots Quality and FSR 3 with ts on/off, still and moving.
  - Compares the sunVis view to the oracle pixel by pixel.
  - Added to Playground's capture/fly-mode flag lists (the walk-mode trap cost me 3 runs).
- KNOWN_ISSUES has a new entry. BUGS #1 is moved to Fixed. `later/38` is moved to `done/`.

## Evidence
- Fast suite: PASS 641 FAIL 0 SKIP 14, 0 `error CS`; all 19 ShadowRay* tests pass (`Logs/overnight/56_fast.out`).
- `run-phase7-rig.sh` (fresh release build): PASS 37 FAIL 0 (`Logs/overnight/56_phase7.out`).
- Oracle, `-tshadow 1`, golden hour 6 deg and 12 deg (before: `Logs/overnight/56_before5.out`; after: `Logs/overnight/56_after.out`):
  - false-dark: slope1 1.05-1.42% -> 0.01-0.03%; slope2 4.6-5.6% -> 0.41-0.53%; shore 1.2-1.6% -> 0.04-0.05%.
  - After the fix, every arm is identical to `-tshadow 0`.
  - The leftover (slope2 0.4-0.5%, false-lit ~0.3-0.6%) is present with reuse OFF as well: the documented dense-brick-only penumbra gap.
- Old-shader rerun for the ray counts (`Logs/overnight/56_before_stats.out`) reproduced the dots (1.1-5.4%).
- Cost: no timing (rule 10). Rays traced per frame with `-tshadow 1`, % of hit pixels (`56_before_stats.out` vs `56_after_stats.out`):
  - before 31-42%, after 25-26% still and 25-33% moving (Quality).
  - FSR 3 moving: 26-47% after vs 31-44% before. One shore frame was higher (47.1 vs 40.1).
  - Expect equal or slightly lower shadow cost; per-pixel overhead is up to 4 history loads.

## Screenshots looked at (`Screenshots/56/`, untracked; native-size crops, left = before, right = after unless noted)
- `diagnosis_slope2_12deg_tshadow_on_vs_off.png`: BEFORE build only.
  - Left column is `-tshadow 1`, right column `-tshadow 0`; top row beauty, bottom row the sunVis view.
  - Top-left shows the dot lattice on the lit voxel tops. Top-right is clean.
- `shore_6deg_ts1_move_before_after.png`: flat ground at sea level, moving. Left: lit squares and crosses inside the cast shadows (the owner's sand look). Right: solid shadow shapes.
- `slope1_6deg_fsr3_move_before_after.png`: play's FSR 3 path. Left: a sawtooth "crenellation" along the lit edge, centre-left. Right: a straight edge.
- `slope2_12deg_ts1_still_before_after.png`, `slope1_6deg_ts1_move_before_after.png`: the same lattice gone, still and moving.
- `noon_ts1_before_after.png`: the noon control, whole frame.
  - The two frames are the same except a few stray dark marks at the bottom-left corner of "before".

## Not done / for the owner
- No play-through by eye (unattended). Worth a look at golden hour (`N`) in `./run-play.sh`.

## For the doc
- Sun-shadow pixels disagreeing with the CPU oracle at golden hour, play's temporal reuse ON: 1.1-5.4% -> 0.01-0.53%, the same as reuse OFF (`Logs/overnight/56_before5.out` -> `56_after.out`).
- Worst pose (slope2, 12 deg): 5.62% -> 0.53% false-dark samples.
- Sun rays traced per frame, Quality, camera still: 31-42% -> 25% of hit pixels. The shadow cost should not rise (no timing taken).
- `run-phase7-rig.sh` PASS 37 FAIL 0; fast suite PASS 641 FAIL 0.
- Image: `Screenshots/56/shore_6deg_ts1_move_before_after.png`. Flat ground at sea level, sun 6 deg, walking. Left (before): lit squares and crosses inside the shadows, the owner's "dots". Right (after): clean shadow shapes.
- Image: `Screenshots/56/slope1_6deg_fsr3_move_before_after.png`. Play's default FSR 3. The sawtooth edge on the lit face (left, centre) is gone (right).
