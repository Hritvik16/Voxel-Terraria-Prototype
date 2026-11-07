# 183-placement-fluid-pockets: DONE. Fluid pockets at rest by rule, behind `-fluidpockets 1` (default OFF). Commit 3a131b9.

## What changed
- New `Assets/TerrainGen/FluidPockets.cs` (its header lists the rules). The flag is `TerrainSpec.fluidPocketsOn` / `spec.Pockets`.
- **Surface pockets:** at most one per 51.2 m cell. Each is a small elliptical basin, r 1.4-5.2 m, fluid 0.4-2 m deep.
  - Fluid by rule: volcanic land -> lava (1 in 4 acid); badlands -> oil (the tar seep); desert -> quicksand; jungle valley or wetland -> sludge (bog); other jungle -> honey; a valley (>= 4 m below the regional mean) or wetland -> water.
  - Kept out: sea, within 32 m of lakes / rivers, non-Walk cells, routes, features, the start zone, stamps.
- **Sealed by construction** inside `ShapeField.Sample`, so LOD 0, shells, FarQuery and the far band agree.
  - The footprint is lowered to L - depth and the rim raised to at least L + 1 (berm max 1-3 voxels on 4 seeds).
  - A thin lining the fluid never reacts with: water mud, acid sinter, lava magma crust, oil shale.
  - Caves are capped under the pocket.
- **Deep sealed pockets** keep their shape and place, but get rule fluids (lava under volcanic land and in the deep cavern half, etc.) and a lining.
  - The OLD deep acid pocket was not at rest: it dissolved its own rock wall.
- **Steam:** not generated. It only belongs where lava meets water, and there the reaction already makes it.
- Plumbing:
  - The fluid code rides in `waterKind` (0 / 1 as before; `FluidPockets.FluidOf`).
  - The plane goes through `MapView.pocket` into ColumnJob, ShellColumnJob, ShellFill, FarQuery and FarField (`TerrainGenChunkSource.Pockets.cs`).
  - The minimap and the far band read the new kinds. TASKS.md has the paragraph.
- No frozen API, save format or memory layout touched.

## Evidence
- **Whole suite PASS 1098 FAIL 0 SKIP 35** (`Logs/overnight/183_all.out`). New: `FluidPocketTests` (3 tests).
- **Four seeds** (`Screenshots/183/table.txt`):
  - The plane is deterministic. 135 / 215 / 144 / 180 surface sites.
  - 0 keep-out / seal violations over 139k rim and 93k footprint columns.
- **At rest** (`Screenshots/183/sites.txt`, seed 42): 7 surface fluids + deep lava + deep acid + 2 FORCED sites (cliff edge, canyon side).
  - CPU fluid oracle, 150 ticks: 0 cells moved, 0 reactions in all of them.
  - Negative controls: a hole cut in the rim makes fluid move in every case. The OLD unlined acid pocket made 83,598 reactions in 150 ticks.
  - FarQuery = chunk, shell 0 = Generate, shells 1-2 and far texels 8-64 agree, brick skip on = off.
- **Rough cost** (Editor, the tests' own runs): plane ~0.35 s per world; chunk generation ON ~ OFF (e.g. 4 chunks 231 vs 243 ms).

## Pictures (all looked at). `Screenshots/183/viewer.html`: Space flips ON / OFF, arrows change pose, Z = full size
- `oil_compare.png` (ON left | OFF right): a dark tar pool in flat badlands clay, centre. It is absent in OFF.
- `water_compare.png`: a blue pond in a snowy valley, left of centre. OFF has unbroken snow there.
- `lava_compare.png`: a glowing lava pool on snowy volcanic land, with a grey dug wall on its far side.
- `cut_deep_lava.png` (vertical slice; OFF left | ON right): the same sealed pocket was water (OFF) and is lava (ON), with a thin dark magma-crust lining.
- `cut_surface_water.png`: a thin mud bed follows the bowl under the water.
- `cut_forced_cliff_water.png`: a site forced onto a cliff still seals, but fills a pillar under the drop. This is why the rules keep pockets on flat ground.

## Left / for the owner (no decision needed to keep it OFF)
- **The look is plain:** a perfect ellipse, with a vertical dug wall where the ground is up to 2.4 m above the level. An irregular outline or sloped banks would be a follow-up.
- **The rules are a first cut.** "Ashlands" = the volcanic landform, which includes snowy volcanic land, so lava pools can sit in snow. Surface sludge is rare (31 on 4 seeds).
- **Untested together with `-trees 1`, `-ores 1`, `-matrules 1`.** Trees skip wet columns, so a tree trunk will not stand in a pocket.
- **Possible float risk:** the plane is built on the main thread with managed float code, so the Editor and an IL2CPP player could disagree on a pocket level by 1 voxel. This is not observed; a Burst build of the plane would remove the risk.
- Shots: shells were not full after the 45 s wait (s3+ missing); near poses only.

## For the doc
- Surface fluid pockets per world (4 seeds): **0 -> 135-215**. Totals: oil 236, quicksand 119, honey 93, water 87, lava 81, sludge 31, acid 27 (`Screenshots/183/table.txt`).
- Keep-out / seal violations: **0** of ~232k columns checked. Berm raised: max **3 voxels**.
- Settle check, 150 CPU-oracle ticks: **0 cells moved** in all 11 cases (incl. forced cliff / canyon sites). The old deep acid pocket: **83,598 reactions -> 0** (`Screenshots/183/sites.txt`).
- Whole suite **PASS 1098 FAIL 0** (`Logs/overnight/183_all.out`).
- Images (`Screenshots/183/`, viewer `viewer.html`):
  - `oil_compare.png`: the badlands tar seep (ON left, centre of frame) vs bare clay (OFF right).
  - `water_compare.png`: the valley pond (ON left, left of centre) vs snow (OFF right).
  - `cut_deep_lava.png`: an underground pocket sliced open; water (OFF left) becomes lava with a crust lining (ON right).
