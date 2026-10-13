# 53-engine-6-cascade-caps

Approval: owner, 2026-10-09 -- engine plan approved (Q36), Q6 (a); this is ENGINE PLAN STEP 6
(`ENGINE_PHASE_PLAN.md` section 3 row 6, 8.13 T6). Queued by the supervisor 2026-10-10 (run before step 5:
it is small). Branch `engine-phase`. Requires nothing new (step 4 = task 49 done).

## Goal
Set the tg2 cascade tier pool caps (`EngineConfig.Tg2CascadeTierPoolCap`, today 320,000 / 96,000, "FIRST SIZING
2x") from task 49's measured PeakUsed, with headroom. No format change, no allocator change: caps only.
Task 49's numbers (`Logs/overnight/49_*.out`, HANDOFF in git `b708f92`), tg2 map v4:
| pool | cap now | peak poses | peak walk | peak coast (poolprobe) |
|---|---|---|---|---|
| tier 1 | 320,000 | 82,641 | 62,000 | 76,150 |
| tier 2 | 96,000 | 16,437 | 13,342 | 16,044 |
Target: cap = max peak x 1.25, rounded up to a tidy number (tier 1 ~104k, tier 2 ~21k). If the code needs a
power of two or a multiple of some block size, honour it and say which. 49 estimates about -200 MB (CPU+GPU).
Do NOT change the tier-0 brick pool (750,000) here: it is not a cascade tier; name it as a follow-up only.

## Steps
1. Change the caps; check what happens on exhaustion today (log line? refused bricks? visible holes?) and say so.
2. Fast EditMode suite green.
3. Evidence (memory counts only, no timing): `MAPV=4 MODE=poses EXTRA=-memledger BUILD=1 ./run-tg2world.sh`,
   `MAPV=4 MODE=walk EXTRA=-memledger ./run-tg2world.sh`, `MAPV=4 MODE=poolprobe EXTRA=-memledger ./run-tg2world.sh`.
   Pass: no cascade exhaustion in any run; ledger shows the drop in MB and the phys_footprint peak before -> after
   (49: poses 3,353, walk 3,027, coast 3,085 MB).
4. Look at 2 pose screenshots from the poses run (far terrain present, no holes) -- midday, sun behind camera if
   the pose set allows.
5. Disk is fine now (31 GB), but delete the runs' player-data folders once their numbers are in the log.

`## For the doc`: caps before -> after, MB saved, phys_footprint before -> after per run, exhaustion count (0).
