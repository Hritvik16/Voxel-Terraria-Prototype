# 61-engine-8b-halfres-shadows

Approval: owner, 2026-10-10 ~23:25 in chat (step-8 question after task 59): "Half-res shadows" -- trace sun-shadow
rays for a quarter of the pixels and fill the rest with an edge-aware reuse. ENGINE PLAN STEP 8, still the ONE
shadow-cost change (roof skip, task 59, was slower on tg2 and stays OFF). Queued by the supervisor 2026-10-10.
Branch `engine-phase`. This IS a performance task: PREAMBLE rule 9 (warm steady state) applies. One change only.
The owner judges the LOOK: whatever you build ships as an F6 menu option with play's default UNCHANGED, plus
side-by-side stills; the default flip is the owner's call (STOP_FOR_OWNER at the end).

## Background
- Task 57 (git `572f5f8` HANDOFF): play frame GPU-bound; sun shadows cost 14.2 ms (walk_near, canyon wall 10-30 m)
  / 4.2 ms (walk_open) of 49.4 / 30.4 ms at noon, FSR 3 at 540. Noise floor 1.4-1.6 ms.
- Task 59 (`tools/overnight/done/59-*.HANDOFF.md`, git `670dd96`): harness `run-cost-split.sh` now has
  `-costarms a,b` and `-costsun DEG`; use them.
- EXISTING mechanism, read it first: rung 1 (`_ShadowQualityLevel == 1`) in `Raymarch.compute` ~line 3020-3050
  already does 2x2 quad reuse (the quad lead traces, the others take its value via quad ops), but only for
  `distant` pixels beyond `_Rung1DistVoxels` (default the LOD1 boundary). `RaymarchFeature.Rung1DistanceOverrideM`
  (line ~180) / `LightingConfig.rung1DistanceM` set that threshold; note `<= 0` means "use LOD1", so "all
  distances" needs a tiny positive value or an explicit flag.
- WARNING in the same code (temporal block comment, measured 2026-09-26): a per-PIXEL 1-in-4 pattern cut rays 74%
  but saved only 9 ms, because an 8x8 group runs in lockstep and waits for its slowest lane. A quad-lead pattern
  still leaves 16 tracing lanes per group, so it may save little. Play also runs temporal shadows (`-tshadow`,
  per-tile rotation) -- check which shadow settings play's FULL arm actually has before reasoning about savings.

## Decide before measuring (CLAUDE.md measurement rule)
- Step A, cheapest version: rung 1's quad reuse at ALL distances (threshold ~0), as a capture-only arm
  `halfres` in the cost split. Then:
  - saves >= 2.0 ms p50 on walk_near at noon -> keep this design; go to "Ship" below.
  - saves < 2.0 ms -> the lockstep problem. Try ONE group-coherent variant (e.g. a separate shadow pass at half
    width x half height over the primary hit buffer, then a depth/normal-aware upsample; or whatever the code
    makes cheapest), measured the same way. If that also saves < 2.0 ms, or it needs a memory-layout / frozen-API
    change, STOP_FOR_OWNER with the numbers and a size estimate (S/M/L) -- do not build further.

## Ship (only if a variant clears 2.0 ms)
- Play setting `PlaySettings.shadowRes` Full/Half (saved like task 58's others; `-shadowres full|half` overrides,
  last match wins), F6 menu row "Shadow resolution" under Shadow quality. Play DEFAULT stays Full. Rigs and capture
  modes unchanged unless they pass the flag.
- Edge handling: a pixel whose depth/normal disagrees with the traced sample must trace its own ray (no shadow
  bleeding across silhouettes). Say in HANDOFF how you checked this.

## Acceptance
1. Warm A/B with `run-cost-split.sh` (warm-up launch first, rule 9), FULL vs the variant, ABBA, both arcs
   (walk_near, walk_open), at noon AND sun 10 deg (`-costsun 10`). p50/p99 and saved ms per arc vs the noise floor.
2. `tools/with-unity-lock.sh` + `EXTRA="<the flag>" ./run-phase7-rig.sh`: report Gate 3 (today 430/438 = 98.17%).
   Half-res is NOT exact, so a lower match is expected; report it, never loosen the gate. If Gate 3 goes red, say
   so plainly in the STOP.
3. Fast EditMode suite green (+1 PlaySettings test if you add the setting).
4. Stills for the owner (well lit: midday or golden hour with the sun BEHIND the camera, owner rule 2026-10-10):
   walk_near and walk_open, Full vs Half, same pose, plus one 4x crop of a shadow edge on a silhouette each.
   Paths in HANDOFF.

## For the doc
p50 frame ms Full -> Half per arc and sun height (and the fps), Gate 3 result, the stills with a caption saying
where the shadow edges are, and the question: make Half play's default, yes/no.

## Owner answer (2026-10-11, delegated)
Owner at 00:10: "pick your recommendations, i can always queue something to change them". Supervisor applied Q1 (a) keep Full, Q2 (a) close step 8 (no group-coherent variant). Recorded in the doc as changeable.
