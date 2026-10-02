# Overnight handoff

**05c-nearshimmer-eye2-wall-check: DONE. Not reproduced. eye_level_2 is an open valley in both `-noslope` runs.**

## What changed
- `KNOWN_ISSUES.md`, the NEAR-TERRAIN SHIMMER entry: recorded "NOT REPRODUCED" with both runs' paths. Committed
  together with this file. No code change.

## Evidence
- Run 1: `BUILD=1 MODE=shimmer EXTRA=-noslope ./run-tg2world.sh`. The build was OK (0 error CS, 0 Shader error).
  Report `Logs/nearshimmer_20261002_132001.txt`, shots `FarBand/2026-10-02_132546_nearshimmer/`.
- Run 2: the same command without BUILD. Report `Logs/nearshimmer_20261002_133003.txt`, shots
  `FarBand/2026-10-02_133144_nearshimmer/`.
- eye_level_2 (3454.77, 679.90, 4932.27), both runs: <50 m 17.7%, 50-200 m 21.2%, no hit 61.1%, **median hit 51.4 m**.
  The first run of task 05 (`FarBand/2026-10-02_111820_nearshimmer/`) had <50 m at 100%.
- I opened `eye_level_2_base.png` from both runs. They are identical: a brown/grey cliff fills the left third, and an
  open valley lies beyond it, with snow, a river and a ridge on the right. The sky is visible. There is no wall in front
  of the camera.
- Shimmer numbers repeat the task 05 baseline. eye_level_2 all is 9.26-9.27 / 13.25. The MEAN is higher
  (6.66 / 8.87) only because slope_walk was skipped.
- A side note on cause: task 05's first run ALSO differed at eye_level_1 (<50 m 56.1% / 50-200 m 1.7%, against
  46.6 / 15.7 in every later run). That run used uncommitted code from before `4d0d03c`. A change in the code is
  therefore a likelier cause than the order of the jumps, but this cannot be checked now.
- Memory peaks were 3270 and 3060 MB. Both are over the 3 GB cap, which is the existing open issue and not new.
- EditMode was not run because this task changed only docs.

## State
- `Assets/Scenes/Playground.unity` was already modified before this task, and the BUILD=1 step regenerated it.
  It is not committed, as before.

## Next step
- Task 06 (TAA) can go ahead. Nothing in this task blocks it.
