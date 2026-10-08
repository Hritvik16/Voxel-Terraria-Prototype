# 33a-lock-wait-message: DONE. `./run-play.sh` (and any lock user) now really waits for a queue task's lock and says who it is waiting for. Code `fd18224`.

## What changed (`tools/unity-lock.sh`, new `tools/test-unity-lock.sh`)
- **The bug:** `unity_lock_acquire` treated any lock whose owner NAME was `voxel-terraria-1-byte` as "an outer script of ours holds it".
  Queue tasks and the owner's `run-play.sh` share that name. So run-play.sh printed nothing,
  then returned without owning the lock the moment no Unity process was alive. That could put two Unity processes on the Mac at once.
- **Now the shortcut needs proof:** the lock's `pid` (already written into the lock line) must be `$$` or an ancestor of it.
  It also counts if the pid equals an exported `UNITY_LOCK_HELD_BY` (set on take, so a detached child still counts) and that pid is alive.
  Otherwise it waits like for any holder.
- **Waiting lines:** printed at once, then about once a minute (or when the holder changes):
  - `16:30:04 waiting for the Unity lock: held by voxel-terraria-1-byte since 16:30:04 (pid 73048: <its command>) -- your command starts when it is free`
  - `still waiting for the Unity lock: held by ...` every ~60 s.
  - Owner pause: `waiting for the Unity lock: owner pause is on (tools/heavy.sh resume lifts it) ...`, then `still waiting: owner pause is on` each minute.
  - Clear wait: `lock is free; waiting for the Mac to be clear of Unity for 45 s` (with `running: pid N: <cmd>` while a Unity process is alive).
  - Nested call waiting on a straggler: `lock is ours (pid N); waiting for a Unity process to exit (...)`.
- **Clear-wait fix:** if the lock is taken or paused during the 45 s wait, it goes back to the outer loop, which names the holder.
  Before, it looped silently.
- **Dead-pid lock:** a lock with OUR name whose pid no longer runs is removed with a message.
  Without this, the stricter check would have turned an orphaned lock (killed holder) from "ignored" into a 2 h block.
  It re-reads the line before removing it. Sibling locks are untouched.
- **Unchanged:** the sibling protocol (same lock file, same owner names, same line format, 2 h stale rule, noclobber take).
- Test knobs `UNITY_LOCK_CLEAR_S/POLL_S/SAY_S` (45/5/60) are plain variables; the test reassigns them after sourcing.

## Evidence
- `tools/test-unity-lock.sh` (no Unity, ~20 s, private temp lock + `unity_busy` stubbed false, so it never touches `/tmp/voxel-unity.lock`):
  **ALL PASS**, 11 checks.
  1. A fake task holds the lock. An unrelated shell prints the waiting line naming the task's pid, repeats it, and is still waiting after 8 s.
     It acquires only after the release, and the lock is released on exit.
  2. Nested: the outer script holds the lock. A child acquire returns in 0 s with no waiting line, twice: once via the ancestor check alone (env var unset).
     The outer's lock is intact afterwards.
  3. A lock of ours with a dead pid is removed and taken.
- **Fast EditMode suite through the new lock:** `tools/with-unity-lock.sh ./run-editmode-tests.sh` gave **PASS 623 FAIL 0 SKIP 13** (`Logs/overnight/33a/editmode_fast.out`).
  Its first line shows the new clear-wait message. No performance arms (rule 10). No screenshots (tooling task).

## How to run the test
`tools/test-unity-lock.sh` prints PASS/FAIL per check and exits non-zero on any FAIL. It is safe while a real build runs.

## Left / next
- `../VoxelTerraria_compare/tools/unity-lock.sh` is a separate file. It was not touched, and its protocol is unchanged.
- Older scripts that do NOT take the lock (`run-editmode-tests.sh`, `run-acceptance-rig.sh`, ...) still need `tools/with-unity-lock.sh`, as before.
- Next: the next queued task.
