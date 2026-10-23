# 120-look-cube-face-lighting: DONE. All 3 parts shipped ON. The distance band is gone, and noon side faces are no longer olive.

## What changed (branch engine-phase, commit 0e6f0d3)
- `Raymarch.compute`: the TASK 120 block after `FarShadeSky` (`CubeBounceLight`, `CubeFilterBlend`, `CubeFaceLight`, `CubeLight`, `CubeFilterWeight`).
  It is used in the near look-1 path and the far-band path. Every use is under `#ifdef VE_FAR_SHADE`, so rigs without far shading are byte-identical.
  1. Ground bounce (owner Q4 (a)): sun colour x 0.25 x sun height x (1 - n.y), added to the sky term.
  2. Filtered cube light replaces 85b's switch at the live rows. A hit is lit as the mean of its staircase's top / x / z faces,
     each weighted by |nS.i| x its cosine to the eye. It blends in over a FIXED 20-50 m.
  3. One colour per drawn cell: `(voxel >> s) << s`, for shells and cascade tiers.
- `RaymarchFeature.cs`: flags `-cubebounce 0`, `-cubefilter 0`, `-cellhash 0`, or `-cubelook 0` for all three (= the old look).
- `PlaygroundCapture.LookReview.cs` (capture-only):
  - `-lookab 1` shoots each fsr3/native arm twice in the same frame state: `<name>` (new) and `<name>_old` (all 3 parts off).
  - `-lookgolden 1` adds a shot at 17 h, turned away from the sun, at every pose.
- `CubeLookTests.cs` (3 tests). The face weights are checked against a brute-force 2D staircase DDA: they match within 0.003 (python check), and the test tolerance is 0.03. Also a TASKS.md paragraph.

## Evidence
- Tests: filtered `CubeLookTests|FarRockSlopeTests` 6/6. Fast suite: `PASS 900 FAIL 0 SKIP 14` (5m27s).
- Build: OK, no shader errors (`Logs/lookreview_build.log`).
- Look runs (noon, FSR 3, shells 3 workers, 45 s shell cap):
  - High at 720 rows, 4 poses + golden: `Logs/lookreview_20261023_010325.txt`. At walk_near/walk_open, s3-s7 were still partly missing (3030 / 1619 chunks); ridge and high were complete.
  - Low at 540 rows, walk_open + high: `Logs/lookreview_20261023_012318.txt`.
- The band is measured gone. Mean luma of the back hill at 114's G pose, Low 540 minus High 720: **old +17.4, new -0.2**.
  So the far lighting no longer depends on the render rows.
- Near slope luma (walk_open, 0-60 m): 77.4 -> 99.2 (sides brighter; tops nearly unchanged).
- What the stills show:
  - Near and mid sand sides are light khaki instead of dark olive. The staircase is still clearly visible.
  - Golden hour is almost unchanged (as designed: the bounce scales with sun height).
  - At the high pose (50 m-3 km), the big mountain changed from pale and smooth (slope-lit, "low-poly") to blocky orange/sand cell texture.
    It is slightly darker and more saturated, because steep ground now averages its darker side faces.
- Not done: the 3 parts were not shot one at a time. None of them looked bad in the combined shots, so all ship.
- No ms change was seen. None was measured (look task, rule 10).

## Left open / next
- The VAO fade to 1 past 50 m is kept from 85b. A staircase's mean AO is below 1, so a slight brightening remains possible. It was not visible in the crops.
- `SeeBehindColour` (ground seen through liquids) keeps the old combine without the bounce. This is minor.
- Next in 114's Q3 order: #4 sky, then #6 haze.

## For the doc
Task 120: near and far ground lit as the same small-cube material.
- Brightness band, back hill at 114's G pose, Low 540 minus High 720 mean luma: **+17.4 -> -0.2** (`Screenshots/120/G_low_vs_high_far_120.png`; logs above).
- Near slope mean luma, walk_open noon: **77.4 -> 99.2** (sides no longer olive).
- Golden hour (17 h): visually unchanged (`Screenshots/120/sbs_walk_open_golden.png`).
- Images:
  - `Screenshots/120/G_low_vs_high_far_120.png`: 114's G crop pose. Top row = new, bottom row = old; left = Low 540, right = High 720.
    In the old row, the back hill (top-left of each panel) is pale on Low and dark on High: the band that moved with the rows.
    In the new row, Low and High are identical, and the side faces everywhere are lighter khaki.
  - `Screenshots/120/sbs_walk_open_fsr3.png`: top = new, bottom = old, walk_open at noon, High 720.
    Look at the slopes in the middle: the dark olive risers become light khaki, and the cube staircase stays.
  - `Screenshots/120/crop_high_mid.png`: top = new, bottom = old, the high pose's mountain at 50-500 m.
    The old one is smooth and slope-shaded (low-poly look). The new one shows blocky per-cell sand/orange texture, like the near ground.
  - `Screenshots/120/sbs_walk_near_fsr3.png`: top = new, bottom = old, a cliff about 5 m away. The sand side faces (left) are khaki, not olive.
