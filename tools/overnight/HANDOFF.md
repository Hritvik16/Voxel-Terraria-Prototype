# 248-raystats-material-split: DONE -- wood coverage measured; porous estimate moved UP to ~2 ms per 1 % of screen (was 1.3-1.8); solid ~0

## What changed (commit 70081f8)
- `Raymarch.compute`: `RayStatsMatClass(mat)` (1 wood = task 225 class 1: 147/204-207/215/221; 2 ground = other generator ids 128-223,
  not leaf). `RayStatsAdd(..., matCls)` writes the record's two SPARE slots [6] wood / [7] ground, for the primary ray of a voxel hit only
  (sun / sky pass 0u). Leaf stays [4]; other = hit - leaf - wood - ground (CPU). Still only under `VE_SHELL_DIAG`; play kernels unchanged.
- `RayStatsReport.cs`: `MatClass` twin, `Mat()` split, a second table "primary hit material, % of the SCREEN" per band.
- `RayStatsReportTests`: `MatClass_TwinsTheShader_WoodIsTask225sClass1`, `MatSplit_SumsToRays_PerBandAndAll_FormatPrintsScreenShares`.
- `WORLD_FILL_BUDGET.md` sec. 0 note, 1a material table, 1b.2-1b.4 re-derived, sec. 4 rule note, E1 marked done. `TASKS.md` Task 248.
- `Screenshots/248/`: run.sh (246's runner, 16 deg only), parse.py, coverage.py -> coverage.txt, the 4 costsplit reports, run_log.txt.

## Evidence (counts only, no frame times: rule 10)
- Tests: filter `RayStatsReportTests` 7/7; fast suite after: PASS 1125 FAIL 0 SKIP 25 (6m43s). Before: today's TestResults.xml 1207/0 fail
  (no code changed since 245).
- 4 launches, one dev-kernel build (46 min: full Raymarch recompile). Logs `Logs/overnight/248/` (player_*_16.log, launch_*.out).
  No `[kernels] FAILED` in any; `CSMainFarShadeDiag verified: it ran`. No new buffer or binding -> Metal slot count unchanged (<= 31).
- Repeatability vs 246: off arm band shares identical (23.5/12.9/5.5/58.1 %), leaf coverage 24.5/16.1/28.1 % (246: 25.0/16.0/28.0),
  tier-0 steps within 1 % (dense 23.15 vs 22.99 M).
- Fit (3 tree arms, through off): tier-0 steps per 1 % of screen: porous +0.305 M, solid -0.152 M. Residuals -0.89/+1.65/-0.17 M.
  Well-conditioned pairs: pre-210+dense +0.314/-0.190; sparse+dense +0.233/+0.016. pre-210+sparse is near-collinear, dropped.
- Memory (rule 11, capture-only): +0 bytes (spare slots reused; RayStats stays 9.6 KB, allocated only with -raystats 1). forest_eye
  ledger repeats 246: GPU peak 153,364 / 254,396 / 221,323 / 245,704, refused 0 / 27 / 0 / 0, CPU peak 436,947-493,793, process peak
  2,481-2,579 MB (noise).
- Pass card (rule 12): no pass added or changed in play. `CSMainFarShadeDiag` (capture only): +<= 1 InterlockedAdd per primary pixel
  while -raystats 1. Slice (rule 14): none taken (capture only). HEAVY: no.
- No image: the task changes only counters; nothing new is drawn (the off vs dense still is 246's `forest_eye_off_vs_dense_16.jpg`).

## Next
- E3 (`246c-voxel-grass-fringe-counts`) can now report fringe coverage directly with this split (fringe would count as ground unless it
  gets its own id -- give it one). Sec. 4's admission rule says 1.5 ms per %; the doc notes 2.0 -- supervisor/owner may adopt it.
- Timing milestone 238 should check the 2 ms per 1 % and the "solid ~0" claim.

## For the doc
Task 248 -- counting trunks, not just leaves. RayStats now says what each screen pixel hits: leaf, wood, ground, other, or nothing.
Re-ran the 4 tree arms at forest_eye, sun 16 deg (counts only, no timing).

| forest_eye 16 deg (% of whole screen) | leaf (see-through) | wood (solid) | ground | sky / far |
|---|---|---|---|---|
| no trees | 0 | 0 | 52.8 | 47.0 |
| pre-210 trees | 24.5 | **29.1** (19.0 inside 10 m) | 34.8 | 11.5 |
| sparse (210) | 16.1 | 16.9 | 51.5 | 15.5 |
| dense (default) | **28.1** (22.9 at 10-40 m) | 11.2 | 54.0 | 6.7 |

- Does "1.3-1.8 ms per 1 % of see-through screen" hold? **Only at its low end.** With wood counted, the best fit is 1.7-2.3 ms (est.;
  pairs 1.3-2.4). Budget ~2 ms per 1 %. Source: `Screenshots/248/coverage.txt`.
- Solid content (trunks): 0 to -0.19 M tier-0 steps per 1 % of screen -> costs ~0 ms (it hides work behind it). Old guess -0.15..-0.20 M.
- Wood in the pre-210 arm was 29.1 %, not the ~17 % that 246 guessed by decomposition.
- Memory: +0 MB (2 spare counter slots reused); play unchanged.
- No image: only counters changed, nothing new on screen.
