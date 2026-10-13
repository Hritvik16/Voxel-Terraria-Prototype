# 85b-far-shading: DONE. Far terrain is shaded, not specked: sky light, slope normal at distance, haze lights escape (BUGS #9, Q48 (a)).
## What changed (commit `bd98d9d`, branch engine-phase)
- `Raymarch.compute`: a new kernel **`CSMainFarShade`** (`VE_FAR_SHADE`). Every 85b line is under `#ifdef`, so CSMain, CSMainChecker and CSMainDetail are the pre-task code.
  (My first version used runtime `if`s and was NOT byte-identical: 1-LSB diffs in 8 of 19 Phase 7 shots.) Look 1 path; near, tiers 1-2, shells and the far band alike:
  1. Sky light, `FarShadeSky`: the flat ambient (0.3 x horizon) becomes the sky colour the face looks at (horizon .. half-way to the zenith)
     x (1 + n.y) / 2 x 1.1, still x the sky probe and VAO. It follows the time of day; at night it is BELOW the old night ambient.
  2. Slope normal, `FarSlopeNormal`: from the far band's column heights (bilinear over 4 texels, at the finest level whose texel is >= 2 px).
     It blends in from where a 0.1 m voxel is 2 px to where it is 1 px. At 720 rows that is **34 m -> 69 m** (it follows the render rows).
     Skipped in caves and under overhangs (the hit is > 1 texel off the column tops), on fluids and on lava. Sun, moon, sky and torches all use it.
  3. Haze, `FarShadeHaze`: 1 - exp(-d / 3.5 km), thinned over a 1.5 km scale height, toward the sky behind the pixel (dark at night).
     Light (`lightAdd`: torches, emission, bleed, far lava) takes only 25% of the haze. Without a far band, the old range fog is kept as a floor (it hides the world's edge).
- `RaymarchFeature`: `FarShade` (also reads `-farshade 1` on any player) picks the kernel. The checkerboard and the `-detailspeed` square keep theirs (no far shading then).
  Constants: `FarShadeSkyStrength` 1.1, `FarShadeHazeRangeM` 3500, `FarShadeHazeScaleHeightM` 1500, `FarShadeLightHaze` 0.25.
- `PlaySettings.farShade` (on; an old file gets on), `-farshade 0|1`, F6 row "Far shading". The F6 panel now scales for 1010 rows (it was 930 tall in 900).
- Capture `-farpreview -farshadeshots` (`PlaygroundCapture.FarShade.cs`): day lit/backlit + night torches/lava, OFF/ON, raw/FSR 3; sheets `tools/farshade85b-sheet.py`.
- Passes added: none. Inside the primary kernel, a hit past the blend start does a level loop + 4 FarTexels reads, one extra `UniformFaceHue` and the haze exp. No new rays.
## Evidence
- Whole suite **PASS 768 FAIL 0 SKIP 21** (Logs/overnight/85b-all.out; 767 + `FarShade_OldFileGetsOn_FlagOverridesForTheRun`).
- run-phase7-rig, switch OFF: **PASS 37 FAIL 0, 19/19 shots byte-identical** to task 85's (Phase7Cycle/2026-10-13_020910 vs _005521).
  Switch ON (`EXTRA="-farshade 1"`): **PASS 37 FAIL 0** (Phase7Cycle/2026-10-13_021118), 10/19 shots change look.
  The cave-lit-by-torch shot is unchanged in practice (the cave stays dark). In golden hour the slopes are smoother and shadows lighter.
- ONE player launch for the sheets (the 2nd: the 1st, Screenshots/85b/run1, had sky x1.3, the owner pose's frame was a wall, and the lava sat at 80 m; ~8 min).
  `Logs/overnight/85b-shots2.out`, `Screenshots/85b/farpreview.txt`; peak memory 2,529 MB. Shells off (the play default), waits 7-38 s, deficit 0 at every pose.
  CAVEAT: these shots came from the runtime-`if` build. The final kernel build has the same math; I did not reshoot.
- Near-black share (luma < 20/255), OFF -> ON, raw (Screenshots/85b/farshade85b_stats.txt): **eye_level_3 backlit noon 18.8% -> 0.0%**, lit 9.6% -> 0.0%,
  glide backlit 6.8% -> 0.0%, peak lit 2.2% -> 0.0%. Night stays dark: mean luma eye_level_3 14 -> 18, peak_from_2km 18 -> 20 (of 255).
- Sheets I looked at:
  - `Screenshots/85b/sheet_day_raw.jpg`: rows = pose x (lit, backlit); columns OFF | ON.
    eye_level_3 backlit (the owner's case): the black, speckled peak becomes shaped brown slopes with the strata readable.
    glide_400m backlit: the black peak becomes brown. oblique_3000m: the far sea and island haze toward the sky; a bit milky.
    peak_from_2km: snow tops get brighter.
  - `Screenshots/85b/sheet_day_fsr3.jpg`: the same through FSR 3 at 720 rows (FSR 3 context confirmed in Player.log); it matches raw.
  - `Screenshots/85b/sheet_night.jpg`: 0.5 h, OFF | ON. Night stays dark. Torches 12-28 m are bright points, and the 60-150 m ones are orange glows on the slope (eye_level_3 rows).
    ON lifts the moonlit terrain slightly (the slopes lose their black step lines).
## Found / left
- **Owner pose:** `eye_level_1` now shows a hillside filling the view (83b showed a valley + peak, 85 a plain with the sea), with the same map hash and eye. BUGS #10 (new).
  I used `eye_level_3`, the same dark-speckled valley + peak look, as the owner's case.
- **Lava:** seed 42 has no volcano. A placed 5 m lava pool (1,961 voxels, lava lights registered) is not drawn, in OFF or ON. BUGS #11 (new).
- **Far torches beyond ~120-150 m cannot exist yet**: EditService refuses non-resident chunks, and lights register only there (farpreview.txt lists each spot).
- **Torch-through-walls (BUGS #4):** neither better nor worse. Occlusion rays are untouched. Tier >= 1 hits still use the unshadowed sum (as before).
  The haze now keeps light, but point lights reach 8 m, at <= ~70 m where haze is ~2%.
- **Step 21 (far lights) plugs in at `lightAdd`.** Whatever a far light adds to a far pixel (far band / shell hit), add it to both `lit` and `lightAdd`. `FarShadeApplyHaze` then keeps 1 - 0.25 x haze of it.
  The night sky term stays low, so points of interest read across the island. The far-band lava path already does this (`lightAddF`).
## Next step
Owner: look at the three sheets. Possible tuning: sky 1.1, haze 3.5 km, snow brightness. Then 86 as queued.

## For the doc
- Near-black pixels at the owner's case (eye_level_3, noon, sun ahead): **18.8% -> 0.0%**; sun-behind 9.6% -> 0.0%; glide backlit 6.8% -> 0.0% (Screenshots/85b/farshade85b_stats.txt).
- Night stays dark: mean luma 14 -> 18 (eye_level_3) and 18 -> 20 (peak_from_2km) of 255; torches at 12-150 m stay visible.
- Slope-normal blend 34 -> 69 m at 720 rows (a 0.1 m voxel 2 px -> 1 px); haze 3.5 km, 1.5 km scale height; light keeps 75% of what haze takes.
- OFF is byte-identical: Phase 7 rig 19/19 shots; PASS 37/0 off and on; whole suite 768/0/21.
- `Screenshots/85b/sheet_day_raw.jpg`: rows = poses (lit, backlit), OFF | ON. Look at eye_level_3 backlit: a black speckled peak (OFF) becomes shaped slopes (ON).
- `Screenshots/85b/sheet_night.jpg`: 0.5 h, OFF | ON. Night stays dark, and the torch glows (near points, orange patches at 60-150 m) are visible in both.
