# 27-gen-speed-state-thermal-or-ecore

Queued by task 25 as its one low-risk follow-up (evidence only, no default change).
Requires: 25-gen-contention-attribution

## Why
Task 25: a chunk-gen worker has two speeds, ~2.2x apart in THREAD CPU time.
N>=4 workers run in the slow state the whole walk. At N=1 the single worker
switches to it about halfway through the walk. The cause is either efficiency-core
placement or thermal frequency (fanless M1 Air), and the fix differs: a QoS/priority
change vs a compute/heat budget.

## Do
- Trace-only: add each chunk's worker-start time since launch (s) to
  `streamprobe_chunks.tsv`. Use the existing `tWorkerDone` minus wall gen+down;
  there is no new clock.
- Thermal test, no code beyond that column: run
  `MODE=stream MOTION=walk EXTRA="-genworkers 1" ./run-tg2world.sh`
  (a) COLD: after 10 min with no Unity process, and
  (b) WARM: right after a `-genworkers 7` launch.
  Do this twice, in ABBA order. If the slow-state onset (the CPU-time decile step)
  comes earlier when warm, the cause is thermal. If it is tied to the walk's
  position or chunk content regardless, it is not.
- Report the onset time and the walk position per launch. Pair chunks with
  `tools/streamprobe-pair.py`.

## Do not
Change worker count, priority/QoS, the generator or allocation. A QoS test
(`pthread_set_qos_class_self_np` on the workers) needs the owner's approval first.
