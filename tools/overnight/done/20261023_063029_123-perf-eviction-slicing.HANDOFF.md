STOP_FOR_OWNER -- Q (task 123): eviction slicing works, but it did not make walking smoother, so I left it OFF. Pick one:
(a) keep it OFF; next, find out what makes a chunk-crossing frame slow on the GPU side (recommended);
(b) turn it ON anyway (it takes 5-17 ms of CPU work off ~15 frames per walk; frame times did not improve);
(c) remove the code.

# 123-perf-eviction-slicing: DONE, kept OFF. Slicing removes the eviction CPU spikes, but crossing frames and p99 did not improve.

## What changed (commit 6598a63)
- `StreamManager`: `-evictslice N` (N > 0) queues out-of-range chunks that are still INSIDE the store window (FIFO + dedupe set).
  - `DrainEvictQueue` evicts at most N of them per frame, inside the slide timer.
  - Default `EvictSlicePerFrame = 0` = the old one-frame slab. `EngineConfig.EVICT_SLICE_PER_FRAME = 32` is the value tested.
  - Why 32: a clean eviction costs ~6-7 us (115), so 32 is ~0.2 ms a frame. A slab drains in ~8-30 frames, well before the next crossing (~2 s).
- Only in-window chunks wait. An in-window coord owns its ring slot and its cascade-tier slot, so a queued chunk cannot alias.
  - Out-of-window chunks still go at once under task 26's save cap: teleports, and queued chunks the window has slid past.
- The other rules:
  - A queued chunk that comes back into range is kept (`EvictQueueKeptTotal`).
  - Under pool pressure the queue is evicted first: before an admission's transfer, and before §3.6's LRU valve (`EvictQueueFlushedTotal`).
  - `WaitForIdle` flushes the queue, and `EvictionDeferred` also counts it.
  - The save cap is now counted per frame (`_savesThisFrame`), shared by both eviction paths.
- No frozen API, contract or memory-layout change; main thread only.
- `Phase4Bootstrapper` reads `-evictslice`.
- New tests: `EvictionSlicingTests` (4):
  1. Each frame evicts at most the slice, and every resident chunk stays inside the window.
  2. Slice 0 evicts the whole slab in the crossing frame (old behaviour).
  3. Re-entry: 24 of 25 queued start chunks were kept.
  4. Pool pressure: the queue went 17 -> 2 and 4 in-range chunks were kept.
- `tools/crossing-report.py` reports crossing frames, eviction bursts and slide ms (`SKIP=N` drops run-start frames). TASKS.md paragraph added.

## Evidence
- Fast suite: `PASS 911 FAIL 0 SKIP 15` (`Logs/overnight/123-tests.out`). Filtered runs: `DeferredEvictionTests` (task 26) still green.
- Warm steady state, AC power, no owner pause.
  - Warm-up (discarded): `Logs/tg2world_20261023_055330.txt`.
  - 3-min `MODE=walk` A B B A with task 115's flags (FSR 3 at 540 rows, quality, shells ON, noon, `-spikelog`). A = `-evictslice 0`, B = 32.
  - Logs: A1 `Logs/tg2world_20261023_060112.txt`, B1 `..._060559`, B2 `..._061047`, A2 `..._061535`. Driver: `Logs/overnight/123-driver.sh`.
- Whole run, from the rig's own line (A1 / A2 vs B1 / B2):

  | | A1 / A2 (OFF) | B1 / B2 (ON) |
  |---|---|---|
  | p50 ms | 48.1 / 47.4 | 47.5 / 47.7 |
  | p95 ms | 81.4 / 78.3 | 80.0 / 80.3 |
  | p99 ms | 96.6 / 103.0 | 107.8 / 108.1 |
  | > 33 ms | 86.3 / 86.1% | 85.9 / 85.5% |

- From `Screenshots/123/crossing_report.txt` (run-start frames dropped):
  - Frames over 100 ms: A 26 / 49, B 52 / 62.
  - Max chunks evicted in one frame: A 1,628, B 32.
  - Slide (eviction) frames > 5 ms: A 15 / 15, B 0 / 0. Slide max: A 17.7 / 16.2 ms, B 2.2 / 2.3 ms.
  - Crossing frames (88 per walk): median A 60.1 / 54.0 ms, B 58.3 / 55.4 ms. No change.
- **Why crossings stay slow:** a crossing frame runs ~10 ms over an ordinary frame, but nearly all of it is GPU wait (`pre_ms` 53.7 vs 43.8 ms).
  - Our own CPU work is only +1.5 ms (`upd` 3.3 vs 1.8). The eviction itself costs ~1 ms even unsliced (big-burst slide median 1.0-1.2 ms; 115 saw 2.5-3.6).
  - Crossing frames upload more: clipmap 831 KB vs 579 KB, shell 50 KB vs 16 KB.
- **The extra > 100 ms frames in B are NOT on drain frames:** 15-18 of 52-62 evict, the same as the 31% of frames that evict anyway.
  - Every one of them had a shell upload: these are 115's unexplained GPU waits.
  - So B's worse p99 may be noise (A alone spans 26-49), but the task's keep-ON rule needs an improvement, and there is none.
- No screenshots: no look change, and the default behaviour is unchanged.

## Left open / next
- The crossing-frame hitch (~+10 ms) is GPU-side. Candidates: the clipmap/cascade uploads on a crossing, and the shell re-centre uploads.
  - Isolating them (suppress one upload kind on crossings, re-measure) is a performance task for the owner to approve. No follow-up queued.
- CSVs for re-analysis (untracked): `Screenshots/123/{A1,B1,B2,A2}_spikes.csv`, cut at 3604 / 3628 / 3637 / 3647 rows.

## For the doc
- Biggest chunk-eviction burst in one frame: 1,628 -> 32 chunks (`Screenshots/123/crossing_report.txt`).
- Eviction (slide) frames over 5 ms per 3-min walk: 15 / 15 -> 0 / 0; slide max 17.7 / 16.2 -> 2.2 / 2.3 ms.
- Chunk-crossing frame median: 60.1 / 54.0 -> 58.3 / 55.4 ms (no change: the cost is GPU wait, +10 ms `pre_ms`).
- Whole-walk p99: 96.6 / 103.0 -> 107.8 / 108.1 ms; > 100 ms frames 26 / 49 -> 52 / 62 (not on drain frames). p50 48 ms both.
- Result: kept OFF (`-evictslice 32` turns it on). No images: nothing visible changed.
