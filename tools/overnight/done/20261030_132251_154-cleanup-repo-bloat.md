# 154-cleanup-repo-bloat

Model: opus

Approval: owner, 2026-10-25 (chat): "go ahead and do clean up to remove the bloat".
A CLEANUP task. No engine, shader, test-logic or rig-behaviour change. No Unity launch except the fast EditMode suite once at the end.
Goal: less text that every session re-reads, fewer stale files to wade through. MOVE, never lose: everything removed from a
live file lands in an archive file or `git mv`'d folder, so `git log`/grep still finds it.

## Do (one commit per step, so any step can be reverted alone)
1. **CLAUDE.md diet (biggest win: it is re-read on every turn of every session, 30.8 KB).** Keep every RULE section word for word
   (Ground truth, Hard invariants, Measurement discipline, Working style, Session economy, ONE UNITY PROCESS, Commands, RIG
   SELECTION RULE, build-run-review loop, landmines). Move out to `PROJECT_HISTORY.md` (new, newest first): the dated history
   paragraphs in "Current state" (2026-10-01 "Where the project is", the Phase 4 paragraph, older merge notes); keep a 5-8 line
   "Current state" that says where we are today (engine-phase merged 22 Oct, quality pass, ROADMAP.md, latest suite count).
   Shorten the "Known open issues -- INDEX" to ONE short line per entry (<= ~150 chars, status + task number); the full text
   already lives in `KNOWN_ISSUES.md` -- first check each long index entry's detail IS there, and append it to the matching
   KNOWN_ISSUES entry if not. Target: CLAUDE.md <= ~18 KB. Report before/after bytes.
2. **Root docs.** `git mv` finished/one-off reports into `docs/archive/` (create it): e.g. GEMINI_HANDOFF, GEMINI_TASK,
   HANDOFF_2026-09-24, MORNING_REPORT, FPS_INVESTIGATION_RESULTS, FLUID_*_RESULTS, FLUID_TICK_DIAGNOSIS, ARCHITECTURE_COMPARISON,
   UNITY_VERSION_RESEARCH_2026-09, PHASE_7_READING_NOTES, VISUAL_REVIEW_114, OPTIMIZATION_CANDIDATES (if superseded by
   PERF_30FPS_PLAN). NEVER move: ARCHITECTURE_v8.6, any AMENDMENT_*, PHASE_*_COMPLETION, DESIGN_LARGE_WORLD..., ROADMAP, CLAUDE,
   KNOWN_ISSUES, TASKS, SESSION_HANDOFF, PLAYGROUND_GUIDE, current plans (PERF_30FPS_PLAN, PERF_SHELL_MARCH_PLAN, SOFT_SKY_PLAN,
   RIG_REFRESH_PLAN, ENGINE_PHASE_PLAN, GAMEPLAY_PHASE_PLAN, GAME_DESIGN_BRIEF_SNAPSHOT, GAMEPLAY_DESIGN_ROUND_*). Before each
   move, `grep -rl '<NAME>' --include='*.md' --include='*.sh' --include='*.cs' .` and fix live references (CLAUDE.md, TASKS.md,
   KNOWN_ISSUES.md, scripts) to the new path. Add `docs/archive/README.md`: one line per file (what it was, why archived).
3. **Rigs.** Retire `run-playground.sh` to `tools/retired-rigs/` (RIG_REFRESH_PLAN R1 left it only because
   `tools/capture-gpu-trace.sh` cites it): point capture-gpu-trace.sh's message at `run-play.sh` / `MODE=... run-tg2world.sh`
   (whichever launches the same scene -- read both), README row. Grep CLAUDE.md/TASKS.md/KNOWN_ISSUES for other root `run-*.sh`
   that RIG_REFRESH_PLAN marked retire/merge but are still at the root; list them in HANDOFF, do not retire beyond the plan.
4. **Queue folders.** `tools/overnight/blocked/`: for each item, check `git log --oneline | grep '<task number>'` and the doc
   decisions in SESSION_HANDOFF/TASKS.md. Answered or superseded (87 by amendment 8.15; 93b-93b5 by tasks 99/116/132/134;
   111 answered 22 Oct; 140/142/144 done as 145/147/148-149; 93e fixed; 37e lake fixes merged) -> `git mv` (or plain mv if
   untracked) to `tools/overnight/archive_work/blocked-<date>/`. `later/`: same for items already done or replaced (e.g. 89, 90,
   93b, 102 vs the shell-march tasks; 78 vs RIG_REFRESH_PLAN); keep real future work (gameplay steps, crafting, 37c, 40, 106, 79
   unless TASKS.md shows it done). List every move with its one-line reason in HANDOFF.
5. **Untracked junk.** Add to `.gitignore`: `tools/__pycache__/`, `__pycache__/`, `TestResults*.xml`, `*.pyc`. Delete
   `tools/__pycache__/`. Do NOT touch `Screenshots/` (3.3 GB, git-ignored; the supervisor archives images), `Logs/`, the three
   modified `Assets/Scenes/*.unity` files (owner/editor state), or `Assets/ThirdParty/**`.

## Rules
- Do not edit `tools/overnight/run-queue.sh`, `PREAMBLE.md`, or any `run-*.sh` you are not retiring (rule: never edit a running
  script; the queue runner is running you).
- If a step would delete information that exists nowhere else, STOP that step and list it in HANDOFF instead.
- Fast suite once at the end (`./run-editmode-tests.sh`); it must stay `PASS 946 FAIL 0` (or the current count).

## Deliverable
HANDOFF `## For the doc`: CLAUDE.md bytes before -> after; root .md count before -> after; files moved per folder; anything
skipped and why. TASKS.md paragraph.
