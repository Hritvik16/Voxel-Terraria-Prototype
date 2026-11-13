# 190-placement-towns-on-sites: DONE. `-towns 1` flattens the 187 sites and places the hut / house / hall kit on them; towns are spread apart. The default stays OFF; the owner decides from the pictures.

## What changed (commit 32a77e4)
- **Spacing** (`Sites.TownSep2`): no town sits inside another town's fair share of the land, a disc of area land / towns, about 1.35 km here. An easy-area town that is too close is dropped. The missing towns go on **Walk ground anywhere** ("tier 2": every town disc rule, near a route). If nothing fits, there are fewer towns, never a town on bad ground. DECISION (mine): tier 2 keeps 3 towns per island. "Drop only" would leave seed 7 with 1 town. Easy to switch if the owner prefers.
- **Flatten** (`Assets/TerrainGen/Towns.cs`, inside `ShapeField.Sample`, so every path agrees):
  - The disc is set to the pad level, and a ring R/2 wide is blended back to the natural ground.
  - The ring stops only at water, features or another site. Columns with water, a stamp or a ledge are never moved.
  - Caves stay 6 m below a town. No trees and no fluid pockets inside towns.
- **Buildings** (`Towns.Layout`): town = 1 hall + 8 houses + 8 huts; frontier 1 + 4 + 4; village 3 + 3; outpost 2 huts.
  - The hall goes at the centre, its door toward the route. The rest stand on rings round an 8 m plaza, doors facing the centre, with 4 m lanes.
  - Never on route, stamp, start, water or feature cells, nor on natural steps steeper than 30 deg.
  - The footprints go into `WorldRecord.BuildingList`.
- **Engine** (`TerrainGenChunkSource.Towns.cs`): the town plane reaches every sampling job.
  - LOD 0 and the point query use the exact kit part.
  - Shells show the highest solid part in each coarse cell, so far walls and roofs have no holes.
  - The far band gets the flattened heights only.
- The world survey now prints task 188's `[pools]` line.

## Evidence
- Tests:
  - `TownTests` (fast: an adversarial one-corner island gives 1 town of 2, a layout adversarial, OFF == the 188a pins, every column outside the towns byte-identical; Slow: 4-seed table).
  - `TownChunkTests`: chunk == kit; ON vs OFF differ only inside site discs + rings; FarQuery == chunks; shell 0 == Generate; shell 1 exact; shells 2-3 have no holes in the hall.
  - `SiteTests`' checker now knows tier 2 and the spacing.
- Whole suite: **PASS 1058 FAIL 0 SKIP 27** (`Logs/overnight/190/all.out`, wall 26 min).
- Counts (`Screenshots/190/table.txt`):

| seed | towns (tier 2) | frontier | villages | outposts | buildings (hall/house/hut) | min town distance before -> after |
|---|---|---|---|---|---|---|
| 42 | 3 (0) | 1 | 6 | 4 | 102 (4/46/52) | 1533 -> 1533 m |
| 7 | 3 (2) | 1 | 4 | 6 | 96 (4/40/52) | **739 -> 1359 m** |
| 2026 | 3 (1) | 1 | 4 | 6 | 95 (4/39/52) | 1361 -> 1459 m |
| 31337 | 3 (1) | 1 | 2 | 8 | 84 (4/34/46) | **899 -> 2363 m** |

- Cost at load: `[Towns] plan + layout` takes 245-262 ms in the player; the plane is 3.7 KB.
- Pools (`[pools]`, 4 launches, `Screenshots/190/<seed>/lookreview.txt`):
  - Shell 1 peak: 39-44 k of 150 k bodies (26-29 %).
  - Shell 2 peak: 13-31 k of 119 k.
  - 0 refusals.
  - One hall's shell-1 chunk: 871 dense bricks ON vs 719 OFF (`chunks.txt`).
- Not done: no timing (not a performance task). Shells 3+ were mostly unfilled after the 45 s waits.

## Look notes (owner)
- **Black upper walls** in every eye shot are the eaves' shadow. At noon the sun is 78 deg high, so the 0.4 m eaves shade ~1.9 m of wall, and shade renders black (task 193, sky light in shade, is queued). The gable ends and lower walls are lit.
- **Grey box in `Screenshots/187/eye_frontier.png` = an ARCH stamp, not a defect.** It is at (4696.5, 5917.1) m, 432 m from the eye, at bearing 23 deg (an exact match). A snow rise hides its opening. The arch is a flat-topped rectangular slab 30-50 m long and 15-30 m tall by design, so it reads as a box. Possible look question: rounder arch tops. The grey slabs on the far hills in `42/ws_fixed_village_eye` look like a stone ring.

## For the doc
- Seed 7: closest towns 739 m -> 1359 m; seed 31337: 899 -> 2363 m. Every seed keeps 3 towns (`Screenshots/190/table.txt`).
- 84-102 buildings per island (4 halls, 34-46 houses, 46-52 huts); 0 rule failures in an independent checker.
- `-towns 0`: byte-identical (the 188a pins); with towns on, every column outside the town discs + rings is unchanged.
- Shell pools with towns: peak 29 % of shell 1, 0 refusals.
- `Screenshots/190/spacing_7_before_after.png`: left 187, right 190. The dashed orange circles are the spacing; T16-T18 move from one southwest cluster to west / centre / east.
- `Screenshots/190/plans_7.png`: 200 m plans. Buildings (hall red, house orange, hut yellow, black = door wall) sit inside the white disc, off the cyan routes.
- `Screenshots/190/<seed>/ws_fixed_town_eye_native.png`, `ws_fixed_village_eye_native.png`, `ws_fixed_town_150m_native.png`:
  - 42: forest timber + slate town, savanna adobe + thatch village.
  - 7 town: centre, the stone hall's front and door between the houses.
  - 150 m: the town as a compact cluster with no pad edge visible.
  - 2026 village: mostly at the left edge of the frame.
