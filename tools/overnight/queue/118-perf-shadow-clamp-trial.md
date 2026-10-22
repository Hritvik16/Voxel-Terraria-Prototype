# 118-perf-shadow-clamp-trial

Model: opus
Requires: 117-perf-medium-preset-trial

Approval: owner, 2026-10-22 (chat, Q67 (a)): "Clamp 128 m, then reuse", judged on golden-hour stills before keeping.
PERF_30FPS_PLAN.md row 5 (a) / step 4. This task is the CLAMP only (1-in-8 temporal reuse is a later, separate task).
NO default change: the owner sees the stills first. PERFORMANCE task.

## What
`-shadowclamp 128` (exists, `PlaygroundCapture.cs`) vs today's 290 m; play settings (540 rows, FSR 3, shells ON, the preset
play uses at the time; say which).
- Stills at golden hour (`-tod 17`, sun ~14.5 deg, sun behind or beside the camera): walk_open, walk_near and a long ridge view
  where shadows past 128 m matter. Side by side + crops of the 128-290 m band. Captions say what to look at.
- Timing: warm-up (discarded), cost-split FULL vs clamp at walk_near / walk_open, at noon AND golden hour (ABBA), and `MODE=walk`
  golden hour 3 min x 4 (ABBA). Report walk p50 / p95 / % > 33 / p99.

## Output
HANDOFF with numbers, image paths, `## For the doc`. TASKS.md paragraph. STOP_FOR_OWNER: "keep the clamp as default?".
