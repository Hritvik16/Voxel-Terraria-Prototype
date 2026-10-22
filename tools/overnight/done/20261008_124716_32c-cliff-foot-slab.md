# 32c-cliff-foot-slab

Approval: follow-up named by task 29 (B3) HANDOFF, item 3 -- a small bug fix, queued by the
supervisor 2026-10-08 under SUPERVISOR.md's backlog rule. Owner intent it protects: "natural-looking,
nothing sticking out". No performance arms (PREAMBLE rule 10). Fast suite, or `SUITE=all` if
`Assets/TerrainGen/` changes.

## The bug
Since B3 (`469ce00`), the v4 `MODE=poses` shot `cliff_foot_1` shows a thin slab standing on the
ridge (`Logs/overnight/b3/maps/cliff_foot_1_slab_crop.png`; shots `FarBand/2026-10-08_090838_world/`
and `FarBand/2026-10-08_111413_world/`). B2's shot `FarBand/2026-10-04_132705_world/cliff_foot_1_golden.png`
has a clean ridge. The band view (`cliff_foot_1_golden_band.png`) shows the far band F0 draws it,
not voxels.

## Do
1. Find out which it is, from the data (no guessing): a one-cell spike in the v4 height map (read
   `WorldMap.Height` around that ridge; compare with the needle guard's 25 m / 40 m rule -- why did
   `StampTests.NoNeedles_OnTheMap` not catch it?), or a far-band bake/sampling artifact.
2. Fix the cause with ONE change. If it is a map spike: make the guard catch this shape too (test
   first, red on 469ce00, green after) and remove it in `TrimSpikes` or the erosion path. If it is
   the far band: fix the bake. v3 must stay byte-identical.
3. One `MODE=poses` run on v4: the ridge is clean, no new holes or floating geometry. Captioned
   before/after crop in HANDOFF.
