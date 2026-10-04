Owner, still pending from earlier tasks: approve 26 (eviction retry) and 22 (coast bar) by deleting `OWNER-APPROVES-26` / `OWNER-APPROVES-22` from their Requires lines. Task 23's plan decisions are in its commit 4fecdf6 / `PLAN_COARSE_FIRST_GEN.md`.

# 28-gen-n4-cold-vs-warm: DONE. At N=4 the slow state comes even when the Mac starts cold. Four workers heat the chip themselves within about 100 s.

## What changed
- No code. One new `KNOWN_ISSUES.md` sub-bullet under task 25/27's attribution, plus this handoff (one commit, see `git log`).
- Driver `Logs/overnight/28_driver.sh` is a copy of 27's with the measured arms at `-genworkers 4`. It ran 05:06-06:01 in the order HEAT1(+build) W1, idle, C1, idle, C2, HEAT2, W2. Both idles reached 600 s clear without restarting the clock.

## Evidence
- Fast suite: PASS 611 FAIL 0 SKIP 12 (`Logs/overnight/28_suite_fast.out`).
- Every arm ran on AC at 100%. No owner-pause NOTE appeared, and `pmset -g therm` recorded no warning level before or after any arm.
- Analysis files: `Logs/overnight/28_onset.out` and `28_pair.out`. Task 27's cold N=1 launches C1n1 (042723) and C2n1 (044436) are included as the reference.
- Paired = the 1,852 non-uniform chunks common to all 6 launches. "Factor" is the paired speed factor, a chunk's CPU divided by its fastest CPU across the launches. Capacity is the report's `admitted N in 180 s`.

| arm | run | N | walk start (s after launch) | paired CPU p50 ms | factor p50 | slow (>1.6x) | slow episodes (walk time) | capacity /s | wall/CPU |
|---|---|---|---|---|---|---|---|---|---|
| C1n1 (ref) | 042723 | 1 | 155.9 | 55.4 | 1.04 | 1% | none | 19.8 | 1.01 |
| C2n1 (ref) | 044436 | 1 | 155.9 | 53.8 | 1.00 | 0% | none | 20.2 | 1.00 |
| W1 | 051256 | 4 | 82.8 | 152.6 | 2.89 | 100% | from +0 s, the whole walk | 18.0 | **1.51** |
| C1 | 052859 | 4 | 54.5 | 126.5 | 2.17 | 79% | from +45 s (= 100 s after launch) to the end | 39.4 | 1.09 |
| C2 | 054435 | 4 | 56.0 | 105.2 | 1.82 | 69% | from +45 s (= 101 s after launch) to the end | 50.2 | 1.03 |
| W2 | 055530 | 4 | 77.2 | 135.8 | 2.55 | 100% | from +0 s, the whole walk | 36.9 | 1.07 |

- **Cold N=4 is never as fast as cold N=1, even before it slows.** Per-chunk CPU p50 over walk 0-45 s was 65-92 ms (about 1.3x N=1's 46-50 ms). From walk +45 s it rose to 104-157 ms, the same level as the warm arms.
- **Capacity in the cold arms:** 50-70 chunks/s in the first 30 s, 23-34/s in the slow middle (from the report's 10 s bins).
- **Conclusion:** the x2.1 thread CPU that task 25 found at N=4 is mainly heat, but N=4 produces that heat on its own. A cold start only delays the slowdown by roughly the first 45 s of the walk.
  - The capacity to quote for N=4 on this M1 Air is the thermal steady state: about 23-37 /s, not the cold 50-70 /s.
  - Even so, N=4 roughly doubles N=1's 20 /s.
- **W1 is the least trustworthy arm.** On top of running slow, it was descheduled: wall/CPU 1.51, CPU share of wall 54%. The other N=4 arms were at 1.03-1.09. W1 ran straight after the player build, so this is probably background load after the build (for example, indexing). That is not verified. W1's 18.0 /s should not be quoted.
- **Not separated:** in both cold arms, "about 100 s since launch" and "walk +45 s" happen at the same moment. Task 27's warm N=1 W1 also tipped over at walk +45 s. So a load increase at that point of the walk may be what triggers the slowdown.
  - Telling these apart needs a walk-delay option, which does not exist (it would mean a small trace-only change to the harness).
  - I did not queue it: it would not change the conclusion that capacity should be quoted at steady state.
- Peak memory was 3,135-3,234 MB in every launch, over the 3 GB cap (a known issue).
- No screenshots were looked at (a numbers-only task).

## Unfinished / next
- Nothing is queued from this task.
- Owner decisions carried from 27, still open:
  - (a) Quote `StreamingRequirement` section 7 capacity at thermal steady state. This task shows that holds for N=4 too.
  - (b) A QoS test (`pthread_set_qos_class_self_np`) to tell E-core placement apart from a frequency cap. It needs your approval because it changes performance.
- Optional, if you want the trigger pinned down: add a `-walkdelay` option and run a cold N=4 launch with the walk held for 120 s. That separates heat building up since launch from load at walk +45 s.
