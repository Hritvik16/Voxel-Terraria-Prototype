STOP_FOR_OWNER -- task 176d (ship biome Size 2). Size 2 IS committed and the whole suite is green except the route test. The route test is red for one reason the task did not allow, so I am stopping.
- **Q1. Seed 42's route test now fails because of 178's scree, not a river ford.** This is the same thing as 178's Q2.
  - Size 2 moved the routes. Seed 42's route 0-1 now crosses a scree foot at (3777, 5278) m.
  - There, 178's scree apron lifts the ground by up to 3.4 m: **3.4 m of Sheer**, steepest 1 m step 48 deg.
  - With `-cliffs 0` the same spot is 28 deg, not Sheer. There is no water near it (wetnbr 0) and the map slope is 16 deg. Numbers: `Logs/overnight/176d_probe_heights.txt`.
  - Seed 31337's 1.5 m is the known river-ford bank (allowed red).
  - (a) **Recommended:** answer 178 Q2 with (a), "the relief skips route cells". A 178b then fixes both seeds' scree steps. Nothing else is needed here.
  - (b) Leave it red until C5 together with the ford.

# 176d-worldgen-ship-biome-size-2: Size 2 shipped (regions gate = presence, owner 176c Q1 (a)); whole suite PASS 1064 FAIL 1 (route test: ford on 31337 + 178 scree on 42)

## What changed (the "176d: ship biome Size 2" commit on engine-phase)
- `ShapeField.cs`: 176c's patch applied cleanly on top of 177/178.
  - `CLIMATE_WAVE_R` 0.75 -> 1.5, a 4th octave `CLIMATE_OCT4` 0.12 in `Climate3`; `BlendClimateReach`'s bound gains `OCT4 * 2.03^3`. `BiomeSizeDump.cs`: 176c's check.
- `BiomeLayoutTests.cs`: ONLY the regions gate changed, to `REGIONS_MIN = 1` ("every biome present, >= 1 region of >= 25 ha"; was 2). The comments cite the owner's 2026-11-06 decision. Every other C1 gate is unchanged.
- Re-pinned goldens:
  - ShellFill GOLDEN: same 85 chunks. The biome under them moved, so their cover and layer rocks changed.
  - `WorldMapBurstTests.PLAYER_V4_SEED42` 74C7E69A614F8996 -> **32D19FFC21BE14D0**, taken from the PLAYER log `Logs/lookreview_20261106_060453.txt`. It equals the Editor's hash. It moved because the biome feeds danger, danger feeds the routes, and the routes ease the map.
- `PlaygroundCapture.WorldSurvey.cs`: the survey header now prints the tg2 report's "map v4 Height FNV" line. A survey launch can now re-pin the hash; no 20-min tg2world launch is needed.
- `TASKS.md` paragraph. `KNOWN_ISSUES.md` route line and the CLAUDE.md index line: new seeds and causes.

## Evidence
- Filtered run (`Logs/overnight/176d_filter1.out`):
  - C1Layout PASS. Every BiomeBlendTests case passed, including the early-out bound with the 4th octave.
  - `Logs/TerrainGen/c1/c1_layout.txt` (copy: `Screenshots/176d/c1_layout.txt`), across the 8 seeds:
    - one-region biomes 1-3 per seed, none absent
    - straight border 1.06-1.42 km (gate 1.7)
    - change E-W 512-701 m
    - median patch 106-231 ha
    - shares 5.7-28.3 % (all equal to 176c's numbers)
- **Whole suite: PASS 1064 FAIL 1 SKIP 33** (`Logs/overnight/176d_all.out`, 42.5 min). The one failure is `RouteTests.V4_FourSeeds_RoutesJoinTheEasyAreas`:
  - seed 31337: 1.5 m, the ford (allowed)
  - seed 42: 3.4 m, 178's scree (Q1)
- Survey launch: 5 fixed poses (176/177's), native arm, 30 s shell cap, log `Logs/lookreview_20261106_060453.txt`.
  - Shells 4-7 were mostly not stored within 30 s (the `[shells]` lines, e.g. s4 12-187 of ~987). The distant background is the far band.
- Before shots = 177's after shots (`Logs/lookreview_20261106_012752.txt`). They are from BEFORE 178, so the badlands thin stripes -> thick grey layers change in the pairs is 178's, not Size 2's.
- Biome at each pose target (record plane, seed 42), before -> after:
  - start: snow -> forest
  - desert_alpine-range: snow -> savanna
  - forest_river-plains: savanna -> forest
  - great-peak-0: snow -> savanna
  - range_200up: snow -> savanna
- Gameplay consequence: the island's tallest peak is no longer in the snow biome. It shows savanna rock with little snow. Snow is still seed 42's largest biome (28.3 %), on other ranges.
- Rough cost: `Climate3` adds 1 snoise per climate sample, which is negligible. In the launch, a worker spent 71-105 ms per chunk. This is not comparable to 178's 110-121 ms (different poses, no timing arm).

## Next
- Owner: Q1 (= 178 Q2). After that: 178's Q1 (keep cliffs).

## For the doc
- Area-weighted median biome patch, 8 seeds: 44-65 -> **106-231 ha** (`Screenshots/176d/c1_layout.txt`).
- Biome change walking E-W: ~375 -> **512-701 m**. Longest straight border: 1.04-1.65 -> **1.06-1.42 km** (gate 1.7).
- One-region biomes: 0 of 48 -> **14 of 48** (allowed now: the gate is presence). Absent biomes: 0.
- Player map hash seed 42: 74C7E69A614F8996 -> **32D19FFC21BE14D0** (= Editor).
- Route test: 31337 1.5 m (ford) + **42 3.4 m (178 scree, Q1)**.
- Images: `Screenshots/176d/viewer.html` shows full size. Space flips before | after; arrows change the image.
  - `Screenshots/176d/map_seed42.png` (plus `_seed7`, `_seed2026`, `_seed31337`): left = 176 size, right = Size 2, 1 px = 8 m, north up. Look at patch size, 2-3x bigger. Desert (ochre) and savanna (gold) are ONE region each on seed 42.
  - `Screenshots/176d/poses_sheet.png`: 5 rows, before (left) | after (right).
    - Row 3 (forest_river-plains): yellow savanna plain -> green forest.
    - Row 1 (start): the start is now forest.
    - Rows 4-5: the peak is savanna rock. Its thick layers are 178's.
