STOP_FOR_OWNER -- 170's memory trim is DONE and committed (d00fc53). Its main-thread target (p99 < 1 ms) is NOT met, and edits got
worse: per edit, p99 went from 1.2-1.6 ms to 2.6-6.1 ms, and the upload from ~8 MB to ~200 MB. The flood is ON in play.
Question: how should the upload be fixed? (a) a small scatter-upload compute pass (a new GPU pass: rule 12 card + `_off` arm; recommended),
(b) allocation that keeps dirty words together, (c) accept it, or put MergeBatch back to 4,096 (4x fewer uploads, apply spikes ~2.8 ms).

# 170-torch-flood-memory-trim: DONE for memory (CPU 31 -> 15.9 MB, GPU 20 -> 9.25 MB, scratch 27 -> 13.1 MB, output exact); main-thread p99 < 1 ms NOT reached

## What changed (commit d00fc53 on engine-phase; a later session committed this tree, the code unchanged since its tests)
- `TorchFloodPool.cs`: `MaxJobs` 2 -> 1; `MergeBatch` 4,096 -> 1,024; snapshots sliced by box brick plane (`ContinueSnapshot`),
  budget `FrameBudgetMs` 0.5 counted from the snapshot phase's start (it was counted from the Tick's start, which starved the single slot:
  fill 26.8 s); `TrimIdle`; `GrowGpu` sizes the GPU tables/data buffers to the CPU list capacity and re-uploads a grown buffer whole.
  The inherited GPU-side copy on growth (`CSFloodCopy`) FAILED the in-player readback (run 1: `tables[0] 00000000 vs 8000FC90`, twin
  probe 511 levels). I removed it; the compute file is back to HEAD. New counters: `GpuGrows`, `GrowTicks`/`GrowTickMsMax`, `SnapshotYields`.
- `TorchFloodBuild.cs`: `SnapshotBegin` + `SnapshotPlanes`; `GrowCap` = need + 1/4 rounded up to 64 Ki words (up from 1/8: about 3x fewer
  growth steps); `Reserve` = ONE exact reallocation through `UnsafeList` Ptr/m_capacity. The inherited Resize + TrimExcess copied twice
  through a power-of-two block. Per-torch cache lists `TrimExcess` on the worker.
- `PlaygroundCapture.TorchFloodL3.cs`: the report prints jobs, merge batch, snapshot yields, GPU grows, list growth ticks.
- Tests: `Budgeted_EditsMidSnapshot_EqualsScratch`, `Reserve_GrowsToGrowCapExactly_KeepsContents`. `TASKS.md` paragraph.
- Why this design is the efficient one (rule 11): there is one scratch, not two. Lists grow by at most 25 % slack, not 100 %. Nothing is
  kept that is not drawn, and the caches stay because the owner asked to keep them.

## Evidence
- `FILTER=TorchFlood`: PASS 29 FAIL 0 SKIP 1 (report-only), incl. Nightly `Fuzz_Long`; scratch == Encode(oracle), incremental == scratch.
- Fast suite: PASS 1109 FAIL 0 SKIP 25 (`Logs/overnight/170_fast.out`).
- In-player, 3 launches (`Screenshots/170/tfl3_170.txt`; final = `Logs/costsplit_2026-11-19_090913.txt`): at camp fill and after D1/D2/D3a/D3b,
  live == scratch IDENTICAL, GPU readback == CPU, GPU vs twin 0 levels, all PASS. The scene now places 28 torches, not 31 (4 refused).
  No frame-time arms.

| (before = 169 ON arm `Screenshots/169/tfl3_169.txt`, 31 torches, pre-236) | before | after (final) |
|---|---|---|
| CPU allocated (union + caches) | 21.1 + 9.9 = 31.0 MB | 9.30 + 6.60 = 15.9 MB |
| GPU allocated | 20.0 MB | 9.25 MB |
| job scratch peak | 26.1 MB (2 jobs) | 13.1 MB (1 job) |
| camp fill until idle | 5.3 s, 95 frames | 14.4 s, 176 frames |
| main thread per busy frame, fill: mean / p99 / max | 0.79 / 3.1 / 10.4 ms | 0.85 / 3.6 / 9.8 ms (run 2: 0.70 / 2.4 / 12.9) |
| edits D1-D3b: p99 / settle | 1.2-1.6 ms / 2.0 s | 2.6-6.1 ms / 4.0-4.6 s |
| uploaded per edit | ~8 MB | 187-214 MB |

## Unfinished: main-thread p99 < 1 ms (owner question)
- The worst frames are upload (max 3-4.6 ms) and merge apply (max 2.7-5.5 ms). They are not snapshots (max 0.5 ms in edits) and not
  list growth (0 growth ticks in edits). Cause: task 236's one `SetData` span per buffer per Upload re-sends first..last dirty word. The
  dirty table/data offsets are scattered (free-list reuse), so each 1,024-brick merge re-sends about the whole 4-9 MB buffer. 4x more merges
  means 4x the bytes. Splitting the span means more calls, and 236 measured about 4 MB of Metal staging per call.
- Options: (a) a scatter upload: ONE SetData of the packed dirty words + offsets into a small buffer, then a compute scatter. That is a new
  GPU pass (rule 12 card + `_off` arm), and run 1 shows dispatch/SetData ordering needs care. (b) Locality-preserving allocation, so dirty
  words cluster. (c) Accept: MergeBatch back to 4,096 means 4x fewer uploads, but apply spikes about 2.8 ms. Recommend (a).
- Fill is 2.7x slower than 169 because one job runs floods in series. That is the owner's Q106 (a) trade; the snapshot budget fix took it
  from 26.8 s to 14.4 s.
- HEAD's play has the flood ON (task 175), so this trim reaches play. No process memory peak line in this capture, so there is no ledger
  number to quote. The pool's own memory went down by 28 MB (CPU 15 + GPU 11 + scratch 13 at peak).

## Not committed
- `Assets/Scenes/Playground.unity` and `Logs/TerrainGen/*` are build/test side effects, left as they were. Stale `blocked/170-*` files: superseded.

## For the doc
**170 -- torch flood memory trim.** Same light, exactly (live == from-scratch, GPU == CPU at every step), using less memory:
- CPU memory 31.0 -> 15.9 MB; GPU 20.0 -> 9.25 MB; flood scratch peak 26 -> 13 MB (one flood job at a time).
- Camp of 28 torches lights in 14.4 s (was 5.3 s with two jobs: the owner's one-job trade).
- Not reached: the main-thread worst frames (p99) are 2.6-6 ms, against a < 1 ms target. The GPU upload re-sends whole buffers. Fixing it
  needs an owner call (recommended: a small scatter-upload pass).
- Report: `Screenshots/170/tfl3_170.txt`. No images: nothing visible changed (the light is byte-identical).
