# 109-merge-engine-phase-main

Model: opus

Approval: owner, 2026-10-22 in chat: "Merge engine-phase into main this week? -> Yes, first task". Queued by the
supervisor 2026-10-22. This task is the ONE exception to PREAMBLE rule 4 ("never `main`"): it merges into local
`main`. NEVER push anything anywhere.

No performance measurement: the merge happens whatever a number says (CLAUDE.md: skip a measurement whose every
outcome leads to the same action). No rig, no stream probe, no screenshots.

## Change -- whole suite, then merge
1. `SUITE=all ./run-editmode-tests.sh` (via the Unity lock: `tools/with-unity-lock.sh`) on `engine-phase`:
   0 FAIL is required. If anything is red, STOP_FOR_OWNER with the failing tests and the log lines (do not fix
   product code in this task; a test that only pinned an old default may be updated, say which).
2. Check `main` is an ancestor of `engine-phase` (`git merge-base --is-ancestor main engine-phase`; true on
   2026-10-22, 303 commits ahead). If NOT, STOP_FOR_OWNER (do not resolve a divergence on your own).
3. The working tree has uncommitted scene fileID churn (`Assets/Scenes/*.unity`) and untracked files. Do not
   delete or discard any of it. Stash only what blocks the checkout (`git stash push -- <those paths>`) and pop
   it after.
4. `git checkout main && git merge --no-ff engine-phase -m "Merge engine-phase: LOD shells, far edit ops,
   8.15 D1-D4/E1/C1/Q1, fluid fall + fixed tick, gameplay steps 1-10 + BLD1-2 + RX1 (owner, 2026-10-22)"`,
   then `git checkout engine-phase` (keep working there; it now equals main). Report both heads.
5. Update the "Current state" paragraph of `CLAUDE.md` in at most 4 lines: engine-phase merged to main on
   <date>, the suite line from step 1, next = the quality pass the owner set on 2026-10-22 (performance,
   lighting, world gen, visuals before populating the world; crafting/core loop after). Commit on
   `engine-phase`, then fast-forward `main` to it (`git checkout main && git merge --ff-only engine-phase &&
   git checkout engine-phase`).

## HANDOFF
Suite line, both heads, anything stashed/popped, anything that did not go as written.
