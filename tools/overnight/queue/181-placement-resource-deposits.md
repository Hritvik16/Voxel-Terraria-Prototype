# 181-placement-resource-deposits

Model: opus
(2026-11-06 supervisor: the Requires on 180 was dropped; 180 stopped for the owner's look and the ore work does not depend on the trees.)

Approval: owner, 2026-11-06: "Placement plan (draft for approval)" doc APPROVED, incl. batch 2 answers. Order: trees (180) -> resources (this) -> towns' buildings -> fluid pockets.
Read the plan doc's "Batch 2 answers" (summary below), `GAME_DESIGN_BRIEF_SNAPSHOT.md` "Underground materials", "Object placement" and "World changes supported, and new ores",
the pick-tier chain in `Assets/ContentModules/Tools.cs` (task 105, TASKS.md), and the memory rule: a rule reads generator data, never a seed constant.

## Do (behind a flag, default OFF: `-ores 1`)
- Place the existing five-tier ladder: copper, gold, iron, cobalt, emberstone (stone pick opens copper, ... cobalt opens emberstone; bedrock never).
  If a material is missing from `Materials`/`FarField.GenToEngine`, add the mapping (known landmine: every new GenMat needs a mapping).
- Sources, from the brief (Decided): depth (rarer deeper), the biome x landform pair, veins exposed in cliff and canyon walls, rich deposits at signature
  features. Badlands carry ore veins in their walls.
- Owner R1 (b): the first copper is UNDERGROUND ONLY -- nothing placed to be found on the surface near the start. (Cliff/canyon-wall veins elsewhere are still allowed per the brief.)
- Owner R2 (a): cobalt and emberstone are LATENT: generated at world creation but they look and mine as the plain rock around them until the world stage
  unlocks them. Design the minimum mechanism (a per-material "latent until stage N" flag read from `world.state`, task 101) and keep it deterministic;
  do NOT rewrite ground later. If this needs a frozen-API or save-format change, STOP_FOR_OWNER with the options instead of doing it.
- Density: copper and gold common in easy and mild land, cobalt and emberstone only in the harshest biome x landform pairs and deep. Rules on data the
  generator has (depth, biome, landform, slope, danger); no seed constants. Keep out of route-corridor surface cells, water and the player start.
- Prove: determinism, 4 seeds + one adversarial case (a tall cliff, a deep canyon), counts per ore per biome/depth table, nothing at the surface near the start.
  The CPU stays the sole terrain writer; LOD 0, shells and the far band agree.
- Fast suite + whole suite (generator change). Feature note in TASKS.md. Rough cost note from the task's own runs only; no timing arms.
- Shots, full size, ON vs OFF: a cliff face with veins, a cut-away or dug cave showing deposits, a badlands wall. A caption each.

## End
STOP_FOR_OWNER only if a frozen-API or save-format change is needed or the pictures look wrong; otherwise Done. HANDOFF with the counts table and pictures.
