# World-gen look plan (task 173, 2026-11-05)

**Question.** Where does today's island (tg2, map v4, seed 42) fall short of "looks fantastic", and which
world-gen changes should come next, in what order? The owner's order is: lighting, then world gen, then visuals
(ROADMAP "CURRENT ORDER").

**This task changed no generator, shader or default.** It added a capture mode (`-lookreview -worldsurvey 1`,
`Assets/Game/PlaygroundCapture.WorldSurvey.cs`) and a sheet and viewer tool (`tools/survey173.py`).

**Rules this plan follows:**
- The design brief's Decided rows (`GAME_DESIGN_BRIEF_SNAPSHOT.md`).
- DESIGN_LARGE_WORLD §2 and §12: biomes are independent of landform, borders come from climate fields with no
  domain warp, no cones or needles, overhangs come only from stamps.
- The memory rule `no-terrain-specific-engineering`. Every candidate below is a rule on data the generator
  already has (slope, altitude fraction, flow, landform depth). Each is checked on several seeds plus an
  adversarial case, never tuned to seed 42's poses.

## The survey

**Viewer:** `Screenshots/173/viewer.html`, opened locally.
- Arrow keys step through the 36 poses.
- Space switches between native 1080 and FSR 3 at 540 rows (play's upscaler). Underground poses also have a third
  arm with the shape light.
- Keys 1-4 zoom to fit / 100 / 200 / 300 %. Drag to pan.
- Each pose's caption is its finding.

**Contact sheets:** `Screenshots/173/survey173_sheet_1..6.png`, native, 2 tiles across.

**Map:** `Screenshots/173/survey173_map.png`. Each record cell is coloured by biome and hill-shaded. White dot =
the camera, red line = its view, number = the pose's number in the viewer and in `survey173.txt`.

**Conditions:**
- Play's flags: tg2, map v4, FSR 3, shells, far band, `-shadowlevel 0 -shadowres full`.
- Noon with the clock paused; the sun is 78 degrees up, so "sun behind the camera" makes little difference.
- Fluid is suspended, the HUD is hidden, the preset is High.
- `-shellworkers 6`: this only fills shells faster and does not change the look.

**Poses** are chosen from the world record (`WorldSurvey.cs` header):
- 18 biome x landform pairs. These are every pair covering at least 1.9 ha, up to 18 of them. That covers
  91 % of the land; 24 smaller pairs are listed in the log but not shot.
- The 5 signature features. There are two canyons; the canyons are shot from inside and from outside.
- 2 coasts, the range from 200 m up, a river valley, the tallest sunlit sheer cliff, the play start (walk and view).
- A cave mouth, its tube, a tunnel 40 m down and a cavern 200 m below sea level.

**Limits:**
- One seed only.
- Each pose waited 30 s for the shells. After that wait, shells s3-s7 were mostly still missing (log), so terrain
  beyond about 300 m is the far band.
- Underground at noon is black in play's lighting. That is correct, since a sealed cave stays dark until a torch.
  So tunnel and cavern also have a capture-only "shape light" arm (`undergroundAmbientFloor` 1.0).

**Logs:**
- `Logs/lookreview_20261105_134936.txt`: all poses.
- `Logs/lookreview_20261105_143742.txt`: reshoot of the features, river, tunnel and cavern after two fixes to the
  capture code (a map-units bug skipped the river; duplicate feature names overwrote the first canyon and mesa).

**Land shares on seed 42:**

| Biome | Share of land |
|---|---|
| Snow | 31 % |
| Desert | 31 % |
| Forest | 17 % |
| Savanna | 8 % |
| Badlands | 8 % |
| Jungle | 4 % |

The largest single pair is snow x river plains, at 14 %.

## Findings

### What already looks good

**Big landforms read from afar.** The mesas, striped buttes, crater lakes, the gorges and the canyon depth all
look like real places.
- `pair_forest_volcanic`: a gorge with a lake and waterfalls. This is the best frame of the survey.
- `pair_desert_volcanic`: a red gorge.
- `feature_mesa-4`: the three tiers read clearly.
- `pair_badlands_rolling-highlands`: a crater lake with a red rim.

**Coast and islets.** `coast_typ` is a sand islet with shallow-water tint. `coast_cliff` is a real sea cliff
over a bay.

**Lake chains in valleys and the far view.** From `river`, `start_walk` and `cave_in`, the view across the island
to coast and islets is convincing.

### What looks weak, by kind (most visible first)

**1. Biome layout: straight diagonal belts.**
- On the map, forest, savanna and badlands run as parallel straight bands NW-SE across the whole island.
- Two consequences show on the ground:
  - The 600 m peak is split by a straight vertical seam, ochre on one side and red on the other
    (`feature_great-peak-0`, `range_200up`).
  - Snow, bright grass and red badlands meet on the same flat ground (`pair_snow_rolling-highlands`,
    `pair_forest_river-plains`).
- The cause is in `ShapeField.Biome6`. The climate is ONE octave of noise at a 15 km / 12 km scale. Across a 6 km
  island that field is close to a linear ramp, so the nearest-climate-centre rule cuts it into straight bands.
- Borders are drawn as a per-voxel random dither a few tens of metres wide. Up close they look like pixel noise,
  not a transition.

**2. Ground cover: speckle and camouflage instead of surfaces.**
- Most frames are a smooth slope covered in a two-tone high-frequency dither: yellow and grey at the start,
  orange and sand on the desert range (`start_walk`, `start_view`, `pair_desert_alpine-range`,
  `pair_savanna_alpine-range`).
- At walking distance it reads as camouflage. At 100 m it reads as TV noise.
- Flat ground is the opposite: a single colour to the horizon (`pair_forest_river-plains` is a lawn,
  `pair_snow_river-plains` a white sheet).
- Cover does not follow the shape of the ground: slope, hollows, ridges and exposure play no part.

**3. Rivers: canals, not rivers.**
- Channels run straight with 45 and 90 degree kinks, the D8 flow path drawn as is (`pair_desert_canyonlands`,
  `pair_snow_high-plateau`, `feature_mesa-4`'s canal grid, `feature_great-canyon-2_out`'s two parallel straight
  rivers).
- Width is constant.
- On flat ground the channel sits between raised levee walls (`pair_snow_river-plains` looks like an aqueduct).
- The water steps down in boxy tiles.
- One segment rises out of the ground like a bent pipe (`pair_badlands_high-plateau`).

**4. Rock faces: no geology.**
- Every sheer face is one smooth grey material: the 167 m cliff, the sea cliff, the breaks and the canyon walls
  (`cliff`, `coast_cliff`, `pair_forest_breaks`, `feature_great-canyon-2`).
- No layers, cracks, ledges or scree.
- Large faces show 8 m checker blotches (map-cell material) and contour-ring moiré where a smooth curved surface
  is stair-stepped in voxels.

**5. No snow on the high ground.**
- The 600 m summit and the range are brown and ochre to the top (`feature_great-peak-0`, `range_200up`).
- The brief's rule "difficulty is readable from a distance: snowy peaks" is not met. Snow exists only where the
  snow BIOME is, mostly near sea level in the north.

**6. Landforms that do not show at walking scale.**
- "Dune sea" under jungle, forest or savanna is a flat lawn with no dunes (`pair_jungle_dune-sea`,
  `pair_forest_dune-sea`, `pair_savanna_dune-sea`).
- "Canyonlands" is a rolling sand sheet with no canyon in view (`pair_desert_canyonlands`).
- "Breaks" under snow is lumpy white (`pair_snow_breaks`).
- Mountains are smooth cones and domes. There are no ridgelines, arêtes or side valleys at the 50-200 m scale
  (`range_200up`).

**7. Stamps that look placed.** All of these are pure functions of the stamp or the slope, not of a place.
- Cap-rock ledge undercuts appear as egg and disc shapes with contour rings and black holes under them
  (`pair_desert_wetland-basin`, `feature_great-canyon-1`).
- The nearest cave mouth sits on a steep slope. There its tube becomes an OPEN TRENCH, not a cave (`cave_in`), and
  the mouth itself shows as a black sliver (`cave_mouth`).
- The geyser basin is a flat white perfect ellipse with round pools (`river`, `pair_jungle_dune-sea`).
- The stone hoop and slab stand alone on bare plains (`pair_snow_high-plateau`, `pair_badlands_high-plateau`).

**8. Underground.**
- The tunnel is a uniform smooth worm tube of one material (`tunnel`, shape-light arm).
- The cavern is a large chamber (56 m free) with a smooth floor showing contour rings, grey rock, and a few red
  ore streaks. It has no columns, stalactites, water or anything to find (`cavern`, shape-light arm).

### Not world gen: handed to lighting and visuals, noted here so they are not lost

- Shaded walls of an open trench are pure black at noon (`cave_in`): no sky ambient in deep narrow cuts. This
  belongs to the lighting stage.
- Water is flat uniform blue, waterfalls are a solid sheet or tube, and a hard colour seam crosses the sea at a
  shell edge (`coast_typ`). These belong to the visuals stage.
- Faint white dashes on near-vertical faces (`pair_forest_breaks`). This needs a render check.

## Candidates, in the proposed order

Effort: S = one task, M = 2-3 tasks, L = a short phase. Every candidate is a generator change, so each runs
`SUITE=all` and a multi-seed map check, per CLAUDE.md. The render cost is the same for all: none at draw time,
because the march does not care what the voxels are.

- Two things can still move: brick memory (more mixed-material bricks) and generation time per chunk.
- Each task records both from runs it already does (no timing arms).
- Candidates 2 and 4 are the ones to watch. The brief notes that thin strata once "made most underground bricks
  expensive".

**C1. Biome layout: organic regions and real transitions (S-M).**
- *Fixes:* finding 1, the straight belts, the vertical seam on the peak, and the dithered borders.
  Picture: `survey173_map.png` and `feature_great-peak-0`.
- *How:*
  - Make the climate field vary across the island, not just ramp. Options: its scale relative to the island
    radius, or a second octave at low amplitude. No domain warp, which stays the owner's rule.
  - Replace the per-voxel dither with a transition band whose cover blends by patch, not by pixel.
- *General:* acceptance is a measured map statistic on 8 seeds, not a look at seed 42:
  - no border runs straight for more than about 1.5 km;
  - every biome is in 2 or more separate regions;
  - land shares stay within the brief's targets (easy land, 6 biomes).
- *Note:* this moves biome borders on every seed. Saves are deltas on top of the generator, so they would shift.

**C2. Ground cover from the ground's shape (S-M).**
- *Fixes:* finding 2, the speckle, the camouflage, and lawns to the horizon.
  Pictures: `start_view`, `pair_desert_alpine-range`, `pair_forest_river-plains`.
- *How:* choose the cover from slope, curvature (hollow / ridge), height above the local valley and moisture
  (freshwater distance in the record). Draw it in coherent patches of metres, not per voxel:
  - grass on flats;
  - bare rock and scree on steep ground and ridges;
  - sand and gravel in hollows and beside water;
  - dry grass on sunny slopes.
- *General:* all of these inputs exist for every column of every seed. The test is the per-biome cover share on
  4 seeds.
- *Cost risk:* fewer per-voxel mixes means fewer mixed bricks. This is likely cheaper; it is recorded either way.

**C3. A snow line on high ground (S).**
- *Fixes:* finding 5. Pictures: `feature_great-peak-0`, `range_200up`.
- *How:* a cover rule only, applied above an altitude that is a FRACTION of the world's own height range (the
  brief already measures "low altitude" this way). Snow on flats and in hollows; steep faces stay bare. The
  biome map itself does not change.
- *General:* a fraction of the world's own range, so it scales with world size. No constant tuned to the 600 m
  peak.
- *Owner question:* is this compatible with "biomes are independent of topography"? It does not change the
  biome; it is cover, like snow on any mountain.

**C4. Cliffs and faces with geology (M).**
- *Fixes:* finding 4. Pictures: `cliff`, `coast_cliff`, `pair_forest_breaks`.
- *How:* on steep faces, show the biome's own depth layers as the brief decided: soil over stone, a few thick
  layers with wavy boundaries. Add face relief from the detail field on Sheer cells: vertical fluting, horizontal
  ledges every few metres, scree at the foot.
- *General:* driven by slope and depth.
- *Owner question:* does the brief's open row "whether the badlands biome keeps striped rock" become yes?
- *Cost risk:* more mixed bricks on faces. Measured from the task's own runs.

**C5. Rivers that meander and carve (M-L).**
- *Fixes:* finding 3. Pictures: `pair_snow_river-plains`, `feature_mesa-4`, `pair_desert_canyonlands`.
- *How:*
  - Smooth the D8 centre line into a curve. Add meanders whose wavelength scales with the river's own size
    (`Rivers.FLOW_SIZE`), and a width that grows downstream.
  - Carve banks into the ground instead of raising levees.
  - Let the water surface follow the bed's gradient in small steps.
- *General:* from the flow graph of any seed. The adversarial cases are a river crossing a canyon floor, a lake
  outlet, and a river crossing a biome border.
- *Note:* rivers are "never at rest" (brief). This changes the shape only, not the fluid simulation.

**C6. Landform signatures at walking scale (M-L).**
- *Fixes:* finding 6. Pictures: `pair_jungle_dune-sea`, `pair_desert_canyonlands`, `range_200up`.
- *How:* give each landform a meso shape that does not depend on the biome:
  - dunes: directional ridges with a steep lee side, under grass as well as under sand;
  - canyonlands: slot canyons and benches;
  - alpine: ridgelines and side valleys from slope-following detail.
- *Guard:* the no-needle guard stays (`StampTests.NoNeedles_OnTheMap`).
- *General:* a per-landform rule, checked by a shape statistic per landform on 4 seeds, for example the relief
  spectrum at 20-200 m.

**C7. Stamps that sit in the land (M).**
- *Fixes:* finding 7. Pictures: `cave_in`, `pair_desert_wetland-basin`, `river`.
- *How:*
  - A cave mouth is placed only where its tube stays covered along its length. This is tested on the column
    heights, so a steep slope gives a cave and not a trench.
  - Ledges get a flat rock ceiling and lip, so no egg shapes or contour rings.
  - The geyser basin's outline follows the ground (terraces down the slope).
  - Lone hoops and slabs need a context rule: they sit in rock or on a rise, not on a bare plain.
- *General:* each is a test on the stamp's own footprint.

**C8. Underground variety (L; the owner parked underground design, so this is LAST and needs a yes first).**
- *Fixes:* finding 8. Pictures: `tunnel` and `cavern`, shape-light arm.
- *How:* varied tunnel cross-sections, columns and stalactites in caverns, floor water and the brief's fluid
  pockets in sight, layer materials showing on cave walls.

**Recommended order:** C1, C2 and C3 first. They are cheap, visible in every frame and change no shapes.
Then C4, C5, C6, C7. C8 waits for an underground decision.
