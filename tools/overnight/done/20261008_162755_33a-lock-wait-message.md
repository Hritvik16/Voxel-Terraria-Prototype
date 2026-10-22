# 33a-lock-wait-message

Approval: bug fix in tooling, no owner decision (supervisor, 2026-10-08, after the owner
asked "does run-play.sh output anything at all if it can't run yet?"). No performance arms.
Do NOT edit any `run-*.sh` or `tools/unity-lock.sh` while a script using it is running
(CLAUDE.md: bash reads scripts incrementally) -- check `tools/heavy.sh status` first.

## The bug (found by the supervisor reading `tools/unity-lock.sh`)
`unity_lock_acquire` treats a lock whose owner name equals `UNITY_LOCK_ME`
("voxel-terraria-1-byte") as "already held by this project (an outer script took it)".
But the queue's task scripts AND the owner's `./run-play.sh` both use that same name. So when
the owner runs `./run-play.sh` while a queue task holds the lock:
1. it prints NOTHING (the same-owner branch only loops on `unity_busy`; it never echoes),
   so the owner sees a silent terminal; and
2. as soon as no Unity process is alive for a moment (e.g. between a task's test run and its
   build), it returns WITHOUT owning the lock and launches the player -- while the task still
   holds the lock and may start Unity again: two Unity processes on the 8 GB Mac.

## Do (one change)
- Make the "already held" shortcut apply only when the lock really belongs to an outer
  script of THIS process (e.g. the pid in the lock file is an ancestor of `$$`, or an exported
  `UNITY_LOCK_HELD_BY=<pid>` that child scripts inherit). Otherwise wait like for any holder.
- While waiting, print who holds it and what is running, once at the start and then about
  once a minute: e.g. `14:41:05 waiting for the Unity lock: held by voxel-terraria-1-byte
  since 14:38:47 (pid 41111: tools/run-editmode-filter.sh Sweep20_EasyAreas) -- your command
  starts when it is free`. Same for the pause and the 45 s "clear" wait (say it is waiting
  for the Mac to be clear of Unity for 45 s).
- Keep the sibling-repo protocol unchanged (same lock file, same owner names).

## Acceptance
- A small script test (bash, no Unity): hold the lock from one shell as a fake task (a
  background process writing the lock file), run a second `unity_lock_acquire` from an
  unrelated shell: it prints the waiting line and does NOT return until the lock is released.
  A nested acquire (outer script holds it, child script acquires) still returns at once.
  Put the test under `tools/` and say how to run it in HANDOFF.
- Fast EditMode suite green (it goes through the lock -- proves the normal path still works).
