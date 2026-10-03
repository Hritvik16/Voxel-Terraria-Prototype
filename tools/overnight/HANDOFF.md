# Overnight handoff

**11-editmode-suite-speedup: DONE.** Test code and runner only; no engine or generator change. Times:
- Fast suite (the new default): **3m02s**.
- `SUITE=all`: **9m23s**.
- Before: **14m50s**.

Both suites: 0 FAIL. `SUITE=all` runs the same 630 cases.

## What changed (commit f1e8dc4 on design-brief)
- **Shared maps.** `Assets/TerrainGen/Tests/SharedMaps.cs` (new) builds one WorldMap per whole `TerrainSpec` per run. Before, seed 42 was built 9 times per run.
  - Cave, DetailCull, Hydrology, PaletteSample, Stamp, TerrainGen, V0DenseBrick and Region tests now use it.
  - It also stores each build's time and `LastErosion`, so the Regions report still prints them.
  - Determinism tests still build twice. `MapV3_IsDeterministic` and `Determinism_SameSeed...` compare a FRESH `WorldMap.Build` with the shared map, which was built earlier in the run. A test that mutated the shared map would fail there.
  - The erosion-input dump in `Regions_VarietyAndReport` now needs `REGIONS_EROSION_DUMP=1`, which builds fresh. Nothing in the repo reads that dump.
- **Opt-in reports.** `ReportOnly.cs` (new): without `REPORTS=1` these tests are Ignored (counted as SKIP). I checked each body and its helpers; none of them asserts anything:
  - `HydrologyTests.Report_Hydrology`
  - `PaletteSampleTests.Report_PaletteSamples` (86 s)
  - `V0DenseBrickTests.Report_V2_MountainBandDenseBricks`
  - `TerrainGenTests.Diagnose_BurstVsMonoIntermediates`
  - `TerrainGenTests.Report_BurstVsEditorMono_ShapeMismatchRate`
  - `TerrainGenTests.Report_ShapeAndDetailCostUnderBurst`
  - `TerrainGenTests.Report_WriteWorldMapPng`
  - `TerrainGenTests.Report_LandmarkInEveryDirection`
- **13 tests tagged `[Category("Slow")]`** (> 10 s after the cache):
  - RegionTests: `Regions_VarietyAndReport`, `MapV3_IsDeterministic`
  - TerrainGenTests: `Determinism_SameSeed...`
  - StampTests: `NoNeedles_OnTheMap`. It builds seed 7 itself when RegionTests is not in the run.
  - VerticalStreamingTests: both tests. Their maps are built inside the engine's `TerrainGenChunkSource`, so they can't share without an engine change.
  - ShadowRayTests: `Fuzz_Penumbra...`
  - OccupancyMaskTests: 3 tests
  - AirMipTests: 2 tests
  - RaymarchMacroSkipTests: `O1_RandomizedFuzz`
- **`run-editmode-tests.sh`.** Runs the fast suite (`-testCategory "!Slow"`) by default, or the whole suite with `SUITE=all`.
  - The summary line is now `PASS n  FAIL n  SKIP n  suite=fast|all  wall=XmYYs`.
  - A `SUITE=all` run also saves a copy as `TestResults_all.xml` (added to .gitignore). The fast suite compares against it and prints "left out N of M cases".
- **CLAUDE.md "Commands"**: fast suite for every change; `SUITE=all` before any merge to main and on every terrain-generator change; the REPORTS / SharedMaps rules. I did not touch PREAMBLE.md or SUPERVISOR.md.

## Evidence (scripts' own output)
- **Before:** `Logs/overnight/10_editmode.out`, 08:11:18 -> 08:26:08 = **14m50s**, PASS 626 FAIL 0 SKIP 4. XML copy: `Logs/overnight/11/before_TestResults.xml`.
- **SUITE=all:** `Logs/overnight/11_all.out`: `PASS 618 FAIL 0 SKIP 12 suite=all wall=9m23s`. XML: `Logs/overnight/11/all_TestResults.xml`.
  - It has the same 630 full names as before: none removed, none added.
  - Only 8 results changed, all Passed -> Skipped, and they are exactly the 8 opt-in reports. SKIP 12 = the old 4 + those 8.
  - Total test-case time went from 660 s to 490 s. `Regions_VarietyAndReport` went from 109 s to 54 s; `NoNeedles` from 27 s to under 1 s.
- **Fast:** `Logs/overnight/11_fast.out`: `PASS 605 FAIL 0 SKIP 12 suite=fast wall=3m02s`, "left out 13 of 630 cases".
  - Test time 114 s. The slowest remaining case is CaveTests.StructuredCaves at 7.3 s.
  - XML: `Logs/overnight/11/fast_TestResults.xml`.
- 0 `error CS`. No screenshots: none were needed for this task.

## Notes / unfinished
- About 1.5 of the fast suite's 3 minutes is Unity start-up and compile; the tests take ~2 min. The fast suite still builds the seed-42 map once (~27 s) because many of its terrain checks need it.
- The supervisor still has to switch PREAMBLE/SUPERVISOR wording ("EditMode suite before committing") to the fast suite, plus `SUITE=all` for TerrainGen changes or merges.
- The CLAUDE.md "Current state" line still quotes an old EditMode count (625/2). I left it alone; the supervisor can update it.

## Next step
Next queued task. No follow-up task added.
