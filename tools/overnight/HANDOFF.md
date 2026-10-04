Owner, still pending from earlier tasks: approve 26 (eviction retry) and 22 (coast bar) by deleting `OWNER-APPROVES-26` / `OWNER-APPROVES-22` from their Requires lines. Task 23's plan decisions are in its commit 4fecdf6 / `PLAN_COARSE_FIRST_GEN.md`.

# 25-gen-contention-attribution: DONE. Workers mostly RUN SLOWER (thread CPU time x2.1 from 1 to 4 workers, flat to 7). Descheduling adds ~1.35x on top at 7 workers. GC is negligible.

## What changed
- `StreamTrace` / `StreamManager.GenerateOnWorker`, trace-only (off unless the streamprobe runs). The probe now records the worker's thread CPU time for generate and downsample, via `clock_gettime_nsec_np(CLOCK_THREAD_CPUTIME_ID)` (P/Invoke into libSystem; it works under IL2CPP). It also records how many GC collections started during each chunk's work.
- The streamprobe report gives CPU, wall-CPU and wall/CPU per chunk, plus the walk's GC counts, the mono heap and one timed `GC.Collect()` after the walk. The TSV gains `genCpuMs downCpuMs gcN`.
- Commit a909953 (code). Second commit: `tools/streamprobe-pair.py` pairs the CPU columns; a KNOWN_ISSUES sub-bullet under "Why a worker chunk costs 3x"; queue task 27.
- Not available: cumulative allocated bytes (`GC.GetTotalAllocatedBytes` is not in Unity's API profile).

## Evidence
- Fast suite: PASS 611 FAIL 0 SKIP 12 (`Logs/overnight/25_suite_fast.out`).
- 6 launches: `MODE=stream MOTION=walk EXTRA="-genworkers N"`, N = 1,4,7,7,4,1, map v3. All on AC at 100%, no owner pause, no slow mode. Driver: `Logs/overnight/25_driver.{sh,out}`. Runs 20261004_031133 / 032327 / 032921 / 033500 / 034041 / 034625.
- Paired results, 971 non-uniform chunks common to all six launches (`Logs/overnight/25_pair.out`):

| N | wall p50 | thread CPU p50 | wall-CPU p50 | wall/CPU p50 | capacity /s |
|---|---|---|---|---|---|
| 1 | 72 / 62 | 70 / 61 | 0.9 / 0.3 | 1.01 / 1.00 | 7.9 / 10.9 |
| 4 | 140 / 181 | 131 / 139 | 7.8 / 35 | 1.06 / 1.25 | 35.5 / 20.0 |
| 7 | 191 / 177 | 136 / 135 | 54 / 43 | 1.39 / 1.33 | 37.8 / 36.2 |

- **GC: ruled out.**
  - 13-16 collections per 180 s walk at every N (gen0 = gen1 = gen2, since Boehm is not generational; incremental is on).
  - A forced full collection pauses for 3-10 ms.
  - Only 1-2.6% of chunks span a collection. Even 16 x 10 ms is under 0.1% of the walk.
- **Classification per N:**
  - N=1: wall = CPU (not descheduled).
  - N=4: CPU x2.1 (slower while running), plus a run-dependent descheduling gap. Run B had 35 ms and capacity 20 /s vs run A's 35.5. Task 20 also saw ~17-19 /s at N=4, so A may be the outlier.
  - N=7: CPU flat vs N=4. The extra wall time is descheduling: 7 threads x 61% CPU share is about 4.3 cores' worth.
- **The slow state is a ~2.2x per-thread speed step, not gradual sharing** (`Logs/overnight/25_modes.out`).
  - In both N=1 launches the worker switches to it about halfway through the walk. The CPU-time deciles go from 55 ms to 145-195 ms.
  - Chunks that were slow at N=1 cost the same at N=4 (ratio 0.93). Chunks that were fast at N=1 slow down x2.19.
  - The content difference between the two groups is only ~1.3x (both measured at N=4).
  - So it is a machine speed state, not worker-vs-worker cache or bandwidth sharing, which a single worker cannot cause.
  - What remains is **efficiency-core placement or thermal frequency.** This machine is an M1 MacBook Air: fanless, 4 performance + 4 efficiency cores. The capacity plateau at about 4 cores fits either explanation.
- No screenshots: this task was numbers-only, so none were looked at.

## Unfinished / next
- Efficiency cores vs thermal is not separated. Queued **27** (evidence only): a worker-start-time column, plus N=1 cold-vs-warm launches in ABBA order. If the onset comes earlier when warm, the cause is thermal.
- A direct efficiency-core test would set the workers' QoS (`pthread_set_qos_class_self_np`) behind a flag. That changes priority, so it needs the owner's OK. It is not queued.
- Peak memory (MB): N=1 2,990 / 2,969; N=4 2,964 / 3,195; N=7 3,215 / 3,239. The 3 GB cap is exceeded at N=7 (a known issue).
