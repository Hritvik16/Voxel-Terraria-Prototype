# 33c-B3b-soften-regions: DONE (`fd6f2c1`). Seed 42 median 16.6 -> 14.9 deg; rolling highlands 25 -> 15, badlands 25 -> 22, canyonlands 27 -> 22. Two targets still missed: steeper than 45 deg (alpine, left alone by design) and open ground.

**Owner check: does it still look natural?** All files are under `Logs/overnight/b3b/maps/`.
- `seed42_slope_bands_B3_B4_B3b.png`: three panels, B3 | B4 (before) | B3b. Look at the east coast, right of the volcano. Its orange and red turn yellow and green in B3b. The alpine range in the centre is unchanged on purpose.
- `seed42_slope_bands_east_B4_B3b.png`: the east half enlarged, B4 on the left, B3b on the right. The rolling highlands, right of the volcano, are now an even yellow ("Scramble", 15-30 deg). **Question: is that too uniform?** The talus pass leaves smooth ramps at one angle.
- `seed42_relief_B3_B4_B3b.png` and `seed42_landform_class_B3_B4_B3b.png`: same layout. Relief is almost unchanged. The class map is unchanged, which is correct.
- `walkposes_before_vs_B3b.png`: left before, right B3b, eye 2 m above the map ground in every row (the logs confirm the CPU store top equals the map height, so no eye is in rock).
  - Row 1, plains: flatter, and the far strip of sea at the left is gone. That is from B4's easy areas, not this step.
  - Row 2, canyonlands (4428,2764): identical frames. This spot (map 724.1 m in both) did not move.
  - Row 3, alpine: identical, as intended.
  - Row 4, rolling highlands (5900,3388), new pose, before = B4: the ground is 16 m lower (576 -> 560 m). Before, a wall filled the frame. Now you look down a valley with sky to mid-frame. Both are dim because the flysweep clock is at dusk.
- `poses_v4_b3b_sheet.png`: 9 of the 18 `MODE=poses` golden shots. No holes, no floating geometry, and the coast frame is about 50% sea (not 90%).
  - `eye_level_1` and `ledge_view` are black. `dark_poses_B4_B3b.png` shows they were equally black at B4 (ledge_view has the same mean luma, 3.97). These are fixed-height poses buried in rock, so this is NOT new.

## The change (v4 only): `Regions.P(k, mapVersion)`, used by the erosion inputs in `WorldMap.BuildV3`
- **What sets the slope:** the per-region erosion limits, not the starting amplitude. Halving the noise amplitude of the highlands or badlands moved their median by 1 deg or less. The talus pass holds a slope at its talus angle. Also, LESS erodibility made badlands STEEPER (25 -> 29), because stream power wears the uplifted land down. That matches B3's finding.
- **New values:**
  - Rolling highlands: talus 0.55 -> 0.1925, erodibility 0.07 -> 0.14.
  - Canyonlands: talus 0.80 -> 0.52.
  - Badlands: erodibility 0.13 -> 0.169.
  - Alpine and volcanic are untouched. Rough, terraces, biomes, the class map, iterations, rivers and B4 are untouched.
- **Sweep:** `Logs/overnight/b3b/experiment.txt`. It covers amplitude, octaves, wavelength, talus, terrace and erodibility per region, on seeds 42 and 7. The harness is kept at `XSoftenExperiment.cs.txt` with `experiment_hooks.patch` (neither is committed).

## Per-region median slope, deg (B4 -> B3b; "-" = region absent)
| seed | highlands | badlands | canyonlands | alpine | volcanic | class g/b/s |
|---|---|---|---|---|---|---|
| 42 | 25 -> **15** | 25 -> **22** | 27 -> **22** | 43 -> 43 | 29 -> 29 | 9/22/37 -> 9/19/37 |
| 7 | 15 -> 13 | - | 21 -> 19 | 46 -> 47 | 28 -> 28 | 9/17/40 -> 10/17/40 |
| 2026 | 11 -> 11 | - | - | 43 -> 43 | 30 -> 31 | 9/16/36 -> 10/16/37 |
| 31337 | 15 -> 12 | 18 -> 16 | - | 49 -> 49 | 29 -> 29 | 10/17/36 -> 10/16/36 |

## Targets (`PlayMetricsReport`; B4 before -> B3b; [B3])
| seed | median <= 15 | < 15 deg >= 50% | > 45 deg < 10% | open >= 20% | river >= 15 km |
|---|---|---|---|---|---|
| 42 | 16.6 -> **14.9** [18.8] | 47.1 -> **50.3** [44.3] | 11.2 -> 10.7 (miss 0.7) | 11.5 -> 11.5 (miss 8.5) | 26.2 |
| 7 | 15.4 -> 13.9 | 49.3 -> 53.6 | 12.6 -> 12.1 | 10.3 -> 9.8 | 27.2 |
| 2026 | 14.3 -> 13.2 | 51.6 -> 56.4 | 7.2 -> 7.1 | 11.4 -> 10.2 | 30.0 |
| 31337 | 16.2 -> 14.0 | 47.7 -> 53.2 | 6.5 -> 6.3 | 10.8 -> 10.0 | 22.7 |
- **The misses:**
  - The > 45 deg share is the steep class (alpine, volcanic, sea cliffs), which the task said to keep dramatic.
  - Open ground (relief over 250 m) did not move under ANY variant swept (11.3-11.6% on seed 42). It is not a regional-slope lever, and it dropped about 1 point on the other seeds. I tried the one other value allowed, F1 (highlands talus x0.25): seed 42 > 45 deg 10.6%, open 11.5%. It does not fix either miss. I shipped F2 (talus x0.35), the milder change.
- **Still true:**
  - Gentle share is 48.9-49.6% on every seed, all three classes are present, and there are 0 inselbergs.
  - B4 easy areas: 4 per seed, 10.0% of land, 0 failing cells.
  - v4 record hashes changed and differ per seed (42 `D1DA4EF62D3FEC4F`, 7 `38B84C483EC644FC`, 2026 `421BF43C416D0E21`, 31337 `7EA4ECAD54513489`).
  - v3 `playmetrics.txt` is identical to B4's, apart from timing lines.
  - Map build: v4 22.0-22.6 s vs v3 26.4-27.3 s.
- Reports: `Logs/overnight/b3b/after/` (B3b) and `b4_ref/` (before); runner log `pm.out`.

## Evidence
- `SUITE=all`: **PASS 642 FAIL 0 SKIP 15** (`Logs/overnight/b3b/suite_all.out`). This includes `NoNeedles_OnTheMap` (v3 + v4), `V4_Deterministic_SeedDependent_DiffersFromV3`, `MapV3_IsDeterministic`, `V4Map_GentleShare_AllClasses_NoInselbergs` and `V4Seed42_EasyAreas_CountSizeShare`.
- Screenshot logs: `arm_walkpose_*.out` and `arm_poses_v4.out` (shots in `FarBand/2026-10-08_183230_world/`); script `pics.sh`. The player was rebuilt at fd6f2c1. No timing arms were run (rule 10).

## Next
- Owner: is the evenly yellow highland ramp natural enough? If not, the next lever is fine detail on top of the talus ramps.
- Open ground at about 11% vs the 20% target is unmoved by region slope. It needs its own lever (wider flat floors or terraces), which is an owner decision.
- Not queued (measure-only/QA): the `eye_level_1` and `ledge_view` poses sit at fixed heights inside rock on v4 and should become terrain-relative.
- `Assets/Scenes/Playground.unity` was already modified before this task. I left it uncommitted.
