# 126-look-dusk-calmer-red: DONE. Dusk is a violet sky with a faint red-brown glow, and sunset is a little less red. Golden hour and noon are unchanged. The owner can compare with `-skyrimscale 1`.

No owner decision is needed. Optional: dusk's glow near the sun is faint now. Values between 0.5 and 1 give more of it, e.g. `EXTRA="-skyrimscale 0.7" ./run-play.sh`.

## What changed (commit `fa9a30c`)
- `SunCycle.RimKeepAt(el, F)` gives the share of the rim that is kept:
  - 1 from 6 deg of sun up, so golden hour (14.6 deg at 17 h) and noon are untouched;
  - F at -4 deg and below;
  - 0.82 at sunset (2 deg), with F = 0.5.
- `LookSkyRimAt(hours, scale)`:
  - multiplies the rim strength by that share;
  - once the sun is down, mixes the red toward a violet-brown (none at 2 deg, so sunset stays orange).
- New `LookSkyRimShapeAt`, sent to the shader as `_LookSkyRimShape`:
  - x: the rim's weight on the side away from the sun (task 121: 0.4);
  - y: the power of the halo floor's lobe round the sun (121: 8; at dusk now 15.8, so a tight glow instead of a red wash).
- `Raymarch.compute LookAtmosphere`: the halo floor now has its own lobe, computed as `max(lerp(sky, halo, pow8), lerp(sky, rim, pow y))`. With y = 8 this equals task 121's `lerp(sky, max(halo, rim), pow8)`, because a lerp with a weight in 0..1 is monotonic.
- New flag `-skyrimscale F`: `RaymarchFeature.SkyRimScale`, default `SkyRimScaleDefault` 0.5, clamped to 0..1. `-skyrimscale 1` = task 121.
- Capture: `-looksky 1 -lookrim 1` makes the sky A/B's second arm `<pose>_sky_<t>_rim121` (F = 1) instead of `_old`. Every `-looksky` run now also shoots `dusk_away`.
- New tool `tools/rim126-sheet.py <dir>` builds the sheets and `rim126_diff.txt`.
- Tests: `SkyRimCalmTests` (4). TASKS.md has a paragraph.

## Evidence
- Filter run (`SkyRimCalmTests`, `SkyLookTests`, `DrawnEdgeFadeTests`): 10/10.
- Fast suite: `PASS 921 FAIL 0 SKIP 15` (`Logs/overnight/126-tests.out`).
- Look launch: High preset, FSR 3 at 720 rows, poses high + walk_open, 45 s shell cap.
  - Log: `Logs/lookreview_20261023_084716.txt`. No `SHADER RUNTIME ERROR`.
  - Shells still missing at the cap: s3-s7 at walk_open, s4-s7 at high.
- Px diff, NEW vs 121, in the sky (rows 0-449 of 1080), from `Screenshots/126/rim126_diff.txt` and my own sky-only check:
  - Noon: mean RGB identical to 0.1 level, max 3 levels. Both arms send bit-identical uniforms at noon, so this is frame noise.
  - Golden away: same, max 3. Golden toward: max 7, near the disc.
  - Whole-frame diffs (about 40% of pixels) are on the terrain, which kept streaming between the two shots.
  - Not measured: task 121's binary against this build (two launches would differ by streaming). By the max identity above, the change there is at most float rounding.
- Sky mean RGB, rows 0-449, high pose (walk_open is similar):
  - dusk toward the sun: (70,29,27) -> (27,13,26);
  - dusk away: (30,15,27) -> (11,9,27);
  - sunset: (152,86,63) -> (118,69,57).
- Rim uniforms at dusk: strength 0.87 -> 0.45, colour (0.55,0.24,0.17) -> (0.48,0.23,0.24), away weight 0.21, lobe 15.8 (`Screenshots/126/lookreview.txt`).

## Left / next
- Dusk's glow is a faint dark-red patch just above the horizon. Raise F if the owner wants more.
- Not checked: dusk under FSR off. The launch shot FSR 3 only, as task 121 did.

## For the doc
- Dusk toward the sun, sky mean RGB: (70,29,27) red -> (27,13,26) violet. Rim strength 0.87 -> 0.45, glow lobe power 8 -> 15.8. (`Screenshots/126/lookreview.txt`, `Logs/lookreview_20261023_084716.txt`)
- Dusk away from the sun: (30,15,27) -> (11,9,27), dark blue-purple.
- Sunset (2 deg), toward the sun: (152,86,63) -> (118,69,57). Rim strength 1 -> 0.82, the same orange colour.
- Noon and golden hour, sky: mean identical to 0.1 level; max 3 levels (7 at the golden disc), which is frame noise.
- `Screenshots/126/high_rim_sheet.png`: LEFT is task 121, RIGHT is task 126. Rows top to bottom: noon, golden away, golden toward, sunset, dusk toward, dusk away.
  - Rows 1-3: left and right are the same.
  - Row 4: the right side is a softer orange round the sun.
  - Row 5: the red wash over the whole frame becomes a violet sky with a faint red-brown glow at the horizon.
  - Row 6: purple-red becomes a dusk blue.
- `Screenshots/126/walk_open_rim_sheet.png`: the same layout at the walking pose. Look at rows 5-6.
