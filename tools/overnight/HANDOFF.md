# 190b-landsig-default-on: DONE. Landform signatures (task 189: dunes, canyonland benches + slot canyons, alpine ridges) are now the DEFAULT everywhere (play, rigs, capture runs, tests). `-landsig 0` brings back the pre-189 ground, byte for byte.

## What changed (commit 448c579)
- `TerrainSpec.landSigOn` became `landSigOff`, the same pattern as 187a / 190a. A default spec (byte 0) now has signatures; `LandSig => mapVersion >= 4 && landSigOff == 0`.
- `TerrainGenChunkSource.LandSigArg()` now defaults to true; `-landsig 0` sets `landSigOff = 1`. `FarField` does the same for the far band's spec, so near chunks, FarQuery, shells, shell fill and the far band agree.
- `WorldMap.Build` now builds the LandSig weight plane whenever `spec.LandSig` is on. Reason: `ShapeField.Sample` quietly takes the OFF path when a map has no plane, so without this any map built from a default spec outside the engine's two call sites (tests, tools) would have shown the OFF ground. The engine's explicit `EnsureLandSigPlane` calls stay (no-ops now).
- `WorldMap.LandSigBuildMs` (new) times the plane build. Launch log lines print the setting in force:
  - `[StreamManager] tg2 chunk source: ..., landform signatures ON (default; plane 3072 KB in 34 ms)`
  - `[FarField] tg2 WORLD ...` ends the same way. With `-landsig 0`: "landform signatures OFF (-landsig 0)".
- Scene-builder landmine: no SerializeField carries this setting (only the arg and the spec); the log lines above prove the flip took effect in a built player.
- Tests (no assertion loosened):
  - `LandSigTests` builds its world from the default spec (ON); `off` is `landSigOff = 1`. `Off_IsTodayByteForByte` still matches 188a's pins on OFF, and now asserts the default is ON and that `WorldMap.Build` built the plane. New `Default_IsOn_Pinned`: seed 42 `0x5647ec7f`, seed 7 `0x08a2a095`, and the default differs from `-landsig 0`.
  - `MatVariantTests.Variant1_IsTodayByteForByte`: 188a's pins on `-matvariant 1 -landsig 0`; 190a's `PIN_DEFAULT_42` (0xc6bc4dbe) on `-landsig 0`; today's default on new `PIN_DEFAULT_190B_42` = 0x5647ec7f (the same as LandSig's ON pin, as it should be: both hash the same 600 columns).
  - `TownTests.Flatten_OffPins_OutsideIdentical`: its 188a pin runs on `-landsig 0`. Every other town assertion (fast and Slow) now runs on the ON default and passes.
- TASKS.md: task 190b paragraph. `Screenshots/190b/viewer.html` committed.

## Evidence
- Filter run (the 4 pin tests): first pass gave the new pins (`Logs/overnight/190b/filter1.out`); fast suite after pinning: PASS 987 FAIL 0 SKIP 19 (`Logs/overnight/190b/fast1.out`).
- Whole suite: **PASS 1059 FAIL 0 SKIP 27** (`Logs/overnight/190b/all.out`, wall 26m14s; `Logs/overnight/190b/TestResults_all.xml`).
- Cost of building the plane by default (from the launch log, no timing arms): 1024 x 1024 cells x 3 B = **3,072 KB** of CPU memory, built in **34 ms** in the player (`Logs/overnight/190b/player_default.log`). The plane is shared by the chunk source and the far band (one map); no GPU memory. Shell pools were not refused in either arm (`[pools]` lines, r0).
- Shots: one build + two launches (the setting is a start-up arg), 45 s wait each, noon, sun behind the camera, 189's two good poses (`Logs/overnight/190b_shots.sh`, `Logs/overnight/190b/shots.out`). `dune_field_low` skipped (broken pose per 189). After 45 s shell 1 was fully stored (missing 0), shell 2 partial (597-702 missing), shells 3+ largely unfilled, in both arms, so small far-horizon differences between arms are streaming, not this change.

## Unfinished / next
- Nothing left in this task. Task 191's "everything ON vs OFF" preview will now show signatures in the default.
- Watch item (no failure): town rings blend back to the natural ground, which now includes the signatures. Tier-1 towns sit on easy areas, where the plane is 0 by construction; a tier-2 town in canyonland could have a ring that blends into a slot or bench. TownTests stay green; worth a look in 191's preview.

## For the doc
- Landform signatures: **OFF -> ON by default** (`-landsig 0` = the old ground). Launch log: "landform signatures ON (default; plane 3072 KB in 34 ms)" (`Logs/overnight/190b/player_default.log`).
- Cost of the default plane: **+3,072 KB CPU memory, +34 ms at world start** (player log). No GPU memory.
- Old ground is byte for byte unchanged under `-landsig 0`: seed 42 `0x6f6c8910`, seed 7 `0xeaaad676` (188a pins); new default pinned at seed 42 `0x5647ec7f`, seed 7 `0x08a2a095` (`LandSigTests`).
- Whole suite: PASS 1059 FAIL 0 SKIP 27 (`Logs/overnight/190b/all.out`).

Images (full size; open `Screenshots/190b/viewer.html`: Space flips, arrows change the pose, Z zooms):
- `Screenshots/190b/pair_desert_canyonlands_default.png` vs `..._landsig0.png`: the biggest change. In `-landsig 0` the whole desert is a lumpy sand sheet with a rounded red hill on the right. In the NEW DEFAULT the middle becomes a flat bench plain crossed by thin winding slot canyons (the dark lines), the right side is a tall stepped red cliff band (mesa with two rock layers), and the left horizon is a rough band of eroded red rock. The green river strip in the foreground is the same in both (keep-out ground).
- `Screenshots/190b/range_200up_default.png` vs `..._landsig0.png`: subtle at 1.5 km. Look at the left face of the big mountain: the default has grooves running down it and a notched, stepped summit line on its right shoulder; `-landsig 0` is a smooth dome. The red ridge at the right is a little more jagged in the default.
