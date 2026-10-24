STOP_FOR_OWNER -- 93b2 part two: the cave fill on shells 1-2 FIXES THE CAPS (start pose complete, 0 FULL) but the walk's end got WORSE for shells 2-3.
Question (supervisor: number it): the walk is now short on WORKERS, not caps. What next?
 (a) RECOMMENDED: keep the fill on shells 1-2 and approve a PERF task that cuts the fill's worker cost (+25-54 ms per shell-1 chunk; the mouth
     pass is ~70% of it). Goal: fill <= ~10 ms per chunk.
 (b) Ration shell 1 (e.g. a cap on its share of the workers). This changes the Q47 (a) order policy.
 (c) A 4th shell worker. The fanless M1 heats up.
 (d) Revert to fill from 3 (`FIRST_FILL_SHELL = 3`, one line): back to 93b2's state, with the caps.
Plainly: even before this change the walk's end held only 35-44% of shell 1. Reaching >= 90% at 5 m/s needs ~2-3x the worker throughput, whatever the fill costs.

# 93b2 (part two, owner Q54 (a)): fill on shells 1-2 -- caps FIXED, walking target NOT met (workers)
## What changed (commit 52aad5e, engine-phase)
- `FIRST_FILL_SHELL` 3 -> 1: shells 1-2 get task 83's fill, with fluids as in 83b. Rays from outside see the same.
- Bug found and fixed: mouth sub-sample offsets were `odd << (shift - 3)`, which wraps for shifts 1-2. Now `(odd << shift) >> 3` (`SubOff`). Shells >= 3 keep the same bytes.
- `-shellfillfrom N` (diagnostic; 3 = the old bytes; `FillsShell` per source) and `ShellStreamer.OpsRedrawnOnArrival` (a filled chunk is redrawn, not caught up).
- Tests: `ShellFillTests` now cover shells 1-7, plus `Shells1and2_AreFilled_RaysSeeTheSame` (replaces `..._AreNotFilled`), and the prefilter check on shells 1-6.
  `EditOpRedrawTests` counts redrawn-on-arrival. `EditSummaryTests`' streamer test turns the fill off on both sides (it tests the redraw pipeline).
  The walking twin now models the fill (+45 ms) over 30 s and pins "not FULL, > half the band". KNOWN_ISSUES and CLAUDE.md are updated.
## Evidence
- Filter run (9 shell classes incl. Slow): 42/45 pass, then the remaining 3 fixed (2 tests assumed unfilled shells 1-2, 1 timeout). The walking twin passes after its update.
  Fast suite **PASS 796 FAIL 0 SKIP 14** (`Logs/overnight/93b2c-fast.out`).
- Visibility (`Logs/Shells/fill83_tests.txt`): 3,677,460 outside rays over 120 chunks (shells 1-7), 0 differ. Cave entrances shown before and after: shell 1 3/3, shell 2 3/3, 0 lost.
  Dense bricks after the fill: s1 61%, s2 52% of before. Sea/lake on shells 1-2: 0 reachable fluid voxels changed.
- Same build, AC, quiet, 120 s start-pose wait + 3 min walk, counts only. A2 = `-shellfillfrom 3`, B2/B3 = default:
  logs `Logs/tg2world_20261016_{144656,144009,145541}.txt` (copies in `Screenshots/93b2c/*/walk.txt`).
| | A2 fill from 3 | B2 / B3 fill from 1 |
|---|---|---|
| start pose: s1 | FULL at 1,339 (4 s) | complete 2,170/2,170 at 52 / 53 s |
| start pose: s2 at walk start | FULL 612 | 1,990 / 1,963 of 2,001 |
| brick cap used at walk start s1 / s2 | 100% / 100% | 68% / 81-82% |
| FULL refusals s1 / s2 (launch) | 14 / 13 | 0 / 0 |
| walk's end held s1 / s2 / s3 / s4 | 948 / 322 / 644 / 142 | 766-769 / 135-147 / 416-467 / 6-40 |
| shell-2 chunks generated DURING the walk | 524 | 56 / 0 |
| worker ms per s1 chunk: column+voxel / fill | 17+8 / 0 (start), 39+23 / 0 (launch) | 12+7..8 / 24-25 (start), 25-26+15-19 / 44-54 (launch) |
- Extra worker time from the fill: shell 1 +24-25 ms per chunk (start pose, cool), +44-54 ms (whole launch); shell 2 +27-29 ms.
  Editor split, shell 1: generator 91, fill without mouths 107, with mouths 148 ms (`Logs/Shells/fill83_prefilter.txt`).
- Why the walk got worse: shell 1 no longer goes FULL, so its cap stops rationing it. Q47 (a) gives it the workers first; it takes ~80% of the 3 workers (28 chunks/s), and shell 2 starves.
  Shell 2 does not stop at its cap (81% at 99% of its band), so it is not short of pool, as the owner asked.
- Peak footprint 2,672 (A2) vs 2,701 / 2,696 MB (B2/B3). No pool change.
- Discarded runs (in `Screenshots/93b2c/other_runs/`): B0 (owner paused 666 s, 5 min walk), B (Mac busy mid-run: 180-207 ms/chunk), A (on battery but at normal speed; matches A2).
- B2's end shot: the view is blocked at the eye by tier-0 material (0 far rays) at the same eye where A2 and B are clear. Fluid is live in this probe; the shells do not write tier 0.
  B3 is clear, so the pair below uses B3. A tier-0 `StreamManager.WorkerLoop` ObjectDisposedException at quit, B2 only: an old quit race, not shells.
## Gameplay consequence (surface it)
Shells only draw caves that open to the surface, and that now starts at shell 1: ~69 m on High (was 275 m). Once shells are the default (93c),
a player in a cave looking down a passage longer than ~69 m sees it end in rock. Shells are opt-in today, so play is unchanged.
## Next
Owner answers the question above. 93b3 does not depend on the fill result, but the runner holds it behind this STOP. Release it if (a)/(c)/(d).

## For the doc
Task 93b2 part two: task 83's cave fill now also runs on shells 1-2. Their brick caps no longer stop them: standing still, both fill their whole band.
Walking got worse for shells 2-3, because the fill costs worker time and shell 1 is no longer held back by its cap. Owner question.
| 3 min 5 m/s walk, 3 shell workers | before (fill from 3) | after (fill from 1) |
|---|---|---|
| start pose: shell 1 held | 1,339 FULL | 2,170 / 2,170 in 52 s |
| start pose: shell 2 held | 612 FULL | ~1,975 / 2,001 |
| FULL refusals s1 / s2 | 14 / 13 | 0 / 0 |
| walk's end held s1 / s2 / s3 | 948 / 322 / 644 | ~768 / ~141 / ~440 |
| fill worker time per shell-1 chunk | 0 | +25 ms (cool) to +54 ms |
Logs: `Screenshots/93b2c/{before_fill3,after_fill1_B3}/walk.txt`.
Images:
- `Screenshots/93b2c/walk_end_before_after.jpg`: left column = before (fill from 3), right = after (fill from 1); top row = the walk's end view, bottom row = shell tint
  (green = shell 1, blue = tier 0, grey = far band). The picture is the same either way (near mountain, hill, far plain), with no holes. The green shell-1 strip on the valley floor
  left of the hill is a little narrower after, and the hill and plain are grey (far band) in both: shells 2-3 had no time to fill there while walking. Golden hour, because the walk mode pins the clock.
