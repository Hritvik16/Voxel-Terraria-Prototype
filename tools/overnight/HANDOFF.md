# 74-engine-10-coarse-first: DONE. Amendment 8.14 adopted; shell chunks generate at 0.2-12.8 m, exact, row 13's line holds on seed 42. Two owner questions before step 11 (below).
## Questions for the owner (needed before step 11 renders shells; not blocking this step)
1. **The coarse top loses the 0.3 m grass/sand/snow layer.** A centre sample lands inside the 3-voxel surface layer
   (`DetailField.SURFACE_THICKNESS`) in only about 3/2^k of top voxels. Top-voxel material matches the vote in
   96 / 77 / 61 / 56% of columns at shells 1-4 (seed 42 river plains shell 3: 43%). Distant land would show soil or
   rock colour instead of grass or snow. **(a, recommended)** Make the top: a coarse voxel that is its column's top
   solid voxel takes the material of the fine voxel at the column's ground height. That is one change to row 13,
   and the test can still check it exactly. (b) Keep the pure centre sample and let the shading fix the colour.
   (c) Use the far band's `TopMaterial` per column.
2. **Sample index.** Row 13 uses fine index 2^(k-1) per axis. Index 2^(k-1)-1 (one fine voxel lower) matches the
   cascades' tie-to-solid vote better everywhere. Pooled shell 1: voxels 97.17 -> 98.49%, heights 98.97 -> 99.58%.
   Flat river plains: 94.7 -> 98.7%. **(a, recommended)** Switch, which makes edited chunks pop less when they move
   to the downsampled path (row 12). (b) Keep the centre.
## What changed
- `9a859f1` Amendment adopted: `AMENDMENT_8_14_LOD_SHELLS_DRAFT.md` renamed to `AMENDMENT_8_14_LOD_SHELLS.md`; header
  says ADOPTED 2026-10-11 with Q1-Q11; CLAUDE.md ground-truth list gains 8.14 after 8.12. No code.
- `ac334f8` Step 10: `Assets/CoreEngine/WorldGen/TerrainGenChunkSource.Shell.cs` (new; the class is now `partial`):
  `GenerateShellChunk(shell 0-7, coord, ShellChunk[, sampleIndex])`, two Burst jobs using `Generate`'s own Sample,
  MaxStep neighbours, `PrepareColumn`, ore plane, `CullBrick` per FINE brick and `Material`, plus the air fast path.
  Output goes to caller-owned scratch (`ShellChunk`), never ChunkStore; no op log yet. `Assets/CoreEngine/Tests/
  ShellChunkTests.cs` (new, 3 tests). Frozen APIs, D.1, §4.4 and `Generate` are unchanged.
## Evidence
- `SUITE=all` **PASS 726 FAIL 0 SKIP 19** (Logs/overnight/74-all.out). Baseline: 73's fast run, 691/0/14; code was
  unchanged from 73 until this task.
- Exactness: shell 0 == `Generate` byte for byte (surface, -3 layers, cavern layer). Shells 1-7: 79,507 voxels each
  vs TerrainGen's unculled D at the centre, **0 differ** (plus underground chunks at shells 1-2).
- Agreement (Logs/TerrainGen/coarse_first.txt; regions of 2x2 or 4x4 0.1 m chunk columns around the surface, one per
  landform; 9 landforms on seed 42, 8 each on seeds 7 and 1234):
  - **Gate, seed 42 (row 13's set):** shells 1/2/3 voxels in mixed bricks 97.32 / 97.58 / 97.77% (>= 97); heights
    within 1 voxel 99.97 / 99.995 / 100% (>= 99). **PASS.** Shell 4: 97.75 / 100%.
  - **Below the line elsewhere (reported, not gated), shell 1:** seed 7 97.40% voxels / 96.97% heights; seed 1234
    96.57 / 98.74%; pooled 97.17 / 98.97%. Causes: steep faces (seed 7's canyonlands and volcanic sites have a mean
    fine step of 4.27 and 2.83 voxels, seed 42's <= 1.5; a sideways half-voxel shift reads as 2-3 voxels of height,
    and 97% of misses are "coarse 2-3 below"); the vote's tie-to-solid bias on flat ground (plains 94.7%); the vote
    thickening floating ledges by one voxel, where the centre sample is closer to the true shape.
  - I first gated the pooled total as well. It failed at 98.97% heights, so I narrowed the gate to the set row 13
    names and kept the rest as report lines. The test header says so.
- Generation cost (from the test; Editor, Burst, one thread, warm, mean of 3; no rig): `Generate` 47 ms; shells 1-2
  78-88 ms; shells 3-7 193-217 ms. Shells 0-2 pay 4 extra MaxStep samples per column; at shell >= 3 every coarse voxel
  is in its own fine brick, so `CullBrick` runs per voxel. The amendment estimated "about the same as a 0.1 m chunk".
  It is 1.7x (shells 1-2) to 4.2x (shells 3-7) of that. That matters for step 16's columns/s, and per-brick culling
  at coarse scale is the obvious lever.
## Next
- Owner answers Q1/Q2 (each a small default change plus a test update); then step 11 (shell pools + rendering).
## For the doc
- Exactness: coarse voxels vs the 0.1 m generator at their centre: **0 of 79,507 differ** at each of shells 1-7.
- Row 13 gate, seed 42: voxels 97.32 / 97.58 / 97.77%, heights 99.97 / 99.995 / 100% (shells 1/2/3). Seeds 7 and 1234
  at shell 1: 97.40 / 96.57% voxels, 96.97 / 98.74% heights (below the line, reported).
- Top-voxel material vs the vote: 96 -> 77 -> 61 -> 56% (shells 1-4, pooled), so the surface layer is lost (Q1).
- Generation ms per shell chunk (test, Editor): shells 1-2: 78-88; shells 3-7: 193-217; 0.1 m `Generate`: 47.
- Log `Logs/TerrainGen/coarse_first.txt`. Images: rows = shells 1/2/3; columns = vote top | coarse top | vote section |
  coarse section (red = solid/air differs).
- `Screenshots/74/74_alpine_range.png`: seed 42 alpine slope. The coarse sections (right column) match the vote except
  a few red specks on the slope. In the shell 2 row (middle), the coarse top view is more speckled.
- `Screenshots/74/74_river_plains.png`: seed 42 plains. In the shell 3 row (bottom), the coarse top view is blue-grey
  where the vote's is light grey. The thin snow layer is lost (Q1).
- `Screenshots/74/74_below_line_seed7_canyonlands.png`: an overhanging ledge (right half of the sections). The vote's
  ledge is one voxel thicker underneath (red outline at shells 2-3). The slope differs only in specks.
