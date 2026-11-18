# Unattended task session -- read this first

You are running UNATTENDED in a fresh headless session started by
`tools/overnight/run-queue.sh`. Nobody can answer questions until morning.

1. **Context.** `CLAUDE.md` is loaded. Read `tools/overnight/HANDOFF.md` (the
   previous task's report) and, before touching streaming, rendering, fluids,
   memory or a rig, the matching entry in `KNOWN_ISSUES.md`. Use grep and
   targeted reads -- never read large files whole.
2. **Scope.** Do exactly the task below: one change. Do not start
   the next queued task or anything not asked. Do not change frozen APIs,
   engine contracts or memory layout unless the task says so.
   **Follow-ups (owner, 2026-10-02):** if your result names a low-risk follow-up
   that needs no owner decision (a QA pose, an evidence run, a doc fix), you may
   add ONE new task file for it at the END of `tools/overnight/queue/` (next free
   number; a `Requires:` line if it depends on another task). **Not a measure-only
   task** (owner, 2026-10-08): no probes, evidence runs or re-measures as follow-ups;
   name them in HANDOFF instead. No new design work
   and no performance-changing code without the owner's approval written in a
   task file.
3. **Rules that still apply:** the shared Unity lock (`tools/unity-lock.sh` /
   `tools/with-unity-lock.sh`), EditMode suite before committing (the fast
   suite, `./run-editmode-tests.sh`, ~3 min; `SUITE=all` when the task changes
   terrain-generator code -- every B step -- or prepares a merge to `main`), one
   performance change per measurement, look at screenshots before concluding.
   **Test trim (owner, 2026-10-12, (a)):** while iterating, run only your own
   tests (a `-testFilter` run); run the required suite (fast or `SUITE=all`)
   ONCE, at the end, before the final commit -- again only if that run fails
   and you fix something. Take ALL demo/screenshot shots in ONE player launch;
   relaunch only for a shot that came out broken.
   **Launch budget (owner, 2026-10-12: "what is the point of running over 5 mins if we are not
   playtesting?"):** a screenshot/metrics launch takes the FEW poses the task needs (3-5, not all 16)
   and lasts <= ~10 min including load. Never wait for streaming to finish (e.g. `-shellwait 360`): use
   a short fixed wait (<= 60-90 s) and REPORT what is still missing (the `[shells]` missing counts)
   instead. Task 83b's launch waited 360 s at 16 poses = 95 min, and 11 poses were still unfilled.
   Longer only in a performance/streaming task, or when the task file says why.
   **Images are NOT committed** (owner, 2026-10-13): `Screenshots/**` images/videos are git-ignored; never `git add -f`
   them. Commit text reports (txt/json/md) only. Cite image paths in HANDOFF; the supervisor archives them.
4. **Git.** Commit on the branch that is checked out, never `main`, never push.
   End commit messages with `Co-Authored-By: Claude <the model you run on, e.g. Opus 5.5 / Sonnet 5.5> <noreply@anthropic.com>`.
   Your task's feature note (flags, tests, demo shots) goes in `TASKS.md` as one paragraph, NOT in CLAUDE.md
   (owner, 2026-10-22: CLAUDE.md is re-read on every turn of every session).
5. **NEVER end your turn while something is still running.** This is a `-p`
   session: the moment you stop and wait for a notification, the session EXITS
   and kills every background job (2026-10-01: both queued tasks died this way
   and were wrongly logged as done). Do not use `run_in_background` or Monitor
   and then wait. Run a command in the foreground if it fits in the 10-minute
   Bash timeout (pass `timeout: 600000`). For anything longer, start it detached
   (`nohup ./run-x.sh > Logs/overnight/x.out 2>&1 &`), then poll it in
   FOREGROUND calls of under 10 minutes each, e.g.
   `until grep -qE 'DONE|FAIL|error' Logs/overnight/x.out || [ $SECONDS -ge 540 ]; do sleep 20; done; tail -5 Logs/overnight/x.out`,
   repeating until it has finished. Your final message comes only after step 8.
6. **When the task needs the owner** (a design decision, a failed acceptance
   check you can't fix inside the task, anything destructive), stop: commit what
   is safe, and put `STOP_FOR_OWNER` plus the question at the TOP of
   HANDOFF.md. The runner sets this task aside in `tools/overnight/blocked/`
   (uncommitted edits are saved as a patch there and reverted) and goes on with
   tasks that do not `Require:` it.
7. **Usage is scarce.** No claude.ai docs or artifacts. Screenshots only where
   the task asks. Short tool outputs (grep/head/tail). No subagents unless the
   task says so.
8. **Finish by OVERWRITING `tools/overnight/HANDOFF.md`** (under 60 lines):
   task name and result in one line; what changed (files, commit hashes);
   evidence (test counts, measured numbers with log paths, screenshots looked
   at and what they showed); anything left unfinished; the next step. If the
   owner must decide something, start the file with `STOP_FOR_OWNER`.
   **Every image you cite gets a one-line caption saying what to look at**
   (which panel/row, which part of the frame, what it shows) -- owner, 2026-10-03.
   **End HANDOFF with a `## For the doc` block** (owner, 2026-10-09): the 3-6 numbers that show
   what this task changed (before -> after, with units and the log/report path), and 1-3 image paths
   with captions (a before/after pair or contact sheet if anything visible changed). The supervisor
   copies it into the owner's doc as this task's report. A task with nothing to show says why.
9. **Performance is quoted at WARM STEADY STATE** (owner, 2026-10-04, Q20 (a)). This Mac is
   a fanless M1 Air: a warm chip runs chunk generation up to ~2.2x slower (tasks 27/28), and
   4+ workers heat it within ~100 s even from cold. So every timed measurement (stream probe,
   acceptance rig, frame-time A/B) starts with ONE warm-up launch whose numbers are discarded
   (`MODE=stream MOTION=walk ./run-tg2world.sh`, ~6 min), then runs the measured arms back to
   back (no idle gap over ~2 min; interleave A and B, e.g. ABBA). Report "warm steady state"
   and the warm-up's log path. Cold numbers may be shown beside them, never as the result.
10. **Measure performance ONLY in a performance task** (owner, 2026-10-08). A task whose
   purpose is a feature, terrain, visuals or a bug fix proves itself with tests, metrics
   reports and screenshots -- no stream probe, acceptance rig or frame-time arms, even if
   its file asks for a "re-measure" (older task files do). Performance is checked at
   milestones by a dedicated task. Rule 9 applies only when a timed measurement runs.
11. **Efficient by construction** (owner, 2026-11-14: "We cant keep leaving everything up to the performance stage. Every
   task has to make sure it is doing stuff in a way that prevents further work ... straight away building the most memory and
   performance efficient way of doing things"). Every task that adds or changes content, data or drawing:
   (a) chooses the most memory- and performance-efficient design it can find FIRST (shared / deduplicated data, nothing
   stored that is never drawn, heavy builds on worker threads, nothing per frame that can be done once) and says in HANDOFF
   why that design is the efficient one;
   (b) reports its memory cost from the launches it already does, against its switch OFF: memory peak, GPU body pool peak and
   refusals, CPU pool peak and valve evictions (ledger lines; no extra launch);
   (c) must not make any of those worse beyond its task file's budget (default: refusals not up, memory peak at most +50 MB).
   Over budget = fix it inside the task or STOP_FOR_OWNER. "Leave it for the performance stage" is not an option.
   This is not frame timing: rule 10 still forbids frame-time arms outside performance tasks.
12. **Keep the budget: cost proxies** (owner, 2026-11-16, task 220; full plan `PERF_BUDGET_GUARD.md`, numbers `PERF_BUDGET.json`).
   Every task that adds or changes drawing or content reports in HANDOFF, from the launches it already does (no extra launch,
   no frame-time arm; rule 10 stands): P1 memory (rule 11); P2 a "pass card" for each GPU pass added or changed (resolution:
   render rows / output 1080p / fixed; every frame or 1 in K; rays, march step cap, texture taps per pixel; GPU bytes added;
   the AMENDMENT_8_17 step it belongs to and the share it takes); P3 `step <name>: +a ms (cold capture, est.) vs share b ms`
   ONLY if its launch already prints frame time at a pose (`OVER SHARE` when a > b); P4 for content, instance / tree / body
   counts at its poses vs OFF (limit +10 % unless density is the task); P5 when a ray march changes, the rays/steps-per-frame
   counter lines vs OFF (flag at +25 %). A new pass gets its own switch AND a capture-only `-costarms <name>_off` arm.
   Without approval in the task file: no new pass at OUTPUT resolution, no new per-pixel ray march. Write `HEAVY` in HANDOFF
   if the task adds either, prints `OVER SHARE`, or crosses a P1/P4 limit: the supervisor then runs the budget check next.
13. **Budget check counter** (owner Q-AO (b), N = 5, task 230): the supervisor adds 1 to `tools/overnight/BUDGET_COUNT` (line 1)
   for each drawing/content task it marks done, and queues `tools/overnight/run-budget-check.sh` next when it reaches 5, at once
   after a `HEAVY` task, and before any merge to `main`; an accepted check (PASS or `--rebaseline`) sets it to 0, WARN to 2.
