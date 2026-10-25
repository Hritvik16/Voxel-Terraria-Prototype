# 148-rig-lock-wrappers: DONE -- 9 kept/updated rigs now take the shared Unity lock themselves; fast suite green

## What changed (commit 7ef2474, branch engine-phase)
- Three lines added right after `set -uo pipefail` in each script (a comment, `source tools/unity-lock.sh`,
  `unity_lock_acquire`); nothing else in them changed:
  - kept (RIG_REFRESH_PLAN 3a, R2's five): `run-editmode-tests.sh`, `run-phase7-rig.sh`, `run-acceptance-rig.sh`,
    `run-acceptance-fluid.sh`, `run-fluid-tiled.sh`
  - "update" verdict, also lockless: `run-phase5b-rig.sh`, `run-phase5d-rig.sh`, `run-phase6-brushguard.sh`,
    `run-phase6-combined.sh`
- None of the nine sets its own EXIT trap, so the lock's `trap unity_lock_release EXIT` does not clash with anything. All nine
  already assumed cwd = the repo root (`$(pwd)` as the project path), so the relative `source tools/unity-lock.sh` matches
  run-tg2world.sh / run-cost-split.sh.
- Wrapping with `tools/with-unity-lock.sh` (or the queue) still works: the inner acquire sees an ancestor holding the lock and
  returns at once.
- Left alone: the 20 lockless rigs the plan marks retire or merge (fluid-ab/activity/benchmark/chaos/scale/stagger/tilecap,
  late-game-siege, 5a, 5b-check, 5b-demo, 5c, 6-ccd/edit/player/qa/sandbox, phase7-rung1, playground, playtest-bugs). R1/R3 deal
  with them.
- Docs: TASKS.md "Task 148" paragraph; RIG_REFRESH_PLAN R2 row marked DONE; CLAUDE.md's lock paragraph corrected (it said
  editmode/phase7/acceptance do NOT take the lock -- no longer true).

## Evidence
- Before editing: `ps` showed none of the nine running and `/tmp/voxel-unity.lock` was absent.
- `bash -n`: OK on all nine.
- `./run-editmode-tests.sh` run UNWRAPPED: printed "lock is free; waiting for the Mac to be clear of Unity for 45 s", ran, and
  released the lock (file gone afterwards). `PASS 946 FAIL 0 SKIP 15 suite=fast wall=6m41s`, rc 0
  (`Logs/overnight/148-editmode.out`). The other eight were checked with `bash -n` only (they are long rigs, not run here).

## Left / next
- R1 (retire the 32, owner Q1) and R3 (gameplay-stack rig, Q4) are still open. Nothing needs the owner from this task.

## For the doc
- Kept/updated rigs without the Unity lock: 9 -> 0 (`run-*.sh` with no lock: 29 -> 20, all marked retire/merge).
- Fast EditMode suite, now taking the lock itself: PASS 946 FAIL 0 SKIP 15 (`Logs/overnight/148-editmode.out`).
- No images: a script-only change with nothing visible.
