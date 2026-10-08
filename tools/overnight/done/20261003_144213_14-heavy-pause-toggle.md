# 14-heavy-pause-toggle

Owner, 2026-10-03: "even better than the day/night thing would be to easily toggle
for builds to be paused/unpaused. Ideally without completely stopping the agent that
is working currently ... I'd just want to toggle in the main agent that is overseeing
all the queues." Tooling only: no engine, shader or performance code.

## Goal
One command the supervisor (or the owner) runs to give the Mac back, and one to
take it again, while the task agent keeps running:

    tools/heavy.sh pause          # freeze running Unity/player/il2cpp, block new ones
    tools/heavy.sh pause --kill   # same, but terminate them to free the RAM too
    tools/heavy.sh resume         # unfreeze, unblock
    tools/heavy.sh status         # PAUSED/RUNNING, what is frozen, RSS, lock owner

## Do
1. **HOLD file** `/tmp/voxel-unity.hold`, next to the shared lock
   `/tmp/voxel-unity.lock`, so the sibling repo `../VoxelTerraria_compare` can honour
   it later. Write a one-line note for its owner in HANDOFF; do not edit the sibling repo.
2. **`tools/unity-lock.sh`**: `unity_lock_acquire` waits while HOLD exists, printing
   once `held by owner pause -- waiting`. The stale-lock rule (age > 7200 s) must
   never fire while HOLD exists or because of a pause; for example, touch the lock
   on resume. Reuse `unity_busy`'s process patterns to find heavy processes. Do not
   duplicate them.
3. **`tools/heavy.sh`**:
   - `pause` creates HOLD, then SIGSTOPs every heavy process: Unity editor/batchmode,
     bee/il2cpp build workers, and the built player (`Builds/*.app`). It records
     their pids in HOLD.
   - `--kill` terminates them instead (SIGTERM, then SIGKILL after 10 s).
   - `resume` SIGCONTs the recorded pids and removes HOLD.
   - Both append `pause <epoch>` / `resume <epoch>` lines to `Logs/heavy-pauses.log`.
   - Idempotent: pause twice or resume when not paused is harmless.
   - It never touches `claude` processes: the agent keeps working.
4. **Measurement integrity.** `run-tg2world.sh` and `run-acceptance-rig.sh` check
   `Logs/heavy-pauses.log` at the end of a run. If any pause overlapped the run,
   print `WARNING: PAUSED DURING RUN (<n> s) -- timing numbers from this run are
   invalid` into the run log and stdout, like task 12's battery warning. Never edit
   a run-*.sh while it is running.
5. **Runner**: do NOT block the runner or the task agent on HOLD; only Unity waits.
   The runner is not running now, so `tools/overnight/run-queue.sh` may be edited, but
   it should not need changes.

## Acceptance (run each, report the output)
- During a fast-suite `./run-editmode-tests.sh` (under the lock): `pause`, wait 60 s,
  check that `status` shows the frozen pids and Unity's CPU is ~0 (`ps -o %cpu`), then
  `resume`. The suite must finish with 0 FAIL. If a test fails because of the
  freeze (a wall-clock timeout), report which one.
- With HOLD set, a second `tools/with-unity-lock.sh true` waits and proceeds within
  ~60 s of `resume`.
- `MODE=walk MINUTES=1 ./run-tg2world.sh` with a 30 s pause in the middle prints the
  PAUSED DURING RUN warning. The same run without a pause prints none.
- `pause --kill` during a short run: the processes are gone and the RSS is freed
  (`status` before/after). The script exits non-zero and says so.
- Fast EditMode suite 0 FAIL before committing. Document the commands in
  `CLAUDE.md` "ONE UNITY PROCESS AT A TIME" (2-4 lines) and `KNOWN_ISSUES.md` (1 line).
- HANDOFF says plainly what pausing does NOT do: a frozen Unity keeps its RAM (macOS
  compresses/swaps it), and only `--kill` frees it.
