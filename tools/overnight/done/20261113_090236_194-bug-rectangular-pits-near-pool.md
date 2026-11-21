# 194-bug-rectangular-pits-near-pool

Model: opus

Approval: supervisor, 2026-11-13, as an owner-independent BUG task (SUPERVISOR.md backlog: follow-ups a finished task names; never-idle rule). Named by task 191's HANDOFF (commit b182509, "Next step" (1)). No design choice: find the cause, fix it if it is a bug inside a sound design.

## The bug
With placement switches on (`-trees 1 -ores 1 -fluidpockets 1 -sites 1 -towns 1`), near-field pictures show sunken RECTANGULAR PITS with straight vertical walls in flat ground:
`Screenshots/191/on7_town_150m.png` (3 pits in front of the desert town) and `Screenshots/191/towns42_town_150m.png` (2 pits). Never in an OFF shot.
191 saw them appear as the near GPU body pool's refused count rose (seed 7: 4,073 -> 4,450; seed 42 ALL ON refused 23,020; cap 254,400, ~98-100 % full even in OFF). Correlation only.
By design (`Assets/CoreEngine/Mirror/TerrainClipmap.GpuPool.cs` header, "FULL POOL"): a refused chunk is cleared to air, reported to the shell hand-over (`IsGpuRefused` -> `FarField.UpdateResidency` drops its column, so shell 1 draws it) and re-dirtied once a slot frees. A pit = that hand-over not covering the hole (or something else entirely).

## Do
1. Reproduce at 191's `town_150m` pose, seed 7, ALL ON (`Logs/overnight/191/` has the launch lines and the pose). ONE launch, few shots. Log per frame/pose: refused-now count, which chunk coords are refused, and whether shell 1 has those columns.
2. Prove or disprove "pit = refused chunk": e.g. a capture-only debug that tints refused chunks, or compare the pit rectangles' world coords with the refused list. If NOT the pool, find what it is (town flatten? fluid pocket carve? tree footprint?) and say so.
3. If it is the hand-over: fix the hand-over so a refused chunk is always drawn by its shell (bug fix inside the existing design; no pool size, memory-layout or frozen-API change). Prove: same pose, no pits, refused count unchanged or lower; a seed 42 pose too.
4. If the only fix is a bigger pool, a different eviction rule, or a memory-layout change: do NOT do it. STOP_FOR_OWNER with the evidence, 2-3 options and what each costs in memory (the 3,000 MB cap; 191 measured peaks 2,47x-2,61x MB).
- Fast suite before the commit; a test for the hand-over if the fix is in code that EditMode can reach. TASKS.md paragraph.
- No timing arms (rule 10). Pictures: before/after at the pit pose, midday, sun behind camera; captions say where the pits are.

## End
Done with HANDOFF + pictures, or STOP_FOR_OWNER per step 4.
