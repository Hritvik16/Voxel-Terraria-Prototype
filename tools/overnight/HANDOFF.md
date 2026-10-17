# 93f engine 14d3 (8.15 row D3) GPU brick-body pool: DONE. TerrainClipmap GPU 441 -> 193 MB, Metal 1,296 -> 1,041 MB, 0 refused, validator clean

## What changed (branch engine-phase)
- cf23259 (the first run of this task; it hit the usage limit right after committing, so it never wrote a handoff):
  `TerrainClipmap.GpuPool.cs`. With `-shells 1`, BrickDataBuffer is its own pool. A CPU-slot -> GPU-slot table is refcounted per
  holder (a mirror box slot or a fluid dir entry). The staged handles (ClipmapBuffer, FluidDirHandles) carry the GPU slot in [29:0],
  so every shader reader is unchanged: grepped Raymarch / RaymarchStripped / FluidCA; tier reads use their own pools.
  When the pool is full the chunk is refused and counted (ledger), held as air and handed to its shell (FarField residency), then
  retried once slots free. `ClipmapValidator` compares through the table. `-gpupool 0` = the old layout, `-gpubrickcap N` overrides the cap.
  Shells off keeps the old layout. `GpuBrickPoolTests` (6).
- dd8f595 (this run): the cap is set from the measurement. `EngineConfig.TG2_GPU_BRICK_CAP_16CUBE` 242,000 (the estimate) -> **254,400**
  (measured peak 203,450 x 1.25). ARCHITECTURE §3.7 got a note pointing to 8.15 §10 row 2. CLAUDE.md got a Task 93f line.
  Text reports are in `Screenshots/93f/{before,after}/`.

## Evidence
- Tests: own tests 21/21 in the first run's filter run (`TestResults_filter.xml`). **SUITE=all PASS 860 FAIL 0 SKIP 25** (`Logs/overnight/93f-chain.out`).
  The tests cover: round trip (allocate / upload / free / reuse), a long recentre walk with no slot leak (in use == in-box dense
  bricks at every step, 0 at the end), pool-full refusal counted with no overwrite and a retry, the validator finding an injected
  table swap (2 BothDenseDifferentSlot) and a body byte, and shells-off = the old layout.
- Shells off: `run-phase7-rig.sh` **PASS 37 FAIL 0, 19/19 shots byte-identical** to `Phase7Cycle/2026-10-16_171743` (run `2026-10-16_214816`).
- `run-acceptance-rig.sh` (it cannot run `-shells 1`: flat Phase 4 world, so this is shells off) `Phase4Acceptance/2026-10-16_215100`:
  **PASS 52 FAIL 1**. Gates C and D all green. The FAIL is the known Gate E §4.3 upload-p99 timing check (1.54 ms).
  The B/C/D stills show the same snowy plain, terrain ~60% of the frame, no holes or floating geometry. No frame time quoted (rule 10).
- Memory: the same 4 poses and flags as 93e (`MODE=poses EXTRA="-shells 1 -shellworkers 3 -gateres 1280 -shellwait 60 -poses
  eye_level_3,glide_400m,cliff_foot_1,peak_from_2km_ground -memledger -shellsdebug 60"`). Before = `+ -gpupool 0`
  (`Logs/tg2world_20261016_212529.txt`); after = the default (`Logs/tg2world_20261016_213600.txt`). Both on AC power.
  - GPU pool peak by pose: peak_from_2km_ground 186,451, **eye_level_3 203,450**, cliff_foot_1 160,274, glide_400m 0 (400 m up: the box
    holds only air). Refused 0. CPU->GPU table 4.3 MB (the amendment estimated ~2 MB; it is 750 k x (4 + 2) B).
  - Ledger TerrainClipmap row: **440.7 GPU + 75.1 managed -> 192.7 + 84.0 MB**. Ledger SUM 1,759.4 -> 1,520.0 MB.
  - Metal (footprint IOAccelerator graphics, at peak): **1,296 -> 1,041 MB**. phys_footprint median **2,672 -> 2,371 MB**, peak 2,683 -> 2,401.
  - These arms ran at the 242,000 cap. The final 254,400 cap adds 12,400 x 512 B = **+6.1 MB GPU** (arithmetic, not measured).
  - 8.15 §2 estimated the GPU mirror at ~660 MB before D2 -> ~266 MB after D2 + D3. Measured: 513 (before 93e) -> 193 MB here.
  - Presets: Low/Medium/High all draw the same 16^3 box (93e), and the CPU load radius (10) is the same on every preset, so High's
    measured cap holds for all three. Ultra (32x8x32) is chunk-count scaled (508.8 k): NOT measured.
- Validator in game: every pose `MIRROR lost 0 lag 0 stale 0`, `body mismatches 0` (bodies checked through the table, 4 chunks).
- Stills (`Screenshots/93f/{before,after}/`, one launch per arm):
  - Tier-0-only shots (A_off) are **pixel-identical at all 4 poses**. So is cliff_foot_1 B_on.
  - eye_level_3 B_on differs on 4.8% of pixels, glide_400m on 5.3%, peak_from_2km_ground on 0.01%. All of the difference is shell chunks
    that streamed in a different order within the 60 s wait. The after arm ran warm right after the before arm: at eye_level_3 it had
    s3 904 vs 1,213 stored. Tier 0 had nothing at glide_400m, so the table cannot be involved there.

## Not done / next
- The peak comes from teleport poses with 60 s waits. A long walk could peak higher; the ledger's `GPU brick pool` row and `refused`
  count will show it. If refusals ever appear, raise the cap.
- Next in the queue: 93g (CPU brick cap ledger).

## For the doc
Task 93f (engine 14d3, 8.15 row D3): with the shells on, the GPU keeps its own brick-body pool for the drawn 16^3 box + fluid
directory, instead of a copy sized to the whole CPU pool.
- GPU pool cap 254,400 bodies (124 MB) = measured peak 203,450 (eye_level_3) x 1.25; refused 0 (`Logs/tg2world_20261016_213600.txt`).
- TerrainClipmap GPU **440.7 -> 192.7 MB**; Metal **1,296 -> 1,041 MB**; process footprint median **2,672 -> 2,371 MB** (`Screenshots/93f/{before,after}/`).
- Validator through the table: lost/lag/stale 0, body mismatches 0 at all 4 poses. It does find an injected table swap (test).
- Shells off unchanged: Phase 7 19/19 byte-identical, PASS 37; acceptance Gates C/D green; SUITE=all PASS 860 FAIL 0.
Images:
- `Screenshots/93f/eye_level_3_sheet.jpg`: top row = beauty BEFORE | AFTER | changed pixels (white); bottom row = shell tint before | after | diff.
  The near ground (blue = tier 0, drawn from the new pool) is identical. The white patches are only on the far mountain, where the
  tint shows a shell-3 (orange) area before that was still a coarser grey shell after: streaming timing, not a missing brick.
- `Screenshots/93f/glide_400m_sheet.jpg`: same layout. Only shell-4 (red tint) patches differ. Tier 0 is empty at this height.
- `Screenshots/93f/acceptance_BCD.jpg`: Gate B (top left), C (top right), D (bottom left) and the last traverse still (bottom right): matching views, no holes.
