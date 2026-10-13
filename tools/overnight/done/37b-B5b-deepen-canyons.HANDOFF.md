# 37b-B5b-deepen-canyons: DONE. Canyon tableland committed (7cb9b26); SUITE=all PASS 654 FAIL 0 SKIP 17.
Owner Q33 (a), 2026-10-09: accept the tableland as it is and accept seed 42's route sight of 66.3%.

## What changed
- 7cb9b26: the patch `tools/overnight/blocked/37b-B5b-deepen-canyons.patch` applied cleanly on 6a20a59 (Features.cs, WorldMap.cs, DangerTests/PlayMetricsReport/WorldRecordTests).
- RouteTests: `MIN_IN_SIGHT` 0.70 -> 0.65, with the comment "owner Q33 (a) 2026-10-09: canyon tablelands lengthen routes; seed 42 = 66.3%". The header comment says 65% too. No other gate changed.
- Evidence (pictures, reports): 6a20a59. The first attempt's full report: `tools/overnight/done/37b-B5b-deepen-canyons.first-attempt.HANDOFF.md`.

## The change in short (v4 only; v3 untouched)
- Tableland: the banks rise to floor + 135 m on the higher-ground side and to 35% of that on the other side. The rise ramps up over 1.2 km from the mouth, then a 100 m flat rim and an outer slope 4x the rise (~14 deg). The bank yields to the easy areas and the shore, and it only ever raises the ground.
- Side canyons (`Gaps`): the bank is cut 56 m wide wherever a stream of >= 0.1 km2 crosses it.
- Floor grade 12 -> 6 m/km. The footprint is a pure function of the feature. Exact candidate range per coarse cell for `CanyonAt` (the hashes are identical with or without it).

## Evidence
- `SUITE=all`: PASS 654 FAIL 0 SKIP 17 (17 = report-only/ignored, the same as before). Log: `Logs/overnight/b5b/suite_all_final.out`. RouteTests V4_FourSeeds passes; seed 42 is in sight 66.3%.

| canyon | depth max / median (m), before -> after | walls > 45 deg (cells) |
|---|---|---|
| 42 [2] | 61/9 -> 143/74 | 93% (73) -> 56% (2682) |
| 42 [3] | 159/27 -> 169/89 | 96% (917) -> 75% (2410) |
| 7 [2] | 160/35 -> 172/57 | 86% (1828) -> 76% (4081) |
| 7 [5] | 140/71 -> 151/99 | 91% (1141) -> 84% (1820) |
| 2026 [3] | 35/2 -> 104/64 | 71% (7) -> 59% (2007) |
| 2026 [4] | 45/34 -> 117/67 | 92% (142) -> 55% (1891) |
| 31337 [3] | 54/35 -> 140/96 | 77% (128) -> 65% (1603) |

- B5: 5 features on every seed, spacing >= 1219 m. Sees a feature: 80.4 / 84.4 / 92.7 / 90.6% (before: 82.1 / 85.8 / 93.4 / 91.3).
- B4: 4 areas per seed, share 10.0%, no cell both easy and feature. On seed 2026 a spare area replaced one.
- B3 slopes (v4), median: 15.1 / 12.7 / 14.2 / 13.8 deg. Share > 45 deg: 13.7 / 15.9 / 10.5 / 8.0%.
- Map build vs v3: -0.2 / +3.0 / +0.1 / -1.8 s. Seed 7 was already over +2 s before this change (+2.1).
- Water: new lakes at the foot of the banks (fresh water 50 -> 62 ha on seed 42, 64 -> 90 ha on seed 2026). They are at spill level: nothing floating, no sea inland.
- Walls > 45 deg: share 55-84% after (was 71-96%), on 3-30x more wall cells. "Most walls" holds on every canyon.

## Pictures (Screenshots/B5b/, in 6a20a59; no new ones this session)
- `features_before_after.png`: one row per seed, before on the left, after on the right. Look at the darker raised banks beside the red canyons and the new lakes at their foot (seed 42, top right).
- `canyons_seed42_noon_before_after.png`: row 2 (120 m) shows canyon [2] with longer, deeper terraced walls on the right. The 2 m rim poses (rows 1 and 3) look across open ground: they are poor poses.
- `routes_seed42_after.png`: the north-south route now runs west, beside the west canyon.

## Left open / next
- Not queued: a rim-shot pose finder that actually looks into the canyon. The 2 m shots still do not show it.
- Seed 7's map build is +3.0 s over v3 (the limit is +2 s; it was +2.1 s before this change). Flag it for the next perf milestone.
- Task 37e requires this task and can now run.
