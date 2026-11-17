# 208-bug-valve-spares-in-view-chunks

Model: opus
Requires: 207-bug-gpu-retry-only-what-fits

Approval: OWNER, 2026-11-14, Queue decisions doc Q-AB answer (a), fix (2): "Never drop a chunk the player can see; drop chunks
behind or far away first. Neither uses more memory." This changes the eviction ORDER only. Pool sizes stay the same.

## The problem (task 197 HANDOFF, commit fc73592, "Valve eviction")
The CPU pool's LRU valve evicts chunks the player can see: 28-41k valve evictions per survey run, pool peak 495k of 570k (valve
at 484.5k). The evicted chunks reload, and in between their clipmap slots hold air. 197's hand-over draws shell 1's copy there
(probe h1: pit chunks `ABSENT, stage Unloaded`). Inside shell 1's hole, nothing covers them.

## Do
1. Make the valve pick its victims by priority: chunks outside the view frustum and far from the camera first; LRU only breaks
   ties. A chunk inside the frustum within the near range is evicted only when nothing else is left (log a count when it happens).
   Cheap frustum test only, no new per-chunk memory beyond a few bytes. The main thread stays the single writer (CLAUDE.md
   single-writer rule).
2. Keep the 197 hand-over and the 207 retry change as they are.
3. Tests (EditMode): with a full pool, the valve evicts out-of-view and far chunks before any in-view near chunk, the result is
   deterministic, and the eviction count per call is unchanged.
4. Proof: one launch with `-mirrorprobe 1` at 197's poses plus a walk. Report valve evictions in total and IN VIEW (before vs
   after; 197's runs give the before), hand-over columns, ABSENT chunks. Before / after shots at jungle_high and one pose where
   197 showed an evicted pit (`Screenshots/197/v/2_evicted_*`).
- Fast suite before and after; WHOLE suite before done (streaming change). TASKS.md paragraph; KNOWN_ISSUES entry. No timing
  arms; rough cost line from the launch you already do.
- If staying exact needs a pool size change, or the generator cannot keep up, so that in-view drops can't reach ~0: STOP_FOR_OWNER
  with numbers.

## End
Done with HANDOFF (`## For the doc` with numbers and 1-2 before/after pictures, captions naming what to look at).
