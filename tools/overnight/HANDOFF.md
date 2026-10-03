# Overnight handoff

**14-heavy-pause-toggle: DONE. All acceptance checks pass.** Commit `74f263a` on `surface-rebuild`. Tooling only: no engine, shader or performance code changed.

## Commands
    tools/heavy.sh pause          # SIGSTOP Unity/il2cpp/bee/player + their child processes; block new launches
    tools/heavy.sh pause --kill   # SIGTERM them (SIGKILL after 10 s): frees the RAM, loses the run
    tools/heavy.sh resume         # SIGCONT what was frozen; lift the block
    tools/heavy.sh status         # PAUSED/RUNNING, frozen pids (stat/cpu/rss), heavy processes now, lock owner+age

## What changed
- **`tools/heavy.sh` (new).**
  - HOLD file is `/tmp/voxel-unity.hold`, next to the lock. It records the frozen pids, and with `--kill` the killed pids too.
  - Pauses are logged to `Logs/heavy-pauses.log` as `pause <epoch> [kill]` / `resume <epoch>`.
  - Running `pause` twice or `resume` while not paused is harmless.
  - It never signals claude, node, shell or tmux processes.
  - `overlap START [END]` is the check that the run scripts call.
- **`tools/unity-lock.sh`:**
  - `unity_core_pids` holds the process patterns. `unity_busy` now calls it, and heavy.sh adds child processes plus any orphaned `bee_backend`.
  - `unity_lock_acquire` waits while HOLD exists and prints `held by owner pause -- waiting` once.
  - The 2 h stale-lock rule is skipped while HOLD exists. `pause` and `resume` both touch the lock.
- **`run-tg2world.sh`, `run-acceptance-rig.sh`:** at the end, and on build/no-report failure, they print `WARNING: PAUSED DURING RUN (<n> s) -- timing numbers from this run are invalid` if a pause overlapped the run. A `--kill` pause also prints `RUN KILLED BY OWNER PAUSE` and the script exits 3. tg2world writes these lines into its log as well.
- **Docs:** `CLAUDE.md` (4 lines under ONE UNITY PROCESS) and `KNOWN_ISSUES.md` (1 bullet, top of the open-issues list).

## Evidence (logs in Logs/overnight/t14_*.out)
- **60 s freeze during the fast suite:**
  - All 5 processes showed `stat TN`, cpu 0.0%. Unity's cumulative CPU time held at 1:04.56.
  - After `resume`, Unity was back to `RN` at 97.6%.
  - Suite result: **PASS 605 FAIL 0 SKIP 12**, wall 4m11s (normal ~3m07s). No test was hurt by the freeze.
- **Lock wait:** with HOLD set, `with-unity-lock.sh true` printed `held by owner pause -- waiting`. It then got the lock **47 s after resume**: 45 s of required clear time plus polling.
- **`MODE=walk MINUTES=1 ./run-tg2world.sh`:**
  - With a 30 s pause mid-walk: the log printed `WARNING: PAUSED DURING RUN (30 s)` (`Logs/tg2world_20261003_145554.txt`).
  - Without a pause: no warning (`Logs/tg2world_20261003_145204.txt`). In that run my pause landed 5 s after the run ended, so it also shows that a pause outside the run window is correctly ignored.
- **`pause --kill` during a walk run:**
  - status before: player pid 18762, 343 MB RSS. After: none.
  - The player exited on SIGTERM in under 10 s, so the SIGKILL fallback was not exercised.
  - The run script printed KILLED + PAUSED (5 s) and exited **3** (`Logs/tg2world_20261003_145852.txt`).
- **Overlap arithmetic:** 9 synthetic windows against a fake pause log, all correct (none / partial / open pause / kill).
- **Fast suite before commit (no pause):** PASS 605 FAIL 0 SKIP 12, wall 3m07s.
- No screenshots: none were needed for this task.

## What pausing does NOT do
- **A frozen process keeps its RAM.** macOS compresses or swaps its pages. In the test, the frozen Unity's RSS fell 412 -> 10 MB, but that memory moved to the compressor and was not freed.
- **Only `pause --kill` frees memory**, and it loses the run in progress.
- **A pause does not stop the agent or the queue runner.** They keep running. Only the next Unity launch waits, so an agent may sit in a rig/test script for the whole pause.
- **A paused rig run's timings are invalid.** The player sees one huge frame on resume. Rerun it.
- A pause longer than 2 h on our lock relies on the sibling honouring HOLD (below). Until then, its stale-lock rule could remove our lock.

## Note for the sibling repo (../VoxelTerraria_compare) owner
Please make your lock wait honour `/tmp/voxel-unity.hold`: don't acquire while it exists, and don't treat our lock as stale while it exists. The reference is `unity_hold_wait` in this repo's `tools/unity-lock.sh`.

## Unfinished / next
- Not covered by a test: `run-acceptance-rig.sh`'s warning path. It is the same `overlap` call, syntax-checked only; the rig was not run, to save ~10 min.
- The SIGKILL fallback was not exercised either.
- Next queued task: 15 (B1).
