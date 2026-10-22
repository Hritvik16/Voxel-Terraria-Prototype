# 33c-B3b-soften-regions

Requires: 33-B4-easy-areas

Surface rebuild follow-up to B3 (task 29). Approval: owner, 2026-10-08, Queue decisions
Q25 answer (a): "Soften the region shapes that do not match their class (rolling
highlands first, then badlands 25 and canyonlands 27), keeping the steep class dramatic.
Cost: one B step, judged on the same renders." Queued by the supervisor 2026-10-08 to run
BEFORE B5, so the owner's B5 review pack shows the softened land.

Branch `surface-rebuild`. Terrain-generator change: run `SUITE=all`. v4 only; v3 stays the
default and byte-identical. Read B3's report first (`git show bddb2c9:tools/overnight/HANDOFF.md`).

## Why (from B3)
B3 (60 erosion iterations, half the uplift, `469ce00`) barely moved the slopes: seed 42
median 19.8 -> 18.8 deg. With NO uplift the land got steeper, so the steepness is in each
region's STARTING SHAPE, not in erosion. Seed-42 region medians: alpine 43, volcanic 30,
canyonlands 27, rolling highlands 26, badlands 25 deg. Class medians gentle/broken/steep:
8/22/38 deg. **Rolling highlands is a GENTLE-class region at 26 deg** -- the worst mismatch.

## The one change (v4 only)
Soften the starting shape (amplitude / roughness / ridge sharpness -- whichever parameter
actually makes the slope; say which and by how much) of the regions whose slope does not
match their class: rolling highlands first (bring it toward the gentle class, median
<= 15 deg), then badlands and canyonlands (toward the broken-class median, ~22 deg).
Do NOT soften the steep class (alpine, volcanic): the owner wants it dramatic. Nothing
else changes (biomes, class map, erosion, rivers, B4's easy-area logic).

## Acceptance (seeds 42, 7, 2026, 31337, `PlayMetricsReport`, `REPORTS=1`)
Targets (plan's B3 row; B3 seed-42 values in brackets):
- Land under 15 deg >= 50% [44.3%]; steeper than 45 deg < 10% [13.3%];
  median slope <= 15 deg [18.8]; open (< 20 m relief) >= 20% [12.1%]; rivers >= 15 km [25.5].
- Per-region median slope table, before and after, all four seeds.
- Still: gentle share 50 +- 5%, all three classes on every seed, 0 inselbergs, needle
  guard green on v4, v4 + record hashes stable and seed-dependent, v3 unchanged, map build
  time <= v3 + 2 s, B4's easy-area counts and tests still pass.
- If a target is still missed, report how far, try at most ONE other value, report both,
  ship the better one. Do not tune further (no STOP_FOR_OWNER unless nothing is shippable).
- `SUITE=all` 0 FAIL. No streaming or timing arms (PREAMBLE rule 10).

## Pictures for the owner ("does it still look natural?" -- goes into the B5 review pack)
- Seed 42 slope-band, relief and region-class PNGs: B3 beside this step, same layout as B3
  (`Logs/overnight/b3/maps/`).
- Walk poses at 2 m eye, B3 vs this step side by side: plains 4772,5580; canyonlands
  4428,2764; alpine 3948,3932; plus one in rolling highlands (check the eye is not in rock).
- One `MODE=poses` run on v4: read the shots for holes, floating geometry, a 90%-sea frame.
- Caption every image in HANDOFF (what to look at). Put "Owner check: does it still look
  natural?" with the paths at the top of HANDOFF.
