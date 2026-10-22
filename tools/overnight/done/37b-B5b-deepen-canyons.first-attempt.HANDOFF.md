STOP_FOR_OWNER -- 37b-B5b-deepen-canyons: deep canyons need raised banks, and on seed 42 those banks push B8's route sight to 66.3% (gate 70%). Pick one:
  (a) accept 66.3% on seed 42 for B8's "next waypoint in sight" (owner gate change), and I commit the patch as it is;
  (b) a lower bank (e.g. 90 m, not 135 m): less blocking, but "max >= 100 m" would then rest on the natural ground (some canyons would miss);
  (c) where a canyon's higher side lies between it and the peak (seed 42's east canyon), put the full bank on the other side. Routes keep the inland corridor, but B5 visibility on seed 42 then drops (tested once: 79.0%);
  (d) drop the 100 m max target: the median >= 40 m holds even with the full bank on fewer sides.
  Recommended: (a). Every other B5b gate passes; the route only became longer and goes round the tableland.
# 37b-B5b-deepen-canyons: BLOCKED. A canyon "tableland" meets the depth targets on 7 of 7 canyons. SUITE=all is red on one B8 gate (seed 42 route sight). Code is NOT committed.
Code: tools/overnight/blocked/37b-B5b-deepen-canyons.patch (the runner saves it; a copy is in Logs/overnight/b5b/37b-tableland.patch). Evidence commit: 6a20a59.

## Why the floor can't simply be cut deeper
A canyon's floor only falls toward the sea (about 2 m at the mouth, 11-17 m at the head). So depth = rim - floor, and through lowland the rim is low. B5's shallow canyons ran through 10-60 m ground.

## The change (v4 only; v3 untouched, all in Features.cs + one WorldMap line)
- **Tableland.** The banks are raised to floor + 135 m on the higher-ground side and 35% of that on the other side. A full bank on both sides hid the canyon and the peak from the lowland: seed 42 fell to 77.5% seeing a feature.
  - The rise ramps up over 1.2 km from the mouth (the way in).
  - Then a flat 100 m rim, and an outer slope 4x the rise (about 14 deg mean).
  - The bank yields to the easy areas and the shore (about 340 m), and it only ever raises the ground.
- **Side canyons (`Gaps`).** A drainage pass on the eroded grid cuts the bank 56 m wide wherever a stream of 0.1 km2 or more crosses it. Without this, a dammed outlet flooded seed 31337's easy area.
- **Floor grade 12 -> 6 m/km.** The floor itself is now carved deeper.
- **Footprint.** Natural walls or bank walls + 20 m, plus the rim (up to 100 m) where the bank stands. It is a pure function of the feature, not of the map's heights.
- **Speed.** Per coarse cell, an exact candidate-segment range for `CanyonAt`: the hashes are identical with or without it.
- **Tests.**
  - The record "rebuilds identically" checks (WorldRecordTests, DangerTests, PlayMetricsReport) now rebuild with the kept easy areas, as WorldMap does. A fresh regrowth on the route-eased map differed by 2 easy cells on seed 42. This is B8's design, not a determinism bug: two full builds are identical.
  - The report gains wall steepness and a depth profile from head to mouth.

## Evidence (Screenshots/B5b/reports/b5_features_{before,after}.txt; all logs in Logs/overnight/b5b/)
| canyon | depth max / median (m), HEAD -> after | walls > 45 deg (cells) |
|---|---|---|
| 42 [2] | 61/9 -> 143/74 | 93% (73) -> 56% (2682) |
| 42 [3] | 159/27 -> 169/89 | 96% (917) -> 75% (2410) |
| 7 [2] | 160/35 -> 172/57 | 86% (1828) -> 76% (4081) |
| 7 [5] | 140/71 -> 151/99 | 91% (1141) -> 84% (1820) |
| 2026 [3] | 35/2 -> 104/64 | 71% (7) -> 59% (2007) |
| 2026 [4] | 45/34 -> 117/67 | 92% (142) -> 55% (1891) |
| 31337 [3] | 54/35 -> 140/96 | 77% (128) -> 65% (1603) |

- **B5:** 5 features on every seed. Spacing is unchanged (>= 1219 m). Sees a feature: 80.4 / 84.4 / 92.7 / 90.6% (HEAD 82.1 / 85.8 / 93.4 / 91.3).
- **B4:** 4 areas per seed, share 10.0%, no cell both easy and feature. On seed 2026 the spare (50960, 27280) replaced (37840, 42000).
- **Slopes (B3, v4):** median 15.1 / 12.7 / 14.2 / 13.8 deg (HEAD 14.3 / 13.0 / 13.3 / 13.9); >45 deg 13.7 / 15.9 / 10.5 / 8.0% (HEAD 11.9 / 14.7 / 8.9 / 7.4).
- **Map build vs v3 (s):** -0.2 / +3.0 / +0.1 / -1.8 (HEAD -0.2 / +2.1 / -0.5 / -2.4). Seed 7 was already over +2 s at HEAD.
- **SUITE=all** (before the last two fixes): PASS 652 FAIL 2 SKIP 17.
  - The Danger rebuild check is fixed since.
  - RouteTests seed 42: in sight 66.3% (B8: 72.8%). The 5-route set now has a 3.4 km extra route 0-1. Its passes are 54% in sight; features on the route are 89% in sight. The re-run after the rim footprint is still 66.3%.
  - Hashes: two builds are identical and seeds 42/7 differ (WorldRecordTests PASS); the v3 tests pass.
- **Water:** fresh-water area on the feature maps went from 50 -> 62 ha on seed 42 and from 64 -> 90 ha on seed 2026. These are lakes at the outer foot of the banks: real spill-level lakes, none floating, and no sea inland.

## Pictures (Screenshots/B5b/)
- `features_before_after.png` -- one row per seed, left HEAD | right after. Canyons are red; look at the darker raised banks beside them. Seed 42 (top row, right): a new long lake east of the peak, at the bank foot. Seed 2026 (3rd row, right): lakes beside the upper canyon, and the easy-area circle that moved to the lower right.
- `canyons_seed42_noon_before_after.png` -- noon, the same eye left | right.
  - Row 2: canyon [2] from 120 m. Its terraced walls are longer and deeper on the right.
  - Rows 1 and 3: B5's rim poses at 2 m. They look across open ground and do not show the canyon in either version, so these poses are poor.
- `canyon2_after_deepest_rim.png` -- canyon [2] at its new deepest point (143 m).
  - Left (2 m eye): only sand, no canyon in view.
  - Right (120 m): terraced brown walls with water on the floor (lower left), and lakes on the desert plain beyond.
  - So from these poses the canyon still does NOT read clearly. A better pose finder is a follow-up.
- `routes_seed42_after.png` -- left half of the island: the north-south route now runs west, beside the west canyon. At B8 it ran down the east side (see Screenshots/B8/).

## Next
The owner picks (a)-(d). If (a): `git apply tools/overnight/blocked/37b-B5b-deepen-canyons.patch`, change RouteTests' MIN_IN_SIGHT as decided, run SUITE=all, commit. Follow-up, not queued: a rim-shot pose that actually looks into the canyon. Task 37e requires this task.
