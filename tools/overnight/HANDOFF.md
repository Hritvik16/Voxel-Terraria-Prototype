# 101-engine-c1-world-clock: DONE -- world.state (clock, stage byte, per-chunk last-seen + generation stage), tests 4/4, fast suite green

## What changed (commit 54c50b3)
- `Assets/CoreEngine/Streaming/WorldState.cs` (new, namespace `VoxelEngine.Streaming`): the 8.15 row C1 side file `world.state`
  in the delta folder, modelled on `EditOpLog` (main-thread only, `DeltaCodec.WriteAtomic`, `WorldMeta.Crc32` trailer, seed check).
- `StreamManager.cs`: `World` opened beside `ops.log`; `AdvanceClock(dt)`; `NoteGenerated(coord, World.Stage)` on both Resident
  arrivals (normal and generation-failed air); `StampSeen` in `EvictChunk` (clean and dirty); `SaveWorldState()` (stamps residents,
  writes if persistent) every 30 game s from `Update` and at the end of `FlushAllDirty`; `MarkPersistent` when a delta is written/deleted.
- `Phase4Bootstrapper.cs`: `Streamer.AdvanceClock(Time.deltaTime)` in LateUpdate; `world.state(.tmp)` cleared with the deltas/ops.log.
- CLAUDE.md task line. No D.1 / GetVoxel / SetVoxel / §4.4 / chunk or brick layout change; nothing reads the new data (C2, S1 later).

## Picks (owner may change)
- Clock = GAME time (`Time.deltaTime`, scaled), advancing in play AND rigs/capture runs (recommended in the task).
- The file is written ONLY once the save is real: it already existed, the log holds ops, or this session wrote/deleted a delta.
  Runs that save no terrain never create it (most rigs, capture runs). Rigs that save deltas (acceptance rig) write it too;
  their gates count `*.delta` / `*.delta.tmp` only, so nothing there sees it.
- Last-seen is stamped on eviction (in memory) and for every resident chunk at each write, so after a quit "last seen" = the quit time.
  A force-quit loses at most the last 30 game s of stamps (same class as un-flushed deltas).
- Generation stage is kept for EVERY chunk ever generated (= the stage it was last generated at), a superset of §14's pinned chunks;
  S1's pin pass decides pinning from the D.1 files + op log as §14 says, so this byte alone does not pin.
- Size: 21 B per chunk ever visited (a 100k-chunk walk = ~2 MB), encoded on the main thread at each 30 s write. Fine now; if it
  shows up later, C2/S1 can split it or write off-thread.

## Evidence
- `WorldStateTests` 4/4 (filtered run, `Logs/unity_test_101.log`): round trip (clock 1244.25, stage, 3 entries incl. stage 3, same bytes
  re-encoded, file = 19 + 3x21 + 4 B, no .tmp left); missing file -> clock 0 / stage 0 / NEVER / 0, nothing written until persistent,
  NaN/negative/inf steps ignored; CRC flip, 7-byte, truncated, empty, wrong seed -> defaults + warning + `.rejected`, no throw;
  length-lie and NaN clock rejected by TryDecode; StreamManager (flat world): arrivals noted at stage 0, clean eviction stamps
  last-seen = 100, no file while nothing is saved, a dirty eviction makes it persistent and FlushAllDirty writes it (reread: clock 175,
  evicted 100/150, residents 175).
- Fast suite `PASS 857 FAIL 0 SKIP 14` (was 853; 13:45).
- No frame-time measurement (not a performance task); no screenshots (nothing visible).

## Next
- Per 8.15 §11 order: Q1 -> Q2 (C2 catch-up needs step 18). Nothing unfinished in C1.

## For the doc
- Result: the engine now keeps a saved world clock, a world stage byte (0) and per-chunk last-seen + generation stage in `world.state`;
  no behaviour reads them yet (C2 catch-up, S1 stage flip).
- Format (little-endian, v1): header {uint seed, ushort version=1, double clockSeconds, byte worldStage, uint chunkCount} = 19 B;
  chunkCount x {int3 chunk, double lastSeenSeconds (-1 = never), byte generationStage} = 21 B each, sorted by (x,y,z);
  trailer uint CRC32 = 4 B. Atomic `.tmp` + rename like D.1.
- Tests: `WorldStateTests` 4/4; fast suite 853 -> 857 PASS, 0 FAIL.
- No images: nothing visible changed.
