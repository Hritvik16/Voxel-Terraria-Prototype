# 40-bug-ledge-view-crescent

Bug fix, named by task 34 (B5) and queued by the supervisor 2026-10-08 as Q29.5 "(a) unless you
object" (owner: recommended option is the default for reversible engineering choices). Runs after
the owner's bugs 38/39.

## Evidence
`Screenshots/B5/poses_v4_b5_sheet2.png`, row 2, second panel (`ledge_view`, noon, seed 42 map v4):
a SOLID BLACK CRESCENT over a tall rock wall near B5's canyon [3] (seed 42, around 5102,3116).
Task 34 found: the band view says VOXELS draw it, not the far band; the map has no fin there (0 cells
on seed 42 stand > 30 m above both neighbours). So it is a render/lighting artefact, not terrain.
Poses run: `FarBand/2026-10-08_224205_world`.

## The one change
1. Reproduce `ledge_view` (`MODE=poses ./run-tg2world.sh`, or the single pose). Localise with the
   existing views (band view, tier view, lighting view `Y`, shadow rung `J`, far band off `C`):
   does the black come from the primary hit (miss / wrong tier / clipmap), shading (normal, NaN,
   shadow), or the far band seam?
2. Fix the cause inside the render path (bug fix; no frozen API or layout change). If it is a
   design limit (e.g. tier hand-off at that distance), STOP_FOR_OWNER with pictures and options.
- No performance arms (PREAMBLE rule 10). Check KNOWN_ISSUES (phantom geometry, tier seams, NaN).

## Acceptance
- Before/after `ledge_view` captioned (the crescent gone), plus the full `MODE=poses` sheet read for
  regressions (holes, floating geometry, black areas).
- Fast EditMode suite 0 FAIL; `run-phase7-rig.sh` PASS if the lighting path changed.
