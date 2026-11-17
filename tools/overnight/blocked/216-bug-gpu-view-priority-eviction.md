# 216-bug-gpu-view-priority-eviction

Model: opus

Approval: OWNER, 2026-11-15, Queue decisions doc Q-AI answer (a) (new eviction design approved): keep 208's valve fix ON and add a
GPU-side follow-up. Source: `tools/overnight/blocked/208-bug-valve-spares-in-view-chunks.HANDOFF.md` (read it first), KNOWN_ISSUES
entries 197 / 201a / 208, TASKS.md 207 and 208.

## Do
1. Problem: with 208 ON the CPU valve drops far out-of-view chunks, so the GPU body pool stays full and in-view chunks are REFUSED
   (GPU refusals 857 -> 1,190; 31-138 chunks wait at each shot, drawn by 197's hand-over).
2. Fix: when an in-view (or nearer) chunk is refused, the GPU body pool drops the bodies of chunks that are OUT OF VIEW and farther,
   inside the mirror box, first (the hand-over draws those). Single writer stays the main thread; no new locks; no pool made bigger;
   no layout / frozen-API change (flag it as STOP_FOR_OWNER if you find it needs one). Flag `-gpuviewevict 0|1`, default ON, 0 = today.
3. Efficient by construction: one pass, no per-chunk storage, reuse 208's frustum. Say why in HANDOFF.
4. Memory (rule 11) ON vs `-gpuviewevict 0` from the same poses as 208's launch pair, FEWER poses (3-4, <= ~10 min): GPU refusals at the
   shot must go DOWN against `-gpuviewevict 0` (target: back to 857 or lower), process peak not up, CPU pool peak and valve counts. Pictures of
   jungle_high and walk2 (208's), looked at. If refusals cannot be cut: STOP_FOR_OWNER with the numbers.
5. Fast suite before, WHOLE suite before done. Tests for the eviction order. TASKS.md paragraph + KNOWN_ISSUES 208 entry updated.

## End
Done with HANDOFF + `## For the doc`.
