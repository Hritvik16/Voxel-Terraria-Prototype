# 174-fsr3-reactive-mask

Model: opus

Approval: owner, 2026-11-05, chat Q4 (c): "Reactive mask task". A VISUALS task (look stage, ROADMAP.md "CURRENT ORDER").
Read `tools/overnight/blocked/171-fsr3-shadingchange-default.HANDOFF.md` and `tools/overnight/blocked/166-look-fsr3-softening.HANDOFF.md`
(166 found the reactive and transparency/composition masks missing; 171 found sc10 ghosts behind a moving torch: error 17.2 -> 47.2 at 432).

## Goal
Keep 166's crisper still terrain (`-fsr3sc 10`) WITHOUT 171's ghost: give FSR 3 a reactive mask, so that only pixels whose light
really changes skip the history.

## Do
1. Build the reactive mask in the raymarch output, behind `-fsr3reactive 1` (OFF by default until step 3 passes). It covers:
   emissive surfaces (torch, lava, glowing materials); fluid surfaces; pixels whose voxel changed this frame (edits); and pixels
   whose dynamic-light term (point-light torches and, when it is on, the torch flood) changed against the previous frame.
   For the last one, our motion vectors are camera-only (171 "Why it ghosts"). Pick a sound, general way (for example, last frame's
   dynamic light reprojected by the camera motion, compared per pixel), not a tuned threshold per scene. Write down which way
   and why. The mask must be 0 wherever nothing changes, so still terrain stays as crisp as sc10.
2. Re-run 171's QA (`-lookreview -fsrlive 1`, `tools/fsr171.py`) with new arms: today / sc10 / sc10 + mask, at 432 and 540.
   ADD a flowing-lava scene (the in-play moving emissive; 171 did not test it) and run it with the torch flood both ON and OFF.
   Also run the still sun pose, to prove the mask leaves still terrain as crisp as sc10.
3. Clean result (sc10 + mask is no worse than today on every moving scene, and as crisp as sc10 on still terrain): make
   `-fsr3sc 10 -fsr3reactive 1` the play default (`-fsr3sc 0` = old). Rigs and capture are unchanged. Run the fast suite once.
   Otherwise do NOT flip anything, and end STOP_FOR_OWNER with captioned sheets, one picture per problem.
4. Rough cost (look-stage rule): note the mask's extra GPU work from runs you already do. No timing arms.

## For the doc
Full-size viewer (as 162's) of the moving-torch, lava and still-sun sheets: today | sc10 | sc10 + mask, each with a caption saying what to look at.
