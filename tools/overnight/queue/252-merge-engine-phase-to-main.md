# 252-merge-engine-phase-to-main

Model: opus

Approval: OWNER 2026-11-20 ("go with recommended for all questions") -> Q-AS (a): merge `engine-phase` into `main` with a plain merge
commit, then push `main` and `engine-phase` to origin. Task 234 prepared it (rollback point `main` = 412e04e).

1. CLAUDE.md: the WHOLE suite before any merge to main. `SUITE=all ./run-editmode-tests.sh` (takes the unity lock). Any FAIL -> stop
   (BLOCKED), do not merge. Known: 249's `SunCacheOracleTests` C4 is `[Explicit]`, so it should not run; the river-ford Slow route test is
   red by known cause only if it is listed so in KNOWN_ISSUES -- say which.
2. Merge WITHOUT checking out `main` (other tasks and the owner use this working tree; never switch branches here):
   `git merge-base --is-ancestor main engine-phase` must hold, then
   `c=$(git commit-tree engine-phase^{tree} -p main -p engine-phase -m "Merge engine-phase into main (tasks 109-251; Q-AS)")` and
   `git update-ref refs/heads/main $c <old main sha>`. Check `git diff main engine-phase` is empty.
3. Secrets check as task 234 did (no .env / credentials / settings.local.json in the range). Size check (< 100 MB files).
4. `git push origin main engine-phase`. If the push is refused by permissions, stop with BLOCKED and the exact command for the owner.

`## For the doc`: commits merged, whole-suite line, the new `main` sha, push result, rollback sha 412e04e.
