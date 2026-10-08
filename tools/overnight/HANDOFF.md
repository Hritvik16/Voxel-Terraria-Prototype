# 29-B3-light-erosion: DONE, targets MISSED. 60 iterations + half the uplift barely moves the slope numbers (seed 42 median 19.8 -> 18.8 deg). Erosion is not where the steepness comes from. Owner decides the next lever.

**Owner check: does it still look natural?** (folds into the B5 review pack; all paths under `Logs/overnight/b3/maps/`)
- `seed42_slope_bands_B2_B3.png`: three panels, B2 | B3 (60 it, start 0.6, shipped) | alt (60 it, start 1.0). Compare the red/dark-red ridges in the centre: B3 is a little smoother, with fewer fine gullies on the alpine range. The three panels are close to identical.
- `seed42_relief_B2_B3.png`: same layout. Green = open (< 20 m relief). Look at the south-west basin: B3 has slightly more green. Everything else looks the same.
- `seed42_landform_class_B2_B3.png`: same layout. The class map has not changed, which is correct (classes are B2's).
- `walkposes_B2_vs_B3.png`: left B2, right B3. Rows: plains (4772,5580), canyonlands (4428,2764), alpine (3948,3932). Eye is 2 m above the map ground in all three (`arm_walkpose_*.out`). Rows 1-2: no visible change. Row 3: B3's lake reaches a little higher up the frame (about half the frame is water).
- `poses_v4_b3_sheet.png`: six `MODE=poses` v4 shots. No holes and no floating geometry. The coast frame is about 50% sea, not 90%. Look at `cliff_foot_1` (bottom middle): a thin slab stands on the ridge.
- `cliff_foot_1_slab_crop.png`: the slab, enlarged. **New in B3:** B2's v4 shot (`FarBand/2026-10-04_132705_world/cliff_foot_1_golden.png`) has a clean ridge there. The band view (`FarBand/2026-10-08_090838_world/cliff_foot_1_golden_band.png`) shows it is drawn by the far band F0 (green), not by voxels. It is a one-cell spike in the map or a far-band artifact. NOT investigated. It goes against "nothing sticking out".

## Targets (`PlayMetricsReport`, REPORTS=1; seed 42 unless stated)
| metric | target | v3 | B2 v4 (240 it, start 0.2) | **B3 (60 it, start 0.6)** | alt (60 it, start 1.0) | miss |
|---|---|---|---|---|---|---|
| land < 15 deg | >= 50% | 29.5% | 42.5% | **44.3%** | 43.4% | -5.7 pts |
| steeper than 45 deg | < 10% | 29.1% | 15.1% | **13.3%** | 14.1% | +3.3 pts |
| median slope | <= 15 deg | 30.1 | 19.8 | **18.8** | 19.3 | +3.8 deg |
| open (< 20 m relief) | >= 20% | 4.1% | 11.2% | **12.1%** | 11.2% | -7.9 pts |
| river km | >= 15 | 22.7 | 25.1 | **25.5** | 26.4 | met |
| class median slope g/b/s | -- | 9/27/42 | 9/23/41 | **8/22/38** | 9/23/39 | |
| gentle share | 50 +- 5% | 26.7% | 49.4% | **49.4%** | 49.5% | met |
| map build (s) | <= v3 + 2 | 27.0 | 28.2 | **20.9** | 20.6 | met |

Other seeds, B3 (B2 in brackets). Median slope: 7 19.6 (20.8), 2026 19.4 (20.0), 31337 20.4 (20.7). <15 deg: 41.2 / 39.1 / 35.2% (39.4 / 37.9 / 34.4). >45 deg: 13.2 / 8.2 / 6.4% (14.9 / 9.7 / 7.2). Open: 8.6 / 5.1 / 6.4%. River km: 25.3 / 27.8 / 18.9. Gentle share: 48.8 / 48.8 / 49.2%. All three classes on every seed. Inselbergs 0 on every seed. Record hashes differ per seed (42 `199A49073361804E`, 7 `C0A9DC60B857ED21`, ...).
- **Reading:** the alt arm (no uplift at all, start 1.0) is *steeper* than start 0.6. So less erosion does not mean gentler land. The steepness is in the region starting shapes and the fine-map detail added after erosion. The seed-42 region slopes are alpine 43, volcanic 30, canyonlands 27, rolling highlands 26 and badlands 25 deg. Those are B4/B5-type levers (region shapes, detail amplitude) and need the owner's approval. This task tried only the one allowed alternative and did no further tuning.
- **Shipped value:** start 0.6, because it is the best of the two on every slope metric. 60 iterations also make the map build about 6-7 s faster than v3.
- v3 is unchanged: its numbers are identical to B2's v3 run on all four seeds (`b2_ref/playmetrics.txt` vs `b3_i60_s06/playmetrics.txt`).

## What changed
- `469ce00` (from the 08:37 run, reused): `WorldMap.cs` adds `EROSION_ITERATIONS_V4 = 60` and `EROSION_START_V4 = 0.6`. `Erosion.Run` gains a `start` parameter (default 0.2 = v3).
  - "Lower uplift" means: land starts at 0.6 of its target instead of 0.2, so it rises 40% of its height during the pass instead of 80% (half the uplift).
  - v3 path is untouched.
- This commit: HANDOFF only.

## Evidence
- `SUITE=all` PASS 636 FAIL 0 SKIP 13 at 469ce00 (`Logs/overnight/b3/suite_all.out`). This covers the needle guard, the hash-stability tests and v3 byte-identity. No TerrainGen code changed after 469ce00. Task 30 (`e4fa714`) later passed SUITE=all 637/0 on top of it.
- Metrics:
  - `Logs/overnight/b3/b3_i60_s06/playmetrics_v4.txt` (shipped) and `b3_i60_s10/playmetrics_v4.txt` (alt).
  - B2 reference: `b2_ref/playmetrics_v4.txt`. Runner logs: `pm_b3.out`, `pm_alt.out`.
- Poses: `arm_poses_v4.out`, shots `FarBand/2026-10-08_090838_world/`.
- No streaming or timing numbers (PREAMBLE rule 10). The `arm_walk_*`, `arm_fly14_*` and `arm_warmup*` files are discarded.

## Next step (owner)
1. Keep B3 as is, or revert v4 to 240 iterations? The slope gain is small. The map build is about 7 s faster.
2. Which lever for the slope targets: region starting shapes, or fine-detail amplitude? Neither is approved yet.
3. The cliff_foot_1 far-band slab: queue a bug task to check whether it is a map spike or a far-band artifact. It is a low-risk follow-up. I did not queue it, because that would mean investigating before the owner's decision on 1.
