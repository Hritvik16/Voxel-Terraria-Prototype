# 224-perf-reuse-gen-scratch-per-worker: DONE -- generator scratch reused per worker; output bit-identical; peak 2,602 -> 2,583 MB
NOTE for the supervisor: the memory watchdog touched `tools/overnight/PAUSE` at 19:46 (system swap 10,342 MB while my first fast-suite
run was going; owner's Firefox open, swap built up over the day). Swap drained to ~5-6 GB, the rerun passed. PAUSE is still there: clear it
(`rm tools/overnight/PAUSE`) when the Mac is fit to go on. `Logs/overnight/watchdog.log` last 2 lines.
## What changed (commit fde5224)
- `TerrainGenChunkSource.cs`: `ColumnPass` (already one per StreamManager worker) now also holds Generate's voxel scratch (`mats` 128^3,
  brick mat/uniform, stats = 2.06 MB), made on the worker's first non-air chunk (`EnsureVoxelScratch`), reused after; no per-chunk malloc.
- `.ShellFill.cs`: new `ShellScratch` (fill arrays + voxel stats, ~0.46 MB); `.Shell.cs`: new overload
  `GenerateShellChunk(shell, coord, output, ops, summaries, scratch)`; `ShellStreamer.WorkerLoop` makes one per worker thread (and one for
  the synchronous path). Null pass / scratch = one-shot per call, the old behaviour (tests, reports).
- Why this design is the efficient one (rule 11 a): the scratch is held once per WORKER (7 + 3), not per chunk and not in `ShellChunk`
  (2 x workers + 2 of those wait for upload: x2.7 the memory). Held total ~15.8 MB, the same as the 223 in-flight peak with all workers busy.
- Reset per use = exactly a fresh allocation: ClearMemory arrays zeroed (8 KB near; ~0.4 MB shell), lists emptied; `mats`/`scanLo` were
  UninitializedMemory and are always written before read (VoxelJob writes every non-proven brick; every overlay expands a uniform brick first).
- Tests: `GenScratchHashTests.VoxelReuseIsExact_DenseThenSparse` (fast, 9.5 s), `ShellScratchReuseIsExact_DenseThenSparse` (Slow, 12 s):
  a dense chunk, then sparser targets (proven-air + proven-solid bricks, shared and refilled column, wholly-air fast path), once plain and
  once with the scratch `Scribble`d with junk, vs fresh scratch: bytes, brick table, skip/fill stats. Probe `GeneratorOutput_Hash` now hashes
  both paths and asserts equal. TASKS.md "Task 224", AUDIT_TEMP_ALLOC.md follow-up section.
## Evidence
- Bit-identical: `Logs/TerrainGen/temp223/hash_after224.txt`: one-shot `near 69CE4AAEAE2F03BC shells 7E504985E4B9FDDF` = 223's, reused
  path the same two hashes. Adversarial tests pass (near targets: 3 went through the voxel pass, 3064/1212/109 proven-air bricks).
- Launch pair `Logs/overnight/224_pair.sh` (223's flags: look review high, jungle_high + walk1), after 19:09, before 19:25, back to back;
  logs `Logs/overnight/224/player_before|after.log`, ledgers `Screenshots/224/before|after/lookreview.txt`:
  process peak 2,602.2 -> 2,582.8 MB; GPU body pool peak 254,399 -> 254,396, refusals 58/70 -> 57/69; CPU pool peak 494,808 -> 494,485
  (valve 484,500); aged-TempJob / JobTempAlloc warnings 0/0 -> 0/0. Budget (refusals not up, peak <= +50 MB): within it.
  Valve evictions are not printed by this capture mode. (223's after run read 2,516 MB: launch-to-launch spread is ~±50 MB.)
- Pictures: walk1 before vs after 0 px differ; jungle_high 10,315 px, all in rows 0-215 (far band, as 223/218b).
- Fast suite: PASS 1062 FAIL 0 SKIP 25 (`Logs/overnight/224/fast2.out`; `fast.out` = the run the watchdog killed). No frame timing.
## Left unfinished / next
- Nothing open. Mishap, harmless: my copied pair script's first run wrote its shots into `Screenshots/223/after/`; 223's files were backed
  up first and restored (lookreview.txt compared identical). The script is fixed.

## For the doc
- Malloc/free per chunk build: ~2.06 MB (near) / ~0.46 MB (shell) -> 0; scratch now held once per worker (~15.8 MB total, 7 + 3 workers).
- Process memory peak: 2,602 -> 2,583 MB (back-to-back pair, `Screenshots/224/before|after/lookreview.txt`).
- GPU refusals 58/70 -> 57/69; CPU pool peak 494,808 -> 494,485; aged-TempJob warnings 0 -> 0.
- Generator output hash unchanged: near 69CE4AAEAE2F03BC, shells 7E504985E4B9FDDF (`Logs/TerrainGen/temp223/hash_after224.txt`).
- Image: `Screenshots/224/after/ws_fixed_walk1_native_sf0.png` -- walk1 after: the whole frame (jungle canopy, ponds, river, snow forest
  behind) is identical to before to the pixel; nothing visible changed, which is the point.
- Image: `Screenshots/224/after/ws_fixed_jungle_high_native_sf0.png` -- jungle_high after: full canopy, nothing missing; the only pixels
  that differ from before are in the top ~215 rows (the far band at the horizon), as in 223's and 218b's pairs.
