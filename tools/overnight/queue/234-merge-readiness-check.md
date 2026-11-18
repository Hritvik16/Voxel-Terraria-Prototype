# 234-merge-readiness-check
Model: opus
Requires: 235-perf-budget-worst-case-poses
Approval: OWNER 2026-11-17 (chat): "we should also probably push and merge to origin main once we have reached a stable point". This task only CHECKS readiness. It does NOT merge, does NOT push, does NOT touch `main` (anything on `main` and any push is the owner's explicit go, asked by the supervisor).
## Do
1. State of the branch: `git log --oneline main..engine-phase | wc -l`, `git status --short` (list anything uncommitted that is not a known generated file: `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*`), is `origin/main` == `main`, any file over 50 MB or any secret-looking file in the diff `main..engine-phase` (`git diff --stat main..engine-phase | sort by size`, check .gitignore covers `Screenshots/**` images and `Logs/` output). Report size of what a push would send.
2. `SUITE=all ./run-editmode-tests.sh` once (the whole suite, before any merge to main; CLAUDE.md). Report PASS/FAIL/SKIP and the known slow-route failure (seed 31337 river ford bank, KNOWN_ISSUES) separately from anything new.
3. Run the budget check from task 230 (`tools/overnight/run-budget-check.sh`) and quote its BUDGET line and table. No other timing.
4. Docs: list which of CLAUDE.md "Current state", `SESSION_HANDOFF.md`, `TASKS.md`, KNOWN_ISSUES index lines are out of date for a merge (do not rewrite CLAUDE.md; propose the lines in HANDOFF).
5. Write a "merge plan" in HANDOFF: merge method (plain merge commit vs squash; recommend plain merge to keep history), the exact commands, what to do about the 8 blocked tasks' leftovers in `tools/overnight/blocked/`, and a rollback line (the `main` commit to reset to). Verdict: READY or NOT READY with the reasons.
## Rules
No code changes. Commit only the report. If SUITE=all shows a NEW failure, say so and STOP_FOR_OWNER only if it is not fixable in one small change; otherwise list it. `## For the doc` block with the verdict and numbers.
