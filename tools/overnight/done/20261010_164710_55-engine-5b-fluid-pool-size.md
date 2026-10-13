# 55-engine-5b-fluid-pool-size

Approval: owner, 2026-10-09 -- engine plan approved (Q36); plan section 2 sets the fluid budget at ~250 MB.
This is the follow-up task 54 named (engine step 5's memory half). Supervisor default under the owner's
"recommended option is the default" rule (2026-10-08), posted as Q39 in the Queue decisions doc,
2026-10-10: (a) 480 tiles. Queued by the supervisor 2026-10-10. Branch `engine-phase`.

## Goal
Task 54 made fluid tiles go to AWAKE fluid only (commit ac23cf3): at the coast the pool peak fell 1,024 -> 32
tiles, but the pool is still ALLOCATED at 1,024 tiles (512 MB GPU; ledger 548.8 GPU + 42.5 managed MB).
Shrink the pool for Playground / tg2 / play to **480 tiles** (~240 MB GPU at 0.5 MB per tile), the plan's
~250 MB budget. If the code needs a power of two or another granularity, use the largest allowed size at or
under 250 MB and say why.

## Steps
1. Find every use of `FLUID_TILE_POOL_CAPACITY` and `_tilePoolCap` (serialized in `Assets/Scenes/Playground.unity`).
   Do it the way task 53 did the cascade caps (`EngineConfig.Tg2CascadeTierPoolCap`): a separate Playground/tg2
   cap; every fluid RIG keeps 1,024 so `run-fluid-tiled.sh` and the other fluid rigs measure what they did.
   Memory landmine: a scene-serialized value overrides a changed code default (memory note "scene builder
   overrides SerializeField defaults") -- check the scene builder and the .unity value, and prove the new
   size in Player.log / the ledger, not by reading code.
2. What happens when 480 fill up must stay what 54 built (nearest tiles win, refusals counted); check it with a
   stress case: an EditMode test that asks for more awake tiles than the cap and asserts no crash, nearest kept,
   refusals counted.
3. Fast EditMode suite 0 FAIL; `tools/with-unity-lock.sh ./run-fluid-tiled.sh` still PASS 19.
4. `MODE=poolprobe ./run-tg2world.sh` once (memory + behaviour only, no timing; PREAMBLE rule 10): ledger fluid
   GPU MB before -> after, phys_footprint peak, refusals at the coast and lake stops, placed-box water after 480
   frames (54: 1029 -> 3 at the coast, 1029 -> 1 at the lake).
5. One midday still at the lake with the placed box flowing (as 54's `lake_after_2s.png`), sun behind camera.

## For the doc
Fluid pool tiles and GPU MB before -> after, ledger total, phys_footprint peak at the coast, refusals, the still.
