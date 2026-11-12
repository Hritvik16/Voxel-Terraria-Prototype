# 190-placement-towns-on-sites

Model: opus

Approval: owner, 2026-11-12, answer (a) to "place the house kit on the 187 sites?": yes, behind `-towns 1` (default OFF), AND spread the towns apart.
Read task 187's HANDOFF (commits `0f5886a`, `69b8424`; `Assets/TerrainGen/Sites.cs`, SiteList, the stored flatten rule: pad Y = median map height, blend R/2, cut/fill),
task 184's kit (`2aacfba`, `Assets/TerrainGen/BuildingKit.cs`, BuildingFootprint, empty `WorldRecord.BuildingList`), CLAUDE.md "no terrain-specific engineering".

## Do
- Spread: on seed 7 all three towns sit together in the southwest (routes only join the easy areas, so sites cluster). Add a spacing rule derived from island size
  (e.g. a minimum town-town distance scaled to the land area / town count), integer, deterministic, no seed constants; prove on 4 seeds + an adversarial island
  whose easy ground is all in one corner (the rule must degrade gracefully: fewer towns, never a town on bad ground).
- Apply the flatten rule inside each site disc (only there; terrain outside sites byte-identical, prove with the height FNV test; `-towns 0` / `-sites 0` byte-identical to today).
- Fill `BuildingList`: lay out the kit's huts / houses / halls per site kind (town > village > outpost), doors facing the site centre or route, no overlap, no building on water, rivers, features/stamps or slopes above the 187 limit.
- Near == far agreement for the new voxels (shell and chunk paths) as for trees/stamps; check memory/pool use is not pushed over capacity (task 188's lesson).
- Tests per rule; fast suite while iterating, `SUITE=all` once at the end. TASKS.md paragraph. No timing arms; plan cost at load from the task's own log.
- Pictures (look at each yourself, midday sun behind camera, full size, captions say what to notice): the 4 island maps with sites + buildings marked; per seed one eye-level shot
  of a town and one of a village; one shot from ~150 m away showing a town in its landscape; seed 7 before/after spacing map.

## End
Done; default stays OFF; the owner decides the default from pictures. HANDOFF with counts table (towns/villages/outposts/buildings per seed, min town distance before -> after) and image paths.
