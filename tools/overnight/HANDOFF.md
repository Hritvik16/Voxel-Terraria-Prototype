# 187-worldgen-b7-site-planner: DONE. The town / village SITE PLANNER exists behind `-sites 1`. Default is OFF; it writes records only and changes no terrain.

## What changed (commit 0f5886a)
- Resumed from the first run's patch, which applied cleanly, plus its untracked `Sites.cs` / `SiteTests.cs`. Its last test run (t5) was green.
- `Assets/TerrainGen/Sites.cs` (new): the B7 planner. Integer rules on the map + record; no seed or place constants; deterministic.
  - TOWN: one per easy area. Disc R 64 m, range <= 8 m.
  - FRONTIER: on hard ground (Volcanic landform or Snow biome), within 256 m of its edge.
  - VILLAGE: fresh water <= ~100 m, danger <= 3, within 2 km of a route.
  - OUTPOST: what a village can't be (more danger, or water up to ~260 m).
  - Keep-outs: sea, lakes/rivers, start zone (128 m round both start columns), features, stamp discs, other sites.
  - Counts scale with land area (~3 towns, ~10 villages + outposts on the medium island).
- NEW THIS RUN, the STEP RULE in the ground check: no 2 m neighbour step inside a disc may be steeper than Scramble (30 deg; `Sites.StepTooSteep`, integer 3d^2 > 20^2).
  - Why: the first run put villages on canyon terraces. The map range read 0.0 m, but the real ground had a 6 m wall at 50-52 deg (seed 42 #25, seed 7 #22, seed 2026 #22).
  - The check stops at the first steep step, so the plan stays at ~1 s.
- Output: `WorldRecord.SiteList` (`Site` struct) with:
  - id >= 16, kind, integer centre, radius, flat box, biome x landform pair
  - nearest route + join point + spur length
  - the stored flatten rule: pad Y = median map height, blend R/2, cut/fill
  - The record's site byte is marked on each disc. `RouteList` is unchanged and the record is never saved, so there is no save-format change and no frozen API is touched.
- Flag plumbing: `TerrainSpec.sitesOn`/`Sites`, `TerrainGenChunkSource.SitesArg` (I fixed the misplaced doc comment from the patch), `FarField`, and `WorldMap.EnsureSites` (logs `[Sites] plan ..` plus one line per site).
- Tests in `SiteTests`:
  - Fast: `Adversarial_FewFlatCells_OnlyTheFlatPatches`, a 4 km Climb island with 4 flat patches. Negative controls: no flat ground, and the village patch on Scramble.
  - Fast, NEW: `GroundCheck_StepRule_WallInDisc_NoVillage`, a 3 m wall the map cannot see.
  - Slow: `FourSeeds_EveryRule_CountsTable`, an independent checker of every rule.
  - Fixed the test map PNG, which was drawn north-DOWN (Texture2D row 0 = bottom).
- `tools/sites187.py` labels the maps. TASKS.md has the Task 187 paragraph.

## Evidence
- Whole suite (superset of fast): **PASS 1036 FAIL 0 SKIP 25, 24m04s** (`Logs/overnight/187/all1.out`). That is 1033 + the 3 site tests.
- Counts (`Screenshots/187/table.txt`), town/frontier/village/outpost:
  - seed 42: 3/1/6/4
  - seed 7: 3/1/3/7
  - seed 2026: 3/1/4/6
  - seed 31337: 3/1/2/8
  - Every seed met its targets of 3 towns, 1 frontier and 10 settlements.
  - Mean centre water distance: 78-129 m. Distinct biome x landform pairs: 8-10 per seed.
  - Steepest 1 m slope inside any site: <= 39 deg (was 50-52 deg before the step rule). The ground range is at most 9.1 m, on a 64 m town.
- Adversarial island: exactly 1 site per patch, 2 settlements against a target of 6 (`Screenshots/187/adversarial.txt`).
- Player (`-sites 1`, seed 42, `Logs/overnight/187/player_sites.log`): `[Sites] plan 235 ms: 14 sites`. These are the same 14 centres as the test's seed 42 plan.
- Shots: one launch, 3 poses, High, 45 s shell wait. Shells s3+ were mostly unfilled and the far band covers them (`Screenshots/187/lookreview.txt`).

## Unfinished / next
- Frontier choice: 2 of 4 seeds put the frontier on a Snow-biome plain (snow x river plains / dune sea), not at the volcano's foot. Both count as "hard ground" by the rule. Worth a look by the owner; no change made.
- Sites cluster around the easy areas because the routes only join those areas. Routes don't yet re-route to villages; each site only records its spur.
- Not done: the stored flatten rule is not applied.
- **Next step: place BuildingKit on the sites behind `-towns 1`** (flatten the pad inside the site disc, then the town kit). Not queued: it needs the owner's go.
- `tools/overnight/blocked/187-*.patch` is now applied and committed and can be deleted.

## For the doc
- Sites per seed, town/frontier/village/outpost: none (no planner) -> 42: 3/1/6/4, 7: 3/1/3/7, 2026: 3/1/4/6, 31337: 3/1/2/8 (`Screenshots/187/table.txt`).
- Steepest 1 m ground slope inside a site: 52 deg (first run, canyon-terrace villages) -> 39 deg (step rule, 408 terrace discs rejected on seed 42).
- Plan cost: 235 ms once at world load with `-sites 1` (`Logs/overnight/187/player_sites.log`). With the flag OFF it costs nothing, and terrain is byte-identical either way.
- Whole suite: PASS 1036 FAIL 0 SKIP 25 (`Logs/overnight/187/all1.out`).
- Images:
  - `Screenshots/187/sites_sheet.png`: the 4 seeds, 2x2 (42 top-left, 7 top-right, 2026 bottom-left, 31337 bottom-right). Orange T = towns on the green easy areas; red F = the frontier at the edge of the red-tinted hard ground; green V / white O = villages / outposts along the cyan routes. Full size per seed: `sites_<seed>.png`.
  - `Screenshots/187/eye_town.png`: seed 42 town #16 at eye level. The whole foreground is a broad flat green plateau (forest x high plateau) with mountains behind; only the 0.1 m detail noise breaks it.
  - `Screenshots/187/eye_frontier.png`: frontier #19, a flat snowfield with ice patches and a grey cliff at the right edge (the hard ground it borders). `eye_village.png`: village #20, a flat sand basin.
