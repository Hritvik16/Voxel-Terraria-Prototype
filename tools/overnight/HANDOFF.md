# 210-look-trees-space-colonization-world: DONE, default OFF (`-trees2 1` turns it on; the owner decides after the pictures). Memory budget met: refusals DOWN, peak not up.

## What it is (efficient by construction, rule 11)
- Trees are INSTANCES of a shared library: task 200's method (a), grown ONCE per process on worker threads.
  - 30 prototypes (oak, birch, spruce, acacia x 6; banyan, redwood x 3; variants 1-3 = task 200's sheets), 10.6 MB, 2 bits a voxel, 550-695 ms on 6 threads (player).
  - Chunks, shells and the point query all read the same prototype. Nothing is stored per tree and nothing is rebuilt per chunk.
  - Roots snap to multiples of 8 voxels, so the bricks of every copy of a prototype are byte-identical wherever they don't touch terrain. Byte duplicates in the GPU box: forest_eye 50.7 k vs today's 21.8 k. That is the room a dedup pool would get back.
- Brick budget per area: today's trees' expected bricks per 51.2 m square (forest 13,619; snow 3,191 for spruce; savanna 1,872 for acacia).
  - Each species' probability = budget / (the prototypes' MEASURED mean bricks). A bigger tree is rarer, never costlier per area.
  - Forest at full plane: 3.2 oaks + 11.9 birches per 51.2 m square (`trees210/library.txt`). Giants on a 102.4 m grid: banyan (jungle, p 0.30), groves of 3-5 69 m redwoods (wet forest, p 0.35).
- 192's trees kept: desert cactus, snow pines (6/16), jungle emergents and palms (14/16; the 2/16 pay for the banyans), savanna baobabs. None in forest.
- Birch bark is a real GenMat (`BirchBark` = 19, engine 147). Placement is deterministic (one suppression level per tier).
- Commit ef6f12f. Code: `Assets/TerrainGen/Trees2.cs`, `Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Trees2.cs` + hooks in `.Trees.cs`; note in TASKS.md "Task 210".

## Evidence
- Tests:
  - `Trees2Tests.Library_Turns_Budget`: builds are byte-equal; quarter turns map bricks to bricks; bounds hold; every biome stays within its budget.
  - `Chunks_Deterministic_Border_PointAndShellsAgree`: an oak across a chunk border. Runs are equal and only air turns into tree. 126,447 FarQuery points match, and shells 0-3 match (`Logs/TerrainGen/trees210/chunks.txt`).
  - Whole suite: SUITE=all PASS 1127 FAIL 0 SKIP 31 (`Logs/overnight/210/all.out`; 209: 1125), incl. `TodayTrees_Pinned` (off = today byte for byte).
- Memory (one build, same poses, -trees2 1 vs -trees2 0; `Screenshots/210/table210.txt`, `table210_final.txt`):
  - 20 poses: peak 2,485.3 vs 2,779.2 MB; GPU refusals 1,034 vs 1,470; GPU pool peak 254,400 in both (jungle box FULL with today's trees too); CPU pool peak 495,162 vs 495,117 (valve 484,500).
  - final code, 4 poses: peak 2,565.6 vs 2,570.6 MB; refusals 678 vs 1,127; per pose jungle_eye 275 vs 476, jungle_100m 287 vs 502, banyan_near 116 vs 149 (was 110 vs 85 before jungle 14/16).
  - No pool made bigger. Valve eviction counts print only with `-mirrorprobe` (not run).
- GPU tree bodies: forest_eye 68.9 k vs 112.8 k, forest_100m 78.4 k vs 125.9 k, snow 51.7 k vs 62.1 k, savanna 17.3 k vs 14.9 k, redwood_near 98.2 k vs 111.3 k, jungle 150 k vs 157 k.
- Rough cost (no timing arms), average shell chunk generation per launch: 100 vs 117 ms (main pair), 167 vs 120 ms (final pair). Launch order / chip heat dominates; no clear difference.
- Pictures I looked at:
  - forest: oaks, pale birches, a redwood grove behind (today: dense uniform canopy); jungle: as today + a banyan hidden in the canopy; savanna: acacias + baobabs; snow: spruces in front, 192's pines behind; desert: cacti (pose at a town on the forest edge).
  - giants: the grove is clear at 300 m (`redwood_far1_t2.png`). At 1.5 km, once shells 3-4 have streamed (~3 min after a teleport), it shows as thin dark spikes beside the white stone arches (`final_redwood_far1500_farwait_t2.png`). With 40 s waits, shells 3-5 were empty and the far band (no trees) drew the ground, so the giants pop in with their shell chunk, like every tree.
  - Broken shots: `redwood_far2` is black (the camera was inside a hill). `redwood_far3` / `banyan_far2` look behind a plateau.

## Left / owner calls
- Turning it on is the owner's call.
- Forest density is capped by the 12.8 m grid, not by memory: 95 % of full-plane forest cells already hold a tree, and the forest uses 61 % of today's tree bodies. Its look is an open woodland with clearings from above and a forest at eye height.
  - Denser = a 6.4 m grid for birches (code), or a bigger oak share.
  - There is ~30 k bodies of room at forest_100m before the pool fills.
- The banyan does not stand out inside the dense 192 jungle (emergents are 11-20 m, the banyan 21 m).
- NOT done: the desert's "oasis palm near water" (Q-AD2). A follow-up needs a water-distance rule.
- Birch leaves keep task 200's `GenMat.Leaves` (88, the look the owner saw); it is not in `IsTreeLeaf`.

## For the doc
- Memory, -trees2 1 vs today (same build, same poses):
  - peak 2,485 vs 2,779 MB (20 poses), 2,566 vs 2,571 MB (final 4); GPU refusals 1,034 vs 1,470 and 678 vs 1,127; no pool bigger (`Screenshots/210/table210*.txt`).
- Forest GPU tree bodies 68.9 k vs 112.8 k (forest_eye): fewer, bigger trees. The library is 10.6 MB for the whole world, built in 0.55-0.7 s on 6 worker threads.
- Density per 51.2 m square at full plane: forest 3.2 oaks + 11.9 birches; snow 3.7 spruces; savanna 3.4 acacias. Giants: a banyan per ~3 jungle hectares; redwood groves of 3-5 in wet forest.
- Images (full-size flip viewer: `Screenshots/210/viewer.html`, Space = new / today at the same camera):
  - `Screenshots/210/sheet_biomes.jpg`: left column = new trees, right = today. Rows 1-2 forest: oaks (broad dark crowns), birches (pale stems) and three 69 m redwoods at the back, against today's even canopy. Rows 5-6 savanna: acacias plus bottle-trunk baobabs.
  - `Screenshots/210/forest_eye_t2.png`: eye height in the forest. Two-stemmed birches on the left, oaks on the right, a redwood grove behind the meadow.
  - `Screenshots/210/final_redwood_far1500_farwait_t2.png`: 1.5 km away, shells streamed. The grove shows as dark spikes just right of the white stone arches in the centre.
