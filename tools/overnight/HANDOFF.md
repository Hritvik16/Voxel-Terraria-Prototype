# Overnight handoff

**06-taa-core: DONE. TAA is in the raymarcher, OFF by default. With the switch OFF the output is unchanged. With it ON, stills shimmer much less and look softer. It has NOT been tested in motion, after edits, on fluid, or for cost: that is task 07.**

## What changed (commit 718dd03)
- `Raymarch.compute`:
  - A TAA uniform block, and a jitter on the primary ray (only when `_TaaEnabled`).
  - At the end of CSMain, each pixel writes its reprojection data: the previous-frame pixel of the exact hit point
    (far band and sky included), that point's distance from the previous camera, and a voxel+material key.
  - Two new kernels: `TaaResolve` and `TaaSharpen`.
- `RaymarchFeature.cs`:
  - `Taa` switch (`-taa 1`) and `-taasharpen <percent>` (default 25).
  - Ping-pong buffers, the 8-frame Halton jitter, the previous unjittered view-projection, a scratch target, and the two
    extra dispatches. With TAA off, dummy buffers are bound.
  - History resets on: a resize, a non-Beauty view, a teleport over 20 m, the sun moving more than ~1 deg in a frame, or a
    look / shadow-rung / tonemap change.
- `TerrainClipmap.cs`: an additive `UploadedChunks` list (nearest 16 chunks, recorded only while TAA is on). The resolve
  gives no history to pixels in those chunks.
- `Playground.cs`: F4 toggles TAA.
- The FarPreview and NearShimmer reports now state the TAA setting.
- `KNOWN_ISSUES.md` has a new TAA entry, and the `CLAUDE.md` index has a line for it.

## Evidence
- EditMode: PASS 626, FAIL 0, SKIP 4 (`Logs/overnight/06_editmode.out`). The build had 0 error CS and 0 Shader error.
- **Switch OFF, md5 check:** 55 of 56 `MODE=poses` shots are byte-identical to the previous commit (`FarBand/2026-10-02_150159_world` vs
  `_143337_world`; hashes in `Logs/overnight/06_baseline2_md5.txt` and `06_off_md5.txt`).
  - The 56th shot is `minimap_check_underground`. Its differences sit only in the minimap HUD's depth slice, which was still
    streaming in when the shot was taken. I opened the crops. It is not the raymarcher.
  - My first baseline (`_134244_world`) is INVALID: I edited Assets while that build was compiling. I rebuilt it clean from HEAD.
- **Switch ON, poses** (`FarBand/2026-10-02_152951_world/`). Every shot is taken after at least 90 still frames. I opened
  eye_level_1..3 noon ON vs OFF:
  - Sub-pixel speckle and moire on distant slopes, cliffs and snow turn into smooth texture.
  - Near blocks keep their edges, but are visibly softer.
  - Edge energy falls 1.40 -> 1.08, 5.08 -> 2.58 and 4.44 -> 2.51 for eye_level_1, 2 and 3.
  - I saw no ghosting and no holes. With a still camera, none was expected.
- **Shimmer, same build** (`Logs/nearshimmer_20261002_154753.txt` ON vs `_155606.txt` OFF). The OFF run equals the task 05 baseline exactly.
  Mean of poses, as mean |dL| / % of pixels over 24:

  | band | OFF | ON |
  |---|---|---|
  | near | 4.14 / 5.84 | 2.67 / 1.57 |
  | far | 6.38 / 8.36 | 3.55 / 2.50 |
  | all | 5.89 / 7.59 | 3.04 / 1.76 |

  - Worst pose: eye_level_2 all goes from 9.26 / 13.25 (OFF) to 4.12 / 2.01 (ON).
  - CAVEAT: with TAA ON the still-camera floor is no longer 0. It is 0.73-3.18 mean |dL| per pose (eye_level_2: 2.74 / 0.87%).
    That is the residual of the 8-frame jitter cycle at weight 0.1. So a still frame is not exactly repeatable, and part of
    every ON nudge number is this floor.
- No frame-time claim, as the task said.

## Not done / known limits
- Not tested: moving camera, edits, fluid, light changes, far seams, cost. These are all task 07's checks.
- Dirty-region rejection works per CHUNK (12.8 m), not per brick, and covers at most the 16 nearest chunks a frame.
  A chunk with live fluid therefore never builds up history, so TAA is effectively off there.
- The still floor could be lowered with a smaller still weight or a 16-frame cycle, at the cost of more ghosting. That is a
  tuning question for task 07 or the owner.
- `Assets/Scenes/Playground.unity` is still modified, because the BUILD=1 step regenerated it. Not committed, as before.

## Next step
- Task 07 (TAA risk checks) can start. The flag is `-taa 1`; F4 works in the Playground. Nothing is blocked.
