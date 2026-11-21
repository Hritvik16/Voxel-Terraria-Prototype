# Task 230 -- the milestone budget check: plan and outcomes (written BEFORE the run)

## What runs (tools/overnight/run-budget-check.sh)
1. Build (skipped here with SKIP_BUILD=1: `Builds/Playground.app` is 16:56 today, HEAD's code except the `-startsun`
   default that 233 flipped 16 -> 0 after the build; the budget flags pin `-startsun 0`, so the build runs HEAD's noon).
2. Warm-up `MODE=stream MOTION=walk ./run-tg2world.sh` (discarded).
3. DAY launch (run-cost-split.sh, play flags, High, 432 rows, -shellwait 60, -startsun 0 = noon): walk_near + forest_eye
   (-costfixed), arms from PERF_BUDGET.json steps (skyfill0, sun_off, flood_off, shells_off, ceiling), F X X F.
   The machinery cannot time FULL alone at one path and arms at another (-costtimedpaths makes the other path a still),
   so forest_eye runs the same arms; its numbers are secondary (reported, never failed on).
4. NIGHT launch (-costpaths torch, 28-32 torches, 0.50 h): the torch scene's FULL is the OLD point lights (flood off,
   TorchCost.cs), so the arms are tf_on (play's flood) and tl_none (no torch light). Night play frame = tf_on p50;
   torch flood step = tf_on - tl_none p50. (A separate launch because -costpaths torch replaces the walk paths.)
5. hididle input watch for both launches; a dirty launch reruns once; still dirty = BUDGET NOT CLEAN.
6. budget_compare.py: one table, BUDGET PASS|WARN|FAIL|NOT CLEAN, exit 1 on FAIL; --rebaseline "who".

## Possible outcomes and what each makes us do
- First run is a REBASELINE (owner Q-AO b, task 230): whatever the ratchet rows say, the run becomes the baseline. The
  comparison against the provisional (212/219) numbers is printed for information only.
- Hard limit crossed (process peak > 3,000 MB or GPU refusals at walk_near > 0): still rebaselined? NO -- a hard limit
  is not a ratchet. Report it; if it is real (not a capture artefact), STOP_FOR_OWNER with the number, do not write the
  baseline over a hard-limit failure.
- Input during a launch twice: NOT CLEAN, no rebaseline; HANDOFF says rerun when idle.
- Lighting steps vs shares: reported (OVER SHARE expected for sun, 233: 9-18 ms vs 5.0); no action in this task, that is S2.
- Failure-path test (baseline x 0.8 copy): must print BUDGET FAIL and exit non-zero; if not, the comparer is wrong: fix it.

## Outcome (after the run)
- Run `Logs/budget/2026-11-17_192530/` (check.txt, launch_*.out, costsplit_*.txt, idlewatch.txt), 19:25-22:11 = 165 min.
  Warm-up 4.5 min (`Logs/tg2world_20261117_192617.txt`), day 73 min DIRTY (owner input 19:33-20:26), day rerun 73 min,
  night 8 min DIRTY, night rerun 7 min DIRTY + owner pause (heavy.sh pause 22:04). Verdict `BUDGET NOT CLEAN`, rebaseline refused.
- Clean part: the day rerun's walk_near (timed 20:46-~20:56; no input 20:26-21:19; first input 21:19:43 during forest_eye).
- Hard limit: night process peak 6,025 / 6,055 MB (tf_on's synchronous torch flood Rebuild; 229's night 6,037; 233's night
  without tf_on 2,538). Day peak 2,568 MB. -> STOP_FOR_OWNER per the plan: no baseline over a hard-limit failure.
- Failure path: same-numbers copy -> BUDGET PASS exit 0; x0.8 copy -> BUDGET FAIL exit 1 (failtest_same/strict.txt).
- Changes after the run (not yet run for real): forest_eye moved to its own FULL-only launch (day was 13 min walk_near +
  60 min forest arms), per-path input rule, lock-release trap fix, BUDGET_COUNT hook.
