# 153-perf-shell-column-height-bound

Model: opus

Requires: 152-perf-shell-ray-compaction

Approval: owner, 2026-10-25, doc Q97 answer (a) (named: "then 1 (height bound), one task each"). The ~28 KB GPU column-height table is a layout
addition the doc Q97 text flagged as the owner's call; the owner chose (a) knowing that. If the table turns out larger than ~64 KB or touches
Appendix A/B layouts or a frozen API, STOP_FOR_OWNER instead.
A PERFORMANCE task, ONE change. Design: `Screenshots/137/height_bound_design.txt` (task 137). Goal: near sun rays stop climbing to the shells'
1,100 m top. Ceiling: task 133's cost, <= 6.1 ms walk_near, ~1 open, ~1.5 walk (task 146).

## Decision table
- gain >= 1 ms walk (window means) and 0 differing px: ship ON (`-shellheightbound 0` = before).
- gain < 1 ms: leave OFF, report why, name the next cut (rows below 540: needs owner stills at 432/540, see task 146 HANDOFF).
Generality rule (owner 2026-10-24): the bound is derived from the data (per-column maximum solid height), exact, updated on edit ops and
streaming; adversarial test: a 1-voxel pillar and an edit-op tower must raise the bound (rays must still hit them: 0 px difference).

## Do
Implement per the design (CPU Burst builds/updates the table from the same data the shells use; single-writer rules hold), proofs as
in task 152 (byte-identical raw stills incl. an edit-op tower pose), one warm cost measurement, fast suite once.

## Deliverable
HANDOFF + For the doc as in 152; TASKS.md paragraph.
