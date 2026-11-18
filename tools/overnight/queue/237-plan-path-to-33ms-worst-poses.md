# 237-plan-path-to-33ms-worst-poses
Model: opus
Requires: 234-merge-readiness-check
Approval: SUPERVISOR 2026-11-18, planning only (SUPERVISOR.md allows a no-code plan task). Owner 2026-11-17 chose judging by worst-case poses (low sun, torches); 235's table shows forest at a 16 deg sun is 135 ms (4.1x the 33.3 ms budget) and no single lighting fix reaches 30 fps.
## Do (NO code, NO builds, NO timing; read `Logs/budget/2026-11-17_235256/check.txt`, `PERF_EXACT_CUTS.md`, TASKS.md entries for 226-233, KNOWN_ISSUES)
1. Write `PLAN_PATH_TO_33MS.md` in the repo root: for each worst pose (forest 16 deg 135 ms, jungle noon 70, torches night 53, walk 35), the cost split from 235 (sun 54.7, sky fill 25.5, torch 8.4, rest ~55) and every known candidate cut with: estimated ms saved at that pose (cite the task that measured it, or say "unmeasured"), what changes on screen (plain words, what the owner would SEE), memory effect, risk, and whether it is exact or approximate.
2. Add the "rest ~55 ms" breakdown that is already known (near march, trees, FSR, CPU); say what is NOT known and what ONE cheap capture would split it (do not run it).
3. Give 2-3 coherent packages (e.g. "keep the look, accept 20 fps", "drop low sun shadow cost with a visible trade", "lower render rows at the worst poses") with the resulting estimated frame time per pose, honestly marking guesses. Say plainly whether 30 fps at forest 16 deg is reachable at all and what it would cost in look.
4. Respect: no terrain-specific tuning (data-derived, adversarial case), efficient by construction, no frozen-API changes.
## Rules
Commit only the plan file by path. `## For the doc` block: the 3 packages in a small table (pose -> est ms) and the one question the owner must answer. No images needed; if you cite one, use the 235 sheet.
