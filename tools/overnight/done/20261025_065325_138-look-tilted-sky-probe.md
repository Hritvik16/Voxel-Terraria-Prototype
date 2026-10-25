# 138-look-tilted-sky-probe

Model: opus

Requires: 137-perf-near-shadow-hole-leap

Approval: owner, 2026-10-24, in chat (AskUserQuestion), Q89 "Tilt the sky check (Recommended)": task 136 found tier-0 faces under a
ledge/arch, out of the sun, render pure black because the sky-visibility probe is ONE ray straight up (blocked -> skyOpen =
_UndergroundAmbientFloor, multiplying ambient AND the shadow floor). Fix (a) from 136's HANDOFF: tilt the probe out of the face.
This is the Phase 7 "FLAGGED, NOT DECIDED" sky probe: the owner has now decided. LOOK change with a small cost: ONE change, look
check + measurement, then STOP_FOR_OWNER. Generality rule (owner, 2026-10-24): no terrain-specific constants; it must behave for any
generator output, future features and player builds.

## Read first
`tools/overnight/done/*136*` or `tools/overnight/HANDOFF.md` history (136), `Screenshots/136/blackshape136.txt`, the comment above the
sky probe in `Raymarch.compute`, `ShadowRayReference` (CPU oracle, dir (0,1,0)) and the sky-probe tests, `KNOWN_ISSUES.md`
(Phase 7 lighting entries).

## Do
1. Behind a flag (`-skytilt 1`; 0 = today): side faces probe along normalize(up + n_horizontal); down faces probe horizontally
   outward (pick a deterministic outward direction from the face normal); up faces keep straight up. Same ray count.
   Update the CPU oracle and tests to the same directions (a flag-aware reference is fine).
2. Look check in ONE launch with `-blackshape 2` (136's A / close / other = arches), plus: a sealed cave or deep cave (must stay
   dark), a cave mouth, an open cliff face in sun (must be unchanged), noon and sun 20. Before|after sheet; count pure-black pixels
   (136's metric). Target: 136's `nosky` look under ledges/arches, caves still dark. Look at every shot.
3. Measure: decisions first; on the charger; warm-up discarded; ONE warm ABBA at walk_near and walk_open (noon), plus one walk arm.
4. Default ON if black pixels drop to near 136's `nosky` numbers, caves stay dark, open faces unchanged, and cost <= ~1 ms p50.
   Otherwise leave OFF and report. 5. Fast EditMode suite once at the end (wait out an owner pause rather than skip it).

## Deliverable
HANDOFF `## For the doc`: before|after sheet (ledge, arches, cave, open face) with a caption saying where to look, black-pixel counts,
the ABBA table, default decision, gameplay consequence in plain words (lighting only). Then STOP_FOR_OWNER. TASKS.md paragraph.
