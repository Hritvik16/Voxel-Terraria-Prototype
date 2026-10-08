# Roadmap and engine backlog (owner, 2026-10-08)

**This file overrides every older FUTURE plan** in `DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md`,
`AMENDMENT_8_11_RENDER_RANGE.md`, `PLAN_COARSE_FIRST_GEN.md`, `KNOWN_ISSUES.md`, SUPERVISOR.md and
the claude.ai plan docs where they disagree about ORDER or SCOPE. It does not change any adopted
spec section or frozen API; items below that would are flagged.

Owner, 2026-10-08: "I don't want to think about the player or actually populating the world until
all the features above are done and performant." Nothing in the backlog below is queued yet; each
item becomes a plan the owner approves first.

## Order
1. **Now:** queue 29 (B3) -> 30 (column-pass sharing) -> 32 (UploadDirty fix) -> 32b (launcher) ->
   B4, B5 (review pack), B6, B8, B10 (review pack). B7 (towns) and B9 are not queued.
2. v4 becomes the default and `surface-rebuild` merges to `main` (owner approves).
3. **Engine capability + performance phase** (backlog below). Starts with ONE plan doc for the
   owner: targets, order, memory budget. Replaces the single "milestone perf check".
4. Only then: player (movement-abilities question batch, top speed) and placement (ores, objects,
   towns, start position).

Superseded: "coarse-first after the movement questions" and "0.4 m tier evidence + palette bricks
after placement" -- both move INTO step 3. "Performance only at a milestone" now means step 3.

## Backlog for step 3 (not started)
| # | Area | Today (2026-10-08) | Wanted (owner) |
| --- | --- | --- | --- |
| E1 | Resolution | Interactive preset 480x270 + FSR 1.0 (spatial); rigs 960x540; walk ~12 ms / ~32 ms | 1080p output from 540-720p internal with a temporal upscaler (URP STP or MetalFX), 60 fps on M1 Air 8 GB. Native 1080p ruled out (owner). OPEN: 540 vs 720 vs dynamic. |
| E2 | LOD shells | 0.1/0.2/0.4 m tiers at 64/128/290 m (`LODConfig`, 540-row design), but voxels end at the residency edge (~128 m on axes, ~181 m diagonals; 89.6 m vertical band); 2.5D far band (0.8-12.8 m columns) beyond | Never able to see a voxel coarser than the screen resolves: nested cube shells (clipmap), each starting where its voxel is <= 1 px, in ALL directions incl. vertical, out to the horizon. Needs coarse shells generated directly at coarse resolution (coarse-first + mid band, Amendment 8.11 option B). Memory costed first (0.4 m tier evidence, palette bricks 8.13). Changes `LODConfig` bounds = an 8.9 amendment. |
| E3 | Streaming speed | Generation 2-29 chunks/s vs 2,285/s worst case; zero prefetch margin even walking | Generation keeps up at the top speed, with a margin. Task 30 is the first step. |
| E4 | Frame time | ~32 ms at 960x540 | Inside the E1 budget. |
| E5 | Far edits | An edit (bomb) outside loaded chunks is refused | Store it as an operation in the save, apply on generation, update the far band at once. |
| E6 | Fluid tick | ~6 frames per tick (GPU->CPU round trip), so fluid speed depends on frame rate | Fixed simulation rate independent of frame rate (co-op determinism too). |
| E7 | Fluid range | Simulated within 128 m; moving fluid beyond visibly freezes; "simulate everywhere" never tested | Active fluid keeps simulating wherever it is (lower rate far away); render-only flow for far rivers. Constraint: CPU is the sole terrain writer, so far fluid needs its chunk resident. |
| E8 | Fluid levelling | Whole-cell CA; a pour can leave a mound; cells sleep after 8 still ticks | Water always settles flat however much is poured (levelling rule; no sleep while lower ground is near). |
| E9 | Lighting | 32 active point lights (furthest dropped), torch reach a few metres, no lights in the far band, no bounce light, torch leaks through walls (Phase 7 flag) | At least Terraria-in-3D: effectively unlimited lights (light propagation grid), coloured light, lights visible across the island at night, dark caves you must light, some form of GI. Phase 7 flags (shadow rung, torch-through-wall, lava light) are folded in here. |
| E10 | Memory | Over the 3 GB cap before the player builds anything | Every item above fits the 3 GB cap. |

## Notes
- **Coarse shells without a CPU copy (E2) and far edits (E5) fit together:** the truth stays on the
  CPU as seed + an edit OPERATION log ("sphere r at x,y,z"); coarse shells and the far band are
  derived from it, like the far band is today. A full 0.1 m CPU copy is needed only where things
  are SIMULATED (player, fluid, debris, drops), not wherever an edit lands. A far crater's debris
  or released fluid simulates when that area loads (or E7's far simulation loads it).
  FLAG: an op log beside the frozen D.1 delta format is a redesign -- it needs an amendment and
  owner approval before any code.
