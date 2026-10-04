# 31-detailspeed-walk-horizon-shot

Requires: 24-detail-radius-at-speed

Queued by task 24 (low-risk QA follow-up, no owner decision, no performance change).

## Why
Task 24 asked "is the voxel/far hand-over visible at walking speed?" The stream probe's
walk shot (`moving_beauty/band.png`, `-streammotion walk`) faces a slope a few metres away:
its band view is 100% voxels in both arms, so it cannot answer. With `-detailspeed 1`
walking at 5 m/s puts the hand-over at R_d = 124.8 m (a camera-centred square), versus
the residency edge (128-140.8 m) with the flag off.

## Do (QA only)
1. Give the stream probe's mid-motion shot an optional look override (e.g. `-shotyaw`,
   `-shotpitch`, default = today's behaviour), or pick a walk-circle point whose
   heading already looks over open land past ~130 m. Default output must not change.
2. Walk arms, v4, AC, warm steady state (one discarded warm-up, then OFF ON ON OFF):
   `MODE=stream MOTION=walk MAPV=4 EXTRA="-detailspeed 0|1 <look flags>"`.
3. Contact sheet: moving_beauty and moving_band, OFF on top, ON below, plus a 2x crop
   around where the band view's blue (voxels) meets green (far F0). Caption every image
   with what to look at. Describe what is in frame before concluding.
4. Report: is the hand-over line visible in beauty at walking speed? Any seam, gap,
   colour step or floating geometry at the square's edge?

## Do not
- Change the detail radius, the filter, the shader or the streaming. Measure and report.
