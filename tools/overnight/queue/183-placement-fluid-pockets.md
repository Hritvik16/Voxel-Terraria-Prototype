# 183-placement-fluid-pockets

Model: opus

Approval: owner, 2026-11-06: "Placement plan" doc APPROVED, incl. batch 2 answers (supervisor queues step 4, reversible engineering behind a flag, default OFF).
Read `FluidRosterTests` (the eight fluids: water, oil, lava, acid, sludge, honey, quicksand, steam), `FarField.GenToEngine` (every new GenMat needs a mapping),
`Assets/TerrainGen/` ore/tree placement from tasks 180/181 for the flag + reserved-space pattern, and `GAME_DESIGN_BRIEF_SNAPSHOT.md` (fluids, tar seep, volcanic ashlands).

## Do (behind a flag, default OFF: `-fluidpockets 1`)
- Worldgen places fluid pockets AT REST as terrain, using the engine's existing fluid voxel kinds. Placement only: the fluid simulation is UNCHANGED.
- Where (plan): oil in the badlands tar seep; lava in volcanic ashlands and deep underground; water in valleys and caves; acid, sludge, honey, quicksand by
  rules on generator data (biome, landform, depth, slope, wetness/flow); steam only where a lava+water neighbour makes it natural, else skip and say so.
- Rules read data the generator has, never a seed constant. Pockets are additions into reserved space only (no carving beyond the pocket basin); basin sealed
  so the fluid is at rest (no spill on load). Keep out of route corridors, the player start and existing rivers/sea. Not every pair must appear in every world.
- Prove: determinism, 4 seeds + one adversarial case (steep cliff, deep canyon), counts per fluid per biome/depth table, every pocket at rest (a short settle check:
  fluid cell count unchanged after N sim ticks in an EditMode test, no timing arms), LOD 0, shells and the far band agree. CPU stays the sole writer.
- Fast suite + whole suite (generator change). Feature note in TASKS.md. Rough cost note from the task's own runs only; no timing arms.
- Shots, full size, ON vs OFF: a lava pocket underground (cut-away), a badlands oil seep, a valley water pocket. One caption each.

## End
STOP_FOR_OWNER only if a frozen-API/save-format change is needed or the pictures look wrong; otherwise Done. HANDOFF with the counts table and pictures.
