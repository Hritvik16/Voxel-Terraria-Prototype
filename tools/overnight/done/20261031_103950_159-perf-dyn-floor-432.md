# 159-perf-dyn-floor-432

Model: opus

Approval: owner, 2026-10-31, doc Q100 (a): FSR 3 dyn 432-540 with a ~30 ms target (below the 2026-10-01 Q38 540-row minimum, owner's
call). `PERF_30FPS_PLAN.md` step 3. A PERFORMANCE task, ONE change.

## Do
In play's dyn path only: the dyn floor goes to 432 rows and `UpscalerSelect.DynTargetMs` to ~30 ms, so rows drop only on heavy views and
stay at 540 on light ones (task 156's `CaptureMinRows` and capture flags unchanged; rigs unchanged). Keep the old behaviour one flag away
(e.g. `-dynfloor 540`). PlaySettings / F6: the dyn label says its range. Fast suite once.
Measurement: warm (one discarded warm-up), `MODE=playwalk` ABBA old vs new, per-half-minute window means (task 150: whole-run p50 spreads
42.4-47.2 ms on one build): walk p50, p95, % frames > 33 ms, and the share of frames at each row count. Plus 10 s motion clips or
burst stills at walk_open in both arms (FSR 3 shimmer at 2.5x shows only in motion; task 156 had stills only).

## Decision table
- walk p50 drops >= 4 ms and the motion check shows no new shimmer/ghosting: ship as the play default.
- gain < 4 ms, or rows rarely leave 540: keep it behind the flag, report why (the dyn controller may need tuning; say how).
- visible shimmer in motion: keep OFF, STOP_FOR_OWNER with the clips.

## Deliverable
HANDOFF + "For the doc" (numbers + motion stills with captions naming what to look at); TASKS.md paragraph.
