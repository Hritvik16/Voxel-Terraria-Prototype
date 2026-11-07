# 187-worldgen-b7-site-planner

Model: opus

Approval: owner, 2026-11-07, Q-S answer (a): build the town and village SITE PLANNER as its own task (surface-rebuild step B7, now approved; the old "do NOT queue B7" is lifted for this). Behind a flag `-sites 1`, default OFF.
Read task 184's HANDOFF (git `2aacfba`; no sites exist), `WorldRecord.cs` (site byte ids >= 16 are reserved for B7-B8; EasyAreaList, FeatureList, RouteList), `Routes.cs`, the placement plan decisions below, `BuildingKit.cs`.

## Plan decisions (owner, batch 2, 2026-11-06)
About 3 towns plus about 10 villages and outposts on a medium island (~6 km), scaling with world size; at least ONE frontier town (foot of the volcano or the snow line: hard ground). Sites from flat ground, water and the biome x landform pair, with land shaped around them.
Towns on the easy areas; villages / outposts on Walk cells near fresh water along the routes. Not every pair appears in every world.

## Do
- A site planner in the generator: integer rules on data the generator has (flatness, slope, distance to fresh water, biome, landform, route proximity, danger); no seed constants; deterministic; 4 seeds + one adversarial case (an island with few flat cells).
- Output: a `SiteList` in the world record (id >= 16, kind town / village / outpost / frontier, integer centre, radius, flat-area box, which route it joins) + a minimal land-shaping footprint plan (flatten rule stored, NOT yet applied unless trivial; if applying changes terrain, only inside the reserved site, behind the flag).
- Routes may now also join sites (do not break `RouteTests`; the known-red ford test is green since 179).
- Keep out: sea, rivers, start zone, features/stamps. Records are never saved, so no save-format change; STOP_FOR_OWNER if a frozen API must change.
- Tests per rule + a counts table per seed (towns / villages / frontier, distance to water, slope). Fast + whole suite. TASKS.md paragraph. No timing arms.
- Pictures: the island map with sites marked (labelled) for each of the 4 seeds, and one eye-level shot of a site's flat area. Captions.

## End
Done; default stays OFF. HANDOFF names the next step (place BuildingKit on the sites behind `-towns 1`).
