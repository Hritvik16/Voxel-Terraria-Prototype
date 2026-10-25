# 145-bug-cascade-tier-caps

Model: opus

Approval: owner, 2026-10-25, doc Q93 answer (a): raise the cascade tier pool caps to measured peak x 1.25 (task 53's rule).
Source: `tools/overnight/blocked/140-bug-brickdatapool-exhausted.HANDOFF.md` (peaks tier 1 176,516, tier 2 32,505 after task 122's biome blend).

## Do
1. Set the tg2 cascade tier caps to tier 1 221,000 and tier 2 41,000 (`EngineConfig.Tg2CascadeTierPoolCap` and its comment, or wherever
   task 140's `-tier1cap/-tier2cap` defaults resolve; read the code first). Keep the caps data-derived: write the peak x 1.25 rule and
   the two measured peaks in the comment. No other memory budget changes.
2. One `MODE=stream MOTION=walk ./run-tg2world.sh` shells-OFF run (footprint only, not timed): exceptions in Player.log 0, cascade pool
   lines (cap / peak / still exhausted), peak footprint MB vs 140's 2,206 MB baseline. If peak footprint passes the 3 GB cap or any
   exception remains: STOP_FOR_OWNER with the numbers.
3. Update any test or doc that quotes the old caps. Fast EditMode suite once at the end.

## Deliverable
HANDOFF `## For the doc`: caps before -> after, exceptions 2,777 -> 0 (log paths), footprint MB. TASKS.md: extend the Task 140 paragraph.
Not a performance task: no timing arms.
