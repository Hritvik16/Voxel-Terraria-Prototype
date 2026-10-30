# 37c-B3c-highland-variation

Requires: 37-B10-savanna-badlands

Terrain step after B10. Approval: owner, 2026-10-08 23:4x, Q29.2 (b): the softened rolling highlands
are "too uniform" -- "add variation (knolls, small valleys) in a step after B10". Queued by the
supervisor 2026-10-08. Targets are the supervisor's proposal (flagged in the doc).

Branch `surface-rebuild`. Terrain-generator change: `SUITE=all`. v4 only; v3 byte-identical.

## Background
Task 33c (B3b, `fd6f2c1`) softened rolling highlands (talus 0.1925, E x2), canyonlands (talus 0.52),
badlands (E x1.3) on map v4: seed 42 rolling highlands median 25 -> 15 deg. The result is an even
15-30 deg band (`Logs/overnight/b3b/maps/seed42_slope_bands_east_B4_B3b.png`, right panel: the east
coast is uniformly yellow). Owner wants it walkable AND varied.

## The one change (v4 only)
Break the uniform band inside rolling highlands with sparse knolls (rounded hills tens of metres
high) and small valleys/dells, at a few-hundred-metre spacing, seeded. Keep it walkable.
Targets (rolling highlands cells, seeds 42, 7, 2026, 31337): median slope stays 13-18 deg; the
slope distribution WIDENS (more < 10 deg AND more 30-40 deg than B3b; report the histogram);
> 45 deg share in the class does not rise above B3b + 1 point; no spikes (needle guard green).

## Acceptance
- Slope histogram for rolling highlands, B3b vs now, per seed; the per-class slope table (gentle /
  broken / steep median, land < 15 deg, > 45 deg, open, easy ground) v3 / B3b / now.
- B4 easy areas, B5 features (count, spacing, 3 km visibility) still pass.
- Pictures (captioned): seed 42 slope bands east, B3b | now; two 2 m-eye walk shots in the
  highlands at NOON, B3b | now.
- Hashes stable and seed-dependent; v3 unchanged; build <= v3 + 2 s; `SUITE=all` 0 FAIL.
  No performance arms (PREAMBLE rule 10).
