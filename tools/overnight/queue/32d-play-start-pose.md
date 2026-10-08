# 38-play-start-pose

Approval: follow-up of task 32b (bug fix, reversible, no owner decision: the spawn itself
stays the `Tg2Spawn` placeholder). No performance arms.

## The bug (found by 32b, 2026-10-08)
`./run-play.sh` (tg2, map v4, seed 42) opens on a BLACK world: the interactive Playground
always ends Start with `TeleportToArena()` (Playground.cs), and on tg2 v4 that pose has the
eye INSIDE WATER. Evidence: `SHOT=45 ./run-play.sh` -> Player.log
`[Playground] -playshot: ... eye 4004.4,655.4,3974.8 m = voxel int3(40044, 6553, 39748)
material 6 (WATER), mode Walk`; HUD "buoyancy 100% submerged in water"; arena at
int3(40076, 6529, 39780); the minimap marker sits in a lake. Shot: `Screenshots/Play/spawn_v4.png`.
Also in the log: `[PlayerController] could not free the spawn at (1280.00, 9.50, 1268.00)`
-- the serialized old-world `_spawnM`, harmless because TeleportToArena moves the player after.

## Do (one change)
- Find out first WHY the eye is in water: is `TryFindNaturalBasin` picking a natural lake
  bed on tg2 (its water-surface skip only tests one voxel), or do the arena's own vents fill
  the basin within the first seconds? Say which in HANDOFF.
- Fix it so the interactive start pose has the eye in AIR above the arena (e.g. lift the
  pose above the water/terrain column, or reject basins under natural water). Keep the old
  world's start pose unchanged (it is in historical shots).

## Acceptance
- `SHOT=45 ./run-play.sh` -> the `-playshot` line says `(air)`, and the shot shows terrain.
  `MAPV=3` likewise. Fast EditMode suite green. Caption both shots in HANDOFF.
