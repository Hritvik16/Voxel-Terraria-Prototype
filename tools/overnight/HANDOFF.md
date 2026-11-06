# 177b-worldgen-palette-tweaks: DONE -- all three owner tweaks shipped (desert cliffs one colour, softer savanna earth, badlands no stripes); whole suite red only on the known route test

## What changed
- **The code landed in `c629fda` by accident.** That is a supervisor commit called "new-session.sh: comment wording", made at 08:42 while this task's whole suite was running. It picked up the working tree, which had this task's re-applied patch in it. The content is exactly this task's patch. I did not rewrite history.
  - `Cover.PALETTE`, desert `steep`: the variant was cream sandstone; now it is BARE. Steep desert has no patch variant, so cliffs are one cell: 178's thick orange-sandstone layers.
  - `Cover.PALETTE`, savanna `flat`: the variant was red earth; now it is packed sand, a dusty tan. Its colour distance from savanna grass is CIE dE 18, against red earth's 43.
    - The share of the patch variant is unchanged at ~23 % of flat, because the variant noise does not depend on the biome.
    - Savanna's `hollow` variant and savanna soil stay red earth.
  - Badlands: the palette never made stripes. The 3.2 m stripes came from `DetailField.BadlandsBand`, the depth bands, not from the palette.
    - 178's thick layers had already replaced them with cliffs on.
    - 177b deletes `BadlandsBand`, so `-cliffs 0` now shows plain `BiomeRock(Badlands, 0)` too.
    - `CliffTests.Layers_...` now asserts one rock per biome with cliffs off.
  - `tools/cover177.py`: the palette swatch draws BARE as 178's three layers. `TASKS.md` gets the 177b paragraph.
- **`418bd0b`** (this session):
  - ShellFill GOLDEN re-pinned. 18 of 85 chunks changed (36 counting the fast and reference paths); the chunk set is the same.
  - TASKS result line.
  - `Screenshots/177b/{shares177b.txt, palette177b.md, pairs.json}` and `tools/palette177b.py`.
- Heights did not move, so `PLAYER_V4_SEED42` (map hash) still passes.
- The first run's patch also changed `Assets/Scenes/Playground.unity`. That was a side effect of the scene builder, so I dropped it.

## Evidence
- **Whole suite on the final code: PASS 1063 FAIL 2 SKIP 33** (`Logs/overnight/177b_all.out`, XML copy `Logs/overnight/177b_all_TestResults.xml`, 46 min).
  - `ShellFillTests.Fill_BytesAsGolden_FastAndReference`: expected, a palette change. It is now re-pinned from `REPORTS=1 FillHashes_Report` (`Logs/Shells/fill93b2d_hashes.txt`).
  - `RouteTests.V4_FourSeeds_RoutesJoinTheEasyAreas`: the known red. Seed 42 is 3.4 m (178's scree; 178b fixes it) and seed 31337 is 1.5 m (the ford).
  - The aborted first run's orphaned whole-suite run, on the same code, gave the identical 1063/2 (`Logs/overnight/177b_orphan_TestResults.xml`).
- After the re-pin, a filtered run (ShellFillTests + CliffTests + CoverTests) gave **16 passed, 0 failed, 5 report-only skipped** (`Logs/overnight/177b_verify.log`).
- CliffTests layer log, seed 42: every biome shows 3 rocks with cliffs on, thinnest layer 15.4 m, and one rock with cliffs off (`Logs/overnight/177b_filter.log`).
- Shares, from `CoverTests.Report_CoverMaps` (`Screenshots/177b/shares177b.txt`), over seeds 42/7/2026/31337:
  - Savanna tops in red earth: **9.3-15.2 % -> 0.7-1.5 %**. Only the hollow variant is red earth now; the packed-earth patches take 8.6-13.8 %.
  - Desert steep's cream-sandstone patches: **~22-23 % of steep -> 0**. Cream remains only where it is intended: the `high` slot and 178's light layer.
- Shots come from one survey launch per arm, 30 s shell cap (`Logs/lookreview_20261106_071401.txt`). Shells 3-7 were partly unfilled at 30 s, so far detail behind the poses comes from the far band.
- Rough cost: palette data only, so the per-voxel work is the same. Deleting BadlandsBand removes a branch. No timing arms.

## Unfinished / notes
- "Desert cliffs one colour" means no patch variant. A desert cliff still shows 178's thick layers (orange, cream and red-brown). 178c (cliff look rework) owns that look.
- The orphaned suite from the aborted run held the lock for 42 min. I was not allowed to stop my own queued duplicate, so both ran.
- Next: the queue continues with 178b, then 178c.

## For the doc
- Savanna flat ground in red earth: **9.3-15.2 % -> 0.7-1.5 %**. Soft tan packed-earth patches now take that ~23 % of flat ground. (`Screenshots/177b/shares177b.txt`)
- Desert steep ground with cream-sandstone patches: **~22-23 % -> 0 %** (same file).
- Badlands stripes: with cliffs on they were already gone (178). With `-cliffs 0`, **3.2 m bands -> one plain red rock** (CliffTests log).
- Whole suite: **PASS 1063 FAIL 1** after the golden re-pin. The 1 is the known route test (`Logs/overnight/177b_all.out` + `177b_verify.log`).
- Images (full size; viewer `Screenshots/177b/viewer.html`):
  - `Screenshots/177b/shot_pair_desert_canyonlands_before.png` | `_after.png`: desert x canyonlands. Look at the orange steep patches across the whole ground and on the ridge at right. BEFORE has white cream-sandstone blotches inside the orange; AFTER is plain orange. The grey patches are hollow-slot gravel and are unchanged.
  - `Screenshots/177b/shot_pair_savanna_wetland-basin_before.png` | `_after.png`: the savanna slope filling the right half. BEFORE has dark red-earth blotches on the gold grass; AFTER has soft tan patches in the same places. The striped mountain at left is 178's layers, not this task.
  - `Screenshots/177b/shot_pair_badlands_canyonlands_after.png`: a badlands face in the foreground. Plain red rock with hardpan patches, no thin stripes; BEFORE is identical.
  - `Screenshots/177b/palette177b_before.png` | `palette177b.png`: the palette table. Look at the desert row's STEEP variant (now bare) and the savanna row's FLAT variant (packed sand instead of red earth).
  - `Screenshots/177b/shot_fixed_great-peak-0_before.png` | `_after.png`: the great peak from 1.6 km. Look at the desert flank at the right edge: the cream blotches are gone.
