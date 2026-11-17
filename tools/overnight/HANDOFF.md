# 232-perf-sun-lowres-share: DONE, switch kept OFF. Exact low-res sun (`-sunres 4`) is built and correct but saves ~0 ms. Commits e70c1a9 (code + tests), 52e51b9 (runner, plan, reports)

## What changed
- `-sunres 1|2|4` (default 1 = today's path; the preset is unchanged). This only applies with a far-shading primary kernel that is not the sun-defer one.
  How it works: the primary kernel shades from the face's sun record (its own, or a PRIOR = at least 2 reprojected probes on the SAME face plane that agree
  within 1/16). Where neighbours disagree (shadow edge, penumbra) or nothing fits, it traces the exact ray inline. New kernel `CSSunTrace`
  re-traces one face per 4x4 block per frame, exactly. A prior that tracing proves wrong drops that block's other priors on that plane. Every value
  comes from some face's EXACT ray, with no coarse bricks, so 228's triangle field cannot happen.
- Efficient design (rule 11): no new G-buffer or UAV. It reuses task 56's temporal-shadow history (record 8 -> 12 B, to carry the face). The trace pass
  has one thread per block, and all its lanes trace. Shell-hit rays start at the face centre (< 0.05 m off), so one face = one ray.
- Arms `sunres_off|sunres2|sunres4`; the run line prints `sunres inline|4x4`. CPU twin `SunLowReference`, tests `SunLowReferenceTests`; `KernelHealth` lists CSSunTrace.
## Evidence
- Fast suite `PASS 1099 FAIL 0 SKIP 25` (`Logs/overnight/232/fast.txt`; 13 new cases). Twin: a slot floor never takes the ledge's light. A steep slope
  is exact under 3 suns (0 wrong on the slope). A thin pole near the camera is exact. Far shadow edges are wrong only within 1 voxel of the true edge
  (the straight wall: 0 wrong). A FAR sub-pixel pole line is missed in 1.7 % of pixels (all at its edge): a known trade.
- Timing, WARM steady state (warm-up `Logs/tg2world_20261117_134511.txt`), one cost split, ABBA, noon, High, 432 rows, play flags
  (`Logs/overnight/232/costsplit_day.txt`; plan written first: `PLAN.md`). The run lines confirm `sunres 4x4` on FULL and `inline` on sunres_off. p50 ms:
  pose         before (sunres_off)  sunres 4   sun off   noise(FULL range)  -> sun cost now / saved
  walk_near          62.1             60.6       42.3        1.3            -> 19.8 / 1.5
  forest_eye         95.8             98.3       78.0        8.9            -> 17.8 / -2.5
  jungle_eye         70.5             69.3       54.7       27.4 (a 90 ms pass)  -> 15.8 / ~0
  Per the plan: under 5 ms at walk_near, so the default stays 1. That is not a STOP. FULL is about equal to before, which the plan names as the "orphans" outcome.
  INFERRED, not measured: inline orphans still make whole SIMD groups wait (124's finding). They come from every shadow edge, the canopy dapple and
  far steep slopes, where FSR3 jitter shows new faces each frame and each step is its own plane: the twin's steep-slope case traces 78 % of
  pixels inline. Shrinking the rays per pixel does not cut time while one lane per group still traces.
- Pictures (`Logs/overnight/232/diff.txt`): clean mean|d| vs before 2.03 / 1.88 / 1.72, at the 228 noise floor (0.90 / 2.06 / 0.67).
  sunvis 1.91 / 2.76 / 0.53 (228's coarse: 15.3 / 6.9 / 1.5). The 228 hillside crop shows zero difference.
- Rule 11/12: P1 +2.65 MB whenever the history exists (play has it: the preset turns temporal shadows on; record 8 -> 12 B x 2 x 768x432), with the switch
  ON or OFF. -sunres 4 adds nothing more. Launch peak 2,566.3 MB (228: 2,639.0; 227: 2,564.9). GPU refusals 0 / 0 / 2,859 (228: 0 / 0 / 4,797). CPU pool
  peak 494,866 (228: 496,530). All arms run in one process, so ON and OFF share one ledger. P2 pass card, CSSunTrace: render rows (768x432), every
  frame, 1 thread per 4x4 block, 1 sun ray (tier 0 + shell continuation, step caps unchanged), <= 32 record reads/writes per thread, 0 new bytes
  beyond the history. 8.17 step S2 (sun), inside the sun's share. Only with -sunres 2|4. P5: no counter in the stripped build. Not HEAVY.
- Night torch_camp was not launched: the sun is below the horizon there (0 sun rays), so the picture is identical by construction (PLAN.md).
## Left / next
- Not committed (not mine): `Assets/Scenes/Playground.unity`, `Logs/TerrainGen/*`. Images git-ignored (`Screenshots/232/`).
- Next (owner/supervisor): the sun's ~16-20 ms is not in ray COUNT: 124 (halving), 228 (coarse far) and 232 (1/16 + reuse) all saved ~0-6 ms.
  Suggest measuring the inline-orphan share (a counter in a VE_ALL_KERNELS build) before any further sun-ray work. Another option is moving ALL sun rays
  out of the big kernel (sunres with no inline path, accepting 1-2 frames of lag at edges), which would need the owner's OK on look.

## For the doc
- Low-res sun (`-sunres 4`, OFF by default), warm ABBA p50: walk_near 62.1 -> 60.6 ms (noise 1.3), forest_eye 95.8 -> 98.3 (noise 8.9), jungle_eye 70.5 -> 69.3 (noise 27). No real saving (`Logs/overnight/232/costsplit_day.txt`).
- What the sun costs now (before minus sun off): 19.8 / 17.8 / 15.8 ms.
- Look: unchanged. Clean diff vs before 2.03 / 1.88 / 1.72, at the noise floor; no false shadows (228's coarse sunvis diff was 15.3 at walk_near).
- Memory: +2.65 MB (the history record 8 -> 12 B); peak 2,566 MB; refusals 2,859 at jungle (228: 4,797).
- `Screenshots/232/sheets/sheet_sunvis.png`: rows walk_near / forest / jungle; columns before | low-res 4x4 | sun off (white = lit, black = shadow). Columns 1 and 2 look the same: the canopy dapple and trunk shadows are in the same places.
- `Screenshots/232/sheets/walk_far_hill_sunvis.png`: 228's broken hillside at 3x, before | low-res | |d| x4. The right panel is black (no difference), with no triangle field.
- `Screenshots/232/sheets/zoom_forest_eye_sunvis.png`: forest at 3x around the biggest difference. The white block in |d| is a far patch streamed differently between arms (sky-blue = not drawn), not a shadow change; canopy edges match.
