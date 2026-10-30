# 86-coarse-column-fields: DONE. Cave noises on an 8x8-column grid (bilinear); the column-detail step is 3.1x faster, caves move <= 1.41 voxels, strata do not move.
(The first session for this task hit the usage limit right after step 0. This session reused its step-0 test and result, then did the change.)
## Step 0 (decides the rest; rule stated in the task file before measuring)
- `ColumnPassSplitTests` (REPORTS=1, Editor Burst, one thread, ABBA): ShapeField.Sample 10.2 ms vs PrepareColumn 16.0 ms per chunk column.
  **PrepareColumn = 61.0% of the column pass** (61.7% in the first session), well over the ~30% bar -> do the coarse grid (not float4 Sample).
## What changed (commit `95a9510`, branch engine-phase)
- `DetailField.PrepareColumn`: the 25 cave noises (4 tunnel layers x line/gate/width/half-height, the 4 tunnel depths, cavern nR/nF,
  underworld pillar/floor/ceiling) are now computed only at the nodes of an ABSOLUTE grid, one node per 8x8 columns, then interpolated
  bilinearly (`CaveTerms`, `Bilerp`). abs(), the gates, the sqrt cross-section and the cavern cell hashes stay per column (they don't interpolate).
  **Geology (strata offsets), soil and ore stay exact per column.** Nodes sit at columns = 7 mod 8, so the columns of shells >= 4
  (`origin + (i<<k) + 2^(k-1)-1`) land ON nodes: one node each, no interpolation.
- Still a pure function of (x, z): chunks, shells, shell fill, far edits (FarField) and the dev map all agree. Loops pass a `NodeCache`
  (256 slots, a job's own local; same bytes as uncached): ColumnJob, ShellColumnJob, ShellFill's sub-samples, DevMap.
- `TerrainSpec.coarseColumnShift` (SizeClass2 sets `COARSE_COLUMN_SHIFT` = 3; 0 = per column, byte-identical to the pre-86 code).
  `-coarsecols 0` on a player (`TerrainGenChunkSource.CoarseColumnArg`, applied in the chunk source and FarField).
  Applies to map v3 and v4. `PrepareColumnExact` = the reference (tests). `TerrainGenChunkSource.Spec` is now a property, plus `SetCoarseColumnShift` (tests).
- Tests: `CoarseColumnTests` (3; the boundary one is Slow, 18 s); `ColumnPassSplitTests` now also runs the A/B (report only).
## Evidence
- Whole suite **PASS 771 FAIL 0 SKIP 22** (Logs/overnight/86-all.out; was 768/0/21; +3 new tests, +1 report-only skip). Wall 26m32s (85b: 26m02s).
- **Boundary shift** (Screenshots/86/coarse_columns.txt): 4 vertical slices x 16,384 columns through the island. Each voxel that differs
  from the per-column version, and its distance to the nearest reference voxel in the same state:
  tunnels **max 1.41 voxels** (0.90% of 184,550 open voxels differ, 1,653 of 1,655 are within 1 voxel); caverns **1.00** (0.10%);
  underworld **1.00** (0.008%); strata **0** (0 of 65,536 columns differ). The test asserts <= 2 voxels.
- **Timing, warm steady state** (warm-up `Logs/tg2world_20261013_044431.txt`, then the test straight after; Editor Burst, isolated,
  ABBA; this is a direction check, not a streaming or frame-time claim; Screenshots/86/column_pass_split.txt):
  PrepareColumn per chunk column 15.96 -> **5.07 ms** (x0.318; 974 -> 309 ns/column); column pass S+P 26.2 -> 15.3 ms (x0.584);
  whole chunk column, 7 layers 100.1 -> **89.3 ms (x0.893)**; shell chunks: 1 x0.867, 2 x0.899, 3 x0.973, 4 x0.988, **5 x1.017** (slightly slower, see below).
- Shots: 4 launches (before/after needs a generator flag at startup): poses A/B (`-coarsecols 0` / default) and `-oredemo` A/B, each about 5-7 min.
  - Surface poses cliff_foot_1, ledge_view, eye_level_2 (golden/noon/band) + minimap: **pixel-identical A vs B** (0 px differ). Caves start >= 6 m down,
    and strata are exact. The dense-brick count differs slightly (384,180 vs 384,195), so the flag did take effect.
  - Ore demo: the same deposit in both. The dig removed 486 (A) vs 493 (B) voxels. Dev-map 1-voxel slice: 103 px of 260,100 differ, cave px 10,461 vs 10,452.
## Left / notes
- The remaining 309 ns/column is mostly the cavern cell loop: 4 cells x 9 candidates x Hash01 per column. Its inputs are constant over a
  chunk column (one 102.4 m cavern cell), so per-chunk candidate precompute would be byte-identical and cut most of it. It changes
  performance, so it needs the owner's OK; I did not queue it.
- Shell 5's +1.7%: likely its shell-fill sub-samples (offsets 4 mod 8 -> 25 nodes per voxel column vs 16 exact columns). It is small, and shells 6-7 sub-samples are not node-aligned either.
- Not measured: streaming throughput in a real player (a milestone perf task should).
## Next step
Owner: look at the slice sheet below. Then 87 as queued.

## For the doc
- Column-detail step (PrepareColumn) per 0.1 m chunk column: **15.96 -> 5.07 ms**; whole 7-layer chunk column **100.1 -> 89.3 ms (-10.7%)**;
  shells 1-2 -13% / -10%, 3-4 about -2%, 5 +1.7% (Editor Burst, warm, ABBA; Screenshots/86/column_pass_split.txt).
- Largest boundary shift: tunnels **1.41 voxels (0.14 m)**, caverns 1.00, underworld 1.00, strata **0** (Screenshots/86/coarse_columns.txt).
- Differing voxels: tunnels 0.90%, caverns 0.10%, underworld 0.008% of their open voxels. Surface poses pixel-identical.
- Whole suite 771 / 0 / 22.
- `Screenshots/86/sheet_slice_crop.jpg`: the dev map's 1-voxel slice through the ore-demo tunnel; left = per column (A), middle = coarse (B), right = |A-B|.
  The black tunnel crescent has the same shape in both; the diff is a dotted 1-voxel outline along its edge.
- `Screenshots/86/sheet_1_cave_wall_ore.jpg`: the tunnel wall with the copper deposit (green) under torchlight, A | B | diff. Same wall, same ore;
  the white marks are single voxel steps that moved by one voxel.
- `Screenshots/86/posesB/cliff_foot_1_noon.png`: a cliff face with brown strata over tan stone, pixel-identical to posesA (strata exact).
