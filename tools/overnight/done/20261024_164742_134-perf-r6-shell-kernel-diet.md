# 134-perf-r6-shell-kernel-diet

Model: opus

Requires: 133-perf-q83-near-shadow-through-shells

Approval: owner, 2026-10-24, in chat (AskUserQuestion), Q86 "R6: slim draw code (Recommended)": R6 from `PERF_SHELL_MARCH_PLAN.md`
(row R6). That row includes Q72 (b): removing the OFF-by-default experiment paths (air-brick mask `-shellmask`, column sky leap
`-shellcolleap`, the plane leap) from the PLAY kernel. Exact (same image), no memory cost. PERFORMANCE task: ONE change, measured,
then STOP_FOR_OWNER. Runs after 133 (owner looks at 133 first; one performance change per measurement).

## Read first
`PERF_SHELL_MARCH_PLAN.md` (R6 row, §4-5), `tools/overnight/blocked/130-perf-shell-march-probes.HANDOFF.md` (E2: the march is
execution-bound, 3.3 / 0.77 ns per iteration), 132's and 133's HANDOFFs, `KNOWN_ISSUES.md` "SHELLS COST".

## Do
1. Kernel diet on the shell march as the plan's R6 row lists: hoist `ShellByDistance` to a per-band test, shrink `ShellHit`, move the
   93b4-116 diagnostic fields/counters into a capture-only variant (keyword or separate kernel; the play kernel carries none), and drop
   the OFF-by-default mask / column-leap / plane-leap blocks from the play kernel. If keeping those experiments in a capture-only
   variant is cheap, keep them; otherwise remove their flags and list what was removed in TASKS.md (git keeps them).
2. Correctness: byte-identical stills vs before (no upscaler) at walk_near, walk_open and 131's cliff1; hit counters equal (in the
   capture variant). Fast EditMode suite once at the end.
3. Measure: decisions written first; on the charger; warm-up discarded; ONE warm ABBA (before build vs after, or a flag if you keep the
   old kernel selectable) at walk_near and walk_open, plus one walk arm. p50/p99, % > 33 ms.
4. Keep it if faster at both poses beyond noise and identical; otherwise revert and report what was learned.

## Deliverable
HANDOFF `## For the doc`: the ABBA table, the identity check (one before|after still, caption), what was removed from the play kernel,
gameplay consequence (expected none). Then STOP_FOR_OWNER. TASKS.md paragraph.
