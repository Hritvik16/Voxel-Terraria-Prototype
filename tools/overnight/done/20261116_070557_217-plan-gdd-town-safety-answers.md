# 217-plan-gdd-town-safety-answers

Model: opus

Approval: OWNER, 2026-11-15, Queue decisions doc Q-AK: "recommended for all Qs" = C1 (a), C2 (a), C3 (a), C4 (a), C5 (a). **PLAN ONLY. No code.**
This task may edit ONE Claude Doc, the "Game design" doc https://claude.ai/artifact/BprGXbxRr6W5MPdDAXxYp8 (allowed here explicitly), plus
the repo copy `GAME_DESIGN.md`. Read task 211's HANDOFF/commit 69c5cc4 for the section it wrote ("Town safety by NPC behaviour").

## The answers (the first option of each question in task 211's list)
- C1 (a): lights, closed rooms and people each lower danger within a radius, all on ONE danger map (~0.55 MB, 1 byte per 8 m cell on Medium).
- C2 (a): a resident leaves when there is no valid room (door, bed, light), or danger at the home stays above a limit for a few in-game days;
  one check per resident per day (wrecking a town empties it, so edits react for free).
- C3 (a): guards are ordinary residents with one more rule: at night walk between the town's lights and fight what spawns; one per few residents.
- C4 (a): a settlement steps up to town and city by resident count alone, the same for the player's and generated ones (e.g. 5 / 15 / 40);
  each step brings new NPC kinds (merchant, trainer, guard captain).
- C5 (a): no claimed area: rooms and residents within ~64 m count as one settlement.

## Do
1. Move the "Town safety" rows to Decided (owner, 2026-11-15, Q-AK), in the doc and `GAME_DESIGN.md`; update the "How residents react to edits"
   Leaning row (now Decided through C2); fix A6's "inside your claimed area" wording (C5).
2. Turn the section into short rules + the "missing pieces" table with a build order (what must exist before character work: danger map,
   room detection, NPC day routine, spawn rule). No numbers invented beyond the answers; mark any estimate as an estimate.
3. Add to the placement checklist: danger-map memory (~0.55 MB), the 64 m settlement rule.
4. HANDOFF `## For the doc`: what changed (short list) and any NEW questions (recommendation first), printed so the supervisor can copy them.
- No Unity, no tests, no memory cost.

## End
Done with HANDOFF.
