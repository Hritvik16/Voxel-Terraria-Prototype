STOP_FOR_OWNER -- Q-245: make the foliage light early-out the default? **Recommendation: NO (keep it OFF; close lever F4).**
It saves 7-8 % of forest sun + sky ray steps at eps 1/8 (13-18 % at 1/2), but it is wrong a third to a half of the times it fires:
crowns are clumps with gaps, not a random medium, so the rays it stops would often have reached the sun. Under the oak the dapples
nearly vanish (lit sun rays 4 % -> 1 % at 16 deg). (a) keep OFF, close F4 (recommended); (b) default ON at eps 1/8 and accept the look;
(c) other (e.g. a per-brick "gap" statistic instead of fill: new design work).

# 245-foliage-light-early-out: DONE behind -foliageout 1 (default OFF); data says it does not pay -> Q-245

## What changed (commit 49c3a32)
- `Mirror/FoliageDensity.cs` (new): SIDE BUFFER, no brick-layout change. 1 byte per GPU body = leaf voxels (183-186, 216), (n+1)>>1; 8 bytes
  per shell body (full, or one per compact sub-brick). CPU-written (Burst count of the very words uploaded) in `TerrainClipmap.WriteBrickRunAt`
  and `ShellStore.Continue`; one `FoliageDens` buffer, dirty 4 KB blocks uploaded once a frame (RaymarchFeature).
- `Raymarch.compute`: each air cell of a dense brick multiplies a leaf transmittance by (1 - f); below `_FoliageEps` = blocked. In
  `TraceShadowRay` (sun + sky-fill visibility leg; never the ground-bounce leg) and `ShellMarch` (shadow rays). Far-shading kernels only
  (`FOLIAGE_ON`, +1 Metal slot: CSMainFarShade 30 of 31). RayStats [5] / table column `folstop`.
- Cost-split arms `foliage_off`, `foliage_eN`; `-foliageeps`, `-rsnoident 1`. Tests `FoliageDensityTests` (5). TASKS.md paragraph.
- NOT done: the CPU twins (ShellRayReference / ShadowRayReference) do not model the early-out (it is OFF by default).

## Evidence
- Whole suite PASS 1207 FAIL 0 SKIP 37 (`Logs/overnight/245/all.out`). Player: every kernel loaded (`[kernels]` no FAILED, both launches).
- Calibration (CPU, forest_eye's 100 generated chunks, 4 sun heights, floor + surface receivers; `Screenshots/245/calibration245.txt`):
  rule "largest eps whose stopped rays would have been lit <= 5 %" -> NONE: P(lit | stopped) 27-58 % at every eps 1/2..1/128.
  Brick-fill threshold (the task's other option): >= 50 % never fires; >= 25 % wrong 6-52 %, saves 1.5-3.5 % of dense-cell steps.
- GPU counters (counts, not time; budget settings; `Screenshots/245/costsplit_{16,noon}.txt`), per frame, OFF -> eps 1/8 -> eps 1/2:
  | pose | sun steps | sky steps | sun rays blocked |
  | forest_eye 16 deg | 9.32 M -> 8.62 M (-7.4 %) -> 8.07 M (-13.4 %) | 2.01 -> 1.86 (-7.5 %) -> 1.66 M (-17.5 %) | 64 -> 67 -> 70 % |
  | forest_eye noon | 4.40 M -> 4.06 M (-7.7 %) -> 3.73 M (-15.1 %) | 1.77 -> 1.65 -> 1.45 M | 32 -> 34 -> 38 % |
  | oak_under 16 deg | 2.20 M -> 1.36 M (-38 %) -> 0.66 M (-70 %) | 1.38 -> 1.24 -> 1.10 M | 96 -> 99 -> 100 % |
  | oak_under noon | 0.72 M -> 0.55 M (-23 %) -> (broken shot) | 1.04 -> 0.93 M | 94 -> 97 % |
  | walk_near (no trees) | unchanged (+-0.3 %) at both suns | unchanged | unchanged |
- Pictures (`Screenshots/245/diff245.txt`): forest_eye 16 deg 9.9 % px changed (1/8), 13.5 % (1/2), luma -1.5 / -2.1 %; noon 6.7 / 9.5 %;
  noise floor = walk_near (nothing fires): 0.5 % (16 deg) / 6 % (noon) px. oak_under luma -2.7 % (16) / -3.5 % (noon).
  BROKEN: `oak_under_e2_noon.png` and its counter window are a different pose (top-down river view; camera not at the pose) -- not relaunched.
- Memory (rule 11, ON vs 244's OFF launch at the same poses/settings): +5.58 MB GPU (254,400 B tier 0 + 5,323,776 B shells) + 5.58 MB CPU,
  only with -foliageout 1; process peak 2,512.7 vs 2,515.0 MB (16 deg; noise); GPU pool peak 245,704 = 245,704 at forest_eye, refused 0;
  oak_under refused 21 (243 OFF: 20-21); CPU pool peak 494,560 vs 494,389, valve 484,500. Within budget (refusals not up, peak +50 MB).
  The shell part is 21x the task's +0.25 MB estimate (665,472 shell pool bodies x 8 B).
- Pass card (P2): no new pass. Changed: the sun / sky rays of CSMainFarShade* (+Diag/Lean/Flood twins), CSSkyFillTrace, CSSunTrace,
  CSSunDeferLean; render rows (432 here), every frame (sun 1 in 4 temporal); per dense-brick air cell +1 multiply/compare, +1 buffer read
  per brick change; +5.58 MB GPU; AMENDMENT_8_17 step: sun + sky light. P3: no frame time printed (rule 10). P5: steps DOWN 7-18 % (no flag).
- Slice (rule 14): trees have no own slice (they take from sun + sky). Default OFF: 0 ms / 0 MB on tier A. ON: est. a few % of the
  sun + sky share at forest poses (guess; the counted step cut is 7-15 %), +11 MB.

## Next
Owner Q-245. If (a): nothing to do (code stays OFF; could be removed later). Timing tasks 238 / 239 unaffected.

## For the doc
Task 245 -- foliage light early-out (F4), behind `-foliageout 1`, default OFF. Verdict: does not pay for its look cost.
- Sun steps per frame, forest_eye 16 deg: 9.32 M -> 8.62 M (eps 1/8, -7 %) / 8.07 M (eps 1/2, -13 %) (`Screenshots/245/costsplit_16.txt`)
- Sun rays reaching the sun, forest_eye 16 deg: 36 % -> 33 % -> 30 %; under the oak 4 % -> 1 % -> 0 %
- Calibration: a ray the early-out stops would have reached the sun 27-58 % of the time, at every threshold (`Screenshots/245/calibration245.txt`)
- Memory: +5.6 MB GPU + 5.6 MB CPU when on; pool peak 245,704 = 245,704, refusals not up; process peak 2,513 vs 2,515 MB
- `Screenshots/245/sheet245.jpg`: rows walk_near / forest_eye / oak_under at 16 deg then noon; columns OFF | eps 1/8 | 4x diff | eps 1/2 | 4x diff.
  Look at the forest_eye diff columns: the change sits on sunlit leaf patches inside the crowns and trunks; the even ground grain is temporal noise
  (walk_near, where nothing fires, shows it too). Last row's eps 1/2 tiles are a broken shot (wrong pose).
- `Screenshots/245/viewer.html`: full-size flip viewer (Space = cycle arms, S = sun, Z = full size); look at the dapples under the oak (oak_under 16 deg).
