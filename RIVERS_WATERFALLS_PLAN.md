# Rivers and waterfalls: why they look like slabs, and a plan (task 73, 2026-10-11)

**Status (task 76, 2026-10-11): R1, R2, R5 and R6 are BUILT** (owner Q1 (a), Q2 (a), Q4 (a)): rivers are plain
water held by one-voxel lips (option A1's shape, the lip a material R3 can turn into held water); R3 and R4 remain.
See `tools/overnight/done/76-*.HANDOFF.md` and `Screenshots/76/`.

Owner, 2026-10-11: "I just want all the fluids to be present in the initial world. Surface water, rivers and
waterfalls. Right now the rivers ... look like slabs." Status: investigation + plan. **Nothing here is built
except a throwaway shading prototype behind `-riverlook` (OFF by default).** Questions for you are at the end.

## Recommendation (first)

**Make the generated river a smooth sloped sheet of "held water" (Option A2 below).** The generator writes the
river surface per voxel column from a smooth downhill profile, so the surface steps by one voxel (10 cm) every
few metres instead of 1-10 m every 8 m. The river is made of a new material, *held water*: the renderer draws
it as flowing water and hides the 10 cm risers. The fluid sim treats it as solid until a player touches it.
Then the touched cells become real water and the river works as an endless source. Waterfalls are a curtain of
held water down the cliff. Rivers cost 0 pool tiles until touched and 0 MB (one material id, plus 1 MB if we
use a flow-direction map). This is the brief's proposal with one change: "at rest" comes from the material, not
from the shape. A binary voxel fluid cannot hold a sloped surface still.

| Step | What | Size | Slots into `ENGINE_PHASE_PLAN.md` |
|---|---|---|---|
| R1 | **Generator: smooth river profile.** Per voxel column, from the mouth (sea or lake level) up the drainage tree. The level never rises downstream. Banks are raised where the ground is below the level (as `LakeRim` does for lakes). The river ends AT the lake or sea level (BUGS #5 and #7). Plain water for now. | M | New, before 17. A terrain change (`SUITE=all`). Fixes most of the look on its own. |
| R2 | **Renderer: river look.** Hide the 1-voxel risers between water cells (drawn as surface). Animate the surface along the flow (a 1 MB flow-direction map from Hydrology's parent chain, or Q4 (b)). Foam where water drops. | S | With R1. |
| R3 | **Held water + the source rule.** A material that the CA skips. A touched held cell becomes water. A held cell next to air feeds water into it, within the fluid budget. The motor and the underwater view treat it as water. | M-L | After 17 (fixed-rate tick). Replaces the BUGS #5 part of 18. Needs its own measurement. |
| R4 | **Waterfalls.** Where the R1 profile drops more than ~2 m in one cell: a pool held by a lip at the top, a held curtain 1-2 voxels thick down the face, a plunge pool, then foam and mist. | M | After R3. |
| R5 | **Far rivers.** The far band already samples R1's levels (`FarField.cs:613-615`), so it gets them for free. Add flow animation on far water texels. This is 19b. | S | = 19b. |
| R6 | **At-rest test for every generated fluid.** Sea, lakes, underground pockets and lava: no plain fluid voxel has air below it, or beside it at its own level. Pool = 0 tiles on arrival. This is BUGS #7's acceptance. | S | With R1. |

## The problem, in pictures (map v4, seed 42, noon, clock paused; `run-play.sh EXTRA="-riverdemo"`)

![today near](Screenshots/73/today_near_sheet.jpg)
*Near views, about 13 m away. **Top left, A (river on a slope):** two flat terraces with a 4-7 m dark water wall
between them, the full width of the river; the next box stands behind. **Top right, B (river into a lake):**
the lake is flat and fine. Look at the thin dark line across it (the river's last square, 0.4 m above the lake)
and the 3 m water box hanging over the inlet at the top right. **Bottom left, C (river over a 10.5 m drop):** a
water tower with square corners standing on the slope. Nothing falls. **Bottom right, D (river mouth):**
terraces with straight 0.1-1.3 m steps across the whole channel, on the 8 m grid (BUGS #7's slab).*

![today mid](Screenshots/73/today_mid_sheet.jpg)
*The same four sites from about 100 m. **Top left and bottom left:** a staircase of floating blue boxes up the
valley, each one map cell (8 m) long. **Top right:** the lake is fine; the river's boxes come down to it from
the top right. **Bottom right:** the mouth's terraces as dark lines across the channel.*

![far](Screenshots/73/a_A_slope_reach_far2km_pair.jpg)
*From 2 km (left half; the right half is the prototype and looks the same). On the flats, rivers read as clean
blue ribbons. **On the mountain (centre and right),** they are dotted blue dashes going up the gullies: the
same boxes, drawn by the far band and the coarse tiers.*

Wall census of the loaded world, in a 24 x 24 m box at each site (`Logs/overnight/73-demo-player.log`). A flat
lake has almost no water side faces:

| Site | water side faces facing air, per top face | tallest exposed water wall |
|---|---|---|
| A slope reach (map drop 3.8 m per cell) | 0.79 | 7.6 m |
| B river into lake (river 0.4 m above the lake) | 0.16 | 3.2 m |
| C 10.5 m drop | 1.23 | 10.5 m |
| D river mouth (river 1.1 m above the sea) | 0.10 | 1.3 m |

## Why it looks like this (from the code)

1. **One water level per 8 m map cell.** `Hydrology.cs:134` sets each river cell to its ground + 0.8 m
   + 0.2 m x log2(flow). On a slope, neighbouring cells differ by slope x 8 m: 3.8 m at A, 10.5 m at C.
2. **The column takes the HIGHEST of the 4 cells around it.** `ShapeField.SampleWater`, `ShapeField.cs:272-286`.
   So every river cell becomes a flat square whose level reaches half a cell over its lower neighbour. That is
   why the edges are straight and sit on the grid.
3. **Water fills every column whose ground is below that level, with nothing holding it.**
   `DetailField.cs:273, 371`. There is no bank or lip, so a river square stands on lower ground as a box with
   vertical sides. Lakes got rims and shelves in tasks 37e/42 (`ShapeField.LakeRim`). Rivers never did, which
   is BUGS #5 (B above) and #7 (D above).
4. **Water is drawn like stone.** It is an opaque voxel material (`Raymarch.compute:942`), with the same face
   shading (`:3500`), shadows and AO as rock. So a 10 cm step reads as a cube edge and a 7 m step as a dark blue
   wall. There is no transparency, reflection or motion.
5. **The far band copies it.** `FarField.cs:613-615`: a texel column (0.8-12.8 m) topped at `c.water`. These are
   the same terraces, coarser.
6. **There are no waterfalls.** `Features.cs:10` lists Waterfall as a later task. Site C is just the terrace
   rule on a steep slope.
7. **Why "simulate the river" can't be the answer.** The fluid is binary voxels: a cell is full or empty, and
   there is no fill level (`FluidCA.compute:74-82`). Binary water rests only when it is flat. A sloped river is
   a staircase of 10 cm steps, and every step spills. So "at rest" for a river must come from somewhere else:
   lips under every step (A1) or a material that doesn't flow (A2).

## Prototype: what shading alone can do (`-riverlook 1`, OFF by default)

Shader-only, Look 1 path (`Raymarch.compute:1206`, `RiverLookAlbedo`). Water side faces become white-water
streaks. A 1-voxel riser over water is drawn as surface. A top face next to a drop gets a foam lip.

![proto A](Screenshots/73/a_A_slope_reach_near_pair.jpg)
*Site A, near. **Left:** today, a dark 7 m water wall. **Right:** the prototype. The wall turns grey-blue with
streaks and reads like a stone or ice wall, still a box. The foam line along its top edge helps a little.*

![proto D](Screenshots/73/d_D_river_mouth_sea_near_pair.jpg)
*Site D, near. **Right half:** the small steps read a bit like weirs, but the foam outlines the 8 m squares and
makes the grid MORE visible.*

**Verdict: shading can't fix it; the shape has to change first (R1).** The riser-hiding rule had almost nothing
to act on, because today's steps are 1-10 m tall, not 10 cm. It is meant for R1's output. The prototype stays
behind its flag so it can be re-shot after R1. `-riverdemo` finds the same four sites again.

## Options

| Option | What it looks like | Memory | GPU cost | Player digs a bank / drops a block in | Invariants touched |
|---|---|---|---|---|---|
| **A2 (rec.) Held water, smooth profile** | A smooth sloped sheet (10 cm risers hidden), animated along the flow; curtains on cliffs | 0 MB (one material id) + 1 MB flow map (optional) | Low: a few neighbour reads + ALU per water pixel | Touched cells become water; the river is an endless source, so the hole floods and the river stays | New material id (not a layout change); a new CA rule (engine change, measured on its own); CPU stays sole writer (conversion goes through the op list -> `ChunkStore.SetVoxel`); D.1 and §4.4 unchanged; pool 0 until touched |
| A1 Brief as written: plain water at rest in step-pools | Flat pools, each held by a 1-voxel stone lip at its lower end. Lips show as lines across the river every 2-3 m unless drawn as water | 0 MB | Low | The pool drains into the hole until the hole fills. Small pools visibly drop, unless the source rule (R3) is added anyway | Generator only; no CA change; at rest is testable like lakes |
| B Render-only heightfield sheet over a dry bed | The best surface: true slopes, smooth falls | 2-4 MB (surface per cell + a near raster) | Medium: a second heightfield intersection on primary and shadow rays | Nothing happens until a "materialize" step writes thousands of voxels and tiles at once. Swim, collision and the underwater view all need the sheet too | A second water representation outside `ChunkStore`, which breaks the spirit of "the GPU mirror is written from the CPU state" |
| C Simulate a short window, dormant beyond | Real moving water near you, with a seam at the window edge | A 16 x 64 m reach, 1 m deep, is ~1 M cells: over the 750 k-cell / 480-tile budget | High | Real | Breaks the fluid budget; needs sources and sinks at the edge (17/19) |

The held-material idea is Minecraft's source blocks, plus Noita's "static" liquid materials. As I recall,
Noita's hand-made scenes use liquids that stay put until something disturbs them; I have not checked this.
Waterfalls: a held curtain (recommended; interactive, voxel-consistent, 0 MB) vs a render-only sheet (prettier
edges, but nothing happens when you touch it). Spray: a foam albedo plus a mist tint at the plunge pool first;
particles later.

**Bug #7 / "pool 0 on arrival":**
- The sea is at rest by construction (`DetailField.cs:272`).
- Lakes are at rest since 37e/42 (tested).
- Rivers and falls: held, so they never enter the pool (A2), or at rest (A1).
- Underground pockets (water, oil, acid, sludge, lava) are ellipsoids filled to a flat level (`DetailField.cs:512`).
  They should be at rest unless a tunnel cuts them below their level. **Not checked.**
- The volcano's lava lake has never been checked (BUGS #5).

R6's test covers all of these. Order: R1 + R2 + R6 can go now (terrain + shader, no fluid-engine change). R3 and
R4 need step 17's fixed-rate tick. R3 makes 18's "check BUGS #5" moot for rivers. R5 is 19b.

## Questions for the owner (recommendation first)

1. **What a river is made of:** (a) held water, smooth profile, endless source when touched (A2);
   (b) plain water in step-pools held by hidden lips (A1); (c) render-only sheet (B); (d) simulate a window (C).
2. **When:** (a) R1+R2+R6 now, as the next terrain task (fixes the look before step 17); R3/R4 after 17;
   (b) all of it after step 18; (c) after far fluid (19).
3. **Where waterfalls come from:** (a) wherever the R1 profile drops more than ~2 m in one map cell, plus the
   Waterfall signature feature later; (b) only the signature feature, with R1 smoothing every other drop into rapids.
4. **Flow direction for the animation:** (a) a 1 MB flow-direction map built from Hydrology; (b) 8 held-water
   material ids, one per direction (no texture, 8 palette slots).
5. **Lava rivers (volcano flows):** (a) same held mechanism later; (b) water only.
