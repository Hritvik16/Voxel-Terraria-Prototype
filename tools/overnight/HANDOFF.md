# 119-look-far-rock-dashes: DONE. The far dashes are gone, but the cause was NOT rock: they were far SUN SHADOWS. Fixed both ways, ON by default.
One non-blocking owner question at the end (Q74): the shadow fix goes beyond the mechanism you approved.

## What changed (commit `51e107e`, branch engine-phase)
- **Diagnosis first.** Task 114 blamed `FAR_WALL_LIP` rock. I built the approved slope-rock rule and shot before/after: **0 px changed** in
  the dash crop (the pink plain is BadlandsClay, which `FarWallMaterial` does not even map). A `-farshadow 0` arm removed EVERY dash on the
  pink plain and the snow (`Screenshots/119/diag_noshadow_low/high_native.png`). Cause: the far band is a staircase of texel-wide steps,
  and the far sun-shadow ray (started 0.05 voxel off the hit) was shadowed by the next step up. That gives 1-px dark dashes at the foot
  of every step, and they crawl when moving.
- `Raymarch.compute`, far-shade kernel only (`#ifdef VE_FAR_SHADE`: play and `-farshade 1`; rigs keep their pixels):
  - (1) **`_FarShadowLift`**: the far sun-shadow ray starts one texel (`8 << fh.level` voxels) above the hit. A step no taller than its
    width (slopes up to 45 deg) no longer shadows its own staircase; anything taller than a texel still casts shadows. `-farshadowlift 0` = old.
  - (2) **`_FarRockSlope`** (the task's change): a far step side becomes rock where `FarSlopeNormal` is steeper than ~40 deg
    (`FAR_ROCK_SLOPE_NY` 0.766). The 1.5 m lip applies only where FarSlopeNormal has no answer. `-farrockslope 0` = old.
    It made no visible difference at these poses. Kept because it was approved and is harmless.
- `RaymarchFeature.cs`: `FarShadowLift`, `FarRockSlope` (both ON) and `ParseFlagOn`. New `FarRockSlopeTests` (3 tests: the flags' text,
  both shader blocks inside VE_FAR_SHADE). TASKS.md has a Task 119 paragraph. VISUAL_REVIEW_114 row #3 now has a correction line.

## Evidence
- Fast suite **PASS 897 FAIL 0 SKIP 14** (`Logs/overnight/119-tests.out`). Player build OK, no shader errors.
- `./run-look-review.sh`, same build, same poses as 114, noon + golden hour, FSR 3 at play rows. Before = `EXTRA="-farrockslope 0 -farshadowlift 0"`.
  - Low: before `Logs/lookreview_20261022_220344.txt`, after `Logs/lookreview_20261022_230756.txt`.
  - High: before `Logs/lookreview_20261022_231215.txt`, after `Logs/lookreview_20261022_231900.txt`.
  - Shots: `Screenshots/119/{before,after}_{low,high}/`.
  - No timing (rule 10). The owner paused heavy work 22:13-23:04; all four look launches ran after the pause was lifted.
- Dark-dash pixels on the pink plain (Low, high pose, native): **29.2% -> 5.2%**. What's left is real hill shading.
- Far-snow crawl, ridge walk burst, 4 frames: flicker **0.74 -> 0.43** (none), **0.57 -> 0.29** (fsr3). Pixels flickering > 24 levels: **0.93% -> 0.30%**.
- High noon: walk_near / ridge / high **0 px** changed (shells cover the frame at those poses); walk_open 1.2%.
- **Trade-off, visible:** at golden hour (High, walk_open away), the shadow over the far sea cast by land behind the camera now ends ~30 px
  nearer (`D_high_golden_away_sea_shadow.png`). Its edge is straight in both versions, so it is probably a far-ring edge. Occluders lose up
  to one texel of height (25.6 m at the coarsest level).

## Left open
- The shadow lift jumps at far-ring edges (one texel per level). A gentler option is a lift of `slope x tv` from FarSlopeNormal, or 0.5 tv.
  That is not done. Ask if the golden-hour edge matters.
- #10 (black dots on the sea) is still visible in the B crop; it is a separate defect.

## Owner question
Q74. You approved "rock by slope". The real cause turned out to be shadows, so I also shipped the shadow lift, ON by default.
- (a) Keep both ON (recommended: the dashes are gone; the cost is one shadow edge at golden hour).
- (b) Keep the lift but drop the rock rule. It had no visible effect at these poses.
- (c) Lift OFF by default (`-farshadowlift 1` to see it). This brings the dashes back.

## For the doc
Task 119: the grey dashes that crawled over far gentle land were the far band's staircase shading itself, not rock. The far sun-shadow ray now
starts one texel up, so the dashes are gone. Real shadows and slopes are still shaded.
- Dash pixels on the pink plain (Low, high, native): **29.2% -> 5.2%**.
- Far-snow flicker, ridge walk (4 frames): **0.74 -> 0.43** (none), **0.57 -> 0.29** (FSR 3).
- Slope-rock rule alone: **0 px** changed. `-farshadow 0` removed every dash, which is what identified the cause.
- `Screenshots/119/B_before_after_native6x.png`: top = before, bottom = after (Low, native, 6x). Look at the pink plain (centre) and the
  snow (left): the grey 1-px dashes along every step are gone, and the hill shading at the right stays.
- `Screenshots/119/A_before_after_trio.png`: rows before / after; columns FSR 3 / none / native. Look at the pink plain and snow behind
  the mountain: the dashes are gone in all three; FSR 3 was only hiding them while still.
- `Screenshots/119/C_ridge_walk_far_motion.png`: left = before, right = after; top = frame 0 of the ridge walk, bottom = 4-frame flicker
  (x4). Look at the far snowfield (top half): it is dashed before and clean after, and the flicker dots over it thin out. The bottom-left
  diagonal is the near mountain's real parallax (unchanged).
- `Screenshots/119/D_high_golden_away_sea_shadow.png` (the trade-off): golden hour, High; top = before, bottom = after. Look at the darker
  shadowed sea: its far edge sits ~30 px lower (closer) after.
