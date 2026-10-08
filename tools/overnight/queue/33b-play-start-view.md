# 33b-play-start-view

Approval: owner, 2026-10-08, Queue decisions Q27 answer (a): "Start at noon with a level
camera, only in ./run-play.sh." Rigs, capture runs and old screenshots stay untouched.
No performance arms.

## Background
Task 32d (`30fff51`) made `./run-play.sh` start on the nearest dry column (eye in air).
But the first view is dim: the start keeps the test arena's camera, pitched 18 deg down
at the slope, under the low start-up light (`Screenshots/Play/start_v4_32d.png`).
In game, M = noon, N = golden hour (`Playground.RegisterDefaultToys`).

## Do (one change)
- In the interactive tg2 start only (the same path as 32d's `SettleStartPose()`, i.e. not
  a capture run): set the time of day to NOON (the same state the M key sets) and level the
  camera pitch to 0 (keep the yaw, or face the most open direction if that is trivial).
- Capture runs, rigs, `TeleportToArena` via SendMessage and the old (non-tg2) world keep
  today's behaviour. The clock keeps running as today after the start (do not pause it).
- If a run-play.sh flag is the cleanest way (e.g. run-play.sh passes `-starttime noon`),
  that is fine; say which in HANDOFF.

## Acceptance
- `SHOT=45 ./run-play.sh` (v4) and `MAPV=3 SHOT=45 ./run-play.sh`: the shots show the
  landscape in full daylight with the horizon roughly mid-frame; the `-playshot` line says
  `(air)`. Caption both shots in HANDOFF (what is in frame, where the horizon sits).
- Fast EditMode suite green.
