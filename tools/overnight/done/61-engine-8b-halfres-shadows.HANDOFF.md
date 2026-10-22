STOP_FOR_OWNER -- Half-res sun shadows are built and ship as an F6 option ("Shadow resolution" Full/Half, `-shadowres`). Play's default is still Full. The shipped version saves only 0.5-1.3 ms, which is BELOW the task's 2.0 ms bar. Two questions:
1. Q1. Make Half play's default? (a) no, keep Full (recommended: about 1 ms for visibly blockier shadow edges, and Gate 3 is 1 px from red); (b) yes.
2. Q2. Shadow cost, step 8: (a) close it here and go on to the next plan step (recommended); (b) build the group-coherent variant (a separate half-res shadow pass over a hit buffer, then a depth/normal-aware upsample). Size M (est.): the march kernel splits into hit / shadow / shade passes, ~4-8 MB more buffers at 540 rows, no frozen API touched.

# 61-engine-8b-halfres-shadows: built, measured, default unchanged. Below the 2.0 ms bar, so it's the owner's call.
## What changed (branch `engine-phase`)
- e892f04: `Raymarch.compute` `_ShadowHalfRes`: rung 1's 2x2 quad reuse at ALL distances, made edge-aware. A pixel takes its quad lead's sun value only if its shadow origin (voxel + normal) is within 2 voxels + 2 pixel footprints of the lead's, or on the lead's plane (same normal, within 1 voxel). Otherwise it traces its own ray. The old rung 1 path is unchanged. `RaymarchFeature.ShadowHalfRes`; `PlaySettings.shadowRes` full|half (+1 test); F6 row; cost-split arms `halfres` (step A: plain rung 1 at all distances) and `half` (the shipped switch). Stills now also save `_sunvis` and `_clean` (no HUD).
- (this commit): menu hint now has the measured numbers; HANDOFF; `Screenshots/61/`.
## Evidence (warm steady state, FSR 3 at 540 rows, run-cost-split.sh, F X X F order)
- Step A, `halfres`, session 1 (warm-up `Logs/tg2world_20261010_234100.txt`): noon walk_near 48.0 -> 44.8 ms (+3.2/+3.1 saved), walk_open +0.5; sun 10 near +0.7/+0.8, open +1.8/+1.7 (`Logs/costsplit_2026-10-10_234721.txt`, `_235523.txt`). That cleared 2.0 ms, so the design was kept.
- Same build, `half` and `halfres` (warm-up `Logs/tg2world_20261011_000820.txt`; `Logs/costsplit_2026-10-11_001455.txt` noon, `_002546.txt` sun 10):
  - noon walk_near: FULL p50 43.0 / p99 51.9 -> half 41.8 / 49.5 (saved +1.3, +1.0; noise 0.3). `halfres` saved only +1.4 this time.
  - noon walk_open: 29.2 / 36.3 -> 28.5 / 32.8 (saved +1.3, +1.0; noise 1.6-1.7, so inside the noise).
  - sun 10 walk_near: 35.0 / 42.9 -> 34.5 / 42.3 (+0.5, +0.6; noise 0.1).
  - sun 10 walk_open: 33.5 / 39.6 -> 32.4 / 38.3 (+1.2, +1.3; noise 0.1).
- UNEXPLAINED: FULL at noon walk_near was 48.0 in session 1 and 43.0 in session 2 (both on battery). So step A's 3.2 ms looks like a session effect. The likely cause is lockstep: quad leads still leave 16 of 64 lanes tracing in every 8x8 group, and the group waits for its slowest lane (the same as the 2026-09-26 warning). Not measured further. `Builds/Playground_old.app` (HEAD shader) and `Playground_new.app` are left from the earlier attempt, unused.
- Gate 3 (`EXTRA="-shadowres half"` run-phase7-rig.sh, `Logs/overnight/61_gate3.out`): 97.26% = 426/438 (> 97%, PASS), was 98.17% = 430/438. All 12 mismatches are cpu=shadow, gpu=lit. One more mismatch would turn it red. Rig PASS 37 FAIL 0.
- Fast EditMode: PASS 649 FAIL 0 SKIP 14 (before and after the hint edit).
- Edge check (how): (1) by construction: sky, far-behind-near and other-plane pixels trace their own ray. (2) The `_sunvis` debug views of Full and Half are diffed at the same pose. Differing pixels (0.2-0.5% > 64/255) lie along shadow TERMINATORS in 2x2 steps, not along depth silhouettes. (3) In the 4x crops the ridge-against-sky silhouette is identical in both panels.
## Unfinished
- No group-coherent variant (Q2). The menu row is not click-tested in a play session; it applies like task 58's rows.

## For the doc
| sun / arc | Full p50 ms (fps) | Half p50 ms (fps) | saved |
|---|---|---|---|
| noon, walk_near | 43.0 (23.3) | 41.8 (23.9) | 1.0-1.3 ms |
| noon, walk_open | 29.2 (34.2) | 28.5 (35.1) | 1.0-1.3 ms (inside noise 1.6) |
| 10 deg, walk_near | 35.0 (28.6) | 34.5 (29.0) | 0.5-0.6 ms |
| 10 deg, walk_open | 33.5 (29.9) | 32.4 (30.9) | 1.2-1.3 ms |
Source: `Logs/costsplit_2026-10-11_001455.txt` and `_002546.txt` (warm). Gate 3: 98.17% -> 97.26% (PASS, 1 px from red).
- `Screenshots/61/walk_near_sun25_full.png` / `walk_near_sun25_half.png`: sun 25 deg from front-left, terrain lit. Look at the shadow line running diagonally down the near ridge on the right half of the frame. Half's edge is blockier there.
- `Screenshots/61/walk_near_sun25_crop4x.png`: 4x crop of that shadow line (left Full, right Half; where it is: `walk_near_sun25_cropbox.png`, yellow box). Half's line steps in 2x2-pixel blocks and moves by a few pixels, and its shadow reaches a little further on the right.
- `Screenshots/61/walk_open_sun25_crop4x.png`: 4x crop of the ridge against the sky (left Full, right Half). The silhouette is identical, so nothing bleeds across it. Only small soft-shadow patches on the slope differ.
Question: make Half play's default, yes/no (Q1); build the bigger variant (Q2).
