# 110-perf-frame-budget-split

Requires: 109-merge-engine-phase-main
Model: sonnet

Approval: owner, 2026-10-22 in chat: before populating the world, make performance, lighting, world gen and visuals
good, starting with performance; "stable 30 fps" is the goal on this Mac (60 is not reachable here). This is a
PERFORMANCE task, so measuring is its purpose (CLAUDE.md "Measure performance only in performance tasks").
Also the Sonnet trial (owner 2026-10-22: supervisor picks models): do it exactly as written, no extra arms.

## Why, and what each result does (decided before measuring)
Where does a play frame's time go today, with shells ON (play's default since 93c)? The result feeds task 111,
a "30 fps plan" that ranks cuts by ms saved and visual cost. Every outcome leads to 111; the numbers set its order.
Last data (Oct 17, `Screenshots/93b/rerun/perf93b_rerun.txt`, before task 99): steady ON p50 66.0 / 48.0 ms
(walk_near / walk_open), moving p50 42 ms with 79% of frames > 33 ms; GPU-bound; shell march ~16-18 ms.

## Arms (code unchanged; ONE warm-up launch, then all arms back to back, no idle gap > 2 min)
Setting = play's default, pinned as 93b's re-run did: `-upscaler fsr3 -renderrows 540`, quality preset, far band +
far shadows, `-farshade 1 -seethrough 1 -riverlook 1`, noon, plus `-shells 1 -shellworkers 3`. Copy the exact flags
from `Logs/overnight/93b-rerun-driver.sh` (its ON arm); write your own driver beside it as
`Logs/overnight/110-driver.sh`.
1. **Steady split:** `run-cost-split.sh` at walk_near and walk_open (streaming pinned, shell fill wait as 93b's
   re-run: 120 s), with EVERY cost arm `PlaygroundCapture.CostSplit.cs` offers that applies with shells on (list them
   from the code; e.g. shells_off, far_off, fshade_off, shadow arms, fluid, see-through, sky exit). Two ON launches
   (the second = noise check). Table: arm, saved ms vs FULL, both launches, both poses.
2. **Moving:** `MODE=walk` circle, 3 min, `-tod 12 -memledger -frametiming`, two launches: p50 / p99, share of frames
   > 33 ms, peak MB.
3. **Resolution sensitivity (one ON launch, steady split only):** FULL at `-renderrows 720` and at 360-or-lowest
   supported rows, so 111 knows how much of the frame scales with pixels.
`pmset -g batt` before every launch: AC only, else wait (battery arms were wrong before). Owner pause note -> rerun
that arm. Shared Unity lock for every launch. No code changes, no new arms in code.

## Report (HANDOFF)
Warm-up log path. Tables 1-3 with log paths. One line: CPU- or GPU-bound, and the 5 largest costs in ms (pose, arm).
One still per pose at FULL (midday, sun behind the camera), captioned with what to look at. Do not propose fixes
beyond naming the 5 largest costs; 111 does that.
