# 214-defaults-trees2-on

Model: opus
Requires: 213-look-trees2-denser-forest

Approval: OWNER, 2026-11-15, Queue decisions doc Q-AJ answer (a): the new trees become the default in every world.

## Do
1. Make `-trees2 1` the default (play, rigs, capture, EditMode generator defaults as other default flips did, e.g. 195a / 201 / 209b:
   copy their pattern). `-trees2 0` = today's trees byte for byte. Update `TodayTrees_Pinned` and any test that assumed the old default
   so they pass the flag explicitly; do not loosen any assertion.
2. Memory (rule 11) from the launches you already do, default vs `-trees2 0`: process peak (must stay under 3,000 MB), GPU body pool
   peak and refusals (not up), CPU pool peak. Over budget = STOP_FOR_OWNER.
3. Two pictures of the default world (forest eye, savanna 100 m), looked at.
4. Fast suite before, WHOLE suite before done. TASKS.md line + the CLAUDE.md "Commands" paragraph only if it states a tree default.

## End
Done with HANDOFF + `## For the doc`.
