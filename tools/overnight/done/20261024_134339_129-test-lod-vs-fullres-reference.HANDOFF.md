STOP_FOR_OWNER -- Q83 (numbered Q83 because Q82 is task 128's E1-E3, already answered): tier-0 terrain (the 0.1 m window, ~0-124 m round the player) never gets sun shadows from terrain outside that window: its shadow ray counts as LIT at the window edge and at the 7-layer band's ceiling (`Raymarch.compute` TraceShadowRay, ~line 1940; only shell hits march the shells, ~line 4555). So a cliff more than ~45 m above the player, or more than ~100 m away, shades the far ground (shells) but not the near ground. Found by this test (the 0.1 m reference lost the coast cliffs' shadow on the sea), but not yet shown in a play frame. (a) recommended: queue a QA pose at a cliff foot, sun behind the cliff, to show it; then a fix task (continue tier-0 shadow rays into the shell march). (b) leave it.

# 129-test-lod-vs-fullres-reference: DONE. 0.1 m reference vs the game at 50 m-6.6 km, 3 scenes x 8 D x 5 arms; a repeatable flag.

## What changed (capture-only; no default, frozen API or layout change). The first run hit the usage limit before committing; the 15:45 re-run checked the work and committed it unchanged. Scene .unity diffs are scene-builder churn, not committed.
- `Assets/Game/PlaygroundCapture.LodRef.cs` (new, `-lookreview -lodref 1`); hook in `PlaygroundCapture.LookReview.cs`.
- `Phase4Bootstrapper.RefTier0Centre` (capture seam: only the 0.1 m window follows it). Debug view 14 `HitInfo` in `Raymarch.compute`
  (CSMainFarShade, behind `_DebugMode == 14`, like 12/13) + enum entry. `tools/lodref129.py`, `Screenshots/129/129-driver.sh`, TASKS.md.
- REF = the normal tier-0 window moved onto P: 204.8 x 204.8 m x 89.6 m (7 layers) at 0.1 m, no extra memory (same pools). Fill 14-92 s per pose.
## Evidence
- Fast suite PASS 921 FAIL 0 SKIP 15 (`Logs/overnight/129-fast.out`). Capture log `Screenshots/129/129-full.out`; per scene
  `Screenshots/129/{ridge,coast,plain}/` (lookreview.txt, poses json, summary.txt/csv); table `Screenshots/129/lodref129_table.txt`.
- Self-check: at 50 m the game draws the patch with tier 0 itself -> REF vs GAME-NATIVE |d luma| 0.8 / 0.3 / 0.3 (ridge/coast/plain).
- Looked at: ridge_200/400, coast_400, plain_400 sheets, coast contact sheet (see captions below). All shells full at every pose except the
  first (waited 142 s).
## Verdict (High preset, 1080 output)
- **0 to ~124 m: the 0.1 m detail IS there.** The game draws with tier 0 out to the window edge (~100-124 m, not rule A's 69 m; shell 1
  only covers ~123-137 m). REF and GAME-NATIVE match (|d luma| 0.3-3.6). The only loss is the upscaler: FSR 3 at 540 rows keeps 44-56 % of a
  perfect frame's edge energy (REF through FSR3 540: |d luma| 2.3-3.1). At 720 rows it keeps 97-134 % of the anti-aliased reference.
  Native TAA comes closest everywhere (|d luma| 0.5-2.9).
- **~124 m to 3.3 km (shells 2-6): the 0.1 m detail is lost, but no screen could show it.** A 0.1 m voxel there is 0.03-0.6 px at 1080. Each
  shell's voxel is 1.6-2.4 px, as rule A intends, and silhouettes hold within 0.9 px (ridge/plain). What we lose is the COLOUR MIX of
  sub-pixel materials (the LOD stage). The plain comes out greener (mean RGB at 400 m, ref (102,123,102) vs game (77,128,86)). Single stones
  become whole 0.8 m blocks, reddish dirt specks show on ridge grass, and thin 0.1 m lines vanish. Error vs the anti-aliased REF:
  5-12/255 luma. The upscaler adds almost nothing here: FSR3 540 is as close to the reference as native (ridge 200 m 5.2 vs 6.1).
- **3.3 km and beyond: haze.** Errors 0-7/255. At 6.6 km the coast patch is not drawn at all (sky), but the haze hides it (0.0).
- **FSR 3 / TAA:** keep FSR 3 dynamic on. Past ~124 m it anti-aliases the coarse shells as well as native TAA. Near, more rows matter more
  (720 >> 540). Native TAA is the best image but costs native rows, so it is not an option at 30 fps. No separate TAA on top of FSR 3.
- **LOD distances: not too near. Change nothing.** Moving the shells out would not show more detail, because past ~103 m 0.1 m is under
  1 px at 1080. The remaining error is how a coarse voxel picks its colour (one material). A coverage-weighted colour is the lever (design
  question, not asked here). Coast caveat: the coast REF lost a shadow (Q83), so coast errors at 200-1600 m are overstated; its silhouette
  numbers (water line) are unreliable.
## Next
- Owner: Q83. Optional design question: coverage-weighted colour for shell voxels. Re-run after LOD changes: `Screenshots/129/129-driver.sh` then
  `tools/lodref129.py <LookReview dir>`.

## For the doc
| D (ridge / plain) | drawn by | voxel px @1080 | GAME-NATIVE vs REF-TAA (luma, detail) | FSR3 540 vs REF-TAA | REF through FSR3 540 vs REF |
|---|---|---|---|---|---|
| 50 m | tier 0 | 1.3 / 2.8 | 2.8 / 1.8 | 2.2 / 1.8 (75 / 84 %) | 3.1 / 2.7 (44 / 56 % kept) |
| 200 m | shell 2 0.4 m | 1.6 / 2.4 | 6.1 / 11.8 | 5.2 / 9.9 (58 / 77 %) | 5.0 / 9.9 |
| 800 m | shell 4 1.6 m | 1.8 / 1.9 | 6.4 / 9.2 | 5.8 / 7.5 | 5.2 / 9.1 |
| 3.3 km | shell 6 / far | 1.8 / - | 3.8 / 6.1 | 3.2 / 5.8 | 3.0 / 6.5 |
Source: `Screenshots/129/lodref129_table.txt` (all 24 poses, every arm). LookReview dirs under
`~/Library/Application Support/DefaultCompany/Voxel Terraria 1 Byte BrickMap/LookReview/`: ridge `2026-10-23_123711_high`, coast `..._130721_high`, plain `..._133508_high`.
- `<ridge dir>/lodref129/ridge_200_sheet.png`: top row REF (0.1 m, dimmed = not compared) | GAME-NATIVE | GAME-PLAYED (FSR3 540). Same cliff
  and shape. The game is blockier (0.4 m) and FSR is softer; the bottom-row 4x crops show REF's finer grass streaks.
- `<plain dir>/lodref129/plain_400_sheet.png`: bottom row, REF has thin grey arcs and darker fine grass. NATIVE/PLAYED show brighter, uniform
  green with lone grey 0.8 m blocks (the material-mix loss).
- `<coast dir>/lodref129/coast_contact.png`: one row per D (REF | NATIVE | PLAYED). The dark shadow band on the sea in NATIVE at 100-400 m is
  missing in REF (Q83).
