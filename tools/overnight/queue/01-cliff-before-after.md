Approved by the owner 2026-10-01 ("make sure to QA this and fix it").

Goal: the missing BEFORE shot for the terrain-aware band placement (commit
b63f941). The AFTER run is `FarBand/2026-10-01_213438_world` (cliff_foot_2:
band 46 -> 48, all voxels to the frame top). The `-centredband` re-run was
closed after a minute, so there is no BEFORE.

Steps:
1. `MODE=poses EXTRA="-centredband" ./run-tg2world.sh` (no BUILD needed if
   `Builds/Playground.app` is newer than the last commit touching Assets/;
   otherwise BUILD=1). It takes the Unity lock itself.
2. From its report, record cliff_foot_1..3 rows (band, deficit, dense).
3. Look at `cliff_foot_2_golden.png` and `cliff_foot_2_golden_band.png` from the
   new run and the AFTER run. Describe what each shows: share of far band
   (green/yellow) vs voxels (blue) on the cliff, streaks, stepped edges. If the
   camera is inside rock (pure black frame), say so -- do not compare.
4. Add one line with the result to the TERRAIN-AWARE BAND PLACEMENT entry in
   `KNOWN_ISSUES.md`, replacing "The A/B shot with -centredband is MISSING".
5. Commit that doc change. No code changes in this task.
