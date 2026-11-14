# 198-bug-white-dashes-steep-faces

Model: opus

Approval: SUPERVISOR, 2026-11-13 -- a render check already listed as owner-independent in `WORLD_GEN_LOOK_PLAN.md`
("Not world gen": "Faint white dashes on near-vertical faces (`pair_forest_breaks`). This needs a render check.").
Never-idle rule: owner-independent work behind 197. Bug investigation; no look or design change.

## The problem
Faint short white dashes / speckles show on near-vertical rock faces, e.g. `Screenshots/178c/fixed_forest_breaks_*.png`
(the `fixed_forest_breaks` pose; also task 173's `pair_forest_breaks`, `Screenshots/173/`). Unknown whether they are
in the VOXELS (snow/cover voxels the generator leaves on steep faces) or in the RENDERER (normals / sub-voxel hit
precision / shadow or AO sampling at grazing angles / LOD seam).

## Do
1. Find out which, from evidence: ONE capture launch (<= 5 poses, midday, sun behind camera, native 1080) at the
   `fixed_forest_breaks` pose + 1-2 other steep faces, close up. Use the existing debug views (material id / normal /
   tier) for the same poses, and a CPU-side check of the voxels at a few dash pixels (what material is there).
2. If it is the GENERATOR (real voxels): stop at a report -- world-gen look changes go to the owner. Write it into
   HANDOFF with pictures (no STOP_FOR_OWNER needed; it is a finding, not a blocker).
3. If it is the RENDERER: trace the exact cause (the actual step/mask/precision path -- not "floating point"),
   fix it exactly if the fix does not change anything else on screen (pixel-diff the same poses before/after; every
   differing pixel explained), with an EditMode test where the path is testable on the CPU side. A fix that would
   change frame cost or the look elsewhere: report the options in HANDOFF instead.
- Fast suite once at the end if code changed. TASKS.md paragraph. No timing arms (rule 10). Launch budget applies.

## Pictures
Full-size close-ups before/after (or the diagnosis views), `viewer.html`; captions say where the dashes are.

## End
Done with HANDOFF + pictures.
