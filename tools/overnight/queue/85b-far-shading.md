# 85b-far-shading

Approval: owner answered **Q48 (a)** in chat, 2026-10-12 23:xx, adding: "we'd have to take care of torches too. Night
gameplay will show a lot of points of interest via lighting so we need to plan with that in mind. Of course object
placement is when we actually plan how we populate the world." Queued by the supervisor. Branch `engine-phase`.
BUGS #9. Runs after 85 (near-first shells), before 86.

## The problem (owner screenshot `Screenshots/owner/2026-10-12_far_specks.png`, 83b's poses run, `-shells 1`)
Far terrain reads as SPECKS of colour, not shapes with depth. Same look with shells off (`Screenshots/83/cliff_foot_*`).
Causes (supervisor's reading of the stills; confirm or correct): (1) faces turned from the sun are near-black: no sky /
ambient term worth the name; (2) at ~1 voxel per pixel (shells start at 1 px) a slope becomes lit tops + black sides, a dot
pattern; (3) no distance haze. FSR 3 in play smooths part of (2), not (1).

## Change (one look change; read Phase 7 / AMENDMENT_8_12 and KNOWN_ISSUES "EMISSIVE + MOBILE MATERIALS" first)
1. **Sky light:** faces away from the sun get a sky term (sky colour x an openness/occlusion factor you can afford: e.g.
   the voxel's up-facing exposure, or the existing AO if there is one). It FOLLOWS TIME OF DAY: at night it falls to a dim
   moonlight level, so night stays dark and lights stand out (owner: night shows points of interest by their light).
2. **Slope shading at distance:** beyond a blend distance (~100 m on High, or where a voxel is <= ~2 px; state it), light a
   voxel with a smoothed normal (the ground slope around it, e.g. from the column heights / neighbour occupancy) instead of
   its cube face, blended in so there is no visible ring. Applies in the shells, the cascade tiers and the far band alike.
3. **Distance haze (aerial perspective):** light haze toward the sky/horizon colour with distance, by time of day (no bright
   haze at night). **Lights must not be swallowed by haze**: emissive voxels (lava, glowing ores, crystals) and torch-lit
   surfaces keep their light at distance (haze attenuates them far less than lit terrain). Don't build far lights (engine
   step 21) here, but write in HANDOFF how step 21's far lights will plug into this shading so night points of interest
   show across the island.
4. **Torches (owner: "we'd have to take care of torches too"):** point lights go through the same new shading (smoothed
   normal at distance, sky term underneath, haze rule above), so a torch-lit wall at 50-150 m still reads as lit. Do NOT
   fix torch-through-walls (BUGS #4 / engine step 2) here; say whether this change makes it worse or better.
5. One switch for the whole change (e.g. `-farshade 0|1`, F6 row if cheap): ON in play, OFF in rigs/capture runs unless
   passed, so rigs stay byte-identical (like task 75's `-seethrough`).

## Acceptance
- Stills, ONE player launch, each OFF/ON (well lit: midday with the sun BEHIND the camera, plus the owner's backlit case):
  the owner's pose (find it: valley + peak, 83b poses run around 22:20; nearest `cliff_foot_*` / `eye_level_*` otherwise),
  glide_400m, peak_from_2km, oblique_3000m; raw AND with `-upscaler fsr3`. NIGHT set: the same 2 poses at night (`-tod`)
  with a few torches placed near (10-30 m) and far (100-300 m) and the volcano/lava in view: lights visible, haze does not
  wash them out, night stays dark. Contact sheets with captions saying what to look at. Look at every sheet.
- Fast EditMode while iterating, `SUITE=all` once at the end. `run-phase7-rig.sh` unchanged with the switch off (byte-identical
  shots) and still green with it on (report its gates; do not loosen any).
- No frame-time arms (PREAMBLE rule 10); say in one line which passes the shading adds.
- HANDOFF `## For the doc`: the sheets (day OFF/ON, night OFF/ON), what changed, the step-21 plug-in note.
