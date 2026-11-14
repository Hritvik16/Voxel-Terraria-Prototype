# Game design -- DRAFT, owner answers pending

Repo copy of the Claude Doc "Game design" (https://claude.ai/artifact/BprGXbxRr6W5MPdDAXxYp8), task 202, 2026-11-14.
The DOC is the live version (the owner answers there); this file is a snapshot. Plan only, no code.
CLAUDE.md rule 5: `ARCHITECTURE_v8.6.md` is engine only and is not cited here. The game-design brief stays the authority
for terrain; its Decided rows bind. This doc adds everything else on top of it.

**DRAFT -- owner answers pending.** It collects what the owner has already decided about the game, marks what is still
open, and ends with questions. Nothing here is new design by the agent: a thing the owner has not said is marked Open.

- **Decided**: the owner said it; the source and date are given. It binds the work.
- **Leaning**: a preference the owner may revisit, or the agent's reading of an answer.
- **Open**: not yet asked or not yet answered. A question at the end covers the important ones.

## The pitch

**"Terraria in 3D: Minecraft look at 0.1 m blocks, Noita's fluids, Palworld's sense of scale. Terrain as fun to dig through as to look across."** (owner's one-line brief, 2026-09-25)

The world is one large island that you keep, chosen by seed and size. From the top of a 600 m peak you can see coast to coast; 500 m under your feet the rock ends in a lava sea. Every block is 10 cm, so a hillside is something you can dig into, carve, flood or blow apart.

You start on gentle ground with a clear path to more of it. The hard places are visible from far away: snowy peaks, sheer canyons, a volcano. They are hard because of where they are, not because of your level. The good materials are in those hard places, so you go there when your gear is ready.

The world is made of materials that act. Water and lava make stone and steam, fire spreads and oil burns, acid eats rock, sand and snow fall and pile. You can throw fluids yourself, for example with a flamethrower. Explosions really blast the land, up to a 50 m nuke late in the game, but nothing collapses just because it is unsupported.

Between the wild areas sit towns: safe stops on the roads that cross the island. They have trade, rest, quests and residents who react to what you do to their town. You can build your own settlement too, with quick click-and-drag strokes in the spirit of Lay of the Land, and reuse anything you built as a brush.

You fight with gear and learned skill moves: "anime-esque fun combat that can be fast paced or slow depending on the player" (2026-10-15). A weapon's material grade is one half of your power, the moves you learn are the other, and moves combine with unique effects. Beating certain bosses flips the whole world into its next stage, with new ores and new creatures.

You move a lot: swim, dive, climb, glide, grapple, dash, wall-run, and late in the game fly at about 60 m/s. At night the island can be lit by "a million lights" (2026-10-15).

The first rule for now is the look: "I want the game to look fantastic first" (2026-11-05).

## The design, area by area

Most of the world and its rules are Decided; combat details, survival rules, creatures and the minute-to-minute loop are mostly Open. Sources, short names: **Brief** = the game-design brief (2026-10-01; `GAME_DESIGN_BRIEF_SNAPSHOT.md`); **Round b1-b6** = `GAMEPLAY_DESIGN_ROUND_2026-10-15.md`; **P1-P6** = placement planning batch 1 (Queue decisions doc, Answered tab, 6 Nov); **V1-V4, T1-T4, W1, G1** = the 13 Nov talks (`tools/overnight/OWNER_TOPICS.md`); **Plan Pn** = the agent's picks in `GAMEPLAY_PHASE_PLAN.md` (Leaning until the owner confirms).

### World and travel

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| World | One large island per seeded world. Small ~4 km, Medium ~6 km, Large ~9 km across. Peaks to ~600 m, rock to 500 m below sea level. | Decided | Brief; Overhaul §2.2 (2026-09-25) |
| Surface vs underground | Equal halves; both carry real progression. | Decided | Brief |
| What makes a place hard | Altitude, the biome x landform pair and world stage. Never distance from the start. No player levels. | Decided | Brief |
| Start | The best gentle spot found, with a Walk/Scramble path to easy land (about 10 % of the land, in several areas). | Decided | Brief |
| Biomes | Eight: forest, savanna (mild); alpine taiga, badlands (medium); jungle, desert, snow, volcanic ashlands (harsh). Nothing that spreads or corrupts. | Decided | Brief |
| Landmarks | Drawn per world from a pool (great peak, canyon, waterfall, mesa, sea arches, volcano, crater lake, sinkhole, sky cavern, stone arch): Small 3, Medium 5, Large 7; at least one way down. | Decided | Brief |
| Roads and the danger map | Keep the danger map. The routes become the roads between towns; towns are the safe stops; land off the roads stays wild. | Decided | V4 (a), 13 Nov |
| Gating | Progress is gated by danger and materials, plus a few boss gates; not by walls. Every barrier has at least two ways past. | Decided | P2 (c), 6 Nov; Brief |
| Movement | Swim, dive, double jump, dash and grapple are built. Climb, glide, wall-run, blink and teleport are planned. | Decided (planned) | Brief |
| Travel | Boats, fast travel, and flight at about 60 m/s late in the game. | Decided (planned) | Brief |
| How fast travel works | Waystones, towns, a map click, a cost? | Open | -- |
| Live reach | Everything within 128 m simulates on every machine; herds and birds further out are visual only. | Decided | Round b6 |
| When you leave | Areas freeze and catch up on return. Fire, lava and floods, powders and town life keep running coarsely far away. | Decided | Round b2-b3 |
| Map | A relief minimap always on, plus the 3-D map on a key; "explored only" comes with gameplay. | Leaning | Overhaul §12 (recommended, not confirmed) |

### Settlements

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| What towns are for | Trade and services; a safe haven (respawn, rest, protection); quests and story; a marker of world progress. | Decided | Brief |
| Who builds them | The world generates villages and towns; you can found settlements that NPCs move into. | Decided | Brief |
| Editing towns | Generated towns are fully editable; residents react (anger, leaving, asking for repairs). | Decided | Brief |
| How many | Scales with world size. | Decided | Brief |
| Ground | Flatten only under each building. Slopes get foundations, steps, stilts or terraces. Today's towns (task 190) still flatten a whole disc; this replaces that. | Decided | V1 (a), 13 Nov |
| Layout | Streets first: paths grow from the route along easy ground and buildings line them. A city is more rules: a square, districts, walls, a castle on high ground. | Decided | V2 (a), 13 Nov |
| Buildings | Made by rules: footprint, floors, roof, biome materials, details; WFC for facades and interiors. They fit any ground, even a cliff. The owner asked for "fantastic looking towns", castles, and "a lot of unique procedurally generated buildings". | Decided | V3 (a), 13 Nov; Questions tab |
| Placement order | Resources first, then towns, then enemy lairs. | Decided | P6, 6 Nov |
| Size tiers | Today: outpost, village, town, frontier town (task 187). Where do cities sit, and how many per world? | Open | -- |
| Frontier towns | Some towns sit near hard ground (foot of the volcano, the snow line), not only on easy plains. | Leaning | Brief (text, not a row) |
| Growth | Towns grow and new NPCs arrive as the world advances. | Decided | Brief, Progression |
| Town life far away | Day and night routines, NPCs move, lights at night. | Decided | Round b3 |
| Your own settlement | What makes NPCs move in (beds, a hall, safety)? | Open | -- |
| Safety | Is a town a no-spawn zone, walled, guarded? | Open | -- |

### Resources and materials

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| Ores | From depth (rarer deeper), the biome x landform pair, veins in cliff and canyon walls, and rich deposits at landmarks. Rule ores are on by default (task 195). | Decided | Brief |
| Pick ladder | Stone, copper, gold, iron, cobalt, then emberstone and meteor ore; rock is never refused, only slower. | Leaning | Plan P30 (task 105) |
| What you pick up | The exact material you dug, counted in voxels, picked up automatically. | Leaning | Plan P2-P4 |
| Ores after a stage flip | New ores and biome spread only in never-touched rock, by one pass over the world. Your builds and digs are never overwritten. | Decided | Round b6 |
| Underground | A few thick layers by depth, each biome its own rock; one shared underworld (ash, obsidian, lava sea) from 400 m down. | Decided | Brief |
| Trees | 0.1 m cubes, closer to real trees (banyan, massive redwood); 2-3 species per biome; recognisable but each tree unique. Trees first; bushes, grass and flowers later. | Decided | T1, T3 (a), T4 (a), 13 Nov |
| Tree method | The owner wants to see examples before choosing (task 200's gallery; task 210 queued). | Open | T2, 13 Nov |
| Wood and plants as materials | Do trees give wood, fruit, fibre? Do plants regrow? | Open | -- |
| Fluids | Water, lava, oil, acid, honey, sludge, quicksand. Each is a hazard, a tool, a resource and a spectacle. | Decided | Brief |
| Show-pieces | Every fluid gets at least one (lava falls, tar lake, dripping hive, acid cavern lake, quicksand basin). | Decided | Brief |
| Ponds and pockets | Every fluid, with irregular natural outlines, fed by streams (plan 204). | Decided | W1 (a) + (e), 13 Nov |
| Rivers and waterfalls | Look flowing, stay still until disturbed; then real water from an endless source. | Leaning | Brief |
| Reactions | Water + lava to stone + steam, fire spreads, oil burns, acid eats rock, powders fall and pile; heat (melt, freeze, sand to glass), gas and explosives, electricity, life (mud, moss, regrowth). | Decided | Round b1, b5 |
| Fluids as weapons | Elements react, and you can throw fluids, for example a flamethrower. | Decided | P4 (a), 6 Nov |
| Crafting | v1: a recipe table and a craft panel, no stations. Do stations exist later? | Leaning / Open | Plan step 11 |

### Building and editing

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| Strokes | Click and drag, like Lay of the Land: line to plane, cylinder, cone roof, paths drawn on the ground, raise and lower ground. | Decided | Round b4-b5 |
| Face pull | Place a block, pull a face into a line, pull the line into a wall (built, task 93b1). | Decided | Q1, 15 Oct |
| Your build as a brush | Select anything you built and drag with it. | Decided | Round b5 |
| Destroy everything | Explosions blast terrain, up to a nuke of 50 m and more late in the game. | Decided | Round b1-b2 |
| No structural integrity | Nothing falls for lack of support, except powders. | Decided | Round b1 |
| Big builds seen from afar | A megabase or a crater shows from across the island. | Decided | Round b2 |
| Lay of the Land study | How its building system works, then any change to the strokes. | Open | Plan BLD-R |

### Survival

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| Death mode | Chosen per world at creation: soft, classic or hardcore (one life). | Decided | Round b4 |
| Hardcore | The world locks to a fly-and-look viewer. | Decided | Q2 (a), 15 Oct |
| Classic | "You only lose some items"; the exact rule is decided in the placement phase. | Open | Q3, 15 Oct |
| Health and harm | Fall damage (safe to 4 m, lethal at 20 m), 20 s of air under liquids, lava and acid damage; soft death returns you to the start (task 93d1). | Leaning | Plan step 9a (agent's numbers) |
| Hazards | Cold, heat, lava, acid, depth and creatures gate places; gear and consumables answer them. | Decided | Brief |
| Hazard zones | Cold and heat by biome x altitude. | Leaning | Plan step 16 |
| Hunger and food | Never asked. | Open | -- |
| Night | Is night more dangerous, or only darker? | Open | -- |

### Combat and skills

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| Feel | Spells and gear; "anime-esque fun combat that can be fast paced or slow depending on the player". | Decided | Round b3 |
| Whose moves first | Both you and the enemies, on one modular move system; your moves first (wind-up, impact, dashes, air combos, hit-stop). | Decided | P1, 6 Nov |
| Build model | Learned skill moves, separate from the weapon; the weapon's material grade is the other half; moves have unique effects that interact. | Decided | P3, 6 Nov |
| Elements | Elements react, and fluids can be thrown (a flamethrower). | Decided | P4 (a), 6 Nov |
| Spell crafting | Leaning toward Noita's wand slots and modifiers, but the owner found that "too complicated". | Open | Round b4 |
| How moves are learned | Found, taught in towns, dropped by bosses, practised? | Open | -- |
| Weapon kinds | Which families (blade, hammer, bow, thrower, staff)? | Open | -- |
| Our twist | The one thing no other game has. Left open on purpose. | Open | P5, 6 Nov |

### Creatures and NPCs

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| How many | About 50-70 around you at the busiest, like Minecraft. | Decided | Round b1 |
| When you leave | Wild creatures despawn and respawn by biome; town NPCs and named creatures persist. | Decided | Round b3 |
| Far creatures | Herds and birds past 128 m are visual only, so long views are not empty. | Decided | Round b6 |
| Bosses | Some live at landmarks, some are summoned or triggered by events. | Decided | Round b3; Brief |
| Lairs | Enemy lairs and boss arenas, sorted by biome x landform; third in placement order. | Decided | P6, 6 Nov |
| Bodies | Player and NPC characters wait for a humanoid design tool. Static objects and foliage go ahead now. | Decided | P6 note, 6 Nov |
| Physics | Unity PhysX between entities, or the voxel motor? One measured test with 10 / 50 / 100 walkers decides. | Open | Brief Q13 |
| Roster | Which creatures live in which biome? | Open | -- |
| Companions | Tamed or hired creatures? Palworld is named only for its sense of scale. | Open | -- |

### Progression

| Topic | Design | Status | Source |
| --- | --- | --- | --- |
| Who levels up | The world, not the player: boss defeats, conquered landmarks, town milestones and time move it forward. | Decided | Brief |
| What a step changes | New creatures and dangers, new materials, towns grow, new NPCs arrive. | Decided | Brief |
| Stages | 3-4 big jumps; one boss flips the whole world (Terraria Hardmode style). | Decided | Round b4 |
| Later additions | Only into space reserved at world creation (a floating city, a sealed vault); the reveal adapts around your blocks. | Decided | Brief |
| Your power | Weapon material grade plus learned moves, plus movement abilities, consumables and skill. | Decided | P3, 6 Nov; Brief |
| Abilities | All unlocked from the start until stages and bosses gate them. How each is earned later is not set. | Leaning / Open | Plan P15 |
| Stage count and landmark bosses | Exactly how many stages, and which bosses live where. | Open | Brief Q11 |

### The core loop

The world is persistent: one island you keep, single player first, co-op kept possible (Round b1). The three rows below are the agent's reading of the owner's answers, not the owner's words, so they are Leaning until confirmed (question A1).

| Timescale | What you do | Status |
| --- | --- | --- |
| A minute | Dig and gather exact blocks, drag a stroke to build, fight a creature with a move, set off a reaction (water on lava, fire in a forest). | Leaning |
| An hour | Leave a town along a road, go up or down into a harder place, bring back a better material, raise your weapon grade or learn a move, build up a base. | Leaning |
| A session (many hours) | Clear a landmark and beat its boss, flip the world to the next stage, watch towns grow and new ores appear in untouched rock. | Leaning |

## What placement must deliver, and what comes before characters

The placement phase runs in the owner's order: resources, then towns, then lairs, with trees and other static objects alongside (P6, 6 Nov). Every item reports its memory against its switch OFF (owner rule, 14 Nov). Items marked (needs answer) wait for a question below.

### Placement: resources

- [ ] Ores in cliff and canyon walls and rich deposits at landmarks, on top of today's rule ores (task 195).
- [ ] Stage ores: one ore table per stage, placed only in never-touched rock at a flip (Round b6).
- [ ] Ponds and pockets of every fluid with natural outlines (task 203) and streams feeding them (plan 204); the pond look talk (shallow edges, reeds) after.
- [ ] One show-piece per fluid at its landmark: lava falls, tar lake, dripping hive, acid cavern lake, quicksand basin.
- [ ] What trees and plants give you (needs answer A4).

### Placement: flora and static objects

- [ ] Trees: pick the method from the examples (tasks 200, 210); 2-3 species per biome, each tree unique.
- [ ] Bushes, grass and flowers, after trees (T4).
- [ ] Small static objects for the land and towns (the agent's suggestion: rocks, logs, crates, signs; the owner's list may differ).

### Placement: towns and roads

- [ ] A towns plan doc for V1-V4 first.
- [ ] Flatten only under each building; foundations, steps, stilts, terraces on slopes. Replaces task 190's whole-disc flatten.
- [ ] Streets that grow from the route along easy ground, with buildings along them.
- [ ] Rule-built buildings with WFC facades and interiors, fitted to any ground; castles on high ground for cities.
- [ ] The route network drawn as real roads between towns; wild land off them.
- [ ] Every building's footprint recorded, so residents can react to edits (the record exists from task 190).
- [ ] Places for NPCs to use inside buildings: beds, counters, a hall (static only).
- [ ] Reserved space for later additions (a floating city, a sealed vault) in the site plan.

### Placement: lairs

- [ ] Lair and boss-arena sites by biome x landform, and at landmarks.
- [ ] Spawn zones per biome x landform, as data only (no creatures yet).

### Before character work

- [ ] Look finished: lighting, world gen, visuals polish (order of 5 Nov).
- [ ] Performance: 30 fps on the M1 Air at a lower preset, memory under 3 GB.
- [ ] The placement phase above.
- [ ] The humanoid design tool: what it is and who uses it (needs answer A3).
- [ ] Entity physics test with 10 / 50 / 100 walkers (Brief Q13).
- [ ] A combat question round: spell crafting, how moves are learned, weapon families.
- [ ] The classic death rule (Q3, open since 15 Oct).
- [ ] A creature roster per biome.

## Questions for the owner

Batch A comes first because it shapes the placement phase; batch B can wait until combat and survival are designed. In each question the first option is the recommendation. A letter is enough as an answer (answer in the doc).

### Batch A: placement

**A1. Is the core loop above right?**
- (a) Yes, as written: minute = dig, build, fight, react; hour = a trip from a town into harder ground and back with a better material; session = a landmark boss and a world flip. *Unlocks:* a yardstick for spacing, e.g. towns about an hour's trip apart.
- (b) Close; change one row (say which).
- (c) No; describe a typical hour in a sentence or two.

**A2. Cities: how many, and where?**
- (a) One city per Medium world (0-1 Small, 2 Large) on the biggest easy area, with a castle on its high ground. Other sites stay towns, villages and outposts. *Cost:* the biggest single build (a town is an estimated 20-80 MB while you are near it; a city more), so one is the safe number.
- (b) No generated cities; towns grow into cities as the world advances.
- (c) Several cities, one per region.

**A3. The humanoid design tool: what is it?**
- (a) One rule-based generator, like the buildings: body plan, proportions, clothes, biome culture. It makes every NPC unique, and the player creator is the same tool with sliders. *Cost:* the largest option, but one system for both.
- (b) A developer editor where we design NPC bodies by hand; a simpler player creator later.
- (c) Decide when the character phase starts.

**A4. What do trees and plants give you?**
- (a) Each species gives its own wood (a building material in its own colour), and some give fruit. A felled tree leaves a stump and regrows by time passed. *Cost:* about 2-3 new materials per biome out of a limited palette (ids 128-223). *Unlocks:* wood for building and crafting.
- (b) Wood only, one kind; no regrowth.
- (c) Decoration only; nothing to gather.

**A5. How safe is a town?**
- (a) No wild creatures spawn inside a town's area; attacks on towns come only as events later. *Unlocks:* towns become spawn-free zones in the spawn data the lairs step writes.
- (b) Walls and guards, but creatures can still wander in.
- (c) No special rule.

**A6. How does your own settlement get residents?**
- (a) An NPC moves in when you build a room with a door, a bed and a light inside your claimed area (Terraria's rule). *Unlocks:* the list of interior objects placement must provide (doors, beds, lights).
- (b) You place a town-hall item that founds the settlement; residents come over time.
- (c) Decide later.

### Batch B: survival, travel and skills

**B1. Classic death: which items do you lose?** The owner said "you only lose some items" (15 Oct).
- (a) The backpack drops in a bag where you died, kept until you fetch it; the hotbar stays with you. *Cost:* small (one bag marker, no physics).
- (b) Everything you carry drops in the bag.
- (c) Half of each stack drops.

**B2. Hunger and food?**
- (a) No hunger bar. Health refills on its own; food gives short buffs (Terraria). *Unlocks:* fruit and cooking as buffs, no upkeep chore.
- (b) A hunger bar (Minecraft).
- (c) A per-world option, like the death mode.

**B3. What does night change?**
- (a) More and harder creatures outside towns at night; towns light up and stay safe. *Unlocks:* the spawn tables get a night column.
- (b) It is only darker.
- (c) It differs by biome.

**B4. How do you learn skill moves?**
- (a) From trainers in towns and from beating bosses; better moves open as the world advances. *Unlocks:* a reason to visit towns and fight bosses beyond materials.
- (b) Found in the world as scrolls or relics.
- (c) By using a weapon: practice unlocks moves.

**B5. How does fast travel work?**
- (a) Between towns you have visited, along the road network (fits V4: towns are the safe stops). *Cost:* small; towns already have records.
- (b) Waystones you place anywhere.
- (c) None until flight.

**B6. Companions?**
- (a) None for now; decide in the character phase. *Cost:* nothing.
- (b) Hire NPCs from towns.
- (c) Tame creatures (Palworld).

Later batches, not asked yet: spell crafting, weapon families, the number of stages and which bosses live where, and "our twist" (P5, open on purpose).

## Sources

- Game-design brief (https://claude.ai/code/artifact/d31b4321-77c3-4026-93d2-460c190950bf; repo snapshot `GAME_DESIGN_BRIEF_SNAPSHOT.md`, 2026-10-11)
- Queue decisions doc (https://claude.ai/artifact/DrfR6aQJHYmhnQ2Syu33Hq), Answered tab (6 Nov batch P1-P6) and Questions tab (towns, castles, characters)
- `GAMEPLAY_DESIGN_ROUND_2026-10-15.md` (batches 1-6), `GAMEPLAY_PHASE_PLAN.md` (agent's picks P1-P30)
- `tools/overnight/OWNER_TOPICS.md` (13 Nov: V1-V4, T1-T4, W1, R1-R2, G1), `ROADMAP.md` (order of 5 Nov)
- `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md` §2 and §12 (2026-09-24/25)
