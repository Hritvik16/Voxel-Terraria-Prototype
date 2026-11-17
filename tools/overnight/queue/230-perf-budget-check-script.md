# 230-perf-budget-check-script
Model: opus
Requires: 229-perf-torch-flood-only-when-torches
Approval: OWNER 2026-11-16 (Q-AO): "b" = milestone check: after every 5 drawing/content tasks, at once after any heavy task, and before every merge to `main`.
Performance task (timing allowed, owner rule 10; warm steady state, interleaved arms; outcomes written first).
## Do
1. Read `PERF_BUDGET_GUARD.md` (sections 2, 3, 6), `PERF_BUDGET.json`, PREAMBLE rule 12 and the HANDOFF of 220. Write `tools/overnight/run-budget-check.sh` exactly as sec. 3 describes (build unless SKIP_BUILD=1, warm-up, one cost-split launch at the budget settings, `hididle` input watch, ONE table, `BUDGET PASS|WARN|FAIL|NOT CLEAN`, `--rebaseline "<who approved>"`). It holds the Unity lock. Reuse `run-cost-split.sh` machinery; do not duplicate it.
2. Record the owner's pick in `PERF_BUDGET_GUARD.md` (option b, N = 5) and add the counter: `tools/overnight/BUDGET_COUNT` = drawing/content tasks done since the last accepted check, plus a one-line rule in PREAMBLE telling the supervisor to queue the check when it reaches 5 or after a heavy task, and before any merge. Tasks 226 to 229 count as the first 5 (heavy: none unless one adds an output-resolution pass).
3. Run it ONCE for real on the current build (after 229's cheaper lighting) and `--rebaseline "owner Q-AO b, task 230"` to set the first accepted numbers. Report the table (sky fill / sun / flood steps, memory, refusals) and how long it took.
4. Test the failure path without a real regression: run the comparison part against a copy of the baseline made stricter by 20 % and show it prints `BUDGET FAIL` and exits non-zero. No frozen API / layout change. STOP_FOR_OWNER if one is needed.
