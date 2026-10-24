# 116-perf-shell-sky-leap

Model: opus
(No Requires: 115 is measurement only and changes no frame time, so this runs after it even if 115 stops for the owner.)

Approval: owner, 2026-10-22 (chat): shells stay ON and their cost is cut (Q66 (a); owner: shells off "is bad for gameplay",
far edits must stay 3D). PERF_30FPS_PLAN.md row 1 / section 3 step 1. Supersedes `later/102` (read it for the history).
PERFORMANCE task, ONE change.

## Change
Shell column sky leap, exact (0 px): a rising shell ray above its chunk column's highest stored brick (and above the far band's
height there) leaps to the column's edge, instead of climbing brick by brick to the global `SolidTopY` (task 99). A small
per-column max-height table in the shell page tail (KB, like 93b5's mask; a layout ADDITION, not a frozen structure). Kept in
sync on shell fill and edits. Prove exactness with a CPU twin + test like task 99 (0 px vs today at the 110 poses).

## Measure (plan section 3)
Warm-up launch (discarded), then back to back, AC only: one cost-split launch FULL vs leap_off at walk_near / walk_open (ABBA
inside), and `MODE=walk` 3 min x 4 (A B B A). Report walk p50 / p95 / % > 33 ms / p99 and near/open p50 vs task 110.
Decision rule: >= 2 ms at near or open -> ON by default; < 1 ms at both -> off by default and the line closes; between -> ON.

## Output
HANDOFF with the numbers, 0-px evidence, `## For the doc`. TASKS.md paragraph. Fast suite once at the end. STOP_FOR_OWNER.
