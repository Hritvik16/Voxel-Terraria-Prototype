# 161-perf-kernel-diet-2

Model: opus

Approval: supervisor, 2026-10-31. `PERF_EXACT_CUTS.md` cut **B** + section 4 step 1. B is first or included in EVERY answer to task 160's
Q1 (a/b/c), and it is exact (picture identical, Q72 (b) precedent), so it runs while the owner answers 160's Q1-Q3 and 158's Q1-Q3.
A PERFORMANCE task, ONE change (B); the extra arms are data for picking the next cut, not changes.

## Power first
Run `pmset -g batt`. If it does not say "AC Power", wait (re-check every 5 min, up to 2 h) before the warm-up; if still on battery after 2 h,
do the code + correctness parts, skip the measurement, and STOP_FOR_OWNER saying so. Re-check before each measured arm; any arm that ran on
battery is rerun or reported as such. (159 and 160 both ran on battery.)

## Do
1. Cut B as the plan's section 2 row B describes: lean play kernels (`CSMainFarShade`, `CSShellPrepass`) WITHOUT what is OFF in play (dead
   one-pass `ShellMarch` call with the pre-pass ON, traversal modes 0/2/3, the tier 1-2 cascade march when shells draw, debug views 2-11 +
   `DebugOut` / `isDebugPixel`, half-res and rung-1 quad shadow paths, roof skip / hole leap / height bound / sky tilt, dev colours). A twin
   keeps today's code for every other setting and F6 toggle (134's `ShellDiagKernels` pattern). Lean ON by default in play only if the
   decision table says so; `-diet2 0` = today.
2. Correctness: lean vs twin raw stills **0 px different** at walk_near / walk_open / cliff1 (any non-zero px outside animated water = stop,
   do not measure). Fast suite once.
3. ONE warm launch set (warm-up discarded, then back to back): cost split at `-renderrows 432` (the 1-line `CostRows` clamp ->
   `UpscalerSelect.CaptureMinRows`, plan section 4 step 1) with arms `diet2_off`, `skyprobe_off` (new capture-only arm, 3 lines),
   `sun_off`, `nearsh_off`, `fshadow_off`, `reuse1`, `reuse16`, `roof_on`, `roof_build`, `shells_off`, `ups_none`, `tiny_gate`; FULL repeated
   so the order is interleaved. Plus ONE per-encoder GPU trace (`tools/capture-gpu-trace.sh`) of a walk_near frame: pre-pass / main passes /
   FSR 3 / copies.

## Decision table (write it into the handoff with the numbers)
- diet2 saves >= 1 ms at walk_near or walk_open (beyond noise) and 0 px: lean ON in play. ~0: keep it behind `-diet2 1`, say the structural
  theory weakened (C1 / C2 down-ranked).
- `skyprobe_off` >= 2 ms -> recommend D next; else C1 next.
- `reuse1` ~ `reuse16` ~ FULL but `sun_off` large -> the sun cost is fixed per pixel -> C2 is the lever.
- End STOP_FOR_OWNER only if something needs the owner (a non-exact result, a new memory cost, or the 160 answers changing the next step);
  otherwise end normally with "next = D or C1 per the arms" in the handoff.

## For the doc
Arm table (ms p50 per view, noise), the GPU trace's per-pass split as one small chart or table, which cut is next and why. No new images
unless something visible changed (it must not).

## Resume (supervisor, 2026-11-01): MEASUREMENT ONLY
Code + 0 px proof are done (`24424c2`, `cd565ee`); read `tools/overnight/blocked/161-perf-kernel-diet-2.HANDOFF.md` first. The Mac is on AC now
(`pmset -g ps` 13:12 "AC Power", 100 %). Run its "Next" section as written: warm-up (discarded), then the cost split at 432 with all arms, plus the
trace attach. Re-check power before each step; on battery -> wait as in "Power first". If `Builds/Playground.app` no longer matches `24424c2`'s
sources, rebuild first. Apply the decision table: diet 2 >= 1 ms -> `KernelDiet2` default ON in play (fast suite once), and state plainly which
of D / C1 is next per the arms (owner Q102 (a) approved the order and C1's 3-6 MB). Do NOT start D or C1 here. End normally unless something
needs the owner.

## Supervisor note (2026-11-01 18:15, while waiting on the usage limit)
The warm set finished (`Logs/costsplit_2026-11-01_143603.txt`, `MEASURE_DONE`); do NOT rerun it unless a check below fails. In it, walk_near
FULL ~33.4-35.1 vs `diet2_off` 33.3 / 34.0 (about 0, unlike the cold -7.3), and the `diet2_off` rows also print "kernel lean". Before applying
the table, confirm `diet2_off` really dispatched the twin kernel (the d2pair / kernel line); if it did not, the arm is invalid: fix the arm and
rerun only diet2_off vs FULL (warm). `skyprobe_off` ~27.2 vs ~34.5 looks like ~7 ms -> D by the table.
