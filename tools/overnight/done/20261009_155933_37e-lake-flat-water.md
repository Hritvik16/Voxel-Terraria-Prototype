# 37e-lake-flat-water

Requires: 37d-B4b-easy-area-sweep

Approval: owner, 2026-10-09 in chat, Q31 part 2 (a): "a small terrain task after 37c that gives
every lake one flat water level (v4 only)". Found by task 39 (fluid pool full), see its report
`KNOWN_ISSUES.md` "POOL FULL / live 750,000" and `Screenshots/Task39/fix_lake_after_2s.png`.
Queued by the supervisor 2026-10-09.

Branch `surface-rebuild`. Terrain-generator change: `SUITE=all`. v4 only; v3 byte-identical.

## The problem
tg2 lake surfaces are STEPPED, not flat: task 39's `-poolprobe` found 85-90k lake water cells with
air diagonally below them. They are not at rest, so the first time a lake is woken (an edit, or a
re-centre seed) the whole lake starts flowing: the slot high-water mark went from 26k to 673k and
lake water spills onto the shore.

## The one change (v4 only)
Every generated lake (and geyser pool, and any other still water body the generator places --
NOT rivers, NOT the sea, NOT the volcano's lava lake unless it has the same defect; say which in
HANDOFF) gets ONE water level: water fills exactly the cells below that level inside the basin,
so no water cell has air beside or below it within the lake. Find where lake water is written
(map lake level vs voxel fill) and fix the cause there; do not add a post-pass that pours water.
Basins must still hold their water (no leaks through a low rim: lower the level to the rim or
raise the rim, say which and why).

## Acceptance (seeds 42, 7, 2026, 31337)
- A test that counts, per seed, generated still-water cells with air directly below or beside
  them (and diagonally below, the probe's measure): **0** on every seed (report before/after).
- Lake count and total lake area per seed reported before/after (should barely change).
- B4/B5/B6/B8/B10 gates still pass; v4 hashes change (expected), stay stable and seed-dependent;
  v3 unchanged. `SUITE=all` 0 FAIL.
- No performance arms (PREAMBLE rule 10). The `-poolprobe` slot high-water at the lake MAY be
  quoted if it is cheap to read from a run you make anyway for pictures; it is not a gate.

## Pictures
- Seed 42 lake shore before/after (the `fix_lake_after_2s` pose or a ground pose at a lake),
  captioned: is the surface one flat level, and does it stay put for a few seconds without an edit?

## Re-queued 2026-10-09 16:00 (supervisor) -- READ FIRST
The first two attempts' EditMode run grew Unity to a **22.5 GB footprint** (swap 14.8 GB;
one attempt forced a hard power-off of the Mac). The cause was the attempt's own new test,
`LakeRestTests.cs` (generator code was untouched). It is moved OUT of the project to
`tools/overnight/blocked/37e-LakeRestTests.LEAKED.cs.txt` -- **do not restore it or copy its
structure** (a parallel `RestJob` over every lake cell of 4 seeds via a voxel-sampling hook).
Owner, 2026-10-09: rewrite the test from scratch rather than diagnose the old one.
- Write the acceptance test small and bounded: one seed per test case, count from the MAP
  data (lake level vs per-column fill) wherever possible, sample voxels only where needed,
  no allocation that scales with (all lake cells x voxel height) at once.
- Run it FIRST on its own (`-testFilter`) for ONE seed and check Unity's memory while it
  runs (`footprint <unity pid> | head -3`); expect well under 4 GB. Only then all 4 seeds,
  then `SUITE=all`.
- `tools/mem-watchdog.sh` now runs with every locked Unity run: it kills Unity past an
  8 GB footprint (or 10 GB swap) and PAUSEs the queue. If your run is killed, say so at
  the top of HANDOFF.md with STOP_FOR_OWNER.
