# 201-defaults-fluidpockets-on

Model: opus

Approval: OWNER, 2026-11-13, Queue decisions doc Q-AA answer (a): "This is fine for now but these need to be more like
ponds." -> surface fluid pockets (task 183, natural basins since 196) ON in every world now. The "more like ponds" look is
a LATER look talk (`tools/overnight/OWNER_TOPICS.md`), NOT this task: do not change the pocket shape, placement or rules.

## Do
1. `-fluidpockets` defaults to 1 (`TerrainGenChunkSource` default flip, the 195 / 195a pattern). `-fluidpockets 0` must
   give the pre-201 world byte for byte (with 196's basins unchanged under `1`).
2. Do NOT touch ores, trees, towns, sites, leaf fill, sky fill, `-pocketbasin`.
3. Check everything that assumes pockets are OFF by default: tests pinning the default, the F6 menu if it lists them,
   rigs whose identity/hash gates need the old world (pass `-fluidpockets 0` there explicitly and name each one; KNOWN_ISSUES
   RIG SELECTION RULE: live fluid breaks Gates C/D by design -- never loosen them). Fluid sim cost: pockets are at rest
   (183/196); say whether any pocket wakes the fluid tick at the test poses (the launch log already prints it).
- Generator default changed -> `SUITE=all` once at the end. TASKS.md paragraph.
- No timing arms. Memory: report the process peak and near-pool refused counts the shot launch already prints, old vs
  new default (195a: peak 2,504 MB; cap 3,000 MB). Over 3,000 MB at any pose: STOP_FOR_OWNER with the numbers, default left OFF.

## Pictures (one launch, 3-4 poses, midday, sun behind camera)
196's pocket poses (water, lava, one in forest) + one ordinary 195a pose: new default vs `-fluidpockets 0`. Captions say what changed.

## End
Done with HANDOFF + pictures.
