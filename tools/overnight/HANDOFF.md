# 55-engine-5b-fluid-pool-size: DONE. The Playground/tg2/play fluid tile pool went from 1,024 to 480 tiles. Fluid GPU memory in the ledger fell from 548.8 to 276.8 MB, with 0 refusals at the coast and lake stops.

## What changed (commit 7451b8c, branch engine-phase)
- `EngineConfig.PLAYGROUND_FLUID_TILE_POOL_CAPACITY = 480`, next to `FLUID_TILE_POOL_CAPACITY` (1,024, unchanged).
  - 480 x 0.5 MiB = 240 MiB of cells. That is the largest whole-tile count under plan §2's ~250 MB.
  - There is no alignment rule. FluidTileMap and FluidGpuSimulation take any count; only regionDims must be pow2, and DispatchGrid folds rows past 60,000 groups.
- `Playground.cs`: the `_tilePoolCap` default is now the new const.
- `Phase5aSceneBuilder`: the Playground builder now stamps the new cap. This was the scene-builder landmine: it never set the field before.
  - The brush-guard scene is also a Playground component. Its builder now pins `FLUID_TILE_POOL_CAPACITY` (1,024) explicitly.
- `Playground.unity`: `_tilePoolCap: 1024 -> 480`. Only that one line is committed. The working tree also has an older, uncommitted regeneration diff of this scene (fileID churn); I left it alone.
- All other fluid rig scenes and classes are untouched and keep 1,024 or their serialized 512.
- New test `FluidAwakeOnlyTilesTests.MoreAwakeTilesThanThePlaygroundCap_NearestWin_RefusalsCounted`. It asks for 640 remembered tiles against the real 480 cap:
  - Pass 1, near end: 480 resident, 160 refusals counted, the nearest kept, the refused tiles still remembered.
  - Pass 2, far end: 160 displacements, 0 refusals, the nearest kept again.
  - It also asserts that the cells fit in 250 MiB.
- Updated KNOWN_ISSUES (task 55 note under task 54's entry) and the CLAUDE.md index line.

## Evidence (memory/behaviour only, no timing; rule 10)
- Fast suite: PASS 641 FAIL 0 SKIP 14. The new test passed, and the log has 0 `error CS` (`Logs/overnight/55_fast.out`).
- `run-fluid-tiled.sh`: PASS 19 FAIL 0 at its unchanged cap. It printed "551 <= 1024" (`Logs/overnight/55_fluidtiled.out`).
- Poolprobe. The build ran with BUILD=1 (build OK, 0 error CS). It ran twice: once without the ledger (`55_pool.out`), then with `EXTRA=-memledger` (`55_pool_ledger.out`, the main one). Both report "pool: 480 tiles of 32^3, GPU 248 MiB" (240 MiB cells plus the 8 MiB ring directory).

| | task 54 (1,024 tiles) | task 55 (480 tiles) |
|---|---|---|
| ledger: Fluid simulation, GPU MB | 548.8 | 276.8 |
| ledger: SUM of owners | 1,920.4 MB | 1,651.6 MB |
| pool-peaks row: fluid tiles cap MB | 512.0 | 240.0 |
| phys_footprint peak (whole probe run) | 2,870 MB | 2,472 MB (2,556 in the run without the ledger) |
| refusals, whole run (coast + lake) | 0 | 0 |
| pool peak | 32 | 32 (30 at the lake) |
| box water after 480 frames, coast | 1029 -> 3 | 1029 -> 12 (9 in the other run) |
| box water after 480 frames, lake | 1029 -> 1 | 1029 -> 8 (11 in the other run) |

- The pool never got near 480, so the cap does not affect behaviour here. The 1-12 voxel spread in leftover box water is between runs of the same policy; it is not a cap effect.
- The phys_footprint drop is about the size of the ledger drop, but it is a single run each. Treat it as direction only.

## Screenshot looked at (`Screenshots/55/`, copied from the probe's shot folder, untracked)
- `Screenshots/55/lake_after_2s.png`: midday, sun behind the camera, the same scene as 54. The placed box is falling as a dark column into the lake, left of centre. The vent's water spreads as blocks over the right half of the lake and the sand. Placed water still flows at the 480 cap.

## Not done / follow-ups (none queued)
- Headroom risk, for the owner to watch: under the mask policy, fluid-tiled's 220-pocket scatter peaks at 551 tiles.
  - An awake-only blast that wide would hit 480.
  - It is handled (nearest tiles win, refusals counted), but far pockets would visibly hang until there is room.
  - No awake-only wide-scatter scenario exists in Playground yet. That would be the evidence if it matters.
- Still open from 54: stepped generated sea/lake surfaces, and the 488-tick quiet release.

## For the doc
- Fluid tile pool (Playground/tg2/play): 1,024 -> 480 tiles; cells 512 -> 240 MiB GPU (`Logs/overnight/54_pool.out` vs `Logs/overnight/55_pool_ledger.out`).
- Ledger "Fluid simulation" GPU: 548.8 -> 276.8 MB. Ledger sum: 1,920.4 -> 1,651.6 MB.
- phys_footprint peak, coast poolprobe run: 2,870 -> 2,472 MB (single runs; direction only).
- Refusals at the coast and lake stops: 0 -> 0 (pool peak 32 of 480). Every fluid rig keeps 1,024; fluid-tiled still PASS 19.
- Image: `Screenshots/55/lake_after_2s.png`. Midday at the lake: the placed box falls as a column into the lake (left of centre) and the vent's water spreads over the right half. Placed water still flows with the smaller pool.
