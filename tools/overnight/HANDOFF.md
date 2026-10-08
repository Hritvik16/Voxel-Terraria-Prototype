# 33-B4-easy-areas: DONE, every acceptance check PASSES. Map v4 seeds 4 easy areas before shaping (Medium, Q1 (a)), 10.0% of land, flagged in the record with a list. Code `d6ee1c1`.

## What changed (v4 only; v3 byte-identical)
- **New `Assets/TerrainGen/EasyAreas.cs`.** The pass runs in three steps.
  1. **Pick** runs on the 16 m starting shapes, before erosion. A site is scored on a 640 m square: low mean height + low spread.
     It costs extra for non-gentle land, for being a basin floor, and for being a predicted lake (`Hydrology.Flood` run on the starting shapes).
     Sites must avoid the beach, the peak and the volcano, and the wetland basin does not count as gentle here (it is shaped to hold lakes).
     The pick is greedy, with a spread bonus (`SPREAD`: 2 km is worth 100 m of score). It seeds 4 + 2 spare sites, with looser passes if too few fit.
  2. **Press:** inside 400 m, with a 350 m fade, starting relief keeps 20% around the site's mean. Talus is 0.15 and detail is a wetland's.
     There are no terraces or dunes, and a hollow needs 3 m before it becomes a lake.
  3. **Mark** runs in `WorldRecord.Build`. It keeps the 4 sites with the most easy ground. Each area takes 10% of land / 4 cells, nearest first.
     Areas grow round-robin, so ground that two areas both reach is split by nearness. An area that comes up short is made up by the others (each up to +50%).
  - Easy ground means: Walk (8 m slope < 15°), gentle class, under 1/3 of the highest land, and dry. Biome does not count (Q2 (a)).
- **Record:** `FLAG_EASY` = flags bit 0. `EasyAreaList` holds the centre, ha, radius, mean height and cells. `WorldRecord.MapVersion` is new.
  `WorldMap.EasySites` holds all 6 sites.
- **Size class:** sizeClass 2 (8 km tg2) is **Medium = 4**, as in the B5 queue entry (`EasyAreas.CountFor`). No start position, flag or list (owner 2026-10-04); `Tg2Spawn` untouched.
- **Tests:** new `EasyAreaTests`: a seed-42 gate (Slow), plus an opt-in 20-seed sweep (`EASYSWEEP=1 tools/run-editmode-filter.sh EasyAreaTests`) that fails naming every failing seed.
  `WorldRecordTests` now allows the easy bit. `PlayMetricsReport` writes the `b4_easy_areas.txt` table and `seed<N>_v4_easy_areas.png`.
- **`PlaygroundCapture.FlySweep`** (evidence tool):
  - The eye now stands over the **exact voxel ground** (it used to fall back to the map height, as in B3's walk poses), and it logs the player's own easy-area list.

## Evidence (no performance arms, rule 10)
- `SUITE=all` **PASS 642 FAIL 0 SKIP 15** (`Logs/overnight/b4/suite_all.out`).
- **20-seed sweep: 20 of 20**, every seed 10.0%, 4 areas, smallest area 36 ha (`Logs/overnight/b4/easysweep_20of20.txt`).
- PlayMetrics (`Logs/overnight/b4/maps/b4_easy_areas.txt`, `playmetrics_v4.txt`). The full table:

| seed | easy share | areas, ha | gentle | inselbergs | build s v4 (v3) | median ° B3→B4 | <15° B3→B4 | >45° B3→B4 | open B3→B4 | river km |
|---|---|---|---|---|---|---|---|---|---|---|
| 42 | 10.0% | 4: 44 44 44 44 | 49.6% | 0 | 21.9 (27.0) | 18.8→16.6 | 44.3→47.1% | 13.3→11.2% | 12.1→11.5% | 26.1 |
| 7 | 10.0% | 4: 43 ×4 | 49.0% | 0 | 21.9 (26.4) | 19.6→15.4 | 41.2→49.3% | 13.2→12.6% | 8.6→10.3% | 27.2 |
| 2026 | 10.0% | 4: 45 ×4 | 48.9% | 0 | 22.3 (26.7) | 19.4→14.3 | 39.1→51.6% | 8.2→7.2% | 5.1→11.4% | 30.0 |
| 31337 | 10.0% | 4: 47 47 41 36 | 49.3% | 0 | 21.9 (26.5) | 20.4→16.2 | 35.2→47.7% | 6.4→6.5% | 6.4→10.8% | 23.0 |

- Other checks:
  - Flagged cells that fail the report's own definition: 0.
  - The record rebuilds to the same hash. Hashes differ per seed (42 `C6540AEA5F6324D9`, 7 `24A408B86C236814`).
  - v3 is unchanged: every v3 metric is identical to B3's v3 run (diff, timings aside).
  - **The player's map has the same 4 areas as the Editor's** (same centres, 43.7 ha each; flysweep log), despite the known Editor/IL2CPP map difference.
- **Pictures** (all in `Logs/overnight/b4/maps/`):
  - `easy_areas_4seeds.png`: 2×2, seeds 42/7/2026/31337. Yellow = flagged easy cells, over the class map (green gentle, orange broken, purple steep).
    Look for: every yellow area sits on green, away from the purple mountains.
    Seed 31337 (bottom right): one area bends round the big central lake. Seed 2026 (bottom left): all four areas cluster in the north-west.
  - `easy_walkposes_seed42.png`: 2×2, a 2 m eye over the real voxel ground at each seed-42 area centre, 5° down, facing north-east.
    - A, top left: flat pale ground with green plain behind.
    - B, top right (plateau): flat ground beside a pond.
    - C, bottom left: green gentle rolling plain.
    - D, bottom right: flat sand, with terraced canyon cliffs outside the area.
    No holes, nothing floating, no eye in rock.
  - `poses_v4_b4_sheet.png`: 9 `MODE=poses` v4 shots (all 18 poses MIRROR 0/0/0, outOfBox 0; `arm_poses_v4.out`). No holes, no floating geometry; the coast frame is about half sea.
    Known issues that are not new: `eye_level_1` is black (a capture pose, from 32c), and `cliff_foot_2` has a dark blade on the left.
  - `b3_vs_b4_cliff_map.png`: top row B3, bottom row B4. `cliff_foot_2`'s blade and the dark caldera disc in `map_7000m` are identical in B3, so they are not new. The `cliff_foot_1` slab is gone (32c).

## Read this (owner, for the B5 review pack)
- **B4 also made v4 gentler overall.** Seed 42 median is 16.6° (B3 18.8°); seeds 7 and 2026 are at or near B3's ≤ 15° target.
  The reason is that 6 pressed sites with their fades cover a large part of the land. If that is too much flattening, shrink `FADE`/`SPREAD` or drop the spares.
- **The task's "easy ground 8-12%" is read as the record's flagged cells.** Easy ground anywhere (the old PlayMetrics line) is ~32-35% of v4 land, which is far over 12%. Only the seeded areas can meet the band.
- **Some areas touch:** seed 2026's NW cluster, seed 42's west pair. `SPREAD` (0.05) is the knob. A larger value spreads areas more but pushes them onto higher or rougher ground.
- The flagged areas are near-round (nearest cells first). The pressed terrain under them has a noise-bent edge.
- **Next step:** task 34 (B5) as queued.
