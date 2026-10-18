# 108-engine-e2-saved-edit-summaries

Requires: 106-engine-q2-far-raycast

Approval: owner, 2026-10-15, Amendment 8.15 Q10 (a) (ADOPTED): owed part E1-E2, D1-D4, C1, Q1, Q2 in that order; E2 is the
last of it. Contract row 6 of 8.15 §10 (D.5 edit summaries, Appendix D) is part of that adoption. Queued by the supervisor
2026-10-17. Branch `engine-phase`. (`Requires: 106` only for run order; no code dependency.)

## Change
Implement row **E2** of `AMENDMENT_8_15_SIMULATION_REACH.md` exactly (§5 "Proposal: an edit summary per edited chunk",
~line 240; §10 row 6; §12 risk "Summary and D.1 disagree after a crash"): edit summaries SAVED. When a chunk with D.1
edits is saved (eviction or autosave), build its shell-1 summary (majority vote, bricks that differ from
`GenerateShellChunk` at shell 1) -- reuse task 93a's `EditSummary.cs` path, do not write a second downsampler. Store it
as D.5 `summary/` (one file per edited chunk, `.tmp` + atomic rename, CRC, written right after D.1, carrying D.1's CRC so
a stale summary is ignored). Apply it in `GenerateShellChunk` after ops <= S and before ops > S at every shell; the far
band folds it into column height + material (2.5D, as task 82's op patch). Evicted chunks keep their look (today: 93a's
summary is dropped on eviction). Index of edited chunks + an LRU of loaded summaries; give the RAM number (guardrail
"> ~20 MB gives the number"). Cleared with the deltas, like `ops.log` and `world.state`. D.1, `SetVoxel`, `GetVoxel`, the
4.4 table unchanged -- any change to them is STOP_FOR_OWNER.

## Acceptance
- EditMode tests (`EditSummarySaveTests`): save -> evict -> shells regenerate with the summary (shell-1 bricks equal the
  live 93a summary of the same chunk); stale summary (D.1 CRC mismatch) ignored -> generator look; corrupt / truncated
  file rejected safely; ops order (ops <= S, summary, ops > S); cleared with the deltas; far band column folded.
- Fast EditMode suite green. Report save size per summary vs D.1 and the summary build cost per chunk (test timing, NOT
  a frame-time measurement -- rule 10).
- Screenshots (one launch, few poses, short waits, midday, sun behind camera): task 93a's `-farwall` tower seen far away
  after its chunks were evicted, before (generator look) vs after (the tower). Captions say what to look at.
- HANDOFF `## For the doc`: result, file format, sizes, RAM, test counts, the before/after image.
