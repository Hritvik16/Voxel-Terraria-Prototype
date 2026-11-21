# 254-perf-fsr3-jitter-arm
Model: opus
Approval: SUPERVISOR 2026-11-20, follow-up task 238 names ("ask the owner whether to approve the one-line capture arm fsr3_j0 first: it is
the only test of the biggest number"). Recorded in the doc as "going ahead unless you object" (capture-only, reversible, no look change).
NEEDS AN IDLE MAC: parked in `later/`, released by `tools/overnight/release-when-idle.sh` after 30 min without input.
Decision table written BEFORE measuring (238's open number: `ups_none` saves 59.3 ms at forest_eye 16 deg, the upscale pass alone is ~5.6):
- `fsr3_j0` saves most of the 59 ms (>= ~40) -> FSR 3's jitter defeats the keyed temporal sun reuse. Next task: make the sun reuse
  jitter-proof (key on the un-jittered pixel / reproject), owner approves the design first.
- `fsr3_j0` saves little (< ~15) -> the 59 ms is something else in the FSR 3 path (its input resolution / history / dispatch).
  Next: split `ups_none` further (one more capture arm), not a fix.
- in between -> report both and stop for the owner.
Every outcome picks a different next step, so the measurement is worth running.

## Do
1. Add ONE capture-only cost arm `fsr3_j0` to `Assets/Game/PlaygroundCapture.CostSplit.cs`: it sets `RaymarchFeature.Fsr3ZeroJitter = true`
   for that arm and restores it after (the switch already exists, `RaymarchFeature.Upscalers.cs:53`; `-rowslook ... j0` already uses it).
   No product behaviour changes. Fast EditMode suite once before committing.
2. One discarded warm-up launch (`MODE=stream MOTION=walk ./run-tg2world.sh`), then ONE `./run-cost-split.sh` launch with 238's flags:
   `$PLAY_SETTINGS_FLAGS -quality high -renderrows 432 -shellwait 60 -startsun 16 -costfixed forest_eye -costtimedpaths forest_eye
   -costarms fsr3_j0,ups_none,reuse16`. Same per-arm idle check as 238 (`Logs/budget/hididle`, rerun an arm once if the owner touched the Mac).
3. Report: FULL / fsr3_j0 / ups_none / reuse16 p50 at forest_eye 16 deg, which decision row applies, memory vs FULL as usual.
## Rules
One code change (the arm). Commit the arm + the text report. `## For the doc`: the 4-row table and the chosen row; one image
(forest_eye with `fsr3_j0`, to show the frame looks the same apart from shimmer).
