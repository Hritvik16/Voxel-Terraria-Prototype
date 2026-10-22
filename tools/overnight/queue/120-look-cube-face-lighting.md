# 120-look-cube-face-lighting

Model: opus

Approval: owner, 2026-10-22 (chat, answers to task 114 Q1/Q3/Q4/Q6). LOOK task, not a performance task: no timing run (rule 10). Read `VISUAL_REVIEW_114.md` first (the defect rows named below, causes with file:line). Ship the change ON by default with a flag that restores the old look. Before/after stills with `run-look-review.sh` (114), well lit (midday, sun behind the camera; plus golden hour where named), FSR 3 at play rows, same poses as 114; captions say what to look at. HANDOFF with `## For the doc` (2-4 before/after images). TASKS.md paragraph. Fast suite once at the end.

## Change (114 defects #1 + #2 + #9, and the owner's "far terrain looks like low-poly land instead of 0.1 m cubes")
Owner goal: near and far terrain read as the SAME material made of small cubes. Individual 0.1 m cubes are sub-pixel past ~50 m
(at 540 rows), so keep their CHARACTER, not the cubes:
1. Warm ground-bounce on side faces (owner Q4 (a)): albedo x sun colour x ~0.25 x (1 - n.y) (`Raymarch.compute:4466-4472`), so sides
   stop going olive under the blue sky term.
2. Replace the distance switch from cube-face to slope lighting (`FarShadeBlend`, :3120-3124, driven by live dispatch rows) with
   FILTERED cube lighting: far ground is lit as the average of what its cube faces would show (top-face and side-face lighting
   weighted by how much of each a staircase at that slope exposes to the view), so there is no band and no smooth low-poly look,
   and nothing moves with FSR 3's dynamic rows.
3. Per-cell colour variation at the size each cell is DRAWN (#9: hash `voxel >> shellAtHit` in `LookAlbedo`, :974-979) so far land keeps
   fine texture instead of flat paint.
If one of these 3 hurts the look on its own, ship the others and say so. Stills at ~20 / 100 / 500 / 2000 m, noon AND golden hour
(`-tod 17`), plus 114's `crops/G_low_vs_high_far.png` pose. Note any ms change you happen to see but do not measure.
