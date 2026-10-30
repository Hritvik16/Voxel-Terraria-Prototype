# 154-cleanup-repo-bloat: DONE -- CLAUDE.md 30.8 -> 24.6 KB, root .md 54 -> 41, 1 rig retired, 30 queue files archived; nothing deleted except tools/__pycache__/

**For the owner (no decision needed):** CLAUDE.md is 20% smaller. The ~18 KB target was not reachable: the rule sections the task
said to keep word for word already add up to ~20 KB (Commands 4.7, Measurement 3.3, Ground truth 2.6, build-run-review 2.5 KB).
Getting under 18 KB means trimming rule text, and that needs your go-ahead.

## What changed (branch engine-phase; one commit per step)
- `a86dfd1` step 1: the dated "Current state" paragraphs moved word for word to `PROJECT_HISTORY.md` (new, newest first). The new
  Current state is 13 lines: ROADMAP order, the 22 Oct merge, the quality pass, the 30 fps miss, suite 948/0/15, pointers.
  The index is 40 one-line entries (<= 150 chars each; added the owner-pause and shadow-dots entries KNOWN_ISSUES already had).
  The 10 long index lines are kept word for word in a new last section of KNOWN_ISSUES.md, so no text was lost.
- `d4292df` step 2: `git mv` to `docs/archive/` (+README, one row each): GEMINI_HANDOFF, GEMINI_TASK, HANDOFF_2026-09-24,
  MORNING_REPORT, FPS_INVESTIGATION_RESULTS, FLUID_PERFORMANCE_AB_RESULTS, FLUID_SCALE_ARCHITECTURE_RESULTS, FLUID_TILE_CAP_RESULTS,
  FLUID_TICK_DIAGNOSIS, ARCHITECTURE_COMPARISON, UNITY_VERSION_RESEARCH_2026-09, PHASE_7_READING_NOTES, VISUAL_REVIEW_114,
  OPTIMIZATION_CANDIDATES (4 Sept proposals, replaced by PERF_30FPS_PLAN). Paths were updated in TASKS.md, KNOWN_ISSUES.md,
  PLAYGROUND_GUIDE.md and ENGINE_PHASE_PLAN.md. Amendments, completion docs and `.cs` comments still name the bare filename, which grep
  still finds. I left them alone because they are spec/history files and code.
- `92f6675` step 3: `run-playground.sh` -> `tools/retired-rigs/` (README row; replaces its "Not moved" note). `tools/capture-gpu-trace.sh`:
  its stale-build hint now says to regenerate the scene with `BUILD=1 ./run-play.sh` and rebuild with the BuildPlaygroundTraceStandalone
  command. The old hint was also wrong: run-playground.sh built `Playground.app`, not the `PlaygroundTrace.app` the tool checks.
  PLAYGROUND_GUIDE §2 and RIG_REFRESH_PLAN R1 row updated.
- `61354a4` step 4 -> `tools/overnight/archive_work/blocked-2026-10-30/` (22 files, all of blocked/):
  87 (superseded by amendment 8.15); 93b, 93b2-93b5 HANDOFFs (answered/superseded by tasks 99/116/132/134 and 93c); 93e (fixed `9d85dc6`);
  111 (plan answered 22 Oct, PERF_30FPS_PLAN); 140 (done as 145); 142 (done as 147); 144 (done as 148/149); 37e (lake fixes merged, `8b19f21`);
  153 patch + leaked test (resumed and committed as `da979b8`).
  -> `archive_work/later-2026-10-30/` (8 files): 70 and 71 (gameplay steps 7/8 = tasks 100/103); 78 (replaced by RIG_REFRESH_PLAN/144);
  89, 90 and 93b (done as 93b/93c); 102 (replaced by the shell-march tasks 132/134/152/153); `shell-column-tops-design-137.txt` (built as 153).
  Kept in later/: 106 (far raycast, not done), 107 (crafting), 37c, 40, 79. 37c and 40 were untracked and are now committed.
- step 5 `bc2f1ae`: `.gitignore` gains `__pycache__/`, `tools/__pycache__/`, `*.pyc`, `TestResults*.xml`; deleted `tools/__pycache__/` (3 .pyc).
- `2913123`: TASKS.md "Task 154" paragraph.
- Not touched: Screenshots/, Logs/, the 3 modified `Assets/Scenes/*.unity`, Assets/ThirdParty/**, run-queue.sh, PREAMBLE.md, and every kept run-*.sh.

## Evidence
- Fast suite `PASS 948 FAIL 0 SKIP 15` (`Logs/overnight/154_fast.out`), the same as task 153.
- `bash -n tools/capture-gpu-trace.sh` OK. `run-queue.sh` still `mkdir -p`s blocked/. No queued task `Requires:` an archived item
  (155 has no Requires).

## Left for later (not retired: RIG_REFRESH_PLAN marks them merge/update, not retire)
Still at the root: merge into the gameplay-stack rig: run-phase5a-rig, run-phase5c-rig, run-phase6-player, run-phase6-ccd, run-phase6-edit,
run-late-game-siege. Merge: run-farband (into run-tg2world) and run-cost-split (into the perf milestone). Update: run-phase5b-rig,
run-phase5d-rig, run-phase6-brushguard, run-phase6-combined. Owner Q3: run-phase7-lighting-ab.
Possible later candidates (not on the task's list, so not moved): PHASE_7_PLAN (cited by queued task 155), SVO_LIGHTING_NOTES,
PLAN_COARSE_FIRST_GEN, RIVERS_WATERFALLS_PLAN, DESIGN_NOTE_7_x, PHASE_6_QA_PASS, root `Codebase_Context.txt`.

## For the doc
- CLAUDE.md 30,781 -> 24,621 B (-20%; the rule sections alone are ~20 KB, so the 18 KB target needs rule edits = owner call).
- Root .md files 54 -> 41 (14 to `docs/archive/`, +1 `PROJECT_HISTORY.md`).
- Root run-*.sh 23 -> 22 (`run-playground.sh` -> `tools/retired-rigs/`, now 32 .sh there).
- Queue: blocked/ 22 -> 0 files, later/ 13 -> 5 (30 files to `tools/overnight/archive_work/*-2026-10-30/`).
- Fast suite PASS 948 FAIL 0 SKIP 15 before and after.
- No images: a docs/file cleanup has nothing visible to show.
