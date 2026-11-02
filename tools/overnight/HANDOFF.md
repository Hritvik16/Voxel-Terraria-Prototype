# 168-perf-next-exact-cut: DONE. Cut D built and exact (0 px), but it saves ~0 ms, so it stays behind `-roofskip 1`. Next cut = C1.

**Which cut and why:** D. In 167's warm AC arms (`Logs/costsplit_2026-11-01_151803.txt`), `skyprobe_off` = -6.2 ms at walk_near, which is >= 2 ms (`PERF_EXACT_CUTS.md` section 4, step 1).
This session resumed after the first 168 session hit the usage limit at 08:01. That session had already built D, run the exactness launches and done the
warm measurement, so no new launch was needed.

## What changed (`af2edf6`)
- Roof skip ON now draws a diet-2 kernel: `CSMainFarShadeLeanRoof` (`VE_DIET2_ROOF`, `KernelDiet2Rule.RoofKernel`). It no longer falls back to the twin kernel.
  Exactness scope: only rays whose exit point nothing reads use the skip (the sky probe, plus sun rays without shell continuation). The diet-2 roof kernel never
  skips the sun ray, because task 133's shell continuation starts from that exit point.
- Incremental roof pyramid: `TerrainClipmap.HeightDirty.cs` records which ring chunk columns were written. `RaymarchFeature.RoofInc.cs` plus the
  `BuildHeight*Cols` kernels rebuild only those columns. `-roofverify N` compares the result against a full rebuild on the GPU.
- Cost-split arms `roof_on`, `roof_full`, `roof_off`; `-costroofpair`; look review `-cliffbefore 6`; `tools/roof168.py`. Tests: `RoofPyramidIncrementalTests` and `KernelDiet2Tests`.
- **`-roofinc` now defaults to OFF** (this session's change, after reading the verify log; see "Open bug" below). With `-roofskip 1`, the pyramid is fully rebuilt every frame (task 59's exact build).
- No change to play: roof skip stays OFF. Memory added: about 1 KB for the dirty-column buffer. The verify buffer is allocated only with `-roofverify`. Well under 3-6 MB.

## Evidence (warm steady state, on AC, 432 rows, play flags)
- Warm-up `Logs/tg2world_20261102_075153.txt` (discarded). Cost split `Logs/costsplit_2026-11-02_075812.txt`: FULL x4 interleaved with roof_on x6, both views, on AC throughout.
  Streaming was pinned, so the timed passes paid no pyramid upkeep ("incremental 0 (0 columns)"). This is D's best case.

| view | FULL p50 (4 runs, range) | roof_on minus FULL p50, 3 pairs | verdict |
|---|---|---|---|
| walk_near | 36.8 ms (36.6-37.1, 0.6) | -0.3 / -0.4 / +0.1 | ~0 |
| walk_open | 20.6 ms (19.9-21.5, 1.6) | -0.7 / -0.2 / +0.4 | ~0 |

- The skip does change the rays: at walk_near, far PRIMARY steps per frame went from 9,044 to 1,945, and the share of sky rays ended by the sky exit went from 89 % to 97 %. It saves no time, though:
  the sky probe's 6.2 ms (167's arm) is not spent in its walk through the tier-0 window.
- 0 px proof (`UpscalerClips/2026-11-02_064444/stills`, `python3 -I tools/roof168.py pair|arms`): at walk_near and walk_open, the in-place flip (raw2/flip, flip/back)
  and roof_on vs FULL raw stills are all **0 px**. cliff1 A native, roof off vs on (`LookReview/2026-11-02_065242_high`): **0 px**.
  cliff2 A native (not a required pose): 3,947 px. All of them sit in a distant horizon strip on the left (x 0-324, y 511-586 of 1080), beyond the window the skip
  covers. Shells were still filling during those shots (s3/s4 missing 385/156), so streaming is the likely cause, but this run does not prove it.
- Fast suite: PASS 985 FAIL 0 SKIP 16 (`Logs/overnight/168_fast.out`). The first session's filtered run passed 21/21 (`Logs/overnight/168_filter.xml`).

## Open bug (not fixed; the feature is off)
- The streaming warm-up with `-roofskip 1 -roofverify 4` (`Logs/overnight/168_warmup_Player.log`, grep `roofverify`) found the incremental pyramid differed from a full rebuild
  on 774 of 3,749 checks (29-4,718 words each). Some of those were idle frames with no dirty columns. The errors are transient and clear within a few checks.
  So some input changes without a recorded column write. Already ruled out: other ClipmapBuffer writers (only `WriteClipmapChunk` writes after construction),
  lock-buffer uploads (off), `ReadClipmap` (reads only the ring slot), and the rebinding of the full-build kernels. Not yet ruled out: an error in the verify itself, and the order of
  `SetData` vs the render-graph compute pass. This matters only if D is revisited.

## Next
- Per the decision table: the next task is **C1** (shadow continuation out of the big kernel). The supervisor queues it. Then E, then the milestone walk.
- Not a measurement follow-up: the incremental-pyramid mismatch above, only if D ever comes back.

## For the doc
- Cut D (roof skip in the diet-2 kernel + incremental pyramid), warm AC, 432 rows (`Logs/costsplit_2026-11-02_075812.txt`):
  walk_near 36.8 -> about 36.8 ms p50 (roof_on minus FULL -0.3 / -0.4 / +0.1, noise 0.6); walk_open 20.6 -> about 20.6 (-0.7 / -0.2 / +0.4, noise 1.6). **~0: stays off.**
- Exact: 0 px at walk_near and walk_open (in-place flip and arms) and at cliff1 A native. Memory added: about 1 KB.
- Why there's no gain: the skip cuts window steps (far primary steps 9,044 -> 1,945 per frame), but the sky probe's 6.2 ms is spent elsewhere.
- Incremental pyramid not exact under streaming (774 of 3,749 verify checks bad), so `-roofinc` is off by default.
- Next cut: **C1**.
- No images: nothing visible changed (0 px). The 0 px stills are in `.../UpscalerClips/2026-11-02_064444/stills/` (walk_near_full_raw.png vs
  walk_near_roof_on_raw.png: the same noon walk view, identical to the pixel).
