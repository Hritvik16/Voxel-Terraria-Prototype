OWNER ANSWERS (2026-11-06): Q2 = (b) leave the route test red until C5, ford + scree together (also answers 176d Q1). Q1 pending.

STOP_FOR_OWNER -- task 178 (C4 cliffs). Viewer: `Screenshots/178/viewer.html`. It shows full size; Space flips before | after; arrows change the image.
- **Q1. Keep these cliffs?** The rock below the soil is now a few THICK layers, each 9.6-38.4 m (24 m nominal), with wavy boundaries. Every biome uses its own 3 rocks. Badlands' thin stripes are gone.
  - Faces also get vertical fluting, a ledge every 4 m and a scree apron at the foot.
  - (a) **Recommended:** keep.
  - (b) Keep the layers, change some layer rocks. For example: jungle's dark layer is MossRock, which reads as a GREEN band on `coast_cliff` (it can look like grass).
  - (c) Drop it (`-cliffs 0` = before).
- **Q2. One route got a new Sheer step.** The route test is still red. Seed 42 now has 1.5 m of Sheer: 0.5 m is 177's known river-bank step, and 1.0 m is NEW.
  - The new step is on route 2-3 at (4471, 2390). The map slope there is 22 deg, on a 6.4 m route fill. Below 40 deg (FACE_FROM_DEG) only the scree apron + rubble is active, so the scree steepened it.
  - (a) **Recommended:** the relief skips route cells, the same way it already skips water. This needs the record's route flag in `MapView` (a small generator change).
  - (b) Leave it red until C5, with the bank.
  - Also: seed 42's "next waypoint in sight 61.9 % < 65 %" is in the same failure message. Sight reads only the map, and 178 does not change the map, so this was already failing at 177. 177's log printed only the first line of the message.

# 178-worldgen-c4-cliff-geology: thick depth layers + face relief (fluting, ledges, scree); whole suite PASS 1064 FAIL 1 (the known route test, now also +1.0 m from scree: Q2)

## What changed (commit 80cc785)
- `DetailField.LayerRock`: the stone zone (above sea - 50 m) is now layers by height: `LAYER_THICK` 24 m +- 30 %, boundaries waved by `LayerWave` (+-4 m / 90 m, +-1.5 m / 26 m).
  - Each layer's role comes from a hash: main ~50 %, light ~30 %, dark ~20 %. `LAYER_ROCKS` (6 biomes x 3) is the only per-biome data.
  - The brick prover bounds the wave (`LAYER_WAVE_BOUND`).
- `ShapeField.FaceRelief` uses only the map's spline slope and height:
  - fluting +-0.8 m sideways (from 55 deg, full at 70 deg)
  - ledge treads every 4 m along the layer wave
  - a scree apron from the map's concavity at +-20 m (<= 10 m, rubble on top; scree columns take Cover's `steep` cell)
  - skipped within ~20 m of lake/river cells and under the sea
  - `BandLimitBound(spec, ..)` covers the far-band footprints.
- Off switch: `TerrainSpec.cliffsOff` / `-cliffs 0` (engine + FarField). v3 is unchanged.
- New `CliffTests` (4 cases) and `tools/cliff178.py` (sections, top views, viewer).
- Test fixes: `EditSummaryTests.Build`'s pit is aligned to the 8-voxel grid. Its "shell 3 changed something" check depended on height & 7; the summary still equals the reference at every level. ShellFill GOLDEN re-pinned (85 chunks; the chunk set moved).

## Evidence
- CliffTests 4/4 passed (`TestResults_filter.xml` 03:29): layers >= 9.6 m, only the biome's rocks in all 6 biomes; relief only on faces/scree; band limit + LOD-0 contract.
- Whole suite, first run: **PASS 1062 FAIL 3 SKIP 33** (`Logs/overnight/178_all.out`):
  - EditSummary: fixed (fixture).
  - ShellFill GOLDEN: re-pinned.
  - Route test: Q2.
- Whole suite, rerun after the fixes: **PASS 1064 FAIL 1 SKIP 33** (`Logs/overnight/178_all2.out`, 41 min). The only failure is the route test (Q2).
- Shots: one launch per arm, 173's `-wsfixed` poses, 30 s shell cap. Logs: `Logs/lookreview_20261106_032950.txt` (after), `..._033816.txt` (before).
  - Shells 4-7 were not stored within 30 s in either arm (`[shells]` lines). The distant background is the far band.
- White dashes (173): the forest_breaks before-shot has many tiny white dashes on the grey face at centre. After: fewer, same place. They appear in both arms, so they are not material; not fixed by 178.
- Rough cost (no timing arm):
  - Mixed bricks in a 12.8 m block at the face: 5481 -> 6275 (+14 %), 1560 -> 1828 (+17 %), 2757 -> 2979 (+8 %). All-solid 2+-rock bricks: 1183 -> 2207, 516 -> 783, 12 -> 359.
  - `Sample` (Mono): 60.3 -> 62.8 us/column (+4 %).
  - In the player, a worker spent 110-121 ms per chunk (after) vs 72-83 ms (before). Not interleaved and only 30 s per pose: flag it for the perf stage. `ScreeFill` adds 4 map samples to EVERY land column.
  - Log: `Logs/TerrainGen/c4/cliffs178.txt`.

## Next
- Owner: Q1, Q2. If (a)/(a): queue a 178b for the route-cell skip, then re-run RouteTests.

## For the doc
- Rock layers on a face: one rock per biome (badlands 3.2 m stripes) -> **3 rocks per biome in 9.6-38.4 m layers** (`CliffTests`).
- Mixed bricks at the 167 m face: 5481 -> **6275** (+14 %) (`Logs/TerrainGen/c4/cliffs178.txt`).
- Column sample cost (Mono): 60.3 -> **62.8 us** (+4 %). Worker ms per chunk in the shot launches: 72-83 -> **110-121** (rough, not a timing arm).
- Route Sheer, seed 42: 0.5 -> **1.5 m** (new 1.0 m on a scree apron; Q2).
- Images:
  - `Screenshots/178/viewer.html` image 1: `shot_cliff_after.png` vs `_before.png`. Right half: badlands' thin red/cream/ochre stripes become thick red layers. Bottom: one dark blue-grey layer runs across both biomes at the same height.
  - `Screenshots/178/shot_coast_cliff_after.png`: the cliff behind the lake. Look for the grey face with a red and a green (moss rock) wavy layer; before it was one grey wall.
  - `Screenshots/178/section_cliff.png`: right panel (after) is a vertical cut through the face. Look for the small ledge steps on the face, the wavy thick layers and the scree mound at the foot (left = before: smooth, one rock).
