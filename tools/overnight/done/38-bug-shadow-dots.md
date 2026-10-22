# 38-bug-shadow-dots

Requires: 37-B10-savanna-badlands

Bug fix. Owner, 2026-10-08 ~19:30, while playing `./run-play.sh` (tg2, map v4, Quality preset
960x540, shadows "Full (soft edges)", TAA off): "what looks like shadow acne -- just dots on the
terrain ... These are genuine bugs." Owner wants it queued AFTER the B steps (not right after B5).
Queued by the supervisor 2026-10-08.

## Evidence (owner's screenshots, 2880x1800, read them -- crop at native size to see the dots)
`~/Desktop/Bugs?/` (quote the path: the folder name has a `?`)
- `Screenshot 2026-10-08 at 7.28.49 PM.png`, `7.28.54`, `7.28.55`, `7.28.56`: grass slopes, low
  sun, y ~589 m, alt ~79 m, next to a snow region (minimap top right). Look at the bottom and right
  thirds: the TOP faces of grass voxels show LIT/DARK TRIANGLES and sawtooth patches, i.e. a single
  flat face is partly shadowed with diagonal edges. Supervisor's read: looks like false
  self-shadowing at a grazing sun; not confirmed.
- `Screenshot 2026-10-08 at 7.29.54 PM.png` (and its " 1" copy): flat sand beside a lake,
  y ~532 m, warm low sun. Middle/right of frame: scattered small dark SPECKS on flat sand. Unknown
  whether these are real shadows of 1-voxel bumps or acne.

## The one change
1. Reproduce: find matching poses (grass slope near snow, sand lakeshore) on seed 42 v4 at a low
   sun (golden hour, `N`) with the play preset; capture stills at native size. Compare `-taa 0`
   and shadow quality rungs (`J`) and hard/soft (`K`) to localise which pass makes the dots.
2. Decide, with evidence: (a) false self-shadowing (ray origin / bias / normal offset / temporal
   shadow reuse / half-res upsample), or (b) real geometry shadows. For (b) show the voxel that
   casts each speck (`ShadowRayDump` / `ShadowRayReference` exist).
3. If (a): fix it inside the shadow path (bug fix; no API or layout change). If (b): no change;
   STOP_FOR_OWNER with the pictures and the question whether that look is wanted.
- Phase 7 correctness: `./run-phase7-rig.sh` must stay green (wrap with `tools/with-unity-lock.sh`).
- No performance arms (PREAMBLE rule 10); name any expected cost change in HANDOFF.

## Acceptance
- Before/after stills at the same poses and sun angle, cropped at native size, captioned (where the
  dots were, and that they are gone or explained). Also one noon pose to show nothing else changed.
- `ShadowRayTests` + fast EditMode suite 0 FAIL; `run-phase7-rig.sh` PASS.
