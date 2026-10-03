# Overnight handoff

**14b-pause-drain-only: DONE. All acceptance checks pass.** Commit `bc98a7b` on `surface-rebuild`. Tooling only: no engine, shader or performance code changed.

## Commands
    tools/heavy.sh pause    # HOLD file only: what is running finishes; new builds/tests/launches wait
    tools/heavy.sh resume   # lift HOLD; waiters proceed after the usual 45 s clear check
    tools/heavy.sh status   # PAUSED/RUNNING, lock owner + command + run time, waiters,
                            # "draining: N heavy processes still running" -> "idle -- safe to use the Mac"

## What changed
- **`tools/heavy.sh`:**
  - Removed the freeze and `--kill` paths: code, usage text and docs. `pause` never signals anything.
  - `status` gives the lock owner from the lock line, its pid's command, and its run time (measured from the lock's start stamp, not its mtime).
  - `overlap` returns 0 or 1, with no exit 3. Old `pause <t> kill` lines in `Logs/heavy-pauses.log` are still read correctly.
- **`tools/unity-lock.sh`:**
  - `unity_hold_wait` registers each waiter as `/tmp/voxel-unity.waiters/<pid>` (time + command) and removes it when the pause lifts. `status` skips and deletes entries whose pid is dead.
  - The stale-lock rule still never fires while HOLD exists, and `pause`/`resume` still touch the lock.
- **`run-tg2world.sh`, `run-acceptance-rig.sh`:** they print `NOTE: owner paused during this run (Mac likely in use) -- timing numbers may be contaminated; rerun this arm (paused N s of the run)`. The KILLED/exit-3 path is removed.
- **Docs:** the `CLAUDE.md` owner-pause lines and the `KNOWN_ISSUES.md` OWNER PAUSE bullet are rewritten.
- **Queued `16-pause-gate-in-launch-loops.md`** (follow-up, see below).

## Evidence (logs in Logs/overnight/t14b_*.out)
- **Pause during the fast suite** (`t14b_suite.out`):
  - Paused 65 s in. `status` showed `draining: 5 heavy processes still running`, with Unity at 100% CPU and 805 MB.
  - The suite kept running and finished **PASS 605 FAIL 0 SKIP 12, wall 3m02s** (normal is ~3m07s).
  - `status` then showed `lock: free` and `idle -- safe to use the Mac`.
  - All edits were in place before this run, so it is also the fast suite before the commit.
- **Lock wait while paused** (`t14b_waiter.out`):
  - `with-unity-lock.sh true`, started during the pause, printed `held by owner pause -- waiting`. `status` listed it under "waiting on the pause".
  - It stayed blocked after the suite released the lock.
  - It took the lock **51 s after `resume`**.
  - The waiters dir was empty afterwards.
- **2-launch loop** (`t14b_loop.out`, two `MODE=walk MINUTES=1 ./run-tg2world.sh` back to back), paused 60 s into launch 1:
  - **Launch 1** ran to completion, rc 0, with the NOTE (`paused 56 s of the run`): `Logs/tg2world_20261003_151303.txt`.
  - **Launch 2** printed `held by owner pause -- waiting` and stayed blocked for 1m40s while `status` showed it as a waiter and `idle`. No timeout.
  - After `resume` (15:17:28), launch 2 continued and finished rc 0, with no NOTE: `Logs/tg2world_20261003_151549.txt`.
  - Both logs are complete reports: 86.9 / 85.3 FPS tick lines, no exceptions, no NO REPORT. These are not performance claims; launch 1 is marked contaminated.
- **Synthetic `overlap` windows:** 6 windows against a fake log that included an old `kill` line, all correct.
- `grep -n "SIGSTOP\|kill -STOP\|--kill" tools/heavy.sh tools/unity-lock.sh` returns nothing.
- No screenshots: none were needed for this task.

## Not covered / caveats
- **Scripts that take the lock ONCE and loop over launches drain to their END**, not to the next launch. The pause takes effect only at the next `unity_lock_acquire`. The scripts are: `run-lavalight-ab`, `run-upscale-ab`, `run-roofskip-ab`, `run-perf60`, `run-looksweep`, `run-overhaul-batch`, `run-flyfast`, `run-farband`, and `run-phase7-lighting-ab` (~4 h under `with-unity-lock.sh`).
  - Per-launch callers (shell loops over `run-tg2world.sh`, which is how the B-step A/B arms and task 08's driver run) do stop at the boundary, as tested above.
  - Task 16 queues a `unity_hold_wait` before each launch in those loops.
- `run-acceptance-rig.sh`'s NOTE path is syntax-checked only. It is the same `overlap` call; the rig was not run, to save ~10 min.
- The sibling repo still needs to honour `/tmp/voxel-unity.hold` (see the 14 note); unchanged.

## Next
Queued: 15 (B1), then 16 (pause gate in single-lock loop scripts).
