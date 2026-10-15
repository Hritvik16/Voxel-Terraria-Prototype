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
