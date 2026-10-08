# 25-gen-contention-attribution (renumbered from 24 by the supervisor: 24 was taken)

Queued by task 20 as its one low-risk follow-up (evidence only, no default change).
Requires: 20-worker-contention-probe

## Why
Task 20: a non-uniform chunk's gen time on a worker rises from ~70 ms (1 worker,
= the isolated figure) to ~165-185 ms (4 workers), then stays flat to 7 workers,
while capacity keeps rising. That is shared-resource contention, but WHICH
resource is unknown, and the fix differs per cause.

## Do
- Diagnostic only, in the streamprobe's per-chunk record: also record the worker
  thread's CPU time for the generate call (macOS `thread_info`/`clock_gettime(
  CLOCK_THREAD_CPUTIME_ID)` via P/Invoke; or `Thread.CurrentThread` user time if
  available) next to the existing wall-clock genMs, and `GC.CollectionCount(0..)`
  plus total GC'd bytes at start/end of the walk.
- Run `MODE=stream MOTION=walk EXTRA="-genworkers N" ./run-tg2world.sh` for N = 1, 4, 7,
  2 launches each, ABBA order, map v3. Pair chunks by (cx,cy,cz)
  (`tools/streamprobe-pair.py`).
- Classify per N: wall >> CPU = descheduled (core competition); CPU rises with N =
  slower while running (bandwidth / frequency / cache); GC count x pause explains
  the gap = GC.

## Do not
Change the worker count, priorities, the generator, or allocation behaviour.
