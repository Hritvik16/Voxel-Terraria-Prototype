# 189-worldgen-c6-landform-signatures: DONE. `-landsig 1` gives dunes, canyonlands and alpine ranges their own 20-200 m shape. The DEFAULT stays OFF (byte for byte today). The owner picks from the pictures.

## What changed (commit d515aa0)
- New `Assets/TerrainGen/LandSig.cs`, applied in `ShapeField.Sample`. Rules use only the landform weight, slope, height and rock layers, never the biome:
  - **Dunes:** the map's OWN dune train (`WorldMap.DuneField`, 160 m apart) sharpened IN PHASE: a straight stoss, a sharp brink and a straight slip face. The 16 m and 8 m lumps are damped inside dune fields.
  - **Canyonlands:** benches on the rock layers (flat tread plus riser), and slot canyons that run down the 200 m regional slope, in patches.
  - **Alpine:** ridgelines and side valleys running down the slope, scaled by steepness.
- Where: a weight plane (`WorldMap.EnsureLandSigPlane`, 3 bytes per map cell, built only when the flag is ON). It is the region weights, faded over 16-48 m away from sea, lakes, rivers, easy areas and routes.
- Plumbed through every MapView: near chunks, point queries, shells, shell fill, FarField and FarQuery.
- This session's fix to the earlier attempt: the column dunes used to be a SECOND 120 m train, which beat against the map's 160 m one. That cancelled the lee/stoss asymmetry: ON 1.26 vs OFF 1.28, a red test. A probe showed the map's dunes survive at 46-82 % of their height (correlation 0.4-0.6), so the column now sharpens those same crests.
- TASKS.md has the 189 paragraph.

## Evidence
- `LandSigTests`, 12 tests:
  - OFF == the 188a FNV pins with the plane built.
  - The dune phase matches `DuneField` (worst difference 0.005 voxels).
  - The slip face is about 3x the stoss and under 45 deg.
  - Slots and ridges run down the slope; the benches are continuous.
  - The plane is 0 on keep-out ground.
  - Adversarial cases pass: a one-cell patch and a biome border (relief on both sides, no step at the border; `Logs/TerrainGen/189/border189.txt`).
  - No new needle on 4 seeds (`needles189.txt`).
- Whole suite: **PASS 1053 FAIL 0 SKIP 27** (`Logs/overnight/189/all1.out`; 188a had 1041).
- Shape statistic: 4 seeds (42, 7, 2026, 31337), 40 transects of 400 m per landform, the same places OFF and ON (`Logs/TerrainGen/189/shape189.txt`; MEAN of the seeds below). Seeds 2026 and 31337 have no canyonlands at weight >= 0.6.

| landform | arm | 20-200 m band m | lee/stoss | flat share | steep share | slots/km | ridges/km |
|---|---|---|---|---|---|---|---|
| dunes | OFF | 8.50 | 1.28 | 26 % | 8 % | 1.1 | 4.1 |
| dunes | ON | 9.95 | **1.54** | 20 % | 9 % | 1.5 | 4.2 |
| canyonlands | OFF | 10.17 | 0.96 | 25 % | 14 % | 2.2 | 4.8 |
| canyonlands | ON | 9.83 | 1.04 | **55 %** | 15 % | **6.7** | 5.9 |
| alpine | OFF | 11.59 | 1.02 | 17 % | 16 % | 3.3 | 4.8 |
| alpine | ON | **12.93** | 0.95 | 14 % | **27 %** | 6.2 | **6.9** |

- Shots: seed 42, noon (sun at 78 deg, behind the camera), one build, 45 s wait, 4 launches (`Logs/overnight/189_shots.sh`, `189_shots2.sh`). Shell 1 was filled at every pose, shell 2 about 2/3, shells 3+ mostly missing.
  - The extra `dune_field_low` pose is BROKEN: OFF has the camera against a hillside and ON is black (camera inside the raised ground). Do not use it.

## What to compare (viewer: `Screenshots/189/viewer.html`; Space flips OFF/ON, arrows change pose, Z full size)
- `Screenshots/189/{off,on}/ws_fixed_pair_desert_canyonlands_native.png`: the biggest change. OFF is a lumpy sand sheet. ON has a flat bench tread in the middle, a stepped cliff band at the right and thin winding slot canyons across the mid-ground.
- `Screenshots/189/{off,on}/ws_fixed_range_200up_native.png`: subtle at 1.5 km. ON shows grooves running down the left face, a notched summit line and a more jagged red ridge at the right.
- `Screenshots/189/{off,on}/ws_fixed_dune_field_native.png`: an extra pose at seed 42 cell (467,589), inside a dune field (grass). ON has a smoother surface (the right-hand bumps are gone) and faint diagonal dune stripes on the right. **Weak at noon**: the camera looks downwind, so the slip faces point away, and the 7 deg stoss slopes take no shading under a 78 deg sun.
- `Screenshots/189/{off,on}/ws_fixed_pair_jungle_dune-sea_native.png`: almost NO change in the foreground, by design. That ground is a river plus an easy-area corridor (keep-out), so there are no signatures there. The far mountains show the alpine change.

## Left / next
- The owner decides the default from the viewer. If dunes should read at noon, the choices are: (a) a taller signature (`DUNE_GAIN`, now 0.5 of the map dune), or (b) judge dunes from a cross-wind or low-sun pose. That is a design call; no follow-up task was queued.
- Not done: no timing (not a performance task). The shell 3+ fill was missing in the shots, so the far ranges are drawn by the far band.

## For the doc
- Canyonlands flat (bench) share 25 % -> 55 %; slot canyons 2.2 -> 6.7 per km (mean of seeds 42 and 7; `Logs/TerrainGen/189/shape189.txt`).
- Dune lee/stoss slope ratio 1.28 -> 1.54 (4 seeds; the signature alone is 1.68).
- Alpine steep (> 45 deg) share 16 % -> 27 %; ridges across the slope 4.8 -> 6.9 per km (4 seeds).
- OFF is byte-identical; whole suite PASS 1053 FAIL 0.
- Image: `Screenshots/189/off/ws_fixed_pair_desert_canyonlands_native.png` vs `Screenshots/189/on/ws_fixed_pair_desert_canyonlands_native.png`. Look at the middle and right: the lumpy sand sheet becomes flat benches, a stepped cliff band and winding slot canyons.
- Image: `Screenshots/189/{off,on}/ws_fixed_range_200up_native.png`. Look at the left mountain face and the summit line: ON adds downslope grooves and notches (subtle at 1.5 km).
