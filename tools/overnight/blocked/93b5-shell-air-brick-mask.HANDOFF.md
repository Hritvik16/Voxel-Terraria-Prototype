STOP_FOR_OWNER -- task 93b5 (b): the EXACT one-leap group mask works (mask on = off: 0 px, 3,063 CPU rays field for field), but the
exit-plane leap it needs moves 440 px (0.021%, over the 0.01% stop line; 1-7 level speckles on the distant sea) vs today, and costs
+5-6 ms p50 by itself. Net vs today: -2.1 ms walk_near, +0.8 ms walk_open. Not >= 1 ms at both, so the mask stays OFF, and mask off
now runs the OLD leap byte for byte. Question: (a) one more perf task: a cheaper exact leap (no loops: exit axis direct, a one-step
correction), accepting the 0.02% px; the ceiling is ~2 ms (the old `shellgroup` arm); (b) RECOMMENDED: close the air-brick line.
The mask stays built and OFF, and 93b re-runs. The shells' +12-15 ms is elsewhere (air-chunk leaps 34-47%, tier-0 leaps 17-23% of
iterations, the per-iteration checks); (c) ship mask ON as is (not recommended: walk_open slower, p99 189 vs 106 ms at walk_near).

# 93b5 (b) exact group leap: built, exact, tested; not faster at both poses -> OFF, default path = today's code

## What changed (commit 251505e, engine-phase; continues 70ddcf5 / 2c1fa45)
- `Raymarch.compute`: new `ShellPlaneT` / `ShellPlanesCrossed` / `ShellLeap`. t = (exit plane - start) x tDelta from the plane itself.
  Each axis crosses exactly the planes whose t <= the exit t (a closed-form guess corrected against `ShellPlaneT`, no eps). The normal is
  the first axis whose crossed plane has that t. So a leap's state depends on its exit t alone. `ShellGroupLeapOk`: an empty group is
  ONE leap (sh = k + 5) when no tier-0 leap can happen inside it and every distance stays in shell k's band (primary: t up to the
  group's exit t; shadow: cell centres, 1 voxel margin). The exact inner loop and the `shellgroup` diagnostic are gone.
  `SHELL_PLANE_LEAP` = mask on (or `_DebugShellSkip` bit 4). Mask off = `LeapSpanReseedClosedForm`, as before 93b5.
- CPU twin `ShellRayReference`: `PlaneLeap`, `PlanesCrossed`, `GroupLeapOk`, `March(..., planeLeap)` (useMask implies it). `Leap` unchanged.
- `ShellAirMaskTests`: (2) compares mask on vs off with `planeLeap: true`; new (4) `Leap_OneGroupLeap_EndsWhereItsBrickLeapsEnd`.
- Cost split: arm `shellgroup` removed, `shellleap_new` (bits 3+4: mask off, new leap) added; the counters line shows groups leapt (stat 60).
  RaymarchFeature, CLAUDE.md (93b5 line + index), KNOWN_ISSUES "SHELLS COST" (new EXACT GROUP LEAP paragraph).

## Evidence (AC power 100%, warm steady state)
- Tests: filter run 18/18 (ShellAirMask, ShellMarchCache, ShellHandOver, ShellOrder, ShellStorage), no expected value moved, no tolerance
  touched. (4): 20,000 rays, 14,961 cross > 1 brick, 5,624 leave the group across an edge/corner: one group leap = its brick leaps
  in t, voxel, normal and tMax. (2): 2,376 primary + 687 shadow rays, 0 capped, differ 0, iterations 70%. (3) mutation: 21 differ (caught).
  **Fast suite PASS 822 FAIL 0 SKIP 14** (`Logs/overnight/93b5b-fast.out`). A player build after the gating compiled with 0 shader errors.
- Warm-up `Logs/tg2world_20261017_040606.txt`, then ONE launch `Logs/costsplit_2026-10-17_041222.txt`: F X X F Y Y F, `-shellmask 1`.
  The ARM NAMES IN THAT LOG are from before the gating: its `shellmask_off` = new leap without the mask (now `shellleap_new`), and its
  `shellleap_old` = today's march (now plain `shellmask_off`). p50 (p99) ms:
  walk_near: mask + new leap 63.5 (189, runs 62.1/65.0/63.4, noise 2.8); new leap only 70.6 (110); today 65.7 (106).
  walk_open: mask + new leap 49.0 (81, noise 1.2); new leap only 54.4 (103); today 48.2 (80).
  Shell iterations/pixel today -> mask: 5.53 -> 2.56 (walk_near, -54%), 30.33 -> 21.89 (walk_open, -28%); the estimate was -40-50 / -25-30%.
  Empty groups leapt: 526 k / 1.45 M a frame (40% / 13% of primary iterations).
- Identity (no-upscaler `_raw` stills, same launch): mask on vs off (both new leap) **0 px** at both poses; raw vs raw2 0 px. New leap vs
  today: walk_near 0 px, walk_open **440 px = 0.0212%, max channel delta 7** (217 px at 1, 153 at 2).
- Default path NOT re-launched after the gating. It is the same code as that launch's `shellleap_old` arm (a uniform branch to the old leap).

## Not done
- No mask default change, no cheaper leap (a second perf change; it waits for the owner's pick). 93b's re-run still Requires 93b5.

## For the doc
Task 93b5 (b): an exact one-leap-per-empty-group air-brick mask. It works, but it is not worth turning on.
- Shell iterations per pixel: **5.53 -> 2.56** (walk_near), **30.33 -> 21.89** (walk_open) (`Logs/costsplit_2026-10-17_041222.txt`).
- Frame p50, today -> mask on: **65.7 -> 63.5 ms** (walk_near, noise 2.8) and **48.2 -> 49.0 ms** (walk_open, noise 1.2). The exact leap alone costs +5-6 ms.
- Image: mask on = off **0 px**. New leap vs today: **0.021%** px at walk_open (1-7 levels), 0 at walk_near.
- GPU memory: the mask stays +33 KB (High). Default stays OFF; mask off = today's march, byte for byte.
Images:
- `Screenshots/93b5b/walk_open_heat_today_vs_mask.jpg`: shell-march step heat, today LEFT / mask on RIGHT. The distant ridge band (centre-right)
  is red/yellow on the left and mostly green on the right. Black = tier 0 drew it; the sky is blue in both.
- `Screenshots/93b5b/walk_open_newleap_vs_today_diff.jpg`: the walk_open frame darkened, with every pixel the new leap changes vs today
  marked by a red dot. They sit only on the distant sea and lowland at the left (scattered, 1-7 levels). The ridges are unchanged.
- `Screenshots/93b5b/walk_open_beauty.jpg`: walk_open at noon, no upscaler (desert ridges, sea and snow at the left). It looks the same with the mask on or off.
