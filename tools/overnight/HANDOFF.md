# 53-engine-6-cascade-caps: DONE. tg2 cascade caps set from task 49's PeakUsed x 1.25: tier 1 320,000 -> 104,000, tier 2 96,000 -> 21,000. Ledger -285 MB, 0 exhaustion in poses/walk/poolprobe.

## What changed (branch `engine-phase`, commit b0e15ff)
- `Assets/ContentModules/Config/EngineConfig.cs`: `Tg2CascadeTierPoolCap` is no longer `2 x CascadeTierPoolCap`. It is a switch: tier 1 104,000 (82,641 x 1.25 = 103,301, rounded up), tier 2 21,000 (16,437 x 1.25 = 20,546). Other tiers keep 2x (there are none: `TIER_COUNT` = 3). The comment records the source numbers.
- No alignment rule applies. `BrickDataPool` takes any count, and the GPU body buffer is cap x 128 uints. No format or allocator change. The flat world's `CascadeTierPoolCap` (160k/48k) is unchanged.
- Note: `ENGINE_PHASE_PLAN.md` line 51 says "2x the measured need". This task file set 1.25x, and I followed the task file. The plan doc is not edited.

## What happens on exhaustion (today, unchanged)
`BrickDataPool.Alloc` throws `InvalidOperationException("BrickDataPool exhausted. LRU valve failed or cap is too low.")`. A cascade pool has no valve. The throw comes out of `StreamManager.DrainCompletions` -> `CascadeTierPool.SubmitPrecomputed` with no catch.
- Player.log shows the exception.
- The chunk is already in the store, but its coarse cell is not written. That leaves a stale or missing far-LOD cell.
- The rest of that frame's drain is aborted.
- Grep for "exhausted" in the run's tg2world log to check.

## Evidence (memory counts only; no timing, rule 10)
- Fast suite PASS 634 FAIL 0 SKIP 14 (`Logs/overnight/53_fast.out`).
- Runs: poses (`Logs/overnight/53_poses.out`, built here: build OK, 0 error CS), walk (`53_walk.out`), poolprobe (`53_pool.out`). "exhausted" count is 0 in all three player logs (`Logs/tg2world_20261010_152439.txt`, `_154030.txt`, `_154733.txt`).
- Ledger, LODCascadeManager GPU / native / managed: 221.1 / 210.8 / 46.1 -> 79.0 / 67.6 / 46.1 MB, so -285.3 MB. The task 49 estimate was about -200, which assumed 2x caps.

| run | tier1 peak / cap | tier2 peak / cap | phys_footprint peak (49 -> 53) |
|---|---|---|---|
| poses | 82,641 / 104,000 (79.5%) | 16,437 / 21,000 (78.3%) | 3,353 -> 3,077 MB (-276) |
| walk | 60,406 / 104,000 (58.1%) | 12,987 / 21,000 (61.8%) | 3,027 -> 2,869 MB (-158) |
| poolprobe (coast) | 76,151 / 104,000 (73.2%) | 16,045 / 21,000 (76.4%) | 3,085 -> 2,924 MB (-161) |

- Pose peaks are identical to task 49 (deterministic), so the headroom is exactly 1.25x of the worst measured pose.
- The phys_footprint drop is smaller than the ledger drop on walk/coast. Untouched pages of a 320k pool were never resident, so phys_footprint did not count them before either.
- Walk and coast now peak UNDER the 3 GB owner cap. Poses is still 77 MB over.
- Power went to battery mid-poses run (charger unplugged around 15:40). That doesn't matter for memory counts.
- Player-data folders of the 3 runs were deleted (81 MB) after the numbers were read. Disk is at 25 GB free.

## Screenshots looked at (copies in `Screenshots/53/`, 960 px, untracked)
- `Screenshots/53/peak_from_2km_ground_noon.png`: snowfield in front, green plain and brown far mountains across the middle. The far band is continuous to the horizon, with no holes or missing cells.
- `Screenshots/53/cliff_foot_1_noon.png`: sand slope and water on the left, brown cliffs behind. The terrain is complete, with no holes in the cascade-drawn middle distance.
- `eye_level_1_noon` was looked at and dropped because it is backlit and dark. It also showed no holes.

## Not done / next
- Follow-up named here, not queued (it is a sizing decision for the owner): the tier-0 brick pool `TG2_BRICK_POOL_CAP` = 750,000. It is not a cascade tier. Task 39/49 show it full on the coast (pool probe), so it may need to go UP, not down.
- Headroom is 1.25x of the poses run. A pose set or route that loads more dense coarse bricks (fly at altitude, a wide coast) could get close to it. If an "exhausted" line ever appears, raise these two numbers.
- Next queued: task 54 (engine step 5, awake fluid tiles).

## For the doc
- Cascade caps (tg2): tier 1 320,000 -> 104,000; tier 2 96,000 -> 21,000 (= worst measured peak x 1.25).
- Cascade memory (ledger, GPU+native): 431.9 -> 146.6 MB (-285 MB) (`Logs/overnight/53_poses.out` vs `49_poses.out`).
- phys_footprint peak: poses 3,353 -> 3,077 MB; walk 3,027 -> 2,869 MB; coast poolprobe 3,085 -> 2,924 MB (`Logs/overnight/53_{poses,walk,pool}.out`).
- Cascade exhaustion: 0 in all three runs (worst use 79.5% tier 1, 78.3% tier 2, poses).
- Images:
  - `Screenshots/53/peak_from_2km_ground_noon.png`: the far mountains across the middle are fully drawn after the cap cut, with no holes.
  - `Screenshots/53/cliff_foot_1_noon.png`: the cliffs and the lake on the left are complete. The cascade tiers are drawing normally.
