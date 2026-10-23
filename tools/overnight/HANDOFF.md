# 121-look-sky-and-haze: DONE. New sky and haze are ON by default. `-skylook 0` gives back the old sky and haze; `-skyhaze 0` gives back the old haze only.

## What changed (commit d610032)
- `Raymarch.compute` `LookAtmosphere`, sky look on:
  - The horizon colour now sits near the horizon: weight `pow(1 - up, 4)`, was `dir.y*0.5+0.5`.
  - A time-of-day rim tint runs round the whole horizon (`_LookSkyRim`). It is strongest toward the sun and 40% of that on the far side.
  - The halo is never darker than the rim. The OLD sky had a dark hole round a setting sun because the halo fades with the day factor.
  - New `LookSkyBackground` adds a sun disc (0.6-0.8 deg) plus a tight glow. It is used only for rays that hit nothing, never as the haze colour.
- `SunCycle.LookSkyRimAt`: 0 while the sun is above 28 deg, full from 4 down to -1 deg, gone by -10 deg. Golden while the sun is up, then dusk red, dimmer below the horizon.
- `FarShadeApplyHaze` (far-shading kernel):
  - Haze range is `SkyHazeRangeM` 1800 m (was 3500 m).
  - Distant colour is desaturated by 0.5 x the haze amount.
  - Haze fades to full over the last 45% of the far band's square (about 3.1-5.6 km).
  - The haze colour is still the sky behind the pixel, so it picks up the new horizon and rim.
- **Bug found on the way:** the first launch rendered EVERY frame black, old arms included. Metal failed at run time with `as_type cast from 'bool' to 'float'`.
  - Cause: task 119's conditionally reassigned `bool farRock` went through Unity's HLSLcc wrongly once my changes moved registers around.
  - Fix: it is now a `uint`, with the same logic. One text anchor in `FarRockSlopeTests` was updated to match.
  - `run-look-review.sh` now prints `SHADER RUNTIME ERROR` taken from Player.log. "build OK" does not catch this kind of error.
- Capture: new `-looksky 1` in the look review. Tests: `SkyLookTests` (3). TASKS.md has a paragraph.

## Evidence
- Fast suite: `PASS 903 FAIL 0 SKIP 14`. The first run had 1 FAIL, the text anchor above, which is now fixed.
- Look launch (High, FSR 3 at 720 rows, poses walk_open + high, 45 s shell cap): `Logs/lookreview_20261023_024117.txt`.
  - Shells were still missing at the cap: s4-s7 at walk_open, s4-s7 at high (see the `[shells]` lines).
- Times shot: noon 77.6 deg, golden 17 h at 14.6 deg, sunset 2 deg, dusk -3 deg.
- What the stills show:
  - Noon: the horizon is paler and the zenith deeper; far land is paler and greyer.
  - Golden toward the sun: a disc and a warm sky down to the horizon. The old sky was noon blue round a halo.
  - Sunset: an orange glow round the disc. The old sky was a dark hole on a navy sky.
  - Dusk: a red rim. The old sky was black.

## Open (owner)
- **The hard sea-horizon line is still there, a bit stronger** (noon, high pose: 128 -> 112 levels across it; old 84 -> 75).
  - The fade at the far band's edge did not remove it, so the line is probably the WORLD's edge (camera x 3,857 m looking +x). Not verified.
  - Fixing it needs the world extent passed to the shader (new plumbing). I did not start that. I recommend it as a small look task.
- The dusk sky is red over the whole frame (the halo floor reaches about 40 deg). That may be too strong.
  - Knobs: `LookSkyRimAt` (strength and colour) and the halo floor in `LookAtmosphere`.
- Golden hour away from the sun: the horizon reads pale lavender-grey (rim at 40% over blue). OK to me; owner's taste.

## For the doc
- Sky horizon weight at 10 deg up: 0.41 -> 0.47 of the horizon colour. At the horizon itself: 0.5 -> 1.0 (the old sky only got halfway).
- Haze range 3500 -> 1800 m: haze at 1 km goes from 0.25 to 0.43; at 3 km from 0.58 to 0.81 (sea level, before the height thinning).
- Golden hour, 17 h, toward the sun: no disc -> a disc, and the sky is warm down to the horizon instead of noon blue.
- Images:
  - `Screenshots/121/high_sky_sheet.png`: high pose, rows top to bottom are noon, golden away, golden toward, sunset, dusk. LEFT is old, RIGHT is new. Look at the horizon band in row 1, the disc and warm sky in row 3, and the glow in rows 4-5 where the old sky has a dark hole.
  - `Screenshots/121/walk_open_sky_sheet.png`: the same layout at walk_open. In row 1, look at the pale band above the sea on the left. In row 2, the far snow and sea are greyer.
  - Single frames are in `Screenshots/121/run3/<pose>_sky_<time>[_old].png`.
