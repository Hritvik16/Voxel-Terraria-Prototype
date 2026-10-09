# 36-B8-routes: DONE. Map v4 links its easy areas by routes (a spanning tree plus extra links, 2+ per area) and eases a 60 m corridor one danger tier lower. All acceptance gates pass on seeds 42/7/2026/31337, and the 20-seed sweep is 20/20. Commits 7b1ba3b (code) and a1a1b38 (evidence).

## What changed
- **New `Assets/TerrainGen/Routes.cs` (plan rule 9).**
  - **Search:** one multi-source Dijkstra per pair of easy areas, on the 8 m map. Cost per cell = band (W1/S2/C6/Sh30) x (1 + danger), x1.5 where neither end area is in sight.
  - **Things the search avoids:**
    - S-level stamps (arches, rings, crystal clusters, fossils, cave mouths, fissures) cost Sheer, and so do the geyser pools. They are voxel-only steps that the map cannot see.
    - Lakes cost Sheer; a river crossing costs 4x (a ford); a feature footprint costs 3x.
  - **Links:** Kruskal's spanning tree, then extra links priced up by how much they overlap a route already taken. The centre line is smoothed.
  - **Easing:**
    - Along the line, slopes are held under tan 0.4. The 20 m cut cap is read on the line AND on the cells beside it, and the foot of a capped step gets a scree fill of at most 25 m.
    - Sideways, a flared cone. It is built on the uncapped profile; the capped profile made neighbouring cones contradict each other.
    - Detail roughness is eased too.
  - **Never touched:** easy areas plus a one-cell collar round them, features, water, and the noise wobble of the corridor's edge (the core does not wobble).
- **`WorldMap.BuildV4`:** builds the record, then the routes, then the record again on the eased map, keeping the FIRST record's easy areas. Reseeding them on the eased map moved them (seed 12: 44 x4 ha became 66/9/39/37).
- **`WorldRecord`:** new `FLAG_ROUTE` (flags bit 2); a corridor cell's base danger is 1 tier lower; new `RouteList` (line, width, ends, passes, cut/fill, corridor cells, `mapVersion`).
- `DangerTests` and `WorldRecordTests` now allow the route flag and the danger drop.
- **New `RouteTests`:** the 4-seed gate (Slow), plus `ROUTESWEEP=1` for the 20-seed sweep. Its report is `Logs/TerrainGen/routes/b8_routes.txt`; a copy, with the PNGs, is in `Screenshots/B8/`.
- `FlySweep` now accepts `-tod`.
- v3 is untouched: the code runs on the v4 path only.

## Evidence
- **Tests:** `SUITE=all` PASS 653 FAIL 0 SKIP 17 (`Logs/overnight/b8/suite_all.out`); fast suite after the evidence commit PASS 631 FAIL 0 SKIP 14.
  - `V4_Deterministic_SeedDependent_DiffersFromV3`, `MapV3_IsDeterministic` and `NoNeedles_OnTheMap` all pass.
- **Per seed** (`b8_routes.txt`; Walk+Scramble is on the voxel ground, sampled every 0.5 m along the centre line):

  | seed | links / area | Walk+Scramble | Sheer | detour max | in sight | safer (floored / strict) | deepest cut | build |
  |---|---|---|---|---|---|---|---|---|
  | 42 | 2 2 2 2 | 92.0% | 0 m | 1.23x | 72.8% | 99.0 / 86.5% | 20.0 m | 26.5 s |
  | 7 | 3 2 2 3 | 96.4% | 0 m | 1.16x | 74.5% | 100 / 81.3% | 0 m | 28.8 s |
  | 2026 | 2 3 3 2 | 99.0% | 0 m | 1.08x | 86.2% | 100 / 96.1% | 0 m | 26.3 s |
  | 31337 | 3 2 3 2 | 95.8% | 0 m | 1.05x | 71.4% | 98.2 / **50.6%** | 0 m | 24.3 s |

- **Climb/Sheer crossings (report only):** seed 42 has 13, and 12 of them have a second crossing within 500 m; 2026 has 3 of 3.
- **20-seed sweep:** graph connected on 20/20, every area has 2+ links, no B4 per-cell failures (`Screenshots/B8/routesweep.txt`).
- **How the Sheer reached 0 m on seed 42:** 6.6 m -> 5.6 m (keeping out of the geyser pools; their rims are 2.5 m steps) -> 0 m (the cut cap read on the cells beside the line, and the cones on the uncapped profile).

## Pictures (`Screenshots/B8/`)
- `routes_seed42.png` (likewise 7, 2026, 31337):
  - Black lines are the routes, with the cyan corridor under them; green discs are the easy areas, magenta shapes the features, red squares the passes, white squares the route ends.
  - On seed 42 the routes curve round the big central feature along the valleys. No straight segments.
- `b8r12cut.png` (route 1-2's 20 m cut, seen from 80 m back at 11:00): a smooth grassy saddle rising ahead. There are no trench walls and no ruled edge. **Natural, not constructed.**
- `route_1-2_mid_seed42_dusk.png` (the same spot, looking along the route at 17:35): a green valley running down to the sea with a stream; layered red cliffs on the left.
- `route_0-3_mid_seed42.png`: open grass rising to a saddle between a rocky peak (left) and a rounded hill. The next waypoint is in sight.
- `b8r02mid.png` (route 0-2's middle): a snowy valley with a lake, green and red cliffs ahead.
- `b8r02cut.png` (route 0-2's deepest cut, from 80 m back): a steep grey stony slope fills the top two-thirds of the frame, with grass on the right. **This is the weak spot:** it does not look built, but it is a hard climb.
- `poses_v4_contact.png` (MODE=poses on v4; rows: map/oblique/eye_level_3, ledge/geyser/eye_level_2, cliff_foot_1/coast/peak):
  - No holes and no floating geometry.
  - Only the coast pose is mostly sea, as it is designed to be; the dark arc at its bottom is the known LOD tier seam.
- `ledge_view_before_after.png` (left: before B8, 8 Oct; right: now): identical. The black oval is the ledge's dark underside and was there before B8. It is already queued as task 40.

## For the owner (no stop)
1. **The "safer" gate is read FLOORED.** A danger-0 corridor beside danger-0 land counts, because it cannot go lower. Read STRICT, seed 31337 has only 50.6% (most of its routes run through danger-0 land). Keep the floored reading?
2. **B4's opt-in sweep was ALREADY red at HEAD before B8**, on three seeds (11: 24.8 ha; 12: 8.7 ha; 65537: 3 areas, want 4). These are list properties of the first record, which B8 does not change. B4's 20/20 sweep dates from before B5, which probably caused this. Worth a fix task.
3. **The scree fill is capped at 25 m.** That number is mine; the plan caps only the lowering (20 m).
4. **Only 4 easy areas per world**, so the graphs are small. Some links are 8 m long because two areas touch (seed 42's 1-3).

## Next
Task 37-B10-savanna-badlands (37b and 37c require it).
