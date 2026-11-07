STOP_FOR_OWNER: "Keep trees on by default?" My recommendation is **not yet**. They look right and pass every correctness check. But in dense forest they overflow the shell 1-2 brick pools (FULL), and they roughly double shell-chunk generation time. Decide after a pool/cost follow-up, or say "on" and accept missing far chunks in forests for now.

# 180-placement-voxel-trees-first-test: DONE behind `-trees 1` (default OFF). Commit cd7e342.

## What changed
- `Assets/TerrainGen/Trees.cs`:
  - At most one tree per 6.4 m cell. Its position and numbers come from an integer hash of the cell.
  - Density = the world record's landform x movement band (0 on sea, route, feature and Sheer cells) x a per-biome rule x the cover slot. A patch variant = a clearing.
  - No tree in water, on a stamp, ledge or lip, on a foot steeper than 45 deg, or where the slope would reach into the crown.
  - 3 shapes per biome family: forest oak / poplar / bushy, jungle buttressed emergent / palm / dense, savanna acacia / baobab / two-tier acacia, snow spruce / pine / fir, desert saguaro / column / barrel. Badlands get none.
- New materials: leaves 55-58 (from each biome's flat cover colour in shade), woods 76-79, cactus 93. `GenMat.IsTreeTrunk` / `IsTreeLeaf` support "cut any trunk voxel -> remove the whole tree".
- Where it lives (`TerrainGenChunkSource.Trees.cs`):
  - The column passes gather the trees round the footprint. Their crowns raise the column top, so the air fast paths stay exact.
  - The voxel passes overlay them. Trees overwrite AIR only: trunks first, then leaves, in cell order.
  - `PointVoxel` (FarQuery) uses the same rule.
  - Shells up to 5 have trees (3.2 m voxels, out to 2.2 km on High). Shells 6-7 and the far band have none.
- Palette: `tools/palette_defs.py` -> `gen-tg2-materials.py --write`. Items and Tools entries added. TASKS.md has the paragraph.

## Evidence
- **Whole suite PASS 1075 FAIL 0 SKIP 34** (`Logs/overnight/180_all.out`).
- `TreeTests.Chunks_...` (`Screenshots/180/chunks180.txt`):
  - Seed 42 forest tree across a chunk border: 101k + 244k tree voxels on the two sides.
  - Two ON runs give identical bytes. ON vs OFF differs only where OFF was air.
  - 94k FarQuery points equal the chunk bytes. Shell 0 = Generate.
  - Shells 1-3: every tree voxel equals the 0.1 m voxel at its sample point.
- `FourSeeds_Rules_Sheets` (`rules180.txt`), seeds 42 / 7 / 2026 / 31337:
  - 0 rule breaks.
  - **0 of 714 trees rejected.** That is 30 per family plus 30 on steep feet per seed. Checks: no voxel floating, trunk on the ground, at most 2 % of the crown in rock, no trunk in rock above its foot.
  - Forest has 5,600-7,100 trees/km², jungle 3,400-9,000, snow 870-2,100, savanna 590-780, desert 36-680.
  - The first version left 1-6-voxel leaf crumbs. Fixed with star-shaped edge noise.
- Cost, from this task's own runs (no timing arms):
  - LOD 0, 9 chunk columns, forest: dense bricks 8,218 -> 17,483 (+113 %, +4.5 MB bodies, CPU + GPU each). Jungle +111 %, snow +28 %, savanna +31 %. Editor gen time +14 / +13 / +3 / +2 % (`cost180.txt`).
  - Shells 1 / 2 / 3 / 4 / 5 in forest: dense +264 / +185 / +75 / +35 / +5 %.
  - Player (same build): shell chunks took **55-74 ms (OFF) -> 89-156 ms (ON) each on a worker**. Logs: `.../LookReview/2026-11-06_205547_high` (OFF) and `_203623_high` (ON).
  - **At the forest pose, shells 1 and 2 hit FULL** (pool cap). The jungle, savanna and snow poses did not.
  - Resident memory was NOT measured directly: these launches print no RSS. The pool caps are fixed at start-up, so the cost shows up as FULL / missing chunks rather than extra MB.
  - Worst case if the whole shell-0 window were forest: about +113 MB CPU and about +270 MB GPU of brick bodies (from the counts above).

## Unfinished / for the owner
- **Black undersides and shadows.** Crown undersides and tree shadows render pure black: the lighting has no sky fill under overhangs. That is a lighting look issue, not a tree issue.
- **Far view.** The pose shows no difference. Shells 3+ were still unloaded after the 45 s wait in both arms (`[shells]` s3 0/2001).
- **Owner checks.**
  - Leaf colours are my pick. Snow's needles are an exception (dark blue-green), because the snow palette row has no green.
  - Route cells get no trees across the whole 8 m cell, not only on the centre line.
- **Not committed.** `Assets/Scenes/Playground.unity` was rewritten by the scene builder during the build. Not mine, left as it was.

## For the doc
- Placement rules hold on 4 seeds. **0 of 714 checked trees float or sit in rock.** LOD 0 / shells 1-3 / point query agree byte for byte (`Screenshots/180/rules180.txt`, `chunks180.txt`).
- LOD-0 dense bricks in dense forest / jungle: **+113 % / +111 %**. Snow / savanna: +28 % / +31 % (`cost180.txt`).
- Shell chunk generation in the player: **55-74 -> 89-156 ms**. Shells 1-2 hit **FULL** at the forest pose.
- Images: `Screenshots/180/viewer.html` (Space flips OFF/ON, arrows change pose; full size).
  - `Screenshots/180/fixed_snow_compare.png`: OFF on top, ON below. Bare snow plain becomes a spruce / pine / fir forest. Look at the black undersides of the cone tiers and the black shadows.
  - `Screenshots/180/fixed_savanna_2_after.png`: the densest savanna spot. Flat umbrella acacias and fat baobabs; again black shadows.
  - `Screenshots/180/sheet_42_forest.png`: 30 forest trees side by side (oak, poplar, bushy). Look for trunks on the ground and no stray leaves. Sheets for every seed and biome are in the same folder; rejected trees would be framed red (none are).
