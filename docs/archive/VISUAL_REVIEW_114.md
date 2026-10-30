# Visual look review (task 114, 2026-10-22) -- what "doesn't look good" is

**Diagnosis only.** No default, look or performance code was changed. The only addition is a capture-only flag,
`-upscalerclips -lookreview` (`run-look-review.sh`), plus `tools/look-review-diff.py`. **Owner decisions: section 4.**

## 1. How the shots were taken
- 3 launches (one per `-quality low | medium | high`, because the shells are sized at start-up only). All three ran on
  2026-10-22 16:15-16:31, each about 4-5 min. Settings: tg2 map v4, fullscreen 1920x1080, play's look flags (far band and
  far shading, see-through liquids, river look, shells with 3 workers, far shadows), noon (sun 78 deg, behind the camera),
  clock paused, HUD hidden.
- Poses: **walk_near** and **walk_open** (task 110's walk arcs, eye at 1.6 m); **ridge**, a level view with ground rising
  296 m ahead (in practice the frame is filled by a mound about 5 m away, so read it as a second near view, with mid and
  far slopes on the right); **high**, the highest column within 800 m plus 20 m, showing a 3 km view.
- Each pose was shot with four arms: `fsr3_<rows>` (play's upscaler: Low and Medium at 540 rows, High at 720, the limits
  UpscalerSelect clamps to), `none_<rows>` (the same rows with a plain blit), `native` (1920x1080 with no AA, the ground
  truth), and `fsr3 + ShellTint`.
- Motion: at walk_open and ridge, for fsr3 and none, 4 consecutive frames each of still, walking (1.5 m/s) and turning
  (6 deg/s), after 60 frames of lead-in. A golden-hour shot (17 h, sun at 15 deg) on High at walk_open.
- Shells were given 45 s per pose and NOT waited for. On High, shells 4-7 were mostly still missing (High walk_open:
  2,946 chunks missing), so beyond about 550 m those frames show the **far band**, which is also what play shows while
  walking (KNOWN_ISSUES "SHELLS ... NEVER FILL WHILE WALKING").
- ShellTint key: blue = tier 0 (0.1 m), green = shell 1, yellow = s2, orange = s3, red = s4, magenta = s5, cyan = s6,
  grey = far band.
- Files: `Screenshots/114/<ts>_<preset>/` (all frames + `lookreview.txt`), `Screenshots/114/crops/`,
  `Screenshots/114/*_poses.png`, burst numbers in `Screenshots/114/bursts.txt`, log `Logs/lookreview_20261022_161003.txt`
  (images are git-ignored).

## 2. Catalogue, ranked by how much it hurts the look

| # | Defect | Crop (Screenshots/114/...) | Where | Likely cause (file:line) | Proposed fix | Risk | Owner? |
|---|---|---|---|---|---|---|---|
| 1 | **Near terrain is olive-khaki staircases.** Sides of sand and stone come out dull olive or khaki, much darker than the tops. Every slope reads as a striped staircase with no texture inside the blocks, which looks like plastic. | `crops/C_high_walkopen_still_vs_turn.png` (top row), `161606_low/ridge_fsr3_540.png` | All presets, all upscalers incl. native, 0-60 m | Look-1 shading, `Raymarch.compute:4466-4472`: at noon the sun is 78 deg up, so a side face gets `sunLight` 0.2-0.36 (top 0.98). That leaves its light mostly to the BLUE sky term (`FarShadeSky`, :3168), and yellow albedo x blue light = olive. There is no ground-bounce (warm) term. | Add a warm bounce term on side faces (albedo x sun colour x ~0.25 x (1 - n.y)), and/or less face contrast at high sun. | Low-medium: changes every frame's look, slightly flatter shadows; cost ~0. | **Yes** (look) |
| 2 | **Lighting changes with distance (a brightness band).** Near slopes are dark staircases. Past ~35-70 m the same ground turns pale and smooth because it is lit by the slope instead of the cube face. The switch distance depends on the render rows, so with FSR 3 dynamic rows (540-720) it MOVES during play. Low (540 rows) puts it nearer. | `crops/G_low_vs_high_far.png` (back hill pale on Low left, dark on High right; `G2_..._tint.png`: both are tier 0) | All presets; nearer at 540 rows; fsr3 and none alike | `FarShadeBlend`, `Raymarch.compute:3120-3124` (a 0.1 m voxel going from 2 px to 1 px at the DISPATCH rows), used at :4462 (task 85b) | Base the blend on a fixed distance, or on the preset's sizing rows, not the live dispatch rows. Better still, fix #1 so near and far are lit alike and the blend has nothing to switch. | Low (a constant); with #1 the band disappears. | Yes (with #1) |
| 3 | **Far-band contour dashes (moire source).** Gentle land and snow beyond the shells is covered in 1-px grey-rock dashes along every height step. When moving they crawl, which is the moire; FSR 3 only averages them while the camera is still. | `crops/B_low_high_dashes_native6x.png`, `crops/A_low_flank_trio.png` (fsr3 / none / native), `crops/E_ridge_grass.png` (snow stripes) | Everything drawn by the far band: Low beyond ~1.5 km, High beyond ~0.5-1 km whenever shells 4-7 are unfilled (i.e. while walking) | `FarWallMaterial` + `FAR_WALL_LIP 15.0`, `Raymarch.compute:2469` and :4742. Any far step side deeper than 1.5 m becomes the rock material. At coarse far levels a texel step is 3.2 m or more, so every riser on near-flat land turns into a rock stripe. | Choose rock by SLOPE (the 85b `FarSlopeNormal`, e.g. rock above ~40 deg) or scale the lip with the level (`8 << level`). | Low; cliffs keep their rock. | No (bug-like) -- recommend |
|   | **Task 119 correction:** the cause above is WRONG. The dashes are the far band's staircase SHADOWING ITSELF (the far sun-shadow ray; a `-farshadow 0` arm removed them all, the slope-rock rule changed 0 px of the crop). Fixed by `FarShadowLift` (shadow ray starts one texel up; `-farshadowlift 0` = old); the slope rock rule also shipped (`-farrockslope 0` = old). See TASKS.md Task 119. | | | | | | |
| 4 | **Flat sky.** One blue from zenith to horizon, no horizon glow, no sun disc, no clouds, a hard sea-horizon line. At golden hour the terrain is orange under a noon-blue sky. The sky is 45-55% of most walk frames. | `162640_high/walk_open_golden_away_fsr3_720.png`, `161606_low/high_fsr3_540.png` | All presets, all upscalers | `LookAtmosphere`, `Raymarch.compute:931-936`: the horizon-to-zenith blend is `dir.y*0.5+0.5` over the WHOLE sphere, so at the horizon the colour only gets halfway to the horizon colour. The only warmth is a pow-8 halo near the sun (`SunCycle.LookSunHaloAt`, warm only below 25 deg). | Concentrate the gradient near the horizon (e.g. `pow(1-dir.y, 4)`), add a sun disc, and a time-of-day horizon tint over the whole rim. Clouds are a separate feature. | Low (sky pixels only, ~0 ms). | **Yes** (look) |
| 5 | **Biomes are flat paint patches with hard edges.** At distance, snow, grass, pink and sand meet along sharp, nearly straight lines and each patch is one uniform colour. | `161606_low/high_fsr3_540.png`, `crops/A_low_flank_trio.png` | All presets; mainly the far band and outer shells | World gen and palette: one surface material per biome cell (TerrainGen biome map; the far band stores ONE material per column). Not a renderer bug. | Border blending in TerrainGen (dithered transition band, transition materials) plus per-biome tint variation. | Medium (TerrainGen work, `SUITE=all`). | **Yes** (world gen) |
| 6 | **Weak aerial perspective.** Land 2-3 km away is as saturated and bright as land at 200 m, so depth reads poorly and patches look pasted on. | `161606_low/high_fsr3_540.png`, `162640_high/high_fsr3_720.png` | All presets | `FarShadeApplyHaze`, haze range `FarShadeHazeRangeM = 3500` (`RaymarchFeature.cs:267`), thinned by height (`_FarShadeParams.z`) | A shorter range (~1500-2000 m) and/or desaturation with distance. | Low (one constant; tune by eye). | Yes (look) |
| 7 | **Tier 0 drawn below 1 px (8.14 rule A broken at the tier-0 edge).** 0.1 m voxels are drawn out to the old cascade bound instead of shell 0's edge. High `high` pose: 78% of tier-0 hits are below 1 px (max 1.18 px), and shell 1's largest voxel is only 1.10 px (rule A says it arrives at 2 px). | `crops/A_high_flank_trio.png`; numbers in `162640_high/lookreview.txt` `[px]` lines | All presets; 46-69 m (shell 0's edge) out to the tier-0 window edge (~90-130 m) | `RaymarchFeature.cs:1036-1040` sets the tier-0 / cascade bound from `LODConfig.FrameBoundariesM` (the old rule, "coarser reaches 1 px"), not `ShellLayout` shell 0's `OuterM`. `Raymarch.compute:3877` only uses `_ShellBounds[0].y` for the leap. | With shells on, cap the tier-0 primary march at `_ShellBounds[0].y` (shell 1 takes over at 2 px). | **Medium**: changes which data draws at 46-130 m and the march cost (needs a perf milestone check). Draw-only; no simulation reach change. | Yes (engine) |
| 8 | **Possibly not this game's Low.** `MODE=walk` rig runs (the builds the owner sees running) use `-renderpreset performance`, i.e. **480x270 with FSR 1** (`run-tg2world.sh:100`, also orbit/stream/flysweep at :74/:89/:94; `RenderPreset.cs:12`): a quarter of `-quality low`'s pixels. Separately, `-quality low` is sized for 480 rows, but FSR 3 clamps to 540-720 (`UpscalerSelect.cs:31`; 8.14 Q10's per-preset row range is not implemented), so Low and Medium shots are nearly identical (burst numbers within 0.1-0.2). | `Screenshots/114/bursts.txt`; `161606_low` vs `162037_medium` | Rigs and walk runs, not play | See left. | Ask which "Low" (Q2). If it was the rigs, nothing in play changes. | -- | **Yes** (question) |
| 9 | Per-voxel colour noise is hashed on the 0.1 m voxel even inside coarse shells, so it changes below one cell. Suspected sparkle in motion; **not isolated** (motion diffs are dominated by real parallax). | -- | Shells 1+ | `LookAlbedo`, `Raymarch.compute:974-979`, fed `shr.voxel` (0.1 m units, :3011) | Hash `voxel >> shellAtHit` (one value per drawn cell). | Low | No -- recommend if #3 is done |
| 10 | Isolated near-black 1-px specks on the sea. | `crops/B_low_high_dashes_native6x.png` (dots on the sea) | Mostly the native arm (50-62 px); once on Medium fsr3 (22 px) | Not found | Debug view of the far start on those pixels. | -- | No |

**Checked and NOT a problem (negatives):**
- **Shell-to-shell seams:** none visible in colour or brightness at tier0 -> s1 -> s2 (`crops/D_high_seam_pair.png`; the tint panel
  shows where the bands are).
- **Shell-to-far-band seam:** none visible on land or sea (`crops/F_high_shell_far.png`).
- **FSR 3 ghosting or smear:** none seen in the walk or turn bursts. FSR 3 is clearly better than `none`, which shows
  stair-step aliasing on every block edge (`crops/C_...`, bottom row). The aliasing in the frames is RENDER aliasing (native
  shows it too) that FSR 3 mostly hides. FSR 3 is not the problem.
- Flicker with the camera still: fsr3 0.3-0.4 / 255 mean, none 0 (no jitter) (`bursts.txt`).

## 3. Rule A check (8.14 §1-2), voxel px at the render rows

| Preset | Sizing / render rows | Shell k: inner -> outer px | Tier 0 measured |
|---|---|---|---|
| Low | 480 / 540 | 2.25 -> 1.13 (s1-s5) | to 0.87-0.88 px (`high` pose max 0.88; all its tier-0 hits < 1 px) |
| Medium | 540 / 540 | 2.0 -> 1.0 | max 0.88 px (`high` pose) |
| High | 720 / 720 | 2.0 -> 1.0 (s7 -> 1.33) | 78% of hits < 1 px, max 1.18 (`high` pose); s1 max 1.10 |

The shells themselves keep rule A. Tier 0 does not (#7).

## 4. Owner questions (recommendation first)
- **Q1. Is this what you meant by "doesn't look good"?** Look at: `162640_high/walk_open_fsr3_720.png` (olive staircases + flat
  sky), `crops/G_low_vs_high_far.png` (brightness band), `crops/B_low_high_dashes_native6x.png` (far-band dashes),
  `162640_high/walk_open_golden_away_fsr3_720.png` (orange land, noon sky). (a) yes, these; (b) partly (say which);
  (c) something else.
- **Q2. Which "Low" looked bad?** (a) a build running a rig or walk run (480x270 FSR 1, #8); (b) `run-play.sh` with
  `-quality low`; (c) the F6 Shadow quality row at a low rung.
- **Q3. Fix order:** recommend **(a)** #3 far-band dashes (bug-like, low risk) -> #1 + #2 shading together -> #4 sky ->
  #6 haze, one look task each with before/after stills. (b) sky first; (c) another order.
- **Q4. #1 bounce light:** (a) warm bounce on side faces (recommend); (b) lower face contrast only; (c) keep the staircase look.
- **Q5. #7 tier-0 rule A:** (a) cap tier 0 at shell 0's edge, checked at the next perf milestone (recommend); (b) leave it.
- **Q6. #5 biome borders:** (a) queue a TerrainGen border-blend task after the renderer fixes (recommend); (b) keep hard borders.
