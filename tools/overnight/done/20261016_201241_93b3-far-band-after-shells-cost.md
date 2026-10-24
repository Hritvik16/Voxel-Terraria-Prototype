# 93b3-far-band-after-shells-cost

Requires: 93b2-shells-fill-while-walking

Approval: supervisor, 2026-10-15 ~22:40, part two of Q52 (a) (see 93b2's approval line; owner rule "recommended option
is the default" for reversible engineering choices). **This IS a performance task:** PREAMBLE rule 9 (warm steady state,
one warm-up launch, ABBA) applies and frame time may be quoted. Branch `engine-phase`.

## Why
Task 93b's paired arms (`Screenshots/93b/perf93b.txt`; logs `Logs/costsplit_2026-10-15_{210051,210747,212140,213552}.txt`):
at steady state shells ON cost +10.5 ms (walk_open) / +15.2 ms (walk_near) p50 over OFF. Turning the far band off saves
13.6-19.2 ms with shells ON but only 2.0-5.7 ms with them OFF, so the far band drawn after the shells (`tFar = tShellEnd`,
task 85) costs ~+11 ms more than after tiers 1-2; the shell march itself ~+3 ms. gpu* rose 34-39%: it is GPU time.
Why the far band costs more after the shells is NOT isolated.

## Step 1: isolate (capture-only flags, no default changes)
Split the +11 ms with cost-split arms inside ONE ON launch per pose (walk_near, walk_open), same settings as 93b
(`run-cost-split.sh`, fsr3 540, quality, far band + far shadows + `-farshade 1 -seethrough 1 -riverlook 1`, noon,
`-shells 1 -shellworkers 3`):
1. far band PRIMARY rays only vs its sun-SHADOW rays (far shadows off),
2. where the far march STARTS: per-pixel `tFar` with shells vs with tiers (a debug view or histogram), and far-march
   steps per pixel (heat-map still); does it start inside terrain, re-march from the camera, or step finer than before?
3. far shading (`CSMainFarShade`) on/off, in case it now runs on pixels that tiers 1-2 used to cover.
Name the line(s) in `Raymarch.compute` that account for most of the +11 ms.

## Step 2: one fix, if it is a bug (it likely is: the same far band costs 4x more behind a different near path)
Fix only what step 1 found; images byte-identical where possible, otherwise show the difference. Shells OFF byte-identical.
Then ONE ABBA set (OFF ON ON OFF) of the steady arms only, after the warm-up: p50 / p99 per pose, before -> after.
STOP_FOR_OWNER if the fix changes the look (far band coverage, shell hand-over distance) or is a design change rather
than a bug: report the split and the options.

## Acceptance
- Tests: `ShellHandOverTests` / `ShellOrderTests` still green, plus one for the fixed start / step rule (CPU twin
  `ShellRayReference`).
- Report: the split table (arm, p50 ms, delta), the fix, the ABBA before -> after, warm-up log path.
- Stills: walk_open ON before/after at noon (sun behind the camera), and the step heat-map before/after, captioned.
- `KNOWN_ISSUES.md` "SHELLS COST +10-15 ms STEADY ..." entry updated with the far-band half.
- `## For the doc` block. Then task 93b is re-run (it is back in queue/, Requires this task).

## Supervisor decision (2026-10-16 20:20, Queue decisions doc Q57, default (a) unless the owner objects)
93b3's isolation is DONE (`6e66b24`): the far band costs ~0 after the shells; the +12-15 ms is the shell march.
Reversible measurement choice, so the recommended option (a) goes ahead: split the shell march (primary vs shell-hit
sun shadow) and fix whichever dominates, in the new task `93b4-shell-march-cost`. 93b3 is closed as done; 93b now
Requires 93b4.
