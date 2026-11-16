# 215-defaults-dashfix-2-on: DONE -- the white-dash fix `-dashfix 2` is the default in play, rigs and capture; `-dashfix 0` = old

## What changed (commit 6c40bb0)
- `RaymarchFeature.DashFix` now parses `-dashfix` with default 2 (was 0). Same static feeds play, every rig and capture.
  Only the voxel / shell shading call changes; the far band's call is untouched (task 205's shader, unchanged). F6 has no dashfix field.
- `-dashfix 0` = the old `CubeFilterWeight` call, byte for byte. Survey arm suffix `_df0` still forces it per shot.
- `DashFixTests.Flag_DefaultOff_LastMatchWins` is now `Flag_Default2_LastMatchWins` and pins `"-dashfix", 2`. Comments updated in `RaymarchFeature.cs` and `Raymarch.compute`.
- KNOWN_ISSUES "WHITE DASHES ON STEEP FACES" is now marked FIXED by default (task 215), with this check added. TASKS.md "Task 215" paragraph added.

## Evidence
- Fast suite before (HEAD 627dedd, my edits stashed): PASS 1049 FAIL 0 SKIP 23 (`Logs/overnight/215/fast_before.out`).
  After: **PASS 1049 FAIL 0 SKIP 23** (`fast_after.out`, includes `Flag_Default2_LastMatchWins`).
- One build, ONE launch using 205's flags plus `-gpupoolprobe 1` (`Logs/overnight/215/shoot.sh`, output `look.out`, player log `player.log`).
  Arms `native_df0, native, fsr3_dyn_df0, fsr3_dyn` at fixed_forest_breaks, cliff_snow and cave_mouth. `-dashprobe 1` predicts the dashes from the first (old) arm.

| pose | predicted dash px | still dashing: native old -> default | FSR 3 dyn old -> default | px changed > 8 levels |
|---|---|---|---|---|
| cliff_snow | 1,296 | (prediction arm) -> 6 | 12 -> 1 | native 1.09 % (205: 1.05 %) |
| forest_breaks | 374 | (prediction arm) -> 0 | 15 -> 0 | dyn 0.51 % (205: 0.42 %) |
| cave_mouth | 0 | -- | -- | native 0.16 % (205: 0.16 %) |

- The forest NATIVE pair cannot be compared (8.2 % changed). The first arm was shot before the far wall's near detail streamed in: its ledges, ore specks and trees appear only in the later frames (`Screenshots/215/z_forest_ur.png`, top = first arm, bottom = later arm). The adjacent FSR 3 dyn pair is the clean comparison for that pose.
- Pictures I looked at:
  - `Screenshots/215/sheet215.png`: rows are forest (dyn), cliff_snow (native) and cave_mouth (native); left column = `-dashfix 0`, right = default. Apart from the dashes, the two columns match. The only other differences are streaming: a far spruce at the left of cliff_snow and a few far shrubs at cave_mouth.
  - `Screenshots/215/zoom215.png`: dash windows enlarged 4x. In the top row (cliff_snow ledge) the bright white stepped line is gone on the right. In the bottom row (forest wall, dyn) the faint light line at the bottom edge is gone. Side faces and the dark undersides are unchanged.
- Rule 11 (memory): no new storage, since the change is one shader int. Both arms ran in one process, so there is a single set of ledger numbers:
  - GPU body pool peak 145,173 of 254,400, refused 0.
  - CPU pool peak 423,881 (valve line 484,500).
  - Process peak 4,477.2 MB. This is over the 3,000 MB cap, but it comes from the `-worldsurvey -dashprobe` CPU oracle, not from the fix. 205's identical-probe launch (still `-dashfix 0` by default then) peaked at 4,448.9 MB (`Logs/overnight/205/look_a4.out`). In this launch the peak was 2,591 MB after the first pose and rose with the oracle poses.
- No frame timing (rule 10).

## Left / next
- Nothing unfinished. Optional: the dash survey's process peak (~4.4 GB) is a probe cost and would only matter if someone runs `-dashprobe` on a busy Mac.

## For the doc
- Default is now `-dashfix 2` (white 1-px dashes on steep sunlit ledges removed; `-dashfix 0` = old).
- Predicted dash pixels that still show a dash, in play's FSR 3 dyn: cliff_snow 12 -> 1, forest_breaks 15 -> 0. At native, cliff_snow 1,296 predicted -> 6, forest_breaks 374 -> 0 (`Logs/overnight/215/look.out`).
- Rest of the picture: 0.5-1.1 % of pixels change (forest / cliff, darker tread tops); cave 0.16 %, i.e. caves untouched.
- Memory: no new storage; GPU refusals 0, CPU pool peak 423,881 under the 484,500 valve.
- Images:
  - `Screenshots/215/zoom215.png`: 4x zoom; left = old, right = new default. Top row (cliff_snow ledge): the white stepped dash line is gone. Bottom row (forest wall, FSR 3): the faint light line along the bottom is gone.
  - `Screenshots/215/sheet215.png`: three poses, left old / right default. Nothing else in the look moves; the stray far spruce and shrubs are streaming.
