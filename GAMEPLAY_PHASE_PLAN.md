# Gameplay phase plan

2026-10-11 · task 63 (first version, agent's picks). **Updated 2026-10-15 · task 92** from the owner's design round
(`GAMEPLAY_DESIGN_ROUND_2026-10-15.md`, batches 1-6 -- the requirement; Decided unless marked OPEN). Planning only: no
code, no Unity, no builds. Branch `engine-phase`. Far-away rules are NOT restated here: they are
`AMENDMENT_8_15_SIMULATION_REACH.md` (ADOPTED 2026-10-15); a step that needs one of its rows says **"Requires 8.15 <row>"**.

## For the owner

- **Changed since 2026-10-11:** your answers replace my guesses. Death is chosen per world (soft/classic/hardcore), not
  "always keep items". Building gets Lay-of-the-Land strokes (line -> plane, cylinder, cone roof, paths, sculpt, your build
  as a brush). New groups: reactions (today's rules vs new simulation), stages (one boss flips the world), creatures,
  gear combat. Spells stay OPEN; those steps wait. Done already: 64 inventory, 65 movement, 66 ores, 66b underwater.
- **New order (first ten):** drag line -> plane stroke -> easy start (67) -> health/respawn -> death mode -> easy
  reactions (lava + water -> stone + steam, mud, glass, melting) -> climb (68), glide (69), grapple (70), wall-run (71).
  Tasks 67-71 stay valid; 72 (health/death) needs a rewrite.
- **Questions (recommendation first):**
  1. Plane stroke: (a) one drag, the box from press to now, one brush thick (flat = floor, upright = wall, thin = line); (b) drag a line, release, sweep into a plane, click; (c) Shift turns the line into a plane.
  2. Hardcore death: (a) the world locks, fly-and-look only (Minecraft); (b) the world is deleted; (c) a new empty-handed character in the same world (Terraria).
  3. Classic death: (a) all you carry drops in one bag where you died, kept until fetched; (b) only the backpack drops; (c) half of each stack drops.
  4. Gameplay vs the engine block (8.15, then lighting): (a) alternate, one gameplay task after each engine task; (b) engine block first; (c) gameplay first.

## Owner may change (one line each)

"owner, 2026-10-15" = decided in the design round; "agent's pick" = still mine, object by queueing a change.

1. **P1 Survival is the default in `./run-play.sh`; Creative one F6 row away.** Agent's pick (done, task 64).
2. **P2 Auto-pickup, no dropped items on the ground.** Agent's pick. Exception: classic death's bag (P22), a static
   marker with no physics.
3. **P3 An item is the exact material you dug** (grass gives its soil). Agent's pick (done).
4. **P4 Amounts in voxels, stack cap 10,000.** Agent's pick (done).
5. **P5 9-slot hotbar + 27-slot backpack, E opens it.** Agent's pick (done).
6. **P6 The brush is today's grid-snapped cube, 1/2/4/8 voxels, Q cycles. Owner, 2026-10-15:** strokes are built ON this
   brush ("pick a brush (today's sized cube), press, drag").
7. **P7 Stroke cost:** a stroke places brush after brush along its path until the slot runs out, then stops; the
   preview shows the cell count before release. Agent's pick (replaces "all-or-nothing", which suited single clicks).
8. **P8 Inventory full -> digging still works, overflow lost.** Agent's pick (done).
9. **P9 Fluids not picked up until the bucket (step 13).** Agent's pick.
10. **P10 Starter kit: 50 torches.** Agent's pick (done).
11. **P11 Dev keys (fly, bomb, chaos, vents, dig tiers, time) live in Survival.** Agent's pick. The bomb becomes a real
    item in RX6 ("destroy everything", owner batch 1); the dev key stays for Creative.
12. **P12 Player state beside the world's edits; fresh world by default, `KEEP=1` keeps both.** Agent's pick. A fresh
    world is also when its death mode (P22) is chosen.
13. **P13 Movement set A = swim/dive, double jump, dash.** Brief Decided (done, task 65).
14. **P14 Top speeds:** walk 5.2, swim 3.0, dash 14 m/s for 0.18 s. Agent's pick (done; `PlayerConfig.json`).
15. **P15 Every ability unlocked from the start** until stages/bosses gate them. Agent's pick.
16. **P16 Movement tuning in `PlayerConfig.json`, not F6.** Agent's pick.
17. **P17-P20 Ores:** five, one per depth layer, density by biome x landform, integer-hash blobs, v4 changed in place.
    Agent's pick (done, task 66). **Owner, 2026-10-15 (8.15 §14):** later stages add ores only in never-touched rock.
18. **P21 Play starts at the best easy area; rigs keep `Tg2Spawn`.** Agent's pick (step 4).
19. **P22 Death is chosen per world at world creation: soft / classic / hardcore (one life). Owner, 2026-10-15**
    (replaces "keep inventory on death"). Meaning of each: soft = respawn, keep everything; classic = Q3; hardcore = Q2.
    Flag `-deathmode soft|classic|hardcore` + a PlaySettings "new world" field; read only when a world is created.
20. **P23 World stages: 3-4, one boss flips the whole world** (new ores, creatures everywhere, some biomes). **Owner,
    2026-10-15.** Engine side = 8.15 §14 (materials only, touched chunks pinned).
21. **P24 Creatures: ~50-70 near you; wild ones despawn/respawn by biome; town NPCs and named creatures persist.
    Owner, 2026-10-15.**
22. **P25 Combat = spells + gear, fast or slow as the player likes. Owner, 2026-10-15.** Spell crafting **OPEN**
    (leaning Noita wand slots, owner finds it complex): gear steps go ahead, spell steps wait.
23. **P26 Bosses: some at landmarks (signature features), some summoned/event-triggered. Owner, 2026-10-15.**
24. **P27 Building = click-and-drag strokes like "Lay of the Land"; also cylinder, cone roof, ground paths, sculpt
    raise/lower, your own build as a brush. Owner, 2026-10-15.** Dig uses the same strokes (LMB): agent's pick.
25. **P28 No structural integrity** (nothing collapses because unsupported, except powders). **Owner, 2026-10-15.**
26. **P29 Single player first; keep co-op possible (deterministic, host-authoritative). Owner, 2026-10-15.**

## 1. Goal

The Terraria-like loop on the tg2 island, with Noita's material world: start on easy ground, dig and get the exact
blocks, build fast with strokes, move with the brief's abilities, find better material deeper and in harsher pairs,
fight with gear (spells later), meet creatures and bosses, and flip the world to the next stage by beating one.
Materials react (water + lava, acid, fire, heat, gas, electricity, growth); powders fall; explosions blast terrain.

## 2. Guardrails (every step obeys these)

1. **The CPU is the sole terrain writer.** Dig, place, strokes, sculpt, reactions written by the CPU go through
   `EditService` -> `ChunkStore.SetVoxel`. New `EditService` methods may be ADDED; existing signatures stay. GPU CA
   reactions keep today's path (the fluid CA's own write-back, §7).
2. **Frozen APIs untouched:** `IWorldQuery.GetVoxel`, `IEditService.SetVoxel`, D.1, the §4.4 table. 8.15's additive
   contract rows (D.4 v2, `IFarQuery`, C1's bytes) are the engine's, not done by gameplay steps. Else STOP_FOR_OWNER.
3. **No memory-layout change.** New materials are palette entries; if the 1-byte id space can't fit them, STOP_FOR_OWNER.
4. **Generator changes run `SUITE=all`**, keep v3 byte-identical, use integer hashing (Burst = Mono; co-op, P29).
5. **Rigs and capture modes keep their behaviour** (Creative, slots 0-3, `Tg2Spawn`, legacy `PlayerMotor.Step`).
6. **Play options in `PlaySettings` + F6 (+ a flag); tuning in `PlayerConfig.json`; new keys in F2.**
7. **Player and world-game state are small separate files** (`player.json`, a world-game file for death mode, prefab
   library, bags), never inside D.1. World stage + clock live in 8.15's C1 world-state file.
8. **Content in tables** (items, recipes, reactions, ores per stage, creatures) under `Assets/ContentModules/`.
9. **Memory:** a step adding > ~20 MB, or raising mixed bricks, gives the number in HANDOFF.
10. **Acceptance = EditMode tests + screenshots + metrics.** No frame-time (owner 2026-10-08) except [measure] rows.
11. **One change per step**; worker picks look-and-feel and lists it under "Picks (owner may change)".
12. **Reach:** everything here acts inside NEAR (128 m, every machine, 8.15). Anything far uses 8.15's rows; a step
    must not shrink reach (owner stopped task 87 over it).

## 3. Steps

S = one session, M = one to two, L = three+. [measure] = a performance task. "8.15 X" = that amendment's §11 row.
Numbers 1-20 keep their 2026-10-11 ids (tasks 67-72 point at them); new groups use letters.

### Done
| # | Step | Task |
| --- | --- | --- |
| 1 | Inventory, hotbar, survival dig/build | 64 |
| 2 | Swim/dive, double jump, dash | 65 (+66b underwater view) |
| 3 | Ores by depth and biome x landform (v4) | 66 |

### Building (P6, P7, P27) -- smallest useful first
| # | Step (one change) | Acceptance | Size | Needs |
| --- | --- | --- | --- | --- |
| BLD1 | **Drag line.** Press, drag, release: the selected brush is stamped along the straight line between the press point and the release point (integer 3D DDA on the brush grid, `& ~(n-1)`), ghost preview + cell count while dragging, Esc cancels; Survival cost per P7; LMB drag digs the same way. One `EditService` call per stroke (added `SetBoxes`/loop over `SetBox`; mobile-solid wake guard kept). | EditMode: line cells exact on 6 directions + diagonals; cost = cells placed; stops when the slot runs out; undo-free (no undo yet). Screenshots: a 10 m wall base drawn in one drag; the preview. | S | 1 |
| BLD2 | **Plane stroke** per Q1 (rec. (a): the box between press and current point, thinned to one brush on its smallest axis). Walls and roofs in one drag. | EditMode: flat drag -> horizontal plane, upright -> wall, thin -> line (BLD1 unchanged). Screenshots: a 4-wall hut + flat roof built in 5 strokes. | S-M | BLD1, Q1 |
| BLD3 | **Cylinder and cone tools** (round towers; sloped/cone roofs): brush shapes chosen on a tool wheel; a drag sets radius and height. | EditMode voxel counts vs the analytic shape (+-1 shell). Screenshot: tower with cone roof. | M | BLD2 |
| BLD4 | **Paths drawn on the ground:** the stroke follows the surface (`VoxelRayWalker` down per step) and replaces the top 1-2 voxels with the path material from the slot. | EditMode on a synthetic slope: path cells lie on the surface, nothing floats. Screenshot: a path over a hill. | S-M | BLD1 |
| BLD5 | **Terrain sculpt raise/lower** along a stroke with a smooth falloff; raise costs soil from the slot, lower gives it (P3). | EditMode conservation (raised = taken). Screenshot: a levelled building plot. | M | BLD1 |
| BLD6 | **Selection -> custom prefab brush:** box-select part of your build, store its voxels (cap 64³ voxels = 256 KB, agent's pick) in the world-game prefab library, then place it with BLD1/BLD2 strokes (stamped at its own size along the stroke); cost = its voxels. | EditMode: copy/paste byte-identical, rotation by 90° steps, library JSON round-trip. Screenshot: a row of copied houses. | M | BLD2 |

Far visibility of builds is the engine's: **8.15 E1** (queued, 93a) and **E2**. Building steps never wait for it.

### Movement, survival, death
| # | Step | Acceptance | Size | Needs | Task file |
| --- | --- | --- | --- | --- | --- |
| 4 | **Start on the best easy area** (P21). Unchanged from 2026-10-11. | as before | S | -- | 67 |
| 5 | **Walkable slope + climbing.** Unchanged. | as before | M | 2 | 68 |
| 6 | **Gliding.** Unchanged. | as before | S | 2 | 69 |
| 7 | **Grappling hook.** Unchanged. | as before | M | 2 | 70 |
| 8 | **Wall-run and wall-jump.** Unchanged. | as before | M | 5 | 71 |
| 9a | **Health, fall damage, oxygen while diving, lava/acid damage, respawn at the start** -- soft death only. Minimal bar HUD. | EditMode damage curves, oxygen drain/refill, respawn location. Screenshots. | M | 2, 4 | 72 (rewrite) |
| 9b | **Death mode per world** (P22): chosen when a world is created (flag + PlaySettings field), stored in the world-game file; soft / classic (Q3 rec.: one bag at the death spot, a static marker saved with the world, fetched by walking into it) / hardcore (Q2 rec.: the world locks to a fly-only viewer). | EditMode: each mode's outcome; the mode survives a `KEEP=1` reload; changing the flag on a kept world does nothing. Screenshot: the bag; the hardcore "world locked" screen. | M | 9a, Q2, Q3 | new |

### Tools, crafting, fluids, hazards
| # | Step | Size | Needs |
| --- | --- | --- | --- |
| 10 | Hardness and tool tiers as gear (unchanged). | S-M | 1, 3 |
| 11 | Crafting v1: recipe table, craft panel, no stations (unchanged). | M | 10 |
| 12 | Ore sources 3-4: cliff veins, rich deposits at signature features (unchanged; `SUITE=all`). | M | 3 |
| 13 | Bucket: pick up / pour fluids through the wake guard, volume conserved (unchanged). | M | 1 |
| 16 | Hazard zones: cold/heat by biome x altitude (unchanged; RX5 later makes cold/heat act on materials too). | M | 9a |

### Reactions (batch 1 + batch 5). Grouped by what the existing material rules can do. The CA is NOT designed here.
**A. Today's rules can do it** (`MaterialRules` pair table: two face-adjacent materials -> two products, already runs
water + lava and acid; `IsFallingSolid`; `IsGas` steam exists):
| # | Step | Size | Needs |
| --- | --- | --- | --- |
| RX1 | **Adjacency reactions, table rows only:** water + lava -> stone (lava cell) + steam (water cell; today: obsidian + air); water + loam/dirt -> mud; sand + lava -> glass (new material); snow/ice + lava -> water. Tests per pair; a screenshot of each. | S | -- |
| RX2 | **Powders everywhere:** gravel, ash and more sands fall and pile (`IsFallingSolid`). Snow too only if the wake-candidate count stays bounded (snow blankets regions; today kept static on purpose): report candidates before/after. Collapses only by powders (P28). | S-M | -- |

**B. Needs new simulation** (each its own model; deps listed, designs in their own tasks):
| # | Step | Size | Needs |
| --- | --- | --- | --- |
| RX3 | **Fire** spreads, oil burns, grass -> ash, smoke. This IS the engine row **8.15 N1** (near fire CA); the gameplay side adds damage and items only. | (N1) + S | 8.15 N1, 9a |
| RX4 | **Life / growth:** moss/fungus spread, ash regrows grass, plants on wet soil. A slow random-tick pass over resident chunks (CPU, hash(seed, cell, tick), deterministic, P29) -- not the fluid CA. Catch-up on return = **8.15 R1** (requires C2). | M | RX1 (mud), 15 for plants |
| RX5 | **Heat:** a coarse temperature field (per brick, agent's pick) -- melt/freeze near fire/lava/cold biomes, sand -> glass by heat. | M-L | RX3 |
| RX6 | **Gas & explosives:** bomb + gunpowder items (blast through the existing destruction path, radius <= ~5 m near); cave gas pockets (a generator change, `SUITE=all`) that ignite; steam pressure bursts. **Nukes 50 m+ require 8.15 B1** (debris budget). | M-L | RX3, 9a; 8.15 B1 for nukes |
| RX7 | **Electricity:** conducts through water and metal per tick, wet targets take more damage. Source (lightning, items, spells) partly waits on spells. | M | 9a, CB1 |

Far away all of B FREEZES and catches up by elapsed time (**8.15 §15, R1**); far fire / lava / floods / powders run
coarsely (**8.15 F1-F4**). Gas and electricity are near-only.

### Combat, creatures, bosses
| # | Step | Size | Needs |
| --- | --- | --- | --- |
| CB1 | **Gear combat v1:** melee swing + one ranged weapon (`ProjectileTrace`) vs a training dummy; hit, knockback, damage numbers; works with dash/double jump (fast or slow play, P25). | M | 9a, 10 |
| CB2 | **Armor/gear stats** from crafted materials (step 11's tables). | S-M | CB1, 11 |
| CB3 | **Spells** -- WAITING: spell crafting is OPEN (P25). A combat/spell question round comes first. | -- | owner |
| 14 | [measure] Trees and plants: voxels vs meshes (unchanged; a performance task). | M | -- |
| 15 | Trees and plants placed by pair density, in 14's form (unchanged). | L | 14 |
| 18 | [measure] Entity physics for 50-70 creatures (brief Q13; now sized by P24's numbers). | M | -- |
| CR1 | **Wild creature spawner:** by biome x landform, ~50-70 near you, despawn beyond NEAR 128 m (8.15 §8), one placeholder creature per pair group. | M | 18 |
| CR2 | **Creature behaviour v1:** wander, flee, attack; uses CB1's damage. | M | CR1, CB1 |
| V1 | Visual-only far creatures (herds, birds past 128 m). **Requires 8.15 V1, Q1.** | M | CR1 |
| BS1 | **First landmark boss** at a signature feature; its defeat calls the stage flip (STG1) or a dev stand-in. | L | CR2, STG1 |
| BS2 | Summoned / event bosses (P26). | M | BS1 |
| 19 | Towns and NPCs who react (unchanged; plan doc first). Persistent residents (P24); far town life **requires 8.15 F1 + far lights (step 21)**. | L | 15, 18, CR2 |
| 20 | Boats, fast travel; flight at 60 m/s (unchanged; flight waits for engine 16 + 8.15 L1). | L | engine 16 |

### World stages (P23)
| # | Step | Size | Needs |
| --- | --- | --- | --- |
| STG1 | **Stage flip = 8.15 S1:** the generator takes the stage; touched chunks pinned by a one-time scan; per-stage ore/biome tables. Agent's pick: built and tested with a dev trigger (`-stage N` / a Creative key) BEFORE bosses exist, so BS1 only calls it. **Requires 8.15 C1, E2.** | M | 3, 8.15 C1 + E2 |
| STG2 | **Stage content:** stage 2 ore set + one biome spread + creature table per stage (CR1's table gets a stage column). | M | STG1, CR1 |

### Removed / replaced
- **Old 17 (owner question batch: creatures, combat, bosses, stages)** -- DONE by the 2026-10-15 round, except spells
  (CB3's question round).
- **Old 9** -- split into 9a + 9b (P22).

## 4. Run order (gameplay only; Q4 decides how it interleaves with the engine block)

1. BLD1 -> BLD2 -> 4 (67) -> 9a (72 rewritten) -> 9b -> RX1 -> 5 (68) -> 6 (69) -> 7 (70) -> 8 (71)
2. BLD4 -> BLD5 -> BLD3 -> BLD6 -> 10 -> 11 -> 13 -> RX2 -> CB1 -> CB2
3. 14 [measure] -> 15 -> RX4; 18 [measure] -> CR1 -> CR2
4. After the engine owes them: RX3 (8.15 N1) -> RX5 -> RX6 -> RX7; STG1 (8.15 C1, E2) -> BS1 -> STG2 -> BS2; V1; 12; 16
5. 19 towns, 20 travel; CB3 after the spell round.

## 5. Parked task files `tools/overnight/later/67-72` (not edited)

| file | status | why |
| --- | --- | --- |
| 67-gameplay-step-4 | **still valid** | spawn at the best easy area; nothing in the round touched it |
| 68-gameplay-step-5 | **still valid** | climbing; movement untouched by the round |
| 69-gameplay-step-6 | **still valid** | gliding |
| 70-gameplay-step-7 | **still valid** | grappling hook |
| 71-gameplay-step-8 | **still valid** | wall-run (Requires 68) |
| 72-gameplay-step-9 | **needs rewrite** | "step 9" is now 9a (health/damage/respawn) + 9b (death mode per world, owner P22); its text would build "keep inventory on death" |

## 6. Risks

- **Powders and snow:** making snow fall could wake whole snowfields (fluid tile pool, KNOWN_ISSUES "POOL FULL"); RX2
  reports candidate counts and keeps snow static if they explode.
- **Material ids:** glass, mud variants, ash, gunpowder, gas, per-stage ores all need palette ids in 128-223.
  Count before RX1; if short, STOP_FOR_OWNER (guardrail 3).
- **Stage flip correctness:** 8.15 §14's landmine (compare an evicted chunk against ITS generation stage) belongs to
  STG1's tests.
- **Strokes and big edits:** a long plane or sculpt stroke writes many bricks in one frame; BLD steps cap cells per
  frame like `MineSphereBudgeted` and finish over frames.
- **Survival vs dev keys** (P11) and **IMGUI placeholders** -- unchanged.
