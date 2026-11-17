# 192-look-trees-unique-open-crowns

Model: opus

Approval: owner, 2026-11-12 (chat): "we probably want to fix the look of trees before [the shade lighting task]. They look very generic and not unique. Plus the density of the leaves is too much. Leaves dont look like that normally."
Earlier owner answer (Placement T1, 6 Nov): "wouldn't each biome have unique type of trees. how do we procedurally guarantee unique trees while also keeping the general aesthetic of plant life in that biome/topography combo?"
Trees stay behind `-trees 1` (default OFF); the owner judges from pictures. Look-first order (owner 2026-11-05).

Read first: TASKS.md task 180 (trees, `Assets/TerrainGen/Trees.cs`) and task 188 (COMPACT shell bricks, `TreePoolTests`), CLAUDE.md "no terrain-specific engineering", memory rule "simple material rules".

## What is wrong today (supervisor looked at `Screenshots/180/fixed_forest_2_after.png`, `fixed_jungle_2_after.png`)
- Every crown is a SOLID blob: every voxel inside the envelope is leaf, one flat green, no branches visible, no sky through it. Real crowns are clumps of leaves on branches with gaps.
- 3 fixed families per biome with hash-jittered numbers read as the same tree repeated; neighbouring trees look cloned.
- Under the solid crowns the ground is near-black at noon (task 193 handles shade lighting; open crowns should already help).

## Do
1. **Open crowns.** Build each tree from a branch skeleton (trunk -> limbs -> twigs; split count, angles, lengths, taper, droop all from the tree's cell hash, integer and deterministic) and put leaves as CLUMPS around branch ends, not a filled envelope. Gaps between clumps must let sky and sun through. Measure leaf FILL = leaf voxels / crown-envelope voxels per tree; today's value vs new, per biome.
2. **Unique trees, biome look kept.** Replace "3 families x jitter" with continuous per-tree parameters drawn from RANGES that each biome (and the slot: flat / steep / hollow / high / shore) sets -- e.g. forest broad deciduous, snow conical conifer, savanna flat-topped, jungle tall emergent + palms, desert cacti. Every parameter comes from the hash inside its range, so no two trees match while a biome still reads as one kind of woodland. Also vary by age/size (saplings, mature, old) and lean on slopes. No seed constants, nothing tuned to one seed.
3. **Leaf density: give the owner three arms to choose from.** A test/diagnostic arg (e.g. `-leaffill 30|45|60`, percent of the envelope) with your best guess as the new default; pictures of all three at the same poses.
4. **Colour:** a tree may take a different leaf shade from its neighbour (pick per tree from the biome's existing leaf GenMats or add at most 1-2 per biome via `tools/palette_defs.py`), but keep ONE leaf value per tree where you can: 188's compact bricks only help when a brick holds <= 2 values. Report full vs compact bricks.
5. **Guards:** `-trees 0` byte-identical to today (height FNV / chunk tests). 180's physical rules stay green (nothing floating, trunk on the ground, < 2 % crown in rock) on 4 seeds; near chunk == FarQuery point == shell sample (`TreeTests.Chunks_Deterministic_Border_PointAndShellsAgree`). Thin twigs must not leave detached leaf crumbs (every leaf voxel 6-connected to its tree).
6. **Memory:** rerun `TreePoolTests.Pools_ForestPose` / `Pools_JunglePose`: shell pools must stay under PoolCap with trees ON at each leaf-fill arm; report bodies used before (188) -> after. If an arm overflows, say so; do not raise caps.
7. Fast suite while iterating, `SUITE=all` once at the end (generator change). TASKS.md paragraph. No timing arms (the 180/188 `Cost_OnVsOff` gen-time line is fine as a rough cost).

## Pictures (look at each yourself; captions say what to notice)
One launch, 180's poses (`Logs/overnight/180_poses.txt`: forest, jungle, savanna, snow, desert) + one close-up under a forest crown looking up at the sky, midday, sun behind camera, full size: today (180 trees) vs new at each leaf-fill arm. Plus a contact sheet of ~12 neighbouring trees per biome (the 180 `sheet_<seed>_<biome>` style) old vs new, so uniqueness is visible side by side. A `viewer.html` flip viewer like 188a's.

## End
Done; default stays OFF. HANDOFF: what to compare, the fill / uniqueness / pool table, and STOP_FOR_OWNER only if no leaf-fill arm fits the pools.
