# 209b-look-pond-streams-at-rest: DONE. Ponds get still streams built like the rivers (every fluid but quicksand), DEFAULT ON; `-pocketstreams 0` = task 209's world byte for byte.

## What changed (commit 63ea792)
- New `Assets/TerrainGen/PocketStreams.cs` (204's option A). Per pond, on the 8 m map:
  - INLET: upstream along the cells that drain into the pond (D8, largest catchment). It stops at a keep-out cell, a rise over 2 m per cell (a spring at the cliff foot) or 6-16 cells. If nothing drains in: a one-cell spring.
  - OUTLET: only if catchment area >= 16 x the pond's area (a simple water balance). It goes downhill to a river (joins at the river's level) or ends sealed.
  - SEEP: a pond with no outlet gets a lining-material trail from its low rim.
- Channel: Rivers' shaped section at stream scale, 0.6-1.2 m wide (by catchment) and 0.25-0.35 m deep. The course uses Rivers' B-spline plus Rivers' meander rule at the stream's own scale.
- Levels: 2 voxels under the pond-shaped ground, never rising downstream, and flush AT the pond level near the shore. Banks, bank raise and LIP rule are Rivers' own.
- Lips: water lips are RiverLip (drawn as water). Lips of the other fluids are lining-coloured, so 0 new ids. Non-water channels are lined.
- Seals: a stream never carves under a pond rim, a river's hold or a lake rim, and never touches a column that already holds water. Streams exist at texel 1 only (LOD 0, shells, FarQuery). The far band does not draw them.
- Storage: one section appended to the pond site plane (`MapView.stream`), so all 6 generator paths and WorldMap.View carry it. Hook: `ShapeField.Sample`, right after `FluidPockets.Apply`.
- `-pocketstreams` and `StreamTestOverride` live in FluidPockets. The pond tests now run streams-off (OneTimeSetUp). `TASKS.md` has the Task 209b paragraph.

## Why this design is the efficient one (rule 11)
- Nothing simulates: 0 fluid tiles and 0 tick cost (204: a flowing stream would cost 20-40 MB and slow the fluid tick).
- The streams are built once per world: 177 ms in the Editor, 20 ms in the player.
- Storage is 63.9 KB per plane copy, under 204's ~128 KB budget. Nodes are 12 bytes each, behind a 204.8 m index grid.
- A column far from any stream reads 2 ints and returns. The full channel test only runs within ~2.6 m of a stream.

## Evidence
- Tests: `SUITE=all PASS 1129 FAIL 0 SKIP 32` (34m45s, `Logs/overnight/209b/all.out`). Report: `Logs/TerrainGen/streams209b/streams.txt`.
- Seed 42 has 131 ponds. They got 104 inlets (28 of them one-cell springs), 35 outlets and 71 seeps: 4,293 nodes, 5.35 km of channel.
  - Streams per fluid: lava 51, acid 25, water 24, oil 21, honey 17, sludge 1.
- Every stream column, channel + 2 rings (578 k columns, 336 k holding fluid, 57 k lips): 0 breaches, 0 mixed fluids, 0 unlined faces.
- 12 CPU fluid-oracle cases (shore + middle of each fluid's longest stream), 150 ticks: nothing moved or reacted.
  - Every negative control (one wall voxel removed) was caught.
  - ON vs OFF changes stay inside the stream's reach. FarQuery = chunk, and shell 0 = Generate.
- OFF plane = 209's FNV 38AF614C8AECA569. Streams-off chunks equal a fresh `-pocketstreams 0` source's.
- Memory, same build and 9 poses, OFF -> ON (`Logs/overnight/209b/look_{before,after}.out`):
  - process peak 2,585.3 -> 2,477.6 MB
  - GPU body pool peak 254,400 -> 254,400, refusals 2,493 -> 2,316
  - CPU pool peak 493,453 -> 494,244 bricks (valve 484,500)
  - Valve evictions are not logged in this mode. Within budget.
- Pictures: I looked at all 9 pairs. Streams read as small winding rivers in oil / honey / water / lava. Acid's outlet sits in a cut gully. Sludge has a short inlet and a mud seep.

## Unfinished / caveats
- No outlet reached a river on seed 42 (0 river cells reached), so the river-join code has no real case. It is guarded: it only joins at or below the pond level.
- Player vs Editor plane: identical counts and size, but the hashes differ (player C5DE7646D4DF6CA9, Editor CACEE583F814B040).
  - Per field: z, width and depth match; x and level differ. So a few nodes round 1 voxel apart.
  - Each world is still at rest by construction. An integer course would make them byte-identical.
- Distant stretches draw as dashes in the shells: shell LOD samples a 1 m channel 1 voxel per 2^k columns.
- From 70 m up a water stream reads as a dark line (shallow water over a mud bed).
- No river-style ripples or foam: the flow map is not free (stretch goal, not built).
- The shot launches ran 38 + 34 min (9 poses x 35 s plus load), over the ~10 min budget. A third, short launch only checked the plane hash.
- The `water_middle` pose sits inside a tree crown in both arms.

## Next step
- Possible follow-ups (supervisor's call): an integer stream course (player == Editor plane); a seed with an outlet that reaches a river, to show the join.

## For the doc
- Streams added (seed 42): 0 -> 104 inlets, 35 outlets, 71 seeps; 5.35 km of channel on 131 ponds (quicksand none) (`Logs/TerrainGen/streams209b/streams.txt`).
- At rest: 578 k stream columns checked: 0 breaches, 0 unlined; 12 / 12 oracle cases still; 0 fluid tiles, 0 tick cost.
- Memory OFF -> ON: peak 2,585 -> 2,478 MB; GPU refusals 2,493 -> 2,316; plane +63.9 KB; build 20 ms in the player.
- Images (`Screenshots/209b/viewer.html`: Space flips ON / OFF at the same camera, Z = full size):
  - `Screenshots/209b/sheet_a.jpg`: rows lava shore / lava middle / oil shore, left OFF, right ON. Look at the winding lava and oil channels leaving and entering each pond.
  - `Screenshots/209b/sheet_b.jpg`: rows acid / honey / sludge. Acid: the outlet in a cut gully at left. Honey: inlet right, outlet left. Sludge: a short inlet and the mud seep strip at left.
  - `Screenshots/209b/aerial_water_after.png`: 70 m up over a forest. The inlet winds down the clearing (top to bottom centre) into the small pond at the bottom.
