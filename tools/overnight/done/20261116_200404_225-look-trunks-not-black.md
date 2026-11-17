# 225-look-trunks-not-black
Model: opus
Approval: OWNER 2026-11-16 (Q-AN doc answer): "Why are the tree trunks pitch black in all of them?" and "lighting is the main issue". Look task, no frame timing.

## Why
Task 221's sheets: with sky fill ON (play defaults) jungle trunks are already near-black, forest trunks dark grey; with sky fill off, forest trunks go black. Lay of the Land's shade is warm/green bounce light, not black.
## Do
1. FIND the cause, do not guess: for jungle_eye and forest_eye (task 219/221 poses, High, play flags, 432 rows) log the actual shade colour of a trunk voxel face step by step (albedo, sun term, sky-fill term, ground-bounce term, shadow, final).
   Candidates: trunk albedo too dark; sky fill sees a blocked sky under a canopy (sampled at too coarse a resolution so tree columns look solid); side faces get no bounce; shadow term double-applied.
   Name which one with numbers. Prove by turning only that term off/on (capture only).
2. FIX exactly (no tuning to these poses: the fix must be derived from the term that is wrong and proven on an adversarial case, e.g. a trunk in open sun, one in a dense stand, one in a cave mouth).
3. Pictures: before/after at jungle_eye, forest_eye, walk_near plus a night torch pose; pixel diff; side-by-side with `Screenshots/222/side_by_side.png` right column for the shade look. Memory rule 11 in the ledger (no new storage expected).
4. Fast suite. STOP_FOR_OWNER only if a frozen API or the memory layout would change.
