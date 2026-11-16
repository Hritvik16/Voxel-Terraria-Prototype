# 218-bug-jungle-high-footprint: DONE -- FIXED (cause: the point query's tree gather piled Allocator.Temp up inside one parallel job)

NOTE for the supervisor: `tools/overnight/PAUSE` exists. The memory watchdog wrote it at 07:34 when my deliberate trees-ON repro reached 10.2 GB
(the expected kill, not a new problem). Auto mode would not let me delete it: remove it to let the queue go on.

## What changed (commit: see `git log -1`, 218)
- `TerrainGenChunkSource.Trees2.cs` `GatherLib`: candidate + cell scratch = `UnsafeUtility.Malloc`/`Free` (was `Allocator.Temp`, ~23 KB a call).
- `TerrainGenChunkSource.Trees.cs` `PointTree`: gathers once per 128-aligned CHUNK column (`v & ~127` = Generate's own footprint), not once per voxel
  column; `TerrainGenChunkSource.Point.cs` `PointColumn`: tile key + Malloc'd `treeList`/`treePrims` + `Dispose()` (a job may not create a
  Persistent NativeArray: my first try threw "Jobs can only create Temp memory"); `FarQuery.cs` `PointJob`: `col.Dispose()` at the end of each batch.
- Diagnostic `-memwatch <MB>` (`Assets/Game/MemWatch.cs`, hooked in `Phase4Bootstrapper.LateUpdate`, default OFF). Tests `PointTreeMemoryTests`,
  `TempAllocThreadProbeTests` (Explicit). KNOWN_ISSUES entry + CLAUDE.md index line, TASKS.md "Task 218". Scripts `Logs/overnight/218_iso.sh`, `218_memsample.sh`.

## Evidence (isolation first, `Logs/overnight/218/`)
- `-trees2 0`, 216's flags, jungle_high/walk1/walk2: peak 3,120 MB, flat (`t0.mem`, `look_t0.out`). So the world trees cause it; the 209/209b
  launch was not needed.
- Trees ON (default): 2.4 -> 10.2 GB in ~2 min (`on.mem`; watchdog 07:33:56). `footprint` categories (`on2.fp20/35/60.txt`): Untagged (native heap)
  978 -> 2,283 -> 5,114 MB, IOAccelerator (GPU) flat at 1,172 MB. With `-memwatch 4000` (`player_on3.log`) the 10 s lines stop at t 284 s
  (2,375 MB) while the footprint climbs to 7.2 GB: the MAIN THREAD was in one frame, right after the first shot's 60 settle frames. That is the
  survey's `-mirrorprobe` `GenProbeLines`: ONE `FarQuery.GetVoxels` over every resident uniform-air chunk x 1,024 points (713-1,299 chunks).
- Mechanism proved in EditMode (`Logs/TerrainGen/temp218/probe.txt`): Temp in `job.Run()` (main or worker thread) is given back (+1 / +2 MB for
  300 x 1 MB); in ONE IJobParallelFor it is not (4,000 batches x 64 KB: +235 MB Schedule, +117 MB Run). Each air point re-gathered (~350 candidates).
- Regression test `PointQuery_Trees2_FootprintFlat` (16,384 air points in distinct columns): RED before the fix (+332 MB, 4,060 ms), now +0 MB, 377 ms.
- Parity: filter run 16/16 PASS (`test_after.out`) incl. Slow `Trees2Tests.Chunks_Deterministic_Border_PointAndShellsAgree` (FarQuery == chunk with
  dense world trees), `FarQueryTests`, `TreeTests`, `TreeGalleryTests`. WHOLE suite: PASS 1142 FAIL 0 SKIP 34, wall 40m34s (`all.out`; usual
  ~12-13 min: the slowest cases are generator / fluid tests (LakeShore 138 s, FluidPocket PathsAgree 136 / 134 s), the owner had just used the Mac;
  the last run's per-test times were overwritten, so the slower wall is NOT attributed). No fast suite BEFORE the change (216's fast run at the
  same code: `Logs/overnight/216/fast_before.out`).
- Player proof (`iso_fix.out`, `look_fix.out`, `fix.mem`): rebuilt, defaults (trees ON), forest + jungle_high + walk1 + walk2, `-mirrorprobe 1`: all 4 poses shot,
  ledger peak 2,559.6 MB (sampler max 2,512 MB). `[gen]` probe: 912/713/732/776 air chunks, 0 with generator solids.
- Screenshots looked at: jungle_high fix vs `-trees2 0`: dense jungle canopy to the snow line, ponds mid-right, no holes / floaters; the trees-ON shot adds a
  spreading world tree by the pond and pale birches upper right. forest: birches (white bark), redwood grove against the mountain, ground complete.

## Memory (rule 11) -- trees ON after the fix vs `-trees2 0` (the old build; same 3 jungle poses)
- Process peak 2,560 MB vs 3,120 MB (the OFF arm also paid the point probe's Temp pile-up for today's trees, now gone too).
- CPU brick pool peak 493,780 vs 495,717 (cap 570,000), valve 484,500 both. GPU body pool full in both; refusals per pose jungle/walk1/walk2:
  ON +295 / +197 / +105 vs OFF 71 / +15 / +8. That rise is the world trees' content (task 214), NOT this fix (no switch here; this fix only
  changes the point path). Flagged for the owner: dense world trees push more refusals at jungle poses (KNOWN_ISSUES "GPU body pool full").
- Rough, not a measurement: survey settle frames ~300 ms with trees ON vs ~215 ms OFF at the same poses.

## Left / next
- Task 216's proof pair (`Logs/overnight/216_pair.sh`) can now run with trees ON (drop its `-trees2 0`): queued already as 218b.
- Any other `Allocator.Temp` inside an IJobParallelFor `Execute` has the same pile-up (none found on the point path; TorchFloodBuild uses Temp, not checked).

## For the doc
- jungle_high, today's defaults (trees ON): footprint 9,088-10,240 MB (killed, 216/218) -> peak 2,560 MB, all 4 poses shot (`Logs/overnight/218/look_fix.out`).
- Point query, 16,384 air points: +332 MB, 4,060 ms -> +0 MB, 377 ms (`Logs/TerrainGen/temp218/point.txt`).
- Cause: one FarQuery call of ~1 M points (survey `-mirrorprobe`); each point re-gathered the world trees into ~23 KB of Temp memory that the
  parallel job only gives back at its end. Now one gather per 128-voxel chunk column, heap scratch freed at once.
- Memory vs `-trees2 0`: peak 2,560 vs 3,120 MB; GPU refusals at the jungle poses higher with trees ON (295 vs 71 at jungle_high: trees content, task 214).
- `Screenshots/218/fix/ws_fixed_jungle_high_native_sf0.png` -- jungle_high with trees ON after the fix: the full jungle canopy, no holes; this pose used to kill the player.
- `Screenshots/218/t0/ws_fixed_jungle_high_native_sf0.png` -- the same pose with `-trees2 0`: compare the right third (world tree by the pond, birches upper right).
