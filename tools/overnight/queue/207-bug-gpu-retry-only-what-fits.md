# 207-bug-gpu-retry-only-what-fits

Model: opus

Approval: OWNER, 2026-11-14, Queue decisions doc Q-AB answer (a): "Both fixes, one task each, retry first." This is fix (1).
Fix (2), keeping in-view chunks out of the CPU valve, is task 208. Do NOT touch the CPU valve here.

## The problem (task 197 HANDOFF, commit fc73592, "Retry gap")
The GPU body pool is full at every tested pose since 195a (trees + towns ON). When ONE slot frees, `RetryGpuRefused` clears the
WHOLE refused set and re-queues all of those chunks. The re-uploads then wait in the budgeted dirty queue (up to ~1,250 chunks),
while their slots still hold the refusal's air. Probe q1 (jungle_high): refused 0, 192 resident chunks missing, up to 214 per
frame. 197 covered this with a hand-over to the shells (`TerrainClipmap.Tier0LacksChunk`, `FarField.ColumnState`), so there are
no pits now, but the churn is still there. At a lake shore the shell's copy can show a vertical water face
(`Screenshots/197/v/3_water_after.png`).

## Do
1. Re-queue only as many refused chunks as there are free slots (nearest to the camera first; keep the rest refused), so a chunk
   is never taken out of the refused set without a slot to land in. Find the exact place first: `RetryGpuRefused` and its callers,
   plus the pool's free count. No pool size change, no memory layout change, no shader change.
2. Keep 197's hand-over rule as it is. It still covers whatever stays refused.
3. Tests (EditMode): a full pool with N refused chunks and k freed slots re-queues exactly min(k, N), nearest first. Nothing
   leaves the refused set without a slot. The existing `Tier0LackHandOverTests` stay green.
4. Proof: one launch with `-mirrorprobe 1` at jungle_high, forest and town_eye (197's poses). Report, per pose, against
   197's f1-f3 numbers: hand-over columns, resident-but-missing chunks, refusals, retries. Before / after shots at jungle_high,
   including the lake shore at the right edge.
- Fast suite before and after. TASKS.md paragraph; update the KNOWN_ISSUES entry if one covers this. No timing arms (rule 10):
  take a rough cost line from the launch you already do.
- If the exact fix turns out to need a pool size or eviction-policy change: STOP_FOR_OWNER with the evidence.

## End
Done with HANDOFF (`## For the doc` with numbers and 1-2 before/after pictures, captions naming what to look at).
