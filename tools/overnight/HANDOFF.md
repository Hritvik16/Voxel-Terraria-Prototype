# 54-engine-5-fluid-awake-tiles: DONE. Fluid tiles now go to AWAKE fluid only (Playground/tg2). At the coast, refused tiles went 2.04 M -> 0, the pool peak went 1024 -> 32 tiles, and the placed box's wakes are no longer dropped (1,029 -> 0).

## Contract check: no frozen contract changes
- The 7.2 tile pool's admission and release are CPU bookkeeping (FluidTileMap / FluidTileResidency).
- Untouched: §4.4 state table, D.1, public APIs, Appendix A/B (`fluidTileMask` unchanged), the shader, decision 11's tick. DESIGN_NOTE_7_2 §4.4 already said "freed when its live count reaches zero"; the mask scan was the stopgap for §9's wake-on-approach gap.
- The fixed-rate half of plan amendment row 5 (decision 11) belongs to step 17 and is not touched here.

## What changed (commit ac23cf3, branch engine-phase)
- `FluidTileMap`: added `AwakeOnly`, a tick `Clock`, per-tile last-activity stamps, `NoteActivity`, `ReleaseQuietTiles`, and an "unsettled" memory.
  - A tile released while still active (radius, orphan or displacement) is remembered (cap 65,536); re-acquiring it sets `SeedRequested`.
- `FluidTileResidency.Refresh` with AwakeOnly: there is no mask scan. It releases quiet tiles, then re-admits remembered tiles that are in the radius and still hold fluid (nearest first).
- `FluidGpuSimulation`:
  - `Tick` advances the clock and turns `SeedRequested` into the recentre seed; `RequestWake` stamps activity.
  - New `NoteOpApplied`: each applied op keeps its tile awake and acquires the tiles across any face it touches. Without this, lateral flow (no wake flag) would stop at 32-voxel tile faces.
  - `QuietTicksFor()` = sleep ticks 8 x slowest interval 60 + 8 = 488 ticks.
- `FluidOpListReadback` calls `NoteOpApplied` for dst and src.
- `Playground` turns AwakeOnly on. `-awakeonly 0` is a measurement seam that restores the mask policy. The HUD says "(awake fluid only)".
- Pool probe prints the new counters and "dropped (wake queue full)". New tests: `FluidAwakeOnlyTilesTests` (6). KNOWN_ISSUES and the CLAUDE.md index line are updated.

## Evidence (memory/behaviour only, no timing; rule 10)
- Fast suite: PASS 640 FAIL 0 SKIP 14 (`Logs/overnight/54_fast.out`). No fluid test is in the Slow category, so SUITE=all was not needed.
- `run-fluid-tiled.sh`: PASS 19 FAIL 0 (`Logs/overnight/54_fluidtiled.out`). This rig still runs the MASK policy (it never sets AwakeOnly), so it shows nothing regressed there. It does not exercise awake-only.
- Pool probe, same build, built here (build OK, 0 error CS). New: `Logs/overnight/54_pool.out` (`54_poolprobe.txt`). Old (`-awakeonly 0`): `54_pool_old.out`.

| coast stop | mask policy (old) | awake only (new) |
|---|---|---|
| refused tiles since start | 2,037,814 | 0 |
| tiles in use / peak (of 1,024) | 1024 / 1024 | 32 / 32 |
| slots live on arrival (before placing) | ~356,216 | 0 |
| placed box: wakes queued / dropped | 0 / 1,029 | 1,029 / 0 |
| box water after 480 frames (of 1029) | 416 | 3 |
| wakes dropped, queue full (whole run) | 56.8 M | 12.5 M |
| phys_footprint peak | 2,873 MB | 2,870 MB |

- The fluid ledger is still 548.8 GPU + 42.5 managed MB in both arms, because the 1,024-tile pool (512 MB) is still ALLOCATED. The memory saving needs the resize (see below).
- Lake stop: 0 refusals, box 1029 -> 1 in 480 frames, 30 tiles. The remaining 12.5 M wake drops are op-neighbourhood wakes while a woken shore spills (pre-existing, 4.5x lower now).

## Screenshots looked at (`Screenshots/54/`, 960 px, untracked)
- `coast_before.png`: the sea at rest on the right and centre. It is now a flat slab with VERTICAL side faces standing ~1.5 m over the beach sand. This is a stepped generated surface (task 39 item 2).
  - Before: approaching it seeded the edge and spilled it.
  - Now it stays a wall until something touches it (§5.5 static water). **Visible change; the generator fix is the real answer.**
- `coast_after_2s/8s.png`: the disturbed slab edge spills over the beach as blocky moving water; nothing frozen at a tile face (dim canyon light).
- `lake_after_2s.png`: midday. The placed box is falling as a column into the lake (left of centre). The vent's water is spreading as blocks over the right half of the lake.

## Not done / follow-ups (not queued; both need the owner)
- **Pool size.** It is still 1,024 tiles (512 MB GPU). This is not a one-line change: `_tilePoolCap` is serialized in Playground.unity and shares `FLUID_TILE_POOL_CAPACITY` with every fluid rig.
  - The measured awake peak is 32 tiles, but from one small placement. x1.25 = 40 is too thin for play (a blast into a lake).
  - The plan budget (250 MB) is about 480 tiles. Owner to pick.
- Stepped generated sea/lake surfaces (now visible as water walls). This is a terrain-generator task.
- Quiet release waits 488 ticks (one material has interval 60); a per-tile interval would free water tiles ~60x sooner. Low priority.

## For the doc
- Refused fluid tiles at the coast: 2,037,814 -> 0 (`Logs/overnight/54_pool_old.out` vs `54_pool.out`, same build).
- Fluid tiles in use / peak: 1024 / 1024 -> 32 / 32 (pool still allocated at 1,024 tiles = 512 MB GPU; ledger 548.8 GPU + 42.5 managed MB both arms until the pool is resized).
- Placed box at the coast: wakes dropped 1,029 -> 0; water left in the box after 480 frames 416 -> 3.
- Live fluid slots on arriving at the coast: ~356,216 -> 0.
- phys_footprint peak (coast poolprobe): 2,873 -> 2,870 MB.
- Images:
  - `Screenshots/54/coast_before.png`: the sea on the right is at rest and costs nothing. Note its vertical side face above the beach (a stepped generated surface, now a still wall until touched).
  - `Screenshots/54/lake_after_2s.png`: midday. The placed water box falls as a column into the lake (left of centre) and the vent's water spreads over the right half. Placed water flows.
