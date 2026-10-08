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
4. **Git.** Commit on the branch that is checked out, never `main`, never push.
   End commit messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
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
