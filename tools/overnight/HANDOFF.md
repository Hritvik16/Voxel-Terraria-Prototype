# 223-bug-audit-temp-alloc-in-jobs: DONE -- no pile-up anywhere; the "older than 4 frames" warnings were generator TempJob scratch -> Persistent (169 + 68 warnings -> 0)
## What changed (commit 996c9f7)
- `TerrainGenChunkSource.cs` (485, 524-527), `.Shell.cs` (243), `.ShellFill.cs` (116-126): 16 `Allocator.TempJob` -> `Allocator.Persistent`.
  They are scratch held by managed streaming worker threads (StreamManager 7, ShellStreamer 3) for one whole chunk build, longer than
  TempJob's 4-frame lifetime. Same sizes, same `finally` Dispose, nothing new kept; Persistent is allowed there (not inside a job).
- New `AUDIT_TEMP_ALLOC.md` (the table), `Assets/CoreEngine/Tests/GenScratchHashTests.cs` (Explicit hash probe), `TASKS.md` "Task 223".
## Audit table (full rows in AUDIT_TEMP_ALLOC.md)
| site | job | per call | peak in flight | verdict |
|---|---|---|---|---|
| FarQuery PointJob / GatherLib (Malloc) | IJobParallelFor | ~23 KB per column | < 1 MB | OK (218's fix) |
| FarField FillRegionJob / FillTg2Job | IJobParallelFor | none (TerrainGen = stackalloc only) | 0 | OK |
| ColumnJob hg, VoxelJob/ShellVoxelJob bTop.., Overlay* touched | IJob Run, once per Execute | 3-68 KB | < 1 MB | OK |
| ShellFillJob stack/subS/pending/hist | IJob Run, once per phase | 16 KB + flood growth | 3 workers | OK |
| TorchFloodBuild FloodBrickJob flag/pass/sorted.. | IJob Scheduled, MaxJobs 2 | ~0.2-1 MB | 2 jobs | OK (no logged stack ever names it) |
| main-thread Temp/TempJob (FarField 917, ShellStore 319, ShellBrickPack, FarQuery plan, ..) | not in a job / same frame | <= 4 MB | 1 | OK |
| **Generate mats/uniform/isUniform/stats** | gen worker thread, TempJob | 2.06 MB | x7 = 14.4 MB | **warned -> FIXED** |
| **GenerateShellChunkRaw stats** | shell worker, TempJob | 16 B | x3 | **warned -> FIXED** |
| **FillShellChunk open/lyOpen/head/scanLo/..** | shell worker, TempJob | 0.46 MB | x3 = 1.4 MB | **warned -> FIXED** |
No allocation is made per iteration of a parallel job; nothing peaks above 50 MB. So no 218-style fix was needed beyond the TempJob swap.
## Evidence
- Bit-identical: `GenScratchHashTests.GeneratorOutput_Hash` (seed 42, 18 near chunks (16 non-uniform) + shells 1-4 with fill), old code
  and new code both `near 69CE4AAEAE2F03BC shells 7E504985E4B9FDDF` (`Logs/TerrainGen/temp223/hash_before.txt`, `hash_after.txt`).
- Every aged stack in all 126 logs under `Logs/` (111,530 + 55,758 lines) ends in Generate / GenerateShellChunkRaw / FillShellChunk; none in TorchFlood.
- Launch pair `Logs/overnight/223_pair.sh` (look review high, 218b survey flags, jungle_high + walk1, ~11 min each; logs
  `Logs/overnight/223/player_before|after.log`, ledgers `Screenshots/223/before|after/lookreview.txt`):
  aged-dispose lines 169 -> 0, JobTempAlloc lines 68 -> 0, process peak 2,528.4 -> 2,516.2 MB, GPU body pool peak 254,399 both,
  refusals 56/70 -> 59/69 (start-up load variance: 218b had 58/70 at these poses; no pool code touched), CPU pool peak 494,535 -> 494,646.
  Rule 11 budget (refusals not up, peak <= +50 MB): within it. Valve evictions are not printed by this capture mode.
- `-diag-job-temp-memory-leak-validation` printed no callstacks in the release player (attribution from the Dispose stacks instead).
- Fast suite: PASS 1061 FAIL 0 SKIP 25 (`Logs/overnight/223/fast.out`). No frame timing.
- Looked at: `Screenshots/223/after/ws_fixed_walk1_native_sf0.png` (jungle canopy, ponds, river, snow forest behind, nothing missing;
  4 px differ from before) and `.../ws_fixed_jungle_high_native_sf0.png` (full canopy; 42,922 px differ, all in rows 0-317 = far band, as 218b).
## Left unfinished / next
- Nothing open. Efficiency note (rule 11 a): per-worker REUSED scratch would also skip the 2 MB malloc per chunk; not done (worker
  plumbing, gain only allocator time). If a perf task wants it, `ColumnPass` / `ShellChunk` are the per-worker scratch to extend.

## For the doc
- "older than 4 frames" warnings per launch: 169 -> 0; "JobTempAlloc" warnings: 68 -> 0 (`Logs/overnight/223/player_before|after.log`).
- Process memory peak: 2,528 -> 2,516 MB (same poses/flags; `Screenshots/223/*/lookreview.txt`). GPU refusals 56/70 -> 59/69 (load noise).
- Generator output hash identical before/after (`Logs/TerrainGen/temp223/hash_*.txt`).
- Audit: 3 parallel jobs in the whole engine, none allocates per iteration; largest temp scratch in flight 14.4 MB (`AUDIT_TEMP_ALLOC.md`).
- Image: `Screenshots/223/after/ws_fixed_walk1_native_sf0.png` -- walk1 after the fix: whole frame (canopy, ponds, river, far snow)
  matches before to 4 pixels; nothing visible changed, which is the point.
