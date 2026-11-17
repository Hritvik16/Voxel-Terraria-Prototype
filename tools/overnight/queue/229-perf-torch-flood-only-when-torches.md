# 229-perf-torch-flood-only-when-torches
Model: opus
Requires: 228-perf-sun-shadow-coarse-far
Approval: OWNER 2026-11-16 (Q-AN doc): "Torches are too important to turn off." Task 221 found that torch flood off changes NOTHING by day, yet 212 measured the flood twin at 27.8 ms at walk_near, noon, with no torches in view.
Performance task (timing allowed, warm steady, ABBA, outcomes written first).
## Do
1. Find out what the 27.8 ms consists of when no torch is in range (the flood build, the per-pixel lookup, or both) with a split (capture only, no guess).
2. Make the cost scale with the torches that exist near the camera: zero cost with none, no change in look with some (pixel-identical at torch_camp, task 221's A arm). Exact condition, no terrain tuning; adversarial: one torch just inside and just outside the range, a torch placed and removed.
3. Report ms at walk_near (day, none) and at torch_camp (28 torches) before/after, memory (rule 11). Switch for A/B. Fast suite. STOP_FOR_OWNER on frozen API / layout change.
