# Unattended task session -- read this first

You are running UNATTENDED in a fresh headless session started by
`tools/overnight/run-queue.sh`. Nobody can answer questions until morning.

1. **Context.** `CLAUDE.md` is loaded. Read `tools/overnight/HANDOFF.md` (the
   previous task's report) and, before touching streaming, rendering, fluids,
   memory or a rig, the matching entry in `KNOWN_ISSUES.md`. Use grep and
   targeted reads -- never read large files whole.
2. **Scope.** Do exactly the task below: one change, measured. Do not start
   the next queued task or anything not asked. Do not change frozen APIs,
   engine contracts or memory layout unless the task says so.
3. **Rules that still apply:** the shared Unity lock (`tools/unity-lock.sh` /
   `tools/with-unity-lock.sh`), EditMode suite before committing, one
   performance change per measurement, look at screenshots before concluding.
4. **Git.** Commit on the branch that is checked out, never `main`, never push.
   End commit messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
5. **When the task needs the owner** (a design decision, a failed acceptance
   check you can't fix inside the task, anything destructive), stop: commit what
   is safe, and put `STOP_FOR_OWNER` plus the question in HANDOFF.md. The queue
   stops there.
6. **Usage is scarce.** No claude.ai docs or artifacts. Screenshots only where
   the task asks. Short tool outputs (grep/head/tail). No subagents unless the
   task says so.
7. **Finish by OVERWRITING `tools/overnight/HANDOFF.md`** (under 60 lines):
   task name and result in one line; what changed (files, commit hashes);
   evidence (test counts, measured numbers with log paths, screenshots looked
   at and what they showed); anything left unfinished; the next step. If the
   owner must decide something, start the file with `STOP_FOR_OWNER`.
