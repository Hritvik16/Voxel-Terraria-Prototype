STOP_FOR_OWNER -- B6's "no distance zones" gate fails on seed 42: danger correlates -0.43 with distance (gate |r| <= 0.3). Which way?
  (a) RECOMMENDED: keep the table, make the gate report-only until placement picks the real start, then gate against that start.
      Why: the steep landform regions sit in the middle of seed 42's island, so this is geography, not distance used as an input. The other three seeds pass.
  (b) Retune the data table, e.g. steep landform tier 2 -> 1. Even with altitude dropped entirely, seed 42 is still -0.34, so this flattens danger overall.
  (c) Terrain work: B2's region dealing keeps steep regions away from the centre. That changes v4 terrain, so it would be its own B step.

# 35-B6-danger-record: DONE except the distance gate (held, above). Record written; terrain unchanged; SUITE=all PASS 647 FAIL 0 SKIP 16.

## What changed (commit 3a52a7c on surface-rebuild)
- `Assets/TerrainGen/Danger.cs` (new). It writes the record's `hazard` byte: bits 0-2 hold danger 0-7 (altitude third + landform class + biome tier); bits 3-4 hold the movement band; bits 5-7 are zero.
  - The tier table is DATA (`Danger.Default`): altitude {0,1,2}, landform gentle/broken/steep {0,1,2}, and biome by GenBiome id. A biome with no row throws.
  - Bands: Walk < 15, Scramble 15-30, Climb 30-45, Sheer >= 45 deg, compared as tan^2. They live only here; `EasyAreas.WALK_TAN2` and PlayMetricsReport's band edges now read them.
  - Sea cells (map height <= 0) store 0. Lakes and rivers are rated like their ground. Land with Region 255 rates as gentle; there are 0 such cells on all 4 seeds.
- `WorldRecord.Build` calls `Danger.Mark` last. Its header documents the byte.
- `WorldRecordTests`: the reserved-field check now covers only hazard bits 5-7.
- `DangerTests` (new):
  - The table and band-edge test, which is fast.
  - `V4_FourSeeds_DangerAndBandsRecorded` (Slow), which checks: recorded hazard == the table's; easy cells at tier 0 for both landform and altitude; sea = 0; the record rebuilds identically; at least 4 levels. It writes `Logs/TerrainGen/danger/b6_danger.txt`, plus PNGs when REPORTS=1.
  - `V4_FourSeeds_DangerIsNotADistanceZone`, the gate, which is `[Ignore]`d pending the owner. To enforce it, delete the Ignore line.

## Biomes vs the table (task asked for this list)
- Mapped today: Forest -> 0, Jungle -> 2, Desert -> 2, Snow -> 2 (the table's "snow" row).
- No GenBiome id exists yet for: savanna (0), alpine taiga (1), badlands (1, B10), volcanic ashlands (3). Their rows are comments in `Danger.Default`.
- So today no cell has biome tier 1 or 3, and danger 7 needs ashlands. Max danger seen is 6.

## Evidence (seeds 42 / 7 / 2026 / 31337; full table in `Screenshots/B6/b6_danger.txt`)
- Danger histogram, % of land, danger 0..7:
  - seed 42: 18.1 / 7.3 / 35.7 / 23.7 / 11.6 / 2.9 / 0.8 / 0
  - seed 7: 10.0 / 9.2 / 45.8 / 21.8 / 11.3 / 1.6 / 0.4 / 0
  - seed 2026: 15.5 / 3.5 / 40.1 / 26.3 / 12.5 / 1.4 / 0.7 / 0
  - seed 31337: 10.6 / 5.4 / 40.7 / 26.1 / 13.3 / 2.9 / 1.1 / 0
- Bands, Walk / Scramble / Climb / Sheer: 42: 51.7 / 24.7 / 11.7 / 11.9%; 7: 55.7 / 17.6 / 12.0 / 14.7%; 2026: 55.4 / 21.9 / 13.8 / 8.9%; 31337: 54.1 / 27.5 / 11.0 / 7.4%.
  - Bands by atan in degrees disagree with the record's tan^2 bands on 0 cells.
- Correlation with distance, spawn / centre:
  - 42: -0.431 / -0.424 FAIL; 7: -0.156 / -0.206; 2026: -0.195 / -0.168; 31337: -0.141 / -0.154.
  - The `Tg2Spawn` placeholder (copied into the test) lands on map cell (500,500) on every seed, about 135 m from the centre, so the two references nearly coincide.
- What drives seed 42 (correlation with distance from the centre):
  - each tier alone: altitude -0.435, landform -0.598, biome +0.049;
  - table variants: altitude 0/1/1 -0.406, altitude 0/0/1 -0.365, no altitude -0.340. All of them still fail.
- Easy cells: every one is landform tier 0 and altitude tier 0 (27300 / 26920 / 27908 / 26856 cells). Their max danger is 2, which comes from the biome tier on jungle/desert/snow.
- Terrain unchanged: map-plane FNV hashes (height, rough, water, flow, water class, region) equal the B5 baseline taken before the change (`Logs/overnight/b6/baseline_hashes.txt`): C85622635EE1F566, 50800CE7D387B1B8, 7ED42358937A231F, D5E2A10DEB2994E7.
  - The record hashed with hazard masked out equals B5's record hashes (E14BB8B2..., AF9BCC1F..., 258C629D..., A0311666...).
- New record hashes (stable, seed-dependent): DECE1BF25475BCD3, 8FB7EB8FD423FCA3, 2C1991391DC12BB7, 3AA88A7706236E7E.
- v3 is untouched: the record is v4 only; the suite's v3 tests are green.
- SUITE=all: `Logs/overnight/b6/suite_all.out`. No performance arms (rule 10).

## Pictures (`Screenshots/B6/`)
- `danger_four_seeds.png`: one panel per seed, flat 0-7 ramp (pale yellow = 0, deep purple = 7), with the legend at the right. The white square is the spawn placeholder.
  - Look at seed 42 (top-left): a dark red/purple danger core fills the island middle, right around the white square, and the coasts are pale. That is the failing correlation.
  - Compare seeds 7 and 31337 (right column): their danger patches are spread out or off-centre.
- `bands_seed42.png`: movement bands for seed 42 (green Walk, yellow Scramble, orange Climb, red Sheer).
  - The central massif is almost solid red/orange Sheer and Climb. The red rings around the north lake and the southern coast are cliff walls. Walk dominates the outer lowlands.

## Next
- Owner: choose (a), (b) or (c) above. Under (a) or (b) the change is one line: delete the Ignore, or edit the table.
- 36-B8 (routes) reads danger and can go ahead on this record under (a) or (b).
