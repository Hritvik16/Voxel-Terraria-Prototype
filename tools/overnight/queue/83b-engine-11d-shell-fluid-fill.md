# 83b-engine-11d-shell-fluid-fill

Approval: task 83 stopped with Q46 ("fill ENCLOSED fluids too?"). Supervisor, 2026-10-12 18:50: recorded in the Queue
decisions doc as "going ahead with (a) unless you object" (reversible: one switch, invisible by test). Branch `engine-phase`.
Requires: nothing (83 is committed: f858dd5, c003f5a).

## Context (83's HANDOFF, done/20261012_184256_83-...HANDOFF.md)
83 fills enclosed AIR in shells >= 3 but never fluids. At seed 42's spawn s3-s6 still need 1.04-1.66x their brick caps.
With the already built switch `ShellFillFluids` on (enclosed fluids only), 83 measured s3-s7 = 91.6k / 72.2k / 62.3k /
27.5k / 4.2k bricks vs caps 102.4k / 102.4k / 102.4k / 71.7k / 17.3k: all fit.

## Change
1. `ShellFillFluids` ON by default (`TerrainGenChunkSource.ShellFill.cs`). It fills only fluid no ray from outside can
   reach (the sealed underworld lava sea, sealed pockets). Sea, lakes, rivers, any fluid open to the sky or a cave mouth
   stay exactly as generated. Fluid seen through a see-through liquid surface (task 75) counts as reachable: keep it.
2. Amendment 8.14 row 13b: add "enclosed fluids are filled too (Q46 (a), 2026-10-12)". CLAUDE.md task-83 paragraph: one clause.
3. `ShellFillTests`: the outside-ray test (top + 4 sides) runs with fluids filled: 0 differ outside the opened mouths.
   Add a case with a sea/lake chunk: no surface fluid voxel changes.

## Acceptance
- Dense bricks needed at spawn per shell, 83 -> 83b (83's need83 method). Target: s3-s7 fit their caps.
- Refusals per shell over the 16 poses (80/83's `[shells]` lines), 83 -> 83b. ONE player launch, `-shells 1 -shellwait 360`
  as in 83. Shells 1-2 starving behind s3-5 (83's finding) is KNOWN and is the owner's Q47: report it, do not fix it.
- The pose sheet (`tools/shells80-sheet.py`) next to 83's `Screenshots/83/sheet_shells_1.jpg`: look at it, say what changed.
- Whole suite once at the end (`SUITE=all`, generator path). `run-phase7-rig.sh` unchanged (shells off in rigs).
- No frame-time measurement. HANDOFF `## For the doc`: bricks per shell, refusals, peak process MB, the sheet.
