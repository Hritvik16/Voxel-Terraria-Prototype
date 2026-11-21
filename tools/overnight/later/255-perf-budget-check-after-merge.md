# 255-perf-budget-check-after-merge
Model: opus
Approval: SUPERVISOR 2026-11-21, PREAMBLE rule 13 / `PERF_BUDGET_GUARD.md` sec. 3 ("before every merge to main the check passes").
Task 252 (owner-approved merge, Q-AS (a)) was queued without it, and it started before the supervisor noticed. This runs the check
right after the merge, so a regression in the merged range is caught now. Since the last accepted check (17 Nov 23:52, task 235 rebaseline),
these default-ON changes landed: 170 (torch flood memory trim) and 242 (torch flood scatter upload). Everything else is default OFF or tests only
(243-251). NEEDS AN IDLE MAC: parked in `later/`, released by `tools/overnight/release-when-idle.sh` after 30 min without input.
Performance task (rule 10 allows timing). Outcomes written BEFORE running:
- BUDGET PASS -> set `BUDGET_COUNT` to 0 (the script does it). Nothing else.
- BUDGET WARN -> report which pose/step moved. Supervisor queues a split for that step (one task), no fix yet.
- BUDGET FAIL -> STOP_FOR_OWNER with the pose/step table. The likely suspects are 170/242 at the torch_camp night pose. `main` stays as merged;
  the owner decides whether to fix forward or roll `main` back to 412e04e.
- NOT CLEAN (owner input) -> report it; the supervisor re-gates it.
## Do
1. `tools/overnight/run-budget-check.sh` (full check, ~1 h 45 min; it builds, warms up and runs the four worst-case launches). Do NOT pass
   `--rebaseline`. Use the unity lock (the script does).
2. Report the script's table, the verdict, and for each pose the delta against `PERF_BUDGET.json`. Memory (rule 11) from the same launches.
## Rules
No code changes. Commit only the report. `## For the doc`: the verdict, the pose table (worst pose first), and one still (torch_camp night).
