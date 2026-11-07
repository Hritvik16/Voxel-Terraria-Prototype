# 182-worldgen-matrules-fix-deepstone-shellmosaic

Model: opus

Approval: supervisor, 2026-11-07, reversible engineering behind `-matrules 1` (default stays OFF; the owner decides the default). Fixes the two defects
task 180b reported (read `tools/overnight/blocked/180b-worldgen-material-rules-prototype.HANDOFF.md`, `Screenshots/180b/viewer.html`,
`Assets/TerrainGen/GroundRules.cs`). Memory rules: exact + data-derived, no terrain-specific tuning, prove on an adversarial case; no 3-D noise.

## Do
1. DEFECT 1, deep stone on tall faces: depth is measured from each column's top, so the lower part of a tall face turns into "deep stone", and below 20 m
   that changes per 0.8 m brick (blocky steps; snow's dark blue slate reads as black boxes in shade -- `scree_compare.png`, `cliff_snow_compare.png`).
   Fix by a rule, not a colour: the deep-stone band is chosen by ABSOLUTE height (the generator's existing stone / deep-stone boundary,
   `STONE_BOTTOM_BELOW_SEA` in DetailField) and never by depth from the column top; between skin and that height the biome's stone / subsoil rules by depth
   apply. If you find a better data-derived rule, state why. Keep "a dug pit shows the ORIGINAL surface's bands" (`pits.txt`) true.
2. DEFECT 2, shell mosaic: a coarse shell voxel (3.2 m and up) samples below the 0.3 m skin on slopes, so far hills show a brick-sized mosaic of soil,
   subsoil and deep stone. Fix: a shell voxel whose top is exposed (the footprint reaches the surface) uses the skin band; deeper bands only for voxels
   truly below the surface by more than their own size. Near == far on the slot choice must still hold (the 180b test).
3. Tests: keep every 180b test green; add one for each fix (a tall face has no deep stone above the absolute boundary; a shell column on a slope shows only
   skin / the slot entry at its top). Whole suite (generator change). Rough cost note from the task's own runs; no timing arms.
4. Re-shoot the SAME 8 poses (`Logs/overnight/180b_poses.txt`), ON | OFF, full size, with the flip viewer, plus the beach pose (left side was the mosaic).
   One caption each. Note in the HANDOFF whether the forest cliff (`cliff_forest_compare.png`) still reads as one flat brown wall; do NOT try to fix
   that here (it is a geometry matter, a separate decision).

## End
Done. Do NOT change the default. HANDOFF with the before | after pictures. The owner's "default?" choice stays in the doc.
