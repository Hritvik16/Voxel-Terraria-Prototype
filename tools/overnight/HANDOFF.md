# 127-look-better-comparison-stills: DONE. Cross-lit High|Medium pairs with 2x hand-off crops, and bird's-eye biome-border BEFORE|AFTER pairs.

No owner decision is needed by this task; the stills answer the owner's Q75 / Q78 follow-ups.

## What changed
- Capture-only flag `-lookcross 1` (new `Assets/Game/PlaygroundCapture.LookCross.cs`, hook in `PlaygroundCapture.LookReview.cs`). No product code, no defaults.
- `tools/cross127-sheet.py` (pair / 2x crop with px diff / mark boxes). Driver `Screenshots/127/127-driver.sh`. TASKS.md paragraph.
- Light for EVERY shot: 15.26 h, sun 40 deg up, sun azimuth -74 deg; headings 60-75 deg off it (cross-light: sunlit vs shaded slopes).
- The first attempt (this task, before the usage-limit stop) used 3 shell workers: shells 6-7 (the 3.3 km hand-off) stayed EMPTY at walk
  height after 6 min, so High drew the far band there too (a meaningless comparison). This run passes `-shellworkers 6` (capture only).

## Evidence
- Fast suite `PASS 921 FAIL 0 SKIP 15` (`Logs/overnight/127-tests.out`).
- Launches (High 540 rows FSR 3; build 09:51): High `Screenshots/127/high/lookreview.txt` (prefill 128 s, then all shells full at x_high,
  x_coast, birds; x_ridge s3-s5 missing 623 at the 120 s cap, s6 176/182, s7 full); Medium `Screenshots/127/medium/`; High blend-off
  `Screenshots/127/blendoff/` (xb_0..2 all shells full). Log `Logs/overnight/127-driver2.out`. Waits were capped (120 s / 90 s); launches 6-23 min,
  over the 10-min budget because the 3.3 km shells had to be full for the hand-off to be visible at all.
- Px diff High vs Medium, whole frame (mean abs RGB): x_high 1.7, x_coast 1.2, x_ridge 0.8, x_bird800 0.2, x_bird2300 0.3.
  Crops: x_high sea ring 2.7, x_high land ring 5.2, x_coast land 3.3, x_ridge 3.5, bird800 ring 0.2.
- Borders BEFORE vs AFTER whole frame: 4.2 / 4.8 / 4.2 (xb_0/1/2).

## Looked at (all), what they show
- Medium looks WORSE: (1) sea past 3.3 km: a dashed dark line and a slightly different, flatter blue beyond it (x_high, x_coast);
  (2) far snow flats: the thin blue river line is gone in Medium (x_high land crop, x_coast land crop); (3) 1-3 km: lake shores and the
  crater ponds are blockier, ponds turn greenish (x_ridge, x_coast).
- Medium looks the SAME: everything within ~1 km (near slopes, the mesa cliff and its shading); and from 800 m or higher up, where the
  3.3 km ring sits in haze (x_bird800 ring crop: diff 0.2, nothing visible).
- Borders from 700 m: BEFORE has ruler-straight lines (xb_1's diagonal savanna|jungle edge, xb_2's yellow|green and green|snow edges);
  AFTER is a ragged ~30 m mixed band that bends. Relief reads well in all three (shadowed gullies on the sun-away side).

## Left / next
- `x_bird2300` is mostly sea haze; not useful. Over land, the 3.3 km ring was never in frame from a bird pose (this world's ring is sea in
  every cross heading: "sea/land mix 0 %").

## For the doc
- Light: 15.26 h, sun 40 deg up, cameras 60-75 deg off the sun (cross-lit). High vs Medium px diff: whole frame 0.2-1.7, hand-off crops 2.7 (sea) / 5.2 (land) levels.
- Biome borders BEFORE vs AFTER (700 m up, -60 deg): px diff 4.2-4.8 levels, all shells filled.
- `Screenshots/127/sheets/x_high_pair.png` -- High (left) | Medium (right) from the lookout, sun on the left: look at the sea at mid-height; Medium has a faint dashed line across it at 3.3 km.
- `Screenshots/127/sheets/x_high_crop_sea.png` -- 2x crop of that line: Medium (right) shows the dashed dark seam and a flatter blue past it; High (left) is seamless.
- `Screenshots/127/sheets/x_coast_crop_land.png` -- 2x crop 1-3 km out: Medium (right) loses the river on the snow flats (upper right) and its crater ponds turn green; the mesa is the same.
- `Screenshots/127/sheets/xb_1_pair.png` -- savanna|jungle border from 700 m: BEFORE (left) a straight diagonal line; AFTER (right) a ragged band that bends.
- `Screenshots/127/sheets/xb_2_pair.png` -- the yellow|green border (logged as forest|jungle) and the green|snow edge: BEFORE straight edges, AFTER mixed edges. Reference map: `Screenshots/122/biome_map_before_after.png`.
