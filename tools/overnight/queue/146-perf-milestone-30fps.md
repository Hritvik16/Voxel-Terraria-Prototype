# 146-perf-milestone-30fps

Model: opus

Approval: owner, 2026-10-25, in chat ("No objection"): a milestone measurement of the walk with current defaults, before more lighting work.
Requires: 145b-rig-playwalk-mode
Goal (owner, 2026-10-22): stable 30 fps = warm, play settings, walking: p50 <= 30 ms AND <= 5% of frames > 33 ms.

## Decision table (written before measuring, CLAUDE.md rule)
- p50 <= 30 and >33 ms share <= 5%: goal met; recommend lighting work (Q94) proceeds, record the headroom in ms.
- p50 within 3 ms of 30: the remaining cut is small; list the top 2 candidate cuts from `PERF_30FPS_PLAN.md` still unspent.
- p50 more than 3 ms over 30: write the gap per stage and a ranked list of remaining cuts (exact ones first) with ms each; owner picks.
All three end in the same artifact: today's real gap and the ranked remaining cuts.

## Do
1. Check `pmset -g batt` (AC power). One warm-up launch, numbers discarded (`MODE=stream MOTION=walk ./run-tg2world.sh`), then, back to
   back with no idle gap over ~2 min, two measured arms with the PLAY defaults via `MODE=playwalk` from task 145b (shells ON, FSR 3 dyn, defaults after tasks 132/133/134):
   the standard walk (p50, p95, p99, share > 33 ms, rows chosen by dyn FSR) and the cost split used by
   `Logs/costsplit_2026-10-22_*.txt` (find how it was run in task 110's notes / TASKS.md) at walk_near and walk_open.
2. Compare against the 2026-10-22 baseline (walk p50 41.0, p99 99, 72-78% > 33 ms; walk_near 60.7, walk_open 43.0).
3. Read `PERF_30FPS_PLAN.md` section 3 and mark each cut done / spent / unspent with its measured ms from TASKS.md.

## Deliverable
HANDOFF `## For the doc`: before -> after table, warm-up log path, the ranked remaining cuts. No code change. Report "warm steady state".
