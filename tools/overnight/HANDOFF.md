# 178b-worldgen-relief-skips-routes: DONE -- cliff relief and the scree apron skip route cells; seed 42's route test passes again; whole suite red only on the known 31337 ford

## What changed (commit 14b0870)
- **Route flag exposed to the generator:**
  - `MapView.route` (new and OPTIONAL; null means no skip) = `WorldMap.RouteMask`, 1 per map cell that the world record flags `FLAG_ROUTE`.
  - It is derived and pinned in the new `WorldMap.SetRecord`, called from MapJob's FromCore.
  - It is driven by the record alone, with no seed or place constant.
- **Plumbed beside `flow` as a `routeMask` NativeArray (length 1 = none) into every job that builds a MapView:**
  - chunk `ColumnJob`, `ShellColumnJob`, `ShellFillJob`
  - `FarQuery` (via `PointRoute`) and FarField's `FillTg2Job`
  - the `ShellChunkTests` and `ColumnPassSplitTests` jobs
  - So LOD 0, the shells and the far band agree.
- **`ShapeField.FaceRelief`:**
  - It returns the un-relieved height on a route cell, so fluting, ledges and scree are all skipped.
  - Off the route, `RouteWeight` fades the relief back in over `ROUTE_BLEND_CELLS` = 2 map cells (16 m): a smoothstep of the distance to the nearest route cell's square.
  - The scree cover is kept only where (1 - w) x fill >= SCREE_MIN.
  - The blend is convex, so the band-limit bound is unchanged. The scree's worst added grade in the blend is about 1.5 x 10 m / 16 m = 0.94.
- **The cliff LOOK off-route is bit-identical to 178.** Cliffs-off and v3 are untouched.
- **New test** `CliffTests.Relief_SkipsRouteCells_UnchangedBeyondTheBlend`.
- **Docs:** TASKS 178b paragraph; KNOWN_ISSUES route line and the CLAUDE.md index line rewritten.

## Evidence
- **Whole suite: PASS 1065 FAIL 1 SKIP 33** (`Logs/overnight/178b_all.out`, XML `Logs/overnight/178b_all_TestResults.xml`, 43 min). ShellFill GOLDEN did not move.
- The only red is the known ford. Exact message: `seed 31337: 1.5 m of Sheer on the voxel ground`.
- Route numbers (`Logs/overnight/178b_b8_routes.txt`):
  - seed 42: PASS, Sheer 3.4 -> **0.0 m**, Walk+Scramble 97.7 %, in sight 85.3 %
  - seeds 7 and 2026: PASS
  - seed 31337: 1.5 m, the ford
- **"In sight 61.9 % < 65 %" is no longer there.**
  - Sight reads only the map (`RouteTests.Sight`: `Surf(map)` and `Features.LineOfSight(map)`), so neither 178 nor 178b can move it.
  - 176d's biome Size 2 moved seed 42's routes (biome -> danger -> routes; the map hash re-pin). On the new routes sight is 85.3 %. 177b's run already showed 85.3 % and the same message without the sight line.
  - Nothing to fix.
- **New test** (filtered run `Logs/overnight/178b_filter1.out`; numbers in `Logs/TerrainGen/c4/routes178b.txt`), seed 42:
  - 12,834 route-cell columns all equal cliffs off; 119 of them had relief under 178.
  - 5,768 land columns beyond the blend all equal 178's column (the same view without routes), height and cover. This is the "-cliffs 0 vs on unchanged off-route" proof: the off-route relief is exactly 178's.
  - 87 columns lie in the blend, and each stays between cliffs-off and 178.
- **Probes:**
  - (3777, 5278) m [176d]: on a route cell. The 1 m band at the point goes Climb -> **Walk**. The worst band over the 81 route points of the 9x9 m grid goes Sheer -> **Scramble**.
  - (4471, 2390) m [178]: since 176d's Size 2 it is **no longer a route cell** (route weight 0). It is Scramble with cliffs on, off and 178 alike, so there is nothing to fix there. The route that crossed it before Size 2 has moved.
- No timing arms. The rough cost is one 5x5 byte read per land column, and only with cliffs on.

## Unfinished / notes
- No screenshots: the task changes only route corridors and the 16 m beside them, and asks for the cliff look to stay as it is. The proof is numeric.
- In the blend, the scree can still add a steep edge just outside the corridor, up to ~43 deg extra grade. It is off-route, so the route test cannot see it.
- Next: the queue continues with 178c (cliff look rework).

## For the doc
- Seed 42 route Sheer on the voxel ground: **3.4 m -> 0.0 m**. Seed 42 now passes: Walk+Scramble 97.7 %, in sight 85.3 % (`Logs/overnight/178b_b8_routes.txt`).
- Probe (3777, 5278) m, worst 1 m band on the route around it: **Sheer -> Scramble** (`Logs/TerrainGen/c4/routes178b.txt`).
- Route-cell columns that 178's relief had moved: **119 -> 0** of 12,834 sampled. Off-route columns changed vs 178: **0 of 5,768**.
- Whole suite: **PASS 1065 FAIL 1**. The 1 is seed 31337's ford, until C5 (`Logs/overnight/178b_all.out`).
- Images: none. The change is invisible off the route corridors and the cliff look is unchanged, so the evidence is the numbers above.
