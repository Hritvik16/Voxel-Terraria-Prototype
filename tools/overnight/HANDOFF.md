# 66-gameplay-step-3: DONE. Map v4 has ores (copper/iron/gold/cobalt/emberstone by depth, density by biome x landform); dug ore lands in the hotbar. Ore is SPARSE (0.026% of rock) because of the plan's memory rule.

## What changed (branch `engine-phase`, commit f97bc0b)
- NEW `Assets/TerrainGen/Ores.cs`: GenMat 65-69, one ore per layer (stone copper, deep stone iron to sea-100 m, gold below, cavern rock
  cobalt, underworld emberstone). Integer hash per 32^3 cell -> at most one deposit in the cell's 2x2x2 inner bricks (1, 2 or 4 bricks),
  one ellipsoid per brick (half-axes 2.5-3.5 voxels, ragged rim). `OreTable`: landform x biome multiplier (11 x 6, 1/16ths) -> per map cell plane.
- `DetailField`: `ColumnDetail.oreMul` (x1 on v4, 0 on v3); ore replaces layer rock only, deeper than 6 m (no soil/surface/stamps/cliffs).
- `TerrainGenChunkSource`: 1 MB ore plane from the world record into the column job. `DevMap`: same plane; Z now cycles island/local/CLOSE
  (32 m at 1 voxel per pixel) + ore legend. Palette D colours (`tools/palette_defs.py` -> MaterialRules + Raymarch.compute), item names.
- `Playground.OreDemo.cs` (`-oredemo`): finds an exposed deposit near spawn, torch-lit shot, digs it via SurvivalDig, E panel, dev map slice.
- No frozen API, no memory-layout change (new palette entries only). v3 untouched (oreMul 0; `MapV3_HasNoOre`).

## Evidence
- `SUITE=all` PASS 722 FAIL 0 SKIP 19 (Logs/overnight/66-all2.out). New `OreTests` (5): placement Burst == Mono and repeats, other seed
  differs; v3 no ore; per-pair x layer cell chance within +-25% of the table (all 330 entries); real chunks (seed 42, 4 sites x 12 chunks,
  Burst): 0 ore outside layer rock, 0 outside its band, 0 within 6 m of the top, deterministic, ore < 0.5% of rock, every layer has ore,
  ring-set mixed bricks <= +5% (asserted). `AirFastPath_IsExact` reference now uses the same plane (it caught 12 ore voxels).
- Report (REPORTS=1) `Screenshots/66/ore_report.txt`: ore 24,726 / 94.07 M rock voxels = 0.026%; mixed bricks, ring set (top chunk + 7 below
  x 4 sites) 4,694 -> 4,882 (+4.0%, +5.9 bricks = 2.9 KB per chunk; +24 MB on V0's 16-layer ring projection). Sampled sites were desert/jungle only.
- History of the density: free 0.3-1.2 m blobs at ~0.17% ore made deep chunks 213 -> 1,471 mixed bricks (+590%); brick-shaped deposits pack
  ~89 ore voxels per mixed brick (was ~56); at x1 they measured +7.8% on the ring set, so bases were cut x0.55.
- Play (`BUILD=1 EXTRA="-oredemo" ./run-play.sh`, Player.log): 786 deposits in 10,934 cells searched; exposed copper 66 m from spawn, 23.8 m
  under the top; loaded world matches TerrainGen (copper at the voxel); dug 486 voxels -> 119 copper ore + 367 orange sandstone.
- Memory: +1 MB plane, +4 B per column per ColumnPass (+64 KB each), mixed bricks above. No frame-time measurement (owner rule).

## Picks (owner may change)
- Deposits are brick-shaped 0.5-0.7 m blobs, clustered 2-4 to ~1.6 m (P19 said 0.3-1.2 m; changed for memory, see above).
- Base chance per 32^3 cell (of 65,536): copper 3300, iron 2500, gold 1650, cobalt 1300, ember 820. Gold/iron split at sea - 100 m.
- Table: alpine/breaks 1.5x, volcanic 1.63x, canyon 1.38x, plateau 1.25x, highlands/sea cliffs 1.13x, dunes/sea 0.75x, plains 0.63x,
  wetland 0.5x; biome: snow 1.13x, badlands 1.25x, jungle 0.88x. Favoured x2: forest copper, savanna iron, desert+badlands gold, snow cobalt, jungle ember.
- Colours: copper verdigris green 3faa86, iron rust c08a6a, gold eac43c, cobalt 4a6ff0, ember ff5530; none emissive. Acid does not eat ore.
- Ore only deeper than 6 m under the column top: cliff faces have none (plan step 12 does cliff veins).

## Unfinished / notes
- Sparse ore is the cost of the 5% rule. Each doubling of density costs about +24 MB on the V0 ring projection. A cheaper idea (owner call,
  it's design): put deposits mostly in bricks that already hold cave air (cave walls), which costs almost no memory.
- Cave shot is torch-lit (underground, no sun). The legend change came after the demo build: shot 4 has no ore legend row.
- Engine step 10 (coarse-first) must treat ore as its host rock (plan note).

## Next step
- Task 67 = plan step 4 (start on the best easy area).

## For the doc
Task 66: gameplay step 3, ores. Map v4 now has five ores, one per depth layer (copper, iron, gold, cobalt, emberstone), more in mountains,
volcanic ground and breaks, fewer in plains and wetlands, each biome favouring one. In `./run-play.sh`: dig into tunnel walls (LMB in
Survival) and the ore goes into the hotbar like any block; O then Z three times shows a 1-voxel dev map slice with deposits. Picks: above.
- Ore share of rock 0.026% (24,726 of 94.07 M voxels, 48 chunks, Screenshots/66/ore_report.txt).
- Mixed bricks on ring-shaped chunks 4,694 -> 4,882 (+4.0%, limit 5%; ~+24 MB on V0's ring projection).
- Ore voxels per mixed brick ~56 -> ~89 (brick-shaped deposits); first-draft density would have been +590% mixed bricks in deep chunks.
- Tests: whole suite 722 passing, 0 failing; 5 new OreTests (Logs/overnight/66-all2.out).
- `Screenshots/66/1_cave_wall_ore.png` -- a tunnel 24 m underground, torch-lit: left of centre, two green copper blobs in the orange
  sandstone wall; the cream square between them is the torch.
- `Screenshots/66/2_hotbar_ore.png` -- the same wall after digging: the lower blob is a hole; bottom centre, slot 3 holds 119 copper ore
  (teal); top left "+119 copper ore".
- `Screenshots/66/4_devmap_close_slice.png` -- bottom-left panel: a 32 m slice at the deposit's height, 1 voxel per pixel; the black curve is
  the tunnel, the teal dots in the orange sandstone are copper deposits (~12 in the square); the white cross is the one in shot 1.
