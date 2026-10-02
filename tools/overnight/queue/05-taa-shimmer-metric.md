Approved by the owner 2026-10-02 (TAA proposal, step 1 ticked:
https://claude.ai/code/artifact/6745ee4a-4af7-482f-b07d-822e46270918 -- do not
open it; this file is the brief). No visual change in this task.

Goal: a "before" shimmer number for NEAR terrain at the default 960 preset, so
TAA (task 06) can be judged by a number, not by eye.

The project already has a nudge test for the far band at 1 km: move the camera
a quarter of a pixel and measure how much the image changes
(`Assets/Game/PlaygroundCapture.FarSeams.cs`, `tools/farshimmer-report.py`;
grep "nudge"). Extend it to near terrain:
- poses: the tg2 eye-level poses (eye_level_1..3) and one walking-height view
  across a slope 30-80 m away (voxels smaller than a pixel beyond ~50 m at 960).
- metric: per pose, mean absolute luminance change and % of pixels changing by
  more than a fixed threshold between the base frame and quarter-pixel nudges
  (several nudge directions), split near (<50 m) / mid (50-200 m) / far by depth
  if the capture has depth; otherwise by screen band. Keep the far-band metric
  unchanged.
- a run mode or flag reachable from `run-tg2world.sh` (e.g. MODE=shimmer) that
  writes a small text report under Logs/.

Acceptance: EditMode 0 FAIL; build OK; run it TWICE on the same build and show
the numbers repeat (a metric that is noisy run-to-run is useless -- say how
noisy). Record the baseline in KNOWN_ISSUES.md or the TAA section of a doc the
next task can find (say where in HANDOFF). Commit.
