# 201a-bug-gpu-body-pool-overflow

Model: opus

Approval: OWNER, 2026-11-14, chat: "We need to do something about this. We cant keep leaving everything up to the performance
stage." The GPU body pool has been FULL at every tested pose since 195a turned trees and towns on. Chunks it turns away are
not drawn in near view; 197's hand-over draws the coarser shell copy there instead. This task runs BEFORE 207/208 and 210.
No pool size increase (owner: careful with memory; this Mac must still play at low settings, 30 fps).

## Evidence so far
- Task 200: GPU body pool peak 254,400 / 254,400 in both launches; 57,639 refusals over run 1; a redwood crown drew thin and
  1 of 3 redwoods was missing (`Logs/overnight/200/player2.log`, `Screenshots/200/engine/run2_s_redwood_spacecol.png`).
- Task 197: the pool was full at every pose; refused chunks churn through `RetryGpuRefused` (207 fixes the churn, not the cause).
- 195a: memory peak 2,504 of 3,000 MB with everything ON. Pool code: `Assets/CoreEngine/Mirror/TerrainClipmap.GpuPool.cs`;
  ledger: `Assets/Game/MemoryLedger.cs`; tests: `GpuBrickPoolTests`, `RefusedHandOverTests`, `Tier0LackHandOverTests`.

## Do
1. ACCOUNT first (isolate before fixing, CLAUDE.md). At 195a's / 197's poses (forest, jungle_high, town_eye, badlands) with
   everything ON, break the pool's in-use bodies down by what fills them: plain terrain vs trees vs towns vs ores vs pockets;
   by distance from the camera / where in the tier-0 box; by kind of brick (uniform one-material, surface, mixed); and how many
   are byte-identical duplicates. Compare with `-trees 0 -towns 0` at the same poses. A table in HANDOFF.
2. From that table, pick the EXACT cut that removes the overflow with the least risk. Candidates to check, not to assume:
   uniform bricks stored as a single value instead of a 512-byte body; sharing byte-identical bodies (the pool already
   reference-counts with `_gpuRefs`; check whether trees/towns go through it); bodies held for chunks tier 0 cannot show;
   anything stored twice. No look change: raw no-upscaler stills 0 px different at 3 poses vs before (132/134 method).
3. Goal: 0 refusals at those poses with everything ON, with no bigger pool, and a memory peak not higher than today's 2,504 MB.
   Leave room for task 210's trees: report the headroom (free bodies) at each pose afterwards.
4. Tests for the changed path (EditMode), fast suite before and after, WHOLE suite before done. TASKS.md paragraph,
   KNOWN_ISSUES entry. No frame-time arms (rule 10): rough cost line from the launches you already do.
- STOP_FOR_OWNER if the only way to 0 refusals changes the look, the GPU memory layout in a way that touches frozen contracts, or
  needs a bigger pool. Show the accounting table and the options with their MB.

## End
Done with HANDOFF: the accounting table (before / after), refusals and headroom per pose, before / after pictures at the pose
where 200's redwood crown drew thin, and `## For the doc`.
