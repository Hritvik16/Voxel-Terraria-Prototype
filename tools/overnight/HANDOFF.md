# 49-engine-4-memledger-pool-peaks: DONE. `-memledger` now prints every pool's capacity / in use / peak (+ fluid refusals); 3 evidence runs.

(Task 48 is still blocked on the owner's upscaler pick. This task changed no upscaler or default.)

## What changed (branch `engine-phase`, `1fcfcb8`)
- `Assets/Game/MemoryLedger.cs`: new `PoolReport()`, appended to `Report()`. For tier-0 bricks, cascade tiers 1 and 2, and
  fluid tiles it prints capacity, in use, peak, peak/cap, bytes/unit, MB of the whole cap, and MB if the cap were peak x 1.25.
  It also prints the fluid refused-tile total (`FluidTileMap.PoolExhaustionsTotal`). It only reads counters.
  Bytes/unit: a brick is 512 B CPU + 512 B GPU mirror. A fluid tile is 32^3 cells x 16 B (claim, slotAt, reacted, wakeMark) + 40 B.
- `PlaygroundCapture.cs`: new helper `MemLedgerIfAsked()`. The ledger already printed at the end of `-sustained`; it now also
  prints at the end of `-farpreview` (MODE=poses) and `-poolprobe`, which never printed it before.
- No cap, pool, allocator or engine change.

## Evidence
- Fast suite: PASS 634 FAIL 0 SKIP 14 (`Logs/overnight/49_fast.out`). Build: 0 `error CS`, 0 Shader error.
- Runs measured memory counts only. No timing was taken and none is claimed. Map v4, AC power, no owner-pause note.
  - poses: `Logs/tg2world_20261010_072648.txt` (copy `Logs/overnight/49_poses.out`)
  - walk: `Logs/tg2world_20261010_074313.txt` (`Logs/overnight/49_walk.out`)
  - poolprobe: `Logs/tg2world_20261010_075028.txt` (`Logs/overnight/49_pool.out`)
- **Comparison with 26c.** 26c's histogram counted dense bricks at the peak pose: tier 0 473-499k, tier 1 ~92k, tier 2 ~19k.
  The measured PeakUsed is 395k / 82.6k / 16.4k (poses). The two agree in size: every measured peak is 10-20% lower.
  A likely cause is that 26c used the map before the rebuild (v3) and v4 terrain is gentler. I did not verify that.
  The conclusion does not change: the tier-1 cap is 3.9x its peak and the tier-2 cap is 5.8x.
- **The fluid pool is full in all three runs** (1024/1024 tiles, peak = cap). This means its "peak" is set by the cap, not by demand.
  - Demand at the coast stop: the residency radius wants **4,667 tiles within 128 m**, and the pool holds 1,024.
  - Of the 3,643 wanted tiles left out, at least 2,228 are "other" in the census (64-128 m bands). No tile in any band is all-mobile (column `all-512-mobile` = 0).
  - So "peak x 1.25" is not a valid size for fluid tiles. Step 5 (tiles only for awake fluid) has to set it.
  - The fluid slot high-water mark at the coast was 426,866-500,291 slots. That is a separate pool and is not in the table.
- Disk: I deleted the runs' player folders (FarBand x2, Sustained x1, 83 MB). The poses report text is kept at
  `Logs/overnight/49_poses_farpreview.txt`. 7.1 GB free at the end.

## Unfinished / next
- Nothing is left in this task. Next is engine plan step 5 (fluid tiles only for awake fluid), then step 6 (cascade caps sized
  from this table). Both need their own task files. I added no follow-up.

## For the doc
Pool peaks, tg2 map v4. Peak = high-water mark since start. MB = CPU + GPU, in MiB.

| pool | capacity | peak used (poses) | peak used (walk) | peak/capacity (max) | MB now | MB if cap = peak x 1.25 |
|---|---|---|---|---|---|---|
| tier-0 bricks | 750,000 | 395,423 | 312,681 | 52.7% | 732.4 | 482.7 |
| cascade tier 1 | 320,000 | 82,641 | 62,000 | 25.8% | 312.5 | 100.9 |
| cascade tier 2 | 96,000 | 16,437 | 13,342 | 17.1% | 93.8 | 20.1 |
| fluid tiles (32^3) | 1,024 | 1,024 (full) | 1,024 (full) | 100% | 512.0 | n/a (peak = cap; coast demand 4,667 tiles) |

- poolprobe (coast) peaks: tier 0 379,529, tier 1 76,150, tier 2 16,044. Every probe ended with the fluid pool full.
- **Brick caps at peak x 1.25 would save about 535 MB** (CPU+GPU), from 1,138.7 to 603.7 MB. Of that, tier 1 alone is 212 MB.
- **phys_footprint peak:** poses 3,353 MB, walk 3,027 MB, poolprobe 3,085 MB. The cap is 3 GB, and all three runs are over it.
- **Fluid tiles refused since start** (pool full): poses 1,034,672, walk 994,060, poolprobe 3,347,356.
- No images. This task changes nothing visible, so the report is numbers only.
