STOP_FOR_OWNER -- 75's cost check landed in my pre-written "rethink" band: +3.5 ms p50 at a lake shore (0.0 ms with no liquid in view).
Rule written before measuring: <= 1 ms keep on everywhere; 1-2.5 ms shorter max distance on Low/Medium; > 2.5 ms rethink.
**Question:** (a) keep it on in play as committed (the cost only applies while liquid is on screen; F6 turns it off);
**(b, recommended)** queue 75b: split the 3.5 ms first (march through the fluid vs. the blend), then make ONE cheaper change,
e.g. stop the fluid march at ~4 m of path or 64 steps instead of 192, and re-measure. Target <= 2.5 ms; (c) default it OFF until 75b.
Until you answer, play has it ON (what the task asked for). Rigs and capture runs are unchanged.
# 75-see-through-liquids: DONE (look + setting + far band); the cost is over budget, see above. Commit `9a28a20` on engine-phase.
## What changed
- `Raymarch.compute`: when a ray from air hits water, acid, honey, sludge or oil, it records the surface and keeps marching
  through the fluid (66b's passMat, at any LOD tier) to what is behind it. The surface is then restored as the hit, so the shadow ray,
  sky probe, shading and TAA all work as before. At the end: `lerp(surface as today, behind, exp(-path*ext))`, then
  a capped Schlick sky reflection + sun glint on top faces. The bed uses the SURFACE's sun shadow plus depth darkening
  `exp(-0.5*depth*ext)`, chosen because it is cheaper: no second shadow ray, no refraction. Early-outs: (1-F)*T < 2%, 192 extra steps, leaving the
  fluid. Lava, quicksand and steam are unchanged. Far band: the same blend from the texel's water depth (`FarField.DepthBits`,
  spare flag bits 25-31; no march, fixed sand bed: the texel has no bed material). `FluidExtinction` is now ONE table (66b reads it too).
- `RaymarchFeature.SeeThroughLiquids` (default OFF), `PlaySettings.seeThroughLiquids` (on), F6 row "See-through liquids",
  `-seethrough 0|1` (capture runs too). `-liquiddemo` (Playground.LiquidDemo.cs: pools + lake, off/on). Cost split: arm `see_on`,
  `-costpaths lake`. CLAUDE.md command line. Test: PlaySettingsTests.SeeThroughLiquids_*.
## Picks (owner may change)
Clear-to-D = a bed D m straight down keeps 10% (view + half light path). ext r/g/b per m: water 0.80/0.31/0.37 (~5 m, blue-
green); acid 2.00/1.02/2.30 (~1.5 m); honey 1.53/2.50/6.00 (~1 m, amber); sludge 4.20/3.07/5.00 (~0.5 m); oil 7.7/8.1/8.9
(~0.2 m). Fresnel cap 0.6 (`SEE_FRESNEL_MAX`; far water now looks paler, more sky-coloured at grazing angles). 66b underwater water went from ~8 m to ~5 m.
## "Sometimes I see an opaque block"
It is not a separate renderer bug. These are generated water bodies that stand ABOVE the land beside them, so their side is
a vertical wall of water, several metres tall. The water is stored as 8 m-cell slabs and stays put until touched (task 54). The first cost pose put the eye
under one (stills in UpscalerClips/2026-10-11_135318/stills; `Screenshots/75/cost/cost_poses_sheet.png` = the second, real shore pose). With 75 the near/mid walls are
see-through by clarity. The far band still draws step sides opaque. The fix for the shape is the generator's job (73 R1 / 76).
## Evidence
- Fast suite PASS 694 FAIL 0 SKIP 14 (Logs/overnight/75-fast2.out). `run-phase7-rig.sh` PASS 37 FAIL 0, same as the 09:23 run;
  **19/19 shots pixel-identical** to that run (Logs/overnight/75-phase7.out).
- Cost, WARM steady state (warm-up Logs/tg2world_20261011_140608.txt; ABBA F X X F X X F; FSR 3 540 rows, 1920x1080, noon):
  Logs/costsplit_2026-10-11_141247.txt. lake_shore FULL p50 51.1 / p99 74.5 ms; see_on 54.6 / 54.8 (p99 81.6 / 81.0):
  **+3.1 / +3.9 ms**, noise 1.1 / 0.1. lake_away (no water) 35.9 vs 35.9 / 35.8: **+0.0 / -0.1 ms**.
- Worst case (first run, the eye facing a water WALL that filled ~60% of the frame; warm-up Logs/tg2world_20261011_134632.txt):
  Logs/costsplit_2026-10-11_135318.txt. 25.5 vs 29.8 / 31.0 ms: **+4.5 / +5.1 ms**.
- Shots (Screenshots/75, noon; the sun is nearly overhead at noon here, so "behind the camera" means only its azimuth): all 6 pools at 3 m + 15 m, lake shore, near 27 m,
  mid 153 m, far 809 m, each _off/_on. Looked at all of them (captions below).
## Next
- Owner answers the cost question. 75b would be a performance task (one change, re-measure).
## For the doc
- Clarity (bed visible to): water 5 m, acid 1.5 m, honey 1 m, sludge 0.5 m, oil 0.2 m, lava opaque (unchanged).
- Cost at a lake shore (warm, p50): 51.1 -> 54.6 / 54.8 ms (+3.1 / +3.9 ms); no water in view: 35.9 -> 35.9 / 35.8 ms (0.0).
  Worst case, facing a wall of water: +4.5 / +5.1 ms. Log Logs/costsplit_2026-10-11_141247.txt.
- Phase 7 rig with the setting off: PASS 37 -> 37, 19/19 shots identical.
- `Screenshots/75/sheet_pools_15m_acid_honey.png`: left OFF, right ON. Row 1: all six pools from 15 m (lava, oil, sludge,
  honey, acid, water, left to right). Rows 2-3: acid and honey from 3 m. Watch the white stripes on each stepped bed (0.2 m deep at the far
  end, 2.2 m near). Water shows every stripe, acid the shallower half, honey the first 2-3 steps.
- `Screenshots/75/sheet_pools_water_sludge_oil_lava.png`: water, sludge, oil, lava from 3 m. Sludge is faint at its shallow
  end. Oil shows no bed, only a navy sky sheen. Lava is identical off and on.
- `Screenshots/75/sheet_lake_shore_near_mid_far.png`: one lake, rows shore / 27 m / 153 m / 809 m. Row 1 ON: the sandy bed fades
  into blue toward deep water. Row 3: the dark arc is an old LOD tier seam (also in OFF), and the bed tint is the same on both sides.
  Row 4 (far band): lake edges and the river at bottom right show the bed the same way.
