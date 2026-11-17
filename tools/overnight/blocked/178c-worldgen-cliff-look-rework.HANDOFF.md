STOP_FOR_OWNER: keep the reworked cliffs? Open `Screenshots/178c/<pose>_compare.png` in Preview: 4 panels, top to bottom BEFORE (-cliffs 0) | 178 | REWORK | REWORK WITHOUT THE MIX. Or open `Screenshots/178c/viewer.html`: Space cycles the panels, B/1/2/3 jump to one.
(a) keep the rework (recommended) (b) rework without the lenses (`-rockmix 0`) (c) `-cliffs 0`, the plain pre-178 faces.

# 178c-worldgen-cliff-look-rework: DONE -- the faces read as horizontal strata, with no drips, hooks or flat colour fields; whole suite red only on the known ford

## What changed (commit 1d57b8f; 3 runs: the 1st died on login, the 2nd on usage, this one finished)
- **The drips:** the rock wore the column's `surfBiome`. Its border jitter has 40 m / 10 m patch octaves; on a steep face every patch edge became a vertical streak, and that happened in pre-178 faces too.
  - Rock now takes `ColumnSample.rockA/rockB` (`ShapeField.RockBiomes`: the smooth 800/150 m warp only, +-29 m).
  - Each layer picks a side by a hash (`DetailField.LayerBiome`).
- **Strata:** `LayerWave` is now +-1.2 m / 90 m plus +-0.2 m / 26 m, plus a gentle dip of +-25 m over ~4 km.
- **Tones:** `LAYER_ROCKS` uses close rocks per biome, CIE dE 7-22 (was 22-62).
- **Lenses:** where 3-D noise > 0.62 (12 m x 1.6 m lenses), a 0.8 m brick wears its rock's nearest-tone PARTNER (dE 5-18; `LAYER_PARTNERS`). That is 28 % of bricks, and every brick stays uniform at LOD 0.
- **Relief:** fluting is a smooth snoise of 9 m (the old ridged 5 m drew fine vertical hatching). The ledges are now ONE bench at each layer's base.
- **New `-rockmix 0`** (`spec.rockMixOff`, `TerrainGenChunkSource.SetRockMix`): the rework without the lenses.
- **Two test fixes:**
  - The ShellFill GOLDEN is re-pinned: 68 of 82 chunks changed, and fast == reference (0 differ).
  - The streamer redraw test now runs without the mix:
    - It runs shells with the fill pass OFF. The fill normally flattens buried mixed bricks, and without it the lenses made buried shell-1 bricks dense.
    - Shell 1's Low pool then filled after 96 chunks, before the test's chunks, so the test never held them.
    - Proven: the test passed with the mix disabled and failed with it on.
- **The viewer:** 178's `viewer.html` was black because its `pairs.json` rows were arrays while the page read object fields, so every `src` was "undefined". Fixed in `tools/cliff178.py`; `tools/cliff178c.py` writes objects and checks that every file exists.
- New tests: `CliffTests.RockBiome_IsSmooth_NoPatchStreaks`, `Mix_ShareAndPartnerRock`, `Layers_NearlyFlat`.
- World survey: `-wsposes cliff_<biome>` picks a face inside that biome.

## Evidence
- **Whole suite: PASS 1068 FAIL 1 SKIP 33** (`Logs/overnight/178c_all2.out`). The 1 is the known `seed 31337: 1.5 m of Sheer` ford.
- Filtered runs: the streamer test + FillHashes are 2/2 (`Logs/overnight/178c_fix1.xml`); the golden is 1/1 (`178c_fix2.xml`).
- The diagnostic of the old failure is in `178c_diag1.xml`: shell full, 467 free bricks, 96 generated.
- Rough cost, no timing arms (`Logs/TerrainGen/c4/cliffs178c.txt`, Editor Mono):
  - Sample cost per column is 90.8 us with cliffs on vs 64.9 us with them off.
  - LOD 0 mixed bricks at the face blocks: cliff 4336 -> 4624 (+7 %), coast 1559 -> 1706 (+9 %), forest_breaks the same.
  - In shells, buried mixed bricks are flattened by the fill pass, so their cost stays on the surface.
- Shots, one launch per arm, poses identical. Dirs: `LookReview/2026-11-06_120011_high` (before), `_122533_high` (178), `_121156_high` (rework), `_143610_high` (no mix).
- I looked at fixed_cliff, cliff_desert and cliff_snow as 3-panel sheets, and at forest_breaks, plus the no-mix panels for snow and fixed_cliff:
  - The rework has no drips and no hooks, and its layers are level.
  - The rework is clearly better than 178.
  - Against BEFORE it is a taste call. The lenses have 0.8 m stair-step edges and can read as "camouflage" up close. The desert's red layer is still a strong band (badlands/desert main-dark dE 7-19 by palette, but laterite reads dark).
  - The no-mix panels go back to flat fields: snow is a flat brown band and a flat cream band on grey. So the lenses are what make the faces read as rock, and (b) is not a better middle option.

## Unfinished / notes
- The lens edges are brick-blocky. If the owner likes the idea but not the blockiness, the next step is a lens shape tweak, not a new design.
- The bench crescents (dark arcs under the layer bases, forest_breaks) are kept: they read as overhanging strata.

## For the doc
- Vertical drip streaks: present in BEFORE and 178 -> **gone**. Biome switches along 300 lines of 200 m: **1553 (surface biome) -> 321 (rock biome)**. Runs under 30 m: 1149 of 1262 -> 7 of 65 (`TestResults_all.xml`, RockBiome_IsSmooth_NoPatchStreaks).
- Layer boundary rise over 10 m: p50 0.25 m, **p99 0.87 m** (Layers_NearlyFlat).
- Adjacent layer tone step, CIE dE: **22-62 -> 7-22** (`python3 -I tools/cliff178c.py tones`).
- Bricks wearing the partner rock: **28 %**. LOD 0 mixed bricks at faces: **+0-9 %** (`Logs/TerrainGen/c4/cliffs178c.txt`).
- Whole suite: **PASS 1068 FAIL 1** (known ford; `Logs/overnight/178c_all2.out`).
- Images:
  - `Screenshots/178c/cliff_snow_compare.png`: compare the panels top to bottom. 178's big flat blue-slate and cream bands become grey schist with brown lenses (rework), then flat bands again in the bottom no-mix panel.
  - `Screenshots/178c/fixed_cliff_compare.png`: look at the whole face. The vertical brown/grey drips in BEFORE and 178 and the dark-blue field in 178 become level grey/brown strata in the rework.
  - `Screenshots/178c/cliff_desert_compare.png`: look at the centre of the face. 178's cream band and flat red field become red/orange layers with ochre lenses in the rework; the red layer is still the strongest step.
