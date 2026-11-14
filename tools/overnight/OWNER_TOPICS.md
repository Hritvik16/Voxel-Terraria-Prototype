# Owner discussion topics (owner, chat 2026-11-13) -- NOT queued; discuss first

Doc: "Queue decisions" tab "Talks to have" (https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq#a374ceba-3968).
Each topic gets a short brief with pictures before the talk (a planning-only task that writes a Claude Doc is allowed,
SUPERVISOR.md "Writing a task file"). Order of work stays look -> perf -> placement (2026-11-05).

1. Trees / flora (LOOK). Owner dislikes current trees: "look like a child drew them, not natural", no space between leaves.
   Explore realistic tree algorithms (space colonization, L-systems...) and real trees as reference; per-biome unique trees.
2. Builds / villages / towns / cities (PLACEMENT). More terrain-agnostic: flatten only each building's footprint; buildings
   joined by roads/paths = village; more buildings + rules = city. Towns/villages across the map with routes between them,
   instead of or in addition to tying terrain to difficulty. Much more building variety (WFC, other procedural methods).
3. Performance (STAGE 2). Owner fears stable 30 fps on the M1 Air needs serious graphics cuts, which they don't want; also
   high res doesn't yet feel "cinematic". Brief: biggest per-frame costs, look-keeping options, look-costing options, honest
   feasibility; what "cinematic" means (reference pictures).
4. Polish (LOOK): reflections in water, metallic shine, reflections on ores.
5. Stamp rundown: ANSWERED in the doc tab (Stamps.cs kinds + chances, 3 singletons, ledges, cavern mushrooms).
6. What's left before character creation + gameplay loop: object placement and everything else to finish first.

## Decided 2026-11-13 (chat), topic 3 performance
P1 (a) the M1 Air is the MINIMUM machine (30 fps on a lower preset; full look for stronger machines). P2 (a) split the
drawing into separate render steps now, before reflections / more light (design doc first). P3 (a) a frame budget per
feature. P4 cinematic references: modded Minecraft shader packs, Lay of the Land (Tooley; 0.1 m cubes; YouTube devlog).
-> task 199 (plan only: presets, budget, render-steps design, look techniques, owner questions).

## Decided 2026-11-13 (chat), topic 1 trees
T1: 0.1 m cubes, closer to REAL trees (banyan, massive redwood); 5-6+ types as the biomes need; recognizable but each
unique. T2: wants to SEE example end products before picking a method. T3 (a) 2-3 species per biome. T4 (a) trees first,
bushes/grass/flowers later. -> task 200 (examples only, behind a gallery flag; world unchanged; questions after).

## Q-AA answered 2026-11-13 (doc): (a) pools ON -> task 201. Owner: "fine for now but these need to be more like ponds"
-> a LOOK talk item (what makes a pond: outline, shallow edge, reeds/plants, size range) before any shape change.
