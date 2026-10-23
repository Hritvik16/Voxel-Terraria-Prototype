# 122-terrain-biome-border-blend: DONE. Biome borders are now a mixed, bent band, and each surface has a mild tint. Both are ON by default on map v4; `-biomeblend 0` / `-biometint 0` give back the old look.

## What changed (commit dfe801c)
- **Generator: `ShapeField.SurfaceBiome`.** A column's MATERIALS (surface, soil, beach rule AND rock/strata) come from the biome at a jittered point. The new field is `ColumnSample.surfBiome`.
  - The jitter has four parts: two warp octaves (800 m / 150 m wavelength, +-60 / +-15 m) bend the line; a clump octave (40 m, +-18 m) and a per-0.8 m-cell hash dither (+-14 m) mix the two sides.
  - The BIOME itself is unchanged: ores, pocket liquids, crystals, danger, the world record and the biome x landform maps all keep the old field. The record biome is the same bits (asserted), and the world-record tests pass.
  - Same seed gives the same world: the jitter is seed-keyed noise plus an integer hash.
- **Rock too, a decision I made mid-task.** The first in-game stills, with only surface and soil blended, still showed the exposed ROCK on steep slopes changing along a hard straight line. Mountains are mostly rock, so strata now follow `surfBiome` as well.
  - Ores keep the record biome, so a border's ore type does not dither.
- **Early-out.** `BiomeAndBorder` returns the climate-space distance to the nearest bisector. Past `BlendClimateReach`, the biome cannot change, so the 8 extra noise calls are skipped. This is exact while the snoise gradient stays at or below `SNOISE_GRAD_BOUND` = 8 (measured max 7.3).
  - The bound is loose: 90% of columns still take the full path. That is about +8 snoise per surface `Sample`; neighbour samples that only read height (`Sample(.., false)`) skip it.
  - No timing run was made (task rule). Flagged for the next performance milestone.
- **Far band and minimap** read `TopMaterial`, so they show the blend with no extra plumbing.
- **Renderer: `BiomeTint` in `LookAlbedo`** (`Raymarch.compute`), so voxels, shells and the far band all get it.
  - Each surface material drifts between two tints of its own: grass dry/lush, sand warm/pale, snow white/blue, savanna golden/olive, clay pink/grey.
  - The drift follows a 2-D value noise with ~100 m and ~400 m cells: tints span up to 0.84-1.08 per channel (grass blue, savanna blue), plus +-4% brightness.
- **Flags:** `-biomeblend 0` sets `TerrainSpec.biomeBlendOff` (chunk source + far band); `-biometint 0` turns the tint off. Map v3 is untouched.
- **Capture:** `-lookbiome 1` (look review) adds poses `border_0..2`, each looking along a land border, and a `<pose>_fsr3_<rows>_notint` shot at every pose.
- **Tests:**
  - `BiomeBlendTests` (4 tests + 1 report).
  - `ShellFillTests` GOLDEN was re-made: 32 of 85 chunks changed because the walk start pose sits on a desert|savanna border. The fast and reference paths still both match it.

## Evidence
- Whole suite: `PASS 961 FAIL 0 SKIP 26` (`Logs/overnight/122-suite2.out`).
  - The first run had 1 FAIL: the shell-fill golden hashes, which change by design. It was re-made and that test re-checked.
- Band metrics (`Logs/TerrainGen/biome_blend.txt`, 64 E-W transects, seed 42):
  - 516 bands, width median 27 m, p90 73 m.
  - Median 13 surface switches per band (before: 1, a hard line).
  - The band's centre sits off the straight climate contour by sd 53 m.
  - 3.7% of columns wear the other side's materials.
- Map metrics: no EditMode map metric changed. Biome shares, danger, ores and the world record are all keyed on the record biome, which did not move.
- Look launches (Low, FSR 3 at 540 rows, 4 poses, 45 s shell cap):
  - after: `Logs/lookreview_20261023_035751.txt`; before (`-biomeblend 0 -biometint 0`): `Logs/overnight/122-before.out`.
  - Shell 3 was still missing 30-40 chunks at `high` and `border_0`; everything else had filled.
- **What the stills show:**
  - border_1 and border_2: the hard diagonal rock line is gone, replaced by a mixed, bent band.
  - high pose (1-2 km away): the green|sand and green|pink edges are ragged and bent instead of straight. The patches still read as separate colours, because a ~30 m band is only a few pixels at that range.

## Open / next
- **Owner taste:**
  - At 1-3 km the far band shows ragged edges but still distinct patches. Making it read as a gradient from far away needs a WIDER band (`BLEND_CLUMP_AMP` / `BLEND_DITHER_AMP`), at the cost of more mixed ground up close.
  - Ore-type borders stay hard (record biome). Recommended as is.
- **Next performance milestone:** generation cost of the +8 snoise per column (no timing was run here).

## For the doc
- Border band (64 transects): a 0 m hard line with 1 switch -> median 27 m wide (p90 73 m), 13 switches; the line moves sd 53 m off the straight contour (`Logs/TerrainGen/biome_blend.txt`).
- Columns wearing the other biome's materials: 0% -> 3.7%. Record biome, world record and map metrics: unchanged (suite `PASS 961 FAIL 0 SKIP 26`).
- Shell-fill golden: 32/85 chunks changed bytes; fast path == reference path.
- Images:
  - `Screenshots/122/biome_map_before_after.png`: the whole 8 km island (left before, right after). Same biome regions; look at the edges, smooth curves before, ragged after.
  - `Screenshots/122/biome_crop_200m_before_after.png`: 200 m around a desert|savanna border at 2 m/px. Left is a clean line; right is a mixed, clumped band that wanders.
  - `Screenshots/122/border_1_before_after.png`: in-game jungle|savanna slope. LEFT: a straight vertical line where pale rock meets brown rock. RIGHT: the line is gone; rock and grass of both sides interleave.
  - `Screenshots/122/border_2_before_after.png`: forest|jungle slope. The left frame's diagonal grey|pale rock edge becomes a broken, mixed edge on the right.
  - `Screenshots/122/high_crop_before_after.png`: the 114 high view's middle distance, 2x. The green field's edges against sand and pink go from straight to ragged.
