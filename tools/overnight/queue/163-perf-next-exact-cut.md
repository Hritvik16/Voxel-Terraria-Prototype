# 163-perf-next-exact-cut

Model: opus

Approval: owner, 2026-11-01, doc Q102 "a for all": order B (161) -> D or C1, picked by 161's arms -> the other -> E -> milestone walk on AC;
C1's GPU scratch 3-6 MB approved; E counts as exact. A PERFORMANCE task, ONE change.

## Requires
161 done with its warm AC arm set (read `tools/overnight/HANDOFF.md`). If 161 did not produce the arms, STOP_FOR_OWNER and say so.

## Do
Pick per `PERF_EXACT_CUTS.md` section 4 step 1 and 161's numbers: `skyprobe_off` >= 2 ms -> **D** (incremental roof pyramid, then roof skip ON,
section 2 row D); else **C1** (shadow continuation out of the big kernel, row C1; first check sun visibility enters the shading linearly). Write
which and why at the top of the handoff. Build it behind a flag; raw stills lean vs today **0 px** at walk_near / walk_open / cliff1 (any non-zero
px outside animated water = stop, do not measure). Fast suite once.
Measure on AC only ("Power first" as in 161): warm-up discarded, then cost split at `-renderrows 432` ABBA new vs today, walk_near / walk_open.

## Decision table
- >= 1 ms beyond noise at either view and 0 px: ON in play by default.
- ~0: keep behind its flag, say so.
- Either way end normally; the next task is the OTHER of D / C1 (the supervisor queues it). STOP_FOR_OWNER only for a non-exact result or memory
  beyond the approved 3-6 MB.

## For the doc
Before/after ms per view with noise; memory added; 0 px proof; which cut is next.
