# 29-B3-light-erosion

Requires: 17-B2-landform-by-flatness

Surface rebuild step B3. Approval:
- The plan was approved by the owner 2026-10-02 (rebuild plan Q11 (a)). B3 row, plan
  section 4; plan rule 5 (section 1): "Erosion as a light valley pass. The same
  `Erosion.cs`, run shorter on lower uplift. It cuts valleys and river channels but no
  longer roughens every slope." Erosion iterations: **60 (today 240)**. Owner intent:
  natural-looking, nothing sticking out; "what goes is the noise between them" (review §13).
- B2's gentle share: owner picked **(a) keep 50%**, no "+cliffs" (Queue decisions Q15,
  2026-10-04). So v4's `gentleShare` default stays 0.50.
- Queued by the supervisor 2026-10-04.

Branch `surface-rebuild`. Terrain-generator change: run `SUITE=all`. Read B2's report
(`tools/overnight/done/20261003_203748_17-B2-landform-by-flatness.md` and its HANDOFF in
git history, commit 3879c29) first. v4 only; v3 stays the default and byte-identical.

## The one change (v4 only)
Erosion as a light valley pass: `WorldMap` calls `Erosion.Run(..., EROSION_ITERATIONS)`
(`WorldMap.cs` ~191, today 240). On v4 run it with **60** iterations, on lower uplift
(say what "lower uplift" means in the code you change, and by how much). Keep the
valley and river-channel cutting. Nothing else changes: biomes, regions, classes,
features and placement are later steps.

## Acceptance (seeds 42, 7, 2026, 31337, `PlayMetricsReport`, `REPORTS=1`)
Targets from the plan's B3 row (v3 seed-42 values in brackets; B2 v4 at 50% in braces):
- Land under 15 deg **>= 50%** [30%] {42.5%}.
- Steeper than 45 deg **< 10%** [29%] {15.1%}.
- Median slope **<= 15 deg** [30 deg] {19.8 deg}.
- Open land **>= 20%** [4%] {11.2%}.
- Seed 42 rivers **>= 15 km** [about 23 km].
- Still from B2: gentle share 50 +- 5%, all three classes on every seed, 0 inselbergs,
  needle guard green on v4, v4 + record hashes stable and seed-dependent, v3 unchanged,
  map build time <= v3 + 2 s.
- If a target is missed at 60 iterations, report how far, try at most ONE other
  iteration count or uplift value, and report both. Do not tune further: the owner
  decides (put it at the top of HANDOFF, no STOP_FOR_OWNER unless nothing is shippable).
- `SUITE=all` 0 FAIL.

## Pictures for the owner's check ("does it still look natural?")
- Seed 42 region-class and slope-band PNGs: B2 (v4 50%) beside B3, same layout as B2.
- Walk poses at 2 m eye in a gentle, a broken and a steep region (B2's three spots:
  plains 4772,5580; canyonlands 4428,2764; alpine 3948,3932 -- check the eye is not
  inside rock), B2 vs B3 side by side.
- One `MODE=poses` run on v4. Read the shots for holes, floating geometry, a 90%-sea frame.
- Every image cited in HANDOFF gets a caption saying what to look at.
- Put "Owner check: does it still look natural?" with the image paths at the top of
  HANDOFF (no STOP_FOR_OWNER).

## Streaming re-measure (PREAMBLE rule 9: warm steady state)
- One warm-up launch, then `MODE=stream` walk x2 and fly 14 m/s x2 on v4, interleaved with
  the same arms on v3, one build. Report section 7 (REQUIRED / CAPACITY / MET) and every
  stage, v3 beside v4. Do not stop for the owner over streaming numbers.
- Check `pmset -g batt` and the owner-pause NOTE; rerun any arm on battery or paused.
