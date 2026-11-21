# 250-voxel-grass-fringe-counts: DONE -- short voxel grass is CHEAP: fringe removes tier-0 steps (-47..-108 k per 1 % of screen; leaf +305 k), ~0 MB
Note: Q-249 (8.18 kill rule) is still open; this task did not touch it.
## What changed (commit 3603953)
- `-fringe 1` (DEFAULT OFF; `TerrainSpec.fringeOn`): `WorldGen/TerrainGenChunkSource.Fringe.cs` `OverlayFringe` (Burst, last in VoxelJob, LOD-0
  only). The stubs are 1-3 voxels tall, on Grass/JungleGrass (new GenMat 18 -> engine 146) or SavannaGrass (GenMat 9 -> 137), with air above. Slope rule: no solid around
  host y+2. A hash of world pos + seed picks 3 columns in 8. Stubs go ONLY into already-dense bricks; a uniform brick refuses them. Guard = dense-brick count before/after
  (`[fringe] ... new bricks 0` log line). Palette `tools/palette_defs.py` (regenerated tables). Both are new GenMats, and both are in `FarField.GenToEngine` (128+g).
- RayStats: I ADDED a "fringe" class, in the primary record's spare slot [5] (a primary ray never takes the foliage early-out, so [5] was
  always 0). The record does not grow. The report has a fringe column, and primary folstop no longer reads [5]. The twin test is updated.
- Collision: the stubs WOULD have collided (IsBlocking = any non-air non-fluid). Made non-solid with ONE line in `VoxelCollision.IsBlocking`
  (flagged in effect: the ids exist only under `-fringe 1`).
- Tests `FringeTests`: synthetic no-new-bricks; fringe OFF byte-identical (FNV pinned `0x019EEF889FB4C206` from the pre-250 code); fringe ON =
  only air -> fringe on a host, identical dense-brick table (seed 42, forest_eye + savanna_eye chunks: 8,405 / 8,915 fringe voxels, 2,084 dense
  bricks both). Docs: WORLD_FILL_BUDGET sec. 0 (rule 3 update), 1b.6 (new), 2 (row), 5 (E3 DONE); TASKS.md.
## Evidence
- SUITE=all before: PASS 1221 FAIL 1 (my not-yet-wired fringe test) SKIP 39. After: **PASS 1223 FAIL 0 SKIP 38** (`Logs/overnight/250/suite_*.out`).
- 8 launches, counts only (`Screenshots/250/run.sh`, `Logs/overnight/250/costsplit_{off,on}_{16,noon}.txt`). Table: `Screenshots/250/fringe.txt`.
  The meadow pose is **savanna_eye** (task 210's savanna pose). savanna_close = the same eye looking at the ground about 5 m ahead.
  Fringe covers 42.9 % (forest_eye), 36.0 % (savanna_eye) and 69.7 % (savanna_close) of the screen. Tier-0 steps go DOWN at every pose and both suns:
  -63..-108 k per 1 % at forest_eye, -71..-96 k at savanna_eye, -47..-76 k at savanna_close (noon..16 deg). Primary steps fall 2.3-3.2 M, sun steps
  fall 0.2-1.9 M, and sky steps move by +-0.15 M. It acts as solid content (1b.3), not as porous content.
- Memory, rule 11 (on vs off): GPU in use 245,704 -> 245,654 at forest_eye (-50, sealed +50) and 184,562 -> 184,534 at savanna. Refused 0 in all 8.
  GPU peak 245,704 -> 245,654. CPU pool peak 494,647 -> 494,820 (16 deg) and 493,054 -> 493,418 (noon): streaming-order noise, the same bricks.
  Valve in-view evictions 0. Process peak 2,498 -> 2,534 (16 deg) and 2,523 -> 2,492 MB (noon): noise. Over 30-32 k chunks: new bricks 0.
- Pass card (rule 12): no new pass, 0 GPU bytes. It changes content inside the tier-0 march (8.17 step "Tier-0 march + far band + shading", share 5.0 ms).
  Est. ~0 ms. P3: none quoted (rule 10, no frame times). P4: bodies -50 / -28. P5: rays unchanged, tier-0 steps -13 to -30 %.
  Slice (rule 14): tier A, inside the tier-0 march slice, ~0 ms / ~0 MB. Not HEAVY.
- Stills I looked at: `savanna_close_on_16.png` shows the ground covered in 1-3 voxel gold stubs, with bare brick-aligned patches. `forest_eye_off/on_16.png`:
  the foreground grass becomes green stubs, the mid-ground reads as a thicker turf, and the trees are unchanged. I saw no seam at the tier-0 edge.
## Findings / left open (no fix made)
- **Bare 0.8 m patches:** the no-new-brick rule refuses a stub whose ground voxel is a brick's TOP layer (1 height in 8; 4.6 % of stub voxels).
  Fixing it means new bricks above those ground voxels, which costs memory. That is the owner's call.
- The fringe is LOD-0 only. The shells, far band and point query (FarQuery) do not have it. Fluids treat it as solid (FluidCA). Digging the grass under a stub
  leaves the stub floating. The edit tools can target stubs. The density (3/8) and height mix are an experiment choice, not tuned for looks.
- The first launch stalled about 1.5 h at the forest teleport (22:51 -> 00:30; other apps held 3.9 GB of swap). Counts are unaffected; the later launches took about 13 min.
## Next
- Owner question for the supervisor's tab: does the owner WANT voxel grass stubs in the look (`savanna_close_off_vs_on_16.jpg`)? If yes, these follow:
  a look pass (density, colour, the bare patches) and one timing arm at the next forest milestone. E2 (mesh layer) stays the path only for clutter taller than
  the surface brick. E4 (253) is next in the queue.

## For the doc
Task 250: can grass be voxels? Counts per 1 % of screen covered (WORLD_FILL_BUDGET sec. 1b.6; `Screenshots/250/fringe.txt`, 248's `coverage.txt`).

| content | tier-0 steps per 1 % of screen | est. ms per 1 % (5.7-7.5 ms per M) | MB |
|---|---|---|---|
| leaf (porous crowns, task 248) | +305 k | ~+2 (1.7-2.3) | trees +45 MB GPU at forest_eye |
| wood / solid (task 248) | 0 to -190 k | ~0 (budget 0) | per prop: surface bricks x 512 B |
| **grass fringe, 1-3 voxels (task 250)** | **-47 to -108 k** | **~0 (-0.3 to -0.8; budget 0)** | **~0 (0 new bodies; -50 sealed)** |

**Yes, for SHORT grass.** 1-3 voxel stubs inside the ground's own 0.8 m blocks cost no memory and remove ray work. They act as solid content, not like leaves.
Taller or see-through grass that rises into the empty block above is still untested. That stays E2's job.
Image: `Screenshots/250/savanna_close_off_vs_on_16.jpg`. Left = off, right = `-fringe 1`, same pose, 16 deg sun, ground about 5 m ahead. Look at the lower half:
the flat gold ground becomes a field of 0.1 m stubs, 1-3 voxels tall. Also look at the few bare square patches (e.g. mid-right): those are where the no-new-brick rule refused the stubs.
Image: `Screenshots/250/forest_eye_off_vs_on_16.jpg`. Same forest pose as 248. Look at the bottom third: the grass turns into green stubs. The trees and the far view are unchanged.
