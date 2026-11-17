# 189-worldgen-c6-landform-signatures

Model: opus

Approval: supervisor queued from the owner-approved look plan (`WORLD_GEN_LOOK_PLAN.md` C6; same approval path as C5 task 179 and C7 task 185), 2026-11-07. Behind a flag `-landsig 1`, default OFF; the owner decides the default from pictures.
Order (owner, 2026-11-05): look first. Read C6 in `WORLD_GEN_LOOK_PLAN.md` (line ~227), task 185's HANDOFF/commit `c7ab7e4` for the flag-behind-default-off shape, `StampTests.NoNeedles_OnTheMap`, and CLAUDE.md "no terrain-specific engineering".

## Do
- Give each landform a meso (20-200 m) shape that does not depend on the biome: dunes = directional ridges with a steep lee side (under grass as well as sand); canyonlands = slot canyons and benches; alpine = ridgelines and side valleys following the slope.
- Per-landform rules from data the generator already has (landform id, slope, flow direction, height); integer, deterministic, no seed constants, nothing tuned to one seed.
- Guard: the no-needle guard stays green; `-landsig 0` is byte-identical to today (prove with the height FNV test).
- Proof: a shape statistic per landform (e.g. relief spectrum 20-200 m, lee/stoss slope ratio for dunes) on 4 seeds, OFF vs ON table; plus an adversarial case (a landform patch at a biome border; a tiny landform patch).
- Tests per rule; fast suite while iterating, `SUITE=all` once at the end. TASKS.md paragraph. No timing arms.
- Pictures (look at each yourself, captions say what to notice): `pair_jungle_dune-sea`, `pair_desert_canyonlands`, `range_200up`, OFF vs ON, same pose, midday sun behind camera, full size.

## End
Done, default stays OFF. HANDOFF names what the owner should compare, with the table and image paths.
