# 85-engine-12-handover-near-first

Approval: engine plan step 12 (ENGINE_PHASE_PLAN.md section 3, approved with the plan) + owner answer **Q47 (a)**, 2026-10-12
in chat: "It's obviously near" -- near terrain loads first. Queued by the supervisor 2026-10-12. Branch `engine-phase`.
This amends Amendment 8.14 section 3 "Order" (Q2 (a)) -- approved by that answer.

## The problem (83's HANDOFF, done/20261012_184256_83-...HANDOFF.md, "Found")
Shell streaming orders the COARSEST missing chunk first (8.14 section 3 "Order", Q2 (a): "a missing fine chunk only looks
blockier"). That is false for hollow shells: shell k+1 stores nothing under shell k, so a missing shell-1/2 chunk is a HOLE
(far band), and `ShellResolve` ends every ray that meets an absent finer chunk inside a coarser hole. Since 83 stores s3-5
instead of refusing them, s1-2 held 0 chunks after 75 s; shells needed 127-296 s per pose to fill
(Screenshots/83/wait75/sheet_shells_1.jpg: rows 1 and 3 grey in the tint).

## Change (shells-on path only; `-shells 0`, the default, stays byte-identical)
1. **Near first (Q47 (a)).** Order shell chunk requests by what they cover on screen: nearest the camera first across all
   shells (a shell-1 chunk at 40 m before a shell-5 chunk at 1.5 km), still biased toward the velocity (section 4.3 prefetch).
   One sort key, e.g. distance to the chunk / its edge length (= inverse of its screen size), smallest first. State the key.
   A coarser shell still fills its own band, just after the nearer bands.
2. **Step 12: hand-over by distance, not residency (8.14 row 5).** With shells on, a ray reads shell k between its bounds;
   `FarStartT`'s residency DDA and `-detailspeed` are not used on that path (leave the shells-off path as it is: it retires
   when shells become the default, a later task). The prefetch margin is > 0 by construction (each ring stores past its
   hand-over distance, >= 12.8 m for shell 0): show it in the streamprobe section-7 latency line or a test.
3. Amendment 8.14: section 3 "Order" and row 5 -> "nearest on screen first (owner Q47 (a), 2026-10-12); Q2 (a)'s
   coarse-first is replaced because shells are hollow". CLAUDE.md: one short paragraph.

## Acceptance
- A test: with a fixed camera and request budget, the order puts every shell-1 chunk inside the view before any shell >= 3 chunk
  farther than it; shells-off path byte-identical (existing tests).
- ONE player launch, `-shells 1`, <= ~10 min (PREAMBLE launch budget): 4 poses (peak_from_2km, glide_400m,
  eye_level_1, cliff_foot_1) at a 75 s wait, like 83's wait75 run; no long-wait arm. Report per pose: chunks per shell
  stored and missing at 75 s, and the time shells 1-2 completed if they did, before (83's wait75) -> after.
  Sheet via `tools/shells80-sheet.py` next to `Screenshots/83/wait75/sheet_shells_1.jpg`; look at it.
- Fast EditMode suite while iterating as PREAMBLE says; `SUITE=all` once at the end. `run-phase7-rig.sh` unchanged.
- No frame-time measurement (PREAMBLE rule 10). HANDOFF `## For the doc`: fill times before -> after, the 75 s sheet.
