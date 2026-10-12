# 76-rivers-r1-r2-r6: DONE -- R1 + R2 + R5 + R6 built; every generated fluid at rest by test; commit `b5ec224` (engine-phase)
## What changed
- **R1** `Assets/TerrainGen/Rivers.cs` (new), map v4 only (v3 untouched): `Profile` (map stage, after the routes) sets each
  river cell's level = bed + depth, capped by every lake it drains out of, raised to its downstream cell (never rises
  downstream), last cell AT the lake/sea level when within 2 m. `Column` (from `ShapeField.Sample`): level per voxel column
  along cell-centre -> parent segments (steps of ONE voxel every few metres), trapezoid channel (edge 0.3 m, 31 deg shelf,
  <= 4 m deep, holes filled), banks of the NEAREST channel at 27 deg. **At rest, how:** the first column of each one-voxel
  step down is a LIP -- solid up to the upper level, material `GenMat.RiverLip` (8 = engine 136): solid to the CPU (CA, motor),
  drawn as water. R3 swaps it for held water later. River fluid is `Rivers.FLUID` (water today; Q5). Falls > 2 m per cell
  flagged `FLOW_FALL` (365 cells on seed 42) for R4 -- no curtains built; they are lip staircases now.
- Flow map `WorldMap.FlowDir` (16 bits/cell = 2 MB: direction, fall, slope class, river size), `MapView.flowDir`, plumbed
  into every MapView site (chunk source, shells, far band, tests). Hydrology keeps its drainage tree for it.
- Things rivers broke and the fix: stamps (cave mouths, fissures, skull cave, arch/hoop openings) and cave tunnels kept out of
  rivers (`nearRiver`, `riverFloor`, like NearLake); the geyser ring holds a river (`riverHoldY`); minimap samples rivers
  at its own step (rivers stay one line). **Volcano lava lake was NOT at rest** (seed 2026: 917,162 lava voxels beside air
  in the ring between caldera floor and inner wall) -> v4 fills that ring to the lava level (`Stamps.Column`).
- **R2/R5** `Raymarch.compute`: `RiverLipAsWater` (lips march, see-through and shade as water, always); with
  `_RiverLook`: a lip's side face drawn as surface where the flow map has a river (every tier) or over a 1-voxel step (tier 0);
  ripples moving along the flow + foam by slope class/fall -- from the MAP (`FarField.RiverFlow` buffer), so tier 0, coarse
  tiers and the far band match; ripples fade to their mean by 400 m, foam keeps its mean; foam lowers see-through (75 kept).
  `RaymarchFeature.RiverLook` (off: rigs byte-identical), `PlaySettings.riverLook` = on, `-riverlook 0|1`; NO F6 row yet.
  Task 73's prototype removed. Palette entry for 136 (water colour, CPU views only).
- **R6** `Assets/TerrainGen/Tests/FluidAtRestTests.cs`: rivers + sea coast (1 in 6 coast cells) + lakes (seeds 42, 7), the
  volcano lava lake (seed 2026, the first test seed that draws one), 24 underground pockets (seed 42), profile monotonicity.
- `-riverdemo`: seed 42 takes 73's four cells; off = shape only (look + see-through off), on = play look; site A also at
  20 m / 150 m / 800 m / 2 km; census counts lip risers. `tools/river76-sheet.py`. BUGS #5, #7 moved to Fixed; CLAUDE.md, plan status.
## Evidence
- `SUITE=all` **PASS 732 FAIL 0 SKIP 19** (Logs/overnight/76-all2.out; first run had 3 fails, fixed: palette, portability, 8 m Sheer on routes).
- R6 (Logs/TerrainGen/fluid_at_rest_42.txt, _7, volcano_at_rest_2026, pocket_at_rest_42): **0 unsettled** voxels: river 124 M /
  113 M voxels, sea 157 M / 179 M, lakes 80 M / 95 M, lava lake 725 M (was 917,162 unsettled), pockets 11.4 M. Routes Sheer 0.0 m.
- FNV seed 42 v4: Height **unchanged** 639552DD962C4B28; Water F93A59C2224B54A8 -> **6E53C3AD2909F15C** (Logs/TerrainGen/river_profile_42.txt).
- Demo: Logs/overnight/76-demo-player.log; shots Screenshots/76/shots/, sheets Screenshots/76/. Looked at all three sheets.
## Not done / caveats
- Pool = 0 tiles on arrival: proven only by the EditMode at-rest tests, not by a runtime pool probe (a `MODE=poolprobe` walk is the check).
- Site B: the river's last cells lie in the lake's shallow fringe and stand 1-4 voxels above it; a short ribbon edged by lip lines
  shows (row B of sheet_near). Site D: through clear sea water the carved channel shows as a trench on the sea bed.
- Steep reaches (A, C) are lip staircases drawn as foaming water: R4 (waterfall curtains, plunge pools) is still to do.
- Cost not measured (rule 10): `Rivers.Column` adds a 7x7-cell scan to every S call (+8 level evaluations next to rivers);
  in the final demo B/C mid shots had unstreamed coarse blocks that the previous run did not -- check generation at the next perf milestone.
## Next
- Owner: look at the sheets; R3 (held water) and R4 (falls) after step 17. Small follow-ups (not queued): F6 row for "River look";
  end river cells inside a lake's fringe at the lake level (site B ribbon).
## For the doc
- Tallest exposed water wall at 73's sites A/B/C/D: **7.6 / 3.2 / 10.5 / 1.3 m -> 0 / 0 / 0 / 0 m**; water side faces per top
  face 0.79 / 0.16 / 1.23 / 0.10 -> 0.00 at all four (Logs/overnight/73-demo-player.log -> Logs/overnight/76-demo-player.log).
  Lip risers (drawn as surface) per water top face: 1.92 / 0.20 / 10.24 / 0.03.
- At-rest (R6): unsettled fluid voxels -> **0** for rivers, sea, lakes, pockets; volcano lava lake **917,162 -> 0** (seed 2026).
- Water FNV F93A59C2224B54A8 -> 6E53C3AD2909F15C; Height FNV unchanged. River cells 3,354; falls flagged for R4: 365.
- `Screenshots/76/sheet_near.jpg`: rows = sites A-D from ~12 m; left = before (task 73), middle = new shape with the look off,
  right = play look. Watch row C: the 10 m blue wall becomes a foaming cascade between sloping banks; row D: the slab
  staircase into the sea is gone, the river enters flush.
- `Screenshots/76/sheet_mid.jpg`: same from ~100 m. Left column's floating blue slab staircases become one thin
  continuous river in the middle/right columns. (B/C right halves: coarse blocks = streaming not finished, not the river.)
- `Screenshots/76/sheet_distances.jpg`: site A at 20 m / 150 m / 800 m / 2 km, top row look on, bottom off. The river stays the
  same colour by slope at each distance: foam-white on the steep lower reach, blue on the gentle upper reach, a light line at 800 m.
