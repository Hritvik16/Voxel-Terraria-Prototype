# 113-summary-build-off-thread

Model: opus
Requires: 112-perf-measure-tooling

Approval: supervisor, 2026-10-22, "going ahead with (a)" (task 111 Q8; reversible, same bytes on disk). Perf-affecting code, so ONE change
and NO timing run here (rule 10); the saving is checked at the next performance milestone.

## Change
Task 108's `SavedEditSummaries` builds the D.5 summary synchronously on the main thread inside `StreamManager.SaveDelta`
(~3.1 ms each, up to ~12-24 ms when a frame evicts 4 edited chunks). Move the vote/build to a worker job following 93a's
Snapshot/Run/Extract pattern, keeping the single-writer rule (workers write only exclusively-owned scratch; the main thread
snapshots the inputs, then writes the file/index on completion). D.1's save and its rename stay on the main thread as today.
Constraints: the .sum bytes must be identical to the synchronous build (reuse 108's tests: saved == live at all 7 levels, CRC,
truncation, stale-D.1); a chunk evicted and reloaded before its job finishes must still see a correct look (define and test it);
shutdown/`-cleardeltas` must wait for or drop pending jobs safely. Flags unchanged. D.1, SetVoxel, GetVoxel, the 4.4 table untouched.
Tests: extend `EditSummarySaveTests` (byte-identical, evict-then-reload-while-pending). Fast suite once at the end.
HANDOFF: what changed, bytes-identical evidence, `## For the doc`. TASKS.md: extend the Task 108 paragraph.
