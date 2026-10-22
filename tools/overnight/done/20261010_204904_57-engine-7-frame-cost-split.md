# 57-engine-7-frame-cost-split

Approval: owner, 2026-10-09 (engine plan Q36: ENGINE PLAN STEP 7, frame-time milestone 1). Its pass/fail is
already known RED (task 51, warm: 540-row options 16-23 fps = 43-62 ms on the walk; target 16.7 ms), so per
CLAUDE.md ("if every outcome leads to the same action, skip the measurement") the milestone becomes a COST
SPLIT that decides step 8's ONE cost change. Supervisor default posted as Q41 in the Queue decisions doc
2026-10-10 ("going ahead unless you object"). Queued by the supervisor 2026-10-10. Branch `engine-phase`.
This IS a performance task: PREAMBLE rule 9 (warm steady state) applies.

## Goal
Answer: where do the ~45-60 ms of a play frame go at the play default (FSR 3 at 540 rows, pinned:
`-upscaler fsr3 -renderrows 540`, so rows do not move between arms), on the warm walk (`MODE=walk`)?
CLAUDE.md "isolate the cause before proposing a fix": switch off ONE thing per arm and measure the frame
time it saves. No GPU-stage capture exists in this workflow; end-to-end deltas are the method.

## Arms (one thing off each, against FULL; ABBA-interleave each against FULL, warm-up launch first)
Use existing flags/keys where they exist; add a capture-only flag only where a pass has none (no default
changes, no look changes in play):
1. FULL (play default, 540 rows pinned).
2. Sun shadows off (or the cheapest rung) -- the plan's "~10 ms of 32" guess (2026-10-01 split) to confirm.
3. Point-light / torch shadow rays off (task 46).
4. Far band off (if a flag exists).
5. Cascade tiers 1-2 off / voxels only to tier 0 (if a flag exists).
6. Fluid simulation paused.
7. Upscaler `none` at 540 (the FSR 3 pass cost).
8. HUD/minimap hidden.
If the eight arms do not fit in one session's budget, run 1, 2, 3, 7 first and name the rest as not run.
Also print CPU main-thread ms vs total frame ms if the rig has both (CPU-bound or GPU-bound?).

## Report
A table: arm, p50 / p99 ms warm, saved ms vs FULL (with the spread between the two FULL runs as the noise
floor), log paths, warm-up log path. Rank what to cut. No code change beyond capture-only flags.
End with `## For the doc`: the table's top 4 lines, CPU- vs GPU-bound, and one still of the walk pose at
FULL (midday, sun behind camera).
