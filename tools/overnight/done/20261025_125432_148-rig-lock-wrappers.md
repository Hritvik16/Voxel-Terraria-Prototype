# 148-rig-lock-wrappers

Model: opus

Approval: supervisor, 2026-10-25, "going ahead unless you object" (follow-up R2 of `RIG_REFRESH_PLAN.md`, task 144: 29 run-*.sh do not take the
shared Unity lock, incl. editmode / phase7 / acceptance / acceptance-fluid / fluid-tiled). CLAUDE.md rule: ONE Unity process at a time.

## Do
For every script the plan lists as not taking the lock and that is kept or updated (not retired): make it `source tools/unity-lock.sh` and
call `unity_lock_acquire` the way the newer runners do. Smallest edit per script, no behaviour change otherwise. Never edit a run-*.sh
while it runs (check `ps`/the lock first). Verify with `bash -n` on each and one real run of `./run-editmode-tests.sh` (fast suite) at the end.

## Deliverable
HANDOFF: the list of scripts changed, bash -n results, the suite line.
