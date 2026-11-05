STOP_FOR_OWNER -- task 156: one question (30 fps plan step 3). Recommended answer first.

**Q1 Internal rows (FSR 3 renders fewer rows, then upscales to 1080p).** (a) RECOMMENDED: dyn 432-540 with a ~30 ms target. Rows
drop only on heavy views and stay at 540 on light ones. (b) Keep 540. (c) Go lower (405 or 360).
- Pictures: `Screenshots/156/rows156_walk_open.png` and `Screenshots/156/rows156_crops.png` (captions below). At full size the two
  halves look almost the same. In the 3x crops, 432 is a little softer on 1-2 px stair-step edges 40-80 m away and on the far cliff
  outline. Mean brightness difference is 0.4-2.0 of 255. Note: these are STILLS. FSR 3's extra ghosting and shimmer at a 2.5x upscale
  only shows in motion, and this task did not capture motion.
- Plan estimates (`PERF_30FPS_PLAN.md` row 3, est., unmeasured), ms saved near / open / walk:
  (a) 432: 11-17 / 8-12 / 7-12. (c) 405: 13-21 / 9-14; 360: 17-27 / 12-18.
  Honest reading: the plan's "~40 -> ~28" assumed steps 1-2 had already saved ms, and they saved ~0. From today's walk p50 of
  44.7 ms (task 146), 432 alone comes to an est. ~33-38 ms, still short of 33. 405/360 would be needed to get under it.
- 432 is below the 960x540 minimum in the 2026-10-01 brief (Q38 (a): 540-720), so this is your call.
- What each answer queues next: (a) one perf task: the dyn floor goes to 432 and `UpscalerSelect.DynTargetMs` to ~30 ms in play's
  dyn path, then a warm walk measurement (p50 / % > 33 ms). (b) Row 3 is dropped. Next is step 5 (spike isolation). 30 fps then needs
  shells OFF or sun shadows OFF (plan Q1 (c)). (c) The same stills at 405 and 360 first (`-lookrowsab 405` / `360`, one launch each,
  ~8 min), then the dyn-floor task at your chosen floor.

# 156-look-rows-432-vs-540: DONE (capture-only) -- stills + sheets at 540 vs 432 rows, five poses

## What changed (branch engine-phase, commit `50d22d4`)
- `UpscalerSelect.CaptureMinRows = 360`: a PINNED row count (`-renderrows N`, `Apply(k, N, false)`) now clamps to 360-720. Play
  cannot reach it: PlaySettings only passes 540/720/dyn, and the dyn controller still moves within 540-720. No default changed.
- `-lookreview -lookrowsab N` (`PlaygroundCapture.RowsLook.cs`, hook in LookReview): every pose shoots fsr3_540 and fsr3_N in the same
  frame state (clock paused, fluid suspended, 60 frames settle). Added poses: `cliff` (the nearest cliff foot with the sun behind) and
  `torch` (task 151's 32-torch scene at 0.5 h).
- `tools/rows156.py`: per-pose side-by-side sheets and 3x crops of each pose's 3 most-different 160x90 windows. TASKS.md paragraph.

## Evidence
- Fast suite: PASS 948 FAIL 0 SKIP 15 (`Logs/overnight/156_tests.out`).
- One launch, High preset, 1920x1080 fullscreen: `Logs/lookreview_20261030_154829.txt` (copy `Screenshots/156/lookreview156.txt`).
  Dispatch was 960x540 vs 768x432 at every pose. Shots are in `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte
  BrickMap/LookReview/2026-10-30_155224_high/`.
- Shells after a 60 s cap: walk_near was still missing 1,699 (s3-s7), walk_open 441 (s5-s7) and cliff 395; high and torch were full or
  nearly. Both arms see the same state, so the comparison still holds.
- Mean |luminance diff| 540 vs 432 (`Screenshots/156/rows156.txt`): walk_near 0.40, walk_open 1.99, high 1.58, cliff 0.71, torch 0.50.
- Looked at all five sheets and the crop sheet. The largest differences are inside the 0.1 m window, not at its edge: walk_open's three
  crops all sit in tier 0 (grey) on `Screenshots/156/rows156_walk_open_tint.png`.
- Torch pose: the start pose is on a slope, so the ring of torches sits low in the frame and only 3-4 torch pools are visible. It is
  dark but readable.
- Not done: a dedicated 0.1 m window-edge crop (the top-difference search never picked one), and motion clips.

## For the doc
- Internal resolution: 960x540 -> 768x432 (0.64x the traced pixels), FSR 3 to 1920x1080; `Logs/lookreview_20261030_154829.txt`.
- Mean |luminance diff| per pose: 0.40 / 1.99 / 1.58 / 0.71 / 0.50 of 255 (walk_near / walk_open / high / cliff / torch); `Screenshots/156/rows156.txt`.
- Plan estimate for 432 (unmeasured): walk -7..-12 ms, so 44.7 -> ~33-38 ms walk p50.
- `Screenshots/156/rows156_walk_open.png`: walk_open at noon, LEFT 540 / RIGHT 432. Look at the mid-distance stair-step slopes in
  the magenta boxes; 432 is a touch softer there, and the rest of the frame matches.
- `Screenshots/156/rows156_crops.png`: 3x crops, each row a pair (540 left, 432 right), two pairs per row. Look at the walk_open rows
  (soft 1-px step edges) and high #1 (the cliff outline against the green field). Night torch crops are near-identical.
- `Screenshots/156/rows156_high.png`: far-horizon pose (20 m over the highest peak). The distant islands and coastline are
  equally sharp in both halves.
