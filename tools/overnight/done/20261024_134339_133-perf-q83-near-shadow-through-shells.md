# 133-perf-q83-near-shadow-through-shells

Model: opus

Approval: owner, 2026-10-24, in chat (AskUserQuestion), Q83 "Fix it (Recommended)": near (tier-0) sun rays that leave the 0.1 m
window or the 7-layer band ceiling unblocked continue into the shell march, riding task 132's R1 pre-pass. Accepted cost shown to
the owner: est. +2-5 ms/frame (unmeasured), +4-8 B/px scratch. This changes what is on screen (a bug fix) AND is performance-
affecting, so: ONE change, measured, then STOP_FOR_OWNER.

## Read first
`tools/overnight/blocked/132-perf-shell-prepass-r1.HANDOFF.md` ("Left open": the design -- pass 1 knows which tier-0 sun rays leave
the window/ceiling unblocked, hands them over, the pre-pass marches them with `ShellMarch(..., shadow=true)`, pass 2 reads sunVis),
`HANDOFF` of 131 (`tools/overnight/HANDOFF.md` history / `Screenshots/131/`), 129's Q83 note (`Raymarch.compute` TraceShadowRay
~line 1940, shell-hit shadow ~line 4555), `KNOWN_ISSUES.md` "SHELLS COST".

## Do
1. Implement the handover for tier-0 SUN shadow rays only (not point lights) inside the R1 path, behind a flag
   (`-nearshadowshells 1`; 0 = today). With `-shellprepass 0` the old single-pass path may stay unfixed (say so in TASKS.md).
2. Correctness: re-run task 131's capture (`-lookreview -cliffshadow 1`, same spots, sun 30 deg): the lit square at cliff1 must be
   gone (C: luma just inside vs outside the window edge within a few levels; today 195 vs 127), and A must show the shadow from your
   feet out. Check a NOON pose and the 132 walk_near/walk_open stills: no new dark patches (byte-identical where no near sun ray
   leaves the window lit). Fast EditMode suite once at the end.
3. Measure (rule 9): write the decision for each outcome first. On the charger (check `pmset -g batt`). Warm-up (discarded), then ONE
   warm ABBA (flag OFF/ON/ON/OFF) at walk_near and walk_open at a LOW sun (where rays leave the window) and at noon, plus one walk arm
   if the launch budget allows. p50/p99, % > 33 ms, memory added.
4. Default: ON if the cliff1 square is gone, nothing else changed in the stills, and the cost is <= ~5 ms p50 at the low-sun poses.
   Otherwise leave OFF and report.

## Deliverable
HANDOFF `## For the doc`: before/after picture at cliff1 (A and C, like `Screenshots/131/owner/q83.jpg`) with a caption saying where to
look, the ABBA table, memory, whether the default changed, gameplay consequence in plain words. Then STOP_FOR_OWNER. TASKS.md paragraph.
