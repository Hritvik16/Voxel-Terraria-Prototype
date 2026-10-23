# 130-perf-shell-march-probes

Model: opus

Approval: owner, 2026-10-23, in the Queue decisions doc: "yes" to Q82 (step 1 only: the probe build E1-E3 from
`PERF_SHELL_MARCH_PLAN.md`; the owner decides the restructure AFTER seeing the result). Queued after 129.
This is a PERFORMANCE task (measurement allowed). Capture-only arms: NO default change, NO look change, NO restructure (R1-R6) shipped,
no frozen-API or layout change to the product.

## Do
Read `PERF_SHELL_MARCH_PLAN.md` §1-3 and `Screenshots/93b/rerun/perf93b_rerun.txt` first. Implement exactly E1 (`CSShellProbe` split
kernel, arm `shellsplit`), E2 (arm `shellsplit2`, march twice) and E3 (SIMD-efficiency counters in the counter window) as capture-only
code, ONE build. Then ONE warm-up launch (numbers discarded, log path stated), then `-costsplit` F X X F at `walk_near` and `walk_open`
(one or two launches, no idle gap over ~2 min, battery/charging checked). Write down the decision each outcome would trigger (the plan's
A/B tables) before measuring. Report warm steady state: FULL, shellmarch_off, shellsplit, shellsplit2 per pose, and E3's efficiency per
pose. The image in the split arms is wrong by design: timing only.
Fast EditMode suite once after the code change (capture-only code must not change the default render: confirm the default arm is
byte-identical or the metrics report unchanged).

## Deliverable
HANDOFF with `## For the doc`: a small table of the numbers, which outcome row of E1/E2/E3 each pose hit, and the plan's rule applied:
which ONE restructure it picks (R1 / R5 / R6, plus R2 if E3 says so), its gameplay consequence in plain words, layout/memory cost, and
the owner question. Then STOP_FOR_OWNER: do NOT start the restructure. TASKS.md paragraph. No images needed unless a chart helps.
