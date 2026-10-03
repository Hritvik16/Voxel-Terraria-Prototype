## YOUR TASK: 13-fly-pose-no-voxels-check

Follow-up from task 10 (streaming baseline, 2026-10-03). Evidence only: no fix, no
behaviour change. Instrumentation/QA poses are allowed if they don't change what is
generated, uploaded or drawn.

Task 10's fly still pose drew NO voxels while fully loaded. The pose was
`-streamprobe -streammotion fly` at the spawn (4014.0, 868.8, 3999.6) m, looking
along (0.71, 0, 0.71) and 8 deg down. Its own diagnostic line said:
- the CPU store has solid ground at 805.7 m straight below the eye;
- that is inside the band (layers 62..68, 793.6-883.2 m);
- the mirror's origin is (297, 61, 296);
- deficit 0, pending 0, dirty 0.

Yet the LOD-tier view (6) was all background and the band view (9) had 0% voxel blue:
far F1 is nearest. Frames took 3.9 ms. Shots:
`StreamProbe/2026-10-03_072436_streamfly_*/still_*.png`. The poses run's elevated
poses DO draw voxels (`coast_from_sea_30m` 30% blue), so this is pose-specific.

Find out which case it is (cite the code):
- a bug, e.g. a tier 1/2 cascade or `_MaxRayDistance` / iteration-cap limit;
- or a designed boundary, e.g. the ray reaching the ground beyond what the
  64/128 m tiers hold.

Method: a still-pose sweep at that XZ and heading:
- eye height above the ground directly below: 2, 8, 16, 32, 48 and 63 m;
- pitch 8 and 30 deg down;
- band view and tier view at each, with % voxel pixels.

Report the threshold and its cause, add a KNOWN_ISSUES line, and stop for the owner
if it needs a fix. Shared Unity lock; EditMode 0 FAIL before committing.
