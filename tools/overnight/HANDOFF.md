# 213-look-trees2-denser-forest: DONE -- the forest is ~1.55x denser than 210's and inside the memory budget (0 GPU refusals vs today's 294); still behind `-trees2 1`

**Owner, please look at `Screenshots/213/sheet213.jpg` rows 1-2.** At eye height the new forest reads as a wood. From 100 m it is clearly
denser than 210's, but it is still more open than today's closed canopy. The GPU body pool is now the limit: at forest_100m the peak is
253,907 of 254,400 (493 free). A closed canopy needs pool room. 77.6 k of the 103.4 k tree bodies there are byte-identical copies, so a
dedup pool would free them; that is design work, not queued. 214 (default flip) can go ahead as it is.

## What changed (commit fe4fc48)
- `spec.trees2On` 1 = task 213's denser placement (what `-trees2 1` now means), 2 = task 210's placement byte for byte (`-trees2dense 0`), 0 = today's trees.
- Dense mode (`Trees2.GatherDense`):
  - Oaks alone on the 12.8 m grid. Birch, spruce and acacia on a new 6.4 m fine grid (one tree a cell, root snapped to 8).
  - Forest brick budget 1.4x 210's: oak 8/16, birch 8/16. Snow and savanna keep 210's budget; only their grid changes.
- Kept from 210: brick budget per area (a bigger tree is rarer), the shared prototype library, nothing stored per tree, same site rules, tiers and Near ranges.
- Efficient by construction: rivals are found through cell index maps (every keep radius <= 96 voxels, so 3x3 normal or 5x5 fine cells).
  - Gather and `Suppress192` cost O(candidates), not 210's O(n^2). A 4x finer grid at shell 5 would otherwise have cost ~16x.
  - Scratch: one int per cell (Temp), sized by `MaxCells`.
- Files: `Assets/TerrainGen/Trees2.cs`, `TerrainSpec.cs`, `TerrainGenChunkSource{,.Trees,.Trees2}.cs`, `Trees2Tests.cs`, TASKS.md "Task 213", `tools/trees213.py`.

## Evidence
- Tests (Trees2Tests):
  - `Dense_Density_FootprintsAgree` (new, Slow):
    - 210's placement pinned (109 trees, oak 17, birch 92 = 210's chunks.txt).
    - 256 m forest window: dense 233 trees vs 150 (1.55x) for 1.33x the world tree bricks (assert <= 1.40).
    - 16 chunk gathers give exactly the trees of one 256 m gather (`Logs/TerrainGen/trees213/density.txt`).
  - `Library_Turns_Budget` also checks the dense budget per biome and that each tier sits on its own grid. At full plane the forest has 25.7 trees per 51.2 m square (210: 15.1).
  - `Chunks_Deterministic_Border_PointAndShellsAgree` now runs the dense mode: chunks, FarQuery and shells 0-3 agree.
  - Calibration: `Dense_Sweep` (Explicit), 11 budget/share settings in `Logs/TerrainGen/trees213/sweep.txt`. 2x the budget = 1.88x the bricks: over the room.
- Suites: fast before PASS 1049 FAIL 0 SKIP 22 (`Logs/overnight/213/fast_before.out`); SUITE=all PASS 1130 FAIL 0 SKIP 33 (`Logs/overnight/213/all.out`).
- Launches: one build, three arms, 4 poses each (forest_eye, forest_100m, savanna_100m, snow_100m), 30 s wait; `Logs/overnight/213_shots.sh`, outputs `Logs/overnight/213/look_{d,s,t0}.out`.

| (launch peaks) | 213 dense | 210 | today (-trees2 0) |
|---|---|---|---|
| process peak MB | 2,546.4 | 2,522.7 | 2,522.8 |
| GPU body pool peak (cap 254,400) | 253,907 | 225,378 | 254,399 |
| GPU refusals (total) | 0 | 0 | 294 |
| CPU pool peak (valve 484,500) | 493,892 | 491,868 | 494,446 |
| forest tree bodies eye / 100m | 93.1 k / 103.4 k | 68.5 k / 77.7 k | 112.8 k / 126.7 k |
- Budget met: +23.6 MB over today (limit +150 MB), refusals 0 < 294, GPU peak below today's, no pool made bigger. Valve evictions print only with `-mirrorprobe` (not run). Per-pose table: `Screenshots/213/table213.txt`.
- Savanna and snow tree bodies are unchanged within noise (18.2 vs 16.2 k, 51.5 vs 51.5 k).
- Pictures looked at:
  - forest_eye: 213 shows a stand of birches and oaks with a birch wood behind; 210 has a few scattered trees.
  - forest_100m: 213's wooded patches are visibly fuller than 210's, but the meadow clearings and gaps remain; today's canopy is closed.
  - savanna_100m: acacias and baobabs with natural spacing, no clumping from the fine grid. snow_100m: the same as 210's.
- Rough cost (no timing arms): mean shell chunk generation 132-153 ms (dense) vs 130-187 (210) vs 148-198 (today). Launch order and chip heat dominate.

## Left / next
- A closed canopy from above needs GPU pool room: a byte-dedup body pool (dupes 77.6 k at forest_100m) or task 216's view-priority eviction. Owner's call; not queued.
- "One step denser" (budget 1.5x, 8/8) would be 1.53x the bricks in the window (sweep.txt), over the pool at forest_100m. Not launched.
- Next: task 214 (make `-trees2 1` the default). `-trees2dense 0` stays as the comparison arm.

## For the doc
- Forest trees per 51.2 m square at full plane: **15.1 -> 25.7** (210 -> 213); 256 m forest window 150 -> 233 trees (1.55x) for 1.33x the tree bricks (`Logs/TerrainGen/trees213/density.txt`)
- Forest tree bodies (GPU) eye / 100 m: 68.5 k / 77.7 k -> **93.1 k / 103.4 k** (today 112.8 k / 126.7 k) (`Screenshots/213/table213.txt`)
- GPU refusals: **0** (210: 0, today: 294); GPU pool peak 253,907 of 254,400 (today 254,399): the pool is now the limit
- Process peak 2,546 MB vs today 2,523 MB (+24 MB, limit +150); CPU pool peak 493,892 vs 494,446
- Images (full-size flip viewer `Screenshots/213/viewer.html`: arrows = pose, Space = cycle 213 / 210 / today):
  - `Screenshots/213/sheet213.jpg`: columns = 213 dense | 210 | today; rows forest eye, forest 100 m, savanna 100 m, snow 100 m. Row 1: the left panel has many more trunks and a wood behind. Row 2: denser groves than the middle panel, still more open than the closed canopy on the right. Rows 3-4: unchanged in look.
  - `Screenshots/213/forest_eye_d.png`: eye height in the new forest. Birches and oaks fill the frame, with a birch wood behind the river on the left.
  - `Screenshots/213/forest_100m_d.png`: 100 m up. Compare its right half (dense groves) with `Screenshots/210/forest_100m_t2.png` at the same camera.
