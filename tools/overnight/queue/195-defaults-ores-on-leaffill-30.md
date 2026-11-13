# 195-defaults-ores-on-leaffill-30

Model: opus

Approval: OWNER, 2026-11-13, Queue decisions doc -- Q-X answer (a) "ores ON now" (task 191) and
Q-Y answer (a) "leaf fill 30" (task 192; NOT the recommended 45).

## Do
1. Ores ON by default: `-ores` defaults to 1 (`Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Ores.cs`,
   `bool on = false` today). `-ores 0` must still give today's world byte for byte.
2. Leaf fill default 45 -> 30: `Trees.DEFAULT_LEAF_FILL` (`Assets/TerrainGen/Trees.cs`). Trees themselves stay
   OFF by default (`-trees 1`); they turn ON in a later task after 193/194. `-leaffill 45` must reproduce 192's 45 arm.
3. Check every place that assumes the old defaults: tests that pin "ores OFF" or fill 45 as the default
   (e.g. 192's pool arms, `Cost_OnVsOff`), the play/settings menu (F6) if it lists these, rigs that must stay
   on the old world (identity/hash gates: pass `-ores 0` explicitly where a rig needs the pre-195 world, and say which).
4. Do NOT touch towns, trees ON/OFF, fluid pockets or sites.
- Generator default changed -> `SUITE=all` once at the end. TASKS.md paragraph.
- No timing arms. Memory: report the process peak the shot launch already prints, OFF vs new default.

## Pictures (one launch, 3-4 poses, midday, sun behind camera)
A cave wall or cut where ore veins show (191's poses), new default vs `-ores 0`; one forest pose with
`-trees 1` at fill 30 (the new default) for the record. Captions say where the veins are.

## End
Done with HANDOFF + pictures.
