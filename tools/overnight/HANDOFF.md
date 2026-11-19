STOP_FOR_OWNER -- 242's scatter upload is DONE and committed (ceaf0a5). Upload per edit went from ~180 MB to ~2.5 MB, and the GPU copy is
exact at every step. The main-thread target is still just missed: edit p99 is 1.01-1.40 ms, target < 1 ms. What is left is merge
apply (max 0.5-2.4 ms) + snapshots (0.5 ms budget) + flood done, not upload (max 0.23-0.32 ms).
Question: (a) accept ~1.0-1.4 ms edit p99 (recommended: upload was the named cause, and it is fixed); (b) a follow-up task that slices
merge apply (e.g. MergeBatch 1,024 -> 512 for edits only, or apply split across frames); (c) something else.

# 242-torch-flood-scatter-upload: DONE (upload ~180 MB -> ~2.5 MB per edit, exact); p99 < 1 ms NOT met (1.01-1.40 ms)

## What changed (commit ceaf0a5 on engine-phase)
- `TorchFloodPool.cs`: `UploadScatter` (default; `-tfscatter 0` / capture `-costarms tfscatter_off` = 170's `UploadWhole`, unchanged bytes).
  Burst `ScanJob` + `PackJob` pack the dirty 8-word units (n indices, then n x 8 words) into one staging send. A CommandBuffer per Upload
  does the staging `SetBufferData`, then `CSFloodScatter` per buffer. A buffer that grew is sent whole in the same CommandBuffer.
  Added `MirrorTwin`/`VerifyMirror` (a C# twin of the GPU buffers for tests) and stats (sends by kind, staging peaks).
- `TorchFloodBuild.cs` ApplyJob: tables dirty = 1 bit per 8-word unit (was 1 byte per table). A NEW table is marked whole, so its zeros
  reach the GPU. The old path derives its per-table flags from these bits.
- `TorchFloodProbe.compute`: kernel `CSFloodScatter` (64 threads; thread t writes word t&7 of unit t>>3).
- Tests (`TorchFloodL3Tests`): every pool runs the mirror twin, and `Step` / Budgeted / the new test check twin == CPU after every
  merge's upload. New test `Scatter_TablesFreedAndReused_TwinEqualsCpu(allowSpan)`: tables appended / freed / reused. As a check, I
  removed the new-table mark and the test FAILED, so it can catch a missed word.
- Capture `PlaygroundCapture.TorchFloodL3.cs`: the `tfscatter_off` arm, plus sends / staging printed per wait. TASKS.md paragraph.
- **Run 1 found a GPU ordering bug** (`Logs/costsplit_2026-11-19_153002.txt`). After D1, readback FAIL `tables[250561] 8017EE18 vs 0`
  (one stale word), while the twin passed. Fix: every incremental write goes through the dispatch (no span copies into the buffer the
  scatter also writes), and the staging is a ring of 3. After the fix, every step PASSes. I did not isolate which of the two caused it.
- Why this design is the efficient one (rule 11): it sends only changed units, with 12.5 % index overhead. The pack is Burst on the main
  thread (~0.25 ms max, inside "upload"). There is one send + 2 dispatches per merge, and nothing per frame when idle. Staging exists
  only while busy.

## Evidence
- `FILTER=TorchFlood`: PASS 31 FAIL 0 SKIP 1, incl. Slow/Nightly `Fuzz_Long` (300 ops x 2 seeds, twin checked) (`Logs/overnight/242_filter3.out`).
- Fast suite: PASS 1111 FAIL 0 SKIP 25 (`Logs/overnight/242_fast.out`).
- In-player, same build (`Screenshots/242/tfl3_242.txt`; ON `Logs/costsplit_2026-11-19_153953.txt`, OFF `..._154300.txt`):
  live == scratch IDENTICAL, GPU readback == CPU, GPU vs twin 0 levels at A, D1, D2, D3a, D3b, in both arms. No frame-time arms.

| per edit D1/D2/D3a/D3b | 170 final | OFF (this build) | ON (scatter) |
|---|---|---|---|
| uploaded | 187-214 MB | 159 / 178 / 183 / 184 MB | 2.34 / 2.32 / 2.68 / 2.60 MB |
| main thread mean ms | -- | 0.77 / 0.85 / 0.79 / 0.71 | 0.52 / 0.48 / 0.59 / 0.54 |
| main thread p99 ms | 2.6-6.1 | 1.32 / 2.04 / 1.23 / 1.25 | 1.10 / 1.01 / 1.40 / 1.08 |
| main thread max ms | -- | 1.45 / 2.31 / 2.29 / 1.53 | 1.43 / 1.04 / 2.25 / 3.04 |
| upload phase sum / max ms | max 3-4.6 | 11.2-17.6 / 0.61-1.45 | 3.7-4.6 / 0.23-0.32 |
| merge apply max ms | 2.7-5.5 | 0.92-1.73 | 0.52-2.43 |
Camp fill (A): uploaded 323.6 -> 42.4 MB. Fill p99 4.26 ms, max 10.4 (OFF 3.03 / 8.66; 170 3.6 / 9.8). The fill max comes from the 15
list growth ticks (max 10.4 ms: whole re-sends + list copies), as in 170.
- Memory vs 170 final: CPU union 9.30 -> 9.31 MB (table bit mask, ~8 KB), caches 6.60 = 6.60, GPU 9.25 = 9.25 when idle, scratch peak
  13.1 -> 13.04 MB. New while busy only: scatter staging GPU 0.75 MB peak (3 x 0.25), CPU pack 0.25 MB. Both are released at idle.
- Rule 12 pass card, `CSFloodScatter`: fixed size (not per pixel). Runs only on an Upload (1 per merge while the pool is busy, 0 when
  idle). 8 threads per dirty unit (~45-110 k threads per merge); 2 reads + 1 write per thread; no rays, no texture taps. GPU bytes
  +0.75 MB while busy, 0 idle. 8.17 step S4 (local light, torch flood); a share is not meaningful (a data upload, not a screen pass).
  Off arm: `-costarms tfscatter_off`. No P3 (the launch prints no frame time at a pose). Not HEAVY.
- Image looked at: `Screenshots/242/on/tfl3_wall.png` (night camp slope after D1). Torches light the slope, with no black or garbage
  patches. `off/` has the same stills.

## Next step
Owner answers the question above. Not committed: `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*` (build/test side effects).

## For the doc
**242 -- torch flood scatter upload.** The light is the same, exactly: GPU == CPU at every step, in-player.
- Upload per torch edit: ~180 MB -> ~2.5 MB (about 70x less). Camp fill: 324 -> 42 MB.
- Upload work on the main thread per edit: max 0.6-1.45 -> 0.23-0.32 ms.
- Worst frames while an edit re-lights (p99): 1.2-2.0 ms (old path, same build) -> 1.0-1.4 ms. The target was < 1 ms: just missed.
  The rest is merge apply, not upload.
- Memory: unchanged when idle (CPU 15.9 MB, GPU 9.25 MB). +1 MB only while re-lighting.
- Image: `Screenshots/242/on/tfl3_wall.png` -- the night camp slope after the wall edit. Look at the slope: it is evenly torch-lit,
  with no black or garbage patches. That shows the scattered GPU data draws correctly. (Nothing else is visible: the light is byte-identical.)
