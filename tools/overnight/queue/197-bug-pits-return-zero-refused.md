# 197-bug-pits-return-zero-refused

Model: opus

Approval: SUPERVISOR, 2026-11-13 -- bug follow-up of task 194 (owner-approved bug fix, default ON); no design choice.
Never-idle rule: owner-independent work behind 196.

## The problem (task 194b's "Noticed, NOT changed" 1)
`Screenshots/194b/sheet_jungle_sf0_before_after_after2.jpg` (left before, middle after, right the after player again):
the MIDDLE panel at 194's own `jungle_high` pose shows chunk-aligned RECTANGULAR PITS again -- flat grey floors, straight
walls, brown dirt rims, no grass or trees -- while the left and right panels show forest. 1 of 3 launches with the SAME
code. 194b's `[refused]` line for that shot said 0 chunks refused at that moment, so 194's refused-chunk hand-over
(`_Tier0Refused` only while GpuPoolRefusedNow > 0) was not active. Either the pits have a second cause, or the refused
state was cleared (count back to 0) while tier 0 / the mirror still held the chunk as air.

## Do
1. Reproduce first: the `jungle_high` pose (`Logs/overnight/194_proof2.sh`, 194b's capture setup, `-trees 1`, native
   1080, sky fill 0), several launches in ONE session of launches until a pit shows (budget: <= 5 launches). Log the
   `[refused]` line, the pool fill and the residency/refused bitmap state of the pit's columns at the shot.
2. Find the cause from that evidence (trace the actual state: which tier drew the floor, what the column's
   resident/refused bits were, whether the chunk's body was ever uploaded). No guessing; no pool-size change.
3. Fix it exactly (the hand-over must cover every way a column ends up as air in tier 0 without its real body),
   with an EditMode test that reproduces this second path like `RefusedHandOverTests` does for the first.
4. Proof: the same pose over 3+ launches shows no pits; one other 194 pose unchanged. Pictures before/after with
   captions; a `viewer.html` for the full-size images.
- If the cause turns out to need a pool/eviction/layout change: STOP_FOR_OWNER with the evidence and options.
- Fast suite once at the end. TASKS.md paragraph. No timing arms (rule 10).

## End
Done with HANDOFF + pictures, or STOP_FOR_OWNER as above.
