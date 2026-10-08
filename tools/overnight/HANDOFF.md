# 32-uploaddirty-box-check: DONE. `UploadDirty` now skips and counts any resident chunk outside the mirror box, and parks it. Fast suite green. v4 poses: `MIRROR 0/0/0 outOfBox 0` on all 18 poses.

## What changed (commit `3cb76d1`, docs in the next commit)
- `Assets/CoreEngine/Mirror/TerrainClipmap.cs`:
  - New `InMirrorBox(coord)`: `(coord - _windowOrigin) & ~_chunkMask == 0` on each axis. Integer and bitwise only; a negative offset sets the high bits, so it counts as outside.
  - In the `UploadDirty` loop, a RESIDENT chunk outside the box is not staged or uploaded. It is removed from the dirty set, counted in the new field `UploadStats.skippedOutOfBox` and the new property `SkippedOutOfBoxTotal`, and added to `_parkedOutOfBox`.
  - Evicted chunks keep the existing clear path and its owner test, unchanged.
  - The skip happens before the budget test, so a stray costs no budget.
  - Additive accessor `CpuMirrorHandle(coord, localBrick)` reads the CPU copy of the clipmap. The tests need it because the suite runs `-nographics`.
  - No public signature changed.
- **Re-dirty on re-entry (checked in code).**
  - The normal admission path re-dirties a chunk only after evict + reload (`StreamManager.DrainCompletions` -> `_clipmap.MarkDirty`, StreamManager.cs ~1094).
  - A stray is always outside the load range: the box contains the load square in XZ, and in Y the mirror covers stream -4..+3, which contains the band (+-3). So a stray is always waiting for eviction. Once evicted, it comes back only by readmission, which marks it dirty.
  - The gap is a box that returns to the chunk BEFORE that eviction. Admission does not run (the chunk is still resident), so nothing would re-dirty it. The parked set closes that gap: at the start of each `UploadDirty`, a parked chunk that is in the box and still resident goes back into the dirty set. A parked chunk that has been evicted is dropped (the eviction marks it dirty itself).
- The count is printed:
  - on the poses `MIRROR` line as `outOfBox N` (`PlaygroundCapture.FarBand.cs`)
  - on the flysweep MIRROR CHECK
  - as the stream-probe TSV's new LAST column `outOfBox`
  - as a line in the acceptance report (`Phase4AcceptanceRig.cs`)
- `UploadDirtyBoxTests.cs` (new). An 8x4x8 mirror inside the 32x16x32 store ring, so chunk (9,0,1) shares the mirror slot of (1,0,1) but has its own store slot. Three tests:
  1. An in-box chunk uploads as before: uploaded 1, skipped 0, epoch set, slot = Stone.
  2. A stray: `skippedOutOfBox == 1`, total 1, uploaded 0, not dirty, never uploaded (epoch -1), and the in-box slot is still Stone.
  3. After sliding the origin to (8,0,0), the parked stray uploads (slot = Sand). The pass after that uploads 0.
- `KNOWN_ISSUES.md` (STRAY RESIDENT CHUNKS entry: the "STILL OPEN" line now points to a new task-32 sub-bullet) and the `CLAUDE.md` index line are updated.

## Evidence
- Fast EditMode suite: `PASS 622 FAIL 0 SKIP 12 suite=fast` (`Logs/overnight/32/editmode_fast.out`). All 3 new tests passed, 0 `error CS`.
  - No EditMode test calls `ClipmapValidator.CountStaleSlots`: the suite runs `-nographics`, so it cannot read the GPU back. The task's "CountStaleSlots-based tests green" therefore rests on the poses run below.
- `BUILD=1 MAPV=4 MODE=poses ./run-tg2world.sh` (pictures, not timing): `Logs/overnight/32/poses.out`, `Logs/tg2world_20261008_120302.txt`.
  - All 18 poses: `MIRROR lost 0 lag 0 stale 0  outOfBox 0`.
  - Resident: 3087 on 14 poses, 1764 on oblique_3000m / map_7000m / caldera_above / rivers_oblique. Identical to task 26's run.
  - My first launch ran the OLD player (no `BUILD=1`). I stopped it partway; its log is `Logs/overnight/32/poses_oldbuild_discarded.out` (shots in `FarBand/2026-10-08_115411_world`, incomplete). Ignore both.
- Shots: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/FarBand/2026-10-08_120657_world/`. 59 of 61 are pixel-identical (`cmp`) to today's 09:08 and 11:14 v4 runs. The 2 that differ are `minimap_check_*`, which are known noise.
  - `peak_from_2km_ground_golden.png` (one of the two poses that showed `lost 12232` in task 21): look at the green hill filling the right half and the peaks on the left horizon. Continuous terrain, no holes, nothing floating.
  - `summit_view_golden.png` (the other one, `lost 5249` in task 21): look at the red-brown slope filling the frame and the sea at right. No chunk-shaped holes.
  - `cliff_foot_1_golden.png`: look at the ridge line near the top centre. The thin upright slab task 29 flagged is still there, unchanged. It is the far band, not voxels, and NOT related to this task.

## Not changed (gaps named, no owner decision needed yet)
- A CLEAN resident chunk that leaves the box and comes back before it is evicted is never re-uploaded: its slot still holds what the in-box chunk wrote. This was already true before this task. Only deltaDirty chunks can become strays (clean ones are evicted at once), so it needs a teleport and back inside the 4-saves-per-frame window. A full fix would re-dirty newly in-box resident chunks when the window slides.
- `ClipmapValidator.ValidateRegion` walks every resident chunk, so a stray would show up as `lost`. Read it next to `outOfBox`.

## Next step
- The next queued task. Task 29's open item is still waiting on the owner: which lever to try next for the slopes, and the `cliff_foot_1` far-band slab.
