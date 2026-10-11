# 73-rivers-waterfalls-plan

Approval: owner, 2026-10-11 ~08:40 in chat: "I just want all the fluids to be present in the initial world. Surface
water, rivers and waterfalls. Right now the rivers and waterfalls look bad. Rivers do not look right. They look like
slabs, which I get as an entire river cannot be simulated at once while allowing for everything else. We need a way
to figure this out." Queued by the supervisor 2026-10-11. Branch `engine-phase`. **Investigation + plan, then
STOP_FOR_OWNER.** Not a terrain-generator change; a throwaway Playground/shader prototype is allowed only behind a
flag that is OFF by default and must not change rigs.

## Background (read, do not re-derive)
- Design brief (`GAME_DESIGN_BRIEF_SNAPSHOT.md`, lines ~300-340): fluids are hazard, tool, resource and spectacle;
  "Rivers and waterfalls stay still until disturbed, and then the disturbed part behaves like real water fed by an
  endless source." Proposal there: undisturbed river = still water voxels whose SURFACE the renderer animates along
  the flow direction the generator already computes per map cell; undisturbed waterfall = a dormant water curtain
  down the cliff, animated, with spray where it lands. "Prototype a small river and waterfall in the Playground
  before deciding."
- `tools/overnight/BUGS.md` #5 (river water steps down into a lake behind grid-edged 2x2 squares, 0.2-1 m above the
  lake) and #7 (generated sea/lake water not at rest: stepped surfaces leave slabs with vertical sides ~1.5 m above
  the beach; owner: "World gen should always place all fluids in resting position ... 0 in the pool until
  disturbed").
- `ENGINE_PHASE_PLAN.md` steps 17 (fixed-rate fluid tick), 18 (levelling rule + BUGS #5), 19 (far fluid; 19b
  render-only flow for far rivers). Task 54: only awake fluid gets tiles; task 55: pool 480 tiles in Playground.

## Change (one task: find out, then propose)
1. **Show the problem.** In tg2 map v4 (`./run-play.sh` world), well-lit screenshots (midday, sun behind camera) of:
   a river reach on a slope, a river entering a lake, a waterfall (or wherever a river drops over a cliff), and a
   sea/lake shore with the bug #7 slab. Near (5-20 m) and mid (50-150 m) views; also how the far band draws a river.
2. **Explain why each looks like slabs**, from the code: how the generator writes river/waterfall water (per map
   cell? per 2x2 column? a flat level per cell so a sloped river becomes terraces with vertical side faces?), how
   the raymarcher shades a fluid surface and its side faces, what the far band does. Cite files/lines.
3. **Options for a river/waterfall that looks right while staying cheap** -- at least these, each with what it
   looks like, memory (MB), GPU cost class, what happens when the player digs a bank or drops a block in, and which
   invariants it touches (CPU sole terrain writer; D.1; §4.4; fluid pool 480 tiles):
   (a) the brief's proposal: still voxels + surface animated along the flow field, water placed AT REST per reach
       (terraces hidden by drawing the surface as a smooth sloped sheet / no side faces between same-river cells);
   (b) a render-only river surface (heightfield sheet over the voxel bed) that becomes real voxels only where
       disturbed;
   (c) simulate a short window of river around the player, dormant beyond;
   plus anything better you find (say where the idea comes from). Waterfalls: dormant curtain vs a sheet; spray.
   Also how all generated water (sea, lakes, rivers, waterfalls, lava) spawns at rest so the pool is 0 on arrival
   (bug #7), and how this orders against engine steps 17/18/19.
4. **Prototype, if it fits:** the cheapest visual check of your recommended option (e.g. a shader-only flow
   animation + no side faces between same-body water cells), behind an OFF-by-default flag, before/after
   screenshots at the same poses. Skip and say why if it does not fit one task.
5. Write `RIVERS_WATERFALLS_PLAN.md` (repo root, under ~150 lines): problem with captioned images, cause, options
   table, recommendation first with its steps sized S/M/L and where they slot into `ENGINE_PHASE_PLAN.md`, and the
   owner questions as specific multiple choice (recommendation first). Then STOP_FOR_OWNER with those questions at
   the top of HANDOFF.

## Acceptance
- Screenshots with one-line captions (what to look at) for every case in item 1 (+ prototype before/after if done).
- If any code changed: fast EditMode suite green and `run-phase7-rig.sh` PASS count unchanged with the flag OFF.
- No frame-time measurement. HANDOFF `## For the doc`: the cause in two lines, the options table, 2-3 images.
