# 181-placement-resource-deposits: DONE behind `-ores 1` (default OFF). Code a7a7bfb. No frozen-API / save change; pictures look right.

## What changed
- `Assets/TerrainGen/OreRules.cs` (new; the header lists the rules). Step 3's ladder and host layers are unchanged. Every input is generator data; no seed constant.
  - Depth: `Ores.BaseChance`, rarer deeper.
  - Biome x landform: `PairMul`, scaled by HARSHNESS = the Danger biome tier + the landform class (0-4).
    - Copper and gold favour easy and mild land.
    - Cobalt only at harshness >= 3; emberstone only at 4.
  - Wall veins: thin tilted lenses in every face where the column step is > 1.5 (56 deg). Badlands x3. None on route cells or below nearby water.
  - Rich: x2 on signature-feature cells.
- **R1 (b), the start.** On the cells of either play start's easy area, plus 128 m round either start column:
  - no veins;
  - deposits only deeper than 6 m + step + 1, in columns with no opening.
- **R2 (a), latent ores.** Cobalt and emberstone are written as a TWIN material per host rock (GenMat 26-29, 38, 39, 59, 94, 95). A twin's palette row is its host's colour.
  - `Assets/ContentModules/LatentOres.cs` resolves a twin to its host until `world.state`'s stage reaches 1 (cobalt) / 2 (emberstone), then to the ore.
  - The resolve is used by `Tools` (hardness, tier), `Items` (drop, name), `MaterialPalette` and the shader (`LatentResolve` on the `_WorldStage` global).
  - `StreamManager` calls `LatentOres.SetStage` on world load. Nothing is rewritten. Unexercised in play until row S1 flips the stage.
- Plumbing: `TerrainGenChunkSource.Ores.cs`, `MapView.oreCell` -> `ColumnDetail.oreCell` (chunks, shells, FarQuery). Brick-skip proofs are extended.
- Not drawn: the far band and DevMap / the ore and tool demos show no rule ore. TASKS.md has the paragraph.

## Evidence
- **Whole suite PASS 1093 FAIL 0 SKIP 35** (`Logs/overnight/181_all.out`, 70 min). The fast suite is a subset of it.
- **Sites, seed 42** (`Screenshots/181/sites.txt`). Adversarial cases: the tallest cliff (relief 121 m), the deepest canyon wall, a badlands wall, a harshness-4 cavern chunk and an underworld chunk.
  - ON twice gives the same bytes. ON vs OFF changes only rock (air and fluid untouched).
  - FarQuery = chunk. Shell 0 = Generate. Shells 1-2 = the 0.1 m voxel at every ore sample (2,178 compared). Brick skip on = off.
  - Ore with open sky beside it, ON vs OFF: cliff 770 vs 0, canyon 276 vs 0, badlands 153 vs 0.
  - Every twin sits in its own host rock (1,196 cobalt, 111 emberstone).
- **Start hidden, 4 seeds** (42 / 7 / 2026 / 31337; `Screenshots/181/table.txt`).
  - 7x7 chunk columns round each start rule's column, plus the start zone's steepest cell: **0** rule-ore voxels with open sky beside them.
  - Ore is there underground: 3-10 k voxels per block.
- **Deposit table** (same file): 32^3 cells holding a deposit, 1/64 of the world, per ore x biome, harshness and depth, ON / OFF. Seed 42:
  - copper 102,603 / 82,065; iron 36,474 / 30,244; gold 44,580 / 45,136; cobalt 109,995 / 175,015; emberstone 14,864 / 39,524.
  - Total **0.83x OFF**.
  - Cobalt and emberstone come out near zero in forest and savanna (201 and 58 cobalt).
- **Rough cost** (sites test, 17 chunks, Editor): dense bricks **+1.9 %** (4,915 vs 4,825); generation 956 vs 910 ms (+5 %).

## Pictures (all looked at)
- `Screenshots/181/shot_tall_cliff_ON.png` vs `_OFF.png`: player view, High, 30 m from the face. ON has green copper streaks and one rust iron streak across the face; OFF is plain rock.
  - The black slots are crevices that are black in OFF too: existing terrain / lighting, not this task.
- `Screenshots/181/shot_badlands_wall_ON.png` vs `_OFF.png`: two gold (yellow) streaks upper right and a copper streak on the red band, centre. OFF has none.
  - The black overhang shadow at the top left is the same in OFF.
- `Screenshots/181/shot_deep_canyon_ON.png` vs `_OFF.png`: small gold streaks in the desert wall. Faint at 30 m.
- `Screenshots/181/face_*.png` (generator elevation): panels OFF | ON as seen | ON with latent ores shown.
- `Screenshots/181/cut_harsh_cavern.png` (cut-away of the deep chunk): the middle panel (stage 0) is plain maroon mudstone; the right panel (stage 2) shows the cobalt blobs. That is the latent mechanism.
- Player shots: `.../LookReview/2026-11-07_051721_high` (ON) and `..._052457_high` (OFF). The 45 s wait left s3+ missing (`Logs/lookreview_2026110*.txt`); all near.

## Left / next
- The default stays OFF. Whether to make `-ores 1` the default is the owner's call when convenient.
- Row S1 (stage flip): call `LatentOres.SetStage` and rebuild the PickTables (built once at start).
- Not tested: `-ores 1` together with `-matrules 1` in a player launch. The twins cover both rock paths (`Twins_ContentResolves`).

## For the doc
- Start zone, 4 seeds: **0** rule-ore voxels with open sky beside them round either start (`Screenshots/181/table.txt`).
- Deposits ON vs OFF, seed 42: total **0.83x**; copper **+25 %**; cobalt **-37 %**, emberstone **-62 %**, now only under harsh land (cobalt in forest 21,506 -> 201) (`table.txt`).
- Wall faces: ore visible from outside, ON vs OFF: tall cliff **770 vs 0**, canyon **276 vs 0**, badlands **153 vs 0** voxels (`Screenshots/181/sites.txt`).
- Cost: dense bricks **+1.9 %**, generation **+5 %** on the 17 site chunks (`sites.txt`). Whole suite **PASS 1093 FAIL 0**.
- Images:
  - `Screenshots/181/shot_tall_cliff_ON.png` / `_OFF.png`: the same cliff, ON shows green copper streaks across the face.
  - `Screenshots/181/shot_badlands_wall_ON.png` / `_OFF.png`: gold streaks upper right and copper on the red band, ON only.
  - `Screenshots/181/cut_harsh_cavern.png`: cut-away; the middle panel (as the player sees it) is plain rock, the right panel reveals the latent cobalt.
