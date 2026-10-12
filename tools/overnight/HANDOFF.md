# 82b-engine-15-generation-speed: DONE. Proven-uniform bricks skip the per-voxel pass; output byte-identical; 0.1 m chunk 26.8 -> 9.2 ms (isolated).
## What changed (commit `0887b49`, branch engine-phase)
- `DetailField.UniformSamples` (TerrainGen): for one column and a brick's 8 sample heights, walks Material's branches over the
  y RANGE and returns the one material every sample gets, or 255 = unproven (the per-voxel pass then runs, so 255 is always safe).
  Each test is Material's own comparison, taken at the end of the range where it is hardest to pass:
  - Above ground: air above max(ground, still water, sea); sea water; lake water or lava.
  - Below ground: bedrock, the river lip, stamp openings, stamp3D, the ledge rule. Caves are checked only if some sample is
    deeper than 6 m and not under a river floor: pocket (CullBrick's test), cavern and underworld intervals, all 4 tunnel layers
    (2 voxels of margin for float rounding). Then stamp body, surface, soil, ores (`Ores.BrickMayHold` = At's cell and deposit
    test, which depends only on the cell and the brick), then layer rock (`LayerRockRange`; float sums are monotone).
  - Brick geology (deeper than 20 m) uses the exact cull values in 0.1 m chunks. In shells, where samples lie in different fine
    bricks, it uses offsets bounded at 2x the noise amplitude. A range across the 20 m depth is split, and both parts must agree.
- `TerrainGenChunkSource`: `VoxelJob` / `ShellVoxelJob` first take per-brick-column bounds. A brick above max top is air in O(1).
  A brick wholly above or wholly below every column's ground asks `UniformSamples` for each of its 64 columns. A proven brick is
  written as the uniform brick it would have been; shells also fill `Mats`. The shell pocket test covers the box of fine bricks
  (`NoPocketInBox`). `BrickSkip` (on by default) and `SkipStats` (air/fluid/solid/total, 0.1 m and shells) are public for tests.
- `Ores.At` refactored to share `DepositMask` (same arithmetic). CLAUDE.md: one Task 82b paragraph.
## Evidence
- **Byte-identical** (`BrickSkipTests`, both Slow; Logs/overnight/82b-identical.txt, 82b-shells-identical.txt):
  - **257 chunks**: 14 seed-42 sites (10 landforms, coast, sea, lake, river, stamp column). Each has the surface band plus deep
    layers 1-36 (underworld, caverns, tunnel layers, pockets). 24,190 dense bricks, 76,771 ore voxels, 995,400 cave-air voxels.
  - **254 shell chunks** over shells 1-7 at 9 of those sites: surface band, layer 0, the cavern top and sea - 60 m.
  - Equal byte for byte with the skip on vs off. No lava-lake site turned up on the scan grid (none on seed 42's grid).
- Existing exactness tests still pass: Shell0_IsGenerate, ShellVoxels vs D (shells 1-7), SurfaceChunk_IsExact, AirFastPath,
  ColumnPassSharing (Logs/overnight/82b-filter2.out).
- **SUITE=all PASS 757 FAIL 0 SKIP 20** (755 + 2 new; the extra skip is the report-only timing test). Logs/overnight/82b-all.out.
- **run-phase7-rig.sh** (fresh build): PASS 37 FAIL 0, **19/19 shots byte-identical** (`cmp`) to 82's
  (Phase7Cycle/2026-10-12_143300 vs _101148). Logs/overnight/82b-phase7.out.
- **Isolated timing** (`BrickSkip_TimingReport`, REPORTS=1): Editor Burst, one thread, ABBA, warm steady state. Warm-up was
  `MODE=stream MOTION=walk ./run-tg2world.sh` (Logs/tg2world_20261012_140430.txt); report in Logs/overnight/82b-timing.txt.
  Chunk set: 8 sites, the stream's surface band (4 below, 2 above, shared column pass).
  - 0.1 m: **26.8 -> 9.2 ms per chunk (2.91x)**. 94.4% of bricks proven: air 14.9, fluid 5.3, solid 74.2%.
  - Shells, ms per chunk (off -> on): k1 90 -> 75, k2 105 -> 85, k3 200 -> 138, k4 197 -> 148, k5 178 -> 145, k6 155 -> 121,
    k7 137 -> 96 (**1.21-1.45x**). Bricks proven: 77 / 64 / 52 / 43 / 35 / 45 / 63%.
  - The pre-split first run (Logs/overnight/82b-timing-v1.txt, also after a warm-up) agreed: 26.8 -> 9.7 ms.
  - This is an isolated generation figure, NOT a streaming or frame-time claim (step 16 measures streaming).
## Not done / notes
- Shells gain less because their **column pass now dominates**: `ShellColumnJob` takes 5 ShapeField.Sample per column for the
  spacing-1 MaxStep, about 70-80 ms of the shell-1 to shell-3 chunk. Tall high-shell bricks also straddle the ground somewhere,
  so they are truly mixed. Making the column pass cheaper is a performance change and needs the owner's go-ahead (not queued).
- No follow-up task added.
## Next step
Queue order: 83 (shell cave fill). Engine step 16 (the streaming check of this speed-up) comes later, per the plan.

## For the doc
- 0.1 m chunk generation, isolated (Editor Burst, 1 thread, warm, ABBA): **26.8 -> 9.2 ms per chunk (2.91x)**.
  Logs/overnight/82b-timing.txt.
- Shell chunks, same check: shell 1 90 -> 75 ms, shell 3 200 -> 138 ms, shell 7 137 -> 96 ms (**1.21-1.45x** across shells
  1-7). The shell column pass is now the larger cost.
- Bricks proven uniform and skipped: **94% at 0.1 m** (air 15%, water 5%, solid 74%); **35-77% in shells** (by shell).
- Byte-identical: **257 chunks + 254 shell chunks (shells 1-7)**, skip on vs off. Whole suite 757/0/20. Phase 7 rig 37/0,
  with 19/19 shots byte-identical.
- No images: output is byte-identical, so nothing changes on screen.
