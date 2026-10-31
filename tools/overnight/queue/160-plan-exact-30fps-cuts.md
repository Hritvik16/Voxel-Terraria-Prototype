# 160-plan-exact-30fps-cuts

Model: opus

Approval: owner, 2026-10-31, chat: "All of those options are bad picture quality though" (on half-res shells, temporal shell reuse,
cheaper upscaler, shells off, far shadows off). PLAN ONLY: no product code, no default/look change. Measurements allowed ONLY if an
existing log cannot answer a row (cost-split, capture only), at most one warm launch.

## Goal
A list of cuts that leave the picture byte-identical (or within the exact-test tolerance the shells already use), ranked by estimated
ms on the walk, to stack on top of task 159 (dyn floor 432; est. walk 44.7 -> ~33-38 ms). The owner rejects visible quality loss.

## Do
1. Where the walk frame goes, today's build: split the GPU frame into its passes (tier-0 primary march, shell pre-pass + shell march,
   sun shadows near/shell/far, torch rays, far band, sky, FSR 3, minimap/HUD, upload/copies) from existing cost-split logs (146, 150,
   151, 153) or one new cost-split run; say which numbers are measured vs estimated. Include the 99 ms p99 spikes (KNOWN_ISSUES
   "WHILE THE CAMERA MOVES") and 150's unexplained half of the +3.7 ms regression.
2. For each pass, candidate EXACT cuts with an estimate and a check method, e.g.: PERF_SHELL_MARCH_PLAN R5 (chunk-state bitmap; layout
   addition -> owner OK), tier-0 march iteration counts vs an ideal DDA, redundant full-screen passes or copies, shader occupancy /
   register pressure in the main kernel, async compute overlap, work done for off-screen or sky pixels, CPU->GPU sync points,
   minimap cost when hidden, the saved-summary build off the main thread (spikes), anything still compiled into the play kernel that is OFF.
   Generality rule (owner 2026-10-24): data-derived and exact, no terrain-specific constants.
3. Rank them; say honestly whether exact cuts alone plus 159 can plausibly reach walk p50 <= 33 ms and p95 near it on the M1 Air.
   If not, say what the smallest remaining gap is.

## Deliverable
`PERF_EXACT_CUTS.md` committed + TASKS.md paragraph; HANDOFF with STOP_FOR_OWNER listing the recommended order (one task per cut)
and any approvals needed (layout additions), recommendation first.
