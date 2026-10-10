# 37e-lake-flat-water: DONE. Every v4 lake now sits still at one level: 0 unsettled lake cells on seeds 42/7/2026/31337 (e4b6a02); SUITE=all PASS 658 FAIL 0 SKIP 17.
**OWNER, PLEASE LOOK (not a blocker):** the seed 42 lake now has a **straight-edged shore** with a flat sand beach (picture below). It looks man-made. See "Left open".

This was the third attempt. The 16:00 attempt wrote the fix and the new test, then hit the usage limit with one gate red. This session fixed that gate and finished the task.

## The change (map v4 only; v3 is byte-identical and its hash tests pass)
- **I raised the rim; I did not lower the level.** The map's lake level is right (it is the spill level). What was wrong was the ground around the lake: dry columns next to a lake dipped below its level.
  - Why not lower the level: that needs every shore column's voxel ground when the map is built, which is not local.
- `ShapeField.LakeRim`:
  - Any dry column within 1.5 map cells of a lake cell is raised to that lake's level. Beyond that it falls away at 27°.
  - Inside the lake, a 4 m **shore shelf** raises the bed toward the level near the water's edge. Without it the raised rim stood as a vertical bank beside the bed.
- **This session's fix (seed 7's red route gate):** the shelf counted a river inlet as dry shore. It stood at the lake level right beside the river bed, a 1.5 m wall at (2996, 2412) m on route 2-3.
  - Now a square with a lake corner, or with water at or above this lake's level, counts as wet.
- `Stamps`: no cave mouth, fissure or skull cave is carved within 2.5 cells of a lake. The basin flatten ring keeps the rim.
- `DetailField`: a stamp opening below still water holds water instead of air. Caves are carved by depth below the ground as it was before the rim raised it.
- `MapView.waterClass` is passed through by `WorldMap`, `FarField` and `TerrainGenChunkSource`.
- Covered: lakes, and the geyser pools (they were already at rest). Not covered: rivers (they are not at rest by design) and the volcano's lava lake (not checked).

## Evidence
- **New test `LakeLevelTests.LakeWater_IsAtRest(seed)`**, one seed per case. It counts lake water voxels with air beside, directly below, or diagonally below them.
  - Unity peaked at about 1.9 GB (`Logs/overnight/37e/after4.mem`). The memory watchdog killed nothing.

| seed | unsettled before | unsettled after | lake water area before -> after |
|---|---|---|---|
| 42 | 666,196 | **0** | 27.30 -> 27.14 ha |
| 7 | 885,872 | **0** | 29.39 -> 29.20 ha |
| 2026 | 1,039,211 | **0** | 44.11 -> 43.87 ha |
| 31337 | 607,546 | **0** | 17.54 -> 17.41 ha |

  - Map lakes are unchanged: 90/86/84/50 lakes, 41.1/40.0/76.4/59.6 ha of lake cells. The map itself is not touched.
  - Geyser pools: 0 unsettled before and after.
  - Logs: `Logs/overnight/37e/before4.log`, `fix5.log`.
- **Route gate:** B8 `V4_FourSeeds_RoutesJoinTheEasyAreas` passes on all 4 seeds with 0.0 m Sheer. Seed 7 was 0.5 m before this session's fix (`fix5.log`).
- **`SUITE=all`: PASS 658 FAIL 0 SKIP 17** (`Logs/overnight/37e/suite_all3.out`). That is the 654 from before plus the 4 new cases. It covers B4/B5/B6/B8/B10, v3 hashes, and v4 hash stability and seed-dependence.
- **Pool probe** (`MODE=poolprobe MAPV=4`, log `Logs/tg2world_*` from 19:21; report `Screenshots/37e/new_poolprobe.txt`). Not a gate.
  - Lake stop, water that could start flowing: 89,590 cells (86,596 with air diagonally below) -> **21**. Those 21 are not identified; the probe also counts river water.
  - Slot high-water on arrival: 34,403 -> 87.
  - Slot high-water after placing water at the lake: 682,258 -> 116,186.
  - The probe's start and coast poses moved since task 39 (the map changed in 37b/37d), so its coast numbers are not comparable.
- No performance runs (rule 10).

## Pictures (`Screenshots/37e/`)
- `lake_before_after.png`: a 2x2 sheet. Top row is before (task 39's shots, 00:40), bottom row is after. Left column has no edit; right column is 2 s after placing water.
  - **Top left:** the near half of the lake is a speckled staircase of water steps. That is the unsettled surface.
  - **Bottom left:** one flat blue sheet with a clean shore. **But the near shore is a dead-straight east-west line and the right shore a straight diagonal, with a flat sand beach at lake level.**
  - **Bottom right:** the placed water falls onto the beach and spreads. The lake surface around it stays flat, and no holes open in it.
  - The grass in the top row turning to sand in the bottom row is the later biome work (B10/37b), not this task.
- `new_lake_after_8s.png` and `old_lake_after_2s.png` are the single frames.
- I did not take a separate "no edit for a few seconds" shot. The 87-slot high-water on arrival says the lake does not wake by itself.

## Left open / next
- **Straight shores (owner's call).** The shelf ends exactly at the edge of the 2x2 map squares that touch a lake, so the shoreline follows the 8 m map grid instead of the terrain. A fix would make the shelf (or the wet area) follow the ground: for example, only raise the bed where the dry rim needs it, or blend the edge with noise.
  - That is new terrain-design work, so I did not queue it.
- The volcano's lava lake was not checked for the same defect.
- The 21 water cells at the lake stop could be counted by water kind (lake vs river) in a later probe.
- Next in the queue: 41 (lava-look shots).
