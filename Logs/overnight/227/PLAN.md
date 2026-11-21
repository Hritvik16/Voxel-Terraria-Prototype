# Task 227 -- what each outcome makes us do (written BEFORE the measured launch, 2026-11-17 03:40)
Measured: one warm-up (MODE=stream MOTION=walk ./run-tg2world.sh, discarded), then the cost split back to back,
day launch: walk_near (moving) + forest_eye + jungle_eye; arms FULL (-skyfillres 2), sfres1 (task 193's inline
path = before), sfres4, skyfill0 (sky fill off); the rig's own order F, then X X F per arm (ABBA).
- FULL saves >= 15 ms p50 vs sfres1 at walk_near AND its picture diff vs sfres1 is within ~2x the 219/221 noise floor
  (walk_near 0.9 / 0.03 %, forest 2.1 / 0.36 %, jungle 0.7 / 0.08 %; mean|d| / px>32) -> keep 2x2 as the default.
- sfres4 saves >= 5 ms more than FULL with diffs still inside ~2x the floor -> make 4x4 the default (state it).
- FULL saves < 5 ms -> the cost is not the ray count (history fetch, divergence of orphan rays, the main kernel's
  remaining work): STOP optimising, report the split, name the next suspect. No second performance change in this task.
- A diff well above the floor at a pose -> open the full-size pair; visible bleed at a trunk/ledge edge = a bug to fix
  (correctness, then re-shoot stills only); smoother distance noise without bleed = report as the expected change.
