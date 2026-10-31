STOP_FOR_OWNER -- task 160 is a plan. Pick the order and two approvals below (recommendation first).

**Q1 Order (one task per cut, checkpoint after each, AC power only).** (a) **Recommended:** B kernel diet 2 (with the missing data in the SAME
launch) -> D or C1, picked by B's arms -> the other -> E upload pacing -> milestone walk on AC; C2 only if cliff views are still > ~38 ms.
(b) C1 first (more certain mechanism, but needs Q2). (c) Stop the 30 fps work at "p50 <= 30" after B-D, and go on to lighting / world gen.
**Q2 GPU scratch for C1** (shadow continuation out of the big kernel): ~3-6 MB at <= 540 rows, like R1's 9.9 MB. (a) **yes**; (b) no -> C1 dropped.
**Q3 E (upload pacing):** the picture at rest is identical, but during streaming a far chunk can appear 1-2 frames later (it is already
asynchronous). (a) **counts as exact, go**; (b) no.
No layout additions are recommended (R5's bitmap is listed but NOT recommended: 130's E2 says the shell march is execution-bound).

# 160-plan-exact-30fps-cuts: DONE (plan only) -- `PERF_EXACT_CUTS.md`; exact cuts reach p50 <= 30 (est.), NOT the <= 5 %-over-33 ms line

## What changed (branch engine-phase)
- `PERF_EXACT_CUTS.md` (new), `TASKS.md` "Task 160" paragraph, `PERF_30FPS_PLAN.md` status line points to it. Docs only, no code; fast suite not run (nothing to test).
- **No launch.** The Mac was **on battery at 15 % and discharging** (`pmset -g batt` at ~task start). A build + warm-up + cost split (~1 h) could
  run it flat, and battery numbers aren't comparable to AC (task 08). I wrote a capture-only `skyprobe_off` cost arm plus the cost split's
  432-row clamp (`CostRows` -> `UpscalerSelect.CaptureMinRows`), then reverted both. They are spelled out in the plan's section 4, step 1, for cut B's task.

## Evidence (existing logs only, re-read)
- Pass split, AC warm, 540 rows (`Logs/costsplit_2026-10-25_115751.txt`, 146): sun shadows 17.2 / 5.5 ms (near / open), shell path 8.8 / 5.5,
  FSR 3 5.7 / 5.4, CPU + fixed floor 7.1 / 4.4, HUD + minimap ~0. Residual (tier-0 march + shading + sky probe + haze + buffers) ~22 / ~13 (est., subtraction).
  Inside the sun cost: near shell continuation 6.1 / 1.1 (133), far shadows 2.0 / 0.35 (93b3), shell-hit sun 1-3 (110 / 93b4).
- **The vertical sky probe (a TraceShadowRay per tier-0 hit pixel, every frame, no reuse, ~4x the sun's traced rays) has never been timed.**
- Key pattern: iteration / ray-count cuts paid ~0 (116, 93b5, 153, 137; 124's reuse 8 halved sun rays for 0 ms), while structural cuts paid
  (132 R1 -4.2 / -6.9, 134 R6 -7.1 / -5.5; 130: the march alone costs ~45 % of in-kernel). `CSMainBody` still inlines ShellMarch 3x (one copy
  dead while the pre-pass is ON) and TraceShadowRay 2x, and compiles OFF paths: traversal modes 0/2/3, the tier cascade (off with shells), debug views 2-11.
- Spikes: 115 attribution (pacing pairs 55-60 % net ~0; streaming 19-25 %; GPU waits with > 300 KB shell uploads 16-22 % = most > 100 ms
  frames); 123 slicing did not move p99. 150's unexplained ~2 ms (540) is still open: est. ~1.5 ms at 432, absorbed by dyn, so not ranked.
- Task 59: roof skip is exact and cuts steps 39 %; it lost only to a ~3 ms full rebuild every frame. Its option (c), an incremental rebuild, was never tried (cut D).
- `tools/capture-gpu-trace.sh` (xctrace per-encoder GPU time; Xcode-beta is in ~/Downloads) is unused in the 30 fps work. Step 1 adds one trace.

## Next
- Owner answers Q1-Q3; the supervisor queues B (perf task with the arms listed in the plan's section 4, step 1). The next perf milestone must be on AC.

## For the doc
- Starting point after 159: walk p50 32.2-33.1 ms, p95 61-75, > 33 ms 47-50 % (battery, `Logs/tg2world_20261031_{110242,110933}.txt`).
- Steady at 540 (measured) -> 432 (est.): walk_near 61.0 -> ~47 ms, walk_open 36.1 -> ~28 ms (`Logs/costsplit_2026-10-25_115751.txt`).
- Biggest passes at 540: sun shadows 17.2 ms (near), residual tier-0 + shading ~22 ms (est.), shells 8.8, FSR 3 5.7 (fixed at any row count).
- Exact cuts B + C1 + D (+ I): walk_near ~47 -> ~38-44 ms est., walk p50 -> ~28-31 est.; p95 est. ~45-55 (target ~33): **the 5 % line needs
  -5..-11 ms more on cliff views than any exact cut listed.**
- No images: plan only, nothing visible changed. Reference view for the slow tail: `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/UpscalerClips/2026-10-25_115751/stills/walk_near_full.png`
  -- a canyon wall fills ~90 % of the frame with sky only top-right; this cliff-facing view is the one the 5 % line depends on.
