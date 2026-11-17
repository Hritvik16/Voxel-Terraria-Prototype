# 225-look-trunks-not-black: DONE -- cause found and measured (two exact defects that multiply), both fixed and ON, commit 18dc60d
NOTE for the supervisor: `tools/overnight/PAUSE` (from the watchdog's swap kills on 2026-11-16) is still there; clear it when the Mac is fit.
This task needed an 88-min day launch: the Mac swapped (7 of 8 GB) and frames ran at 100-190 ms.

## Cause (step 1): measured, not guessed
New capture-only probe: SHADE TERMS debug views 15-18 (`-shadeprobe 1` in the cost split; decoder `Logs/overnight/225/probe.py`). The table
shows medians for shaded trunk side faces (display-space lumas):
| pose | albedo | skyVis (S sky / B bounce) | ambient | direct | lit | on screen |
| forest_eye | 0.82 | 0.40 (0.25 / 0.57) | 0.147 | 0.020 | 0.147 | 35/255 |
| jungle_eye | 0.25 | 0.24 (0.19 / 0.29) | 0.087 | 0.012 | 0.030 | 2/255 |
Candidates ruled out: (a) albedo -- the forest trunks are bright (0.82) and black anyway, and jungle wood's 0.25 is authored; (b) "sky fill too
coarse" -- the sky ray is the voxel-exact `TraceShadowRay` DDA; (c) the shadow term is applied once; (d) side faces DO get bounce (B = 0.29-0.57).
What IS wrong, two things that multiply:
1. `LookToLinear` = pow 2.2, but URP's output encode is the exact sRGB curve (probe ramp: byte 2/255 -> 22, not pow's 28). The look's
   stated contract is "the encode lands back on the Brickmap's value". It failed below ~0.1: 0.030 -> 0.006 on screen (5x darker),
   0.067 -> 0.034. Shaded trunks are exactly in that range.
2. Sky fill (193) made skyVis a FRACTION of linear light, but it still enters the display-space `lerp(floor, 1, skyVis)`, so the light
   scales as skyVis^2.2: 25 % open sky gave 4.7 % of the sky light. The old vertical probe was 0/1, where the two spaces agree.
Proof by switching each term alone (same launch, `Logs/overnight/225/costsplit_day2.txt`). Median screen luma (bytes):
| group | before | only fix 1 | only fix 2 | both |
| jungle trunk side, shade | 2.1 | 7.6 | 8.0 | 15.9 |
| jungle trunk side, sun | 8.5 | 16.1 | 16.9 | 25.0 |
| forest trunk side, shade | 37.9 | 42.0 | 60.9 | 61.9 |
| forest sunlit grass (control) | 124.2 | 123.6 | 127.4 | 126.6 |
The pre-launch per-pixel simulation (`sim.py` on the probe data) predicted 16.0 and 59.8 for the "both" column; measured 15.9 and 61.9.
## Fix (step 2): exact, derived from the wrong term, not tuned to these poses
- `LookToLinear` = the sRGB EOTF; new `LookToDisplay` (its inverse) replaces the two `pow(x, 1/2.2)` undo sites (DrawnEdgeFade, TAA dyn track).
- `LookSkyOpen(skyVis)` = lerp(floor, 1, skyVis^(1/2.2)), used at the look-1 shade site and SeeBehindColour. 0 and 1 are unchanged:
  open ground, shells/far hits (skyVis 1) and sealed caves (0) stay as before. Look 0 (linear lighting) is untouched.
- Switches: `-lookexact 0|1`, `-skylinear 0|1` (`RaymarchFeature.LookExactSrgb/SkyShareLinear` -> `_LookFix` bits). Both are ON in
  play, rigs and capture. Capture arms: `lookfix_off` (= before), `lookexact_off`, `skylinear_off`.
- Adversarial cases (desert sinkhole cave_mouth, 215's survey pose): a cactus (trunk class) in open sun 68.4 -> 70.6, open sand 191.0 -> 190.8,
  pit interior 46 -> 55 (part-open mouth, still dark). Night torch_camp: median 17.0 -> 24.2, black px 46.8 -> 28.5 %, torch pools the same.
- Files: Raymarch.compute, RaymarchFeature.cs, PlaygroundCapture.CostSplit.cs (arms + `-shadeprobe`), LookFixTests.cs (4 tests), TASKS.md.
## Evidence
- Fast suite `PASS 1066 FAIL 0 SKIP 25` (`Logs/overnight/225/fast.out`); `LookFixTests` 4/4 (flags, shader text, curve vs
  `Mathf.GammaToLinearSpace` at all 256 levels, the crush numbers).
- Launches: day2 (walk_near, forest_eye, jungle_eye, cave_mouth x 4 arms) and night2 (torch path, FULL vs lookfix_off).
  Stills are in `Screenshots/225/day2|night2/`. Comparison: `python3 -I Logs/overnight/225/compare.py day2 night2`.
- Rule 11/12: P1: no buffer and no pass (the switch only changes shader arithmetic, so the OFF arms in the same launch have identical
  memory). Process peak 2,492 MB (day2) / 2,515 MB (night2) vs 2,627-2,630 MB in the pre-fix launches. CPU pool peak 496,333 (valve
  484,500), the same as before. GPU refusals 0 at walk_near; 5,104 after forest_eye -> jungle_eye. That is NOT comparable to day1's 738,
  which skipped forest (the pool was already at 245,704 after forest). A path effect, not this change: no streaming or pool code is
  touched. P2 pass card: no new pass. Every look-1 pixel does 1 select + 3 pow in place of 3 pow in LookToLinear, plus 1 pow in
  LookSkyOpen. Render rows, every frame, 0 GPU bytes, step "sky fill" share unchanged. P3/P5: n/a (no ray-march change, no frame timing).
  P4: no content change. Not HEAVY.
## Left / next
- Jungle shade is no longer pitch black, but it is still darker than Lay of the Land. That is now honest arithmetic: a 76 % blocked
  hemisphere, dark wood (5 % reflectance) and ONE bounce (sunlit ground only). Rays that hit shaded ground, leaf undersides or other trunks
  return 0. LotL gets its green/warm shade from multi-bounce GI. Closing the gap is a design choice for the owner (e.g. a second bounce /
  a canopy-transmission term), not a fix.
- Night is lighter in its darkest parts (fix 1 is global). If the owner prefers the old crushed night, `-lookexact 0` restores it.
- Not committed (not mine): `Assets/Scenes/Playground.unity` (regenerated by the build's scene builder), `Logs/TerrainGen/*` edits.
- The 225 day1 "cave_mouth" (4306,854,3892) was a mountaintop. run2.sh uses 215's real survey pose.

## For the doc
- Why trunks were black: two bugs that multiply. (1) The look's last step (pow 2.2) is not the inverse of the screen's sRGB encode,
  so everything below ~10 % brightness was crushed up to 5x. (2) Since sky fill, a "25 % open sky" gave only 4.7 % of the sky's light.
- jungle_eye shaded trunk sides: 2 -> 16 /255; sunlit trunk sides 8.5 -> 25; black pixels 79.8 -> 35.6 % (`Logs/overnight/225/costsplit_day2.txt`).
- forest_eye shaded trunk sides: 38 -> 62 /255; black pixels 12.9 -> 3.5 %; sunlit grass unchanged (124 -> 127).
- Unchanged where it should be: open sand 191 -> 191, a cactus in full sun 68 -> 71. Night torch camp: median 17 -> 24, black 47 -> 29 %.
- Memory: no new storage. Peak 2,492 MB (pre-fix launches 2,627-2,630). No new GPU pass.
- `Screenshots/225/sheet_before_after.png`: left column before, right after, rows walk_near / forest_eye / jungle_eye / cave_mouth /
  torch_camp (night). Look at the jungle row: trunks go from black silhouettes to brown bark, and the under-canopy ground turns green.
- `Screenshots/225/sheet_lotl.png`: before | after | Lay of the Land. The forest row shows grey trunks with bark detail after the fix.
  The jungle row is still darker than LotL's GI-lit shade (see "Left / next").
- `Screenshots/225/diff_jungle_eye.png`: pixel diff x4. Bright = changed. Trunks and shaded ground light up; the sky stays black (unchanged).
