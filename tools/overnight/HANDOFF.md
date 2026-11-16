# 212-perf-t0-measure-play-steps: DONE -- play is 106.8 ms p50 at walk_near (432 rows, warm), not 8.17's est. 43-52; next T2, then T4 before T3

**Owner, please read:** today's play at play's own flags (rows pinned 432) runs ~107 ms per frame at walk_near (~9 fps) and ~135 ms at
cliff1. 8.17 section 3 assumed 37.4 ms today and play ~43-52 ms (est.). The torch flood twin (27.8 ms) and sky fill 2 (40.4 ms) are
together ~68 ms, about the whole rise since 168b (FULL 37.2). 8.17's T1-T5 estimate (-6..-23 ms) cannot reach 33 ms from 107 ms;
the plan's order holds (T2 first is what the checkpoint says), but its budget table needs re-basing on these numbers (owner/supervisor).

## What changed
- 874f431: 8.17 adopted (status line, section 7 answers Q1-Q7), CLAUDE.md ground truth lists 8.17.
- 7232a42: cost split arms `flood_off`, `skyfill0` and path set `-costpaths cliff` (capture only, opt-in). Fast suite PASS 1049 FAIL 0 SKIP 22.
- This commit: TASKS.md paragraph (Task 212) + this HANDOFF. No other code change.

## Arms (warm steady state, AC, play flags, FSR 3 at 432 rows pinned; 3 s settle + 20 s timed per run, order F X X F per arm)
Saving = arm vs the mean of its two bracketing FULL runs, averaged over the clean sets.
| arm | walk_near p50 / p95 | saving p50 | cliff1 p50 / p95 | saving p50 |
|---|---|---|---|---|
| play (FULL) | 106.8 / 204.7 | -- | 135.3 / 240.0 | -- |
| flood_off | 79.2 / 153.8 | 27.8 (27.2, 28.4) | 113.0 / 194.9 | 22.0 |
| skyfill0 | 66.4 / 130.1 | 40.4 (40.9, 39.9) | 78.0 / 130.7 | 54.5 (one-sided, see below) |
| sun_off | 78.2 / 153.9 | 28.1 (28.8, 28.2, 27.3) | 99.8 / 187.4 | 37.9 |
| shells_off | 98.8 / 197.6 | 8.4 (9.8, 9.8, 5.6) | 111.1 / 227.3 | 24.7 |
| ceiling (sun + sky probe + shells off) | 24.6 / 43.4 | 82.6 | 33.9 / 59.1 | 99.4 |
- Sets: walk_near C `Logs/costsplit_2026-11-15_143504.txt` (warm-up `Logs/tg2world_20261115_142714.txt`), E `Logs/costsplit_2026-11-15_160531.txt`
  (warm-up `Logs/tg2world_20261115_155847.txt`), F `Logs/costsplit_2026-11-15_181007.txt` (warm-up `Logs/tg2world_20261115_180320.txt`;
  arms sun_off, shells_off, ceiling only). cliff1: E runs 17-22 (full, flood_off, skyfill0) + F (sun_off, shells_off, ceiling).
- cliff1 skyfill0 has only its left FULL bracket (132.5; the right one was discarded); cliff FULL runs span 132.5-138.2, effect 54 ms.
- cliff1 streams through the timing: pinned pose resident ~2,440, deficit 633-662 in every arm (same condition for all arms).
  walk_near: deficit 0. Shells after the 90 s wait: walk_near s3-s7 missing 2,514-2,557; cliff1 missing 1,479-1,664.
- p95 is ~2x p50 in every heavy arm (and 1.8x in ceiling): frame times are bimodal; not investigated here.
- shells_off also swaps the kernel (no shells -> cascade path, analytic torches, task 206 rule), so its saving includes the flood twin's
  share of that path; it is not a clean "shells alone" number.
- Idle check: `Logs/overnight/212/hididle` (CoreGraphics per event type) every 5 s, logs `Logs/overnight/212/idlewatch_<set>.txt`;
  ioreg's HIDIdleTime is reset every ~10 s by a running player, so it was not usable. Set F: idle 930 s at start, 4,256 s at the end.
- Discarded (input in them): warm-ups A, B, D (aborted); set C's whole cliff1 path (input during runs 17-19); set E run 23 (cliff1
  FULL pass 3, 159.2 ms, input at 16:46 and the owner hold appeared). No timing above comes from an arm with input in it.

## 199's finding: CONFIRMED
Every FULL run prints `kernel lean` (the flood twin `CSMainFarShadeFlood`; diet 2 needs `!floodFarTwin`, `RaymarchFeature.cs:1473`);
every flood_off run prints `kernel diet2`. Cost of the twin + flood together: 27.8 ms walk_near, 22.0 ms cliff1 (the arm cannot split
"diet 2 bypassed" from "flood lighting"). At noon (walk_near is 12.00 h) the flood lights almost nothing and still costs this.

## GPU trace: not taken
`tools/gpu161.py` reads an unlabelled release trace: encoders are named by position only, not by step. 8.17 section 3: per-step ms
from the trace come only after T6. It would not give per-step ms today, so per the task it was skipped.

## Next step (8.17 checkpoint)
flood twin 27.8 ms >= 3 -> **T2 is next** (S4 out: flood + point lights, flood twin gone). sky fill 40.4 ms >= 4 -> **T4 before T3**.
Order by the rule: T2, T1, T4, T3, T5, T6. Sky fill is the biggest single cost, so T2, T4, T1, T3 is the alternative (owner's call).

## Memory (rule 11)
Measurement only: no content, data or drawing change in this task, nothing to compare against an OFF switch.

## For the doc
- Play today, walk_near p50: **106.8 ms** (8.17 had assumed 37.4 today, est. 43-52 for play) -- `Logs/costsplit_2026-11-15_160531.txt`
- Torch flood twin: **27.8 ms** walk_near, 22.0 ms cliff1 (199's finding confirmed: diet 2 is bypassed whenever the flood draws)
- Sky fill 2: **40.4 ms** walk_near, ~54 ms cliff1
- Sun shadows 28.1 ms, shells 8.4 ms (walk_near); everything heavy off ("ceiling"): 24.6 ms
- cliff1 play p50 135.3 ms -- `Logs/costsplit_2026-11-15_181007.txt`; next step T2, then T4 before T3
- No pictures: measurement only.
