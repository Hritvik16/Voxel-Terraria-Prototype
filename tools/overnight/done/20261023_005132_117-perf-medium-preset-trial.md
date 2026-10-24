# 117-perf-medium-preset-trial

Model: opus
Requires: 116-perf-shell-sky-leap

Approval: owner, 2026-10-22 (chat, Q65): "Try Medium, show me". PERF_30FPS_PLAN.md row 2 / step 2. NO default change in this
task: the owner sees the stills and the numbers first. PERFORMANCE task (measurement allowed).

## What
`-quality medium` (exists, `Assets/ContentModules/Config/ShellLayout.cs:133`) vs High, both at 540 rows, FSR 3, shells ON.
- Stills (well lit, midday, sun behind camera): walk_open, walk_near and one LONG view from high ground where the 3.3 km vs
  6.6 km 3D range shows; High vs Medium side by side + 3x crops of the 1-3.5 km band and the 3.3 km hand-off to the far band.
  Captions say what to look at (and where Medium looks worse, if anywhere).
- Timing: warm-up (discarded), cost-split FULL High vs Medium at walk_near / walk_open (ABBA), `MODE=walk` 3 min x 4 (ABBA).
  Report walk p50 / p95 / % > 33 / p99, memory (expect -171 MB) and shell fill while walking.

## Output
HANDOFF with numbers, image paths, `## For the doc` (the side-by-side + the hand-off crop). TASKS.md paragraph.
STOP_FOR_OWNER: "make Medium the default on the M1?".
