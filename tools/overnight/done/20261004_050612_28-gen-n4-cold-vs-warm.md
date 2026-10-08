# 28-gen-n4-cold-vs-warm

Queued by task 27 as its one low-risk follow-up (evidence only, no code, no default change).
Requires: 27-gen-speed-state-thermal-or-ecore

## Why
Task 27: at N=1 the ~2.2x slow state appears only when the Mac is warm. A cold
N=1 run stayed fast for the whole 180 s walk. Task 25 found thread CPU time x2.1
from N=1 to N=4, but every one of its launches ran back to back, so warm. Is the
N=4 slowdown also heat, or does N=4 slow down even when cold? This decides
whether a capacity figure should be quoted cold or at thermal steady state.

## Do
- Reuse `Logs/overnight/27_driver.sh`. Copy it to `28_driver.sh` and set the
  measured arms to `-genworkers 4`. Keep the order W C C W: W = right after a
  `-genworkers 7` heater launch, C = after 600 s with no heavy process.
- Analyse with `python3 tools/streamprobe-onset.py` and `tools/streamprobe-pair.py`.
  Add task 27's C1 and C2 (N=1 cold: 20261004_042723, 20261004_044436) as the reference.
- Report per launch: CPU p50, speed factor, slow episodes with walk time, and capacity /s.

## Do not
Change the worker count default, priority/QoS, the generator or allocation.
