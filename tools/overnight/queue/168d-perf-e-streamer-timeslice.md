# 168d-perf-e-streamer-timeslice

Model: opus

Approval: supervisor, 2026-11-02: "going ahead with (a) unless you object" (reversible engineering choice; doc "Going ahead" item, from task 168b's owner question). Requires: 168c done.
A PERFORMANCE task, ONE change. Read 168b's handoff (`tools/overnight/done/` ... 168b, and its table), `PERF_EXACT_CUTS.md` row E, `KNOWN_ISSUES.md` (streaming entries) and the single-writer rule in CLAUDE.md
(only the main thread mutates ChunkStore / pools / the sparse table; NO locks in the streaming path -- time-slicing must stay on the main thread, spread over frames).

## Why
168b: walk p50 is already 23.7 ms (<= 30 met), but 18 % of walking frames exceed 33 ms (target <= 5 %), and even with sky probe + sun + shells all OFF it is still 12 %.
So the tail is CPU, not shading: 72 % of spikes are main-thread streaming (streamer/evict >= 8 ms); the fluid tick adds +14 ms on its frame (15 % of frames; 31 frames > 100 ms,
1 with `-nosim`). The 33-50 ms band follows streamer > 5 ms (25 % of frames).

## Do (design first, in the handoff, then build)
1. From the spike log (`Logs/tg2world_20261102_102802.txt`, `tools/spike-attribution.py`) list the streamer / evict / admit / fluid-tick sections by their share of the > 33 ms frames.
2. Time-slice the largest main-thread chunks of work with a per-frame budget (a work queue drained up to N ms per frame, remainder next frame): the streamer's admit/evict work, then the fluid tick's main-thread part.
   Behavior must be exact at rest (same final chunk states / same fluid state after the queues drain: prove with a test that drains to equality against the unsliced path).
   Chunks may land a little later while moving: state the added time-to-visible in ms.
3. Judge on a `MODE=playwalk` (3 min, warm, AC, ABBA new vs today, 432 rows): the share of frames > 33 ms and p95, not p50. Target <= 5 %; report what it reaches.

## Decision table
- > 33 ms share drops >= 5 points and deficit/time-to-visible regression is <= 1 s: ON in play by default.
- Smaller gain: keep behind a flag, say what remains the biggest spike source.
- A change to the streaming state machine contract (D / §4.4 transition table) or a frozen API: STOP_FOR_OWNER (redesign).

## For the doc
Spike sources ranked; > 33 ms share and p95 before -> after; time-to-visible cost; whether <= 5 % was reached.
