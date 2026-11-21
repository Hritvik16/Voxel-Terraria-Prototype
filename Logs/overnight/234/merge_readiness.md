# Task 234 -- merge readiness check (engine-phase -> main), 2026-11-18

Verdict: **READY** (to merge once the owner says go). Nothing was merged, pushed or changed on `main`.

## 1. Branch state
- `engine-phase` = `b031629`, `main` = `origin/main` = `412e04e` (178b HANDOFF, 2026-11-06). `main` is an ancestor
  of `engine-phase` (fast-forward possible; plain merge commit recommended below).
- `git log --oneline main..engine-phase | wc -l` = **240 commits**; 806 files changed, +65,627 / -617 lines.
  By folder: Assets/CoreEngine 156, Logs/overnight 156 (text), tools/overnight 102, Assets/TerrainGen 58,
  Screenshots/* text reports ~130, Logs/TerrainGen 34, Logs/budget 26, Assets/Game 23.
- Uncommitted (all known/generated, NOT to be committed): `Assets/Scenes/Playground.unity` (regenerated),
  8 `Logs/TerrainGen/*` txt files, the deleted queue files 235/236 (runner moved them to `done/`), untracked
  `Screenshots/*` viewer.html + image folders, two new `tools/overnight/done/` files (runner). Nothing else.
- Size of the push: `git bundle create main..engine-phase` = **1.7 MB** (1,325 new blobs, 43.9 MB raw before deltas).
  Largest file in the diff: `Raymarch.compute` 401 KB. **No blob over 5 MB** (none over 50 MB).
- Secrets: no secret-looking file names (.env/credential/token/key/settings.local) and no secret-looking added
  lines (api key, password, private key header, ghp_/sk-/AKIA tokens) in `git diff main..engine-phase`.
- Images: no png/jpg/mp4/mov/gif/zip/bin in the diff. `.gitignore` covers `/Logs/` (text logs are force-added
  on purpose) and `Screenshots/**/*.{png,PNG,jpg,jpeg,mp4,mov,gif}`.
- `origin` also has a stale `engine-phase` at `475ac85` (2026-10-13), an ancestor of the local branch.

## 2. Whole EditMode suite (SUITE=all)
- **PASS 1189 FAIL 0 SKIP 35**, wall 37m34s (`Logs/overnight/234/all.out`, `TestResults_all.xml` 2026-11-18 02:21).
- The "known slow-route failure" (seed 31337 river ford bank) did NOT fail: the route test has been green since
  task 179 (KNOWN_ISSUES; `RouteTests.V4_FourSeeds_RoutesJoinTheEasyAreas` Passed). Nothing new failed.
- Skips: 16 report-only (`REPORTS=1`), the rest opt-in probes / pose writers (213, 223, 180b, 210, ...).

## 3. Budget check
Not re-run. Decision (CLAUDE.md "if every outcome leads to the same action, skip the measurement"): the
accepted task-235 check (`Logs/budget/2026-11-17_235256/check.txt`, 2026-11-18 01:32, CLEAN, BUDGET PASS,
rebaselined) ran on exactly HEAD's engine code -- no file under `Assets/`, `ProjectSettings/` or `Packages/`
changed after 236's commit `c140a9a` (23:44), and 235's run started 23:52. A re-run would compare the same code
with its own baseline 30 min later: PASS -> READY, WARN -> noise of the check -> still READY. ~100 min of a hot Mac
for no decision. `BUDGET_COUNT` is 0. Command if the owner still wants it: `tools/overnight/run-budget-check.sh`.
BUDGET line: `BUDGET PASS` / `REBASELINED PERF_BUDGET.json (owner 2026-11-17, task 235)`. Table (p50 ms):

| pose | role | FULL | sun | sky fill | flood | rest | p95 | >33 ms % | deficit |
|---|---|---|---|---|---|---|---|---|---|
| walk_near 16 deg | worst | 35.4 | 12.9 | -0.5 | 0 | 23.0 | 86.9 | 56.8 | 0 |
| forest_eye 16 deg | worst | 135.4 | 54.7 | 25.5 | 0 | 55.2 | 198.2 | 97.9 | 573 |
| jungle_eye noon | worst | 70.0 | 15.5 | -0.8 | 0 | 55.3 | 99.9 | 99.5 | 942 |
| torch_camp night | worst | 52.7 | 0.4 | 0.9 | 8.4 | 43.0 | 76.5 | 90.9 | 0 |
| walk_near noon | control | 34.1 | | | | | 64.0 | 53.9 | 0 |
| forest_eye noon | control | 99.2 | | | | | 138.6 | 97.3 | 573 |

Hard limits: memory peak 2,621 MB < 3,000 PASS; GPU refusals at walk_near 0 PASS. Jungle refusals 4,472 (known).
Performance is NOT at 30 fps (worst pose 4.1x); that is stage 2 of the owner's order, not a merge gate.

## 4. Docs out of date for a merge (CLAUDE.md not rewritten; proposed lines)
- CLAUDE.md "Current state":
  - "engine-phase merged to `main` on 2026-10-22 (task 109, merge `a5f6440`)" -> after the merge:
    "engine-phase merged to `main` again on <date> (task 234 check, merge `<hash>`; previous `main` `412e04e`)".
    (Also: `main` already moved past `a5f6440` to `412e04e` on 2026-11-06.)
  - "Perf state when paused (168b-168e): walk p50 23.7 ms met, 16-19 % of frames > 33 ms" -> "Perf (budget check 235,
    worst-case poses, warm): forest_eye 16 deg 135.4 ms p50 (4.1x 33.3 ms), jungle noon 70.0, torches night 52.7,
    walk_near 16 deg 35.4; peak 2,621 MB (`Logs/budget/2026-11-17_235256/check.txt`)."
  - "Fast suite `PASS 997 FAIL 0 SKIP 16` (2026-11-02, task 168e)" -> "Fast suite `PASS 1107 FAIL 0 SKIP 25`
    (2026-11-17, task 235); whole suite `PASS 1189 FAIL 0 SKIP 35` (2026-11-18, task 234)."
  - "Task 170 (torch memory trim) parked" -> still true; add "night torch 6 GB peak fixed (236)".
- CLAUDE.md Commands: "whole suite, 630 cases, ~9.5 min" -> "whole suite, ~1,224 cases, ~38 min".
- CLAUDE.md known-issues INDEX:
  - "Slow route test red by known cause (176, 178b): ... Never loosen it." -> "Route test GREEN since 179 (C5); seed 31337
    ford fixed. Never loosen it."
  - add: "Night torch scene peaked ~6 GB (230; FIXED 236: one SetData per buffer per Upload, `-tfupload 0` = old)."
- `SESSION_HANDOFF.md`: written 2026-11-17 22:30, before 236/235/234 ran (says "queue 236 -> 235 -> 234"); the
  supervisor's next handoff refreshes it. Not a merge blocker.
- `TASKS.md`: current through 235/236. 234 adds no feature (no paragraph needed).
- `KNOWN_ISSUES.md`: current (236 entry, route test green).

## 5. Merge plan (owner's explicit go required for every step below)
Method: **plain merge commit (`--no-ff`)**, like the previous merge `a5f6440`: keeps the 240 per-task commits that
the handoffs, TASKS.md and KNOWN_ISSUES cite by hash. Squash would orphan those hashes.
Done in a separate worktree so the main checkout (uncommitted Playground.unity, Unity project lock) is not touched:
```
cd "<repo>"
git fetch origin && test "$(git rev-parse origin/main)" = 412e04e9c685bebf0624b999c8611f84136d906a
git worktree add /tmp/voxel-main main
git -C /tmp/voxel-main merge --no-ff engine-phase -m "Merge engine-phase: look stage (lighting, world gen C1-C6, trees, sky/grade), torch flood, budget check (tasks 110-236)"
git -C /tmp/voxel-main log --oneline -1           # note the merge hash
git push origin main                               # sends ~1.7 MB
git push origin engine-phase                       # updates the stale origin/engine-phase (475ac85 -> b031629), fast-forward
git worktree remove /tmp/voxel-main
```
Work continues on `engine-phase` afterwards (as after a5f6440).
Rollback (before push): `git branch -f main 412e04e` (main is not checked out). After push:
`git push --force-with-lease=main:<merge hash> origin 412e04e:main` (owner only; or `git revert -m 1 <merge hash>`, safer).

Blocked leftovers (`tools/overnight/blocked/`, 50 tracked text files, 456 KB): 7 open task files (178c, 180, 180b, 184,
184b, 208, 216 -- each already committed its safe work on the branch and waits on an owner question), 4 orphan
patches whose tasks later finished (176c, 177b, 187, 191), 7 `.patch` files total, plus `stale-20261108/`.
Recommend: merge them as they are (they are records, nothing applies them; no code in them runs). Optional later
housekeeping (supervisor): move the 4 orphan patches + their HANDOFFs into a `stale-20261118/` folder.

## Verdict: READY
Whole suite green, no new failures, no large or secret files, push 1.7 MB, budget check PASS on this exact code
(hard limits met; 30 fps not met = the known performance stage). Before merging: owner's go; optionally apply the
CLAUDE.md line updates above first so `main` carries current state notes.
