# 145b-rig-playwalk-mode

Model: opus

Approval: supervisor, 2026-10-25, "going ahead unless you object" (reversible measurement-method choice; follow-up R5 of `RIG_REFRESH_PLAN.md`
section 5, task 144). Needed so task 146 measures what the owner plays.

## Do
Read `RIG_REFRESH_PLAN.md` (R5 and the run-tg2world.sh findings). Add `MODE=playwalk` to `run-tg2world.sh` (or a small
`run-perf-milestone.sh` that wraps it): the standard walk with the PLAY defaults: tg2 map v4, shells ON, FSR 3 dyn (what `./run-play.sh`
sets), pool 480, Survival/motor as play. Reuse the flags `run-play.sh` passes; do not duplicate them by hand if they can be shared.
Report line: p50 / p95 / p99, share of frames > 33 ms, the internal rows chosen by dyn FSR. Also print the exception count from Player.log
(task 140: the old `head -20` hid it). Takes the shared Unity lock. Do NOT run a timed measurement (task 146 does): a short non-timed
smoke launch (<= 3 min) proving the mode starts with the right flags (check the log's flag echo). Fast suite once at the end.
Never edit a run-*.sh while one is running.

## Deliverable
HANDOFF: the mode's name, the flags it sets (list), smoke-log path. TASKS.md paragraph.
