# 123-perf-eviction-slicing

Model: opus
Requires: 118-perf-shadow-clamp-trial

Approval: supervisor, 2026-10-22, "going ahead with (a) unless you object" (task 115 Q73 (a): eviction slicing as one task AFTER
116-118; reversible engineering, internal only). PERFORMANCE task, ONE change. Read task 115's HANDOFF (in
`tools/overnight/done/*115-perf-stutter-isolation.HANDOFF.md`) and KNOWN_ISSUES "STRAY RESIDENT CHUNKS ALIAS INTO THE MIRROR".

## Change
On a chunk crossing, `EvictOutOfRange` (streamer slide) evicts a whole ~256-chunk slab in one frame (115: ~90 such frames per 3-min walk,
58-64 ms; worst 142 ms evicting 1,157). Queue the out-of-range chunks and evict <= ~32 per frame (tune; say why), following task 26's
deferred save-capped eviction. Constraints: single-writer (main thread only); deferred chunks must stay out of the mirror box
(`UploadDirty` skip/park, tasks 26/32 tests must stay green); a chunk that comes back into range before its eviction is simply kept;
pool pressure: if admission needs a slot, evict from the queue first. No frozen API, no memory layout change. Tests: extend the
streaming tests (deferred eviction, re-entry, pool-pressure).

## Measure
Warm-up (discarded), then `MODE=walk` 3 min x 4 (A B B A) with `-spikelog`, flag OFF vs ON; AC only. Report p50 / p95 / p99 / % > 33 /
> 100 ms count, and the chunk-crossing frames' ms (115's `tools/spike-report.py`). Keep ON by default if p99 or the crossing frames
improve and nothing else regresses. HANDOFF + `## For the doc`, TASKS.md paragraph, fast suite once. STOP_FOR_OWNER.
