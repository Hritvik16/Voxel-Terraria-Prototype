# PERF_BUDGET_GUARD -- keeping the frame budget while we build (task 220, plan only)

Owner, 2026-11-16: "once that question is answered we need a way to make sure we maintain it as we go about future tasks."
Owner answer to Q-AN the same day: keep the look, move toward Lay of the Land's look, make LIGHTING cheaper by design
(tasks 225-229). So the guard must track the lighting cost (sky fill, sun shadows, torch flood) at every check, and say how a
look task that adds a pass (polish, haze, bloom) is costed.

Status: IN FORCE. **Owner pick (Q-AO, 2026-11-16): option (b), milestone, N = 5** -- the check runs after every 5
drawing/content tasks, at once after any heavy task, and before every merge to `main`. `PERF_BUDGET.json` holds the accepted
baseline (first one: task 230's run). `tools/overnight/PREAMBLE.md` rule 12 (per-task proxies) and rule 13 (the counter
`tools/overnight/BUDGET_COUNT`) are in force. The check is `tools/overnight/run-budget-check.sh` (task 230, sec. 3).

## 1. The question for the owner (recommendation first)

How often does the full check (~35 min, one build + warm-up + one cost-split launch) run?

| option | when the check runs | cost | what a failure costs | verdict |
|---|---|---|---|---|
| **(b) Milestone (recommended)** | after every **5** tasks that touched drawing or content, **plus at once** after any "heavy" task (sec. 4), plus before every merge to `main` | ~35 min per 5 drawing tasks (~7 min a task on average) | culprit is one of <= 5; each has a switch, so ONE split launch with their OFF arms names it (~30 min); git bisect only if no switch shows it | catches drift within a few days; cheap |
| (a) Strict | after EVERY drawing/content task | ~35 min each; a look task takes ~1-3 h today, so +20-35 % queue time | none: the culprit is always the last task | safest, slowest; the queue does fewer features a night |
| (c) Merge only | before a merge to `main` only | ~35 min per merge (last merge 2026-10-22: ~100 tasks ago) | the regression can sit under dozens of features; bisect over many commits with rebuilds (~6 x 35 min) and causes can mix | cheapest while nothing breaks; very expensive when something does |

Why N = 5: a failure then has at most 5 suspects, which one launch with 5 OFF arms (+ F passes, ~30 min) can split.
With N = 10 the split no longer fits one launch (~10 arms x 2 + F = ~1 h at today's 9 fps) and two causes in one window
get likely. N = 3 costs ~12 min a task for little gain because the early trigger (sec. 4) already catches the big ones.

## 2. The budget file (`PERF_BUDGET.json`)

- **Settings**: play's look, High, 432 rows pinned, FSR 3, `tools/play-flags.sh`. 219 found the presets change memory, not
  time (Low 108.0 vs High 111.4 ms at walk_near), so the check measures what the owner plays, not a "floor preset".
- **Targets** (30 fps, <= 5 % of frames over 33 ms, 3,000 MB): REPORTED as distance, never a failure. Today every arm is ~3x
  over them (219); a check that always fails gets ignored.
- **Baseline = ratchet**: the last accepted numbers. The check FAILS when anything gets worse than the baseline by more than
  its tolerance. A performance task that cuts cost (227-229) re-baselines with the same script. A task that adds cost on
  purpose re-baselines only with the owner's OK written in the task file (`history` records who and when).
- **Hard limits** that fail regardless: process peak > 3,000 MB; GPU body refusals at walk_near > 0.
- **Tolerances** from real noise: 212's 12 F passes at walk_near spread 104.3-108.2 ms (~4 %); lighting deltas repeat within
  2-3 ms; 224's memory peak spread ~±50 MB. So: FULL p50 max(5 ms, 5 %), p95 max(10 ms, 8 %), each step's delta max(3 ms, 15 %),
  peak +50 MB, refusals max(20, 10 %). WARN at half.
- **Steps** (AMENDMENT_8_17 sec. 3's shares): sky fill 2.5 ms, sun shadows 5.0, torch flood 1.0 (night), shells 4.0, plus the
  "ceiling" (all three off). Each step names its `-costarms` OFF arm; new passes add a step (sec. 5).

## 3. The milestone check (`tools/overnight/run-budget-check.sh`, task 230)

One script, holds the Unity lock, ~35 min:
1. Build the release player (skip with `SKIP_BUILD=1`).
2. Warm-up launch, numbers discarded (`MODE=stream MOTION=walk ./run-tg2world.sh`, ~6 min), PREAMBLE rule 9.
3. One cost-split launch (`run-cost-split.sh` machinery, 212's order F, X X, F ...) at the budget file's settings:
   - **walk_near (day, noon)**: arms from `steps` -- `skyfill0`, `sun_off`, `flood_off`, `shells_off`, `ceiling` + new steps.
     This is the LIGHTING track: sky fill, sun and flood deltas each compared with their baseline AND their share.
   - **torch (night, 32 torches, task 151's path)**: FULL and `flood_off` only -- the torch flood's real cost with torches.
     After 229 the day flood delta should be ~0 and the night one is what counts.
   - **forest_eye**: FULL only, secondary (streaming deficit there: reported, not failed on).
   - Every pass prints its `[mem]` ledger line: peak MB, GPU pool peak/refusals, CPU pool peak/valve.
4. Input watch during the launch (212's `hididle`). If the owner used the Mac: rerun once; still dirty = `NOT CLEAN`, never
   `FAIL` (219's dirty repeat came out 22-35 % slow).
5. Compare with `PERF_BUDGET.json`; print ONE table (number, baseline, delta, tolerance, share or target, PASS/WARN/FAIL) and
   end with `BUDGET PASS`, `BUDGET WARN`, `BUDGET FAIL` or `BUDGET NOT CLEAN`; exit non-zero on FAIL.
6. `--rebaseline "<who approved>"` writes the run into `baseline` and appends to `history` (perf tasks; owner-approved adds).

It quotes frame time, so only the supervisor's check task or a performance task runs it (rule 10 unchanged).

As built (task 230); the differences from the plan above all come from the cost-split machinery (no capture code changed):
- **Three launches after the one warm-up, not one**: DAY (walk_near, every step's arm; `-costfixed forest_eye` only to drop
  walk_open, `-costtimedpaths walk_near`), FOREST (`-costarms none`: forest_eye FULL timed, secondary) and NIGHT (torch).
  `-costpaths torch` replaces the walk paths, and the machinery cannot time FULL alone at one path and arms at another.
  The first run timed every arm at forest_eye: 60 of the day launch's 73 min. The split is NOT yet run for real (owner pause).
- **Night arms are `tf_on` and `tl_none`**, not FULL / `flood_off`: the torch scene's FULL is the OLD point lights
  (`PlaygroundCapture.TorchCost.cs`), so night play = `tf_on` p50 and the torch flood step = `tf_on` - `tl_none` p50.
- **Input watch per path**: the watcher logs which cost-split path the live report is on; input during forest_eye
  (secondary) is reported, not counted. A dirty DAY or NIGHT launch runs again once; still dirty = NOT CLEAN.
- The comparison is `tools/overnight/budget_compare.py` (`--compare DAY NIGHT [FOREST]` reruns it on old logs). A step whose
  own noise (|FULL before - after|) exceeds its tolerance is downgraded FAIL -> WARN ("noisy"). `--rebaseline` is refused
  when the run is NOT CLEAN or a hard limit failed. A full run sets `tools/overnight/BUDGET_COUNT` (PASS / rebaseline 0,
  WARN 2). Output: `Logs/budget/<stamp>/check.txt`.
- **FIXED (task 236, 2026-11-17): the NIGHT launch's ~6.0 GB peak** (task 230's run 6,025 / 6,055 MB) was Metal staging memory from
  ~900 small `GraphicsBuffer.SetData` calls in the torch flood's synchronous rebuild (~4 MB each, held to the frame's end; play's fill
  did the same per frame, 3.4-3.6 GB). One SetData per buffer per upload (`-tfupload 1`, default): night peak 5,985 -> 2,538 MB,
  under the 3,000 MB hard limit; the day stays at ~2.57 GB. KNOWN_ISSUES has the entry. The next clean check can be accepted.

## 4. The per-task proxy (no timing; PREAMBLE rule 12, in force now)

Every task that adds or changes drawing or content reports, from the launches it already does:
- **P1 memory** (rule 11, unchanged): process peak, GPU body pool peak + refusals, CPU pool peak + valve, vs its switch OFF.
  Limit: refusals not up, peak <= +50 MB, never over 3,000 MB.
- **P2 pass card** (static, no launch): for each GPU pass added or changed: resolution (render rows / output 1080p / fixed
  size), runs every frame or 1 in K, per-pixel work (rays traced, march step cap, texture taps), GPU bytes added, its step
  and the share it takes. Default limits: no new pass at OUTPUT resolution, no new per-pixel ray march, no new full-screen
  pass over ~8 taps -- unless the task file approves it.
- **P3 rough ms**, only where the launch already prints frame time at a pose (AMENDMENT_8_17's look-task rule, cold, est.):
  `step <name>: +a ms (cold capture, est.) vs share b ms`, `OVER SHARE` when a > b. Never a launch for it.
- **P4 counts** for content: instances / trees / bodies at the task's poses vs OFF. Limit +10 % unless density is the task.
- **P5 ray-work counters** when a march changes (`[sun]`, `[shell]`, `[far]` rays and steps per frame at a still pose):
  reported; > +25 % = flag. Not a pass/fail: task 128 showed iterations do not track ms for the shell march.

**Heavy task = run the check at once** (do not wait for N): a pass at output resolution, a new per-pixel ray march, an
`OVER SHARE` line, or a P1/P4 limit crossed.

## 5. How a look task that adds a pass (polish, haze, bloom) is costed

1. Its own switch (rule 11) AND a cost-split arm `<name>_off` (capture-only code) so the milestone can split it.
2. It names its step: inside "Tier-0 march + far band + shading / post" (share 5.0 ms) or a new step whose share comes out of
   a named reserve (8.17 sec. 3). Its HANDOFF carries the P2 pass card and P3 if its launch shows it.
3. The supervisor adds the step to `PERF_BUDGET.json` (`steps`, with the off arm and share) when it marks the task done.
4. Counting: one task = one tick toward N, whatever the number of pieces; a pass at output resolution = heavy.
   Task 226's four pieces (colour variation, crevice shading, haze, bloom): one tick, four steps; bloom at output res = heavy.

## 6. What the supervisor does on a failure (never stack features)

1. `BUDGET FAIL`: no new drawing/content task starts until it is resolved. Performance and non-drawing tasks may run.
2. Queue ONE bisect task at the front: one cost-split launch with the OFF arm of every task since the last PASS (switches
   first, no rebuilds). Names the culprit and its ms. If no switch shows it (a change with no switch), `git bisect` over
   those commits with the same script.
3. The culprit's fix is a task on its own (efficient-by-construction redesign). If the cost is the look the owner wants, it
   goes to the owner as a question with the numbers and pictures; only the owner's OK re-baselines it.
4. `BUDGET WARN`: no stop; the next check runs after 3 drawing tasks, not 5. `NOT CLEAN`: rerun when the Mac is idle.

## 7. Supervisor's queue rule (text for SUPERVISOR.md "Writing a task file", the supervisor adds it)

> **Cost proxy line (task 220, PERF_BUDGET_GUARD.md):** a task file that adds or changes drawing or content carries a
> `Cost:` line: its step (8.17), the proxies it reports (P1 always; P2 for passes; P3 if its launch prints frame time;
> P4 for content; P5 for marches) and its limits (default: refusals not up, peak +50 MB, no output-res pass, no new
> ray march). Count drawing/content tasks done since the last accepted check in `tools/overnight/BUDGET_COUNT`; at N = 5 (the
> owner's option (b)) or after a heavy task, queue the check task next. Before a merge to `main`, the check passes.

## 8. Order with tasks 225-229

225 and 226 (look) follow rule 12 now (P1, P2, P3 where shown). 227-229 (perf) measure themselves; once the script exists,
each ends with `--rebaseline`. Suggested: write the script (one task) right after the pick, run it once to replace the
provisional baseline BEFORE 227, so 227-229's wins are recorded against the same yardstick.
