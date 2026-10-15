# 93-bug-fluidprobe-tg2-water-frozen

Follow-up found by task 91 (no owner decision needed: a bug fix). Branch `engine-phase`.

## Bug
`MODE=fluidprobe ./run-tg2world.sh` (tg2, eye_level_1 pose, task 07's water box over a dug hole + lava box):
the placed water NEVER moves -- 20 s after placement `water in box 1029, elsewhere 0`, readback `ops total 0`,
GPU dump `promote.seen=0`, lava `intent.awake=784 intent.no_destination=784`. Same with `-fluidhz 0` (the pre-91
tick), so it is NOT the fixed-rate scheduler. On 2026-10-03 (task 09) the same probe moved water (374 in box at
+45 frames). The arena control (`PROBEWORLD=""`) and `MODE=poolprobe` (tg2 lake + coast) DO move water today.
Logs: `Logs/tg2world_20261015_112947.txt` (fixed) and `_113531.txt` (legacy).

## Task
Find why water/lava placed at the fluidprobe's tg2 site is never promoted / finds no destination (candidates: the
mirror there -- MIRROR_CHUNKS_Y / shells hand-over / RiverLipAsWater / the wake path after task 54's awake-only tiles;
check the GPU mirror's material under the boxes vs the CPU store). Fix it inside the existing design, add an EditMode
test if the cause is testable on the CPU side, and show `MODE=fluidprobe` moving water (CPU counts + 1-2 stills).
Not a performance task. Fast suite while iterating, the required suite once at the end.
