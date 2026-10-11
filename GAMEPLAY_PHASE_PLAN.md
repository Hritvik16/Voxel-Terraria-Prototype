# Gameplay phase plan

2026-10-11 · task 63 (planning only: no code, no Unity, no builds, no rigs). Branch `engine-phase`.
Owner, 2026-10-11 ~00:10: "keep queuing tasks, ideally moving forward with gameplay features instead of
optimizations, pick your recommendations, i can always queue something to change them". This plan therefore
OVERRIDES `ROADMAP.md`'s order for now: its step 4 (player and placement) no longer waits for the engine phase.
`ENGINE_PHASE_PLAN.md`'s remaining steps (8.14 shells, coarse-first, far edits, fluids, lighting) resume later;
nothing here cancels them. Tasks 64-66 implement steps 1-3 below.

Design authority: `GAME_DESIGN_BRIEF_SNAPSHOT.md` (Decided rows bind, Leaning rows are the default, Open rows are
picked here). Every pick below is marked **agent's pick (owner may change)**.

## Owner may change (every pick, one line each)

Skim this list; object to any line by queueing a change. "Reverse" = what undoing it costs.

1. **P1 Survival is the default in `./run-play.sh`; Creative (today's brushes) is one F6 row away.** Reverse: flip one default.
2. **P2 Dug material goes straight into the inventory (auto-pickup), no dropped items on the ground.** Reverse: S (drops need entity physics, brief Q13, first).
3. **P3 An item is the exact material you dug** (red sandstone gives red sandstone), except grass-type surfaces give their soil. Reverse: one table.
4. **P4 Amounts are counted in voxels (0.1 m cells), stack cap 10,000 per slot.** Reverse: one constant / display rule.
5. **P5 Inventory = 9-slot hotbar (keys 1-9, wheel) + 27-slot backpack, E opens it, click-two-slots to swap.** Reverse: S.
6. **P6 Placing builds a grid-snapped cube, size 1/2/4/8 voxels (0.1-0.8 m), Q cycles, default 4 (0.4 m).** Reverse: S.
7. **P7 Placement is all-or-nothing:** too few voxels in the slot -> nothing placed, a status line says so. Reverse: S.
8. **P8 Inventory full -> digging still works, the overflow is lost, status line warns.** Reverse: S.
9. **P9 Fluids are not picked up yet** (no bucket until a later step); digging fluid behaves as today. Reverse: later step.
10. **P10 Survival starter kit: 50 torches.** Reverse: one table.
11. **P11 Dev keys (fly on Tab, bomb, chaos, vents, Z/X dig tool, time of day) stay live in Survival** and never touch the inventory. Reverse: S.
12. **P12 Inventory saves beside the world's edits:** fresh world (default) = fresh inventory; `KEEP=1` keeps both. Reverse: S.
13. **P13 First movement set = swim/dive, double jump, dash** (all Decided in the brief; need no new world rules). Reverse: n/a, order only.
14. **P14 Top speeds:** walk stays 5.2 m/s; swim 3.0 m/s; dash peaks at 14 m/s for 0.18 s (~2.5 m), 0.8 s cooldown. Sustained speed stays inside the 7-11 m/s that full detail keeps up with on the M1 (task 28). Reverse: `PlayerConfig.json` numbers.
15. **P15 Every ability is unlocked from the start** until progression exists. Reverse: one flag per ability.
16. **P16 Movement tuning lives in `PlayerConfig.json` (hot-reloaded), not in F6.** Reverse: S.
17. **P17 Ores: five shared ore materials, one per depth layer** (copper / iron / gold / cobalt / emberstone, placeholder names), rarer and more valuable deeper. Reverse: table + regenerate (pre-release worlds are disposable, brief Q6).
18. **P18 The biome x landform pair sets ore DENSITY (a multiplier table) and each biome favours one ore (x2 weight)**; no per-biome ore colours yet. Reverse: table.
19. **P19 Ores are blobs 0.3-1.2 m, placed by integer hashing of the seed**, under 0.5% of rock volume, in map v4 only (v3 unchanged). Reverse: table.
20. **P20 No new map version for ores:** v4 changes in place (no public build yet; brief Q6 open). Reverse: S (bump to v5 instead).
21. **P21 Start at the best easy area** (step 4) for play only; rigs keep `Tg2Spawn`. Reverse: S.
22. **P22 Keep inventory on death** (step 9) until progression gives losing items a meaning. Reverse: S.

## 1. Goal

The first playable loop on the tg2 island, Terraria-like and carried by the overworld: you start on easy ground,
dig into the land and get the exact blocks you dug, build with them from a hotbar, move through the island with
the brief's abilities (swim, dive, double jump, dash, then climb, glide, grapple, wall-run), and find better
material the deeper and harsher you go (ores by depth and by biome x landform), which later buys better tools and
opens harder places. Progress is gated by danger and materials, not walls (brief, "A very mobile character").
Combat, creatures, story, towns and NPCs are NOT designed here; the table names where they need the owner.

## 2. Guardrails (every step obeys these)

1. **The CPU is the sole terrain writer.** Dig and place go through `EditService` (`SetBox`, `SetSphere`,
   `MineSphereBudgeted`, the existing `TryPaintBrush` wake guard), which writes through `ChunkStore.SetVoxel`.
   New methods may be ADDED to `EditService` (e.g. a mining overload that reports which materials it removed);
   existing signatures stay.
2. **Frozen APIs untouched:** `IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, the D.1 delta format, the §4.4
   state-transition table. A step that needs a change to one of them STOPS_FOR_OWNER instead.
3. **No memory-layout change** (brick, chunk, mirror, pool structs; Appendices A/B). New material ids are new
   palette ENTRIES, not a layout change; if the 1-byte id space can't fit them, STOP_FOR_OWNER.
4. **Terrain generator changes (`Assets/TerrainGen/`) run `SUITE=all`**, keep map v3 byte-identical, and use
   integer hashing (not float noise) for anything new, so Burst and Mono agree (KNOWN_ISSUES "Burst vs the
   EDITOR's Mono are NOT byte-identical for float code") and co-op stays possible (brief, Multiplayer: Leaning).
5. **Rigs and capture modes keep their behaviour.** They run Creative (today's brushes, slot indices unchanged:
   `Phase6BrushGuard` keys off slots 0-3) and the unchanged spawn, whatever `PlaySettings.json` says.
   Walk/jump/step-up with no new ability used must stay identical (existing `PlayerMotorTests` unchanged).
6. **New play options go in `PlaySettings` + the F6 menu** (+ a `-flag` that overrides one run); tuning numbers
   go in `PlayerConfig.json`. New keys go in the F2 help panel.
7. **Player state is a separate small file** (e.g. `player.json` beside the world's deltas), never inside D.1.
8. **Content in tables** (items, drops, ores, recipes) under `Assets/ContentModules/` or TerrainGen's data, not
   scattered through code (brief: Mods Open, "content stays in tables").
9. **Memory:** the world is already over the 3 GB cap (KNOWN_ISSUES). A step that adds more than ~20 MB, or
   raises the share of mixed bricks, says so with a number in its HANDOFF. Ores must not undo the thick-layer
   saving (brief, Underground materials).
10. **Acceptance = EditMode tests + screenshots + metrics.** No frame-time, stream or rig measurement (owner,
    2026-10-08). Screenshots well lit: midday, sun behind the camera.
11. **One change per step.** Look-and-feel left open by a step is picked by the worker and listed in HANDOFF
    under "Picks (owner may change)".

## 3. Steps

S = one session, M = one to two, L = three or more (split into a, b, c). Rows marked [measure] decide something.
"Engine N" = `ENGINE_PHASE_PLAN.md` section 3 step N.

| # | Step (one change) | Acceptance | Size | Needs |
| --- | --- | --- | --- | --- |
| 1 | **Inventory, hotbar and survival build/dig (the core loop).** New `Inventory` (plain class, 36 slots: hotbar 9 + backpack 27, stack cap 10,000, P4-P5) and an item table in `ContentModules` (P3: material -> item; grass/jungle grass/savanna grass -> loam/mud/red earth; fluids give nothing, P9; torch items count placements, not voxels). Mining credits the inventory through a NEW `EditService` overload that reports per-material counts (old overload untouched). Right-click in Survival places a grid-snapped cube from the selected slot (P6-P7, `SetBox`; mobile solids such as sand/snow keep the wake-radius guard), costing exactly the cells it fills. F6 row "Game mode: Survival / Creative" + `-gamemode`; play default Survival (P1), rigs/captures Creative. IMGUI hotbar strip at the bottom, E panel, Q cycles place size; starter kit P10; save/load per P12. F2 help updated. | EditMode: inventory add/stack/cap/full/remove/swap; JSON round-trip; dig N voxels from a synthetic `ChunkStore` -> inventory +N of that material; place -> ChunkStore +k cells, inventory -k (conservation, both ways); refuse when short; grid snap is bitwise (`& ~(n-1)`); Creative path unchanged (brush indices 0-3). Screenshots: hotbar after digging into a hillside; E panel with 3+ materials; a wall built from dug blocks beside the hole it came from. | M | -- |
| 2 | **Movement set A: swim/dive, double jump, dash** in `PlayerMotor` (P13-P16). Swim when >= 50% submerged (the existing `Buoyancy` probes): WASD moves along the look direction, Space up, Left Ctrl down, 3.0 m/s cap, buoyancy keeps you at the surface when idle; works in water, oil, sludge (slower by the fluid's drag), lava (no damage until step 9). Double jump: one extra jump in the air at 0.85x the jump impulse, reset on landing or entering fluid. Dash: Left Shift, 14 m/s for 0.18 s along the move direction (look direction if none), 0.8 s cooldown, one per airtime, swept through the existing substep collision so it can't tunnel. Numbers in `PlayerConfig.json`. HUD shows dash cooldown. F2 updated. | EditMode on a synthetic ChunkStore: swim up/down/level reaches 3.0 m/s and no more; idle swimmer floats to the surface; double jump only once per airtime, resets on landing; dash covers 2.3-2.7 m on flat ground, stops at a wall without entering it (0 penetration), cooldown respected; with no new input, every existing `PlayerMotorTests` case unchanged. Screenshots: underwater view of a lake bed while diving; mid-double-jump over a gap; a numbered top-speed table in HANDOFF (walk / swim / dash peak / fall terminal). | M | -- |
| 3 | **Ores by depth and biome x landform (map v4).** Five ore materials (P17) in free GenMat codes + palette colours (`tools/gen-tg2-materials.py`), one per layer: copper in stone, iron in deep stone, gold in deeper deep stone, cobalt in cavern rock, emberstone in the underworld (rarer deeper, per a layer table). Placement: integer-hash blobs 0.3-1.2 m (P19), only replacing rock (never air, fluid, soil or surface), density x the pair multiplier from `WorldRecord` (landform x biome table, P18; e.g. mountains and eroded breaks richer, plains and wetlands poorer), each biome favouring one ore. v3 untouched. Mined ore lands in the inventory through step 1's table (no table edit needed beyond the new ids). | `SUITE=all` green (map hashes re-pinned for v4 only, v3 byte-identical). New tests: same seed -> same ore hash (twice, and Burst vs Mono if the harness allows); every ore cell within its layer's depth band and inside rock; per-pair density within +-25% of the table over a sampled set of chunks; total ore < 0.5% of rock. Metrics report (EditMode, `REPORTS=1`): ore count per layer x biome, and mixed-brick share before -> after on the same chunks (target: rise <= 5%). Screenshots: dev map's underground slice (O) with ore visible; a cave wall in play with ore blobs; the hotbar holding dug ore. | M | 1 (only for the "dug ore in hotbar" shot) |
| 4 | **Start on the best easy area** (brief Start: Decided). Play spawns in the largest/most central easy area from `EasyAreaList` (B4), on Walk-band ground, never in water; R respawns there; position saved with P12. Rigs and captures keep `Tg2Spawn`. | EditMode: spawn cell is FLAG_EASY, Walk band, dry, deterministic per seed (3 seeds). Screenshot: the first frame of play on 2 seeds. | S | -- |
| 5 | **Walkable slope + climbing** (brief Movement bands: Decided; fixes KNOWN_ISSUES "runs up ~70° slopes"). Auto-step only on Walk-band ground (< ~30°, measured over 0.5 m); steeper ground slides you back unless you jump (Scramble) or climb: hold toward a Climb-band face to cling and climb at ~1.5 m/s, stamina bar, rest on 0.6 m ledges; ice and sand climb worse. | EditMode: 25° ramp walkable, 45° not walkable without jumping, 70° climbable with stamina, stamina refills on a ledge; existing step tests still pass for 3-voxel steps on flat ground. Screenshots: climbing a cliff, sliding off a 50° slope. | M | 2 |
| 6 | **Gliding** (Decided). Hold Space in the air after the double jump; sink rate ~2 m/s, forward ~10 m/s; peaks and table mountains are launch points. | EditMode glide ratio and landing. Screenshot from a summit glide. | S | 2 |
| 7 | **Grappling hook** (Decided): fire at solid within 25 m (`VoxelRayWalker`), reel in, swing. | EditMode: hook hits solid only, pull stops at the wall. Screenshot: hooked onto a Sheer face. | M | 2 |
| 8 | **Wall-run and wall-jump** (Decided). | EditMode on a synthetic wall. Screenshot. | M | 5 |
| 9 | **Health, fall damage, drowning (oxygen while diving), lava/acid damage, death -> respawn at the start** (P22). Minimal bar HUD; no combat. | EditMode: damage curves, oxygen drain/refill, respawn location. Screenshots. | M | 2, 4 |
| 10 | **Hardness and tool tiers as gear.** A per-material hardness table; the dig rate comes from the held tool vs hardness; each ore needs a tool tier. Survival's Z/X dev tiers retire in Survival (kept in Creative). | EditMode: rate table, "too hard" refusal. Screenshot of the refusal message at an iron vein with a copper tool. | S-M | 1, 3 |
| 11 | **Crafting v1:** a recipe table (pickaxes per ore tier, torches, building blocks), a craft panel in the E screen, no stations yet. | EditMode recipe consume/produce. Screenshot. | M | 10 |
| 12 | **Ore sources 3-4: veins exposed in cliffs and canyon walls, rich deposits at signature features** (Decided). `SUITE=all`. | Same test shape as step 3 + cliff-face screenshot. | M | 3 |
| 13 | **Fluid items (bucket):** pick up and pour water/lava/oil through the existing wake guard; conserves volume. | EditMode conservation. Screenshot. | M | 1 |
| 14 | **[measure] Trees and plants: voxels vs meshes** (brief Open, "needs a measurement"). One test forest built both ways; memory ledger + the A/B frame cost, warm steady state. This is a performance task by the brief's own rule. | Decides Q5 of the brief: owner picks from the numbers. | M | -- |
| 15 | **Trees and plants placed by pair density** (Decided density), in the form step 14 picked. | Placement tests + screenshots per biome. | L | 14 |
| 16 | **Hazard zones:** cold/heat by biome x altitude from the B6 danger map, with consumable/gear answers. | EditMode + screenshots. | M | 9 |
| 17 | **Owner question batch: creatures, combat, bosses, world stages** (brief Q11; combat/story have no document). Planning only. | Owner answers. | S | 9 |
| 18 | **[measure] Entity physics for NPCs/drops** (brief Q13: 10/50/100 walking NPCs). | Owner picks PhysX vs voxel motor. | M | 17 |
| 19 | **Town site plan and towns (B7), NPCs who react** (Decided), after 17-18. | Own plan doc. | L | 15, 17, 18 |
| 20 | **Boats, fast travel; flight at 60 m/s** (Decided). Flight waits for engine steps 10-16 (streaming at the top speed). | Own plan. | L | engine 16 |

Order notes: steps 1-3 are independent of each other in code (step 3's "ore in hotbar" screenshot is the only
link) and none needs an owner decision. Step 3 changes v4's chunks, so engine step 10 (coarse-first generation)
must later treat ore as its host rock at coarse resolution (ore is underground; coarse shells see rock).
Steps 4-13 need no owner decision either; 14, 17, 18 do.

## 4. Risks

- **Ores and memory.** Scattered ore turns uniform deep bricks into mixed ones (thick layers saved ~45% of mixed
  bricks). Step 3 caps volume (< 0.5%) and reports the mixed-brick share; if it rises > 5%, the worker lowers
  density before committing.
- **Free material ids.** tg2 ids are `TG2_BASE (128) + GenMat`, palette tables sized for 128-223; codes 17-19,
  26-29, 55-59, 65-69, 76-79, 93-95 look free. Step 3 checks the palette generator first.
- **Survival vs the dev toolkit.** P11 keeps every dev key live; the loop is honest only if the owner ignores
  them. Cheap to hide later behind Creative.
- **Dash and streaming.** Dash is a burst (~2.5 m), so it doesn't raise sustained speed; flight does (step 20).
- **Placeholder look.** IMGUI hotbar/panels are placeholders; UI style is not decided here.
