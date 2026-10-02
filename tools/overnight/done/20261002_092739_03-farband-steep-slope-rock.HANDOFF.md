STOP_FOR_OWNER -- task 03: the far band ALREADY uses the voxels' rock rule. On a 30° slope
the voxels are green too. Which do you want?
  (a) Keep it as it is. Far and voxels match: rock on ~56°+ ground. Close the task.
  (b) Rock from ~45° (texel slope 1.0) on BOTH voxels and far band. This is a TerrainGen
      change (DetailField's 1.5 -> ~1.0), so it changes how the near ground looks too.
  (c) Rock from ~30° on both. Then about 60% of tg2 land turns rock.
  (d) Far band only, mean-colour match: hash-dither rock texels at the voxel rate per slope
      (20% at 45°, 50% at ~53°). The colour average matches the voxels, but there are
      0.8 m speckles, and it makes 30° slopes no rockier (1-3%).
  Any change to the far band alone, below ~1.35, adds a colour seam at the voxel edge.

# Overnight handoff

**03-farband-steep-slope-rock: NOT DONE. I measured it and changed no product code. The
task's premise does not hold (see above).**

## What changed
- `Assets/TerrainGen/Tests/FarSlopeRockReport.cs` (new, opt-in report, ~80 s):
  `FARSLOPE=1 tools/run-editmode-filter.sh FarSlopeRockReport`. On seed 42 it samples 3000 land
  texels per far level (F0/F1/F2). For each texel it compares the bake's texel-spacing `MaxStep`
  with the share of the texel's VOXEL columns that are steep under the voxel rule (8x8 grid,
  1-voxel `MaxStep` > 1.5, detail octaves included).
- `KNOWN_ISSUES.md`, band entry: a "SLOPE RULE (task 03)" paragraph.
- Commit `eac21bd` (branch design-brief).

## Evidence
- Voxel rule (`DetailField.Material` / `TopMaterial`): steep = `stepAtTop > 1.5` (1-voxel
  spacing), then strata rock. The far bake (`FarField.cs:592-595`) already calls
  `TopMaterial(step > 1.5)` with `MaxStep` at texel spacing. Its top AND side faces already
  turn rock when the texel is steep.
- Report (`Logs/overnight/03-report.log`): share of steep voxels by texel slope s (F1;
  F0 and F2 are within a few points):
  s 0.5-0.7 (~30°): 3-5% | 1.0: 27% | 1.3-1.4: 53% | 1.5-1.6: 62% | 1.9-2.0: 84% | 2.4+: ~95%.
  The 50% point is at s ~1.35 on every level. The bake's 1.5 agrees with the voxel majority on
  93.4% (F0) and 92.9% (F1) of texels. The best threshold (1.40 / 1.30) gains <1 point.
- 39% of land texels have s < 0.6. So "rock at 30°" would turn ~60% of land rock.
- EditMode: PASS 626 FAIL 0 SKIP 3 (`Logs/overnight/03-editmode.out`; the new skip is this report).
- Not run: the tg2world poses and the walk. No product code changed, so a rig run would only
  reproduce `FarBand/2026-10-02_001850_world` and `Logs/tg2world_20261002_003414.txt`.

## Unfinished
- Every acceptance step past EditMode. They wait on the owner's choice above.
- If (b) or (c): this is generator work. CLAUDE.md says no new terrain-generator work until the
  owner picks a direction, so the answer to this question is that pick for slope rock. The
  change is DetailField's `1.5` (Material and TopMaterial; Surface's `<= 1f` stability rule is
  separate). The far bake follows it automatically, because it calls the same function. Keep
  the far threshold at the measured 50% point for the new rule: rerun the report to find it.

## Next step
- Owner: answer a/b/c/d. Then queue it as a task, or close 03.
- The queue continues with tasks that do not `Require:` 03.
