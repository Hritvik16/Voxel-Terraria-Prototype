# 198-bug-white-dashes-steep-faces: DONE (diagnosis). The dashes come from the RENDERER's lighting rule; the voxels under them are real. Not fixed: any fix changes the look on those walls. Options below.
## What the dashes are
- Every steep voxel face is a staircase. Each step has a 1-voxel TREAD (a real top face, air above it). At 100-200 m a tread is ~0.2 px tall,
  so one ray per pixel hits it only now and then. Where it does, the pixel takes full noon top-face sun: a 1-px white dash.
- Past 50 m, task 120's FILTERED CUBE LIGHT (`CubeLight`) lights a hit with the mean of its staircase's top and side faces, so the treads
  vanish. It does this EXCEPT where `CubeFilterWeight` is 0, i.e. where `FarSlopeNormal` returns false (Raymarch.compute, the
  `p.y < lo - tv || p.y > hi + tv` test, "a cave or an overhang"). That happens where the 3D wall (ledges, bench lips, cliff relief) lies
  more than one far texel (0.8 m at level 0) outside the 2.5D column-height bracket of the 4 texels around it. There the hit keeps its own
  face's light, and a sun-lit tread pops. The cause is the threshold test, not "floating point".
- NOT the generator: no snow or cover voxels on the faces (dash material = the face's rock, 186/182). NOT a bad hit: 0 inside-start
  (top-normal-by-rule) hits, and no top normal over a solid cell.
## How (commit ac3ec59; a CPU oracle, since the debug views are broken, see below)
- New capture probe `-dashprobe 1` (`Assets/Game/PlaygroundCapture.DashProbe.cs`, hooked in WorldSurvey; capture-only, draws nothing).
  It finds 1-px light lines in the beauty frame, then marches the same camera rays on the CPU through the generator's own shell cells
  (`GenerateShellChunk`: shell by distance, never coarser mid-cell; 4 sub-rays per pixel). It adds the sun-shadow ray (GPU 3-cell rule) and
  emulates `FarSlopeNormal` on the far texels' CPU copy (`-dashprobe 1` turns on `FarField.ShadowTexels`, +20 MB).
  `-dashwin` picks the windows.
- One pose set (forest_breaks, cliff, cliff_snow), native 1080, noon (sun at 78 deg, behind the forest_breaks camera).
## Evidence (run a6, `Logs/overnight/198/look_a6.out`; same numbers in a5)
- forest_breaks, window (600,630): 3,786 sun-lit tread px, all where the slope test holds -> 0 predicted, 0 dashes in the frame.
- Window (520,330): 1,186 lit treads -> 0 predicted, 0 matching dashes.
- Window (1000,380): 66 of 66 dash px over a sun-lit tread are predicted (slope test fails). The other 57 dash px are not treads: ore specks
  and snow edges caught by the detector.
- cliff_snow window: 122 of 125 dash px over sun-lit treads are predicted (80 % of all 153 dash px).
- Controls: random hit px are predicted dashes 0-1 % of the time. fixed_cliff (camera looks UP, no treads visible): 0 treads, 0 matched dashes.
- Only 9-18 % of predicted px show as dashes. That fits point sampling: one centre ray against a tread covering ~1/4 of the pixel, and a
  1.2x-over-both-neighbours detector.
- Fast suite **PASS 1011 FAIL 0 SKIP 20** (`Logs/overnight/198/fast.out`). No timing arms (rule 10).
- Launches: 6. a1/a2: the debug views and debug pixel came out as beauty / zeros (below). a2's later "beauty" frames were stale images.
  a3/a4: oracle without shadows; the forest window was mis-aimed. a5: sun shadows added. a6: slope test added. All in `Logs/overnight/198/`.
## Second finding: debug views 1-14 and the debug pixel are broken in player builds (KNOWN_ISSUES entry added)
- They switch to `CSMainFarShade`, which fails at run time. Player.log: "more uavs (9) than the maximum supported (8)", plus a Metal error
  `as_type cast from 'bool' to 'float'` (program_source:10570/10585).
- The frame keeps the last good image and `DebugBuffer` reads 0, so it silently LOOKS fine.
- 198's own test shader slots were reverted (`Raymarch.compute` unchanged). The error is the same with and without them.
- Needs its own task: which UAV to drop from the non-diet kernel, plus the bool select.
## Fix options (owner / supervisor; each changes pixels beyond the dashes)
1. **Filtered light for sky-open hits even when the bracket test fails.** Keep the face light only for sky-closed hits (true caves and
   undersides). Kills the dashes. Side faces in the fallback bands (red tint in the maps) change slightly.
2. **Widen the bracket from a flat 1 texel to a data-derived bound** (e.g. the 4 tops' spread, or the level's max mip). Fewer fallback
   bands; same kind of change; risks lighting real recesses as open slope.
3. **Rely on play's FSR 3 accumulation.** Play is not native: jittered samples average a tread to its coverage (a faint tint, not a dash).
   Check this first: ONE launch of the same poses with `-wsarms native,fsr3` (not run: launch budget).
4. Leave as is (the treads are real and correctly lit per face).
- Recommendation: 3's check first. If dashes still show in play, option 1.
## Not done
- No renderer fix (look change). No EditMode test (no fix).
- Trees are missing from the oracle's CPU cells (GPU draws them). This does not affect the terrain result.
- A pure-black patch below the cliff_snow ledge (CPU: shadowed terrain at shell 1-2) was seen but not investigated.

## For the doc
- Dash pixels over a sun-lit tread that the slope-test fallback predicts: forest **66 / 66**, cliff_snow **122 / 125** (`Logs/overnight/198/look_a6.out`).
- Sun-lit treads where the slope test holds that show as dashes: **0 of 3,786** and **0 of 1,186** (two forest_breaks wall windows).
- Inside-start (top-normal-by-rule) hits among 900 probed dash pixels: **0**. Generator cover/snow on dashes: **none** (dash mat = face rock).
- Images (`Screenshots/198/viewer.html`):
  - `Screenshots/198/198_forest_win_dashes.png`: LEFT the frame, RIGHT the CPU map. Look at the bottom: the white line in the frame is the
    map's white "predicted dash" line. The cyan treads scattered over the wall show nothing in the frame.
  - `Screenshots/198/198_forest_win_nodashes.png`: a plain wall in the frame (left). The map (right) has thousands of cyan treads there,
    averaged away by the filtered light.
  - `Screenshots/198/198_178c_before_zoom.png`: the bug as reported. Look at the 1-px white rows on the grey canyon wall (178c BEFORE, zoomed 4x).
