# 220-plan-keep-the-budget

Model: opus
Requires: 219-perf-feasibility-ladder

Approval: OWNER, 2026-11-16 (chat): "once that question is answered we need a way to make sure we maintain it as we go about future tasks".
PLAN ONLY plus doc: no engine code. May edit ONE Claude Doc (a new tab in "Queue decisions" is NOT allowed: write the plan as a repo file
`PERF_BUDGET_GUARD.md`; the supervisor publishes it) and `tools/overnight/PREAMBLE.md`.

## Do
1. Read 219's HANDOFF (the works/doesn't-work table, the chosen floor preset) and PREAMBLE rule 11, rule 10 (measure performance only in performance
   tasks) and CLAUDE.md "Efficient by construction". The owner's rule 10 stands: no per-task frame-time arms.
2. Design a guard that keeps the achieved budget WITHOUT per-task timing: (a) a budget file (`PERF_BUDGET.json`: the floor preset, p50 / % > 33 ms
   / peak MB numbers from 219, and per-feature cost shares); (b) a cheap per-task proxy every drawing/content task already can report (memory peak,
   pool refusals, shader ALU/step counts or per-kernel ms from tooling that exists, instance/tree counts), with a limit per task; (c) a MILESTONE
   check: ONE script (`run-budget-check.sh`, reusing 219's arms at the floor preset, ~30 min) that compares to the budget file and fails loudly,
   run after every N tasks that touched drawing (propose N) and before any merge to `main`; (d) what the supervisor does when it fails (queue a
   bisect task, never stack features).
3. Write the rule text for PREAMBLE (rule 12) and the supervisor's queue rule (a task file for a drawing/content change names its cost proxy and
   limit). Also write `PERF_BUDGET_GUARD.md` with options the owner can pick between (strict: run the check after every drawing task, ~30 min each;
   milestone: every 5 tasks; merge-only), each with cost, recommendation first.
4. Do NOT write the script yet: the owner picks the option first (STOP_FOR_OWNER with the question is the intended end if more than one option
   remains reasonable).

## End
HANDOFF with `## For the doc` (the options in plain words).
