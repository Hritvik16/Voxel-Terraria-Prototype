# 93-bug-fluidprobe-tg2-water-frozen: DONE -- the bug was in the PROBE, not the engine; the probe is fixed and the water moves

## Cause
On map v4, the ground 8-20 m ahead of eye_level_1 (looking towards the peak) is higher than the eye. Task 07's
straight-ahead search found no open ground there, logged "no open ground found ahead; using a guess", and put
the dug hole, the water box and the lava box INSIDE the hillside. A census of the CPU store before placement:
water box **1029/1029 solid**, lava box **784/784 solid**. So every fluid voxel was walled in by rock:
`intent.no_destination` = 1029 (water) / 784 (lava), and the GPU sim correctly did nothing. On 2026-10-03 (map v3)
the same pose had open ground ahead, which is why task 09's run moved. The scheduler, mirror, shells, RiverLipAsWater
and awake-only tiles are all innocent (the tiles were acquired, wakes dispatched, the box was inside the mirror window).

## What changed (commit 981b985)
- `Assets/Game/PlaygroundCapture.FluidProbe.cs`: logs the task-07 site and its solid-voxel count in both boxes,
  then tries 8 headings (0, +-45, +-90, +-135, 180 deg) x the same 5 distances and uses the first site whose two
  boxes are ALL AIR in the store (`SolidIn`). If no heading works it says "NO OPEN SITE" and falls back to the old
  site. The arena (non-tg2) path is unchanged.
- `KNOWN_ISSUES.md`: one sub-bullet under "FLUID IN TG2 DOES SIMULATE".
- No EditMode test: the cause is in the rig's site choice (Assembly-CSharp, which the test asmdefs can't reference),
  not in engine CPU code. The probe's census line now catches it at run time.

## Evidence
- Probe run: `BUILD=1 MODE=fluidprobe EXTRA="-fpscap 30 -fluidstrip" ./run-tg2world.sh` ->
  `Logs/tg2world_20261015_152136.txt`; report copy `Screenshots/93/fluidprobe.txt` (committed).
  "task-07 site (no open ground ahead, a guess): solid voxels in water box 1029/1029, in lava box 784/784";
  "site: turned 90 deg ... both boxes all air". Dug 9327 voxels (not 14147: the hole is now partly in open air).
- CPU store, water in box / elsewhere nearby: +1 s 461/568, +2 s 356/673, +4 s 164/865, +8 s 0/1029,
  +20 s 0/997. Lava in box: 784 -> 375 (+2 s) -> 130 (+20 s). GPU: commit.APPLIED 878 -> 1029, no_destination 0.
- Before (task 91's run, `Logs/tg2world_20261015_112947.txt`): water in box 1029 at every point up to +20 s, ops total 0.
- Fast suite: `PASS 732 FAIL 0 SKIP 14` (`Logs/overnight/93-fast.out`). The Mac was on battery at 10-13% at the
  start of the run (charging later); this doesn't matter here (no timing).
- Stills looked at (copied to `Screenshots/93/`, not committed):
  - `Screenshots/93/strip_2s.png` -- centre-left: the blue water box breaking up as it falls onto the slope;
    the orange lava column to its right still stands whole.
  - `Screenshots/93/strip_8s.png` -- the water is gone from the box and sits as a small blue pool in the dug hole
    on the slope (centre-left); the lava column has slumped into a rounded heap.

## Not done / notes
- Other probes that place things relative to eye_level_1 (`-taarisk`'s fluid setup, `PlaygroundCapture.TaaRisk.cs`)
  still use the old straight-ahead guess on map v4, and probably bury their boxes too. Not touched (out of scope).
- No follow-up queued.

## Next step
Nothing for the owner. The queue goes on (93a...).

## For the doc
Task 93: tg2 "water never moves" was the probe placing its boxes inside a hill on map v4, not an engine fault.
- Solid voxels in the task-07 water box / lava box before placement: 1029/1029 and 784/784 (`Logs/tg2world_20261015_152136.txt`)
- Water still in the box at +8 s: 1029 (`Logs/tg2world_20261015_112947.txt`) -> 0 (`Logs/tg2world_20261015_152136.txt`)
- Water ops applied (GPU commit.APPLIED): 0 -> 1029; intent.no_destination 1029 -> 0
- Lava still in the box at +20 s: 784 -> 130
- `Screenshots/93/strip_2s.png` -- centre-left: the water box falling apart 2 s after it was placed, lava column to its right.
- `Screenshots/93/strip_8s.png` -- the same view at 8 s: the water is in the dug hole as a small pool, the lava has slumped.
