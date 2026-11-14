# 209-perf-t0-measure-play-steps

Model: opus

Approval: OWNER, 2026-11-14, Queue decisions doc Q-AC answer "a for all (recommended)": Q1 (a) this Mac's preset drops nothing
play draws today; Q2 (a) start the split: T0, then the steps one by one, reflections after; Q3 (a) every step pixel-exact, and a
+-1-level change stops for the owner; Q4 (a) full look for Apple Silicon Pro / Max first; Q5 (a) cheap mirrored-land lighting;
Q6 (a) water, ice and metal-ore glint first; Q7 (a) clouds + haze + bloom after reflections.
This is a PERFORMANCE task (measuring is allowed and required here).

## First: adopt amendment 8.17
- `AMENDMENT_8_17_RENDER_STEPS.md`: change the status line to `ADOPTED 2026-11-14 (owner, Q-AC: (a) for Q1-Q7)` and record the
  seven answers under section 7.
- `CLAUDE.md` "Ground truth" item 2: append `-> 8.17 (render steps and frame budget; AMENDMENT_8_17_RENDER_STEPS.md, adopted
  2026-11-14)`. One line, nothing else in CLAUDE.md. Commit these two by path, on their own.

## Then: T0 (8.17 section 4, row T0)
Measure TODAY's play, with play's own flags (torch flood ON, sky fill 2, FSR 3 dynamic as `run-play.sh` sets them, rows fixed at
432 for comparability with 8.17 section 3), at walk_near and one heavy cliff view (199's cliff1). Arms: play (all on), flood_off
(`-torchflood 0`), skyfill 0, sun_off, shells_off, and the ceiling arm 8.17 names. Plus one GPU trace if the 8.17 section 3 tool
(`tools/gpu161.py`) gives per-step ms.
- Measurement discipline (CLAUDE.md): first ONE warm-up launch, numbers discarded (`MODE=stream MOTION=walk ./run-tg2world.sh`),
  then the arms back to back, interleaved (ABBA against the play arm). Report "warm steady state" and the warm-up's log path.
  The Mac must be idle (timing task).
- What each result makes us do (8.17 checkpoint): flood twin (play minus flood_off) >= 3 ms -> T2 is next; sky fill (play minus
  skyfill 0) >= 4 ms -> T4 before T3; otherwise T1 is next. Write which one is next in the HANDOFF.
- Also confirm or refute 199's code-reading finding: play draws the flood twin `CSMainFarShadeFlood`, which bypasses diet 2
  (BUGS 199). Report the ms.
- No code change in this task except a measurement flag if one is truly missing (then behind a flag, default off, fast suite).

## End
Done with HANDOFF: a table of the arms (p50 / p95 ms, warm), the next step (T1 / T2 / T4) with its reason, and `## For the doc`.
No pictures needed (measurement only), but name the log paths.
