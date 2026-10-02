# Overnight handoff

**05-taa-shimmer-metric: DONE. Near-terrain shimmer metric built, baseline recorded, repeatable to +-0.01.**
(Still open from 04b: the surface rebuild plan's 11 questions -- see commit 82d4416's HANDOFF.)

## What changed (commit 4d0d03c on design-brief)
- `Assets/Game/PlaygroundCapture.NearShimmer.cs` (new): `-nearshimmer` capture mode (`-noslope` skips the slope pose).
- `PlaygroundCapture.FarBand.cs`: the eye-level pose math moved into `Tg2EyeLevelPoses` (same math, shared).
  `-farshimmer` and its report are untouched.
- `Playground.cs` / `PlaygroundCapture.cs`: flag added to the capture-flag lists and to the dispatcher.
- `run-tg2world.sh`: `MODE=shimmer` (`-renderpreset quality`, i.e. the default 960 preset) runs
  `tools/nearshimmer-report.py` and writes `Logs/nearshimmer_<run>.txt`.
- `KNOWN_ISSUES.md`: new top entry "NEAR-TERRAIN SHIMMER BASELINE, BEFORE TAA" (full table). One index line in CLAUDE.md.
- Queued `09-nearshimmer-eye2-wall-check.md` (evidence run only, see below).

## The metric (what task 06 compares ON vs OFF)
- Poses: eye_level_1..3, plus slope_walk (a candidate from the map, checked on the streamed voxels:
  centre ray 68.7 m, a cliff 30-80 m away). Noon, clock paused, 960x540 gate, temporal shadows ON.
- 4 nudges of a QUARTER GATE PIXEL (an offset in `cam.projectionMatrix`: m02/m12), each shot after
  20 settle frames, plus an unmoved repeat (`_still`) as the noise floor.
- Depth bands from a CPU DDA through the ChunkStore (1 ray per 4x4 px): near <50 m, mid 50-200 m,
  far = no voxel hit (sky masked out by the band view, debug mode 9).
- Report: mean |dL| on 0-255 luma, and % of pixels with |dL| > 24 (the far report's threshold).

## Evidence
- EditMode: PASS 626 FAIL 0 SKIP 3, 0 `error CS` (before and after). Build OK, 0 Shader error.
- BASELINE `Logs/nearshimmer_20261002_113842.txt`, mean of poses: near **4.14 / 5.84%**,
  mid 3.96 / 3.59%, far 6.38 / 8.36%, all non-sky **5.89 / 7.59%**.
- Repeatability: run B, same build (`Logs/nearshimmer_20261002_114820.txt`), differs in 3 of 64 values, each by 0.01;
  run 2, an earlier build (`..._112520`), matches too. Noise floor 0.00 everywhere: without TAA a
  still frame repeats exactly. **The metric is not noisy run-to-run.**
- Screenshots looked at: slope_walk_base (grass in front, a cliff at ~69 m, sky right); the
  xP diff map (changes only at sub-pixel voxel edges on the cliff and the grass -- the shimmer);
  eye_level_2 (open valley, a cliff close on the left); eye_level_3 (mountain, mostly far band).
  slope_walk shows two small dark shapes floating off the cliff edge -- not investigated.

## Bugs met and fixed inside the task
- A 0.03 deg camera ROTATION never reached the frame (the nudged PNGs were byte-identical), so
  the nudge is now a projection offset. That is also the jitter TAA uses.
- Golden hour left the near ground almost black, so the measure moved to noon.
- The sky colour in the band view is sRGB-encoded (38,38,48), so the mask accepts both encodings.
- A 3000-frame streaming wait ran out at high FPS (deficit 763), so the wait is now capped in seconds.

## Notes for task 06 (TAA)
- With TAA ON, the floor will not be 0 (jitter phase). Compare the nudge numbers with the floor.
- If TAA jitters through `cam.projectionMatrix`, it must ADD to the nudge offset, not replace it
  (this mode sets the matrix, then calls `ResetProjectionMatrix`).
- eye_level_1 is dim (low |dL| everywhere), and eye_level_3 is 86% beyond 200 m. The near-terrain
  signal comes mostly from eye_level_2's near/mid bands and from slope_walk.

## Unfinished / open
- UNEXPLAINED: in the FIRST run (eye_level_1 -> eye_level_2 as a direct jump), eye_level_2 looked
  into a wall 0.5-1.4 m away, in the frame AND in the CPU store's depth. That has not been seen since.
  Task 09 re-runs with `EXTRA=-noslope` to see whether it comes back. If it does, it is stale
  CPU-store content after a jump: a correctness bug.
- The committed code adds `-noslope` after the baseline build. Without the flag, behaviour is unchanged.

## Next step
Task 06 (TAA core): use `MODE=shimmer` ON vs OFF against this baseline. Task 09 can run first.
