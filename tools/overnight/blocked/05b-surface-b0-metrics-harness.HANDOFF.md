STOP_FOR_OWNER -- one question; it only gates B1, nothing else in the queue.
**Q (biome-change baseline):** the harness measures today's seed 42 biome change walking east-west as
**297 m** (sampled every 8 m). The review said 360 m, but it did not record how it measured this, and no
honest sampling reproduces 360 (8/16/32/64 m steps give 297/299/303/318 m; only a coarse 128 m step gives 371).
(a) **Recommended:** accept 297 m as the v3 baseline and keep B1's ratio: "at least 2.5 times v3", i.e. ~740 m
· (b) keep B1's absolute "at least 900 m" against the 8 m measure (3.0 times v3) · (c) measure at 128 m to match the review.

# Overnight handoff

**05b-surface-b0-metrics-harness: DONE except one number. The harness reproduces 6 of the review's 7 seed-42 numbers.**

## What changed (commit 0a80629 on design-brief)
- New `Assets/TerrainGen/Tests/PlayMetricsReport.cs` (+ .meta). Report-only, asserts nothing.
  To run it: `PLAYMETRICS=1 tools/run-editmode-filter.sh PlayMetricsReport`. Optional: `PLAYMETRICS_SEEDS=42,7` to
  pick seeds, `PLAYMETRICS_DUMP=1` to write the map as a .bin. It takes ~35 s per seed (27.5 s of that is the map build).
- No generator change. The definitions, the provisional band angles and the matched windows are in its header comment.

## Evidence
- EditMode: **PASS 626 FAIL 0 SKIP 4** (`Logs/overnight/05b-editmode.out`). The previous run was 626/0/3; the
  extra skip is this report, which is ignored without the env var. The determinism tests pass, so v3 is untouched.
- Report: `Logs/TerrainGen/playmetrics/playmetrics.txt` (from the run in `Logs/overnight/05b-filter5.out`).

| seed 42, v3 | review | harness | tolerance (1 pt or 10%) |
|---|---|---|---|
| median slope (8 m) | 30° | 30.1° | OK |
| steeper than 45° | 29% | 29.1% | OK |
| open (< 20 m relief, 250 m square) | 4% | 4.1% | OK |
| mountainous (> 80 m relief) | 73% | 73.3% | OK |
| biome change, walking east-west | 360 m | **296.9 m** | **MISS** (see the question) |
| town sites 4 ha+ | 11 | 11 | OK (7 in plains; the review said 8 in one region type) |
| landmark in every direction, 3 km | 2.2% | 2.2% | OK (same code as TerrainGenTests) |

Also matches the review: median slope by region (plains 8°, dunes 21°, badlands 27°, plateau 29°, volcanic 30°,
canyonlands 40°, sea cliffs 42°, alpine 48°), land under 15° at 29.5% (review: 30%), region change every 1,022 m
(review: ~1 km), and river 22.7 km (plan: "about 23").

Baselines (v3), seed 42 / 7 / 2026 / 31337: median slope 30.1/27.8/26.3/28.9° · >45° 29.1/23.8/22.9/24.9% ·
open 4.1/1.3/4.0/2.0% · mountainous 73.3/67.9/71.5/71.5% · gentle 26.7/41.6/39.8/27.3% · easy 16.4/15.3/20.9/13.1% ·
biome change 297/288/265/295 m · towns 11/9/8/12 · landmark 2.2/0.2/1.1/0.5% · mouths 0.78/0.75/1.12/0.80 per km².

How three definitions were matched (the review recorded only its results):
- Relief uses a 250 m-wide square (±125 m, sea floor not clamped). A ±250 m square gave 0.1% open / 94.7% mountainous.
- A town site is a 4-connected patch of land under 8° (the plan's rule-4 "flat room" angle), rivers and lakes
  included. A 5° dry-land rule gave 8.
- The bands (Walk < 15°, Scramble 15–30°, Climb 30–45°, Sheer > 45°) are **provisional**. The plan names the bands
  but gives no angles, so these use the review's 15°/45° cut points. B6 sets the real ones.

PNGs (512 px at 16 m/px, north up), five per seed: `Logs/TerrainGen/playmetrics/seed{42,7,2026,31337}_{landform_class,slope_bands,easy_ground,relief,biome}.png`.
Looked at: all five for seed 42 and seed 7's class map. They agree with the numbers. Gentle land is in the north
and centre, Sheer runs along the west and south coasts and the volcano, open ground only in the north, and easy
ground sits in the northern plains. Biomes are a ~300 m patchwork. One town site is the volcano's crater floor;
B7's pads should exclude such sites.

## Exceptions to the preamble
- Besides the plan, I made targeted searches (no full reads) in the terrain review doc (7443de71). It is the only
  place the metric definitions are written down, and the task required reproducing its numbers.

## Next step
- The owner answers the question at the top. B1's gate text in the plan should then quote this harness's v3
  baseline. B1 itself is queued by the supervisor after TAA, as the task said. No new task was queued.
