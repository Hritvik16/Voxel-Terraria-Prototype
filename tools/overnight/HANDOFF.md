# 234-merge-readiness-check: DONE. Verdict READY: engine-phase can merge into main once the owner says go. Nothing was merged or pushed, and main was not touched.

## What changed
- No code. The report is `Logs/overnight/234/merge_readiness.md` (full detail, the proposed doc lines and the exact merge commands). The suite output is `Logs/overnight/234/all.out`. Commit: see `git log -1` ("234: merge readiness report").

## Evidence
- Branch: 240 commits ahead (`main..engine-phase`); 806 files, +65,627 / -617 lines. `main` == `origin/main` == `412e04e` (2026-11-06), and `main` is an ancestor of engine-phase.
- Push size: **1.7 MB** (git bundle of `main..engine-phase`). Largest file `Raymarch.compute` 401 KB. No blob over 5 MB, no images, no secret-looking files or lines. `.gitignore` covers `/Logs/` and `Screenshots/**` images.
- Uncommitted: only the known generated files (`Assets/Scenes/Playground.unity`, 8 `Logs/TerrainGen/*` txt) plus the runner's queue/done moves and untracked Screenshots viewers.
- **SUITE=all: PASS 1189 FAIL 0 SKIP 35**, 37m34s. The known slow-route failure (seed 31337 ford) is no longer red: `RouteTests` has passed since task 179. Nothing new failed. Skips: 16 report-only, the rest opt-in probes.
- Budget check: **NOT re-run (my decision).** Task 235's accepted check (`Logs/budget/2026-11-17_235256/check.txt`, 01:32 today, CLEAN, `BUDGET PASS`, rebaselined) ran on exactly HEAD's engine code: nothing under Assets/ProjectSettings/Packages changed after `c140a9a`, and the 235 run started after it. A re-run could only give PASS (READY) or noise-WARN (still READY), so it would spend ~100 min of a hot Mac on no decision. To run it anyway: `tools/overnight/run-budget-check.sh`.
  Its table, p50 ms: forest_eye 16 deg 135.4 | jungle noon 70.0 | torches night 52.7 | walk_near 16 deg 35.4 (controls: forest noon 99.2, walk noon 34.1). Hard limits PASS: peak 2,621 MB < 3,000, and walk_near has 0 GPU refusals.
- No screenshots were taken (none needed: nothing visible changed).

## Docs out of date (lines proposed in the report, sec. 4; CLAUDE.md NOT edited)
- CLAUDE.md "Current state" has stale lines: the merge note (10-22 / a5f6440; main has been at 412e04e since 11-06), the perf state (168b-e -> the 235 worst-case table), and the fast suite (997 -> 1107/0/25). The whole-suite count is also stale ("630 cases, ~9.5 min" -> ~1,224 cases, ~38 min).
- CLAUDE.md index: the "Slow route test red by known cause" line is stale (green since 179). There is no line yet for 236 (night torch 6 GB, FIXED).
- SESSION_HANDOFF.md predates 236/235/234; the supervisor refreshes it. TASKS.md and KNOWN_ISSUES.md are current.

## Merge plan (each step needs the owner's explicit go)
- Plain merge commit `--no-ff`, the same as a5f6440. This keeps the 240 per-task hashes that the docs cite (a squash would orphan them).
- Do it in a worktree (`git worktree add /tmp/voxel-main main`), so the working checkout and its uncommitted Playground.unity stay untouched.
- Then `git push origin main` and `git push origin engine-phase` (origin's engine-phase is stale at 475ac85 from 10-13; the push is a fast-forward). Exact commands: report sec. 5.
- Rollback: `main` -> `412e04e9c685bebf0624b999c8611f84136d906a` (before the push: `git branch -f main 412e04e`; after the push: `git revert -m 1 <merge>`, which is safer than a force push).
- Blocked leftovers (`tools/overnight/blocked/`): 7 open task files (178c, 180, 180b, 184, 184b, 208, 216; their safe work is already committed, and each waits on an owner question), 4 orphan patches from tasks that later finished (176c, 177b, 187, 191), and `stale-20261108/`. They are tracked text, and nothing applies them. Recommendation: merge them as they are; later, the supervisor can move the orphans to `stale-20261118/`.

## Next step
- Supervisor: ask the owner for the go (merge + push), and optionally apply the CLAUDE.md line updates first so main carries current notes. Queue: 237 (plan: path to 33 ms at the worst poses) is next.

## For the doc
- **Verdict: READY to merge** engine-phase -> main (owner go needed; nothing pushed). 240 commits; the push sends about 1.7 MB; no file over 5 MB; no secrets or images.
- Whole test suite: **1189 passed, 0 failed, 35 skipped** (37.5 min, `Logs/overnight/234/all.out`). The old route-test failure is gone (fixed in 179).
- Budget: the 235 check on this same code is `BUDGET PASS`. Memory peak 2,621 MB (under 3 GB). Worst pose: forest at a 16-degree sun, 135 ms (about 7 fps), which is still 4.1x the 30 fps frame. That is the performance stage's job, not a merge blocker.
- Rollback point: main `412e04e`. Merge method: plain merge commit (keeps the task history).
- No images: this task changed nothing visible.
