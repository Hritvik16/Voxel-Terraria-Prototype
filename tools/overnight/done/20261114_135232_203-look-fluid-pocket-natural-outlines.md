# 203-look-fluid-pocket-natural-outlines

Model: opus

Requires: 201-defaults-fluidpockets-on

Approval: OWNER, 2026-11-13 (doc Q-AA "these need to be more like ponds"; chat W1 (a)): IRREGULAR, natural outlines
instead of ellipses -- "for ALL the fluid pockets" (water, lava, acid, oil, quicksand, sludge, honey; surface kind).
(W1 (e) streams feeding them is task 204, a plan; not here.)

## Do
1. Today (`Assets/TerrainGen/FluidPockets.cs`, tasks 183 / 196): a surface pocket is an ELLIPSE (rx, rz) dug to level L,
   with 196's flush rim + 30-deg bank. Make the outline natural by an EXACT, DATA-DERIVED rule (owner 2026-10-24: no
   terrain-specific tuning; prove on an adversarial case), e.g. the pond is the set of columns of the site's basin that lie
   under its fill level -- the outline follows the ground's own contour -- or another rule you can justify. Same rule for every
   fluid and biome. Keep: at rest by construction, 196's banks (no wall, no step), every placement rule, the plane-cell
   containment (a column finds its site by `>> CELL_SHIFT`; the outline must stay inside what the planner checked).
2. `-pocketoutline 0` (or similar) rebuilds today's ellipse byte for byte for before/after pictures; `-fluidpockets 0`
   unchanged byte for byte. Tests like 196's (`FluidPockets` tests: contained, at rest, no wall, adversarial site).
- Generator change -> `SUITE=all` once at the end. TASKS.md paragraph. No timing arms (rule 10).

## Pictures (one launch, midday, sun behind camera, native 1080)
196's pocket poses (water, lava, sludge / honey if near) before (ellipse) / after, plus one from above per fluid. Captions say
what changed. `viewer.html` full size.

## End
Done with HANDOFF + pictures.
