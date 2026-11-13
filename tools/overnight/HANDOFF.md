# 190a-matvariant-default-off: DONE. Materials by the rules only, with no random variant spray, are now the DEFAULT everywhere (play, rigs, capture runs, tests). `-matvariant 1` brings back the old spray.

## What changed (commit 6a549d8)
- `TerrainSpec.matVariantOff` became `matVariantOn`, following the 186 / 187a pattern. A default spec (byte 0) now has no spray. `MatVariant => matVariantOn != 0`.
- `TerrainGenChunkSource.MatVariantArg()` now defaults to false. `-matvariant 1` sets `matVariantOn = 1`. `FarField` sets the far band's spec the same way, so near voxels, the far band and shells still agree.
- Launch log lines now print the setting that is actually in force:
  - `StreamManager`: "... stamp fit ON (default), material variant spray OFF (default)"
  - `FarField`: the same wording.
- Scene-builder landmine: no SerializeField carries this setting. Only the arg and the spec do, and the log lines above show that the flip took effect.
- Tests (no assertion was loosened):
  - `MatVariantTests` now checks that the default has no spray. 188a's pins `PIN_42` / `PIN_7` still pass byte for byte, now on the explicit `-matvariant 1` arm.
  - The new default is pinned too: `PIN_DEFAULT_42 = 0xc6bc4dbe`.
  - The no-spray checks now run on the default spec (no variant columns; same slot, cover, ground and trees).
  - `LandSigTests.Off_IsTodayByteForByte` and `TownTests.Flatten_OffPins_OutsideIdentical` (tasks 189 / 190) compare their 188a pins on a `-matvariant 1` copy of the OFF spec. Their other assertions run on the new default and pass.
- Trees are unchanged: `c.cover` still carries the noise VARIANT bit, and the tree tests are green.
- TASKS.md has a task 190a paragraph.

## Evidence
- Filter run (MatVariant + the 189/190 pin tests): PASS 6 FAIL 0 (`Logs/overnight/190a/filter1.out`). Log: "variant 1: seed 42 fnv 0x6f6c8910 (pin 0x6f6c8910), seed 7 0xeaaad676 (pin 0xeaaad676)". `-matvariant 1` covers 22.4 % of land columns; the default covers 0 %.
- Whole suite: **PASS 1058 FAIL 0 SKIP 27** (`Logs/overnight/190a/all.out`, wall 25m38s).
- Launch logs:
  - `Logs/overnight/190a/player_default.log` shows "material variant spray OFF (default)" from both StreamManager and FarField.
  - `player_variant1.log` shows "ON (-matvariant 1)".
- Shots: one build and two launches (the setting is a start-up arg), 45 s wait each, noon (sun behind the camera), 188a's poses (`Logs/overnight/190a_shots.sh`, `Logs/overnight/190a/shots.out`).
  - Shell 1 was fully stored at all 3 poses (missing 0). Shell 2 was partial (689-1294 missing). Shells 3+ were unfilled.
  - Because shells 2+ were still filling, small far-horizon differences between the arms come from streaming, not from this change.

## Unfinished / next
- Nothing left in this task. Task 191 (the "everything ON vs OFF" preview) will now show the no-spray default.

## For the doc
- Default material variant spray: **ON -> OFF** (`-matvariant 1` = the old look). Launch log: "material variant spray OFF (default)" (`Logs/overnight/190a/player_default.log`).
- Variant columns on land: **22.4 % -> 0 %** with the new default (seeds 42 + 7, 3,000 land columns; `MatVariantTests`).
- Old look is byte for byte unchanged under `-matvariant 1`: FNV seed 42 0x6f6c8910, seed 7 0xeaaad676 == the pre-188a pins.
- Trees: unchanged (same Cover byte, tree tests green).
- Whole suite: PASS 1058 FAIL 0 SKIP 27 (`Logs/overnight/190a/all.out`).

Images (full size; open `Screenshots/190a/viewer.html`: Space flips, arrows change the pose, Z zooms). Each `pair_*.png` has `-matvariant 1` (the old spray) on TOP and the NEW DEFAULT BELOW:
- `Screenshots/190a/pair_desert_flat.png`: on top, the desert sand in the foreground is covered in dark-brown PackedSand blotches. Below, the sand is one even colour. Only the small orange specks stay; they are rule-made and appear in both shots. The green land behind is the same.
- `Screenshots/190a/pair_savanna_flat.png`: the brown blotches go from the golden grass in the foreground. The pale beach sand along the river and the green bank by the water on the right stay, because the rules make them. The hills and the arch are the same.
- `Screenshots/190a/pair_forest_bank_a.png`: in the middle of the forest grass, the brighter JungleGrass patches go, and so do the small pale-blue ice spots on the snow at the bottom right. The brown mud collars along the river and the pale ice on the shaded snowy slope (upper left) stay, because the rules make them.
