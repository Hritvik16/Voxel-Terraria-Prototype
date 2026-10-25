# 149-rig-retire-oneoffs: DONE -- 31 of 32 "retire" rigs moved to tools/retired-rigs/; 1 left behind; fast suite green

## What changed (commit f3d5c0c, branch engine-phase)
- `git mv` (rename only, contents unchanged) of 31 scripts from the repo root into `tools/retired-rigs/`:
  3a: 5b-check, phase6-sandbox, fluid-activity · 3b: perf60, fluid-benchmark, fluid-ab, fluid-scale, fluid-chaos, phase6-qa ·
  3c (all 19): fixes, fluid-stagger, fluid-tilecap, fluidtick-diag, flyfast, lavalight-ab, lodpop, looksweep, overhaul-batch,
  phase7-rung1, roofskip-ab, shadow-dots, torchshadow-shots, upscale-ab, upscaler-smoke/compare/clips/realrate, playtest-bugs ·
  3d: demo, phase7-demo, phase5b-demo.
- `tools/retired-rigs/README.md`: one row per script (why retired, last report date from RIG_REFRESH_PLAN section 3), and how
  to run/revive one (`tools/with-unity-lock.sh tools/retired-rigs/run-x.sh` from the repo root; `git mv` back to revive).
- `RIG_REFRESH_PLAN.md` R1 row marked DONE; `TASKS.md` "Task 149" paragraph. CLAUDE.md untouched (it cites none of them).
- Keep / update / merge scripts not touched. Root now has 23 `run-*.sh` (54 - 31).

## Left behind (1)
- `run-playground.sh` -- `tools/capture-gpu-trace.sh:76` prints `./run-playground.sh` as the rebuild command when the trace
  build is stale. Retire it together with a fix to that message (needs knowing which build the trace tool really wants:
  it checks `CommandLineBuild.BuildPlaygroundTraceStandalone`'s app, which run-playground.sh may not build).

## Reference check (how "nothing calls it" was established)
- `grep -w <name>` over the 22 non-retired `run-*.sh`, CLAUDE.md, and `*.sh`/`*.py` under `tools/` and `Assets/` (excluding
  `tools/overnight/done|blocked|later`). Hits besides run-playground: `run-play.sh:33` comment "like run-demo.sh" (not a call,
  left as is); `tools/report-flyfast.py` docstring names run-flyfast.sh (it is that script's reporter, not a caller; left in
  tools/, noted in README).
- KNOWN_ISSUES.md mentions perf60/flyfast/lavalight-ab/looksweep/overhaul-batch/roofskip-ab/upscale-ab/shadow-dots only as
  past evidence (e.g. the task 16 pause proof), not as commands to run -- not blocking. Completion docs likewise history.
- None of the moved scripts uses `dirname "$0"`/`BASH_SOURCE`; all assume cwd = repo root, so they still run from the new path.

## Evidence
- Fast suite: `./run-editmode-tests.sh` PASS 946 FAIL 0 SKIP 15, wall 6m39s (`Logs/overnight/149-editmode.out`).
- No player launch, no screenshots, no measurement (housekeeping task).

## Next
- R3 (`run-gameplay-stack.sh`, needs owner Q4) then retire 5a/5c/6-player/6-ccd/6-edit/late-game-siege; farband merge into
  tg2world. A small follow-up could fix capture-gpu-trace.sh's rebuild hint and then retire run-playground.sh (not queued:
  needs a look at which build it should name).

## For the doc
- Root `run-*.sh`: 54 -> 23 (31 moved to `tools/retired-rigs/`, 0 deleted); plan said 32, 1 kept back (run-playground.sh,
  still cited by tools/capture-gpu-trace.sh).
- Fast suite PASS 946 FAIL 0 SKIP 15 before and after (`Logs/overnight/149-editmode.out`).
- No images: nothing visible changed (file moves only).
