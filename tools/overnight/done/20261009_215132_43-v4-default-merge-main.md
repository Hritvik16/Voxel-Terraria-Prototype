# 43-v4-default-merge-main

Requires: 42-lake-natural-shores

Approval: owner, 2026-10-09 ~20:25 in chat, Queue decisions Q35 6 (a): "after the answers above land:
whole suite first, make map v4 the default, merge `surface-rebuild` into `main`". ROADMAP.md step 2.
Queued by the supervisor. This task is the ONE exception to PREAMBLE rule 4 ("never `main`"): it
merges into local `main`. NEVER push anything anywhere.

No performance measurement: ROADMAP.md step 3 (the engine phase) replaces the single milestone perf
check, and the merge happens whatever the number says (CLAUDE.md: skip a measurement whose every
outcome leads to the same action). No rig, no stream probe.

## Change 1 -- map v4 is the default (commit on `surface-rebuild`)
- `TerrainSpec` default `mapVersion` 3 -> 4 (`Assets/TerrainGen/TerrainSpec.cs` ~line 89, comment
  saying owner 2026-10-09 Q35 6a). `mapVersion 0 keeps the default` style tests
  (`WorldRecordTests.cs` ~line 76) follow the new default. v3 must stay reachable with an explicit
  `-mapversion 3` / `MAPV=3` and stay byte-identical (its hash tests keep passing with v3 named
  explicitly).
- Every runner/launcher whose default world is tg2 runs v4 when MAPV is unset (`run-play.sh` already
  does; check `run-tg2world.sh` and the other `run-*.sh` that take MAPV -- do NOT edit a run-*.sh that
  is running). The interactive Playground/arena worlds that do not use the tg2 map are unaffected.
- Update the "Current state" paragraph of `CLAUDE.md` in a few lines: terrain overhaul B1-B10 done,
  v4 default, merged to main on <date>, next = engine phase plan (ROADMAP.md step 3). Keep it short.

## Change 2 -- whole suite, then merge
1. `SUITE=all ./run-editmode-tests.sh` (via the Unity lock) on `surface-rebuild` after change 1:
   0 FAIL is required. If anything is red, fix it only if it is a test that pinned the old default;
   otherwise STOP_FOR_OWNER with the failure.
2. Check `main` is an ancestor of `surface-rebuild` (`git merge-base --is-ancestor main surface-rebuild`).
   If NOT, STOP_FOR_OWNER (do not resolve a divergence on your own).
3. The working tree has the usual uncommitted `Assets/Scenes/Playground.unity` fileID churn and many
   untracked screenshots. Do not delete or discard any of it. Stash only what blocks the checkout
   (`git stash push -- Assets/Scenes/Playground.unity`) and pop it after.
4. `git checkout main && git merge --no-ff surface-rebuild -m "Merge surface-rebuild: terrain
   overhaul B1-B10, map v4 default (owner Q35 6a, 2026-10-09)"`, then `git checkout surface-rebuild`
   (keep working on `surface-rebuild`; it now equals main plus nothing). Report both heads.
5. Compile check on `main`'s tree = the same tree; no second suite needed.

## Pictures
- `./run-play.sh`-equivalent start view on the default (no MAPV) world, one noon screenshot, to show
  the default is v4 (the B10 biomes / 37e+42 lakes are visible). Name its path in HANDOFF.

## HANDOFF
Merge commit hash, `main` and `surface-rebuild` heads, suite line, what changed in which scripts,
anything that still defaults to v3 and why.
