# 125-look-sea-horizon-line

Model: opus

Approval: supervisor, 2026-10-23, "going ahead unless you object": task 121's HANDOFF "Open" item (look task 114 defect on the hard
sea/sky line; owner said fix all of 114's defects, Q64-70 answer). Reversible, look only; no terrain-generator, API or layout change.

## Change
Task 121 found the hard sea-horizon line remains (noon, high pose: 128 -> 112 levels across it; old 84 -> 75) and suspects it is the
WORLD's edge (camera x 3,857 m looking +x), NOT verified. First verify: shoot the same pose looking toward the world interior vs toward
the edge, and say which it is. If the world edge: pass the world extent to the far-shade shader and fade the sea/land to the horizon
haze colour approaching the edge (flag `-edgefade 0` = old). If not the edge, find the real cause from the stills and fix that.
Look task (rule 10): tests, stills (noon + golden, old vs new, captions saying what to look at), no timing. Fast suite once.
HANDOFF + `## For the doc`, TASKS.md paragraph. Do NOT change the dusk sky strength (owner's taste, separate question).
