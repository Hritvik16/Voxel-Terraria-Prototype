# 115-perf-stutter-isolation

Model: opus

Approval: owner, 2026-10-22 (chat, Q69 answered "stutter first"): PERF_30FPS_PLAN.md row 10 / section 3 step 5, moved to
first. This is a PERFORMANCE task (measurement allowed, PREAMBLE rule 9). Owner context: "frame rate is horrible"; 540 rows
are the minimum (Q64 (c): no rows below 540), shells stay ON (Q66 (a)).

## Goal
Name what causes the walk's slow tail: p99 99 ms, 72-78% of frames > 33 ms (task 110, `Logs/tg2world_20261022_133936.txt`).
Separate SPIKES (single frames > 50 ms: hitches from CPU work, GC, uploads, admission, saves, shader/pipeline stalls, readbacks)
from a uniformly slow GPU frame (steady 35-45 ms views). Report both shares.

## Method
- Warm-up launch first (`MODE=stream MOTION=walk ./run-tg2world.sh`, discarded; AC only, check `pmset -g batt`).
- Per-frame log on `MODE=walk` (play settings: shells ON, FSR 3, 540 rows): for every frame > 50 ms record frame index, total,
  main-thread CPU vs GPU (or present wait), and what ran that frame (chunk admits/uploads, generation completions, evictions/saves,
  shell fill, fluid tick, far-band rebuilds, GC.CollectionCount change, HUD readback). Reuse `FrameGapProbe` / existing counters
  where they exist (KNOWN_ISSUES "WHILE THE CAMERA MOVES, 8-10% OF FRAMES EXCEED 33 ms"); a new log flag is fine (OFF by default).
- Two walks back to back (3 min each); report the histogram (<= 33, 33-50, 50-100, > 100 ms) and, for > 50 ms frames, the
  ranked co-occurring causes with counts.
- If ONE subsystem accounts for most spikes, confirm it with ONE disable arm (existing flag) and report the frame histogram with
  it off. Do NOT ship a fix in this task.

## Output
HANDOFF: the histogram, the ranked spike causes with evidence, the confirm arm, ONE proposed fix (what, est. effect, risk, look
cost) and `## For the doc` with the histogram as an image or table. TASKS.md paragraph (Task 115). Fast suite once if code changed.
End with STOP_FOR_OWNER (the fix is the next task).
