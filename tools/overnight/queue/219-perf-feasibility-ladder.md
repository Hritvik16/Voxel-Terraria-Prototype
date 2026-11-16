# 219-perf-feasibility-ladder

Model: opus
Requires: 218b-bug-gpu-view-eviction-proof

Approval: OWNER, 2026-11-16 (chat): "write the feasibility question and queue it". A PERFORMANCE task: timed arms are allowed (CLAUDE.md measurement
rules apply: warm-up launch first and discarded, then arms back to back, warm steady state, release standalone player via the existing rigs; never the Editor).
Source: owner question "is 30 FPS possible on this machine on any setting", task 212 (play 106.8 ms at walk_near vs the rig's 23.7 ms p50), `PERF_EXACT_CUTS.md`,
`tools/play-flags.sh` (play's real flags), `MODE=playwalk ./run-tg2world.sh`, amendment 8.14 quality presets.

## Do
1. Question to answer: with play's REAL settings (play-flags.sh: trees, shells, torch flood, FSR 3), what frame time does THIS Mac reach at each quality
   preset / render-row setting, and is any setting at a stable 30 fps (p50 <= 33.3 ms and <= 5 % of frames over 33 ms)? Say what each possible result
   would make us do BEFORE measuring (CLAUDE.md): all arms over 33 ms -> owner question "cut features, lower the target, or stronger machine";
   some arm passes -> that arm is the shipping floor and the question is what it costs in look.
2. Arms (one warm-up launch discarded, then back to back, ABBA where two arms compare): play defaults; the lowest preset the engine has; one step
   between (3 arms at most). Poses: walk_near (212's), a forest pose with the dense trees, and a jungle pose (the density worst case; use 218's
   fix or `-trees2 0` and say which). `MODE=playwalk` style walk or the rig 212 used; reuse 212's script, change nothing else.
3. Report per arm: frame p50 / p99 / % over 33 ms, process peak MB, GPU body pool refusals. Reconcile in words why 212's 106.8 ms and the rig's 23.7 ms
   differ (which flags differ). One short GPU-vs-CPU split if 212's tooling gives it for free; no new tooling.
4. Do NOT change engine code. Pictures: one frame per arm at the same pose, looked at, captioned (what the lowest preset costs in look).

## End
Done with HANDOFF + `## For the doc`: a table arm x (p50, p99, % > 33 ms, peak MB) and the pictures.
