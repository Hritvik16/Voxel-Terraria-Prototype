# 231-look-why-lotl-looks-cinematic: DONE (offline). Sky matters most, then the colour grade. A low sun alone helps as much as all five post factors at forest_eye. All five together close only ~30 % of the gap. Commit f1de235

## What was done
- `Logs/overnight/231/ablate.py` (python3 -I, run from the repo root; no input images in its folder). Ours: 226's forest_eye / jungle_eye /
  walk_near (noon) + 3 low-sun stills (below). Theirs: the 3 Steam shots cropped from `Screenshots/222/side_by_side.png` (640x360).
  Factors, each alone and then all five: a warm sun / cool shade (luma mask), b pale hazy sky + soft clouds (soft blueness sky mask,
  sky pixels only), c grade (greens -28 %, warm mids, 30 % S-curve, vignette), d coloured shade floor (luma < 0.18), e 1/4-voxel grain.
  Reverse test on theirs: grade flattened / sky made our blue / warm-cool split killed / shade crushed / sub-voxel texture smoothed.
- Ranking comes from 8 image metrics: sky chroma, sky luma, warm-cool split, shade p10, chroma, green chroma, contrast, detail.
  Each metric is scaled by its spread over the 6 original shots. I judged every sheet by eye too.
  Report: `Logs/overnight/231/ablation.txt`. "Distance": ours = 1.00, lower = closer to theirs.
- Step 3 used existing flags only (`run.sh`: `-costsun 12 -costfixed forest_eye,jungle_eye,redwood_far1 -costarms none`). The sun is at
  yaw -77 and the camera at 180, so the light comes from the side. **The launch took 24 min, over the 10-min budget.** Four paths each
  had a 60 s shell wait plus load. Shells were still missing at every pose: 2,705-3,283 of ~8,000 (s3-s7).
  It ran no timing arm and I used no frame time.

## Ranking (noon mean / low-sun mean distance; cost class per rule 12)
1. **b sky: 0.78 / 0.77. The best single factor.** Reverse test: making THEIR sky our saturated blue is the removal that makes them
   most like ours (0.60). Engine: sky zenith/horizon colour constants plus 2-octave cloud noise on sky pixels only. Per-pixel ALU on sky,
   no new pass, 0 B (or a 256 KB noise texture). Caveat: the sky colour also feeds the sky fill ambient, so a paler sky changes shade colour.
2. **Low sun / time of day (not one of a-e; this launch shows it).** forest_eye 0.93 -> 0.62 with no post work: as far as all five
   factors move it at noon (0.57-0.70). jungle_eye did not change (1.67 -> 1.69). Engine: constants only (play's start time, shot poses).
3. **c grade: 0.85 / 0.93.** Reverse test: flattening their grade does NOT make them look like ours (1.09). The grade polishes, it is not
   the difference. Engine: constants in the existing final pass (render res, before FSR), or a 32^3 LUT (128 KB). No new pass.
4. **a warm/cool split: 1.10 / 0.89, mixed.** Our noon forest already has a stronger split than theirs (R-B 0.148 vs 0.093), so adding
   more overshoots. It helps only at low sun. Killing their split barely changes them (0.96). Engine: sun / ambient colour constants.
5. **d lifted shade: 0.99 / 1.08 on the numbers.** By eye it is the only factor that fixes the black jungle trunks. But their forest shade
   is just as dark (p10 0.045 vs ours 0.105-0.04); what their shade has is coloured light GRADIENTS (GI), not a floor. Engine: one
   ambient-floor constant or the sky fill's ground-bounce share.
6. **e grain: 0.99 / 0.99. Barely matters.** 226's per-voxel variation already covers the voxel level. A screen-space stand-in reads as
   noise on far hills. Smoothing THEIR sub-voxel texture moves them 0.85 toward ours, so real per-voxel texture matters a little.
   Engine: a 2nd hash at 4x frequency in `LotlVoxelVary`. ALU only, 0 B.
- **All five: 0.70 / 0.71.** By eye, their shots with all five removed still look clearly like theirs. **Most of the "cinematic" gap is
  NOT in these post factors.** INFERRED from the sheets, not tested: content and shape (big hand-shaped trees with dense round canopies,
  houses, paths, props), framing (a subject plus foreground framing), and GI light gradients across surfaces. That is the placement
  phase and GI work, not post.
- Memory: no engine change, so n/a. From the launch, for the record (known issue, not this task): the GPU body pool filled at jungle_eye
  (254,399 of 254,400, 1,027 refusals); CPU pool peak 493,082.
- No EditMode run: nothing under `Assets/` changed (script + text only). Nothing queued or implemented: the look is the owner's call.

## Images (git-ignored; `Screenshots/231/sheets/`; `viewer.html` = arrows pick the pose, Space flips ours / all five, 1-7 pick a factor, Z = full size)
- `grid_forest_eye.png`, `grid_jungle_eye.png`: the main two grids. Top row ours, +a, +b, +c; bottom row +d, +e, ALL FIVE, theirs. Look at the sky
  (b: pale with clouds) and the grass (c: olive instead of lime). In jungle, d is the only tile where the trunks stop being black.
- `grid_lowsun_redwood_far1.png`, `grid_lowsun_forest_eye.png`: the same at a 12 deg sun. In the redwood vista, the raw still (top-left)
  already has a warm horizon and a side-lit mountain. In low-sun forest, c and all five go too dark: the grade needs lighter settings at low sun.
- `reverse_forest.png`, `reverse_canyon.png`, `reverse_swamp.png`: their shot with each factor removed. Only "sky made our blue" changes
  the feel. "ALL REMOVED" (bottom row, 3rd tile) still looks like their game.
- `full_forest_eye_ours.png` / `full_forest_eye_all.png`: the full-size flip pair. Compare the grass colour, the sky, and the far mountain
  (grain shows there as fine noise, a weakness of the stand-in).
- `Screenshots/231/raw/redwood_far1_full_clean.png`: the most "screenshot-like" still we have. It uses no post work, only a low sun.

## Next
Owner decision: which to try in the engine. Cheapest first: low-sun start time and shot poses (constants), sky colour + clouds
(sky pixels only), grade (final-pass constants). Shade and texture would be GI / content work.

## For the doc
- Distance to their look (ours = 1.00, `Logs/overnight/231/ablation.txt`): sky 0.78, grade 0.85, shade 0.99, grain 0.99, warm/cool 1.10, all five 0.70 (noon mean).
- Low sun alone, no post: forest_eye 0.93 -> 0.62 (as much as all five at noon); jungle_eye unchanged.
- Reverse: their shot with our blue sky 0.60 toward ours (the strongest single removal); flattening their grade 1.09 (no closer).
- Cost: sky and grade are constants or ALU on pixels already drawn, no new pass, 0-128 KB; memory n/a (no engine change).
- Image: `Screenshots/231/sheets/grid_forest_eye.png`. Top row ours / +warm-cool / +pale sky / +grade; bottom +shade / +grain / ALL FIVE / theirs. Look at the sky and the grass colour.
- Image: `Screenshots/231/sheets/reverse_swamp.png`. Their swamp with each factor removed. Only the blue sky (top row, 3rd) makes it look like ours.
- Image: `Screenshots/231/raw/redwood_far1_full_clean.png`. Our redwood vista at a 12 deg sun with no post work: warm horizon, side-lit mountain.
