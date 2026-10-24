STOP_FOR_OWNER -- Q (task 118): "keep the clamp as default?" has no effect either way. Play's voxel sun ray is ALREADY capped at
128 m. Next shadow step: (a) row 5 (b), 1-in-8 temporal reuse, as its own perf task (recommended: this is the plan's step 4 rule for
"< 1 ms"); (b) a distance cap on the SHELL-hit shadow march (new design; it would cut the long mountain shadows); (c) leave shadows.

# 118-perf-shadow-clamp-trial: DONE. `-shadowclamp 128` is a NO-OP in play. Nothing to keep or revert, and no default changed.

## Why (code, Raymarch.compute ~l.1827 / RaymarchFeature.cs l.69, l.1109)
- `TraceShadowRay` uses maxDist = min(`_MaxRayDistance`, `_ShadowMaxDistVoxels`).
- With the shells drawn (play's default since 93c), tiers 1-2 are not bound, so `_MaxRayDistance` = `RaymarchFeature.MaxRayDistance` = 1280 voxels = **128 m**.
- The "290 m" in PERF_30FPS_PLAN row 5 is tier 2's outer bound. It applies only with the shells OFF (rigs, or F6 "LOD shells" off).
- Shell-hit sun shadows (`ShellMarch`, iteration cap only) and far-band shadows have NO distance clamp. Mountains past 128 m still shadow
  you through them, and `-shadowclamp` never touches them.

## What changed (capture-only, no product behaviour change; commit: see `git log`, "118:")
- `PlaygroundCapture.cs` `Start()`: `-shadowclamp M` is now parsed for every mode. Before, only `-sustained` / `-stutterprobe` read it, so
  cost-split and look-review silently ignored it.
- `PlaygroundCapture.CostSplit.cs`: arm `clampN` (e.g. `-costarms clamp128,clamp128`), plus a `[sun <arm>]` counters line (voxel shadow
  rays traced / steps, ShadowStats 2 / 4).
- `PlaygroundCapture.LookReview.cs`: `-lookclamp M` (with `-lookgolden 1`). At golden hour, with the sun behind (away) and beside (side) the
  camera, it shoots fsr3 / none / sunvis, `_full` vs `_clamp<M>`.
- TASKS.md paragraph; PERF_30FPS_PLAN.md step 4 row annotated; driver `Screenshots/118/118-driver.sh` (part 2 not run).

## Evidence
- Settings: play's: High (shells default preset), 540 rows, FSR 3, shells ON (3 workers), far band + far shading, see-through, river look.
- Golden hour: 17 h, sun elevation 15 deg. Poses: walk_near, walk_open, ridge, high. One launch: `Logs/lookreview_20261023_014030.txt`.
- No-upscaler and sun-visibility stills, full vs clamp128: **0 px differ in 15 of 16 pairs**.
  - The exception is walk_open away `none`: 12,901 px in the bottom-left lowland, where shell 4 was still filling (627 chunks missing).
    Its sunvis pair is 0 px.
  - The FSR 3 pairs differ by jitter only (mean |d| 0.3-0.6 of 255).
- Shells at the 60 s cap: walk_near s3-s7 partly missing (2,780 chunks); walk_open s4-s7 missing (1,350); ridge and high complete.
- TIMING SKIPPED: the kernel input is identical (min(1280, 1280) = 1280), so every possible result leads to the same action (CLAUDE.md
  "Measure only if outcomes differ"). No warm-up, cost-split or MODE=walk ran.
- Fast suite: `PASS 900 FAIL 0 SKIP 14` (5m25s).

## Images
- `Screenshots/118/sheet_ridge_side.png`: ridge pose, golden hour, sun beside the camera. Top row = FSR 3, bottom row = sun-visibility
  view (white = sunlit); left = today, right = clamp 128.
  Left and right are identical. Look at the shadow edges cast across the near staircase.
- `Screenshots/118/sheet_high_side.png`: high pose (3 km view), no upscaler, today (left) vs clamp 128 (right).
  The long shadows of the grey mountains on the right (on the sea and lowland, km away) are identical in both.
  They come from shell and far-band shadows, which have no clamp.
- `Screenshots/118/walk_open_golden_away_none_diff.png`: today / clamp / changed-pixel mask (white).
  The only changed patch is bottom-left lowland, from shell streaming.
- All raw shots: `Screenshots/118/look/` (git-ignored).

## Next
- Owner question above. If (a): queue row 5 (b) temporal reuse 1-in-8, judged on moving shadows at golden hour.
- The `[sun]` counters line is ready for it.

## For the doc
Task 118: shadow clamp 128 m. It is already in effect in play, so there is nothing to gain.
- Voxel sun-ray cap in play (shells ON): **128 m before = 128 m after**. 290 m applies only with the shells off.
- Golden-hour stills, full vs clamp 128, no upscaler + sun-visibility view: **0 px differ** in 15 of 16 pairs (1 pair: shell streaming,
  sunvis 0 px). Source: `Logs/lookreview_20261023_014030.txt`.
- Frame time: not measured (identical shader input).
- Images: `Screenshots/118/sheet_ridge_side.png` (ridge, sun beside: FSR 3 top, sunlit mask bottom; left today, right clamp; identical).
  `Screenshots/118/sheet_high_side.png` (3 km view: far mountain shadows on the sea, unchanged, because shell/far shadows are not clamped).
