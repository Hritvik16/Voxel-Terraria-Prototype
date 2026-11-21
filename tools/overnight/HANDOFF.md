# 252-merge-engine-phase-to-main: DONE -- engine-phase merged into main (merge 3a4ca0a, 277 commits), both branches pushed to origin
## What changed
- `main`: 412e04e -> **3a4ca0a** = plain merge commit (parents 412e04e, eec7e3d), made without checking out `main`:
  `git commit-tree engine-phase^{tree} -p main -p engine-phase` + `git update-ref refs/heads/main <new> 412e04e`.
  `git merge-base --is-ancestor main engine-phase` held; `git diff main engine-phase` is EMPTY after the merge.
- engine-phase tip merged = eec7e3d (the supervisor's 255 task-file commit landed during the suite run; it adds only
  `tools/overnight/later/255-perf-budget-check-after-merge.md`, no code, so the suite result covers it).
- Pushed: `main` 412e04e..3a4ca0a, `engine-phase` 475ac85..eec7e3d (fast-forward, no force). `git ls-remote` confirms both.
- Follow-up commit on engine-phase (this HANDOFF, `TASKS.md` Task 252, the CLAUDE.md state line now says merged 2026-11-21,
  `3a4ca0a`, rollback `412e04e`). It is pushed to `engine-phase` too; `main` stays at 3a4ca0a (one docs commit behind, normal).
## Evidence
- Whole suite before the merge: `SUITE=all ./run-editmode-tests.sh` -> **PASS 1224 FAIL 0 SKIP 38, suite=all, wall=43m00s**
  (`Logs/overnight/252/suite_all.out`, results copy `Logs/overnight/252/TestResults_all.xml`). Took 43 min, not ~15:
  the `SUITE=all` target in the script header is <= 15 min, so the suite has grown well past it.
- River-ford Slow route test `RouteTests.V4_FourSeeds_RoutesJoinTheEasyAreas`: **Passed**. KNOWN_ISSUES says it has been green
  since task 179 (C5), so it no longer counts as a known red. (The CLAUDE.md index line "Slow route test red by known cause"
  is out of date. I left it alone: it's the supervisor's index.)
- `SunCacheOracleTests.C4_CanopyDapples_StrengthPerMap`: Skipped (`[Explicit]`, as 251 set it).
- Secrets check (as 234 did): no `.env` / credentials / `settings.local.json` / key / pem path in `git diff --name-only main engine-phase`
  or in any commit of the range. An added-line scan for sk-ant / AKIA / ghp_ / private-key / xox tokens found none.
- Size check: no blob > 50 MB in the range. The largest is 413 KB (`Assets/CoreEngine/Rendering/Raymarch.compute`). Range diff: 912 files, +78,554 / -681.
## Notes
- The working tree had untracked leftovers from blocked task 184c (`Assets/{CoreEngine,TerrainGen}/Tests/{TestPar,ParallelEquivalenceTests}.cs`).
  They compile into the run, but their tests are `Category("Nightly")` and `SUITE=all` excludes those. They are NOT part of the merge, and I left them in place.
  `Assets/Scenes/Playground.unity` and some `Logs/TerrainGen/**.txt` were already modified in the working tree before the run (the tests rewrite those logs).
  I left them uncommitted and untouched.
- Memory / pass card / slice (rules 11, 12, 14): none. This is a git operation: nothing is drawn or allocated. HEAVY: no.
## Next step
- Supervisor: 255 (budget check after the merge, rule 13) is idle-gated in `later/`. The rollback, if ever needed:
  `git update-ref refs/heads/main 412e04e 3a4ca0a` + `git push --force-with-lease origin main` (owner only).

## For the doc
- Commits merged into `main`: **277** (tasks 109-251 + supervisor commits up to eec7e3d), as one plain merge commit.
- Whole suite before the merge: **PASS 1224 FAIL 0 SKIP 38** (`SUITE=all`, 43 min; `Logs/overnight/252/suite_all.out`). The river-ford route test passes. 249's C4 is skipped (Explicit).
- New `main` sha: **3a4ca0a** (was 412e04e). `git diff main engine-phase` is empty.
- Push: `origin/main` 412e04e -> 3a4ca0a, `origin/engine-phase` 475ac85 -> eec7e3d (+ this docs commit). Both are fast-forwards and the push was accepted.
- Rollback point: **412e04e**.
- No images: a git merge changes nothing visible.
