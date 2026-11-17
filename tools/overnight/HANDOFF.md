STOP_FOR_OWNER -- pick how often the ~35 min budget check runs: (b) every 5 drawing/content tasks + at once after a HEAVY task
+ before merges (RECOMMENDED), (a) after every drawing task, or (c) only before merges to `main`. Detail: `PERF_BUDGET_GUARD.md` sec. 1.
Also confirm: the check measures play's own look (High, 432 rows, play flags), not a "floor preset" -- 219 showed presets don't change time.
NOTE for the supervisor: `tools/overnight/PAUSE` from 224's memory watchdog may still be there; clear it when the Mac is fit.

# 220-plan-keep-the-budget: DONE (plan) -- guard designed, budget file + PREAMBLE rule 12 written; script waits for the owner's pick
## What changed (commit f41c22b; no engine code, no launch, no Unity run)
- `PERF_BUDGET_GUARD.md`: the plan -- options table (sec. 1), budget file (2), milestone check design (3), per-task proxies P1-P5 (4),
  how a look task that adds a pass is costed (5), what the supervisor does on a failure (6), the supervisor's `Cost:` queue-rule text (7),
  order with 225-229 (8).
- `PERF_BUDGET.json`: PROVISIONAL budget. Settings = play's look (High, 432 rows, FSR 3, `tools/play-flags.sh`). Targets (33.3 ms,
  <= 5 % > 33 ms, 3,000 MB) are reported as distance only; the baseline is a RATCHET (fail = worse than last accepted by more than
  the tolerance). Hard limits: peak > 3,000 MB, refusals at walk_near > 0. Steps with OFF arms and 8.17 shares: sky fill 2.5 ms,
  sun 5.0, torch flood 1.0 (night), shells 4.0, ceiling.
- `tools/overnight/PREAMBLE.md` rule 12 (in force now, the same under every option): P1 memory; P2 pass card (resolution, 1 in K,
  rays/steps/taps per pixel, GPU bytes, step + share); P3 cold ms only if the launch already prints it (8.17's rule); P4 content
  counts (+10 %); P5 ray-work counters (+25 % flag). New pass = own switch + `-costarms <name>_off`. No output-res pass or new ray
  march without approval. `HEAVY` marker in HANDOFF = check runs next.
- TASKS.md "Task 220". SUPERVISOR.md NOT edited (not in my allowed files): paste guard sec. 7 into "Writing a task file".
## Why this design (evidence)
- Ratchet, not target: every arm is ~3x over 33 ms today (219: walk_near 108-111 ms); a check failing on the target always fails.
- Tolerances from measured noise: 212's 12 F passes at walk_near 104.3-108.2 ms (`Logs/costsplit_2026-11-15_143504.txt`,
  `_160531.txt`), lighting deltas repeat within 2-3 ms, memory peak ±50 MB (224). -> p50 max(5 ms, 5 %), step max(3 ms, 15 %).
- Lighting track (Q-AN): the check's walk_near (day) arms skyfill0 / sun_off / flood_off / shells_off / ceiling, plus the night torch
  path (task 151, 32 torches) FULL + flood_off -- after 229 the day flood cost should be ~0 and the night one is what matters.
- No per-task timing (rule 10): proxies are memory, a static pass card, counts and ray counters. P5 is a flag only: task 128 showed
  shell-march iterations do not track ms. P3 is 8.17's existing look-task rule, used only where the launch already shows frame time.
- Bisect by switches first (rule 11 gives every feature one): ONE launch with <= 5 OFF arms names the culprit, no rebuilds; that is
  why N = 5 (10 suspects no longer fit one launch at 9 fps).
- Baseline numbers (provisional): walk_near p50 107.0 / p95 204 ms, sky fill 40.4, sun 28.1, flood 27.8, shells 8.4, ceiling 24.6
  (212); forest_eye 148.4 ms (219 High); peak 2,607 MB, refusals 0 at walk_near / 555 after forest+jungle (219 High,
  `Logs/overnight/219/costsplit_high_432.txt`). Torch path not yet measured at play flags: the first check run fills it.
## Noticed, not changed
- Task 226's file says "Do NOT time frames"; AMENDMENT_8_17 sec. 3 asks look tasks for a cold ON/OFF estimate. Rule 12 resolves it:
  P3 only where the launch already prints frame time, otherwise the P2 pass card. 226 bloom at output res would be HEAVY.
## Next step
- Owner picks (a)/(b)/(c). Then ONE task: write `run-budget-check.sh` (sec. 3) + `--rebaseline`, run it once to replace the provisional
  baseline, ideally BEFORE 227 so 227-229's wins land on the same yardstick. Supervisor: add sec. 7 to SUPERVISOR.md, start
  `tools/overnight/budget_count`.
- Tests: none run -- no code changed (docs, JSON and the preamble only). JSON validated (`python3 -I -m json.tool`).

## For the doc
- The question: how often to run a ~35 min "did we get slower?" check. Recommended: every 5 drawing tasks, at once after a heavy one,
  and before every merge (~7 min a task on average). Strict = after every drawing task (~35 min each, +20-35 % queue time).
  Merge-only = cheapest, but a slowdown can hide under dozens of tasks (last merge was ~100 tasks ago).
- The check compares against the LAST accepted numbers, not the 30 fps goal (we are ~3x over it today): walk_near 107 ms;
  sky fill 40.4, sun shadows 28.1, torch flood 27.8, shells 8.4 ms; memory peak 2,607 MB of 3,000 (`PERF_BUDGET.json`).
- It fails when walk_near gets > 5 ms (5 %) slower, a lighting step > 3 ms (15 %) dearer, memory +50 MB or over 3,000 MB -- limits
  set from the measured run-to-run noise (task 212: 104-108 ms across 12 repeats).
- Every drawing task now (rule 12) lists each new pass's resolution and per-pixel work, its memory, and its object counts, without
  timing. A new pass at full output resolution or a new ray march needs your OK and triggers the check at once.
- When it fails: no new look/content tasks until one switch-off launch finds the culprit; it is fixed or brought to you with pictures.
- No images: plan only, nothing visible changed.
