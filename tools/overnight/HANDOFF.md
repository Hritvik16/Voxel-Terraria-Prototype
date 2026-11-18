# 235-perf-budget-worst-case-poses: DONE. The budget check now judges worst-case poses. The first clean run is rebaselined: worst pose forest_eye at 16 deg, 135.4 ms p50 (4.1x the 30 fps frame).

## What changed (scripts, JSON and docs only; no Assets code). Commit 92bacb1
- `tools/overnight/run-budget-check.sh`: runs 4 cost splits after the warm-up, read from `PERF_BUDGET.json` "launches". The header says why (owner 2026-11-17).
  Input during a launch's TIMED path makes it dirty. Input during a still-only path is reported only. `--compare walk=W forest=F jungle=J night=N`.
- `tools/overnight/budget_compare.py`: per-pose rows, one pose x step table, the worst pose per step, and top 3 steps at the worst pose. The verdict uses the WORST-CASE rows and the hard limits only; noon rows are a control (info).
  A FULL row is now also downgraded to "noisy" when its FULL range exceeds its tolerance. A MISSING row now = FAIL.
- `PERF_BUDGET.json` v2: `worst_case`, `launches`, `poses`, and the pinned `jungle_eye_pose`. The v1 noon baseline moved to `previous_baselines`. The new baseline + history entry were written by `--rebaseline "owner 2026-11-17, task 235"`.
- PREAMBLE rule 12 and `PERF_BUDGET_GUARD.md` sec. 3 each got one line: "report worst-case poses, not only noon / no torches". There is also a TASKS.md paragraph. `BUDGET_COUNT` -> 0 (accepted check).
- Design: forest_eye and jungle_eye keep a streaming deficit, so EVERY run there waits NearSettle's 180 s cap (~3.7 min a run). Those launches therefore time only sun_off and skyfill0 (+ startsun_off at forest).
  Day flood is not timed: every day FULL line said `flood skip` = 0 by construction (229). rest = FULL - sun - sky - flood.

## Evidence (warm steady state; warm-up `Logs/tg2world_20261117_235342.txt`; run `Logs/budget/2026-11-17_235256/`, table `check.txt`)
CLEAN (0 input moments, no pause), BUDGET PASS, rebaselined. p50 ms (% of 33.3):
| pose | FULL | sun | sky fill | flood | rest | p95 | >33 ms | deficit |
|---|---|---|---|---|---|---|---|---|
| forest_eye 16 deg | **135.4 (407 %)** | 54.7 (164 %) | 25.5 (77 %) | 0 (skip) | 55.2 (166 %) | 198.2 | 97.9 % | 573 |
| jungle_eye noon | 70.0 (210 %) | 15.5 (47 %) | -0.8 (~0) | 0 (skip) | 55.3 (166 %) | 99.9 | 99.5 % | 942 |
| torch_camp night (tf_on) | 52.7 (158 %) | 0.4 | 0.9 | 8.4 (25 %) | 43.0 (129 %) | 76.5 | 90.9 % | 0 |
| walk_near 16 deg | 35.4 (106 %) | 12.9 (39 %) | -0.5 (~0) | 0 (skip) | 23.0 (69 %) | 86.9 | 56.8 % | 0 |
| control: forest_eye noon | 99.2 (298 %) | | | | | 138.6 | 97.3 % | 573 |
| control: walk_near noon (same arc) | 34.1 (102 %) | | | | | 64.0 | 53.9 % | 0 |
- The poses furthest over 33.3 ms: forest_eye 16 deg +102.1 ms, jungle_eye +36.7, torch_camp +19.4, walk_near 16 deg +2.1.
- Top 3 steps at the worst pose (forest_eye 16 deg): rest 55.2, sun 54.7, sky fill 25.5. The low sun alone costs +36.2 ms there vs noon (135.4 vs 99.2, same launch; 233 measured +26.8).
- Noise: forest FULL passes 145.2 / 134.0 / 134.1 / 134.3 (the first pass is high, so its sky-fill delta has noise 11.2 and is about 20 ms if you use the after-pass). walk_near FULL range 13.5 (the sun step's noise is 13.5), jungle 4.4, night 8.8.
- Shells (walk_near 16 deg only): 7.0 ms (share 4.0). Ceiling 18.6 ms.
- Memory (rule 11): peak 2,621 MB (forest launch; walk 2,535, jungle 2,521, night 2,512): under 3,000. GPU refusals: walk_near 0 (hard limit PASS), jungle 4,472 (pool full at 254,400, the known issue). CPU pool peak 496,071, valve 484,500.
- Stills looked at (`Screenshots/235/sheet_worst_case.png`): forest 16 deg shows long low-sun shadows; noon does not. Jungle: tall dark trunks, sky through the canopy. Night: a slope lit by torches, 0.5 h sky. Both walk_near stills open on a close sandstone wall.
- Fast suite PASS 1107 FAIL 0 SKIP 25 (`Logs/overnight/235/fast.out`). Nothing under Assets changed.
- Time: 99 min (warm-up 5 min, walk 10, forest 43, jungle 31, night 9). That is OVER the ~40 min the task named. The 4-launch split already keeps each launch under ~45 min, and the deficit wait is the cost.

## Caveats / next
- **walk_near at 16 deg is NOT a worst case.** The capture picks the walk arc from the sun's azimuth, so at 16 deg it is a different arc (it starts facing a wall, and the minimap says "underground -12 m") than the old noon arc (67 ms in 230).
  Its noon control is the same arc (34.1). Suggested follow-up (capture-only code, needs a task): `-costwalkarc <deg>` to pin the arc to the old noon one (-180 deg), then rebaseline the walk rows.
- Shorter check: a capture option to cap NearSettle's deficit wait (e.g. 20 s) would cut forest/jungle from ~3.7 to ~1 min a run (check ~100 -> ~45 min). Not done: capture code is out of this task's scope.
- Not mine, not committed: `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*`, the 236 queue-file deletion.

## For the doc
- How far is the worst case from 30 fps? Forest at a morning sun (16 deg) runs at **135 ms a frame, about 7 fps**, which is **4.1x** the 33.3 ms budget. Jungle at noon runs at 70 ms (about 14 fps), torches at night at 53 ms (about 19 fps), and the walk arc at 35 ms (about 28 fps).
  Even with the sun shadows, sky fill and torch flood all free, forest 16 deg would still be about 55 ms ("rest": the near march, trees, FSR and CPU), so no single lighting fix reaches 30 fps there.
- Worst pose per step: sun 54.7 ms (forest 16 deg; share 5.0), sky fill 25.5 ms (forest 16 deg; share 2.5), torch flood 8.4 ms (night; share 1.0), rest 55 ms (forest and jungle).
- The low sun alone: forest 99.2 ms at noon -> 135.4 ms at 16 deg (+36 ms, same launch).
- Memory: peak 2,621 MB (under 3 GB). Jungle GPU pool full (4,472 refusals, known).
- Table: `Logs/budget/2026-11-17_235256/check.txt`.
- Image `Screenshots/235/sheet_worst_case.png`: top row is forest 16 deg | forest noon | jungle noon, bottom row is torches at night | walk_near 16 deg | walk_near noon. Compare the long shadows in the top-left panel (the costly low sun) with the short noon shadows next to it. The two bottom-right panels show why the walk row is not a worst case: a wall fills the frame.
