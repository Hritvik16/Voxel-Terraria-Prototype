# Gameplay design round (started 2026-10-15)

Owner answers from the supervisor chat, recorded as given. These are DECIDED unless marked otherwise. They feed the
simulation-reach decision (task 87 on hold), step 20/21 lighting, and the gameplay phase plan. The game-design brief
(`GAME_DESIGN_BRIEF_SNAPSHOT.md` / the claude.ai brief) stays the authority for terrain; this file adds to it.

## Why this round
Task 87 (smaller 0.1 m window) would have shrunk gameplay reach 128 m -> 82 m; the owner stopped it ("The player needs to
be able to affect terrain outside of range. NPCs would need to interact with terrain out of range. Fluid simulation would
look weird if it is only contained within that 0.1 area.") and said: "I am not sure how any gameplay aspect will work
after we're done with all the steps in this queue." So: gameplay first, then the engine's reach design.

## Batch 1 -- the shape of the game
| Topic | Answer |
| --- | --- |
| Session shape | **Persistent world (Terraria):** one world you keep; explore, dig, build, beat bosses to open the next stage. |
| Noita parts | **Material reactions** (water + lava -> stone, fire spreads, acid eats rock, oil burns, steam), **spell crafting**, **powders everywhere** (sand/snow/gravel/ash fall and pile), and **destroy everything** -- explosions blast terrain -- but **NO structural integrity** ("I feel that will be too much"). |
| Multiplayer | **Single player first, co-op later:** keep co-op possible (deterministic sim, host-authoritative), do not build it yet. |
| Creatures at once (busiest) | **Minecraft-like, ~50-70** around the player. |

## Lighting (same day; full text in `ENGINE_PHASE_PLAN.md`, "Owner answers, 2026-10-15")
Unlimited lights ("light up the island with a million lights"), visual-first but readable later, thin walls block,
instant near / crawl far, crisp near shadows + soft grid.

## Batch 2 -- the world beyond the player
| Topic | Answer |
| --- | --- |
| Leaving an area | **Freeze, catch up on return** is the DEFAULT (quick effects resolve when you come back: fluid settles, crops grow by elapsed time). |
| Far fire (forest fire, you fly 1 km away) | **Keeps burning, visible:** glow and smoke on the horizon, spreading; the burned area is real when you return. |
| Player builds and craters far away (brief Q12) | **Yes, always:** a megabase or a crater dug block by block is visible from across the island. (Today only far edit OPS reach the shells and far band; block-by-block D.1 edits do not.) |
| Biggest explosion | **Nuke, 50 m+** (Noita late game: delete a mountainside). |

## Batch 3 -- exceptions, creatures, combat, bosses
| Topic | Answer |
| --- | --- |
| Far-away things that keep running coarsely (exceptions to "freeze") | **All four: fire; lava and floods; powders / collapses; town life** (day/night routines, NPCs move, lights at night). |
| Creatures when you leave | **Wild creatures despawn and respawn by biome near you; town NPCs and named creatures persist** (frozen, or on routines per "town life"). |
| Combat | **Spells + gear**, and "**anime-esque fun combat that can be fast paced or slow depending on the player**". |
| Bosses | **Both:** some live at landmarks (signature features), some summoned or event-triggered. |

## Batch 4 -- stages, spells, building, death
| Topic | Answer |
| --- | --- |
| World stages | **Few big jumps (Terraria Hardmode style):** 3-4 clear stages; one boss flips the whole world (new ores, creatures everywhere, some biomes change). Needs a world-wide change pass over the saved world. |
| Spell crafting | **OPEN (owner: "leave this undecided for now").** Leaning Noita (wand slots + modifiers) but "that's too complicated". Revisit when combat is designed. |
| Building tools | **Click-and-drag strokes** (owner: "a simple click and drag ... given initial shape. Simplest can just be a line of voxels. That should allow for any type of build"). Pick a brush (today's sized cube), press, drag: it places along the stroke. Details in batch 5. |
| Death | **Chosen per world** at world creation: soft / classic / hardcore (one life). |

## Batch 5 -- building strokes, reactions
| Topic | Answer |
| --- | --- |
| Building tools (reference game) | **Like "Lay of the Land"** (Steam 2776090; same small-blocky-voxel look). Owner: one click-and-drag stroke "naturally goes from a straight line of voxels to a plane of voxels, allowing for easy wall and roof building". Its store page also lists: a cylinder tool (round structures), a cone tool (sloped roofs), paths drawn directly onto the ground, terrain sculpting (raise/lower ground), and custom prefabs made from your own builds. |
| Reuse your builds | **Yes:** the player can "select" any build they made and click-and-drag with it the same way (a selection becomes a brush / custom prefab). |
| Extra reactions (all four picked) | **Heat** (freeze/melt: ice, snow melts near fire/lava, sand + heat -> glass); **gas & explosives** (cave gas pockets, gunpowder, steam pressure bursts); **electricity** (conducts through water and metal; wet targets take more); **life** (water + dirt -> mud, plants grow on wet soil, moss/fungus spreads, ash regrows grass). Plus batch 1's: water + lava -> stone + steam, fire spreads, acid eats rock, oil burns. |

Sources for the reference game: [Steam store page](https://store.steampowered.com/app/2776090/) (opened 2026-10-15; it does not describe the line-to-plane drag itself -- that part is the owner's description).

## Batch 6 -- simulation reach (Amendment 8.15 draft questions, same day)
All of the draft's recommended answers (Q1-Q10 (a)); full text in `tools/overnight/done/90a-amendment-8-15-simulation-reach.md`.
| Topic | Answer |
| --- | --- |
| Live reach | **128 m on every machine**, plus look-ahead loading at speed and **visual-only far creatures** (herds, birds past 128 m) so long views are not empty. |
| Stage flip in explored areas | **New ores / biome spread only where unseen** (never-touched rock), by a one-time world pass; builds and digs never overwritten; creatures change everywhere at once. |
| New reactions far away | **Freeze**; growth, melt and freeze computed from elapsed time on return; gas explosions and electricity only near you. |
