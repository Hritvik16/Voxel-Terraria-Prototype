STOP_FOR_OWNER -- task 163 (perf, next exact cut D or C1): NOT STARTED. Its `Requires` is not met: task 161 never produced its warm AC
arm set (it is in `tools/overnight/blocked/`, STOP_FOR_OWNER twice: the Mac was on battery). The pick between D and C1 depends on 161's
`skyprobe_off` arm (`PERF_EXACT_CUTS.md` section 4 step 1: `skyprobe_off` >= 2 ms -> D, else C1), which was never measured.
At this task's start (2026-11-01, after 23:11) the Mac was STILL on battery: `pmset -g ps` -> "Now drawing from 'Battery Power'", 77 %, discharging.

**Q1 (recommended (a)):** (a) plug the Mac in and leave it plugged in, requeue 161 as-is (its HANDOFF in
`tools/overnight/blocked/161-perf-kernel-diet-2.HANDOFF.md` has the exact commands, ~1.5 h with warm-up, `SKIP_BUILD=1`), then requeue 163
unchanged behind it. (b) Skip the arm and pick for 163 yourself now: **C1** (shadow continuation out of the big kernel; the default branch of
the rule) or **D** (incremental roof pyramid + roof skip ON). Each would then still be measured on AC inside 163, so (b) only saves the 161 run;
it does not remove the need for AC power.

# 163-perf-next-exact-cut: NOT RUN (requires 161's warm AC arms; 161 blocked on battery)

## What changed
- Nothing in code, no build, no Unity launch. Only this HANDOFF.
- 161's own report is unchanged and preserved in `tools/overnight/blocked/161-perf-kernel-diet-2.HANDOFF.md` (identical to the HANDOFF
  this session found; checked with `diff -q`).

## Evidence
- `tools/overnight/blocked/` holds `161-perf-kernel-diet-2.md` + its HANDOFF; `tools/overnight/done/` has no 161 entry.
- 161's HANDOFF: "warm AC arm set NOT RUN (battery, twice)"; decision row "`skyprobe_off` >= 2 ms -> D next, else C1: NOT MEASURED".
- `pmset -g ps` at this session's start: Battery Power, 77 %, discharging.

## Standing state (from 161, unchanged)
- Diet 2 behind `-diet2 1` (default OFF); exact (0 px at walk_near, walk_open, cliff1 A).
- PRELIMINARY (not warm, battery-adjacent) walk_near 68.4 -> 60.7 ms p50, walk_open 26.9 -> 22.2 (`Logs/costsplit_2026-10-31_171648.txt`).

## Next
1. Owner: answer Q1, and make sure the charger stays connected for the run.
2. Requeue 161 (measurement only), then 163 unchanged. 164-166 do not depend on 161/163 and can run meanwhile.

## For the doc
- Nothing measured or changed: 163 needs 161's `skyprobe_off` arm to choose D vs C1, and 161 never ran on AC (Mac on battery, 77 %,
  at 163's start too).
- No images: nothing visible changed.
