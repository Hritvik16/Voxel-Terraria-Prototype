Owner pick: gentle share 40/50/60% (shipped 50%). Strips are `Logs/overnight/b2_maps/seed42_landform_class_v3_40_50_60.png` and `seed42_slope_bands_v3_40_50_60.png`. Full table: `Logs/TerrainGen/playmetrics/b2_gentle_variants.txt`.
| seed 42 | gentle | broken | steep | median slope | <15 deg | >45 deg | open | mountainous | easy ground |
|---|---|---|---|---|---|---|---|---|---|
| v3 | 26.7% | 32.4% | 40.8% | 30.1 | 29.5% | 29.1% | 4.1% | 73.3% | 16.4% |
| v4 40% | 38.7% | 36.8% | 24.5% | 23.7 | 33.2% | 14.6% | 3.6% | 50.4% | 20.8% |
| **v4 50%** | 49.4% | 29.8% | 20.8% | 19.8 | 42.5% | 15.1% | 11.2% | 49.7% | 28.2% |
| v4 60% | 59.4% | 26.3% | 14.3% | 19.0 | 42.8% | 7.8% | 13.6% | 38.4% | 32.2% |
- Images, each strip left to right v3 | 40 | 50 | 60:
  - `seed42_landform_class_...png`: green = gentle, orange = broken, lilac = steep. Look at the lilac. At 50% and 60% it is only the CENTRE: the alpine peak region and the volcano (pinned, always steep). Every sea cliff region of v3 (the lilac west and south-west coast) is gone.
  - `seed42_slope_bands_...png`: green < 15 deg, yellow 15-30, orange 30-45, red > 45. Look at the red. In v3 it rings the west coast. At 50% it is confined to the peak massif and some terrace edges north of it. At 60% it is almost only the peak.
- **Note for the pick:** at 50% the pinned alpine + volcano regions already fill most of the steep share (about 20%). Seeds 42 and 31337 have NO sea cliffs and no second alpine range. Seeds 7 and 2026 keep one sea-cliff region (6.3% / 4.1% of land). To bring sea cliffs back: unpin, a lower gentle share, or count cliffs separately.

# 17-B2-landform-by-flatness: DONE. Every map acceptance passes; v3 byte-identical; streaming NOT MET (as 16b predicted), v4 faster than v3
## What changed (surface-rebuild 2855a36)
- `TerrainSpec.gentleShare` (0 = 0.50 on v4, ignored below v4) and `GentleShare`.
- `Regions.ClassOf`, `LandCells`, `AssignByFlatness` (v4 only):
  - Seeds are as in v3; alpine peak and volcano stay pinned.
  - Every free region gets a class by exhaustive search (3^10). The target is gentle G, broken 0.6(1-G), steep 0.4(1-G). Area is measured on a 256^2 grid, with at least 1 gentle and 1 broken region.
  - Then an archetype of that class: coastal or inland, from shuffled lists.
  - Warp and nearest-region were factored out of `Weights` (same arithmetic).
- `WorldMap`: no inselbergs on v4; `InselbergCount`. The record archetype is read from `map.Region`, so it follows the new deal (`V4Record_HoldsWhatTheMapSays` passes).
- Tests: new `LandformClassTests` (2, Slow). `NoNeedles_OnTheMap` now runs on v3 and v4. The B1 test is now `V4_Deterministic_SeedDependent_DiffersFromV3` (v4 is no longer v3 cell for cell).
- `PlayMetricsReport`: `PLAYMETRICS_GENTLE=0.4,0.5,0.6`, the B2 tables, and an inselberg count.
## Evidence -- maps (`Logs/TerrainGen/playmetrics/b2_landform.txt`, one run)
- `SUITE=all` **PASS 629 FAIL 0 SKIP 12** (`Logs/overnight/17_suite_all.out`).
- **Gentle share, 50 +- 5:** 49.4 / 48.7 / 48.8 / 49.2% (seeds 42 / 7 / 2026 / 31337). PASS. All three classes are present on every seed. The estimate grid hits 40/50/60 within 0.3 points.
- **Inselbergs v3 -> v4:** 15/17/17/12 -> 0. (The task's "40" is the landmark count; 15 domes actually raised land on seed 42.)
- **Needle guard on v4:** the worst 40 m drop is 17.1 m (seed 42) and 15.3 m (seed 7), under the 25 m limit. v3: 22.0 / 18.9 m.
- **Build time v3 -> v4:** +0.5 / +1.0 / +0.7 / +0.7 s. PASS (<= +2 s).
- **Record hashes** (v4): A2185B5C0F77F4A6 / 870D49600DDE9492 / CA300216029599C4 / CC1DEAF667A7AB5E. Two builds match and the seeds differ (test).
- **v3 unchanged:** the map.bin dumps and all 20 PNGs of all four seeds are byte-identical to the pre-B2 copies (`Logs/overnight/b2_v3_ref/`).
- **Slopes v3 -> v4 at 50%** (median deg, then >45 deg): 30.1->19.8, 29.1->15.1% | 27.8->20.8, 23.8->14.9% | 26.3->20.0, 22.9->9.7% | 28.9->20.7, 24.9->7.2%. Mountainous 73->50%, 68->49%, 72->40%, 72->38%.
- `b2_maps/four_seeds_v4_50_landform_class.png` shows the four seeds at 50%, left to right 42/7/2026/31337.
  - On 42 and 31337, the lilac (steep) is just two radial cones in the middle. Seed 7 and 2026 also have a lilac sea-cliff strip on the north-east and east coast.
  - Seed 2026: orange dunes in the south, striped with long thin interdune lakes.
  - Seed 31337: a large lake (~1 km) in the north-centre gentle land. Worth a look in B3.
## Evidence -- streaming (one build; `Logs/overnight/b2_stream_summary.txt`, arms in `b2_arm_*.out`)
- All arms ran on AC. `walk_v3_1` overlapped the owner pause and ended on battery. It is shown but not used, and was rerun as `walk_v3_3`.
- Every arm is in the **fast** walk mode (gen p50 about 150-210 ms; B1's slow mode was about 370-390).
- p50 numbers, v3 -> v4; section 7 is NOT MET on every arm:
  - **Walk:** worker gen 181/208 -> 153/183 ms. Isolated 53/61 -> 51/70 ms. Admitted 52/40 -> 85/71 /s. Capacity 31.7/27.7 -> 37.8/31.4 /s against REQUIRED 2,285 /s (short 60-83x). Request->GPU p50 7.9/10.0 -> 4.4/6.4 s. Latency p99 32-33 -> 25-28 s against 0 s. Deficit 1945/2141 -> 1447/1704. Upload pass 0.28/0.25 -> 0.38/0.51 ms.
  - **Fly 14 m/s:** worker gen 288/308 -> 181/217 ms. Admitted 39/38 -> 56/47 /s. Capacity 20.7/19.7 -> 31.8/27.2 /s (short 72-116x). Latency p99 19-22 -> 10 s. Deficit 2716/2780 -> 2681/2738. Frame p95 18.8/18.5 -> 30.1/28.2 ms. Upload bytes/frame p95 0.9 -> 1.9-2.2 MB (more admits).
- Peak footprint on the stream arms: 2.9-3.2 GB on both versions. All are over the 3 GB cap, as known.
## Evidence -- poses (`b2_arm_poses_v4.out`; shots in ~/Library/.../FarBand/2026-10-03_212657_world/)
- **NEW: `MIRROR lost 12232` (peak_from_2km_ground) and `5249` (summit_view).** stale 2/2/1 on the first three poses, 0 after. B1's v4 poses run had 0. Peak footprint 3763 MB vs B1's 3414 (Untagged +372 MB). Engine code is unchanged, so this is queued as **21-poses-mirror-lost-check** (evidence only).
- Images from this run:
  - `peak_from_2km_ground_golden.png`: a green hill fills the right half, with grey and brown peaks on the left. No holes. A flat tan wall sits lower left.
  - `summit_view_golden.png`: a red-brown slope fills about 80% of the frame, with the sea at the right edge. No holes.
  - `map_7000m_noon.png`: the whole island from above. Biomes are large (snow north and south-west, desert centre-west); the volcano is a dark disc. Not a 90% sea frame; nothing floating.
  - `coast_from_sea_30m_golden.png`: the known task-18 black bar is lower left over the sea; the island is on the horizon.
  - `eye_level_1_noon.png`: a fixed pose now on the volcano's flank, facing dark basalt steps. Terrain, not a hole.
- Walk poses (v4 seed 42, eye 2 m, pitch 5 deg down, `FlySweep/<ts>/h02_p05_beauty.png`):
  - Gentle (plains, 4772,5580), `2026-10-03_233207_...`: rolling green ground to a sea horizon, with a beach strip at the right.
  - Broken (canyonlands, 4428,2764), `2026-10-03_233911_...`: stepped sandstone walls rising about 30 m, with terraces.
  - Steep (alpine, 3948,3932), `2026-10-04_000203_...`: a dark, mossy mountainside climbing from a lake shore. The lower ~45% of the frame is the lake.
  - The first steep try (3908,3620) put the eye INSIDE rock: the ground is 914 m, above the sweep's fixed 868.8 m start. Discarded.
## Left / next
- The owner picks 40/50/60 (and decides whether sea cliffs must come back). Then B3 (erosion; "still natural?").
- Queued **21-poses-mirror-lost-check**. `Assets/Scenes/Playground.unity` (a build side effect) is left as found.
