STOP_FOR_OWNER -- task 173: world-gen look survey + plan. These are the owner's questions; each has its picture and a recommended option.
Full-size viewer (local, not yet published): `Screenshots/173/viewer.html`. Arrows = pose, Space = native / FSR 3 540 (/ shape light), 1-4 = zoom. Each pose's caption is its finding.
- **Q1. Order** (`WORLD_GEN_LOOK_PLAN.md`): (a) **Recommended:** C1 biome layout, then C2 ground cover, then C3 snow line (cheap, in every frame, no shape change); then C4 cliffs, C5 rivers, C6 landform shapes, C7 stamps; C8 underground last. (b) Rivers first (C5). (c) Another order.
- **Q2. C1 biomes run as straight diagonal belts** (picture: `survey173_map.png`, the NW-SE bands of green/olive/red; `feature_great-peak-0`, the straight ochre|red seam down the peak). Cause: `ShapeField.Biome6`'s climate is ONE octave at 15 km scale, nearly a linear ramp across 6 km. (a) **Recommended:** make the climate vary across the island (scale relative to the island, or a low second octave; still no domain warp), plus patch-wise transitions instead of the per-voxel dither. Accept on 8 seeds: no border straight > ~1.5 km. Biome borders move on every seed. (b) Keep the layout, fix only the border dither. (c) Leave it.
- **Q3. C3 snow on high ground** (picture: `range_200up`, the 600 m summit brown to the top). (a) **Recommended:** a cover-only snow line above a fraction of the world's own height range; the biome map is unchanged. (b) No: biomes stay the only source of snow.
- **Q4. C4 cliff geology** (picture: `cliff`, 167 m of one smooth grey material). (a) **Recommended:** the biome's thick depth layers on steep faces (the brief's Decided "soil over stone") plus face ledges and scree; badlands keeps striped rock (the brief's OPEN row). (b) The same without badlands stripes. (c) Leave it.
- **Q5. C8 underground** (picture: `cavern_native_lit`, a bland smooth grey chamber). (a) **Recommended:** stays parked until an underground design talk. (b) Plan it now.

# 173-plan-world-gen-look: survey of 36 poses shot (2 launches); `WORLD_GEN_LOOK_PLAN.md` written. Capture only; play unchanged.

## What changed (`4d426d2`)
- New capture mode `-lookreview -worldsurvey 1` (`Assets/Game/PlaygroundCapture.WorldSurvey.cs`, hooked in from `PlaygroundCapture.LookReview.cs`).
- `tools/survey173.py`: builds the annotated map, 2-across contact sheets and the full-size viewer. TASKS.md: Task 173 paragraph.
- `WORLD_GEN_LOOK_PLAN.md`: findings and 8 candidates in order, each with effort, how it stays general, and render cost.
- `Screenshots/173/`: `findings173.json`, `survey173.txt`, `lookreview173_a/b.txt`, `viewer.html`. The images are git-ignored and stay on disk.

## Evidence
- Launch 1: all 35 poses, `Logs/lookreview_20261105_134936.txt` (`LookReview/2026-11-05_135456_high`).
- Launch 2: reshoot of features, river, tunnel and cavern, `Logs/lookreview_20261105_143742.txt` (`2026-11-05_144242_high`). It fixed 3 defects in the capture code:
  - the map heights are voxels relative to sea level, which had skipped the river pose;
  - duplicate feature names had overwritten the first canyon and mesa;
  - caves are black at noon, so a capture-only shape-light arm was added.
- Each pose waited 30 s for the shells. Shells s3-s7 were still mostly missing (about 3,000-4,000 per pose in the log), so terrain beyond ~300 m is the far band.
- Noon sun is 78 deg up. The poses mostly look south.
- Fast suite: PASS 997 FAIL 0 SKIP 16 (`Logs/overnight/173_fast.out`).
- Land on seed 42 (record): 42 biome x landform pairs. Snow 31 %, desert 31 %, forest 17 %, savanna 8 %, badlands 8 %, jungle 4 %.

## Findings (I looked at every native shot; detail is in the plan)
- **Good:**
  - Mesas, striped buttes, crater lakes and gorges (`pair_forest_volcanic` is the best frame).
  - The sand islet with shallow-water tint.
  - Far views across the island.
- **Weak:**
  1. Biomes run as straight belts, with dithered seams.
  2. Speckle and camo ground cover, and flat lawns.
  3. Rivers look like canals: D8 kinks, levees, stepped water.
  4. Cliffs are one smooth grey material.
  5. No snow on peaks.
  6. Dune sea, canyonlands and breaks do not show their shape.
  7. Ledges look like egg-shaped holes; a cave mouth on a steep slope becomes an open trench; the geyser basin is a perfect ellipse; hoops stand alone.
  8. Tunnel and cavern are bland.
- **Passed to lighting / visuals:**
  - pure black shade in a trench at noon;
  - flat water and waterfalls that look like solid sheets;
  - a colour seam across the sea at a shell edge;
  - white dashes on a cliff face (`pair_forest_breaks`).

## Next
- The owner answers Q1-Q5. Then the first world-gen task is queued (C1 if the order is (a)).
- No follow-up was queued: every candidate needs an owner yes.

## For the doc
- Numbers (`Screenshots/173/lookreview173_a.txt`):

| What | Value |
|---|---|
| Biome x landform pairs on land | 42 |
| Poses shot | 18 pairs (91 % of the land) + 18 other poses |
| Largest pair | snow x river plains, 14.1 % |
| Snow + desert share of land | 62 % |
| Tallest sun-lit sheer face | 167 m over 48 m |

- `Screenshots/173/survey173_map.png`: island map. Look at the forest/olive/red biome bands running dead straight NW-SE. Numbered dots are the camera poses (see the viewer).
- `Screenshots/173/survey173_sheet_1.png`: first 6 poses. Look at the speckled slopes at the start, and the snow river-plains canal (row 2, left).
- `Screenshots/173/shots/ws_cliff_native.png`: the tallest sheer face. 167 m of one smooth grey material, with no layers or ledges.
