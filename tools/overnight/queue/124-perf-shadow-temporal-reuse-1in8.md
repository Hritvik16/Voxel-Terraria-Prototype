# 124-perf-shadow-temporal-reuse-1in8

Model: opus

Approval: owner, 2026-10-22, Q67 (chat): "clamp then reuse, judged on golden-hour stills"; supervisor 2026-10-23 after task 118
found the clamp a no-op in play (HANDOFF: 128 m cap already applies; shell/far shadows unclamped) -- Q75b (a) "going ahead
unless you object". PERFORMANCE task, ONE change. PERF_30FPS_PLAN.md row 5 (b).

## Change
Sun-shadow temporal reuse: refresh 1 frame in 8 instead of today's 1 in 4 (find the existing reuse/refresh constant with grep in
Raymarch.compute / RaymarchFeature.cs; add a flag, e.g. `-shadowreuse 8`, default stays at today's value until measured).
No API or memory-layout change. Moving shadows (sun or edit change) may lag ~2 frames more; check the existing sun-reset / edit
invalidation still triggers a full refresh.

## Measure and look
Warm-up (discarded), cost-split FULL reuse-4 vs reuse-8 at walk_near / walk_open (ABBA), AC only. Golden-hour stills (17 h, sun
behind and beside, as in 118's `-lookgolden`) with a sun-moving burst and an edit; captions say what to look at.
Report ms moved. Keep default 4 unless the gain is >= 1 ms with no visible lag artefact; the default change itself is the owner's
(STOP_FOR_OWNER with stills). HANDOFF + `## For the doc`, TASKS.md paragraph, fast suite once.
