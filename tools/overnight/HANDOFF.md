# 185-worldgen-c7-stamps-sit-in-land: DONE behind `-stampfit 1` (default OFF). All five rules pass their tests on 4 seeds plus an adversarial case. The owner picks the default from the pictures below.

## What changed (commit: see `git log -1`; generator code behind the flag, OFF = every byte as before)
- `TerrainSpec.stampFitOn` / `StampFit` (v4 only); `-stampfit 1` (`TerrainGenChunkSource.StampFitArg`, FarField too); `ColumnSample.stampRot` (fits in padding).
- `Stamps.cs` (header "TASK 185"):
  - **Cave mouth** `FitCave`: stations every 5 m along the tube (axis and both sides). At each, the lowest the column top can be (map - 94.3 x rough - 1, minus the face-relief bound on a face) must stand 2 m over the tube's top, from <= 40 m out to the tube's end. Tries the hashed axis, then its 3 quarter turns; none fits = no cave. `Carve3D` turns the axis by `stampRot`.
  - **Geyser basin** `GeyserTerrace`: 1.5 m sinter terraces = the map ground rounded up below the vents. The outline follows the ground (at most 2 m over / 40 m under the vents, reach 90-120 m, longer downhill, +-12 % wobble). A pool keeps only columns whose 8 neighbours, and the 8 at 0.6 m, share its terrace.
  - **Hoops** only on a rise (>= 5 m over the +-120 m ring mean) or in rugged rock (map rough >= 0.8).
  - **Sea keep-out** `NearSea`: no cave / fissure / skull / arch / hoop / rib opening within 2.5 cells of a sea cell.
- `ShapeField.Ledge`: shelf top = a rock-layer boundary (`LedgeFitTop`), thickness 3.5 m. `DETAIL_AMP_SUM`.
- Survey cave pose uses the fitted axis. Tests: `StampFitTests.cs` (new), `FluidAtRestTests.Sea_AtRest_StampFit_Seed2026` (`RunTiles` now internal). TASKS.md paragraph; KNOWN_ISSUES seed-2026 entry marked "fixed behind -stampfit 1".

## Evidence (tests; reports in `Logs/TerrainGen/stampfit185/`)
- **Caves** (`caves.txt`): 62 land caves on 4 seeds; 58 kept, 13 of them turned; 4 dropped.
  - OFF has 7 trenches (opening reach 47-81 m). ON, every kept tube opens within 0-24 m of its site (limit 40 m + r).
  - Adversarial: a cave forced downhill on a 0.44 slope opens 60 m OFF; the fit turns it, 17 m.
- **Ledges** (`ledges.txt`): ON 134 ledge columns on a 25 m grid (OFF 313; fewer, because a layer boundary must fall inside the cliff). Every top is on a boundary and 35 voxels thick.
  - Contour-ring steps between neighbouring shelf columns: ON 0 of 257 pairs; OFF 406.
  - Adversarial: the 64x64 window at the tallest shelf (5.8 m, seed 31337): 3,851 columns, 0 ring steps.
- **Geyser** (`geyser.txt`): pools at rest (0 unsettled, FluidAtRest tile job) on 4 seeds, and with the basin moved onto a 56-degree slope (27 terraces).
- **Hoops** (`hoops.txt`): 10 kept, 16 dropped; the flattest plain of each seed is rejected.
- **Sea**: seed 2026 tile (190,545) OFF is NOT at rest (negative control); ON, it and every 6th coast cell are at rest.
- **Suites**: fast PASS 968 FAIL 0 SKIP 19 (2m38s, `Logs/overnight/185/final_fast.keep`). all PASS 1033 FAIL 0 SKIP 25, 23m09s (`Logs/overnight/185/final_all.out`).
- **Rough cost**, from the ledge test's own sampling (managed S, 230k grid columns, 4 seeds): ON 77.4 vs OFF 77.5 us/column, no visible change.
  - A column in a cave's footprint pays up to 4 fits x ~57 map samples. That is a small share of columns, not timed separately (no timing arm).

## Looked at (full-size 1920x1080, seed 42, `-stampfit 1` vs `0`; folders under ~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/)
- The ledge, geyser and cave pictures are clearly better ON. The shelves are now flat. The basin steps down the slope. The trench became a cave into the hill.
- Small leftovers ON:
  - The uphill edge of the geyser basin shows a thin comb of soil columns at a distance (the cut between flat sinter and natural detail).
  - The steep side of the basin becomes many narrow 1.5 m steps.
- The 173 `cave_mouth` pose no longer frames that mouth (the generator changed since 173), so it is not used.
- Shells beyond s2 were mostly unfilled (45 s wait cap). The far band draws them, so the poses are unaffected.

## Unfinished / next
- The owner decides the default (`-stampfit 1` on or off) from the pictures.
- The task says "geyser / wetland basin". I read that as the GEYSER basin (plan C7). `pair_desert_wetland-basin` is the ledge picture.
- Hoops: "slab" = the same hoop seen edge-on. Stone rings are left alone.

## For the doc
- Cave trenches (tube open > 40 m + r from its site), 4 seeds: OFF 7 -> ON 0. Caves kept 58 of 62, 13 turned into the hill (`Logs/TerrainGen/stampfit185/caves.txt`).
- Ledge contour-ring steps between neighbouring shelf columns: OFF 406 -> ON 0 (257 pairs). Ledge columns 313 -> 134 (`ledges.txt`).
- Geyser pools not at rest: 0 on 4 seeds and on a 56-degree slope. Terrace levels on seed 42: 1 flat disc -> 11 (`geyser.txt`).
- Hoops on 4 seeds: 26 -> 10 (16 on plains dropped) (`hoops.txt`).
- Seed 2026 sea voxels not at rest at tile (190,545): >0 OFF -> 0 ON (`Logs/TerrainGen/stampfit185_sea_on_2026.txt`).
- Suites: fast PASS 968 FAIL 0 SKIP 19 (2m38s); all PASS 1033 FAIL 0 SKIP 25, 23m09s (184b: 20m46s; the 4 new Slow tests add ~2.4 min against the pending 15-min question) (`Logs/overnight/185/final_all.out`)
- Images (OFF folder / ON folder, same file name):
  - `2026-11-07_143713_high` / `2026-11-07_142755_high`, `ws_fixed_wetland_basin_native.png`: the canyon wall right of the river. OFF has egg-shaped ledges with contour rings. ON has flat shelves on one rock band with a flat ceiling.
  - same folders, `ws_fixed_river_native.png` (ON re-shot with the pool rim in `2026-11-07_150339_high`): the geyser basin, foreground. OFF is a flat white ellipse with round pools. ON has sinter terraces stepping down the slope, with pools held on their terraces.
  - `2026-11-07_151125_high` / `2026-11-07_150339_high`, `ws_fixed_cave42_over_native.png`: seed 42 cave at (5904, 3074) m, centre of the frame. OFF, the tube runs as an open half-pipe down the coast slope. ON, a round mouth goes into the hill.
  - `2026-11-07_143713_high` / `2026-11-07_142755_high`, `ws_fixed_cave_in_native.png` (the plan's `cave_in` pose): OFF is the open trench from inside. ON is black, because the fit dropped that cave and the camera is inside rock.
  - same folders, `ws_fixed_snow_high_plateau_native.png`: right of centre, the stone hoop on the bare plain is there OFF and gone ON.
