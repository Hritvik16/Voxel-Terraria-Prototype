# 127-look-better-comparison-stills

Model: opus

Approval: owner, 2026-10-23, in the Queue decisions doc: on Q75 (High vs Medium) "These arent good enough comparison pics. I cant get a
good enough idea and they both look flat"; on Q78 (biome border) "Can i get a birds eye view? This looks fine right now though".
Capture-only task (rule 10: no timing, no product change). NO default changes.

## Why the old stills looked flat
117's stills were noon with the sun behind the camera (the owner's earlier rule for well-lit comparisons): that gives almost no shading
relief. Keep them well lit, but light the SCENE from the side: sun elevation ~35-45 deg, ~60-90 deg to the camera's heading
(cross-light), so slopes and cliffs show shape. Fixed time of day for every pair; say the time and heading in each caption.

## Shots (one player launch, `-lookreview`-style, High 540 rows FSR 3 unless stated)
1. **Q75, High vs Medium.** 3-4 poses where the 3.3 km hand-off and the 1-3.5 km band show: a high lookout, a ridge, and a walk-height
   view of a long valley/coast. For each: full frame High | Medium side by side, large (not downscaled below ~1400 px wide per pair),
   plus 2x crops of the hand-off over land AND over sea. Also a bird's-eye-ish pose (camera ~800 m up, pitched down ~45 deg) centred on
   the 3.3 km ring so the whole seam is one shape. Mark where Medium looks worse AND where it looks the same.
2. **Q78, biome borders from above.** A bird's-eye pose (camera ~600-1500 m up, pitched -60 to -90 deg) over 2-3 biome borders (the
   `border_0..2` poses of task 122 give the places), BEFORE (`-biomeblend 0 -biometint 0`) | AFTER, same camera and time of day,
   large. Add the 122 map-render before/after (`Screenshots/122/biome_map_before_after.png`) only as a reference.
Look at every image before writing captions; if a pair still looks flat, change the sun/pitch and reshoot in the same launch plan.
HANDOFF + `## For the doc` (the 3-5 best pairs), TASKS.md paragraph. No suite needed if no code under Assets/CoreEngine/ changes
besides capture-only flags; run the fast suite once if any code changed.
