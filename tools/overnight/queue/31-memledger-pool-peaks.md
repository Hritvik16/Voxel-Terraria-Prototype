# 31-memledger-pool-peaks

Follow-up from 26c (low-risk evidence task, no owner decision: diagnostics only).

## Why
AMENDMENT_8_13_PALETTE_BRICKS.md §8 sizes savings from brick COUNTS, but no tg2 run has
ever logged a pool's `BrickDataPool.PeakUsed`: `MemoryLedger.cs` prints capacities only.
The tg2 cascade caps (320k / 96k, `EngineConfig.Tg2CascadeTierPoolCap`, "FIRST SIZING 2x")
look 3.5-5x the band need (26c histogram: 92k / 19k dense coarse bricks at the peak pose),
and the amendment's T4/T6 need a measured before-number.

## Do (one change)
- `MemoryLedger` (the `-memledger` report) also prints, per pool (tier 0, cascade tier 1,
  tier 2): Capacity, InUse, PeakUsed, PeakUsed/Capacity. No other behaviour change.
- One evidence run: `MAPV=4 MODE=poses EXTRA=-memledger BUILD=1 ./run-tg2world.sh`, then
  `MAPV=4 MODE=walk EXTRA=-memledger ./run-tg2world.sh`. Not a timed measurement (no
  frame-time claims), so no warm-up is needed.
- Fast EditMode suite before committing.

## Done when
HANDOFF: per-pool peak / cap table for the poses and the walk (log paths), and whether the
peaks agree with 26c's histogram counts (tier 0 ~473-499k, tier 1 ~92k, tier 2 ~19k).

## Do not
Change any cap, pool, allocator or engine code. No performance claims.
