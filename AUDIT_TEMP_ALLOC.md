# Audit: temporary allocations in and around jobs (task 223, 2026-11-16)

Why: task 218 found that `Allocator.Temp` inside an `IJobParallelFor`'s `Execute` is given back only when the WHOLE job
ends, so a per-iteration allocation piles up (jungle_high 10 GB). This audit lists every `Allocator.Temp`, `new NativeArray`
/ `NativeList` (Temp or TempJob), `stackalloc` and `UnsafeUtility.Malloc` that runs inside a Burst job's `Execute`, or that
lives as job scratch on a worker thread, under `Assets/` (tests excluded).

## Which jobs exist

- **Parallel jobs (`IJobParallelFor`, the 218 pattern): only three.** `FarQuery.PointJob` (fixed by 218),
  `FarField.FillRegionJob`, `FarField.FillTg2Job`. No `IJobFor`, `IJobParallelForBatch` or `IJobParallelForDefer` anywhere.
- Everything else is a single `IJob`. Its Temp is given back when that one job ends (218's probe: `job.Run()` on a worker
  thread = +1 MB), so it can only pile up if it allocates inside a loop of that one job.
- Chunk and shell generation run `.Run()` from managed streaming worker threads (`StreamManager.WorkerLoop`, 7 workers;
  `ShellStreamer.WorkerLoop`, 3 workers), with `Allocator.TempJob` scratch held for the whole chunk build.

## Table

Peak = size x copies alive at once. "Per job" = allocated once per `Execute`, not per iteration.

| file:line | job (type, how run) | allocation | size per call | calls per job | peak in flight | verdict |
|---|---|---|---|---|---|---|
| FarQuery.cs:296 / Trees.cs:297-300 | `PointJob` (IJobParallelFor, Schedule(batches,1)) | Malloc Persistent `PointColumn` tree lists | ~23 KB per chunk column | once per column, freed per batch | batch's columns x workers, < 1 MB | OK (task 218's fix) |
| Trees2.cs:314-316 | `GatherLib`, from PointJob and the chunk jobs | Malloc Persistent scratch | ~23 KB | per call, freed before return | 1 per worker | OK (task 218's fix) |
| FarField.cs:1126-1300 | `FillRegionJob`, `FillTg2Job` (IJobParallelFor) | none (TerrainGen code reached from them uses only `stackalloc`) | 0 | - | 0 | OK |
| TerrainGen/*.cs (Features, Regions, WorldMap, Rivers, Hydrology, Erosion, EasyAreas, MapJob, PocketStreams) | reached from every gen job | `stackalloc` only (8 B - ~2 KB tables) | <= ~2 KB stack | per call, stack frame | 0 heap | OK (stack, freed on return) |
| TerrainGenChunkSource.cs:619 | `ColumnJob` (IJob, Run on gen worker) | Temp `hg` int[130x130] | 67.6 KB | 1 | x7 workers = 0.5 MB | OK |
| TerrainGenChunkSource.cs:675-677 | `VoxelJob` (IJob, Run) | Temp `bTop/bMinH/bMaxH` int[256] | 3 KB | 1 | 21 KB | OK |
| TerrainGenChunkSource.Shell.cs:427-429 | `ShellVoxelJob` (IJob, Run on shell worker) | Temp, same 3 x int[256] | 3 KB | 1 | 9 KB | OK |
| Trees.cs:214, Trees2.cs:340, Gallery.cs:219, Towns.cs:110 | `Overlay*` inside VoxelJob / ShellVoxelJob | Temp `touched` bool[4096] | 4 KB | 1 per overlay (<= 4) | ~0.2 MB | OK |
| ShellFill.cs:260, 336-338, 398, 431, 454 | `ShellFillJob` (IJob, Run once per phase) | Temp `stack` NativeList<int>(4096), `subS/subD`(16), `widened`, `pending`(256), `hist/hb` int[256] | 16 KB + growth; the rest <= 1 KB | 1 per phase | 3 workers x (16 KB + flood growth) | OK (once per Execute; the flood stack grows geometrically, bounded by one chunk) |
| TorchFloodBuild.cs:431-433, 472, 570-574 | `FloodBrickJob` (IJob, Scheduled, MaxJobs = 2) | Temp `flag` byte[nb], `pass` int[nb], `planeList`, `cand`, `head/fill` int[top+1], `sorted` int[seeds] | nb ~ 36 k bricks at 12 m reach: ~0.2 MB + seeds x 4 B | 1 | 2 jobs, ~0.5-2 MB | OK (no aged-allocation stack names it in any log under `Logs/`) |
| TorchFloodBuild.cs:311, 434-435, 534, 604, 871, 925-927 | Mask/FloodBrick/Merge jobs | `stackalloc` (<= 2 KB) | stack | - | 0 heap | OK |
| EditSummary.cs:381 | `BuildJob` (IJob) | `stackalloc` ulong[GROUP_WORDS] | stack | - | 0 heap | OK |
| LODDownsampler.cs:63-72, TerrainClipmap.Sealed.cs:145 | main-thread / Burst helpers | `stackalloc` (<= 4 KB) | stack | - | 0 heap | OK |
| FarField.cs:917, ShellStore.cs:319, TorchFloodPool.cs:658 | main thread, not in a job | Temp | <= 4 MB, one at a time | - | freed same frame | OK |
| ShellBrickPack.cs:155-156, FarQuery.cs:198, Trees2.cs:240-241, LightFloodReference.cs:669-760, PlaygroundCapture.MirrorProbe.cs:180 | main thread, Run/Complete in the same call | TempJob | <= 0.5 MB | - | freed same frame | OK |
| **TerrainGenChunkSource.cs:485, 524-527** | `Generate` on a gen worker thread (managed, holds the scratch while ColumnJob / VoxelJob run) | **TempJob** `mats` byte[128^3], `uniform`, `isUniform`, `stats` (+ `own` ColumnPass when no pass is given) | 2.06 MB | 1 per chunk | x7 workers = 14.4 MB | **WARNING: alive > 4 frames -> FIXED (Persistent)** |
| **TerrainGenChunkSource.Shell.cs:243** | `GenerateShellChunkRaw` on a shell worker | **TempJob** `stats` int[4] | 16 B | 1 per shell chunk | 48 B | **WARNING -> FIXED (Persistent)** |
| **TerrainGenChunkSource.ShellFill.cs:116-126** | `FillShellChunk` on a shell worker (runs ShellFillJob's phases) | **TempJob** `open` ulong[32768], `lyOpen/head/scanLo` int[16384], `halo`, `dirty`, `pass`, `boxes/cand/next` lists | ~0.46 MB | 1 per shell chunk | x3 workers = 1.4 MB | **WARNING -> FIXED (Persistent)** |

## Result

- **No pile-up anywhere.** No allocation in any job is made per iteration of a parallel job; every Temp in a single `IJob`
  is made once per `Execute`. Nothing has a peak above 50 MB (largest in flight: `Generate`'s `mats`, 14.4 MB over 7 workers).
- **The "older than 4 frames" warnings are NOT Temp-in-a-job.** Every logged stack (`Logs/overnight/221/*.log`, 218b, 219,
  and this task's before launch) ends in a `Dispose` of an `Allocator.TempJob` array in `Generate`, `GenerateShellChunkRaw`
  or `FillShellChunk` on a streaming worker thread. TempJob has a 4-frame lifetime; one chunk build on a warm M1 takes longer,
  so every such Dispose logged "deleting an allocation that is older than its permitted lifetime of 4 frames" and Unity's
  periodic check logged "JobTempAlloc has allocations ... more than the maximum lifespan of 4 frames old" (the two come in
  pairs in the log). They were freed, so not a leak; but they pinned TempJob blocks for the length of a chunk build.
- **TorchFloodBuild:** no aged-allocation stack in any log names it (grep over every `Logs/**/*.log`); its jobs allocate
  once per flood, at most 2 floods in flight. No change.
- **Fix (task 223):** those 16 TempJob allocations -> `Allocator.Persistent` (allowed: managed worker threads, not inside a
  job). Same sizes, same lifetimes (freed in the same `finally`), same contents: hash probe identical (below).

## Evidence

- **Bit-identical:** `GenScratchHashTests.GeneratorOutput_Hash` (Explicit probe; seed 42, 18 near chunks around a land
  surface voxel, 16 non-uniform, + shells 1-4 with the fill): old TempJob code `near 69CE4AAEAE2F03BC shells 7E504985E4B9FDDF`,
  new Persistent code the same two hashes (`Logs/TerrainGen/temp223/hash_before.txt`, `hash_after.txt`).
- **Launch pair** (`Logs/overnight/223_pair.sh`: look review, high preset, 218b's survey flags, poses jungle_high + walk1,
  `-mirrorprobe 1`; one build each; logs `Logs/overnight/223/player_before|after.log`, ledgers `Screenshots/223/*/lookreview.txt`):

| | before (TempJob) | after (Persistent) |
|---|---|---|
| "deleting an allocation older than ... 4 frames" lines | 169 | **0** |
| "JobTempAlloc has allocations ... 4 frames old" lines | 68 | **0** |
| process memory peak | 2,528.4 MB | 2,516.2 MB |
| GPU body pool peak / refusals (jungle_high, walk1) | 254,399 / 56, 70 | 254,399 / 59, 69 |
| CPU brick pool peak (valve 484,500) | 494,535 | 494,646 |

  Refusals and pool peaks move by start-up load variance (218b's same poses: 58 / 70); this change touches no pool code.
  Pictures: walk1 4 px differ; jungle_high 42,922 px, all in the far top band (rows 0-317), as 218b's pair (36 k px there).
- `-diag-job-temp-memory-leak-validation` (passed to both launches) printed no callstacks in this release player; the
  attribution comes from the Dispose stacks and the two warnings arriving in pairs.
