# 76-rivers-r1-r2-r6

Approval: owner, 2026-10-11 in chat, answering task 73's plan (`RIVERS_WATERFALLS_PLAN.md`, commit 9f7029a):
Q1 (a) held water; **Q2 (a): R1 + R2 + R6 now, as the next terrain task**; Q3 (a) waterfalls at every drop > ~2 m per
map cell (that is R4, NOT this task); Q4 supervisor default (a) a 1 MB flow-direction map from Hydrology's parent
chain; Q5 "rivers of all fluids ideally" (design R1/R2 so a river's fluid is a parameter, not hard-coded water;
generate water rivers only for now). Queued by the supervisor 2026-10-11. Branch `engine-phase`. This IS an
approved terrain-generator change: `SUITE=all`.

## Change
Rows R1, R2 and R6 of `RIVERS_WATERFALLS_PLAN.md`, exactly as written there (read the plan's cause section first:
`Hydrology.cs:134` one level per 8 m cell, `ShapeField.SampleWater` max of 4 cells, `DetailField.cs:273,371` no
banks, opaque stone-like shading `Raymarch.compute:942`, far band `FarField.cs:613-615`).
- **R1 smooth river profile** per voxel column, from the mouth up the drainage tree; the level never rises
  downstream; banks raised where ground is below the level (as `LakeRim` does). Until R3 (held water) exists, the
  river must still be AT REST: say how (e.g. 1-voxel lips per step, plain water) and keep it so R3 can swap the
  material later. Steps where the profile drops > ~2 m per cell are left for R4 (mark them; do not build curtains).
- **R2 river look:** hide the 1-voxel risers between same-river water cells (draw as surface), animate the surface
  along the flow (the 1 MB flow-direction map), foam where water drops. Replaces 73's throwaway `_RiverLook`
  prototype (remove it or fold it in). Must stay compatible with task 75's see-through liquids (75 runs first).
- **R6 at-rest test** for every generated fluid: sea, lakes, rivers, underground pockets (`DetailField.cs:512`),
  volcano lava lake. No plain fluid voxel with air below it, or beside it at its own level; fluid pool = 0 tiles on
  arrival. This is BUGS #7's acceptance; also re-check BUGS #5 (river into a lake) and update BUGS.md rows.
- If R1+R2+R6 do not fit one task: do R1 + R6 (shape + test), commit, and say what of R2 remains.
- CPU sole terrain writer; no D.1 / §4.4 / frozen API change; integer bitwise coordinate math.

## Acceptance
- `SUITE=all` green, with the new R6 test(s); report Height/water FNV change for seed 42 v4 (expected: water changes).
- `-riverdemo` (task 73) before/after at the same 4 sites, near and mid, noon, sun behind camera: wall census numbers
  (73's: tallest wall 7.6 / 3.2 / 10.5 / 1.3 m, side faces per top face 0.79 / 0.16 / 1.23 / 0.10) before -> after.
- A far view (800 m) showing the far band follows the new profile.
- Contact sheets with one-line captions. No frame-time measurement.
- HANDOFF `## For the doc`: wall census before -> after, R6 result, 2-3 images.
