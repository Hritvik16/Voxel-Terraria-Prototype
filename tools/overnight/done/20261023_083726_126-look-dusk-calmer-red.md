# 126-look-dusk-calmer-red

Model: opus

Approval: owner, 2026-10-23, Q77 (doc, inline): "Images on column 2, row 5 is apolyptic red and col 1 row 4 is a little too red but i like
how cinematic all the shots look". Read against `Screenshots/121/high_sky_sheet.png`: the NEW dusk_toward (row 5) is "apocalyptic" red
over the whole frame; the sunset_toward (row 4; he wrote col 1, the new sky is col 2) is "a little too red". Keep the cinematic character
(sun disc, warm golden hour, glowing sunset) -- he likes it. Look task (rule 10): tests + stills, no timing.

## Change
Tone down ONLY the red: `SunCycle.LookSkyRimAt` (strength/colour) and the halo floor in `LookAtmosphere` (`Raymarch.compute`).
- Dusk (sun about -3 deg): clearly calmer than now -- a deep red-brown / violet glow, not a red wash over the frame; the sky away from
  the sun should read dusk blue-purple. Sunset (2 deg): a little less red, still orange and glowing.
- Golden hour and noon must stay unchanged (pixel-identical or nearly; say the px diff).
- Flag `-skyrimscale F` (default = the new calmer values; `-skyrimscale 1` = task 121's look) so the owner can compare.
Stills: the 121 sheet layout (`-looksky 1`, high pose + walk_open; times noon, golden away/toward, sunset, dusk) with columns
OLD 121 | NEW 126, plus dusk AWAY from the sun. Captions say what to look at. Fast suite once. HANDOFF + `## For the doc`, TASKS.md paragraph.
