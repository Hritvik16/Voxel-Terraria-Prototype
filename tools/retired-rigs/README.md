# tools/retired-rigs/ -- rigs retired by task 149 (RIG_REFRESH_PLAN.md R1, owner Q1 (a))

Moved here with `git mv` on 2026-10-25, not deleted. The C# capture modes, `-executeMethod` build entry points and
scenes they call are untouched, so each still runs: launch it FROM THE REPO ROOT as `tools/retired-rigs/run-x.sh`
(none uses `dirname "$0"`; all assume cwd = the repo root). None takes the shared Unity lock itself: wrap it,
`tools/with-unity-lock.sh tools/retired-rigs/run-x.sh`. To revive one, `git mv` it back and give it the lock.
"Last" = the newest report/log found by task 144 (RIG_REFRESH_PLAN.md section 3).

| Script | Why retired | Last |
|---|---|---|
| run-phase5b-check.sh | Iteration helper (sand_column only) for a finished 5b fix; run-phase5b-rig.sh covers it. | 2026-09 |
| run-phase6-sandbox.sh | Builds the old DENSE simulation; superseded by run-phase6-combined.sh. | 2026-09-11 |
| run-fluid-activity.sh | Duplicated by fluid-tiled + combined; its header is a copy of sandbox's. | 2026-09-12 |
| run-perf60.sh | 60 fps target arms; the target is now 30 fps on tg2 (task 146) and the presets changed. | 2026-09-26 |
| run-fluid-benchmark.sh | 5b arena fluid perf gate; fluid cost is measured by tg2 fluidprobe / run-acceptance-fluid.sh. | 2026-09 |
| run-fluid-ab.sh | Dense vs tiled wall clock; closed (tiled shipped). | 2026-09 |
| run-fluid-scale.sh | Dense-slot era counters; pool memory is in tg2 `MODE=poolprobe`. | 2026-09-12 |
| run-fluid-chaos.sh | Arena fluid-volume ladder, legacy motor; revive only as a gameplay-stack stress arm. | 2026-09-10 |
| run-phase6-qa.sh | One-off per-system perf + visual QA session. | 2026-09-11 |
| run-fixes.sh | 2026-09-25 visual fixes A/B; fixes shipped. | 2026-09-25 |
| run-fluid-stagger.sh | Staggered-drop diagnosis; closed. | 2026-09-11 |
| run-fluid-tilecap.sh | Tile cap ladder; cap decided (1,024 rigs / 480 play). | 2026-09-11 |
| run-fluidtick-diag.sh | Why a CA tick took 6-7 frames; superseded by task 91/97 fixed-rate ticks. | 2026-09-24 |
| run-flyfast.sh | 2026-09-25 stutter + seams diagnosis; done (tasks 16-26). `tools/report-flyfast.py` is its reporter. | 2026-09-25 |
| run-lavalight-ab.sh | Lava aggregate light ON/OFF; shipped (owner decision 8). | 2026-10-09 |
| run-lodpop.sh | LOD tier switch visibility; answered, shells replaced the question. | 2026-09-25 |
| run-looksweep.sh | Cinematic grade over a day; superseded by run-look-review.sh. | 2026-09-24 |
| run-overhaul-batch.sh | One-off 2026-09-25 brief batch. | 2026-10-03 |
| run-phase7-rung1.sh | Stage E rung 1 cost; answered in PHASE_7_COMPLETION. | 2026-09-13 |
| run-roofskip-ab.sh | Shadow roof skip A/B; shipped ON (task 59). | 2026-10-10 |
| run-shadow-dots.sh | Task 56 shadow dots; fixed. | 2026-10-10 |
| run-torchshadow-shots.sh | Task 46 torch shadow pairs; shipped. | 2026-10-10 |
| run-upscale-ab.sh | URP upscalers vs native; decided FSR 3 (task 52). | 2026-10-03 |
| run-upscaler-smoke.sh | Task 47, one still per upscaler; done. | 2026-10-10 |
| run-upscaler-compare.sh | Task 48 shimmer per upscaler; done. | 2026-10-10 |
| run-upscaler-clips.sh | Task 50 clips; done (the `-upscalerclips` C# mode stays: cost-split and look-review use it). | 2026-10-10 |
| run-upscaler-realrate.sh | Task 51 real-rate clips; done. | 2026-10-10 |
| run-playtest-bugs.sh | Step-0 diagnosis of two playtest bugs; closed. | 2026-09 |
| run-demo.sh | Overhaul / tg2 look launcher; its own header points to run-play.sh. | 2026-10-08 |
| run-phase7-demo.sh | Phase 7 section 12 review; done. | 2026-09-24 |
| run-phase5b-demo.sh | Demo of the closed phase 5b. | 2026-09 |

Not moved (plan said retire): `run-playground.sh` -- `tools/capture-gpu-trace.sh` tells the user to run it to rebuild
before a GPU trace; retire it together with an update of that tool.
