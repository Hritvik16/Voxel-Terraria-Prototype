# 79-see-through-brick-march (LATER -- do not queue until the owner asks; a PERFORMANCE task)

Approval: owner, 2026-10-11 in chat, after task 75b: accepted see-through at +2.0 ms for now, and "we should add this
for much later as we will eventually need to deal with the performance issue". Parked by the supervisor 2026-10-11.

## Background (task 75b, commit 59c55cb, HANDOFF 9e070b0)
- See-through liquids at the lake shore: +5.0 ms -> +2.0 ms (warm ABBA p50, `Logs/costsplit_2026-10-11_231717.txt`).
- Split: the march through the fluid to the bed is ~all of the cost; end blend ~0.1-0.3 ms; far band ~0.
- 75b's change: stop the march where the bed's weight (1 - F) * T < 10% (`SEE_CUT`, was 2%) + `SeeCutFade`.
  Tried and rejected: step cap 192 -> 64 (saved 0.0), coarser march past 3 / 6 m (+2.9 ms worse).

## Change (one)
March a uniform-fluid run at the brick level without re-entering the outer loop: chain leaps through uniform water
bricks, as `_TraversalMode == 2` does for air (`Raymarch.compute`). Alternative if that does not pay: cut on luminance
instead of the clearest channel (green sets the path today). Pick one, say why.

## Acceptance
- Same look: `-liquiddemo` before/after sheets (pools + lake shore/near/mid/far), captions.
- Warm-steady-state cost split (`run-cost-split.sh`, `EXTRA="-costarms see_on,see_on -costpaths lake"`), ABBA, before
  -> after; also a wall-of-water pose if one exists by then (75b: task 76 removed the old one).
- `run-phase7-rig.sh` green, shots pixel-identical with the setting off. Fast suite green.
- Stop for the owner after the measurement (CLAUDE.md: one performance change per measurement).
