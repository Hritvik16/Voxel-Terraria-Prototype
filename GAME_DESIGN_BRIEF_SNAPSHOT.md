# Game Design Brief -- local snapshot (2026-10-11)

Plain-text copy of https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf made by the supervisor so task sessions can read it. The claude.ai doc is the authority; formatting lost.


# Game Design Brief
 · 

## How this brief works
This brief records decisions, not essays. Each one comes from a question you've answered. I ask in small batches. Your answer goes into the matching section, marked Decided or Leaning (a preference you may revisit). Anything not yet asked stays in Open questions.

Scope: only what the terrain, world generation and object placement depend on. Character design, combat, UI and story come later, in their own documents.

Long-term first: some answers are cheap to change later and some are not, such as multiplayer, the save format and world size. Those are asked early and collected under Long-term guardrails.

Engine contracts stay put: ARCHITECTURE_v8.6.md remains the engine specification. Where a design answer would need an engine change, that's flagged as a separate decision, never folded in silently.

Background: the review this follows is Terrain, Play & Scale.

## Pillars
All four activities are core: exploring, digging, building and fighting. Multiplayer is undecided, so nothing built now may block co-op later.
 | 
Decision | 
Answer | 
Status
 | 
Core activities | 
Exploring and discovery; digging and mining; building and bases; combat and bosses. All four. | 
Decided
 | 
One-line brief | 
Terraria in 3D: Minecraft look at 0.1 m blocks, Noita's fluids, Palworld's sense of scale. Terrain as fun to dig through as to look across. | 
Decided (2026-09-25)
 | 
Multiplayer | 
Undecided. Build single-player now; keep co-op possible. | 
Leaning
What "keep co-op possible" means in practice (recorded under Long-term guardrails):

World generation stays a pure function of the seed, so every machine generates the same world. Already true and tested.

Player changes stay a separate record of edits on top of generation, so they can be sent over a network. Already true: the save format stores deltas.

Fluids are the hard part. Their simulation runs on the GPU, near one player. Syncing them between players is an open engine question, not a design one.

## World structure
Difficulty belongs to places, not to distance or player level. High ground, tough terrain and harsh biome × landform pairs are hard wherever they land. The one guarantee is that every start has a clear path to easy ground, so a player can gear up before taking on the hard parts.
 | 
Decision | 
Answer | 
Status
 | 
World shape | 
One large island | 
Decided
 | 
Creating worlds | 
Like Terraria: seeded, player picks a size | 
Decided
 | 
Sizes | 
Small, Medium, Large. Today's ~6 km island is Medium. | 
Decided
 | 
Start | 
The best existing gentle spot the generator finds. Quality varies by seed. | 
Decided
 | 
Player levels | 
None. The world never scales to the player. | 
Decided
 | 
What makes a place hard | 
Altitude, the biome × landform pair, and world progress, together. Not distance from the start. | 
Decided
 | 
Start guarantee | 
From any start there is a path to easy areas that the player can actually travel | 
Decided
 | 
Surface vs underground | 
Equal halves. Both carry real progression; you go up and down to advance. | 
Decided
 | 
Harsh biomes | 
Snow, desert and jungle are harsh on their own. Forest is the mildest. | 
Decided
 | 
Easy land | 
About 10% of the land, in several areas, with easy-ish travel between them | 
Decided
 | 
Small and Large | 
Small about 4 km across, Large about 9 km (Medium about 6 km) | 
Decided
 | 
Biome count | 
Plan for 6–8 biomes (4 exist today). The pair record and the palette are designed for 8. | 
Decided
 | 
New biomes | 
Savanna and badlands first (6), then alpine taiga and volcanic ashlands (8). No corruption- or crimson-style biomes. Details below. | 
Decided

### The eight biomes
The four new ones fill gaps: an easy biome besides forest, a mid step between forest and snow, and homes for the oil and lava show-pieces. All natural, nothing that spreads or corrupts.
 | 
Biome | 
Difficulty | 
What it adds
 | 
Forest | 
Mild | 
Existing
 | 
Savanna (new) | 
Mild | 
Open grassland with long sightlines and room to build. Gives easy land a second look; plains are a top-three Minecraft favourite for exactly this.
 | 
Alpine taiga (new) | 
Medium | 
Pine forest and lakes on mountain flanks, the bridge between forest and snow.
 | 
Badlands (new) | 
Medium to harsh | 
Mesas, canyons and striped rock with ore veins in the walls; the tar seep lives here.
 | 
Jungle | 
Harsh | 
Existing
 | 
Desert | 
Harsh | 
Existing
 | 
Snow | 
Harsh | 
Existing
 | 
Volcanic ashlands (new) | 
Harsh | 
Only around a volcano, when the pool draws one: ash, obsidian, geysers and lava channels.

### What the generator must guarantee (new world rules)

Easy land exists. A minimum amount of low-difficulty ground in every world, in more than one place. The amount and its definition are open questions.

The start is in or next to easy land. "Best spot found" is scored on gentleness, water and room to build.

A travellable path joins the start to the easy areas. Checked at world creation by a path search over the map, the same kind as in the review. A seed that fails gets its start moved, or the path is carved.

Difficulty is readable from a distance. Since hardness comes from height and terrain type, a player can see it coming: snowy peaks, sheer canyons, the volcano.
These are checkable rules, like today's needle test, so every seed can be tested for them automatically.

### Easy ground, in numbers
Easy ground is defined by the land alone: the Walk band, a gentle landform, and the lowest third of the world's height. Biome harshness applies on top, independently.
 | 
Decision | 
Answer | 
Status
 | 
Movement bands | 
Every spot is classed Walk (under about 30°, auto-step), Scramble (steps within jump height), Climb (steep, with perches at least 0.4 × 0.4 m every few metres) or Sheer (no perches: needs grapple, glide or blink). Rest ledges at least 0.6 × 0.6 m every N metres decide what's climbable; N grows with gear (placeholder: 8 m early, 20 m later). Measured on a 0.5 m grid so voxel noise doesn't create or erase routes. Ice and sand climb worse than rock. | 
Decided (numbers are placeholders until the character is designed)
 | 
Gentle landforms | 
River plains, rolling highlands, wetland basins, plateau tops | 
Decided
 | 
Low altitude | 
The lowest third of each world's own height range, so it scales with world size | 
Decided
 | 
Biome in the definition | 
Not part of it. Biomes aren't tied to topography: a snowy plain is easy ground in a harsh biome. | 
Leaning (my reading of your answer; correct it if wrong)
 | 
Start path | 
Uses Walk and Scramble only. Climb counts as one of the "more than one way past" routes. | 
Decided
 | 
Ashlands | 
The one exception to independence: volcanic ashlands appear only around a volcano | 
Decided
 | 
Landform renamed | 
The "badlands" landform becomes eroded breaks (gullied hills); "badlands" now names only the biome | 
Decided

### Underground materials
The underground is a few thick layers by depth, each biome with its own rock in every layer. The thin striped bands go. Variety underground comes from things that matter in play: ores, crystals, fluid pockets, fossils and caves.
 | 
Decision | 
Answer | 
Status
 | 
Structure | 
A few thick layers by depth (soil, stone, deep stone, cavern rock, underworld), tens of metres each, with wavy boundaries. Like Terraria and Minecraft. | 
Decided
 | 
Thin striped strata | 
Removed. They were a default nobody chose, and they made most underground bricks expensive. | 
Decided
 | 
Rock vs biome | 
Each biome has its own rock in every layer, all the way down. A biome border runs down through the layers as a wavy wall of changing rock. | 
Decided
 | 
Underworld | 
The underworld, the hell-like bottom layer from 400 m below sea level, is one shared realm (ash, obsidian, magma, lava sea) under every biome. Every layer above it follows the biome overhead. | 
Decided
 | 
Layered cliffs | 
Cliffs and canyon walls show the biome's surface layers (soil over stone), not stripes. Whether the badlands biome keeps striped rock as its signature is open. | 
Open
Side effect: memory. Thick layers leave nearly every deep brick a single material, so they cost almost nothing. Built and measured 2026-10-01: mixed bricks on mountain ground fell about 45% (849,000 to 458,000–499,000). Every chunk now loads (the backlog went from up to 870 chunks to 0). The memory footprint stays the same until the brick pool, reserved at full size up front, is lowered to match.

## Signature features
Each world draws its big landmarks from a pool, and bigger worlds get more of them. They look natural and exaggerated, with the occasional rare wonder.
 | 
Decision | 
Answer | 
Status
 | 
How many | 
Scales with world size | 
Decided
 | 
Which ones | 
Drawn from a pool per world. Not every world gets every feature; the volcano is one entry, not a fixture. | 
Decided
 | 
How fantastical | 
Mostly natural landforms, exaggerated for drama. Rare wonders allowed, as long as they sit naturally in the land. | 
Decided

### Every signature feature answers four questions

Silhouette: can you recognise it from 2 km away, and from the far band at 5 km?

Approach: is there a route to it, and is the route itself part of the challenge?

Inside: is there something to do in it or on top of it (a cave system, a summit, a crater floor)?

Reward: what is it worth the trip for?
Since difficulty comes from altitude and terrain, a signature feature is usually also a difficulty marker: you can see from afar that the volcano is hard.

### The pool
Decided (proposed in the doc, confirmed 2026-10-01). Picked from what players describe as their favourite places to explore across Minecraft, Terraria, Elden Ring, Subnautica and Zelda. Fluid show-pieces (see Fluids) sit inside or beside these.
 | 
Feature | 
Tier | 
Why it's in
 | 
Great peak | 
Common | 
Summit view and glide launch. Mountains are a top-two Minecraft favourite ("majestic"); Zelda uses big peaks as its landmarks.
 | 
Great canyon | 
Common | 
Ore veins in the walls, routes along them, and land that hides then reveals.
 | 
Giant waterfall | 
Common | 
Water show-piece, with a cave behind it as the hidden way past.
 | 
Table mountain or mesa | 
Common | 
A climb with a flat top worth reaching; anchors the badlands.
 | 
Sea arches and stacks | 
Common | 
Gives the coast a purpose. Plain ocean is Minecraft players' most-disliked biome.
 | 
Volcano | 
Uncommon | 
Lava show-piece, difficulty you can see from afar, boss arena.
 | 
Crater lake | 
Uncommon | 
A calm reward at the top of a hard climb.
 | 
Sinkhole or cenote shaft | 
Uncommon | 
A way down you can see from the surface, like Zelda's chasms into the Depths.
 | 
Underground sky cavern | 
Rare wonder | 
A vast cavern with a glowing ceiling at the end of a long descent. Elden Ring's Siofra River and Subnautica's Lost River are named again and again as favourite moments.
 | 
Colossal stone arch | 
Rare wonder | 
Visible from the far band, and a route in its own right.
Per world: Small gets 3 features, Medium 5, Large 7. Rare wonders: up to 1 in Small, 1 in Medium, up to 2 in Large. Every world gets at least one way-down feature (canyon, sinkhole or sky cavern), since the underground is half the game.
What players praise most, turned into placement rules:

Hide, then reveal. Ridges between features so each one comes into view as you crest a rise (Zelda's "triangle" rule).

The best reveals are underground. A descent that opens into a huge space beats another surface landmark.

Dense is not the same as fun. Jungle is Terraria's favourite but one of Minecraft's most disliked for being hard to move through. Keep corridors through dense biomes.

## Fluids
Fluids are all four things: hazard, tool, resource and spectacle. Rivers and waterfalls stay still until disturbed, and then the disturbed part behaves like real water fed by an endless source.
 | 
Decision | 
Answer | 
Status
 | 
Roles | 
Hazard, tool, resource and spectacle, all four | 
Decided
 | 
Rivers and waterfalls | 
Look flowing, stay still until the player disturbs them (proposal below). Prototype a small river and waterfall in the Playground before deciding. | 
Leaning
 | 
Big player-caused events (break a dam, flood a valley, divert lava) (see also Fluid budget) | 
Allowed, but bounded by the fluid budget so frame rate holds. Revisit once performance work is done. | 
Leaning (my pick, at your request)
 | 
Fluid spectacles | 
Every fluid gets at least one show-piece, not just water (list below) | 
Decided
 | 
Fluid budget | 
Late game gets very chaotic: Noita and Terraria, in 3D. Live fluid keeps at least today's reservation (549 MB of GPU memory for 750k cells and 1,024 tiles, measured 2026-10-01). The 3 GB has to be found elsewhere: terrain, cascades and pool sizes. | 
Decided

### How "looks flowing, stays still" would work

Undisturbed river: still water voxels. The renderer animates the surface along the flow direction the generator already computes for every map cell, so it reads as moving. Costs nothing to simulate.

Undisturbed waterfall: a curtain of dormant water down the cliff face, also animated, with spray where it lands. Like the river, it isn't simulated while nobody touches it.

The player digs next to a river: the touched cells wake up and real water pours into the hole. River cells act as endless sources, like Minecraft's, so the river refills instead of draining away. Your hole floods; the river stays a river.

The player cuts into or blocks a waterfall: the top of the curtain acts as a source and real water spills from it, so you get a true, simulated waterfall while you're nearby. Walk away and it settles and sleeps again.

Engine cost: one new rule in the fluid sim, "source cells refill", plus surface animation in the shader. Much smaller than full river simulation. It's still an engine change, so it gets its own measurement before it ships.

### Fluid spectacles (candidates)
 | 
Fluid | 
Show-piece
 | 
Water | 
Giant waterfall, geyser field, flooded cenote
 | 
Lava | 
Lava falls and channels down the volcano, a lava lake that glows at night
 | 
Oil | 
A tar lake or oil seep in the badlands
 | 
Honey | 
A giant hive in the jungle, dripping
 | 
Acid and sludge | 
A poisonous cavern lake underground
 | 
Quicksand | 
A sinking basin in the dunes

## Settlements and object placement
Towns do everything: services, safety, quests and marking progress. The world generates some, players can found more, and every town can be changed, with residents who react.
 | 
Decision | 
Answer | 
Status
 | 
What towns are for | 
Services and trade; safe haven (respawn, rest, protection); quests and story; progress marker (towns grow or unlock as the world advances) | 
Decided
 | 
Who builds them | 
Both: the world generates villages and towns, and players can found settlements NPCs move into | 
Decided
 | 
Editing generated towns | 
Fully editable, and NPCs react (anger, leaving, asking for repairs) | 
Decided

### What this asks of the generator and the engine

Town sites come from the site plan (the review's placement pipeline): chosen at world creation from flat ground, water and the biome × landform pair, with land shaped around them.

Since towns mark progress, some sit near hard ground. A frontier town at the foot of the volcano or the snow line, not only on the easy plains.

Editable towns are terrain. Built from voxels, they cost memory like terrain (an estimated 20–80 MB for a town while you're near it) and are saved as edits only where the player changes them.

"NPCs react" needs to know which voxels belong to a town. Placement has to record each structure's footprint, which the world record (review section 6) is meant to hold.

### Object placement
 | 
Decision | 
Answer | 
Status
 | 
Density | 
Varies by biome × landform pair: dense jungle, sparse desert, open tundra | 
Decided
 | 
Ores and materials | 
From all four sources: depth (rarer deeper), the biome × landform pair, veins exposed in cliffs and canyon walls, and rich deposits at signature features | 
Decided
 | 
Settlement count | 
Scales with world size | 
Decided
 | 
Trees and plants: voxels or meshes | 
Decide from measured numbers. A test forest is built both ways and its memory and frame cost compared. | 
Open, needs a measurement
The trees question is the most expensive one to reverse: voxel trees become part of saves and terrain memory, mesh trees need a separate object system. It gets a measurement before any placement code is written.

## Progression
The world moves forward, not the player: bosses, conquered landmarks, growing towns and time all advance it. Every hard place has several ways in, and the player picks theirs.
 | 
Decision | 
Answer | 
Status
 | 
What advances the world | 
Boss defeats, conquering landmarks, town milestones, and time | 
Decided
 | 
What changes when it does | 
New creatures and dangers, new materials, towns grow and new NPCs arrive | 
Decided
 | 
New places appearing (a floating city, an underground vault) | 
Wanted; how it works is proposed below | 
Leaning
 | 
How the player takes on hard places | 
Movement abilities, gear tiers, consumables, and plain skill and knowledge. The player chooses their own way. | 
Decided
 | 
Where bosses live | 
A mix: some in arenas at signature features, some summoned | 
Decided

### Every barrier has more than one way past
Because players choose their own way, each barrier the generator places should offer at least two of: climb it (ability), dig through it (gear), wait it out with protection (consumable), or find the hidden route (knowledge). A cliff band with a cave behind a waterfall is the pattern. This becomes a generator rule like the start-path rule.

### Adding places vs changing places
You asked whether adding new terrain later differs from changing existing terrain. It does, a lot.

Adding into reserved space is cheap and safe. At world creation the site plan reserves the space: empty sky where a floating city will appear, a sealed cavity where a vault will open. Nothing else is generated there, so when world progress reveals the place, it doesn't fight the land around it. Generation stays a pure function of the seed plus the world's progress stage, so it stays deterministic (and co-op-ready).

Changing existing land is expensive. Eruptions reshaping slopes, or a corruption-style zone spreading, rewrite ground the player may already have dug or built on. Every rewritten chunk becomes saved edits, and the change has to apply to chunks that aren't loaded. One manageable middle ground is changing materials only (a zone turning to ash or crystal) as each chunk loads, which doesn't move any ground.

Things to plan for with additions:

The far band draws only a height map, so a floating city is invisible from a distance unless the far band learns to draw floating shapes. Easier: a beacon or a light visible from afar.

The player may have built into the reserved space before the reveal. Either reserved space is protected, or the reveal adapts around player blocks.

An underground addition is easiest as a cavity carved at world creation and kept sealed until revealed.

### World changes supported, and new ores
 | 
Decision | 
Answer | 
Status
 | 
Later world changes | 
Additions into space reserved at world creation only. Zones don't change what they're made of. | 
Decided
 | 
Player blocks in reserved space | 
The reveal adapts around them; nothing of the player's is destroyed | 
Decided
 | 
New ores after progress | 
Proposal: latent ores. The ore is generated at world creation but looks and mines as plain rock until the world stage unlocks it. No later rewriting of ground, no growth in save size, and still deterministic. Deeper or harder places that open through abilities and gear add more on top. | 
Leaning
New ores "appearing" doesn't need material changes: if the ore was always there and only becomes visible and mineable later, nothing in the world is rewritten. Terraria's hardmode ores, which really are written into existing stone, would be the expensive version.

## Planned abilities and travel
The character stays a placeholder. These are the abilities the terrain must be designed for, including flight late in the game.
 | 
Decision | 
Answer | 
Status
 | 
Movement abilities | 
Climbing, gliding, grappling, swimming and diving | 
Decided (planned, not built)
 | 
Travel | 
Boats, fast travel, and flight at some point | 
Decided (planned, not built)
 | 
Emergent movement | 
Further abilities may come out of player builds, the way spells combine in Noita. Nothing specific yet besides flight. | 
Leaning
 | 
Extra moves | 
Yes, and more: dash, double jump, wall-run and wall-jump, blink and teleport. Expect a very mobile character. | 
Decided (planned, not built)

### What this means for the terrain

Barriers in tiers, not exact numbers. Abilities will be tuned and combined in ways nobody can predict, so barriers are designed as bands ("needs climbing", "needs gliding"), not "a 4.2 m wall".

The land has to stay interesting once you can fly. Late-game flight erases ground barriers, so the world needs things worth finding from the air: summits, sky additions, sea stacks, entrances seen from above.

Gliding needs height and wind-room. Signature peaks and table mountains double as launch points, with open land downhill to glide over.

Boats need water worth sailing. Islets, sea arches, coves and navigable rivers; the coast becomes a route.

Flight speed is an engine limit. Today the streamer generates about 100 chunks a second, so fast flight outruns the detail and you see the far band until it catches up. A target flight speed is a long-term guardrail (next section).

### A very mobile character changes the terrain's job
A barrier here means terrain that stops or slows you until you have a way past: a cliff too tall to jump, a gap too wide to cross, a river too wide to swim, a zone too cold to survive. With climbing, gliding, grappling, dashing, double jumps, wall-runs, blinks and eventually flight, walls and gaps stop almost nothing for long.
So, as in Terraria (also a very mobile game), progress is gated mainly by danger and materials, not by walls:

Hazards gate places: cold, heat, lava, acid, depth, and the creatures living there. Consumables and gear answer them.

Materials gate power: what you can mine depends on your tools, and the good materials sit in the hard places.

Terrain's job shifts from blocking to rewarding: routes that are fun to move through, verticality worth climbing, secrets worth finding. Physical barriers matter mainly early, before the abilities arrive.
This replaces any idea of the terrain as a lock-and-key maze. The start-path rule and "more than one way past" still apply.

## Long-term guardrails
These are the decisions that are expensive to undo. Where an answer is still open, the rule is to keep the option open, not to pick by accident.
 | 
Guardrail | 
Decision or rule | 
Status
 | 
Minimum spec | 
Apple M1, 8 GB, at 1080p output | 
Decided
 | 
Memory | 
3 GB for the whole process | 
Decided
 | 
Platforms | 
macOS now. Others are open: see "Portability" below. | 
Open
 | 
Multiplayer | 
Keep co-op possible: generation stays a pure function of the seed (and the world's progress stage); player changes stay a separate record of edits | 
Leaning
 | 
Old worlds after updates | 
Undecided. Until decided, every world records the generator version it was made with, so either policy stays possible. | 
Open
 | 
Mods | 
Undecided. Content stays in tables, not scattered through code, which costs nothing and keeps content mods possible. | 
Open
 | 
Late-game flight | 
Fast, about 60 m/s | 
Decided
 | 
Later world changes | 
Additions into reserved space only; latent ores instead of rewriting rock | 
Decided / Leaning
 | 
Internal resolution | 
960 × 540 minimum before upscaling to 1080p (the Quality preset). 480 and 640 are below the floor. Measured 2026-10-01 walking on the new island: 960 takes about 33 ms (around 30 FPS) against 12 ms at 480. Decision: keep 960 as the floor and make the renderer about 2× faster to reach 60 FPS on the M1. Until then the M1 runs at about 30 FPS. 960 is now the default. | 
Decided

### Fast flight is an engine commitment
At 60 m/s, a player crosses the 128 m detail band in about 2 seconds. Keeping full detail around them would need roughly 700 new chunks a second, against about 100 today: about seven times the generation and upload throughput. Air-only chunks are nearly free, so flying high is much cheaper than skimming the ground. The realistic options: more streaming throughput, a smaller detail band while flying fast, or accepting the far band at speed with detail filling in when you slow down. This is an engine design question for later, but the 60 m/s target now sits next to the 3 GB cap as a limit every engine change is checked against.

### Portability (estimates, not measured)

Windows PC: probably the easiest port. The engine is Unity 6 with Burst and HLSL compute shaders, all cross-platform. Untested risks: shaders have only ever been compiled and checked for Metal, and the CPU-to-GPU upload path was tuned for the M1's shared memory. On a PC graphics card, the GPU half of the memory moves to video memory, which helps.

Steam Deck: possible, but its GPU is weaker than the M1's. It would need the lowest preset and a lower internal resolution.

Consoles: possible with Unity, but strict memory limits and certification make them a separate project.

### What 1080p at 60 FPS needs (estimate from M1 measurements)
 | 
Internal resolution (upscaled to 1080p) | 
M1 measured | 
GPU needed for 60 FPS, roughly
 | 
480 × 270 (Performance preset) | 
10–14 ms walking, 15 ms flying | 
M1. Meets it today, apart from stutter episodes
 | 
Balanced preset | 
19.7 ms flying | 
About 1.5× an M1 (an M1 Pro class GPU)
 | 
960 × 540 | 
about 55 ms, before recent optimisations | 
About 3–4× an M1 (M1 Max, or a mid-range PC card)
The stutter while moving is CPU work (terrain streaming), and a faster GPU doesn't fix it. These multipliers assume cost scales with pixels; real hardware could differ either way, and none of it is measured off the M1.

## Open questions
Eleven questions are still open. Only the first three block terrain work; the rest can wait for their own phase.
 | 
# | 
Question | 
Why it matters | 
Decide by
 | 
1 | 
What exactly is "easy" ground in numbers (slope, altitude, biome)? | 
The generator has to guarantee about 10% of it and a path to it from the start | 
Decided 2026-10-01, see Easy ground, in numbers
 | 
2 | 
Which features are in the signature pool, and how many per world size? | 
The generator places them first and shapes land around them | 
Decided 2026-10-01, see The pool
 | 
3 | 
The 2–4 new biomes, to reach 6–8 | 
The biome × landform record and the palette are sized for them | 
Decided 2026-10-01, see The eight biomes
 | 
4 | 
Rivers and waterfalls: does "still until disturbed, then endless source" feel right? | 
Decides the one new fluid-sim rule | 
After a Playground prototype
 | 
5 | 
Trees and plants as voxels or meshes | 
The most expensive placement choice to reverse | 
After a test forest is measured both ways
 | 
6 | 
What happens to old worlds when the game updates? | 
Decides how freely the generator can change after release. A version number is recorded meanwhile. | 
Before the first public build
 | 
7 | 
Platforms beyond macOS | 
Decides shader testing and the true minimum spec | 
Before the first public build; a Windows test build is a cheap first step
 | 
8 | 
Mod support | 
Content stays in tables meanwhile, which keeps it possible | 
Later
 | 
9 | 
How fast flight keeps up with streaming (60 m/s target) | 
About 7× today's chunk throughput, or a smaller band, or pop-in | 
When flight is built
 | 
10 | 
How a sky addition (floating city) is seen from far away | 
The far band draws only a height map | 
When additions are designed
 | 
11 | 
How many world-progress stages, and which bosses live at landmarks | 
Decides reserved sites and latent-ore tiers | 
Combat and progression design phase
 | 
12 | 
Should huge player builds show up from far away? | 
Island-wide, Minecraft-scale building is fine for memory (only the ~128 m around you is loaded). But the far band past 128 m is drawn from the generator, so a megabuild 300 m away looks like the original hillside. Fixing it means feeding player edits into the far band: an engine task. | 
Before building is designed
 | 
13 | 
Entity physics: Unity PhysX (rigidbodies, a cage of box colliders round each entity near the player) or the current voxel-probe motor, for NPCs, projectiles and drops? | 
PhysX gives entity-vs-entity collision for free. Its costs grow with entity count (~540 voxel reads per cage per frame), tiled boxes snag rigidbodies at seams, and fast projectiles still need the voxel ray sweep. Likely split: PhysX between entities, cages only near the player. The player stays on the voxel motor meanwhile. | 
When NPCs are designed: one measured test with 10 / 50 / 100 walking NPCs. Not queued (owner, 2026-10-02).

### What happens next

Terrain track: with questions 1 and 2 answered, plan the generator rebuild against this brief's rules: start path, about 10% easy land, readable difficulty, signature pool, more than one way past, and reserved space for additions.

Engine track: the walking-height run (2026-10-01) put memory at 3,328 MB: about 1.6 GB on the GPU, about 1.5 GB of Unity native memory, and the rest small. Next is a per-buffer breakdown inside those two, then the one measured lever (3 m brick geology), measured on its own.