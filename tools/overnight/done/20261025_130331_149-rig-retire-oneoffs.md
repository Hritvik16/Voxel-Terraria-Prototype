# 149-rig-retire-oneoffs

Model: opus

Approval: supervisor, 2026-10-25, "going ahead unless you object" (follow-up R1 of `RIG_REFRESH_PLAN.md`, task 144, Q1 (a): retire 32 scripts).
Reversible: files are MOVED, not deleted.

## Do
`git mv` the 32 scripts the plan's section 3 marks "retire" into `tools/retired-rigs/`, with a short README there (name, why retired, last
report date, one line each). Before moving each, grep that no kept script, doc index or CLAUDE.md-cited command calls it (`grep -rn`);
if one does, leave that script and name it in HANDOFF. Do not touch the keep / update / merge scripts. Fast suite once at the end.

## Deliverable
HANDOFF: moved count, left-behind list with reason.
