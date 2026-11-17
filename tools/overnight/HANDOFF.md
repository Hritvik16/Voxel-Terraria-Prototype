# 229-perf-torch-flood-only-when-torches: DONE. The flood twin kernel runs only when torches are near (`-floodnear 1`, play default). Day saves 6-13 ms, look unchanged. Commits 7633b47 (code + tests + TASKS), db0e125 (runner, plan, reports)

## What the 27.8 ms was (split, capture only)
- With the flood ON (play), an EMPTY pool still uploads, so `TorchFloodPool.Ready` was true with no torch anywhere. Play therefore drew
  `CSMainFarShadeFlood` every frame. That twin is not a diet-2 kernel, so play lost diet 2, as amendment 8.17 suspected.
- Warm ABBA, walk_open (the clean pose): twin 37.5 ms; flood off (diet 2) 31.3; flood off + diet 2 off 37.35; rule on 31.2.
  So diet-2 loss = 6.05 ms, flood code inside the twin = 0.15 ms (~0), and flood ON outside the kernel (CPU tick) = -0.1 ms (~0).
  The cost is the kernel swap, not the flood lookup or its build.
## What changed
- `FloodTwinRule` (new, VoxelEngine.Rendering). The twin draws only while (a) an ACTIVE light's range sphere reaches the tier-0 window + 1 brick
  (`LightSourceBuffer.ActiveLights`, the shader's own `dist^2 > range^2` gate), or (b) a LIT flood chunk lies in it (`TorchFloodPool.LitChunkIn`:
  non-zero directory entries, 128-voxel chunks, the same as `FloodLevels`). Otherwise the plain kernel (and so diet 2) draws.
  Exact by construction: the two kernels differ only in the light terms on tier-0 hits, and with (a) and (b) false every one of them is 0.
  No terrain tuning. No frozen API or layout change: one read-only property on LightSourceBuffer, one method on TorchFloodPool.
- `-floodnear 1|0` (default 1; 0 = before). Cost arms `floodnear_off`, `floodd2_off`, `tf_twin`; run lines end `flood twin|skip|-`.
  `-costfloodpair` takes the identity stills.
- Why this design is the efficient one (rule 11): per frame, at most 32 sphere tests plus a scan of the window's chunk directory; no allocation, 0 bytes.
## Evidence
- Fast suite `PASS 1086 FAIL 0 SKIP 25` (`Logs/overnight/229/fast.out`; +5 `FloodTwinRuleTests`). Adversarial cases tested: a light 1 cm inside and
  1 cm outside its range (on an axis and on a diagonal corner); 20k random lights, where "rule says no" => the shader skips every window point;
  a real flood with one lit chunk: window one voxel short -> plain, one voxel in -> twin, torch removed -> plain, placed again -> twin.
  `KernelHealthTests` pin updated.
- Timing: WARM steady state (warm-up `Logs/tg2world_20261117_092057.txt`), 432 rows, play flags, plan written first (`Logs/overnight/229/PLAN.md`).
  The 1st day launch (`costsplit_day.txt`, with raw stills) was too noisy (FULL 60-88 ms), so it was re-run right away: `costsplit_day2.txt`, p50:
  walk_near  before 90.2 -> after 78.0, and later in the same launch 72.0 -> 59.2. The whole launch dropped one level at pass 10, so each
             comparison uses neighbouring passes. Flood off 75.6 / 59.5; FULL vs flood off: +2.4 / -0.3 (noise).
  walk_open  before 37.5 -> after 31.2 (6 FULL passes 30.8-31.8).
  torch_camp (night, 28 torches; `costsplit_night.txt`): tf_on (rule on) 61.5 / 61.2, tf_twin (before) 64.0 / 59.8. Both draw the twin in every
  pass, so the picture is the same by construction. (Point lights at night, FULL there: 74-90 ms.)
- Identity (`Logs/overnight/229/diff.txt`): play's raw stills are not deterministic (sky fill sample rotation and sun reuse; raw/raw2 differ on ~850k px).
  Flipping the rule gives diffs equal to the no-op flip in `flood_off`. walk_near: mean |d| 0.38 vs control 0.36-0.38; torch_camp 0.31 vs 0.30;
  0.000 % px > 32 everywhere. "Pixel-identical" is proven by construction plus tests, not by a byte-equal still.
- Memory (rule 11/12): 0 bytes added. Day peak 2,474.5 MB, GPU refusals 0, GPU pool peak 137,204, CPU pool peak 237,347 / 256,984, valve 484,500.
  Night peak 6,037 MB: this is task 221's known synchronous capture flood build (221 tf_on launches: 6,069 / 6,074 MB). Not this change; task 170 is parked.
  P2: no new pass (kernel choice only; 8.17 step S4). Not HEAVY.
## Left / next
- With torches near, the twin still costs the diet-2 loss (~6 ms at walk_open). Next cut, needs owner approval because it changes performance:
  a diet-2 flood twin (`CSMainFarShadeLeanFlood`, VE_DIET2 + VE_TORCH_FLOOD; diet 2 folds the cascade, so the Metal buffer slots stay <= 31), or 8.17 T2 (S4 out).
- Not committed (not mine): `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*`. Images git-ignored (`Screenshots/229/`).

## For the doc
- Torch flood by day, no torches near: walk_open 37.5 -> 31.2 ms p50; walk_near 90.2 -> 78.0 and 72.0 -> 59.2 (warm ABBA; `Logs/overnight/229/costsplit_day2.txt`).
- What the old cost was: all of it was play losing the faster "diet 2" kernel (6.05 ms at walk_open); the flood lookup itself costs ~0.15 ms.
- torch_camp at night (28 torches): unchanged, 61.4 vs 61.9 ms; same kernel, same picture (`costsplit_night.txt`).
- Memory: 0 bytes added; GPU refusals 0.
- `Screenshots/229/sheets/before_after.png`: columns BEFORE (twin always) | AFTER (rule on) | difference x4. Rows: walk_near at noon, torch_camp at night.
  Look at column 3: only fine grain from frame-to-frame noise, no shapes. At night every torch's glow is the same in columns 1 and 2.
