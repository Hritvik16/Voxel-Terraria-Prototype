# 93g engine 14d4 (8.15 row D4) CPU brick cap + STEP 14 ledger line: DONE. Cap 750,000 -> 570,000; process peak 2,375 -> 2,362 MB

## What changed (commit f989de9, engine-phase)
- `EngineConfig.TG2_BRICK_POOL_CAP` 750,000 -> **570,000** = max(task 55's 379,527, this task's 358,892) x 1.5 = 569,291, rounded up
  to 10 k. New `EngineConfig.HighWaterBricksFor(cap)`: the §3.6 valve line stays at 0.85 x cap = 484,500. `BRICK_POOL_CAP` (rigs, 500,000) unchanged.
- `Phase4Bootstrapper.CpuBrickCap(tg2)`: the cap is a code constant (no `[SerializeField]`/scene override). `-cpubrickcap N` overrides it for one run.
  The ledger printed cap 570,000 in the after arm, which ran the new default with no flag, so the constant does take effect.
- `MemoryLedger`: `-memledger` ends with ONE line, `STEP 14 (8.15 D1-D4): CPU brick pool cap/peak (valve, MB) | GPU body pool cap/peak/refused |
  mirror box | tiers 1-2 allocated | process now/peak MB of the 3,000 MB cap`. The process figure is phys_footprint, read in-process
  through `proc_pid_rusage(RUSAGE_INFO_V4)`. I checked the offsets (72/240) with offsetof against the SDK header.
  Small fix: with the GPU pool, the `tier0 bricks` row now counts 512 B/unit, not 1024.
- `CpuBrickCapTests` (2): checks the cap value and its derivation, the valve at 0.85, and that the rigs' cap is unchanged. Pool-pressure test at the NEW cap (flat world,
  13x13 chunks): the store is pushed one chunk past 484,500 with player edits, and one frame later the valve has evicted the 2 coldest chunks (LRU; none in the camera's 5x5).
  Both were saved to D.1 before being dropped (ChunksSavedTotal), and all 8,192 edited voxels came back on reload.
- CLAUDE.md: a Task 93g line. Text reports: `Screenshots/93g/{walk_cap750k,poses_before_cap750k,poses_after_cap570k}.txt`.

## Evidence
- Tests: filter run 3/3 (`CpuBrickCapTests` + `DeferredEvictionTests`). **SUITE=all PASS 863 FAIL 0 SKIP 25** (`Logs/overnight/93g-chain.out`).
- `run-acceptance-rig.sh` `Phase4Acceptance/2026-10-17_002443`: **PASS 52 FAIL 1, Gates C and D green.** The FAIL is the known Gate E §4.3 upload-p99
  timing check (1.363 ms, red in 93f too). It is a flat world with BRICK_POOL_CAP, so this change does not reach it. No stills needed.
- Peak measurement, shells on, counts only:
  - one `MODE=walk` circle (150 m, `-shells 1 -shellworkers 3 -memledger`): CPU PeakUsed **358,892** (`Logs/tg2world_20261016_235314.txt`).
  - 93f's 4 poses: **330,792**. Both are below task 55's 379,527, so 379,527 sets the cap.
- Memory, same 4 poses and flags as 93e/93f (`-shells 1 -shellworkers 3 -gateres 1280 -shellwait 60 -poses eye_level_3,glide_400m,cliff_foot_1,
  peak_from_2km_ground -memledger -shellsdebug 60`), one build, AC power. Before = `-cpubrickcap 750000` (`Logs/tg2world_20261017_000434.txt`),
  after = default (`Logs/tg2world_20261017_001357.txt`):
  - ledger BrickDataPool **366.2 -> 278.3 MB** (-88 MB allocated); ledger SUM 1,526.2 -> 1,437.3 MB.
  - phys_footprint (`footprint` every 10 s): peak **2,375 -> 2,362 MB**, median 2,367 -> 2,331 MB. In-process lifetime peak 2,394.8 -> 2,381.2 MB.
  - **Why only 13-36 MB of the 88:** my guess, not verified, is that the pool pages above the peak are never written after the allocation zero-fills them,
    and macOS compresses zero pages to almost nothing, so they barely count in phys_footprint. The cap mostly cuts reserved/virtual memory.
    It also caps what building can fill before the valve fires (484,500).
  - Validator at all 4 poses in both arms: `MIRROR lost 0 lag 0 stale 0`, GPU pool refused 0, body mismatches 0.
- Step-14 total: task 85's **2,999 MB -> 2,362 MB** peak after D1-D4 (8.15 §9 estimated ~2,370 MB (est.); measured 2,362).

## Not done / for the supervisor
- **GPU body pool came close to full while walking:** peak **242,841 / 254,400 (95.5%)**, refused 0. 93f's teleport poses peaked at 203,450.
  It is not over the cap, but 93f's x 1.25 rested on poses only. If a longer walk or building shows refusals, raise `TG2_GPU_BRICK_CAP_16CUBE` (~+30 k = +15 MB).
- Next: step 16 (streaming milestone), then 8.15 row L1 (look-ahead). **L1 must re-check this cap**: its look-ahead lobe adds residency (8.15 §13.1).
  Gameplay consequence: none at today's peaks. Building inside the 128 m radius can add ~105 k dense bricks (~26 fully dense chunks) above the worst peak
  before the LRU valve starts evicting (it saves edits first). The old cap allowed ~258 k.

## For the doc
Task 93g (engine 14d4, 8.15 row D4): the tg2 CPU brick pool is now sized from a measured peak x 1.5. This was the last row of step 14.
- CPU brick cap **750,000 -> 570,000** bricks (366 -> 278 MB allocated); measured peak 379,527 (task 55; this walk 358,892, poses 330,792); valve line 484,500.
- Process peak (same 4 poses, shells on): **2,375 -> 2,362 MB**. Median 2,367 -> 2,331 MB. Most of the 88 MB was zero pages that were already almost free.
- Step 14 (D1-D4) total: **2,999 MB (task 85) -> 2,362 MB** vs 8.15 §9's ~2,370 MB estimate (`Logs/tg2world_20261017_001357.txt`).
- Ledger line (after arm): `STEP 14 (8.15 D1-D4): CPU brick pool cap 570,000 peak 330,792 (valve 484,500, 278.3 MB) | GPU body pool cap 254,400 peak
  203,450 refused 0 (124.2 MB) | mirror box 16x16x16 | tiers 1-2 allocated no | process now 2226.6 MB, peak 2381.2 MB of the 3,000 MB cap`
- Tests SUITE=all PASS 863 FAIL 0; acceptance Gates C/D green.
Images: none. Nothing visible changes, because the cap only bounds CPU memory and no pose came near the valve.
