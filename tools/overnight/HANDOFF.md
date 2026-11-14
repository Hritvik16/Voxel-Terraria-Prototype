# 196-look-fluid-pocket-natural-basins: DONE. Pocket walls are now natural basins: the shore sits flush with the fluid and a bank of at most 30° slopes down to it. `-fluidpockets` stays OFF; the owner decides whether it goes ON.
## Why the walls were sheer (task 183's carve)
`FluidPockets.BuildPlane` set the fluid level L one voxel under the LOWEST of 24 rim samples, which can spread up to FLAT_SPAN (2.4 m).
`Apply` then cut every footprint column straight down to L. So on a gentle slope the high side became a vertical wall up to ~2.5 m tall
(191's lava). The rim was only ever raised (`max(h, L+1)`), never graded.
## What changed (commit 0c52227)
- `Assets/TerrainGen/FluidPockets.cs` (header "TASK 196"). Same rule for every fluid and biome. All of it comes from data: none is tuned
  to a place.
  - **Rim** (the 8-neighbours of the footprint): flush at L + 1, lined as before. **Bank** (ring t, the smallest nesting ellipse rx+t, rz+t): moves the ground toward L + 1 by at most UP − drop(t) (cut, uphill) or
    DN − drop(t) (fill, downhill levee). drop(t) = (t−1)·147 >> 8.
  - **Slope**: BANK_Q8 = 147 = tan(`Cover.STEEP_DEG` 30°)·256, rounded down. This is the existing steep rule: soil stays on it and a
    walker can scramble out.
  - **UP / DN**: the EXACT excess of every base-terrain rim column over / under L + 1 (`MeasureBank`). So the rim lands exactly on L + 1,
    and the bank tapers to 0 at `BankBand`, with no step anywhere.
  - **Bowl depth**: `BowlDepth` = min(D, 1 + rmin·147/512), so the wall under the fluid is no steeper than the bank.
  - **Caps / storage**: the band stays inside the plane cell and the record cells the planner checked (dry, no route / town / start
    zone; 0 sites capped on 4 seeds). UP / DN live in bits 24-31 of plane words [1] / [2]; 0 / 0 is exactly 183's shape.
- Placement is unchanged (same 131 seed-42 sites, same L). Nothing moves with `-fluidpockets 0`: that path never reaches `Apply`.
- New flag `-pocketbasin 0` = 183's exact shape (before / after pictures only). `TerrainGenChunkSource.Pockets.cs`: plane log line + basins.
- Tests (`FluidPocketTests`):
  - New fast test `Bank_Rules`: pins the slope to the steep rule; checks the rim lands on L + 1, the taper changes ≤ 1 voxel per ring
    and reaches 0, plus `Ring` and `BowlDepth`.
  - `FourSeeds_Plane_Table_KeepOuts`: every uncapped rim must be flush.
  - The adversarial test adds a FORCED site on a ~20° slope, widens every box to the bank, checks the rim on every case, and fails if any
    4-neighbour step outside the footprint grows by > 2 voxels ON vs OFF. Tree voxels moved by the bank are counted, not failed (trees
    root on the final ground, `Trees.cs:211`).
## Evidence
- Whole suite **PASS 1078 FAIL 0 SKIP 28** (`Logs/overnight/196/all.out`; 1077 + `Bank_Rules`). Filter runs: `filter1.out` (tree voxels), `filter2.out` PASS.
- 4 seeds (`Logs/TerrainGen/pockets183/table.txt`): every site banked, 0 capped, max cut 23-26 / fill 1-3 voxels. Every rim column is
  flush at L + 1 (0 exceptions); before, rims stood up to ~25 voxels over the fluid.
- Seed 42 cases (`Logs/TerrainGen/pockets183/sites.txt`), all PASS. Still at rest (150-tick settle), ON twice gives the same bytes, and
  FarQuery, shells 0-2 and the far band all agree.
  - Wall over the fluid: 1 voxel on every planned site; largest step growth ON vs OFF: 1 voxel.
  - Adversarial: forced slope (UP 24) flush, PASS; forced canyon (DN 79, capped) rim flush, PASS; forced cliff edge (capped): the
    downhill rim still holds, as a 6.6 m berm (the planner never puts a site there).
- Shots: one build, `Logs/overnight/196_shots.sh`, seed 42, `-tod 12`, `-pocketbasin 0` vs default. The water pose put the camera inside
  a tree crown, so it was retaken alone (`196_water_retake.sh`). Shells waited a fixed 40 s; s3+ were mostly missing (distance only,
  not the pockets).
- Rough cost: plane build 23-27 ms → 70 ms in the player (once per world, pockets ON only). No timing arms.
## Gameplay consequences (owner should know)
- 111 of 131 bowls are shallower. Example: the largest water pool goes from 2.0 m to 1.4 m deep (radius 4.7 m). Quicksand mostly keeps its depth.
- Each pocket now reshapes ground up to ~4.5 m beyond its rim (the bank). Trees standing there sink with the ground.
## Next
- Owner decides whether `-fluidpockets` goes ON. No follow-up added. `Screenshots/196/lookreview_*.txt` are the water retake's (the
  other 3 poses' figures: `Logs/overnight/196/shots.out`).
## For the doc
- Wall over the fluid at the rim: up to ~25 voxels (2.5 m sheer) → **1 voxel** on every planned site (4 seeds, 0 capped;
  `Logs/TerrainGen/pockets183/table.txt`, `sites.txt`).
- Bank slope ≤ 30° (`Cover.STEEP_DEG`). Bank width = the measured excess / slope: up to 26 voxels cut → ~45 rings (4.5 m). Largest
  neighbour-step growth 1 voxel.
- Bowl depth: 111 / 131 seed-42 bowls are shallower (largest water 2.0 m → 1.4 m).
- Whole suite PASS 1077 → **1078**, FAIL 0. Plane build 27 → 70 ms (player).
- Images (open `Screenshots/196/viewer.html`: Space flips before / after, arrows change pose, Z = full size):
  - `Screenshots/196/surface_quicksand_before.png` / `_after.png`: look at the far half of the pit. Before: a vertical orange-banded wall.
    After: a sloped sand crater, like a sinkhole.
  - `Screenshots/196/surface_lava_before.png` / `_after.png` (191's pose): look at the far rim. Before: a grey sheer wall ~1-2 m tall.
    After: a terraced snow bank stepping down to a thin dark crust shore one voxel over the lava.
  - `Screenshots/183/cut_surface_lava.png` (after) vs `Screenshots/196/before_cutaway/cut_surface_lava.png` (before): CPU side slices,
    right panel = ON. Before: vertical sides. After: the right (uphill) side is a graded bank.
