# Overnight handoff

**12-rig-power-guard: DONE.** This changes tooling only. No engine, shader or performance code changed. Commit `7f59fbc` on design-brief.

## What changed
- **`tools/power-status.sh` (new).** It prints one line: `power <label>: HH:MM:SS source=<AC Power|Battery Power> charge=<n%; state>`. It exits 0 on AC and 2 otherwise.
- **`run-tg2world.sh`:**
  - Runs `caffeinate -dimsu -w $$ &`. The player is started with `open -n`, so caffeinate waits on the script's own pid, which outlives the player.
  - Logs `power start` and `power end` into the run log.
  - For `MODE=walk|orbit` on battery, prints `WARNING: ON BATTERY ...` at start and end. The run still goes ahead.
- **`run-acceptance-rig.sh`:** gets the same caffeinate wrapper, plus `power start` before the build and `power end` before the report. It has no warning; the task asked for one only for walk/orbit.
- **`tools/overnight/start.sh`:** the tmux command is now `IDLE_MIN=.. caffeinate -ims nice -n 10 tools/overnight/run-queue.sh`. It takes effect at the next `start.sh`.
- **`run-queue.sh` is NOT edited**, because it was running. **Supervisor:** apply `tools/overnight/run-queue-power.patch` between runs. It is one line: `say "$(tools/power-status.sh "before $name")"` after `name=` in the task loop. Apply it with `patch -p0 < tools/overnight/run-queue-power.patch` from the repo root, or add the line by hand.
- **`KNOWN_ISSUES.md`:** added a GUARD note to the TAA/checkerboard entry's battery paragraph.

## Evidence
- **Walk run:** `MODE=walk MINUTES=1 ./run-tg2world.sh`, log `Logs/tg2world_20261003_101235.txt`, stdout in `Logs/overnight/12-walk.out`. It showed:
  - `caffeinate pid 8910 (-dimsu -w 8570)`
  - `power start: 10:13:21 source=AC Power charge=100%; charged`
  - `power end: 10:15:23 source=AC Power charge=100%; charged`
  - It reached TG2WORLD_DONE. Caffeinate pid 8910 had exited after the script ended, so nothing lingers.
- **The run was on AC, so no WARNING line printed.** I checked the warning branch separately: I extracted the start block from the script and ran it against a stub `power-status.sh` that exits 2. `MODE=walk` and `MODE=orbit` printed the WARNING line; `MODE=poses` did not.
- **EditMode fast suite:** `PASS 605 FAIL 0 SKIP 12`, wall 3m04s.
- I quote no frame-time numbers; this was not a performance run. The walk run's peak footprint was 3016 MB. That is the known over-the-cap issue (KNOWN_ISSUES), and nothing here changed it.
- I looked at no screenshots; the task did not need them.

## Left unfinished / notes
- The runner's per-task power line waits on the supervisor applying the patch above.
- Caffeinate cannot prevent battery-critical hibernation (the 2026-10-02 21:44 event was Low Power Sleep at 1%). Only AC prevents that.
- An earlier attempt at this task had already written these edits, uncommitted. I reviewed them, ran the acceptance checks and committed them as they were.

## Next step
The next queued task. The supervisor should apply `run-queue-power.patch` and restart the runner via `start.sh` at the next quiet point, so the caffeinate wrapper is live.
