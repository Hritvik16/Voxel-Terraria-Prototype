# 104-engine-q1-far-query-getvoxels: DONE -- IFarQuery.GetVoxels, exact (0 of ~390k test points differ), fast suite green

## What changed (commit 78644ab)
- NEW `Assets/CoreEngine/Memory/IFarQuery.cs`: `void GetVoxels(NativeArray<int3> points, NativeArray<byte> results)`, beside the
  frozen `IWorldQuery.GetVoxel` (unchanged). Main thread only.
- NEW `Assets/CoreEngine/Streaming/FarQuery.cs`: per point, the chunk's LOAD order (8.14 row 10):
  1. resident chunk -> `ChunkStore.GetVoxel` (live voxels, unsaved edits and near-applied ops included);
  2. the chunk's D.1 delta holds the brick -> its byte, then ops > S (S from the ops snapshot's side table: no entry 0, CRC mismatch all);
  3. else the generator + EVERY op in seq order (a brick missing from the delta is gen + ops <= S, then ops > S follow).
  A delta the codec rejects (`DeltaCodec.TryDecodeOnto`, the worker's own check, run on a scratch chunk) counts as no delta.
  Split: rules 1-2 and the per-point plan on the main thread; rule 3 + every op in a Burst `PointJob` (Strict/High, Generate's float mode),
  IJobParallelFor over 32-point batches (one NodeCache + column memo per batch). Reads only the source's read-only map arrays, a
  NativeArray copy of the immutable ops Snapshot (re-copied only when the snapshot object changes) and the plan. No lock.
  Delta cache: LRU of parsed D.1 indices (and "no file"), 128 entries / 16 MB; `ForgetDelta` / `ForgetAllDeltas`.
- NEW `Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Point.cs`: `PointVoxel` = ColumnJob's column (ShapeField.Sample at x+0.5,
  the 4 neighbour samples for the step at the top, PrepareColumn + ore plane) and VoxelJob's CullBrick + Material + GenToEngine.
  Consecutive points of one (x, z) reuse the column. Nothing under `Assets/TerrainGen/` changed (so fast suite, not SUITE=all).
- `StreamManager.cs`: `FarQuery` property (made on first use; NULL on sizeClass 0/1 -- the legacy generator has no point form);
  SaveDelta calls `_farQuery?.ForgetDelta(coord)` after writing or deleting a delta; Dispose disposes it. No frozen signature, D.1 or
  4.4 change. CLAUDE.md: task line.

## Evidence
- `FarQueryTests` 4/4 + `EditOpTests` 10/10, filtered run (`unity_filter.log`, `TestResults_filter.xml`):
  - Generator: 307,200 points (random + the x = 0 chunk-border and z = 8 brick-border planes + 12 edges) in 8 chunks, ONE shuffled call:
    surface (4 materials), below it, 6 layers down, sea, negative x/z, below y 0 (two), above the top layer -- 0 differ vs `Generate`.
  - Load order vs the worker's own steps (Generate, ApplyToChunk, TryDecodeOnto, ApplyToChunk), 22,897 points over the chunk + the one
    below (an op crosses the seam): S = 1 (op 2 lands over the hand edit, the hand edit survives op 1, a uniform D.1 record), side-table
    CRC mismatch (all ops then D.1), corrupt delta (no D.1), deleted delta -- 0 differ each.
  - Resident: 35,792 points (17,416 resident incl. a hand edit and a near-applied op, 18,376 generated in the neighbour) -- 0 differ.
  - Streamed (Slow, tg2 StreamManager): query far (caches "no delta"), stream in, edit + op, leave (delta saved -> ForgetDelta), op after
    the save, query far, stream back: 21,625 of 21,625 points equal the streamed chunk; 513 answered from the delta.
- Fast suite `PASS 868 FAIL 0 SKIP 14` (was 865; the Slow test is SUITE=all only), `Logs/overnight/104-fast.out`.
- Cost per point (EditMode test timing in the Editor, Burst, 20,000 points; NOT a frame-time measurement, rule 10; two runs):
  single thread, every point a new column 6.7 / 9.6 us; single thread, 64-voxel vertical runs 0.31 / 0.32 us; parallel wall
  (7 job workers) 1.7 / 2.2 us. The amendment's 1-3 us estimate holds for runs and parallel; a lone new column costs more (5 ShapeField
  samples: 4 are only for the step-at-top input Material needs).

## Unfinished / notes
- Legacy sizeClass 0/1 worlds (rigs) have no far query (`StreamManager.FarQuery` null).
- Ops are scanned linearly per generated/delta point (fine for tens-hundreds of ops; a spatial index if logs grow large).
- Fluid far away = generated water + ops (FAR-ACTIVE tiles do not exist yet; §7's "live state through the same call" comes with them).
- Next: Q2 `IFarQuery.Raycast` (coarse far-band march, refined with GetVoxels near a hit), per the 8.15 order.

## For the doc
- Result: `IFarQuery.GetVoxels(points, results)` answers any voxel as its chunk would load (resident -> D.1 brick + later ops ->
  generator + ops), exact; Burst job, parallel; `StreamManager.FarQuery`.
- Exactness: 0 of 307,200 generator points, 0 of 4 x 22,897 delta/op points, 0 of 35,792 resident/neighbour points, 0 of 21,625
  streamed points differ (FarQueryTests, `unity_filter.log`).
- Tests: FarQueryTests 4 (one Slow); fast suite 865 -> 868 PASS, 0 FAIL (`Logs/overnight/104-fast.out`).
- Cost per point (Editor test timing): ~7-10 us single thread on a fresh column, 0.3 us along a vertical run, ~2 us wall with 7 workers.
- No images: nothing visible changed (an API for game rules).
