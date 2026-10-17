# 93e (second run, owner Q55 (b)): DONE -- fluid terrain directory built; the 16^3 GPU mirror box is now DEFAULT with `-shells 1`; fluid stays live to 128 m on every preset

## What changed (commit 9d85dc6, engine-phase)
- `TerrainClipmap.FluidDir.cs` (new): a second terrain lookup for the GPU fluid CA. It holds resident chunks OUTSIDE the mirror box that a fluid tile (+1 voxel) touches.
  - Index: the chunk's STORE-ring slot (pow2, `&` mask), then an entry with an alias check on the coords, then 4,096 brick handles.
  - Dense bodies stay at their CPU pool index in `BrickDataBuffer`.
  - Written FROM the CPU state in `UploadDirty`, under the same budget (CPU stays the sole writer).
  - An index slot is published after its handles, and cleared at once when the chunk is no longer wanted, is evicted, or enters the box.
  - Size: 64 entries (1 MB) at start, grows to 1,024 (16 MB), with refusals counted. Well under the 32 MB stop line.
- `FluidCA.compute` `SampleVoxel`: outside the box it reads this directory. `_FluidDirRing.w` = 0 keeps the old AIR path.
- `FluidGpuSimulation`: publishes the wanted chunks each tick (`AddChunksTouchedByTile`).
  - Bug in the first draft, fixed: a wake was released before its chunk's entry existed, so the CA read AIR and the wake was lost. The wake gate now waits for the entry (`FluidDirHolds`; `MaxWakeDeferTicks` still bounds the wait).
- `Phase4Bootstrapper`: `-smallmirror` now defaults to 1 with `-shells 1` (Low/Medium/High 16^3; Ultra keeps 32x8x32). `-smallmirror 0` = the old box. `-fluiddir 0` = no directory (fluid past the box freezes again).
  Shells off: unchanged. Play is unchanged too, because shells are still opt-in in play (77's Q1).
- `-fluidprobe -fluidfar D`: puts the pool ~D m away, outside the box, within 120 m (3D) and in line of sight. It tries the eye-level poses in turn, and each shot has a 6 degree telephoto twin.
- Memory ledger row "fluid terrain dir"; KNOWN_ISSUES entry marked FIXED; CLAUDE.md updated. No frozen API changed, no §4.4 change.

## Evidence
- `FluidTerrainDirTests` (5, 1.2 s):
  - Water at ~106 m, outside the real 16^3 box, run on the CPU reference CA: the GPU-lookup twin's terrain gives the same flow as the ChunkStore's terrain, move for move and byte for byte. It also flows the same as inside the box.
  - Control: without the directory the CA reads AIR there.
  - Edit / unwant / evict / box-slide keep the directory exact; `CountStaleSlots` = 0 after the recentre. Growth past 64 entries works.
  - Fluid radius 1280 v (128 m) on every preset; Low-High need the directory, Ultra does not. Pow2 is asserted.
- **SUITE=all PASS 852 FAIL 0 SKIP 23** (`Logs/overnight/93e2-all.out`). UploadDirtyBoxTests and E1's EditSummaryTests pass.
- Shells off: `run-phase7-rig.sh` **PASS 37 FAIL 0, 19/19 shots byte-identical** to 93e's `Phase7Cycle/2026-10-16_071555` (`Logs/overnight/93e2-phase7.out`).
- Fluid at 111 m, eye_level_2 (eye_level_1 has no open site at 110 m: mountain), chunk outside the 16^3 box, 20 s after placing 13,671 water voxels:
  - before (`-fluiddir 0`): 0 ops, all 13,671 still in the box (frozen).
  - after: 323,347 ops, 10,212 voxels moved out of the box; lava flows too. The directory held 4 entries, 0 refused.
  - Logs: `Logs/overnight/93e2-fluid-{before,after}.out`.
- Mirror check, after, 4 poses: **MIRROR lost 0 lag 0 stale 0**, box 16^3, resident 3,087 (128 m kept) (`Logs/overnight/93e2-mem-after.out`).
- Memory, shells on, same build, 93e's 4 poses (`-smallmirror 0` vs default), both arms on battery (memory only, no timing):
  - TerrainClipmap row: 513.1 GPU + 147.5 managed -> 440.7 + 75.1 MB. Fluid terrain dir: +1.1 MB GPU (+1 MB managed copy).
  - Ledger SUM 1,905.6 -> 1,759.1 MB; ledger GPU (Metal) 1,211.5 -> 1,137.4 MB.
  - phys_footprint median (36 samples after start-up) 3,022 -> 2,674 MB; peak 3,037 -> 2,708 MB.
    Part of that gap is managed-heap noise: the before arm's heap was 408 MB vs 200 MB. The clipmap part is the ~147 MB above.
  - Files: `Screenshots/93e2/mem_{before,after}/`.
- No frame time measured (rule 10).

## For 93f (D3, GPU brick pool)
The directory's dense bricks are read at their CPU pool index in `BrickDataBuffer` (`FluidCA.compute` `SampleVoxel`, after `ReadFluidDirHandle`).
D3 must give every directory entry's dense bricks a GPU page too, written when the entry is published, and keep each mapping until the entry is dropped.
That covers up to 1,024 entries: today a few, worst case all awake-fluid chunks within 128 m. Remap the handle in `FluidDirHandles` the same way as `ClipmapBuffer`.

## Not done / next
- The 93e stills (box edge, shell tint) are from the first run and still hold: only the fluid lookup changed here.
- `run-acceptance-rig.sh` was not rerun: it cannot pass `-shells 1`, and shells off is byte-identical (Phase 7 above).
- Next: 93f (D3).

## For the doc
Task 93e (owner Q55 (b)): the GPU fluid CA gets its own small terrain lookup for chunks outside the shrunken GPU mirror box. The 16^3 box is now ON by default with the shells, and fluid stays live to 128 m on every preset.
- Water 111 m away, outside the box, 20 s: **frozen (0 moves) -> flowing (10,212 of 13,671 voxels left the box)** (`Logs/overnight/93e2-fluid-{before,after}.out`).
- Box: Low/Medium/High 32x8x32 -> **16^3**, Ultra 32x8x32. CPU reach 128 m (3,087 resident). Mirror lost/lag/stale 0/0/0.
- Memory (shells on): TerrainClipmap 661 -> 516 MB, fluid lookup +2 MB. Ledger SUM 1,906 -> 1,759 MB. Footprint median 3,022 -> 2,674 MB (`Screenshots/93e2/mem_*`).
- Shells off byte-identical (Phase 7 19/19, PASS 37). SUITE=all PASS 852 FAIL 0.
Images:
- `Screenshots/93e2/fluid110m_tele_sheet.jpg`: a 6 degree telephoto of a test pool 111 m away (dark blue = water, orange = lava). Left = BEFORE (no fluid lookup) at 20 s: the water box stands frozen, sharp-edged. Middle = AFTER at 2 s: its edges already break up. Right = AFTER at 20 s: the water has slumped into a pile and the lava has spread.
- `Screenshots/93e2/fluid_after/strip_20s.png`: the normal-FOV view from the eye. The pool is the small dark-blue speck at the frame centre (orange lava dot just right of it) on the sandy slope, 111 m away; the lake on the left is natural, not the test pool.
