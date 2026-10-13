# 49-engine-4-memledger-pool-peaks

Approval: owner, 2026-10-09 -- engine plan approved (Q36), Q6 (a): memory reclaim (steps 4-6) follows
steps 1-3. This is ENGINE PLAN STEP 4 (`ENGINE_PHASE_PLAN.md` section 3, row 4). It revives the parked
task `dropped/31-memledger-pool-peaks.md` (read it: its numbers and run lines are copied below).
Queued by the supervisor 2026-10-10. Branch `engine-phase`. Task 48 is blocked on the owner's upscaler
pick; this task does not depend on it (do NOT change any upscaler or default).

## Goal
The `-memledger` report (`MemoryLedger.cs`) also prints, per pool: Capacity, InUse, PeakUsed,
PeakUsed/Capacity. Pools: tier-0 bricks, cascade tier 1, cascade tier 2 (tg2 caps 320k / 96k,
`EngineConfig.Tg2CascadeTierPoolCap`, "FIRST SIZING 2x"), AND the fluid tile pool (1,024 tiles; task
39 "pool full") with its refused-tile count. These numbers set the caps in plan steps 5 (fluid tiles
for awake fluid only) and 6 (cascade caps). No other behaviour change: no cap, pool or allocator edits.

## Steps
1. Add the per-pool counters/prints. Fast EditMode suite green.
2. Evidence runs (memory counts, NOT timing: no warm-up, no frame-time claims):
   `MAPV=4 MODE=poses EXTRA=-memledger BUILD=1 ./run-tg2world.sh`, then
   `MAPV=4 MODE=walk EXTRA=-memledger ./run-tg2world.sh`, plus one coast pose (`MODE=poolprobe`) for
   the fluid pool.
3. Compare with 26c's histogram (tier 0 ~473-499k, tier 1 ~92k, tier 2 ~19k dense coarse bricks at the
   peak pose) and say whether they agree.
4. Disk is at 97% (6.4 GB free): do not keep new raw screenshots; delete any temporary player data the
   runs create once their numbers are in the log.

## Report for the owner (`## For the doc` in HANDOFF)
A table: pool | capacity | peak used (poses) | peak used (walk) | peak/capacity | MB if the cap were
set to peak x 1.25. Plus the phys_footprint peak (MB) for each run and the fluid refused-tile count.
No images needed (say so).
