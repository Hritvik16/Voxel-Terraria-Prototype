STOP_FOR_OWNER -- task 142 (soft sky plan): six questions before step S1. Recommended answer first.
  Q1 Sealed cave at noon: (a) today's floor 0.02 = near-black, torch needed, same day or night | (b) 0.05 faint walls | (c) 0.12 dim grey.
  Q2 Noise: (a) faint shimmer under overhangs while moving + ~0.5 s settle on newly seen surfaces, FSR 3 smooths it | (b) none ever
     (adds S4 spatial filter, ~+0.5-1 ms est.).
  Q3 2 m hole into a 10 m room: (a) physical: soft bright patch under the hole, corners near-black | (b) stretched: whole room dim grey.
  Q4 Cost (+2 to +4 ms p50 at 540 rows, estimated from 138): (a) build behind a flag, measure in S2, try 1 ray per 2x2 if > +3 ms |
     (b) ship only if <= +1 ms.
  Q5 Beyond the 0.1 m window (shells, 59-116 m): (a) keep today (far faces fully sky-lit; hand-over pop shrinks to shaded->lit) |
     (b) also trace sky rays for shell hits (cost unknown; the shell march is the most expensive pass).
  Q6 Digging a skylight: (a) light below fades in over ~0.5 s, free | (b) instant (reset history in each edit's box, extra step).

# 142-plan-soft-sky-light: DONE (plan only). `SOFT_SKY_PLAN.md` written; recommendation A. No code, flag or look change.

## What changed (commit below)
- `SOFT_SKY_PLAN.md` (new): the problem with today's numbers, three candidates, recommendation, a five-step list, Q1-Q6.
- `TASKS.md`: a "Task 142" paragraph.

## The plan in short
- The cause (136/138/139): `Raymarch.compute:4896-4908` decides sky visibility with ONE vertical yes/no ray. `skyOpen` then
  scales both the ambient and the sun-shadow floor (`:5146`, `:3772`, `:5370`). Every fix so far still gave one answer per face.
  The fix is a FRACTION of the sky hemisphere.
- A (RECOMMENDED): one cosine-weighted sky ray per tier-0 hit pixel per frame, replacing the vertical probe (same ray count, same
  `TraceShadowRay`). A per-pixel running mean keyed by `ShadowOriginKey` (1/n, then 1/16). A sealed cave is EXACTLY 0 on every
  frame and open ground exactly 1, so neither has noise. Only partly open faces vary, about +-6% of ambient. History buffer ~8.3 MB
  at 960x540 (14.7 MB at 720). Cost +2-4 ms estimated from 138's tilted ray, not measured. No frozen API or Appendix A/B
  layout touched.
- Window edge (found while reading): a slanted ray that leaves the tier-0 box SIDEWAYS returns "open" (`:2006`). Caves near the box
  edge would leak light and pop on recentre. The fix is step S3: reuse task 133's shell continuation (`_NearShadowShells`, `:4796`).
- B (fixed directions): A's cost plus banding and stripes. C (0.8 m skylight flood): no noise and cheap to sample, but it leaks
  through 0.1 m player walls, needs a new per-chunk layout plus streaming work, and is not exact. Rejected for now.
- Steps: S1 CPU oracle + tests (adversarial cases built with edit ops) -> S2 GPU `-softsky 1`, OFF, with 139's dark-compare sheet
  + one cost-split -> S3 box-edge continuation -> S4 spatial filter only if Q2 (b) -> S5 make it the default (owner).

## Evidence
- Read: 138 HANDOFF (`tools/overnight/done/20261025_065325_138-look-tilted-sky-probe.HANDOFF.md`), 139 HANDOFF (in commit 765dc2d),
  `Screenshots/139/darkcompare139.txt`, Raymarch.compute (sky probe, skyOpen, shadow history, TraceShadowRay window exit),
  PHASE_7_COMPLETION A.5 (sky probe ~0.3 ms), TASKS 124 (halving sun rays saved nothing measurable), spec §6.5/C.9.
- No launches, no timing (plan task). EditMode suite not run: no code, shader or asset changed (docs only).

## Next
- Owner answers Q1-Q6. Then queue S1 (CPU oracle + tests, no GPU change), then S2.

## For the doc
- Today, pure-black px at native 1080 (139): cliff ledge noon 35,345; arches noon 87,621; cave mouth noon 62,821; sealed room all
  black. The "sky check off" arm takes them to 0 / 0 / 0, but the sealed room reaches mean luma 72 (lit like outdoors). A aims for
  the first three near 0 AND the sealed room staying at 0.
- Plan numbers (estimates, not measured): +2 to +4 ms p50 at 540 rows; +8.3 MB GPU at 960x540; noise about +-6% of ambient on
  partly open faces only; ~0.5 s settle after an edit.
- `Screenshots/139/dc139_surface.png`: rows 1-4, the ledge crescent (top right) and the arch undersides stay black through floor 0.40.
  Only the last column ("sky check off") shows the shaded-rock look the plan targets.
- `Screenshots/139/dc139_caves.png`: rows 1-2 (noon), the last column lights the sealed room and tunnel like open air. The plan
  must keep these black.
