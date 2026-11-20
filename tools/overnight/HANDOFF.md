STOP_FOR_OWNER -- Q-244: the counters work and their writes change 0 px, but turning them ON draws with the *Diag kernel, which differs
from play's diet-2 kernel by 7-12 px (1-2 of 255) at noon (0 px at walk_near 16 deg). The task asked "hash-identical on vs off".
(a) accept as is (recommended: capture-only, the counted work is the same algorithm, the counts are the deliverable);
(b) queue a follow-up: a diet-2 counter twin (VE_DIET2 + counters; ~1 h build, +1 kernel) for byte identity; (c) other.

# 244-sun-steps-counter: DONE (counts + table), identity: counters exact, the Diag kernel 1-2 LSB off on <= 12 px -> Q-244

## What changed (commit "244:", see `git log -1`)
- `Raymarch.compute`: RayStats buffer (9,632 B, allocated only when a capture turns it on): primary / sun / sky rays, steps, tier-0 share,
  blocked, blocked-by-a-tier-0-leaf, steps-per-ray histogram, by camera band 0-10 / 10-40 / 40-82 / 82 m+. Writes ONLY under
  `#ifdef VE_SHELL_DIAG` (CSMainFarShadeDiag + new CSSkyFillTraceDiag); play kernels fold RAY_STATS_ON to false.
- BUG FIXED (pre-existing, KNOWN_ISSUES top): `CSMainFarShadeDiag` had 32 Metal buffer slots (cap 31) and never loaded in a player
  (already in Logs/overnight/219, 221) -> every cost-split counter line ([sun], [nearsh], shell / far) read 0 and far-shading debug views
  were magenta. The *Diag twins now fold `_UseLODCascade` to 0 like task 206's flood twins (29 slots); drawn only while the cascade is off.
  The [sun] / [shell] / [nearsh] lines work again (forest 16 deg [sun full] 154,173 rays, 31.9 tier-0 steps/ray = my table's tier-0 part).
- `RaymarchFeature`: CollectRayStats / ResetRayStats / RequestRayStats (AsyncGPUReadback, no stall) / DebugFreezeTemporal (capture only).
- `RayStatsReport.cs` (table), cost split `-raystats 1` [`-raystatsframes N` 32, `-rsfreeze N` 300], `tools/rspair244.py`,
  tests `RayStatsReportTests` (4), `KernelHealth.RaymarchKernels` +CSSkyFillTraceDiag. NOT added to DevKernelStrip (its test needs owner OK).

## Evidence
- Fast suite PASS 1119 FAIL 0 SKIP 25 (`Logs/overnight/244/fast.out`). No frame time quoted (rule 10); launches: FULL still only, untimed.
- Identity (`python3 -I tools/rspair244.py`, temporal frozen 300 frames, no upscaler): walk_near 16 deg all 4 stills IDENTICAL; noon
  walk_near / walk_open: control 0 px, ON vs Diag-OFF 0 px (counters exact), play vs Diag 7 / 12 px max 1 / 2. forest_eye: control itself
  1,185-1,204 px (streaming deficit 542-579: distant band rows 471-583 still loading), ON/OFF the same size in the same rows.
- Memory (rule 11): GPU body pool peak 245,704 / refused 0 at forest_eye = budget baseline (Logs/budget/2026-11-17_235256) 245,704 / 0;
  process peak 2,515 MB (16 deg launch) / 2,612 MB (noon) vs baseline 2,534-2,621; CPU pool peak 494,389 (valve 484,500). Added: 9.6 KB.
- Pass card (P2): capture-only, nothing new in play. Under -raystats: the Diag primary kernel + CSSkyFillTraceDiag at render rows
  (768x432), every frame for the 32-frame window, ~2-12 atomics/px, no new ray march, +9,632 B. Switch `-raystats 1` (OFF = no pass).
  Slice (rule 14): none -- 0 ms / 0 MB on tier A (never runs in play).

## The table (per frame, forest_eye 16 deg = `Logs/overnight/244/costsplit_16.txt`; noon = `costsplit_noon.txt`, `costsplit_noon2.txt`)
| pose | sun rays | steps/ray avg | p95 | sun steps/frame | tier-0 share | stop on leaf | sky steps/ray, p95 |
| forest_eye 16 | 153,561 | 60.3 | 160-175 | 9.26 M | 53 % | 21 % (10-40 m: 43 %) | 45.5, 128-143 |
| forest_eye noon | 154,965 | 28.5 | 60 | 4.42 M | 51 % | 23 % | 39.9, 96-111 |
| walk_near noon | 153,751 | 36.2 | 64-79 | 5.56 M | 71 % | 0 % | 39.3, 96-111 |
| walk_open noon | 133,365 | 9.7 | 31 | 1.30 M | 46 % | 0 % | 36.2, 80-95 |
| walk_near 16 | 83,413 | 28.2 | 32 | 2.36 M | 61 % | 0 % | 28.1, 64-79 (pose faces a wall, all rays < 10 m) |
forest_eye 16 by band (primary steps share / sun steps share): 0-10 m 12 % / 15 %, 10-40 m 35 % / 30 %, 40-82 m 16 % / 22 %, 82 m+ 37 % / 33 %.
- F4 (foliage early-out): SUPPORTED at low sun -- forest sun rays take 2.1x the steps of forest noon and 1.7x walk_near noon (p95 ~2.5x);
  21-46 % of them stop on leaves. 47 % of those steps are the shell continuation, so F4 must act in the shell march too. Not at noon.
- F6 (coarser leaves > 40 m): WEAK -- the 40-82 m band holds 16 % of primary and 22 % of sun steps at forest 16 deg (10-40 m holds more).
- Finding: budget walk_near_16's FULL still pose (pos[0]) stares at a sandstone wall 1-10 m away (`Screenshots/244/walk_near_16.png`).

## Next
Owner Q-244. Then F4 (task file per PERF_SLICE_BUDGETS sec. 6). Counter launch = `Logs/overnight/244/run.sh` / `run2.sh` (~10 min each).

## For the doc
Task 244 -- sun / sky / primary ray-step counters (capture only), and the debug kernel fixed so counters read again.
- Sun steps per ray, forest_eye: noon 28.5 -> 16 deg 60.3 (p95 60 -> 160-175); walk_near noon 36.2 (`Logs/overnight/244/costsplit_*.txt`)
- Sun steps per frame, forest_eye: 4.42 M (noon) -> 9.26 M (16 deg); sun rays stopped by leaves at 16 deg: 21 % (43 % at 10-40 m)
- Steps beyond 40 m at forest 16 deg: primary 16 %, sun 22 % of the 40-82 m band -> F6 weak, F4 supported at low sun
- Memory: +9.6 KB, only while counting; pool peak 245,704 = baseline, refusals 0 = 0
- `Screenshots/244/forest_eye_16.png`: the counted forest pose at 16 deg -- trees 5-80 m, the band where 43 % of sun rays stop on leaves.
- `Screenshots/244/kernel_diff_walk_open.png`: walk_open noon, darkened; the magenta squares mark the ~12 px where the debug kernel differs from play by 1-2 levels (Q-244).
