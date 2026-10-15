# 93g-engine-14d4-cpu-brick-cap-ledger

Requires: 93f-engine-14d3-gpu-brick-pool

Approval: owner, 2026-10-15 in the supervisor chat: Amendment 8.15 Q1-Q10 all (a) -- Q1 (a) NEAR 128 m, Q2 (a), Q7 (a)
budgeted debris, Q10 (a) order -- recorded in `tools/overnight/done/90a-amendment-8-15-simulation-reach.md` "## Owner
answer"; adopted by task 90b. Row D4 of `AMENDMENT_8_15_SIMULATION_REACH.md` §11 (§2 item 4, §9). Last task of
ENGINE_PHASE_PLAN step 14 redone. Branch `engine-phase`.

## Why
After 93f the GPU no longer mirrors the whole CPU pool, so the CPU 0.1 m brick cap can be set from what 128 m of live
world actually needs. Today `EngineConfig.TG2_BRICK_POOL_CAP` = 750,000 (366 MB CPU; `EngineConfig.BRICK_POOL_CAP` =
500,000 is the Phase 4 rigs' cap -- do not change that one). Task 55 measured a peak of 379,527 bricks over 21 x 21
columns (the 128 m load radius). 8.15 §2 item 4: cap = measured peak x **1.5** (not 8.14's x 1.25: players build and
blast), ~570 k, -88 MB (est.). §3.6's LRU valve line stays at 0.85 x cap (`BRICK_POOL_HIGH_WATER_FRACTION`).

## Change (one change: the tg2 CPU brick cap from a measured peak, plus one memory ledger line)
1. Measure `BrickDataPool.PeakUsed` with shells on in play over the 93e/93f poses plus one `MODE=walk` circle (counts,
   not timing). New cap = max(measured peak, task 55's 379,527) x 1.5, rounded up to a 10 k multiple. If that is
   ABOVE 750,000, keep 750,000 and write why in HANDOFF (no raise without the owner).
2. Set `TG2_BRICK_POOL_CAP` (check the scene builder / any `[SerializeField]` that overrides it -- a changed default can
   silently do nothing; the tell is an identical number across two builds). Rigs keep their caps.
3. One memory ledger line (in the existing `-memledger` / `*_mem_peak_breakdown.txt` output) that lists step 14's
   rows together: CPU brick pool cap / peak, GPU body pool cap / peak, mirror box dims, tiers 1-2 allocated yes/no,
   process peak MB vs the 3,000 MB cap. Counts and MB only.
4. Frozen APIs untouched; the valve fraction unchanged; never a lock in the streaming path.

## Acceptance
- Tests: the cap value and the valve line (0.85 x cap) asserted; a pool-pressure test at the new cap still evicts by
  §3.6's LRU and loses no edited chunk (D.1 saved first). Own tests while iterating, `SUITE=all` once at the end.
- `run-acceptance-rig.sh` gates C and D green (pass/fail only, PREAMBLE rule 10).
- Memory: peak process MB in play with shells on, before -> after, same poses; and the step-14 total against today's
  2,999 MB (task 85). 8.15 §9 estimates ~2,370 MB after D1-D4 (est.); report the measured figure beside it.
- No stills needed (nothing visible changes); say so.
- HANDOFF `## For the doc`: CPU cap before -> after, measured peak, process peak MB 2,999 (task 85) -> after D1-D4,
  the ledger line. Note for the supervisor: step 16 (streaming milestone) and Amendment 8.15 row L1 (look-ahead) come
  after this; L1 must re-check the cap (its ahead lobe adds residency, 8.15 §13.1).
