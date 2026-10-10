# 42-lake-natural-shores: DONE. v4 lake shores follow the ground (not the 8 m grid), lakes still at rest; landform Badlands -> Breaks. SUITE=all PASS 662 FAIL 0 SKIP 18.
**OWNER, PLEASE LOOK (not a blocker):** `Screenshots/42/lake_before_after.png`; there is also an older river/lake defect, see "Left open".

## What changed (commits on surface-rebuild)
- `064c317`, Change 1, `ShapeField.LakeRim` (v4 only; v3 byte-identical):
  - **Chosen fix:** lake water no longer comes from the map's 2x2 rule.
    - A column within `LakeReach` of a lake cell's centre is that lake's water wherever its ground is below the level. The reach is 72-104 voxels, varied by 2-octave noise.
    - So the shoreline is the ground's own contour. Where the ground stays low out to the reach (spill point, dry fringe), the shore follows the noisy reach edge.
  - Beyond the reach, the 37e rim still applies (>= level within 1.5 cells). Dips under it are now FOLDED up (half the dip, at most 0.8 m, ramping in), not clamped into a flat plain.
  - The at-rest guarantee is unchanged and holds per column: a lake column's neighbours are within 104 + 1.5 < 120 voxels of the lake cell, so each is water at the level or ground at or above it.
  - Shelf: a lower bound of the distance to the reach edge, 22 deg for the first 4 m, then steepening (no depth cap).
  - Why this and not 37e's "blend/noise the shelf edge": the straight edge was the 2x2 wet area itself. Any shelf inside it keeps the grid. Letting the water follow the ground removes the grid at the source, with no pass and no extra memory.
  - New `ShapeField.LakeWaterY` (the exact lake-water rule, used by the tests).
  - Tests: new `LakeShoreTests.LakeShore_IsNotGridAligned(seed)`, 4 seeds, bounded like 37e's: map-driven tiles, 81x81 column samples per tile on the stack, no voxel loop, 7-17 s per seed.
    - `LakeLevelTests` now classes lake water by `LakeWaterY`, and its tiles reach one more cell.
    - New report-only `LakeShoreView` (top-down lake render).
- `619585d`, Change 2: `Regions.Badlands` -> `Regions.Breaks`, display name "breaks". Value 5 is unchanged.
  - Also changed: comments in Regions.cs and PlayMetricsReport.cs, and the label in `tools/region-map.py`.
  - `GenBiome.Badlands`, `BadlandsClay` and `BadlandsBand` are untouched.
  - Fast suite PASS 631 FAIL 0 SKIP 14. Region / world-record / chunk-source tests pass. All 8 lake reports are byte-identical before/after (`Logs/overnight/42/pre_rename/`).

## Evidence (correctness only; no performance arms, rule 10)
| seed | unsettled | shore edges on 8 m lines, 37e -> now | in straight runs >= 2.4 m | lake area, ha |
|---|---|---|---|---|
| 42 | 0 -> **0** | 25.5% -> **8.5%** | 21.0% -> **0.8%** | 42.60 -> 42.66 |
| 7 | 0 -> **0** | 29.5% -> **9.5%** | 24.6% -> **0.7%** | 42.68 -> 42.74 |
| 2026 | 0 -> **0** | 31.7% -> **9.7%** | 26.9% -> **0.7%** | 73.55 -> 73.66 |
| 31337 | 0 -> **0** | 36.1% -> **10.1%** | 31.5% -> **0.7%** | 54.26 -> 54.30 |

- **Pass lines:** on-grid <= 12.5%, which is twice chance (a shore within +-2 voxels of a line lands there 6.25% of the time by chance). Straight <= 5%.
  - Both have big margins below 37e (25-36% / 21-31%) and a little room above now.
  - The rest of the on-grid excess is the reach discs, which are centred on cells.
- **Map:** lakes unchanged (90/86/84/50). Lake area +0.1-0.2%. No leaks: geyser pools 0 unsettled.
  - The 37e row was taken with a slightly broader classifier (water at `LakeRimY`); that difference is tiny.
- **Logs:** `Logs/overnight/42/{before,lake_shore_*,lake_level_*}`, `fix5.log`, `suite_all.out`.
- **Depth:** shore-tile water volume on seed 42 is 1.46 G -> 1.03 G voxels. That is the wadeable first metres; lake middles keep their depth.
  - A first version capped every lake at about 4 m. The tests caught it and it was fixed.
- **Gates:** B4/B5/B6/B8/B10 all pass in SUITE=all, including the B8 route gate on 4 seeds. v3 hashes are unchanged. v4 stays stable and seed-dependent.
- **Memory:** the watchdog was on and killed nothing.
- **Pool probe** (not a gate, `Screenshots/42/new_poolprobe.txt`): slot high-water on arrival at the lake 87 -> 546. After placing water: 116,186 -> 111,985.

## Pictures (`Screenshots/42/`)
- `lake_before_after.png`: seed 42 lake, the same probe pose as 37e (same place point), no edit.
  - Left (37e): straight east-west near shore and straight diagonal right shore, with a flat sand plain.
  - Right (now): the shore curves with the ground and the water is one flat sheet. The ground in front shows gentle contour steps.
  - Look at the right-middle beach: a few single-voxel (0.1 m) nubs where the fold ramps in.
- `seed7_lake_top.png`: a seed 7 lake from straight above, 136 m square, rendered from column samples. White ticks mark the 8 m lines.
  - Left (37e): tan = ground exactly at the level. Rectangular steps at the north and east ends.
  - Right (now): the light-blue shore is natural all round.
  - **Why a map render, not a 60 m in-game shot:** the player has no seed flag (WORLD_SEED = 42 is a constant).

## Left open / next
- **OLDER DEFECT, not caused by this task (seen in both columns of `seed7_lake_top.png`):** the dark-blue blocks are RIVER water stepping down into the lake (5196 -> 5188 voxels).
  - Its 2x2 squares override the lake's own cells, standing 0.2-1 m above the lake behind grid edges.
  - Rivers are outside the at-rest scope (37e, this task), so no test sees it. In game it would show as a grid-edged water step that flows.
  - A fix is river/lake design work (for example, end a river at the lake's reach), so the owner decides; I did not queue it.
- The volcano lava lake is still unchecked (as in 37e).
- Next: the next queued task.
