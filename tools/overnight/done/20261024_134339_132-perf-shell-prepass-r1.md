# 132-perf-shell-prepass-r1

Model: opus

Approval: owner, 2026-10-23, Queue decisions doc "Questions for you" tab, Q85: "A" = build R1 (shell pre-pass kernel) from
`PERF_SHELL_MARCH_PLAN.md` §4, as picked by task 130's probes. Includes accepting R1's +~10 MB GPU scratch at 540 rows (~18 MB at
720) under the 3 GB cap. This is a PERFORMANCE task (measurement allowed). ONE change: R1 only. NOT R2 (ray compaction), NOT R6
(kernel diet), NOT the Q83 shadow fix (owner not asked yet; note in HANDOFF how a Q83 fix could join R1's pre-pass, nothing more).

## Read first
`PERF_SHELL_MARCH_PLAN.md` (R1 row, §4-5), `tools/overnight/blocked/130-perf-shell-march-probes.HANDOFF.md` (E1-E3 numbers, the
untested shade pass, the CSShellProbe kernel you can build on), `KNOWN_ISSUES.md` "SHELLS COST", `Screenshots/130/perf130.txt`.

## Do
1. Implement R1 behind a flag (`-shellprepass 1`; OFF = today's path, untouched): the main kernel does tier 0 + flags (writes each
   shell-candidate pixel's tTier0 / -1, as 130's split arms do), a small kernel marches the shells, a shade pass finishes those pixels.
   Exact: same image as today. Allocate the scratch only when the flag is on.
2. Correctness: with the flag ON, frames must match OFF (byte-identical at native with FSR off, or the metrics/hash report unchanged
   at the task 116/129 poses; explain any difference and stop if it is visible). Fast EditMode suite once at the end.
3. Measure: write the decision each outcome triggers BEFORE measuring. **On the charger** (check `pmset -g batt`; if on battery, say
   so and wait up to 30 min for AC, else measure and flag it). Warm-up launch (discarded, path stated), then ONE warm ABBA
   (OFF / ON / ON / OFF) at walk_near and walk_open (the `-costsplit` FULL arm is fine), plus one walking arm if the launch budget allows.
   Report p50 / p99 and the % of frames > 33 ms per arm, and the GPU memory added (ledger or counters).
4. Default: if ON is faster at both poses and the image matches, make it the play default (flag `-shellprepass 0` = old path).
   Otherwise leave it OFF and report. Look at one still each pose ON vs OFF before deciding.

## Deliverable
HANDOFF with `## For the doc`: the ABBA table (before -> after, ms, with log paths), memory added, the image check result with one
ON|OFF side-by-side still (caption saying where to look), whether the default changed, gameplay consequence in plain words (expected:
none). Then STOP_FOR_OWNER (CLAUDE.md: one performance change, then the owner looks before the next one). TASKS.md paragraph.
