# 39-bug-fluid-pool-full: DONE. A full pool now keeps the tiles nearest the player, so placed water flows at a coast. The HUD's pool and "live" readouts are fixed. Commit dd6178b.

## What fills the pool (log: Logs/overnight/39_poolprobe_old2.out; reports in Screenshots/Task39/{old,fix}_poolprobe.txt)
- **The pool fills at the COAST, not at lakes.** At the lake shore 488 tiles are wanted and the pool has room (488/1024). At the sea coast, 3,845 tiles are wanted inside the 128 m wake radius:
  - tg2 **beach sand (material 142, a falling solid)** is on every beach tile: 89k of the samples;
  - sea water: 1,062 tiles at 96-128 m, 320 of them all-water interior.
- **Bug:** admission followed the chunk scan order. At the coast, tiles at 128 m were resident while tiles at 46-50 m were refused.
- **Player-facing effect (old arm):** a water box placed 2 m from the player at the coast had its tile refused.
  - All 1,029 voxels stayed frozen for 480 frames. The vent's water hung in mid-air.
  - At the lake shore the pool had room, so water flowed in both arms (1,029 -> 400 in 8 s).
- **"pool FULL 129,689x" is not a tile count.** About 97% of `PoolExhaustionsTotal` is per-VOXEL wake retries: op neighbourhoods, and the vent asking again every frame. At the coast it grew by 300k in 8 s; residency passes refuse at most about 1,000 tiles per pass.
- **"live 750,000" was Counters[0], the slot high-water mark.** It never comes down, but it is real in the sense that the 750k cap was reached.
  - Disturbing a tg2 lake took the high-water from 26k to 673k.
  - The probe finds 85-90k lake water cells with air DIAGONALLY below them. The lake surfaces are stepped and not at rest, so each re-centre seed sets the lake flowing.

## What changed
- `FluidTileResidency.Refresh`: wanted tiles are admitted nearest-first. When the pool is full, a wanted tile displaces the farthest resident tile, but only if that tile is STRICTLY farther, so the set is stable for a still player. New field `Stats.TilesDisplaced`.
- `FluidGpuSimulation.RequestWake` applies the same rule to a wake into a full pool (`TryDisplaceForWake`, with the farthest tile cached).
- `FluidTileMap` gets `TileDistSq` and `TryFarthestResident`.
- Two measurement seams, used by `-poolfix 0`: `FluidTileResidency.LegacyScanOrder` and `FluidGpuSimulation.DisplaceForWakes`.
- HUD:
  - "pool FULL" now shows the last pass's refused tiles, with the cumulative count in grey.
  - "live" is now high-water minus the free list, with the high-water mark printed beside it.
- `-poolprobe` (`MODE=poolprobe MAPV=4 ./run-tg2world.sh`, `EXTRA="-poolfix 0"` for the old behaviour) walks start -> lake shore -> coast at 12 m/s. It logs every residency pass, a tile census by distance and material, the cells a seed would promote, and the placed box/vent counts.
- KNOWN_ISSUES has a new entry ("POOL FULL / live 750,000"), with a matching line in the CLAUDE.md index.
- A first attempt (re-seeding on every tile admission) was rejected. It saturated the 750k slot pool before the lake, and the lake box froze. It is not in the commit.

## Evidence
- Fast EditMode suite: PASS 630 FAIL 0 SKIP 13. New tests: `FullPool_AdmitsTheNearestTiles_NotTheScanOrder`, `FullPool_ANearerTileDisplacesTheFarthestResident`, `FullPool_AStationaryPlayerDoesNotChurn`, `FarthestResident_IgnoresFreedSlots_...`.
- `run-fluid-tiled.sh`: PASS 19 FAIL 0, the same as 2026-09-25. Its peak is 552/1024, so it does not reach the cap or exercise the new path.
- poolprobe at the coast, old vs fix (same build):
  - frontier: nearest non-resident 49.7 m / farthest resident 127.7 m (old) -> 61.2 / 61.2 m (fix);
  - placed box: 1,029 -> 1,029 (old) vs 1,029 -> 722 (fix) in 8 s;
  - 79 wake displacements in the fix run.
- Images, all in `Screenshots/Task39/`:
  - `old_coast_after_8s.png`: the dark pillar at left is the placed box, still intact. The flat blue slab hovering over the sand on the right is the vent's water, frozen in mid-air with its shadow under it.
  - `fix_coast_after_8s.png`: the same view. The pillar is crumbling from its base, and the slab is breaking into falling lumps.
  - `fix_lake_after_2s.png`: the pillar over the lake is half gone (500 of 1,029 voxels), and the vent's water is falling in chunks at right. The speckled blue across the foreground grass is lake water spreading onto the shore (the stepped-surface flow).

## Open (owner, not blocking)
1. **Coast capacity.** At a 128 m radius a coast wants about 3.8k tiles; the pool holds 1,024 (0.5 MiB each, 520 MiB in total). Today the sea beyond about 61 m sleeps. Options:
   - (a) RECOMMENDED: make beach sand 142 static, as snow already is. MaterialRules says the generator only places it at rest.
   - (b) a bigger pool (memory);
   - (c) accept as is.
2. **Stepped tg2 lake surfaces** wake about 500k slots when touched. This is terrain-generator work: flatten the lake water level per lake.
3. **The coast's wake queue is full** of op wakes (capacity = maxOpsPerFrame), so the placed box's own 1,029 wakes were dropped and it flowed slowly. Giving edit wakes priority would be a behaviour change and needs approval.
- No follow-up task was queued.
