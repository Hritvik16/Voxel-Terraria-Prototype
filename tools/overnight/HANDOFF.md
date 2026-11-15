# 207-bug-gpu-retry-only-what-fits: DONE. A full GPU body pool now retries only the refused chunks that fit. Over the same 3 poses, refusals fell 2,806 -> 685 and chunks refused again after a retry fell 2,180 -> 41.
## What changed (commit 93494bd; no pool size, layout, shader, eviction or CPU-valve change)
- `TerrainClipmap.RetryGpuRefused` (TerrainClipmap.GpuPool.cs) runs at the start of every upload pass and works nearest to the camera first
  (Chebyshev XZ, the dirty queue's own order). A refused chunk is re-queued, and leaves the refused set, only if the bodies it asked for when it
  was refused fit. "Fit" means: free bodies minus the bodies promised to earlier re-queued chunks that are still in the dirty queue.
  - `Refuse` stores the bodies asked for (dense, non-sealed bricks). Promises (`_gpuRetryPending`) end once that upload has run (purged per pass).
  - A chunk that does not fit stays refused (197's hand-over keeps drawing it); a farther, smaller one may go ahead of it. Evicted refused
    chunks are forgotten; one that left the box is re-queued to park. The sort runs only when free-minus-promised or the camera chunk changed.
- 197's hand-over rule (`Tier0LacksChunk`, `FarField.ColumnState`) is unchanged.
- `-retryfit 0` = the old retry (launch-wide; `TerrainClipmap.RetryOnlyWhatFits`), used for the before pictures.
- New counters `GpuPoolRetriesTotal`, `GpuPoolReRefusedTotal` (retried, then refused again = the churn), `GpuPoolRetryPending` on `[mirror] box`.
- Tests: `GpuRetryFitTests` (8). They check that k freed slots re-queue exactly min(k, N), nearest first (k = 1/2/4/6, N = 4), and that nothing
  leaves the refused set without a slot. They also cover promises held across deferred passes (a 3rd free slot takes exactly one more), a near
  chunk too big for the free slots staying refused while a farther one goes, evicted chunks being forgotten, and the `-retryfit 0` churn.
  `Tier0LackHandOverTests` (6) are still green and unchanged.
- TASKS.md paragraph added. The KNOWN_ISSUES "GPU body pool full" entry has a 207 line.
## Evidence
- Fast suite: **PASS 1041 FAIL 0 SKIP 21** (`Logs/overnight/207/fast.out`; before: 1033 / 0 / 21, HANDOFF 206). Filter run 35/35 (`filter3.out`).
- Launch pair, one build, 197's poses (forest, jungle_high, town_eye), `-mirrorprobe 1`. b1 = ON (`Logs/overnight/207/look_b1.out`),
  b2 = `-retryfit 0` (`look_b2.out`). Driver: `Logs/overnight/207_pair.sh`.

  | per pose, OFF -> ON | forest | jungle_high | town_eye |
  |---|---|---|---|
  | refusals total (running) | 126 -> 72 | 2,806 -> 685 | 2,806 -> 685 |
  | retries total | 126 -> 72 | 2,806 -> 685 | 2,806 -> 685 |
  | re-refused (churn) | 52 -> 0 | 2,180 -> 41 | 2,180 -> 41 |
  | refused at the shot | 0 -> 0 | 0 -> 18 | 0 -> 0 |
  | resident-but-missing at the shot (dirty) | 0 -> 0 | 28 -> 58 | 0 -> 11 |
  | hand-over columns | 47 -> 43 | 98 -> 94 | 3 -> 22 |
  | dirty set at the shot | 230 -> 0 | 573 -> 86 | 0 -> 37 |

  197's f1-f3 at jungle_high (pre-201a code): refused 0, missing-dirty 107, hand-over 139 / 135 / 129, dirty set 832.
- Read this honestly: the fix cut the CHURN, i.e. re-uploads that were refused again (2,180 -> 41) and total refusals (-76 %). The single-frame
  "missing at the shot" count is NOT lower. Since 201a (sealed bricks), the pool is only full while a pose settles. At the shot, 0-18 chunks are
  refused, and both modes have caught up within 5 s (`[mirror +5s]`: 1 of 24 still missing ON, 2 of 14 OFF).
- A first launch a1 (`look_a1.out`) used an in-launch `_rf0` arm. It never exercised the old retry (0 refused at that arm: retries did not move),
  so the arm was dropped and replaced by the launch-wide flag. 3 launches in total; a1 is kept only as an extra ON jungle_high frame.
- Shots: b2 jungle_high (OFF) has the blue water face + grey rock face at the lake shore (right edge); b1 (ON) has forest there with a thin
  grey face; a1 (ON, same code) has the water face again. So it is 197's hand-over side effect (87-98 hand-over columns in all three frames),
  varying by frame; this fix does not remove it.
- Rough cost (rule 10, not a timing claim; one launch each): settle frames, OFF -> ON: forest 152.7 -> 152.9 ms, jungle_high 214.1 -> 234.0 ms,
  town_eye 133.8 -> 149.4 ms. The streaming state differed (pending loads at jungle 436 OFF vs 742 ON). Per pass, the new code does one dictionary
  purge (<= pending entries) plus a sort of the refused set only when its key changes (<= a few hundred int3). Not believed to cost 15-20 ms;
  unverified.
- Memory (rule 11), OFF -> ON: GPU body pool peak 254,400 both, refusals 2,806 -> 685; CPU pool peak 493,475 -> 494,431 (valve 484,500;
  valve evictions 40,111 -> 41,214). Process peak 2,673 -> 2,741 MB (+68, over the +50 default). The new data is bounded by the refused set
  (two Dictionary<int3,int> + two lists, < 0.2 MB at 3 k entries); 206 saw 2,512-2,652 MB with identical code. Read as launch noise, unproven.
## Not done / caveats
- The pool is still full while a pose settles (jungle_high +552 refusals ON). That is the pool-size matter (201a: chunk-border sealed bricks, duplicates).
- Valve evictions (~41 k per run) are untouched: that is task 208.
- Next: task 208 (keep in-view chunks out of the CPU valve).

## For the doc
- Over the same 3 poses in one build, the retry now re-queues only chunks with free slots. Chunks refused again after a retry: 2,180 -> 41.
  Total refusals: 2,806 -> 685 (`Logs/overnight/207/look_b2.out` -> `look_b1.out`, `[mirror] box` lines).
- jungle_high dirty upload queue at the shot: 573 -> 86 chunks. Hand-over columns about the same (98 -> 94). Missing chunks at the shot are not
  lower (28 -> 76 incl. 18 still refused, one frame each); both catch up within 5 s.
- Fast suite PASS 1041 FAIL 0 (+8 tests `GpuRetryFitTests`). Memory: GPU pool peak unchanged; process peak +68 MB between launches (see caveat).
- Images:
  - `Screenshots/207/b2/ws_fixed_jungle_high_native_sf0.png` (BEFORE, old retry): look at the right edge, the vertical blue water face and
    grey rock face at the lake shore.
  - `Screenshots/207/b1/ws_fixed_jungle_high_native_sf0.png` (AFTER): the right edge is forest with a thin grey face. But
    `Screenshots/207/a1/ws_fixed_jungle_high_native_sf0.png` (also AFTER) still shows the water face. It is a hand-over effect that varies
    by frame, not something this fix removes.
