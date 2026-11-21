STOP_FOR_OWNER: "Keep the new material rules as the default?" My recommendation: **not yet.** Banks, oasis, beach and one-stone cliffs look right. But two defects need one fix task first:
(1) On tall faces the deep-stone band (snow: dark blue slate) shows as brick-stepped black/blue boxes.
(2) In the shells, far hillsides show a brick-sized mosaic of soil, subsoil and deep-stone colours.
Say "fix then default", "default now" or "drop it".

# 180b-worldgen-material-rules-prototype: DONE behind `-matrules 1` (default OFF). Commit 34df5fc.
(A previous run of this task hit the usage limit after writing the code and tests. This session ran the shots, the whole suite and one new test, then committed.)

## What changed
- `Assets/TerrainGen/GroundRules.cs` (new). The header holds the rule list, first match wins:
  shore -> water bed -> steep (bare on a crest or the sunny side) -> wet bank -> high (snow line) -> scree -> dip -> crest -> flat.
  - One palette row per biome: skin, soil, subsoil, stone, deep stone, cavern + slot entries. The biome paints everything.
  - Per-fluid shore table (lava -> magma crust, oil -> shale, ...). Badlands: one red stone, no stripes.
  - Ore hook noted for 181.
- Wired into `ShapeField.Sample`, `DetailField.Material` / `UniformSamples`, chunks, shells, `PointVoxel` and the far band (`FarField`). The shore field is baked once per map (`WorldMap.EnsureShoreField`).
- `-matrules 1` -> `TerrainSpec.matRulesOn`. OFF is bit-identical. TASKS.md has the paragraph.
- `tools/matrules180b.py` builds the ON|OFF viewer.

## Evidence
- **Whole suite PASS 1087 FAIL 0 SKIP 35** (`Logs/overnight/180b_all.out`). The new `Shells_And_SnowCliff_AirAndFluidUnchanged` was added after that run. It passed on its own (`Logs/overnight/180b_air.out`).
- **ON changes paint only.** 0 air/fluid voxels differ ON vs OFF over 81 shell chunks (shells 1-3, 9 sites) and 27 LOD-0 chunks round the snow cliff (`Logs/TerrainGen/180b/air_on_off.txt`). Also on the 18 site chunks (`chunks.txt`).
- **Dig test.** A dug pit never changes what regenerates. The pit shows the original surface's bands (`Logs/TerrainGen/180b/pits.txt`).
- **Per-biome table.** `Screenshots/180b/table180b.txt`, 4 seeds, share by slot and by depth.
  - Forest top: grass 44 %, bare granite 24 %. At 2 m: ochre clay 64 %. At 6 m: granite 93 %. At 22 m: slate 92 %.
  - Desert at 6 m: orange sandstone 95 %. Snow at 6 m: silver schist 97 %.
- **Adversarial cases** (same file): 778 banks in border zones (404 on side A, 374 on side B), 268 desert oasis banks, 4,355 cliffs over 60 deg, 480 canyon floors, 4,499 lake-outlet bank columns.
- **Rough cost** (`Screenshots/180b/cost180b.txt`, Editor, the test's own calls):
  - LOD-0 dense bricks +23 % (+7.7 MB of bodies over 243 chunks). Snow +42 %, scree +54 %, desert +7 %.
  - Gen time per chunk +1 %. Shell chunks 1-3 generate 5-20 % faster, with dense bricks -0 to -6 %.
- Player launches (`-lookreview -worldsurvey`, High, 45 s waits): ON `.../LookReview/2026-11-07_005119_high`, OFF `.../2026-11-07_011653_high`.
  - **The two arms are not a fair comparison past ~200 m.** ON had shells 1-2 mostly full. OFF had s2 109-1049 of 2,170 and s3 0. Near terrain is fine to compare.

## Defects seen (all shots looked at)
1. **Deep stone on faces.** Depth is measured from each column's top. So the lower part of a tall face is "deep" and gets deep stone. Below 20 m that goes per 0.8 m brick, which makes blocky steps. Snow's blue slate is dark, and in shade it reads as black boxes (scree, cliff_snow).
   - Candidate fixes: deep stone closer in tone to stone, or the deep band only by absolute height.
2. **Shell mosaic.** Coarse shell voxels sample below the 0.3 m skin. On slopes they expose soil, subsoil and deep stone, giving a red / yellow / blue brick mosaic on far hills (beach shot, left).
   - Candidate fix: a shell voxel with an exposed top uses band 0.
- Not tested: whether `StampTests.NoNeedles` changes ON. The rules never change shape (proven above), so it can't.

## Next
- Owner decision above. If "fix then default": one task for defects 1 and 2, re-shoot the same 8 poses (`Logs/overnight/180b_poses.txt`).
- 181 is queued and does not need this.

## For the doc
- Generator: **0 air/fluid voxels differ ON vs OFF** (108 chunks). ON only repaints solid voxels (`Logs/TerrainGen/180b/air_on_off.txt`).
- LOD-0 dense bricks **+23 %** (+7.7 MB / 243 chunks). Gen **+1 %** per chunk; shells **5-20 % faster** (`Screenshots/180b/cost180b.txt`).
- Forest columns at 6 m depth: **granite 93 %** (one stone per biome, no layer stripes). Desert sandstone 95 %, snow schist 97 % (`Screenshots/180b/table180b.txt`).
- Whole suite **PASS 1087 FAIL 0** (`Logs/overnight/180b_all.out`).
- `Screenshots/180b/viewer.html`: Space flips ON/OFF, arrows change pose, full size. 8 player poses + 9 cut-away pits, each with a caption.
- Key images:
  - `Screenshots/180b/cliff_desert_compare.png`: ON left, OFF right. The face is all orange sandstone ON; OFF's red lower half and tan stripes are gone.
  - `Screenshots/180b/oasis_or_swamp_compare.png`: ON left grows a green grass + mud collar along the desert river; OFF right is sand to the water.
  - `Screenshots/180b/scree_compare.png`: ON left shows the gravel apron at the cliff foot, centre. Also look at the dark-blue / black brick-stepped boxes on the face above (defect 1).
  - `Screenshots/180b/pit_cliff_forest.png`: generator cut-away, ON left / OFF right. ON is granite over slate in one contact line; OFF is striped layer rock.
