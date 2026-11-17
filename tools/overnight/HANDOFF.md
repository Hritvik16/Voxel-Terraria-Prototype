# 227-perf-sky-fill-lowres: DONE -- sky fill traced once per 4x4 pixel block; walk_near p50 106.8 -> 72.3 ms (warm), no visible change; commits abda510, 70b1a45

## What changed (Raymarch.compute, RaymarchFeature.cs, SkyFillReference.cs, KernelHealth.cs, PlaygroundCapture.CostSplit.cs, 2 test files, TASKS.md)
- Where the 40 ms went (212): one cosine sky-fill ray per tier-0 pixel per frame (2 legs on a ground-half sample). Most of these are
  long, near-level rays that run to the window edge (up to ~200 m), and every pixel of one voxel face traced the SAME ray (the seed is the face).
- New design (`-skyfillres 1|2|4`, DEFAULT 4; 1 = the old inline path): the primary kernel no longer traces. It shades with the face's
  running mean, now one frame old. New kernel `CSSkyFillTrace` runs ONE thread per 4x4 block (1/16 of the rays). It traces one face of
  the block exactly as before (same seed, half and direction) and folds the sample into every pixel of the block on the SAME FACE PLANE:
  same normal, same coordinate along it, and within twice the block's pixel footprint. That rule is the depth/normal-aware filter:
  trunk vs ground, ledge top vs floor, and trunk vs wall behind it never share. A face with no history takes a compatible reprojected
  neighbour's record. Only with no neighbour does it trace inline (exact; no guessed value).
- Why this design is the efficient one (rule 11a): no new G-buffer and no new UAV (the main kernels sit at Metal's 8-UAV cap). The face
  rides in the existing history record (8 -> 12 B). The trace pass is compacted, so it costs 1/16 of the SIMD work, not 1/16 of the lanes.
- Bug found and fixed on the way: the first build's share test (a bool select after an early return) came out of HLSLcc as
  `as_type<float>(bool)`. Metal rejected it, so CSMainFarShadeFlood (play's kernel) did not load: magenta frames, same failure as task 206.
  Rewritten as integer arithmetic (`SkyShareBad`). A test pins "no `?`/`==` in it" and that it equals the plain rule on 50k cases.
  That launch was killed and discarded (`Logs/overnight/227/player_day_broken.log`).
## Evidence
- Fast suite `PASS 1076 FAIL 0 SKIP 25` (`Logs/overnight/227/fast.out`). New LowRes_* tests (SkyFillReferenceTests, CPU twin `LowResFrame`):
  ledge top stays EXACTLY 1.000 beside a 0.3 m slot floor (0.034, quadrature 0.057). Trunk / wall / ground stay within 0.07 of their own truth.
  A one-face block matches the inline path (0.466 vs 0.468, 0.616 vs 0.612, 0.628 vs 0.626).
- Timing: WARM steady state (warm-up `Logs/tg2world_20261117_045823.txt`). One cost-split launch, arms F, then X X F (ABBA), High, 432 rows,
  play flags (`Logs/overnight/227/costsplit_day.txt`). PLAN written before measuring: `Logs/overnight/227/PLAN.md`.
  p50 ms        inline(before)   2x2     4x4(default)   sky fill off
  walk_near        106.8         78.0      72.3           66.3      -> sky fill cost 40.5 -> 6.0 ms
  forest_eye       140.4        108.9     102.5           90.5      -> 49.9 -> 12.0 ms
  jungle_eye       109.8         75.4      66.5           68.1      -> 41.7 -> ~0 (framing shifts between arms here, known 221 issue)
  4x4 beat 2x2 by >= 5 ms at every pose with no visible cost. Per PLAN that makes 4x4 the default.
- Pictures (diff vs the inline path; `Logs/overnight/227/diff.txt`; noise floor 221): mean|d| / px>32 %: walk_near 2x2 1.82/0.17, 4x4 1.99/0.29
  (floor 0.90/0.03; sky fill off 2.60/0.44); forest 1.99/0.16, 2.14/0.22 (floor 2.06/0.36; off 9.26/12.95); jungle 1.92/0.37, 2.12/0.35
  (floor 0.67/0.08; off 11.99/7.55); torch_camp night 1.09/0.02, 1.01/0.01 (floor 0.80/0.01). The zooms show what the residual is:
  the old path's per-pixel speckle on far shaded ground and on trunks is gone, and there is NO bleed at trunk edges or trunk feet.
- Rule 11/12: P1 GPU bytes +2.65 MB (history 2 x 768 x 432 x 12 B = 7.96 MB, was 5.3; +7.4 MB at 1280x720). Process peak 2,564.9 MB (day)
  / 2,495.6 (night) vs 226's 2,548 / 2,558. GPU refusals 0 / 0 / 5,787 (walk / forest / jungle; 226: 0 / 0 / 7,979, same path effect).
  CPU pool peak 497,865 (226: 496,935). P2 pass card CSSkyFillTrace: render rows, every frame, 1 thread per 4x4 block, 1 sky-fill ray
  (2 legs on a ground half) + 16 record reads + <= 16 read-modify-writes; 8.17 sky-fill/shade step, its share drops from ~40 to ~6 ms.
  P5: sky-fill rays/frame drop from 1 per tier-0 pixel to 1/16 + orphans. No counter line exists: the ShadowStats counters live in the
  Diag kernels this build strips. Not HEAVY: no output-res pass, no new per-pixel march, peak within +50 MB.
## Left / next
- Fast-lighting idea for the preset ladder, as numbers only (defaults unchanged): 4x4 = 72 ms at walk_near. "Lumen-Low-like" further
  steps would be a sky-ray distance cap or 8x8 blocks. Not done: one performance change per measurement.
- The default flip to 4 is a C# default plus a shader comment, made after the measured build. The 4x4 code ran as arm sfres4 in that build.
  The next build carries it.
- Not committed (not mine): `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*` edits.

## For the doc
- Sky fill (shade light) now traces one ray per 4x4 pixel block instead of one per pixel (`-skyfillres 1` = old). walk_near p50 106.8 -> 72.3 ms;
  forest_eye 140.4 -> 102.5 ms; jungle_eye 109.8 -> 66.5 ms (warm, ABBA; `Logs/overnight/227/costsplit_day.txt`).
- Sky fill's own cost: ~40-50 ms -> ~6-12 ms. Sky fill off would be 66.3 ms at walk_near, so the shade light now costs ~6 ms.
- GPU memory +2.65 MB (history record 8 -> 12 B). Process peak 2,565 MB.
- `Screenshots/227/sheets/doc_sheet.png`: rows walk_near / forest / jungle / torch_camp night; columns before | 2x2 | 4x4 | sky fill off.
  Columns 1-3 look the same; only column 4 (off) shows the black trunks this light fixes.
- `Screenshots/227/sheets/zoom4_forest_eye.png` (and `zoom4_jungle_eye.png`): trunk feet at 3x, before | 2x2 | 4x4. Look at the trunk edges:
  no grass colour bleeds onto bark and no halo forms. Before has dark speckles on the bark and far ground; after is smoother.
