Owner, still pending from earlier tasks: approve 26 (eviction retry) and 22 (coast bar) by deleting `OWNER-APPROVES-26` / `OWNER-APPROVES-22` from their Requires lines. Task 23's plan decisions are in its commit 4fecdf6 / `PLAN_COARSE_FIRST_GEN.md`.

# 27-gen-speed-state-thermal-or-ecore: DONE. The slow state is triggered by HEAT. Cold N=1 launches never slowed in 180 s. Warm ones slowed from walk +45 / +60 s.

## What changed
- ca4594f (trace-only): `streamprobe_chunks.tsv` gains `workStartS`, the worker's start on the chunk in seconds since launch. It is computed as `tWorkerDone` minus wall gen+down, with the Stopwatch re-zeroed on `realtimeSinceStartup`; no new clock. `streamprobe.txt` prints `walk start X s since launch` on the same scale.
- Second commit: `tools/streamprobe-onset.py` (new). It bins chunks by walk time, gives CPU p50 and slow share (> 100 ms) per bin, and a content-normalised speed factor (a chunk's CPU / its fastest CPU across the launches). It also adds a KNOWN_ISSUES sub-bullet under task 25's attribution and queues task 28.

## Evidence
- Fast suite before the commit: PASS 611 FAIL 0 SKIP 12 (`Logs/overnight/27_suite_fast.out`).
- Driver `Logs/overnight/27_driver.{sh,out}`, ran 04:02-05:04. Order W C C W (balanced, the same as ABBA). W = right after a `-genworkers 7` heater launch. C = after 600 s with zero heavy processes (the idle clock never had to restart). All on AC at 100%, no owner pause. `pmset -g therm` recorded no warning level at any point.
- Every arm: `MODE=stream MOTION=walk EXTRA="-genworkers 1"`, map v3, 5 m/s, r 150 m circle. The analysis is in `Logs/overnight/27_onset.out` and `27_pair.out`.

| arm | run | walk start (s after launch) | chunks | paired CPU p50 ms | speed factor p50 | slow (> 1.6x) | slow onset |
|---|---|---|---|---|---|---|---|
| W1 | 040936 | 186.8 | 1,331 | 85.3 | 1.66 | 54% | walk +45 s = 232 s after launch; camera 225 m / 86 deg round the circle |
| C1 | 042723 | 155.9 | 2,240 | 54.5 | 1.04 | 1% | none in 180 s |
| C2 | 044436 | 155.9 | 2,382 | 52.9 | 1.00 | 0% | none in 180 s |
| W2 | 045715 | 170.1 | 1,778 | 62.7 | 1.21 | 21% | first episode +60 s (44%) = 300 m / 115 deg; sustained from +135 s (48/82/69/46%) = 675 m / 258 deg |

- Paired = the 1,261 non-uniform chunks common to all 4 launches. Wall/CPU stays 1.00-1.01 in every arm, so the worker is not descheduled. It runs slower.
- The slow state comes and goes. W1 is slow at +60 to 90 s, fast at +105 to 120 s, slow again at +135 s.
- Both warm arms were slow at +60 s and at +135 s. Main-thread or GPU load at those points on the walk may tip an already warm chip over. Neither cold arm slowed there (factor 1.00 to 1.06 in every bin). So chunk content alone does not explain it, and neither does the walk position alone.
- W2 was milder than W1. HEAT2 ran 5.5 min, while HEAT1 ran 7.4 min including the build.
- **Conclusion: thermal-triggered.** The onset depends on how warm the machine is, not on the walk or the content.
- **Not separated:** whether "slow" means a P-core frequency cap or the scheduler moving the thread to an efficiency core. A ~2.2x step fits E-core speed better than a typical M1 Air P-core throttle. That is inference, not measured.
- **Consequence:** the capacity figures from tasks 20 and 25 were all measured warm, because those launches ran back to back. Cold N=1 delivers 2,240-2,382 chunks per walk, against 1,331-1,778 warm.
- No screenshots were looked at (a numbers-only task).
- Peak memory, N=1: 2,997-3,030 MB. The 7-worker heaters peaked at 3,253 / 3,158 MB, over the 3 GB cap (a known issue).

## Unfinished / next
- Queued **28** (evidence only): the same W C C W design at `-genworkers 4`. It tests whether task 25's x2.1 CPU at N=4 is also heat, or happens even cold.
- Owner decision, not queued: any capacity or requirement figure (`StreamingRequirement`, section 7) should be quoted at thermal steady state on this fanless M1 Air, not cold. A QoS test (`pthread_set_qos_class_self_np` on the workers) would separate E-core placement from frequency. It still needs your approval.
