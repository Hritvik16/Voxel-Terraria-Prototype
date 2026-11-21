# 195-defaults-ores-on

Model: opus

Approval: OWNER, 2026-11-13, Queue decisions doc -- Q-X answer (a) "ores ON now" (task 191).
Q-Y (leaf fill): the owner first answered (a) 30, then CHANGED it to (b) 45, the recommended value and already
`Trees.DEFAULT_LEAF_FILL`. So the leaf fill does NOT change in this task.

## Do
1. Ores ON by default: `-ores` defaults to 1 (`Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Ores.cs`,
   `bool on = false` today). `-ores 0` must still give today's world byte for byte.
2. Check every place that assumes ores are OFF by default: tests that pin it, the play/settings menu (F6) if it
   lists ores, rigs that must stay on the old world (identity/hash gates: pass `-ores 0` explicitly where a rig
   needs the pre-195 world, and say which).
3. Do NOT touch the leaf fill, trees ON/OFF, towns, fluid pockets or sites.
- Generator default changed -> `SUITE=all` once at the end. TASKS.md paragraph.
- No timing arms. Memory: report the process peak the shot launch already prints, `-ores 0` vs the new default.

## Pictures (one launch, 3-4 poses, midday, sun behind camera)
A cave wall or cut where ore veins show (191's poses), new default vs `-ores 0`. Captions say where the veins are.

## End
Done with HANDOFF + pictures.
