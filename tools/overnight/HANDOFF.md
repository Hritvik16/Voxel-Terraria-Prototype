# 24-detail-radius-at-speed: DONE. The `-detailspeed 1` flag (OFF by default) gives a 0.62 s latency budget. Flag OFF is pixel-identical to the 06:45 run. Still NOT MET (fly 56 p99 3.3-3.8 s, 5.3-6.1x over).

## What changed (commits 0da30d7, 8979c6f, 4adf212, 4b03fe9, plus this doc commit)
- `StreamingRequirement.DetailRadiusM(v)`:
  - Formula: R_d = 128 m - v (0.62 s + 1/60 s), clamped to [64 m, 128 m].
  - Values: walk 5 m/s -> 124.8 m, 14 -> 119.1, 56 -> 92.4, 60 -> 89.8 m (7.0 chunks). v = 0 gives exactly 128.
  - Why a 64 m floor: half the load radius, the probe's "inside 64 m" ring. It only binds above 100.5 m/s, which only the dev camera reaches.
  - `LatencyBudgetS` added. `Compute` accepts R_d in metres.
- `DetailRadius.cs`:
  - `-detailspeed 1` (last match wins), or F5 in Playground.
  - `DetailSpeedFilter` = XZ path length / time over a 0.5 s window. Frame-time jitter cancels; one fast frame is bounded by its extra distance / 0.5 s. A move of more than 128 m in one frame counts as a teleport and resets the window. A still camera reads exactly 0.
  - It is continuous in metres, so walking costs 3.2 m of margin. Walking does lose detail, though: the square is camera-centred, while the residency edge is 128-140.8 m depending on where the camera sits in its chunk.
- Shader:
  - A camera-centred XZ square. The voxel march stops at it. `FarStartT` (task 22's integer DDA, unchanged) gets min(currentDist, tDetail). `FarMarch` skips a resident column only while t < tDetail.
  - All of it is under `#ifdef VE_DETAIL_SPEED` in a separate kernel, `CSMainDetail`, dispatched only while R_d < 128 m. CSMain is the pre-task code. It does not combine with `-checker`.
  - Residency, requests, eviction, workers and the generator are unchanged.
- Stream probe:
  - Section 5: speed, R_d(v), and the deficit inside the R_d square.
  - Section 7: the latency at 60 m/s and at the arm's own speed against the new budget.
- `KNOWN_ISSUES.md`: new bullet under STREAMING REQUIREMENT. `CLAUDE.md`: index line updated.
- Queued **31-detailspeed-walk-horizon-shot** (QA only; see Unfinished).

## Evidence
- Fast EditMode suite: PASS 617 FAIL 0 SKIP 12, after every code change (`Logs/overnight/24/suite5.out`). That is 611 + 6 new tests.
- **OFF is byte-identical.** Build 4adf212, `MODE=poses MAPV=4`, flag off (`FarBand/2026-10-04_095643_world`): 59/61 shots pixel-identical to the 06:45 run (`064529`). The two exceptions are the minimap_check shots, which also differ between my two runs of identical code (19,569 px), so they are run-to-run noise.
- **ON at a still pose is identical too** (`101803_world` vs `095643`: 59/61, the same two minimap shots). That includes all four coast_from_sea_30m shots, so **the coast pose does not change** with the flag on. Speed 0 means no clamp.
- Two traps found and fixed on the way:
  - (a) With the branches compiled into CSMain (uniform = 0), flag-OFF rivers_oblique still differed by 2,132 px along a far-ring boundary. That is a codegen change, which is why the code moved to its own kernel.
  - (b) The dispatch treated ANY kernel other than 0 as the half-width checkerboard, so ON rendered only the left half of the frame. That run's ON frame times were bogus and were discarded (`Logs/overnight/24/third_build_halfwidth_bug/`).
  - One earlier ON poses run (08:16) was ruined by someone at the Mac turning the camera (same eyes, different view). It was rerun.
- **Stream arms, warm steady state.**
  - Setup: one build (4b03fe9), v4, AC, no owner pause. Warm-up launch (discarded): `Logs/overnight/24/warmup.out` -> `Logs/tg2world_20261004_115512.txt`. Then OFF/ON/ON/OFF per motion, back to back.
  - Full table: `Logs/overnight/24/table.md`.
  - Each cell is the two launches, OFF | ON.

| | walk 5 m/s | fly 14 m/s | fly 56 m/s |
|---|---|---|---|
| R_d(v) p50 | 124.8 m | 119.1 m | 92.4 m |
| frame p50 ms | 12.6, 12.6 \| 12.6, 11.9 | 13.6, 12.7 \| 8.5, 8.5 | 11.2, 10.8 \| 9.2, 9.6 |
| frame p95 ms | 23.7, 24.4 \| 23.0, 22.4 | 46.0, 34.4 \| 28.6, 29.4 | 22.0, 24.5 \| 22.6, 20.1 |
| deficit inside R_d square p50 | 2395, 2461 \| 2367, 2512 | 2205, 2322 \| 2372, 2375 | 1563, 1537 \| 1537, 1562 |
| capacity p50 /s (need 2,285) | 16.4, 14.9 \| 15.7, 14.1 | 31.4, 27.7 \| 17.8, 18.1 | 21.3, 26.7 \| 27.0, 20.3 |
| request->GPU p99 vs 0.62 s | 36.0, 36.3 \| 35.3, 36.8 s | 10.4, 12.2 \| 10.7, 10.7 s | 3.8, 3.3 \| 3.4, 3.8 s |

- Reading the table:
  - The latency is NOT MET in every arm, 5.3x (fly 56) to 59x (walk) over the budget. The deficit inside R_d is about the whole square, so generation throughput is still the wall (72-161x short).
  - Frame time: ON is faster while flying (fly 14 by ~4.6 ms p50, fly 56 by ~1.6 ms) and unchanged walking. The voxel march stops at R_d.
  - Fly-14 capacity was lower ON (18 vs 28-31 /s) in both ON launches. **This is not explained.** Walk and fly 56 show no such drop. It might be more frames/s competing with the workers; that is unproven.
- Pictures, all in `Logs/overnight/24/`:
  - `fly56_horizon_strip.png`, OFF on top, ON below: look along the coast, the lake and the ridge line. They are the same in both. There is no hand-over seam in either, because at 56 m/s almost nothing is resident (deficit ~3,000/3,087) and the far band draws the whole frame.
  - `fly56_off_on.png`, right column (band view): both are entirely far band (green, yellow, red). There is no blue voxel area in either.
  - `fly14_off_on.png`, right column: OFF has voxels (blue) in the near middle. ON shows far band (green) in the mid-distance and blue areas with straight edges, which is the detail square. Left column (beauty): no hole, gap or floating piece in either frame.
  - `walk_off_on.png`: both frames face a slope a few metres away and both band views are 100% blue (voxels), so **the hand-over is not in frame and this shot cannot answer the walking-speed question.** That is follow-up 31.

## Unfinished / next
- Task 31: a walking-speed horizon shot, OFF vs ON (is the square edge visible at 124.8 m?).
- Owner's call:
  - Turn `-detailspeed` on by default? Note that it does not make loading fast enough: the 0.62 s budget is missed by 5-60x until generation is faster.
  - Is the fly-14 capacity drop worth a probe?
- Not done: fly 56 with the far band only is the common state today, so the hand-over at 92 m was not seen with resident voxels behind it.
