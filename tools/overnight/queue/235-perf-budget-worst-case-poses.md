# 235-perf-budget-worst-case-poses
Model: opus
Requires: 230-perf-budget-check-script
Approval: OWNER 2026-11-17 (chat): "I want 1 for now" = judge performance on WORST-CASE poses, not noon. Owner's point: a low sun and torches both occur in play, so a gain that only shows at noon / with no torch near is not a gain for the 30 fps target.
Performance task (timing allowed, owner rule 10; warm steady state, interleaved arms; write outcomes first in `Logs/overnight/235/PLAN.md`).
## Do
1. Read `PERF_BUDGET_GUARD.md`, `PERF_BUDGET.json`, `tools/overnight/run-budget-check.sh` (task 230's script) and the HANDOFFs of 229, 232, 233 (done/).
2. Add WORST-CASE poses to the budget check and its `PERF_BUDGET.json` baseline, using existing capture options only (233's `-startsun`, `-costtimedpaths`, 229's torch poses): (a) forest_eye at a 16 deg morning sun (`-startsun 16`), (b) torch_camp at night with torches (flood twin drawn), (c) jungle_eye at noon (the heaviest GPU-pool pose), (d) walk_near at 16 deg. Keep the noon poses as the control row. One table: pose x step (sun / sky fill / flood / rest) with p50 ms, plus the share each step takes of 33.3 ms.
3. Make the verdict line count the WORST pose per step, not the noon one: `BUDGET FAIL|WARN|PASS` is decided by the worst-case rows. State in the script header why (owner, 2026-11-17).
4. Run it ONCE (warm-up first, ABBA, <= the launch budget the script already uses; if it needs more than ~40 min say so and split the poses over two launches) and `--rebaseline "owner 2026-11-17, task 235"`. Report the table, which poses are furthest over 33.3 ms, and the top 3 steps by ms at the worst pose. No code changes outside the script/JSON/docs.
5. Update the rule in PREAMBLE 12 and `PERF_BUDGET_GUARD.md` in one line each: "report worst-case poses, not only noon / no torches". `## For the doc`: the table and a plain-words reading of "how far is the worst case from 30 fps".
