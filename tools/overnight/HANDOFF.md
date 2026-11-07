# 182-worldgen-matrules-fix-deepstone-shellmosaic: DONE, both 180b defects fixed behind `-matrules 1` (default still OFF). Commit 4867115.
The owner's "keep the material rules as the default?" question (180b) still stands. Its blocker (the two defects) is gone.

## What changed
- **Defect 1, deep stone on faces.** Rule: deep stone is chosen by ABSOLUTE height, never by depth below the column top.
  - `GroundRules.Band` is now 0 skin / 1 soil / 2 subsoil / 3 rock. `RockAt` / `RockRange` pick the rock, by height:
    - biome stone down to `DeepTop` = sea - `DetailField.STONE_BOTTOM_BELOW_SEA` (50 m) + the wavy deepOffset. This is the old path's own `LayerRock` line.
    - Below it: deep stone, then cavern rock, then the underworld.
  - Removed: `ColumnSample.mrDeep`, `DEEP_BASE` and `DEEP_AMP`. A face is now one stone; deep stone is a world layer.
  - A dug pit still shows the original surface's bands (`Logs/TerrainGen/180b/pits.txt`: e.g. forest grass | loam 0.3 m | ochre clay 0.6 m | granite 2.1 m).
- **Defect 2, shell mosaic.** New `ColumnSample.mrExpose` (shells only; set in `ShellColumnJob.Expose`; chunk-edge neighbours are sampled).
  - Value: max(2^k, h - the lowest of the 4 coarse neighbours).
  - A coarse voxel shallower than that is bared (top or side) and uses band 0, the skin. Deeper voxels keep their own depth's band.
  - At 2^k = 1 the rule changes nothing (the drop is stepAtTop <= SHEER_STEP = SKIN), so LOD-0 chunks are untouched.
  - Material's brick zone and `GeologyRock` return the skin for band <= 2. Only an exposed coarse voxel can get there.
- Tests: `MaterialRuleTests.TallFace_IsOneStone_NoDeepStoneAboveTheAbsoluteBoundary` and Slow `MaterialRuleChunkTests.Shells_BaredVoxels_WearTheSkin`.
  - `ShellChunk.Column` / `ColumnStep` are new, test-only accessors.
  - The 180b pit / band tests were updated to the new Band / RockAt signatures.
- `tools/matrules182.py` builds the viewer. TASKS.md has the paragraph.

## Evidence
- **Whole suite PASS 1095 FAIL 0 SKIP 35** (`Logs/overnight/182_all.out`, 70 min). It includes every 180b / 181 test and the brick-skip on = off proofs with MatRules on.
- **Tall faces** (`Logs/TerrainGen/180b/tallface182.txt`): sheer faces up to 28.8 / 24.7 / 41.3 m tall on seeds 42 / 7 / 2026. Seed 31337 has no sheer face over 6 m.
  - 2,638 face voxels checked: every one is the column's ONE stone, none is deep stone.
  - The old rule turned everything below 11-19 m to deep stone.
- **Shell bared voxels** (`Logs/TerrainGen/180b/shell_bared182.txt`), shells 3 and 5 round 9 sites:
  - 207,663 bared voxels, **100.00 % skin**, 0 soil / subsoil / stone.
  - 180b's depth bands would have shown a deeper band on 161,291 of them.
  - mrExpose matched the rule at every bared column.
- **Rough cost** (the suite's own `Cost_OnVsOff`, `Screenshots/180b/cost180b.txt`, Editor Burst, not a timing arm):
  - LOD-0 dense bricks ON vs OFF: **+23 % -> +1 %** (+7.7 MB -> +0.3 MB over 243 chunks). 180b's per-brick deep staircase made those bricks dense.
  - Shells: generation 205 -> 204 ms per chunk. Dense bricks are unchanged.
- **Shots:** one ON launch, 180b's 8 poses, High, 45 s wait (`Logs/lookreview_20261107_055618.txt`).
  - It is compared with 180b's ON (before) and OFF launches, which ran the same code paths.
  - Shells were not full after the 45 s wait: s3 missing 1.6-1.9 k of 2 k, s4 and up nothing.

## Pictures (all looked at). `Screenshots/182/viewer.html`: arrows change pose; Space or 1 / 2 / 3 flip 182 ON / 180b ON / OFF; Z = full size
- `Screenshots/182/cliff_snow_compare.png` (AFTER left | BEFORE right): the black box left of centre is gone. The face is one grey schist.
- `Screenshots/182/scree_compare.png`: in BEFORE, the face above the gravel apron is a wall of blue / black boxes. In AFTER it is one grey face.
- `Screenshots/182/beach_compare.png`, far hills LEFT: the red / yellow / blue brick mosaic is gone.
  - Grass / brown / ochre / grey layered faces remain there, and a black slab sits right of centre.
  - Both look like missing-shell-chunk artifacts (straight vertical cut faces; most of s3 was missing). BEFORE has a dark slab over the water at the same pose.
  - Not proven: a longer wait at this pose would show it.
- `Screenshots/182/border_bank_compare.png`: the grey canyon walls (upper middle) lose their dark-blue / black patches.
- **Forest cliff** (`Screenshots/182/cliff_forest_compare.png`): it STILL reads as one flat brown wall. It is the same in AFTER and BEFORE, so the fix did not cause it. This is the geometry matter; not touched here, as the task said.
- Desert cliff, bank and oasis: AFTER = BEFORE.

## Left / next
- The owner decides the default ("fix then default" / "default now" / "drop it"). The defects that blocked 180b are fixed.
- Untested: under `-ores 1` + `-matrules 1`, a wall-vein voxel bared in a shell now shows the skin, not the vein. Not a default combination.
- Possible check: a look at the beach pose with a longer shell wait, to confirm the layered left faces are missing chunks. I did not queue it, because it is a measure-only task.

## For the doc
- Tall sheer faces (up to **41.3 m**, 3 seeds): deep-stone voxels above the boundary went from everything below 11-19 m to **0 of 2,638** (`Logs/TerrainGen/180b/tallface182.txt`).
- Bared shell voxels showing a band under the skin: **161,291 -> 0** of 207,663. Skin share: **100 %** (`Logs/TerrainGen/180b/shell_bared182.txt`).
- LOD-0 dense bricks ON vs OFF: **+23 % -> +1 %** (+7.7 MB -> +0.3 MB over 243 chunks). Generation is unchanged (`Screenshots/180b/cost180b.txt`).
- Whole suite **PASS 1095 FAIL 0** (`Logs/overnight/182_all.out`).
- Images (all `Screenshots/182/`; the viewer is `viewer.html`):
  - `scree_compare.png`: AFTER left has one grey face above the gravel apron. BEFORE right has blue / black brick boxes.
  - `cliff_snow_compare.png`: BEFORE's black box (left of centre) is gone in AFTER.
  - `beach_compare.png`, far hills left: the brick mosaic is gone in AFTER. The remaining layered faces are probably missing shell chunks.
