STOP_FOR_OWNER -- task 162: the resolution ladder sheets are ready. **Q (for the doc, with the sheets below): keep dyn 432-540 / raise the floor to N / native is worth it at any cost?**
- **(a) keep dyn 432-540 (RECOMMENDED).** 30 fps: playwalk p50 32.6 ms (159), the goal stays ~3 ms away, and diet 2 (161, preliminary -5 to -7 ms) could close it.
  Picture: every FSR height is about equally far from native. At walk_open, 720 differs from native by 3.10 and 432 by 3.96 (mean |dlum|). So most
  of the gap to native comes from using FSR at all, and only a small part from the row count.
- **(b) raise the floor to N.** 540 = 159's old arm, p50 47.4 ms (measured): 30 fps is lost by ~17 ms. 648 ~65 ms and 720 ~79 ms (ESTIMATES, below):
  30 fps is out of reach on this Mac with every cut planned so far.
- **(c) native 1080 at any cost.** ~165 ms (ESTIMATE), about 6 fps while walking. That drops the 30 fps goal.

# 162-look-res-ladder-vs-native: DONE (capture-only, no default change). 5 poses x 5 heights in one launch, plus bursts; sheets in Screenshots/162/

## What changed
- `f83ff32`: `-lookladder native,720,648,540,432` (LookReview + `RowsLook.cs` `LadderArms`; generalises 156's `-lookrowsab`, which is unchanged),
  `-lookladderbursts` (default native,540,432); `tools/ladder162.py` (sheets + numbers); TASKS "Task 162"; `Screenshots/162/ladder162.txt`, `lookreview162.txt`.
- Fast suite `PASS 971 FAIL 0 SKIP 15` (`Logs/overnight/162_fast.out`). Launch: `Logs/lookreview_20261031_210547.txt` (High preset, noon with the sun
  behind except torch at 0.5 h; dispatch confirmed per arm: 1920x1080 / 1280x720 / 1152x648 / 960x540 / 768x432). Shot dir
  `~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/2026-10-31_210933_high/`.
- Poses = 156's five: walk_near, walk_open, high ("high #1"), cliff ("high #2", 156's cliff-foot pose), torch (night, 32 torches).

## Table (vs native 1080: mean |dlum| 0-255 / % px > 8 levels; frame time from EXISTING logs only)
| height | walk_near | walk_open | high | cliff | torch | still flicker | walk p50 |
|---|---|---|---|---|---|---|---|
| native 1080 | ref | ref | ref | ref | ref | 0.00 | not measured (est. ~165 ms) |
| FSR 3 720 | 0.82/1.4% | 3.10/8.0% | 2.30/6.5% | 1.29/2.1% | 0.88/1.2% | -- | not measured (est. ~79) |
| FSR 3 648 | 0.85/1.5% | 3.19/8.2% | 2.36/6.9% | 1.37/2.3% | 0.92/1.3% | -- | not measured (est. ~65) |
| FSR 3 540 | 0.90/1.7% | 3.56/9.5% | 2.52/7.8% | 1.46/2.6% | 1.04/1.6% | 0.30 | 47.4 ms (159 A, battery); 146 warm 45.7/43.6 |
| FSR 3 432 (play) | 0.99/1.9% | 3.96/10.8% | 2.85/9.3% | 1.58/2.9% | 1.15/1.9% | 0.36 | 32.6 ms (159 B, battery, 81-89% at 432) |
Estimates are NOT measurements. They are a straight-line extrapolation of 159's ABBA (540 -> 432: -14.7 ms for -186,624 px = ~79 ms per M px) to each
height's pixel count; for native, FSR's ~5.5 ms (160) is subtracted. Treat them as order of magnitude only.
Motion (walk_open, 4 frames after 60 lead frames, 159's metric): walk flicker native 21.84 / 540 20.09 / 432 19.81; turn 20.43 / 18.78 / 18.48.
Under motion this number is mostly the real image motion, so FSR scoring slightly LOWER than native means it is smoother, not that it flickers more.
The still burst isolates FSR's jitter residue: 0.30 (540) vs 0.36 (432), both far below 1 level.

## What the images show (looked at: walk_open, torch, crops, motion)
- In every row, the step from native to FSR is larger than the step between FSR heights. Native shows hard, crisp (aliased) block edges. All four FSR
  tiles show softer blocks, with a faint dark grid/dotted outline along mid-distance block edges. That outline grows slightly from 720 to 432.
- The 3 crops are all walk_open's sunlit desert hillside, mid-distance (the most textured, highest-contrast area). At 432 the stair edges smear more
  than at 720, but none of the FSR heights matches native.
- Torch at night: the 5 tiles are nearly identical (diff about 1). The torch glows and water reflections look the same.
- Caveat: at walk_near, shells s5-s7 were still missing 1,014 bricks at the 60 s cap. Shells keep streaming across the 5 arms (~300 frames), so part
  of walk_near's small diff can be far-shell fill, not resolution. The other poses were full or missing <= 41.
- Side note (not an option asked): native's lead over FSR 3 at 720 suggests FSR 3 itself (its AA/reconstruction) softens, not only the input rows.
  FSR 3 at native input (AA-only) is outside `MaxRows` 720 and was not shot.

## Next
Owner answers the question. (a) changes nothing. (b) means changing `PlayDynFloor` (one constant + PlaySettingsTests). (c) is a design change to the 30 fps goal.

## For the doc
- Mean |dlum| vs native at walk_open (the worst pose): FSR 3 720 3.10 -> 648 3.19 -> 540 3.56 -> 432 3.96; % px > 8 levels 8.0 -> 10.8% (`Screenshots/162/ladder162.txt`).
- Calmest poses (walk_near / torch): 432 is within 1.0-1.15 of native, 720 within 0.82-0.88.
- Still-camera FSR jitter flicker 0.30 (540) vs 0.36 (432) levels; native 0.
- Walk p50: 432 dyn 32.6 ms vs 540 47.4 ms (159, measured on battery); 648 / 720 / native NOT measured (straight-line estimates ~65 / ~79 / ~165 ms).
- `Screenshots/162/ladder162_walk_open.png` -- left to right native / 720 / 648 / 540 / 432. Look at the magenta boxes on the desert hillside: native
  is crisp, and the four FSR tiles look nearly alike.
- `Screenshots/162/ladder162_crops.png` -- 3 rows of 3x crops (walk_open hillside), native in the left column. See how much softer every FSR column
  is than native, and how little 720 -> 432 changes.
- `Screenshots/162/ladder162_motion.png` -- rows native / 540 / 432; columns still / walk / turn (frame 0 + flicker x4). The still flicker column is
  black for native and nearly black for both FSR heights.
