# 174b-torch-flood-falloff-tune

Model: opus

Approval: owner, 2026-11-05, chat: "Tune first, then on" (after "flat yellow camps" was explained). A LOOK task: capture only, no default change,
no frame-time arms. Read `tools/overnight/done/*172-look-torch-cinematic.md`, `tools/overnight/blocked/172-look-torch-cinematic.HANDOFF.md`
("Single biggest fix"), `Screenshots/172/torchcine172.txt`, TASKS.md tasks 158/164/165/172.

## Problem (172, owner-facing words)
With the flood ON (`-torchflood 1 -tfdir 2`), a group of torches lights the ground as one even yellow sheet out to ~12 m instead of a bright
spot per torch fading to dark; floors next to the posts clip to solid yellow; lit slopes shimmer ~2x today while walking (flicker vs native in
lit pixels: walk 4.1 -> 6.8, 40 m strafe 4.4 -> 11.0).

## Do
- Sweep the flood's intensity / falloff (`-tfi`, `-tfk`; today 2.0 / 0.10 vs the point lights' k 0.35), and if needed the curve shape, in ONE
  or two launches of 172's rig (`-lookreview -torchcine 1`), on the same 6 scenes, with today's FSR 3 (NOT sc10; 174 owns that). 4-6 settings
  chosen to span "bright core, dark rim" without black pockets between neighbouring torches. Keep 172's "today, flood OFF" arm as reference.
- Per setting measure: lit-pixel share, clipped (saturated) pixel share near posts, a radial falloff profile round one torch, and the
  walk/strafe flicker vs native as in 172.
- Judge honestly per setting; recommend ONE. No code default changes (the chosen values become defaults in task 175).

## End
STOP_FOR_OWNER with a full-size viewer (as 162's): per scene, today | 3-4 settings side by side, captions saying what to look at; question
"Which setting?" with the recommended one first and its numbers.
