# Task 229 -- what each outcome makes us do (written BEFORE the measured launches, 2026-11-17)
Code reading: with the flood ON (play) TorchFloodPool.Ready is true even with NO torch (an empty pool uploads), so the
renderer draws CSMainFarShadeFlood every frame, and the twin is not a diet-2 kernel (RaymarchFeature: diet2 needs
!floodFarTwin). New rule (FloodTwinRule, -floodnear 1 default): the twin only while a lit flood chunk or an active
light's range reaches the tier-0 window (+1 brick); otherwise the plain kernel = the same picture by construction.
Measured: one warm-up (MODE=stream MOTION=walk ./run-tg2world.sh, discarded), then back to back:
DAY  walk_near, noon, play flags, 432 rows; order F, then X X F per arm (ABBA). F = rule ON (after).
     arms floodnear_off (= before: the twin), flood_off (no flood, diet 2), floodd2_off (no flood, no diet 2).
     Split: twin total = floodnear_off - flood_off; diet-2 loss = floodd2_off - flood_off;
            flood code in the twin = floodnear_off - floodd2_off; flood ON outside the kernel (CPU tick) = F - flood_off.
     + -coststillraw -costfloodpair: raw2 vs fnflip vs fnback (rule flipped and back, 10 frames each).
NIGHT torch_camp (28-32 torches), arms tf_on (rule on) and tf_twin (rule off); both must log `flood twin`.
Outcomes:
- F within ~2 ms of flood_off AND >= 3 ms under floodnear_off, fnpair diffs inside the raw/raw2 control -> keep the rule
  ON (play default), done.
- F ~ floodnear_off (< 3 ms saved) -> the twin is not the cost; report the split, name the suspect, no 2nd change here.
- F more than ~3 ms over flood_off -> a cost outside the kernel stays (CPU tick, uploads): report it, no change here.
- fnpair differs beyond the control, or tf_on logs `flood skip` at torch_camp -> rule bug: fix (correctness), re-shoot
  stills only; if not fixable, default OFF + STOP_FOR_OWNER.
- torch_camp tf_on vs tf_twin differ by more than the pass-to-pass spread -> investigate (same kernel expected).
