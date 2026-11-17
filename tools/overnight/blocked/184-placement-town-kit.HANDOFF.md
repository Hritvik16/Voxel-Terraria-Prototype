STOP_FOR_OWNER: there are NO town / village sites to place buildings on. A site planner (surface-rebuild step B7) is needed first. Which way?
- **(a) RECOMMENDED: queue B7 as its own task.** Pick about 3 towns on the easy areas, about 10 villages / outposts on Walk cells near fresh water along the routes, and at least 1 frontier town at the foot of the volcano or the snow line. Store them in the world record (site ids >= 16 + a SiteList). Then 184b places this kit there behind `-towns 1`.
- **(b) Shortcut:** a small town at each easy area's centre only (4 on a medium island). That means no villages and no frontier town yet. B7 would replace it later.
- **(c) Hold** town placement until the placement-phase game-design doc.

# 184-placement-town-kit: KIT DONE, placement NOT done (no sites). Commit 2aacfba. Nothing in the world changed.

## What I found (site plan)
- `WorldRecord.cs` keeps the `site` byte ids >= 16 "for B7-B8", but B7 (towns) was never built.
  - Routes.cs line 6: "B7 (towns) is not done, so routes join EASY AREAS only".
  - ROADMAP.md: "B7 (towns) and B9 are not queued". SUPERVISOR.md: "Do NOT queue B7".
- The record holds only EasyAreaList (4 on medium), FeatureList and RouteList. No town, village or outpost is reserved anywhere.
- Picking village and frontier sites myself would be the new site planner the task says not to invent.

## What changed (2aacfba)
- New `Assets/TerrainGen/BuildingKit.cs` (its header has the rules). The parts:
  - stone foundation to the ground (max 4 m; deeper is stage 2) and a floor
  - 0.2 m walls, 3x3 corner posts
  - a door gap 1 x 2.1 m from the floor, windows with a 1 m sill, wood frames
  - a 45 deg gable roof with eaves, and gable walls
- The 3 shapes: HUT 4x4 m (1 room); HOUSE 9x5 m (2 rooms + an inner door); HALL 13x7 m (double door, wall posts).
- How it works: a building is a pure integer function (shape, rot, biome, origin, floor y, foundation depth) -> part -> GenMat. It is ready for the Trees.cs overlay (ColumnJob / ShellColumnJob / PointVoxel), so LOD 0, the shells and FarQuery would agree by construction.
- Style by biome rule, existing GenMats only (`Screenshots/184/kit.txt` has the table):
  - wood: the biome's tree wood
  - stone: the biome's own rock
  - walls: timber (forest / snow / jungle huts and houses), adobe RedEarth (savanna), stone (desert, badlands, every hall)
  - roofs: thatch SavannaGrass, Slate / BlueSlate shingle, MaroonMudstone tiles
- `BuildingFootprint` (an integer box + id) and an EMPTY `WorldRecord.BuildingList`. The record is never saved, so filling the list later is no save-format change. No frozen API is touched.
- No `-towns` flag yet: there is nothing to switch on. TASKS.md has the paragraph.

## Evidence
- Whole suite **PASS 1104 FAIL 0 SKIP 35** (`Logs/overnight/184_all.out`). It includes the 6 new `BuildingKitTests`, on every shape (rotations and biomes where they matter):
  - the room is sealed with the openings shut, and leaks with them open (negative control)
  - a 0.6 x 1.8 m player walks from outside through the front door into every room
  - the player never gets in through a window, nor into HOUSE room B with the inner door shut
  - a roof covers every column, and no voxel floats
  - all 4 rotations give equal part counts; the footprint box is tight; the door faces FrontDir
  - every material is solid, static, coloured and GenToEngine-mapped
- Rough cost (solid voxels per building, `Screenshots/184/kit.txt`): hut 17k, house 42k, hall 79k.
  - Memory per TOWN is not measurable until buildings are placed. The brief's estimate is 20-80 MB while near one.

## Pictures (looked at)
- `Screenshots/184/kit_sheet.png`: rows hut / house / hall; columns forest, snow, jungle, savanna, desert, badlands.
  - Look at the right-hand (front) wall of each building: that is where the door sits.
  - Snow's BlueSlate roofs read very blue, and the desert and badlands columns look almost the same (CreamSandstone vs CreamBand).
- `Screenshots/184/kit_cutaway.png`: roofs removed; forest left, desert right.
  - Middle row: the HOUSE's two rooms joined by the inner door.
  - Bottom row: the HALL's double door and wall posts.
- Not taken: the in-world ON/OFF shots (village from eye height, town from 100 m, interior doorway). There is nothing placed to shoot.

## Left / next
- After the owner's answer: B7 (or option b), then 184b places the kit behind `-towns 1`. 184b's checks:
  - the floor at the site's ground; the inside carved to air
  - each footprint added to BuildingList
  - 4 seeds + a steep adversarial site; LOD 0 / shells / far band agree; ON/OFF shots
- Look follow-ups for the owner, if wanted:
  - the snow roof colour
  - a stronger desert vs badlands difference
  - timber posts in timber walls don't show (same wood)

## For the doc
- Building parts **0 -> 8** (foundation, floor, wall, post, door gap, window, frame, roof). House shapes **0 -> 3**.
- Solid voxels per building: hut **17,324**, house **42,116**, hall **78,924** (`Screenshots/184/kit.txt`).
- Buildings placed in the world: **0**. No town sites exist (B7 not built). STOP_FOR_OWNER.
- Whole suite **PASS 1104 FAIL 0** (`Logs/overnight/184_all.out`).
- Images:
  - `Screenshots/184/kit_sheet.png`: the kit in all 6 biome styles (rows hut / house / hall).
  - `Screenshots/184/kit_cutaway.png`: roofless views; middle row shows the HOUSE's inner door joining its two rooms.
