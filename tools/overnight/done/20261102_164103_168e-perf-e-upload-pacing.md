# 168e-perf-e-upload-pacing

Model: opus

Approval: owner, 2026-11-01, doc Q102 "a for all": E (upload pacing) is next in the order and "lands a frame later" counts as exact. A PERFORMANCE task, ONE change.
Read `tools/overnight/done/*168d*` handoff (its "Main finding": in play the > 33 ms tail is NOT streamer CPU; 57 % of those frames have no CPU section >= 5 ms, i.e. a GPU / preUpdate wait),
`PERF_EXACT_CUTS.md` row E and `KNOWN_ISSUES.md` (115: > 100 ms frames coincide with > 300 KB shell uploads; 123: crossing frames +10 ms GPU wait).

## Do
Row E: cap shell + clipmap upload bytes per frame (FIFO) and write them through a staging ring (`GraphicsBuffer` `LockBufferForWrite`) instead of `SetData` on in-flight buffers,
so the CPU does not wait on the GPU ("none (GPU/pre)" frames). The CPU stays the sole writer; no locks; mirror contents identical once uploads drain (test: drained state == today's byte for byte).
Behind a flag; raw stills 0 px at walk_near / walk_open. Fast suite once.
Measure: QUIET Mac first (168d's arms ran with Firefox/ReportCrashService busy and were ~7 ms slower; check `ps` for stray CPU hogs before each arm and say so in the handoff),
warm-up discarded, AC, ABBA `MODE=playwalk` 3 min, 432 rows, `-spikelog`: share of frames > 33 ms, p95, p99, count > 100 ms, plus walk_near cost split p50.

## Decision table
- > 33 ms share down >= 5 points or > 100 ms count halves, 0 px: ON in play.
- Gain within noise: keep behind its flag, say what the top remaining source is (fluid tick pacing 24-27 %, GPU-bound near views).
- Memory beyond +6 MB or a state-machine / frozen-API change: STOP_FOR_OWNER.

## For the doc
Before/after share > 33 ms, p95, p99, > 100 ms count; the staging ring's memory; which source is largest after.
