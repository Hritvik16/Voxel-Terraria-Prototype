# 42-lake-natural-shores

Approval: owner, 2026-10-09 ~20:15, Queue decisions Q35 (B10 review pack): 3 (a) "one small fix
task now: the shore shelf follows the ground, so lakes are natural again" and 4 (a) "rename the
landform Badlands to Breaks inside the next task that touches the code". Queued by the supervisor.

Branch `surface-rebuild`. Terrain-generator change: `SUITE=all`. v4 only; v3 byte-identical.

## The problem (found by task 37e, read `tools/overnight/done/37e-lake-flat-water.HANDOFF.md`)
37e made every v4 lake sit still at one level by raising dry columns within 1.5 map cells of a
lake to the lake's level (`ShapeField.LakeRim`) and adding a 4 m shore SHELF inside the lake.
The shelf ends exactly at the edge of the 2x2 map squares that touch a lake, so the shoreline
follows the 8 m map grid: seed 42's lake is now a straight-edged RECTANGLE with a flat sand apron
(`Screenshots/37e/lake_before_after.png`, bottom left). Owner: it must look natural ("nothing
sticking out", game-design intent).

## Change 1 -- natural shores (v4 only)
Keep 37e's guarantee (every lake at rest at one level, no leaks) and make the shoreline follow
the terrain, not the map grid. 37e's own suggestion: raise the bed / rim only where the dry rim
actually needs it (where the ground would otherwise dip below the lake level), and blend or
noise-warp the shelf edge so no edge is axis-aligned over more than a few voxels. Choose the
simplest fix that meets the acceptance; say what you chose and why in HANDOFF.
Do not lower lake levels map-wide (37e explains why) and do not add a post-pass that pours water.

## Change 2 -- rename (separate commit, no behaviour change)
`Regions.Badlands` (the LANDFORM, `Assets/TerrainGen/Regions.cs`) -> `Regions.Breaks`, display
name "breaks". `GenBiome.Badlands` (the BIOME, B10) and `GenMat.BadlandsClay`/`DetailField.BadlandsBand`
(biome materials) keep their names. Numeric values unchanged, so maps and hashes must be
identical before/after this commit (check: the hash tests pass unchanged). Update comments,
report labels and docs in `Assets/` that mean the landform.

## Acceptance (seeds 42, 7, 2026, 31337)
- `LakeLevelTests.LakeWater_IsAtRest`: still **0** unsettled on every seed.
- A new, small, bounded check (one seed per case, from map data, no allocation over all lake
  cells x height -- see 37e's memory rule) that the shoreline is not grid-aligned, e.g. the share
  of shore edge length lying on 8 m map-cell boundaries, before vs after. Report it; pick a
  sensible pass line and say why.
- Lake count / lake area per seed before/after (should barely change); no leaks.
- B4/B5/B6/B8/B10 gates pass; v4 hashes change (expected), stay stable and seed-dependent;
  v3 unchanged. `SUITE=all` 0 FAIL. Watch Unity's memory as 37e did; the watchdog stays on.
- No performance arms (PREAMBLE rule 10).

## Pictures
- Seed 42 lake, same pose as `Screenshots/37e/lake_before_after.png` (no-edit column is enough):
  37e vs now, side by side, captioned: does the shore follow the ground, is the water flat?
- One more lake on another seed from 60 m up, so the whole shoreline shows.
