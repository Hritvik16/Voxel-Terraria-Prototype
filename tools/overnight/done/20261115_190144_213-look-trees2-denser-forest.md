# 213-look-trees2-denser-forest

Model: opus

Approval: OWNER, 2026-11-15, Queue decisions doc Q-AJ answer (a): turn the new trees on AND make the forest denser next.
Task 210 (commit ef6f12f, `Trees2.cs`, TASKS.md "Task 210", `Logs/TerrainGen/trees210/`) built the new trees behind `-trees2 1`.

## Do
1. Forest density is capped by the 12.8 m planting grid (95 % of full-plane forest cells already hold a tree), not by memory:
   the forest uses 61 % of today's tree bodies and ~30 k GPU bodies are free at forest_100m. Make the forest read as a forest again:
   birches on a 6.4 m grid (210 HANDOFF names this), and/or a bigger oak share. Still behind `-trees2 1` (default flip is task 214).
2. Keep 210's design: brick budget per area (a bigger tree is rarer), shared prototype library, nothing stored per tree, deterministic
   across chunk borders. `TodayTrees_Pinned` stays green (`-trees2 0` = today byte for byte).
3. Memory (rule 11), same launch set, `-trees2 1` denser vs `-trees2 1` as 210 left it vs `-trees2 0`: process peak (hard cap 3,000 MB,
   at most +150 MB over `-trees2 0`), GPU body pool peak and refusals (must stay BELOW `-trees2 0`), CPU pool peak. NO pool made bigger.
   If the forest cannot reach "reads as a forest" inside the budget: STOP_FOR_OWNER with pictures of the best fit and one step denser.
4. Pictures (forest eye, forest 100 m, same cameras as 210's `sheet_biomes.jpg`; midday, sun behind camera): denser vs 210 vs today.
   Look at them before concluding; a flip viewer as 210's `viewer.html`. 3-4 poses per launch, <= ~10 min, no frame-timing arms (rule 10).
5. Fast suite before, WHOLE suite before done (`Assets/TerrainGen/` change). TASKS.md paragraph.

## End
Done with HANDOFF + `## For the doc` (memory table, 2-3 captioned pictures: what to look at). Default stays as it is.
