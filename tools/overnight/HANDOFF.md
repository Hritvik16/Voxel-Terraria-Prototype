# 88-cavern-cell-precompute: DONE. Cavern candidates computed once per 102.4 m cell; PrepareColumn 345 -> 125 ns/column, output bytes unchanged.
## What changed (commit `e7a39a3`, branch engine-phase)
- `DetailField.CavernCells` (inside 86's `NodeCache`, so every cached caller gets it: ColumnJob, ShellColumnJob, ShellFill sub-samples,
  DevMap): the passing cavern candidates (centre, axis, R, stretch, centre y, floor depth, ceiling) of a cell's 3x3 x 4 layers,
  computed once per cell. 4 slots keyed by the cell (gx, gz), slot by parity, so shell chunks spanning many cells (shells >= 4)
  read the right ones without thrashing. A column now runs only the ellipse test on the stored list.
- Byte-identical by construction: the old `CavernInterval` is split into `CavernCandidate` (hashes) + `CavernInterval` (geometry);
  the uncached path calls the same two, in the same order (cy, j, i), first two hits. Uncached `PrepareColumn` (FarField, tests)
  and `PrepareColumnExact` are unchanged. > `CAVERN_CY_MAX` (8) cavern layers -> uncached (seed 42 tg2 has 4).
- Also used at `coarseColumnShift` 0 (`-coarsecols 0`) and on map v3 (same bytes).
- `TerrainSpec.cavernCellCacheOff` (byte, 0 = cached) + `TerrainGenChunkSource.SetCavernCellCache(bool)`: the A arm of the tests.
- New `CavernCellCacheTests` (2): columns (Slow, 17 s in Mono) and chunks/shells (fast, 4.6 s). `ColumnPassSplitTests.CavernCache_TimingReport` (report only).
- Housekeeping: task 87's blocked run left an untracked `Assets/CoreEngine/Tests/SmallWindowTests.cs` behind (its patch was reverted, the
  test was not); it would have broken the compile. Moved to `tools/overnight/blocked/87-SmallWindowTests.LEAKED.cs.txt` (37e's precedent).
  **Whoever runs 87 again: restore it from there along with the patch.**
## Evidence
- Whole suite **PASS 773 FAIL 0 SKIP 23** (Logs/overnight/88-all.out; was 771/0/22: +2 tests, +1 report-only skip). Wall 27m38s.
- Bytes (Screenshots/88/cavern_cache_columns.txt, cavern_cache_identical.txt): 25,600 columns cached == uncached == cache-off, and shift-0
  cached == `PrepareColumnExact` (16,384 random columns + a 96x96 block round cell corner (19456, 65536); 12,807 columns with a cavern,
  2,959 with two). The block filled 4 cells for 4 cells (no thrash). Chunks: 12 mixed 0.1 m chunks (4 chunk columns round the corner,
  cavern layers 24-25 + surface) + 14 shell chunks at shells 1/3/5/7 (cavern layer + surface, both sides of the corner): equal FNV-1a.
- **Timing, warm steady state** (warm-up `Logs/tg2world_20261015_103535.txt` (BUILD=1 first: no Playground.app existed), test straight after;
  Editor Burst, one thread, ABBA, isolated generation -- a direction check, NOT a streaming or frame-time claim; Screenshots/88/cavern_cache_timing.txt):
  PrepareColumn per chunk column 5.66 -> **2.05 ms** (345 -> 125 ns/column; 86 measured 309 ns before, same order);
  whole 7-layer chunk column 88.29 -> **84.71 ms (x0.959)**; shell chunks B/A: 1 0.948, 2 0.945, 3 0.961, 4 0.980, 5 0.970, 6 0.981, 7 0.970.
- No screenshots: output bytes do not change (tests above prove it), so nothing visible can differ.
## Left / notes
- The column pass is now dominated by ShapeField.Sample (~10 ms per chunk column in 86's split) and the voxel pass; PrepareColumn is ~2%
  of a 7-layer chunk column. Further PrepareColumn work is not worth it.
- Not measured: streaming throughput in a real player (a milestone perf task should).
## Next step
89 as queued (shells perf breakdown). 87 is in blocked/ (usage limit) -- the supervisor requeues it with its leaked test file.

## For the doc
- PrepareColumn (per-column cave/cavern step): **345 -> 125 ns per column** (5.66 -> 2.05 ms per 0.1 m chunk column).
- Whole 7-layer chunk column: **88.3 -> 84.7 ms (-4.1%)**; shell chunks 1-7: -2% to -5.5%.
  (Editor Burst, warm, ABBA; Screenshots/88/cavern_cache_timing.txt)
- Output bytes: **identical** -- 25,600 columns, 12 chunks and 14 shell chunks (shells 1/3/5/7) across a cavern cell corner (Screenshots/88/cavern_cache_identical.txt).
- Whole suite 773 / 0 / 23.
- No images: nothing visible can change (byte-identical).
