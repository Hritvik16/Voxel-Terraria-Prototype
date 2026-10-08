# 20-worker-contention-probe

Queued by task 16b as its one low-risk follow-up (evidence only, no default change).

## Why
16b's worst-case requirement (KNOWN_ISSUES "STREAMING REQUIREMENT") is short by
79-1,053x, and generation is the limiting stage. One chunk costs 49-65 ms on the
main thread with the workers idle, but 190-460 ms p50 (up to 3.2 s p99) on one of
7 workers. The fast mode (~200 ms) and slow mode (~400 ms) also alternate between
launches. Nobody knows why. Fix #1 in 16b's HANDOFF depends on this answer.

## Do
- Add a diagnostic `-genworkers N` argument that overrides
  `EngineConfig.CHUNK_GEN_WORKER_THREADS` for this launch only (default unchanged:
  0 = derived, 7 here). Log it in the streamprobe header.
- Run `MODE=stream MOTION=walk ./run-tg2world.sh` with `EXTRA="-genworkers N"` for
  N = 1, 2, 4, 7, 2 launches each, all on map v3.
- Report per N: gen+down p50/p99 per non-uniform chunk; capacity (N x 1000 / p50);
  admitted chunks/s; section 7's verdict line; frame p50/p95.
- Also record per launch: `pmset -g batt`, `pmset -g therm`, and the owner-pause NOTE.
- Classify the cause:
  - cost per chunk flat in N: no contention (the isolated figure is misleading);
  - cost per chunk rising with N but capacity still rising: shared-resource
    contention (memory bandwidth, allocation/GC, a lock);
  - capacity falling past some N: oversubscription (E-cores, Unity's job workers,
    `ThreadPool.SetMinThreads(ProcessorCount*2)`).
- Say what decides fast vs slow mode if the data shows it.

## Do not
Change the default worker count, thread priority/QoS, or any generator code.
Implementing a fix needs the owner's approval.
