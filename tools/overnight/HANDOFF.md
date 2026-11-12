# 187a-stampfit-default-on: DONE. Stamps that sit in the land (task 185) are now the DEFAULT in play, rigs, capture runs and tests. `-stampfit 0` brings back the pre-185 stamps, byte for byte.

## What changed (commit 412f328; task 186's pattern)
- `TerrainSpec.stampFitOn` became `stampFitOff`. 0 means the task-185 fit is ON, so every v4 spec fits.
  - `StampFit => mapVersion >= 4 && stampFitOff == 0`.
- `TerrainGenChunkSource.StampFitArg` now defaults to true. The chunk source and `FarField` set `stampFitOff = 1` only for `-stampfit 0`.
- Where the default lives: I grepped every `stampFit` use, and the TerrainSpec field is the only one. There is no SerializeField or scene-builder copy (`Playground.unity` has no "stamp").
- Effective value is now logged. `StreamManager` and the `[FarField] tg2 ...` line both print `stamp fit ON (default)` / `OFF (-stampfit 0)`.
- Tests are pinned to the path they describe, with assertions unchanged:
  - `StampFitTests.World` and `FluidAtRestTests.Sea_AtRest_StampFit_Seed2026` take the default spec as ON and pin OFF with `stampFitOff = 1`. The map ignores the flag, so they use the default spec's shared map and need no extra build.
  - No other test encoded the old stamp shape: the whole suite was green on the first run.
- Docs:
  - KNOWN_ISSUES: the seed-2026 sea fossil is marked FIXED by default; only `-stampfit 0` still shows it.
  - TASKS.md: the 185 header now says DEFAULT ON, and there is a new 187a paragraph.
  - `Stamps.cs`: header comment updated.
  - `Logs/overnight/187a_shots.sh`: the shot script.

## Evidence
- Fast suite: PASS 970 FAIL 0 SKIP 19 (`Logs/overnight/187a/fast1.out`).
- Whole suite: **PASS 1036 FAIL 0 SKIP 25, 24m26s** (`Logs/overnight/187a/all1.out`). That is the same count as 187. All `StampFitTests`, `FluidAtRestTests` and `SiteTests` passed.
- Site planner (187) with stamps fitted: `Logs/TerrainGen/sites187/table.txt` matches 187's committed `Screenshots/187/table.txt` line for line except plan ms.
  - Town/frontier/village/outpost per seed: 42 3/1/6/4, 7 3/1/3/7, 2026 3/1/4/6, 31337 3/1/2/8.
  - This is expected, because sites read only the map and record, and they keep out of stamp discs.
- Player log lines (`Logs/overnight/187a/player_default.log` / `player_off.log`):
  - Default: `[StreamManager] tg2 chunk source: map v4, material rules ON (default), stamp fit ON (default)` and `[FarField] ... material rules ON, stamp fit ON`.
  - `-stampfit 0`: `... stamp fit OFF (-stampfit 0)` on both lines.
- Shots: seed 42, High, 45 s shell wait, one launch per arm, 3 poses each.
  - Shells s3+ were mostly unfilled at the cap (s2 ~1,200 of 2,170 stored, s3-s7 0 stored). The far band draws them, so the foreground of these poses is unaffected.
  - DEFAULT is `LookReview/2026-11-12_093706_high/`; `-stampfit 0` is `LookReview/2026-11-12_094316_high/` (under ~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/). They match task 185's ON/OFF pictures.

## Unfinished / next
- Task 185's leftovers still apply now that the fit is the default:
  - The geyser basin's uphill edge has a thin comb of soil columns.
  - The basin's steep side becomes many narrow 1.5 m steps. You can see them on the right of the river shot.
- `-stampfit 0` shot of the cave pose: a faint straight seam line runs across the sea in the upper right. It is not in the default shot and not caused by this task (a far-band/shell edge while streaming). Noted only.
- Next: the queue's next task. No follow-up queued.

## For the doc
- Default stamp fit: OFF (`-stampfit 1` needed) -> ON (`-stampfit 0` = old). Log: `stamp fit ON (default)` (`Logs/overnight/187a/player_default.log`).
- Whole suite: PASS 1036 FAIL 0 SKIP 25 before (187) -> PASS 1036 FAIL 0 SKIP 25 after (`Logs/overnight/187a/all1.out`); 0 assertions loosened.
- Sites per seed (town/frontier/village/outpost) with stamps fitted: unchanged, 42 3/1/6/4, 7 3/1/3/7, 2026 3/1/4/6, 31337 3/1/2/8 (`Logs/TerrainGen/sites187/table.txt`).
- From 185, now the default: cave trenches 7 -> 0, ledge contour-ring steps 406 -> 0, hoops 26 -> 10 (`Logs/TerrainGen/stampfit185/`).
- Images: each pair is the same file name in DEFAULT `2026-11-12_093706_high` vs OFF `2026-11-12_094316_high`.
  - `ws_fixed_cave42_over_native.png` (seed 42 coast cave, centre of frame): DEFAULT is a round black mouth going into the hill. OFF, the tube runs as an open half-pipe out toward the sea cliff on the right.
  - `ws_fixed_wetland_basin_native.png` (canyon wall right of the river): DEFAULT has flat shelves on the rock bands with a flat-ceilinged dark hollow, right of centre. OFF has an egg-shaped ledge striped with contour rings in the same spot, plus a small cave on the left slope that the fit drops.
  - `ws_fixed_river_native.png` (geyser basin, foreground): DEFAULT has white sinter terraces stepping down toward the river, with the pools held on their terraces. OFF is one flat white ellipse with round pools lying over the slope.
