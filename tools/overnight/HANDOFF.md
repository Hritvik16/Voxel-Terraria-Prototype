STOP_FOR_OWNER -- Q75: "make Medium the default on the M1?" My recommendation is (b), keep High. The trial does not support Medium.
(a) Medium default on the M1: -112 MB, frame time ~unchanged, plus a visible dashed seam at 3.3 km. (b) Keep High (recommended).
(c) Medium only after the 3.3 km seam is fixed, and only for the memory.

# 117-perf-medium-preset-trial: DONE (trial only, no default changed). Medium saves ~0 ms, not the estimated 2-4 ms. It saves 112 MB and adds a seam.

## What changed (branch engine-phase)
- `PlaygroundCapture.LookReview.cs`: three capture-only flags. `-lookrows N` lets High shoot at 540 rows (before, the look review always shot High at 720).
  `-lookposes` limits the poses, and `-lookbursts 0` skips the bursts and golden hour. No product code changed.
- `Screenshots/117/117-driver.sh` (the whole run); a TASKS.md paragraph; one "Measured since" note in PERF_30FPS_PLAN.md.

## Evidence: warm steady state, AC power, no owner pause. Warm-up: `Logs/tg2world_20261022_234919.txt` (discarded)
- **MODE=walk 3 min x 4 (ABBA, noon, FSR 3 at 540 rows, shells ON).** High: `Logs/tg2world_20261023_002326.txt` and `_003803`. Medium: `_002818` and `_003311`.
  | | High (A, A) | Medium (B, B) | Medium - High |
  |---|---|---|---|
  | p50 | 43.18, 42.08 | 42.63, 42.30 | **-0.2 ms** |
  | p95 | 75.23, 75.69 | 77.68, 77.44 | **+2.1 ms** |
  | % of frames > 33 ms | 81.7, 79.5 | 80.3, 80.6 | -0.2 pt |
  | p99 | 100.3, 101.2 | 103.7, 101.4 | +1.8 ms |
  | memory, median (phys_footprint) | 2285, 2279 MB | 2168, 2173 MB | **-112 MB** (peak -147 MB) |
  | shell fill at the walk's end (s1 / s2, held/cap) | 46%/18%, 43%/13% | 49%/21%, 49%/20% | about +4 pt; s3 and beyond stay EMPTY on both |
- **Cost-split launches H M M H, FULL p50.** Logs: `Logs/costsplit_2026-10-22_235521`, `2026-10-23_000248`, `_000944`, `_001651.txt`.
  - walk_near: High 66.0 / 65.9 ms, Medium 64.0 / 65.9 ms (**-1.0 ms**).
  - walk_open: High 45.8 / 46.3 ms, Medium 45.3 / 45.2 ms (**-0.8 ms**).
  - The noise is 0.3-1.6 ms, so neither difference is clearly real.
  - The shells cost +15 ms on both presets (FULL minus shells_off: High +15.4 to +16.2 ms, Medium +13.6 to +15.4 ms).
- **Why there is no gain: shell iterations per frame do not fall.**
  - walk_open: 12.41 M on High vs 12.53 M on Medium. Per hit ray: 105.9 vs 112.6. Per sky ray: 29.9 vs 30.8.
  - Medium's inner shells are smaller (shell 0 is 52 m instead of 69 m), so a ray crosses more shells to reach the same distance. That cancels the shorter band per shell.
  - The plan's estimate (iterations scale with the sizing rows) is wrong.
- **Stills.** Look report: `Logs/lookreview_20261022_233634.txt`. The shells were full at `high` and `walk_open` (Medium missed 38-45 chunks). At walk_near, s4 and beyond were still filling on both presets at the 90 s cap.
  Pixels changed between Medium and High (native): walk_near 0.01%, walk_open 1.6%, high 7.6%.

## Where Medium looks worse (Screenshots/117/)
- `handoff_high_fsr3_540_3x.png` (top = High, bottom = Medium; 3x crop of the high pose).
  - Look at the sea in the upper third: Medium has a dashed dark line where shell 6 ends at 3.3 km and the far band takes over. High has none.
  - The same line shows in native and at walk_open (`sbs_walk_open.png`, top right, over the sea at left).
- `band_high_native_3x.png` (the 1-3.5 km band, top = High, bottom = Medium): the land shapes and colours are the same.
  - Medium loses small things: the dark pillar centre-right becomes a dot, and the pink flecks on the green plain are gone.
  - The seam crosses the sea at the top.
- `horizon_high_native_3x.png` shows High's own 6.6 km hand-off: a soft straight brightness step near the horizon, with no dashes.
  - Medium also shows more of the known black dots on the far sea (#10), because more of its sea is far band.
- `sbs_walk_near.png`: identical (a cliff a few metres away).

## Left open
- The 3.3 km seam is a far-band/shell water-level mismatch at a steep viewing angle. Not investigated; it is only worth chasing if Medium is wanted.
- Shells 3 and beyond never fill while walking on either preset (the known 93b issue). Medium does not fix that.

## For the doc
Task 117: Medium vs High at 540 rows (warm, ABBA). Medium is not faster: the shell march does the same work.
- Walk p50 **42.6 -> 42.5 ms** (-0.2); p95 **75.5 -> 77.6 ms** (+2.1); frames > 33 ms **80.6% -> 80.4%** (`Logs/tg2world_20261023_00{2326,2818,3311,3803}.txt`).
- Cost-split FULL p50: walk_near **66.0 -> 65.0 ms**, walk_open **46.1 -> 45.3 ms** (noise 0.3-1.6 ms; `Logs/costsplit_2026-10-2*_{235521,000248,000944,001651}.txt`).
- Shell iterations per frame at walk_open: **12.4 M -> 12.5 M** (not the expected -25%).
- Memory (median phys_footprint): **2282 -> 2170 MB** (-112 MB; the plan expected -171).
- Images:
  - `Screenshots/117/sbs_high.png`: left = High, right = Medium. The top row is the picture; the bottom row is the shell tint (one colour per shell, grey = far band). Look at the sea behind the mountain: on Medium the far band starts much nearer, and a dark dashed line marks the edge.
  - `Screenshots/117/handoff_high_fsr3_540_3x.png`: top = High, bottom = Medium, 3x. The dashed seam over the sea is Medium's only clear visual loss; the land is otherwise the same.
