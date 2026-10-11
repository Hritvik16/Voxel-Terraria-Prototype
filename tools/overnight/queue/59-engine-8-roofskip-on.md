# 59-engine-8-roofskip-on

Requires: 57-engine-7-frame-cost-split

Approval: owner, 2026-10-10 ~22:15 in chat, Q42 (a): "we can do a" -- ENGINE PLAN STEP 8, the ONE shadow-cost
change chosen from task 57's cost split. Queued by the supervisor 2026-10-10. Branch `engine-phase`.
This IS a performance task: PREAMBLE rule 9 (warm steady state) applies. One change only.

## Background
Task 57 (`tools/overnight/done/57-*.HANDOFF.md` / git `2c6e0bb`): play frame GPU-bound; sun shadows cost
14.2 ms (walk_near, canyon wall) / 4.2 ms (walk_open) of 49.4 / 30.4 ms at noon, FSR 3 at 540.
The shadow roof skip already exists, OFF by default: `RaymarchFeature.ShadowRoofSkip` (`-roofskip 1`),
`Raymarch.compute` HeightMip block (2D max-height pyramid over the tier-0 window, rebuilt per frame; exact by
construction). September evidence (HANDOFF_2026-09-24.md row "Shadow roof skip"): 0 differing px at 9 poses,
steps/ray -39%, sustained golden orbit 31.4 -> 28.7 ms. The owner then said "leave OFF until the owner says"
and that re-enabling needs `run-phase7-rig.sh -roofskip 1` (Gate 3 oracle). The owner has now said ON.

## Change
Roof skip ON for interactive play (`./run-play.sh` and the play preset path), the same way task 52 made FSR 3
play's default: rigs and capture modes keep today's default (OFF) unless they pass `-roofskip 1`, so historical
rig numbers do not move. `-roofskip 0` still turns it off in play. If task 58's settings menu has a shadows
section, add a "Roof skip (exact, faster shadows)" toggle there (saved like the others).

## Acceptance
1. Exactness: `./run-roofskip-ab.sh` part 1 only (`-roofskipab`, 9 poses) on today's build: 0 differing pixels.
   If any pixel differs, STOP_FOR_OWNER with the diff images.
2. `tools/with-unity-lock.sh ./run-phase7-rig.sh -roofskip 1` (or the rig's way to pass it): Gate 3 green.
3. Warm A/B with task 57's harness (`run-cost-split.sh`, its FULL arm), roof skip 0 vs 1, ABBA, both arcs
   (walk_near, walk_open) at noon AND, if the harness can pin the clock to golden hour (sun ~10 deg), the same
   at golden hour (low sun is where roof skip helps most). Warm-up launch first (rule 9). Report p50/p99 and the
   saved ms per arc against 57's noise floor (1.4-1.6 ms). If golden hour cannot be pinned without new code
   beyond a capture-only flag, add the capture-only flag.
4. Fast EditMode suite green.
5. One still: walk_near at golden hour, roof skip ON (sun behind camera, well lit).

## For the doc
p50 frame ms OFF -> ON for each arc and sun height, the fps those mean, exactness result, Gate 3 result, the still.
