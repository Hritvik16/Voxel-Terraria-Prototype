Owner (carried over from 19): to fix the coast black bar, approve task 22 by deleting `OWNER-APPROVES-22` from the Requires line in `tools/overnight/queue/22-farstartt-integer-dda.md`.

# 20-worker-contention-probe: DONE. Cause class: shared-resource CONTENTION. Per-chunk cost rises from 1 to 4 workers, then stays flat to 7. Capacity keeps rising, so there is no oversubscription up to 7. Default unchanged.

## What changed (surface-rebuild 76a717a)
- `-genworkers N` is diagnostic only. It sets `StreamManager.GenWorkersOverride` (set in `Phase4Bootstrapper`) and overrides `CHUNK_GEN_WORKER_THREADS` for that launch. Without it the derivation is unchanged (7 workers here).
- The streamprobe header now says `N chunk-gen workers (-genworkers override|default, ProcessorCount 8)`.
- New `tools/streamprobe-pair.py`: per-launch non-uniform gen+down p50/p99, plus the same chunks paired across launches by (cx,cy,cz).
- KNOWN_ISSUES has a new sub-bullet under STREAMING REQUIREMENT. Follow-up `24-gen-contention-attribution` is queued (evidence only).

## Evidence
- Fast suite: **PASS 611 FAIL 0 SKIP 12** (`Logs/overnight/20_suite_fast.out`).
- Runs: 8 x `MODE=stream MOTION=walk EXTRA="-genworkers N" ./run-tg2world.sh`, all on map v3, one build. ABBA order (7,4,2,1,1,2,4,7). Driver: `Logs/overnight/20_driver.out`.
- Machine state for all 8 launches:
  - AC power, 100% charged.
  - `pmset -g therm`: no thermal, performance or CPU-power warning recorded, before or after any launch.
  - No owner-pause NOTE.
  - Swap ~5.0 GB used throughout (`Logs/overnight/20_swap.txt`).

| N | launch (Logs/tg2world_20261004_*) | gen+down p50/p99 ms (non-uniform) | capacity N*1000/p50 /s | admitted /s | section 7 verdict | frame p50/p95 ms |
|---|---|---|---|---|---|---|
| 7 | 003351 / 012052 | 210/386, 239/514 | 33.2 / 29.2 | 57.0 / 45.7 | NOT MET, short 68.9x / 78.3x | 10.6/21.2, 10.5/21.0 |
| 4 | 004116 / 011459 | 211/386, 236/449 | 19.0 / 16.9 | 31.4 / 25.8 | NOT MET, 120.6x / 135.1x | 10.5/20.8, 10.1/20.3 |
| 2 | 004714 / 010830 | 152/318, 191/338 | 13.2 / 10.5 | 19.1 / 16.3 | NOT MET, 173.2x / 218.7x | 9.4/19.8, 9.8/19.3 |
| 1 | 005336 / 010102 | 83/273, 101/291 | 12.0 / 9.9 | 12.0 / 10.6 | NOT MET, 190.1x / 231.1x | 8.6/18.9, 9.3/19.4 |

- **Same-chunk paired gen p50** removes the chunk-mix confound (fewer workers generate a different subset). 1,245 chunks are common to all 8 launches (`Logs/overnight/20_pair.out`):
  - N=1: 70 / 82 ms
  - N=2: 122 / 148 ms
  - N=4: 167 / 185 ms
  - N=7: 164 / 187 ms
- The isolated figure (main thread, workers idle) was 56-70 ms in every launch. It is right for 1 worker, so it is not "misleading". It just does not apply when several workers run.
- **Classification:**
  - Cost per chunk rises with N, about 2.4x from 1 to 4, while capacity still rises. By the task's rule that is shared-resource contention.
  - From 4 to 7 the cost is flat and capacity rises 1.7x for 1.75x the threads, so extra threads past 4 are almost free.
  - Capacity never falls, so there is no oversubscription up to 7.
  - Fewer workers make frames slightly smoother: frame p50 ~1-2 ms lower at N=1 than at N=7.
- **Not separated:** which resource it is. Candidates: Mono GC stop-the-world, memory bandwidth, P-cores shared with the main and render threads, or descheduling. That is task 24 (worker thread CPU time vs wall time, plus GC counts).
- **Drift:** the second pass was 10-20% slower than the first at EVERY N (paired p50 +12 to +25 ms), with no thermal warning. Arms measured hours apart are not directly comparable to each other.
- **Fast vs slow mode:** slow mode (7-worker gen p50 416-503 ms) did NOT appear in any of the 8 launches.
  - In the logs it was 3 consecutive launches on 2026-10-03 between 20:15 and 21:57. Everything before and after was fast. So it comes in a time cluster, not alternating per launch.
  - It does not follow charging state (both charging and charged runs were slow).
  - Swap is not the trigger: today's fast runs had 5 GB swapped.
  - The isolated main-thread cost stayed normal in 2 of the 3 slow runs (48 and 62 ms; one was 143). So whatever it is hits the multi-worker case, not single-core speed.
  - The likeliest cause is an external or system load in that window. **Unproven.**
- Screenshots: none looked at. This task measures generation timing only, and the task did not ask for images.

## Left / next
- 24 (attribution) runs after 21. The queue order is unchanged: 21, then 22 (owner-gated), 23, 24.
- For 16b's fix #1: adding workers still helps up to 7, but each one past 1 costs 2-2.7x per chunk. Even 7 perfectly isolated workers (~121/s) would be 19x short of 2,285/s, so the worker count alone cannot meet the requirement.
- `Assets/Scenes/Playground.unity` is a build side effect, left as found.
