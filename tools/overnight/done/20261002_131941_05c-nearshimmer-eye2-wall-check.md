Requires: 05-taa-shimmer-metric

Follow-up added by task 05 (low-risk evidence run, no code change, no owner
decision needed). Run it BEFORE task 06 changes the renderer if possible.

Question: in task 05's FIRST -nearshimmer run, eye_level_2 looked into a wall
0.5-1.4 m away -- in the frame AND in the CPU ChunkStore's depth
(`FarBand/2026-10-02_111820_nearshimmer/eye_level_2_base.png`). Every later run
at the same eye saw an open valley (median hit 51 m). The only difference in
order: the first run jumped eye_level_1 -> eye_level_2 directly; later runs
searched slope candidates first. If the wall comes back, the CPU store held
stale or wrong content after a 1.4 km jump -- a correctness bug.

Do: `BUILD=1 MODE=shimmer EXTRA=-noslope ./run-tg2world.sh`, then once more
without BUILD. For each run, read the eye_level_2 line (median hit) in
`Logs/nearshimmer_<run>.txt` and Read `eye_level_2_base.png`.
- Both open valley (median hit ~51 m): record "not reproduced" in the
  KNOWN_ISSUES near-shimmer entry. Done.
- A wall: record it there with both runs' paths, then STOP_FOR_OWNER (do not
  try to fix streaming in this task).

(Supervisor 2026-10-02: renamed from 09 to 05c so it runs before 06, as asked above.)
