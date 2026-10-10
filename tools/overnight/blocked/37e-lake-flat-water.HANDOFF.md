# 37d-B4b-easy-area-sweep: DONE. B4's 20-seed easy-area sweep is 20/20 again (c44381a); SUITE=all PASS 654 FAIL 0 SKIP 17.
The first attempt (12:31) hit the usage limit while diagnosing. This session reverted its DIAG code and finished the task.

## Why each seed failed (at HEAD 7cb9b26, before the fix)
- **11 (an area of 22.7 ha) and 65537 (3 areas, plus one of 21.9 ha).** Too few sites. `Features.MarkBad` marked feature footprints as `bad`, and `Pick` rejects any window that touches even one bad cell.
  - Long canyons rule out a band ~800 m wide, which cut the candidate windows by about half (seed 11: 1077 -> 391).
  - So Pick seated 4 of 6 sites on seed 11 (the 4th was 82% non-gentle and 0.8 km from area 0, so they shared cells) and 3 of 6 on 65537.
  - Evidence: with MarkBad switched off (diagnostic run), both seeds pass.
- **12 (an area of 8.7 ha).** The lowest-third rule. B5 took away the pinned peak, so a world without a great peak (seeds 1, 12, 123) tops out near 300 m and its lowest-third line falls under 97 m.
  - `LEVEL_MAX` (an absolute 100 m) then pressed seed 12's plateau sites (237/215/146 m starting means) to cores of 106-121 m, all above the line. 4 of 6 sites had under 1,400 easy cells.
  - Seeds 1 and 123 passed only narrowly for the same reason.

## The change (v4 only; `EasyAreas.Pick` + `WorldMap.BuildV3`)
- Feature footprints now go to their own `featureBad` array.
  - RELAX passes 0-3 still exclude them exactly as before.
  - A new last **EDGE pass** runs only if those passes seated fewer than 6 sites. A window there may touch a feature margin (<= 35% of its cells) or the beach/sea (<= 15%), and each such cell costs like non-gentle land.
  - Mark still never flags a feature cell or a sea cell.
- **Lowest-third prediction:** 0.7 x (the highest starting-shape land / 3).
  - The press level is capped at the predicted line.
  - A window whose pressed height is above the line costs 10 per voxel over it and waits for the EDGE pass.
  - On worlds with a peak the line is over 140 m, so nothing changes there.
- `Features.PlaceLate`'s seat check calls Pick without `featureBad`, so feature placement uses the old rule.
- v3 is untouched: the code runs only on the v4 path, and the v3 hash tests pass.

## Evidence
- `EASYSWEEP=1`: **20/20**, every area 42-45 ha. Before: 3 seeds failed, and seeds 8/99/123 had uneven areas of 26-38 ha. Reports: `Screenshots/B4b/easysweep_before.txt`, `easysweep_after.txt`.
- `ROUTESWEEP=1`: 20/20, 2+ links per area, 0 m Sheer (`Screenshots/B4b/routesweep_after.txt`). Seed 42's route in-sight share is now 84.5% (it was 66.3%; the gate is 65%).
- `SUITE=all`: PASS 654 FAIL 0 SKIP 17, the same 17 report-only tests as before (`Logs/overnight/b4b/suite_all.out`). This covers B5 (5 features, a canyon, >= 1.2 km), B6 danger, the B8 4-seed gate, B2/B3, and the hashes. After that run, the only edits were two header comments.
- B5 report: features 5/5 and spacing pass on 11/12/65537/42. Seed 42 sees a feature: 81.4% (80.4% after 37b).
  - Seeds 12 and 65537 show 55.0% and 79.9% MISS on that report-only line (its >= 80% target is for the 4 default seeds).
  - I have no before number for those two: it was NOT compared.
- No performance runs (rule 10).

## Pictures (Screenshots/B4b/)
- `easy_areas_before_after.png`: one row per seed (11, 12, 65537 from top to bottom), before on the left, after on the right. Yellow = flagged easy cells.
  - Row 1: the small 4th area of 22.7 ha is gone; there are now 4 areas of 44 ha (a new one in the east).
  - Row 2: seed 12's two ragged coastal patches (66 ha and 9 ha) become 4 round 44 ha areas: two in the north, one west, one east.
  - Row 3: the 3 areas become 4, two of them close together in the south-east.
- `after/seed42_v4_easy_areas.png`: seed 42 now keeps a spare at the NE coast in place of its old 4th area (202 m mean, 20% non-gentle). The 4 areas: two at the NE coast, one west, one south. **This changes the play world's areas.**
- `before/`, `after/`: the single PNGs plus `b4_easy_areas.txt`.

## Left open / next
- Areas that touch each other (8 m routes) predate this change: 14 in B8's sweep, 12 now. Spacing in the last passes is 0.8 x MIN_SEP.
- Not compared: "sees a feature" on seeds 12/65537 before vs after.
- Task 37e (lake flat water) is next in the queue.
