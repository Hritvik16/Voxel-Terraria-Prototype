# 150-perf-walk-regression-isolate

Model: opus

Approval: supervisor, 2026-10-25 (owner: "isolate the cause before proposing a fix", CLAUDE.md; task 146's HANDOFF "Next"). A PERFORMANCE task:
timing is its purpose. Owner-independent: it only diagnoses, changes no default and no code except flags/report lines it needs.

## Question
Task 146 found the standard play walk got +3.7 ms p50 worse than 2026-10-22 (41.0 -> 44.7 ms) while steady poses held or improved.
Which of these causes it? Candidates (146): (1) task 133's near sun shadows through the shells (`-nearshadowshells 0` = before),
(2) shells holding more chunks while walking (s2 600-1,060 held vs 546-643, s3 17-27 vs 0), (3) playwalk's extra defaults (`-underwater`, `-minimap`).

## Decision table (before measuring)
- Arm A (`-nearshadowshells 0`) recovers >= 2 ms p50: cause (1); report the walk cost of 133 per sun angle and STOP_FOR_OWNER only if the
  owner's Q87 keep-ON answer needs revisiting (it was ON at +1.1..+1.9 ms, so quote the new walk number beside it).
- Arm B (`-underwater 0 -minimap` off) recovers >= 2 ms: cause (3): list which default and say it is HUD/CPU or GPU.
- Neither: cause (2) or noise; report the shell-held counts per minute and the run-to-run spread. No fix proposed here.
Every outcome ends with a named cause or a measured "not 1 or 3".

## Do
1. AC power check (`pmset -g batt`). One warm-up launch (`MODE=stream MOTION=walk ./run-tg2world.sh`), discarded; then back to back, no idle
   gap over ~2 min, ABBA: base (`MODE=playwalk`), A (`EXTRA="-nearshadowshells 0"`), B (the -underwater/-minimap arm; read
   `tools/play-flags.sh` for the exact flags), base. 5-min walks as 146 did.
2. Table: arm, p50 / p95 / p99, share > 33 ms, UPSCALER ROWS, shell held counts, log paths. State warm steady state and the warm-up log.
3. No fast suite needed unless code changed; if a report line was added, run it once.

## Deliverable
HANDOFF `## For the doc`: the table and the named cause. TASKS.md paragraph only if code changed.
