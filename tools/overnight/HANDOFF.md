STOP_FOR_OWNER -- 73-rivers-waterfalls-plan: the plan is ready. Please answer 5 questions (recommendation first; details in `RIVERS_WATERFALLS_PLAN.md`):
1. **What a river is made of:** (a) "held water": a smooth sloped river made of a new material, drawn as flowing water. The sim treats it as solid until you touch it; then it becomes real water and the river is an endless source. (b) Plain water in flat pools held by hidden stone lips (can drain when dug). (c) A render-only surface sheet, turned into voxels where disturbed. (d) Simulate a window of river around the player.
2. **When:** (a) R1+R2+R6 now, as the next terrain task: the smooth river shape + the river shading + an at-rest test for all generated fluids. R3/R4 (held water, waterfalls) after engine step 17. (b) All of it after step 18. (c) After far fluid (19).
3. **Where waterfalls come from:** (a) wherever the new river profile drops more than ~2 m in one 8 m map cell, plus the Waterfall signature feature later. (b) Only the signature feature; every other drop is smoothed into rapids.
4. **Flow direction for the animation:** (a) a 1 MB flow-direction map built from Hydrology. (b) 8 held-water material ids, one per direction.
5. **Lava rivers:** (a) the same held mechanism later. (b) Water only.

# 73-rivers-waterfalls-plan: DONE (investigation + plan). Rivers are 8 m-cell water slabs. Shading alone can't fix them; the plan changes the shape first.

## Cause (code)
- `Hydrology.cs:134` gives each river cell ONE level: its ground + 0.8-1.6 m.
- `ShapeField.SampleWater` (`:272-286`) takes the MAX of the 4 cells around a column, so each cell is a flat square
  on the 8 m grid. `DetailField.cs:273,371` fills every lower column with no bank, giving boxes with vertical sides.
- The drop between cells is slope x 8 m: 3.8 m at site A, 10.5 m at site C. Water is drawn as an opaque material,
  shaded like stone (`Raymarch.compute:942`). The far band copies the terraces (`FarField.cs:613-615`).
- No waterfall feature exists (`Features.cs:10`). Binary voxel fluid can only rest flat, so a sloped river can't
  be "at rest" without lips or a non-flowing material.

## What changed (commit 9f7029a, branch engine-phase)
- `Assets/Game/Playground.RiverDemo.cs` (new): `-riverdemo`. Finds 4 sites on the map, nearest the spawn first:
  slope reach, river into a lake, biggest drop, river mouth. It shoots each near/mid (A also at 800 m and 2 km),
  logs a wall census of the loaded world, then quits. With `-riverlook`, each pose is shot off and on.
- `Raymarch.compute` `RiverLookAlbedo` + `_RiverLook`; `RaymarchFeature.RiverLook` (static, default false);
  `Playground.cs`: flag hookup. Throwaway prototype, OFF by default, Look 1 path only.
- `RIVERS_WATERFALLS_PLAN.md` (133 lines); CLAUDE.md: one command line.

## Evidence
- Fast EditMode `PASS 691 FAIL 0 SKIP 14`, before and after (Logs/overnight/73-fast.out, 73-fast2.out).
- `run-phase7-rig.sh` with the flag OFF: `PASS 37 FAIL 0`, Gate 3 at 98.17% (Logs/overnight/73-phase7.out, run
  2026-10-11_092345). **All 19 shots are md5-identical to 66b's run 2026-10-11_084304.**
- Demo: `Logs/overnight/73-demo-player.log`, seed 42 map v4, noon, sun behind the camera for the mid/far shots
  (near shots look along the river instead). Wall census in 24 x 24 m boxes: A 0.79 side faces per top face, wall 7.6 m;
  B 0.16, 3.2 m; C 1.23, 10.5 m; D 0.10, 1.3 m.
- No frame-time measurement (owner rule).

## Left open
- Underground fluid pockets (`DetailField.cs:512`) and the volcano lava lake have not been checked for rest.
  The plan's R6 test covers them.
- The prototype curtains read as grey stone. The riser-hiding rule is untested on real 10 cm steps (none exist until R1).

## For the doc
- Cause: one water level per 8 m map cell, sampled as the max of 4 cells and filled over lower ground with no
  bank, so rivers are flat squares with 1-10 m vertical water walls. Water is shaded like stone and nothing falls.
- Numbers (`Logs/overnight/73-demo-player.log`): tallest water wall 7.6 m on a slope reach, 10.5 m at a drop,
  3.2 m at a lake inlet, 1.3 m at the sea mouth. Side faces per top face: 0.79 / 1.23 / 0.16 / 0.10 (a flat lake is ~0).
- Options:
  | Option | Look | Memory | GPU | Dig a bank | Touches |
  |---|---|---|---|---|---|
  | A2 held water, smooth profile (rec.) | smooth animated sheet; curtains | 0 (+1 MB flow map) | low | becomes water; river is an endless source | new material id + CA rule |
  | A1 plain step-pools with lips | flat pools, lip lines | 0 | low | pool drains into the hole | generator only |
  | B render-only sheet | best surface | 2-4 MB | medium | needs a mass "materialize" | second water representation |
  | C simulate a window | real flow near | ~1 M cells, over budget | high | real | fluid budget |
- `Screenshots/73/today_near_sheet.jpg`: 4 panels, near. Top-left: the 7 m water wall across a sloped river.
  Bottom-left: a 10 m water tower on the slope. Bottom-right: grid-straight steps at the river mouth.
- `Screenshots/73/today_mid_sheet.jpg`: the same sites from 100 m. Top/bottom-left: a staircase of floating blue
  boxes up each valley.
- `Screenshots/73/a_A_slope_reach_near_pair.jpg`: left today, right the shading prototype. The wall turns into
  grey streaks and is still a box, so the shape must change (R1).
