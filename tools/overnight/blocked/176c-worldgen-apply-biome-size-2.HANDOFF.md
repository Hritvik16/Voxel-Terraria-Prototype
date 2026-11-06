STOP_FOR_OWNER -- task 176c (apply biome Size 2): a BiomeLayoutTests gate fails, and the task says not to loosen it. Picture: `Screenshots/176b/full_seed42.png`, bottom-left panel (Size 2). It has ONE ochre desert patch and ONE savanna patch. Numbers: `Screenshots/176c/gates_176c.txt`.
- **Q1. The "every biome is in 2+ separate regions of >= 25 ha" gate fails on all 8 gate seeds with Size 2.** On each seed, 1-3 of the 6 biomes are one big region.
  - Desert is the one-region biome on 5 of 8 seeds.
  - Seed 2 has three one-region biomes (D, S, B).
  - 176b's 20-seed table showed this already: 33 of 120 biomes were one-region at Size 2. Even Size 1 fails the gate (10 of 120).
  - So 176b's note that "Size 2 passes the gates" was wrong. Only 176's current size passes it.
  - Every other C1 gate passes at Size 2 on the 8 seeds: straight border 1.06-1.42 km (gate <= 1.7), shares 5.7-28.3 % (gate 5-35), E-W change 512-701 m (gate >= 300), median 106-231 ha (gate >= 35).
  - (a) **Recommended:** the gate becomes "every biome present (>= 1 region of >= 25 ha)". The straight-border, change and median gates still catch B10's long straight belts. The 176c patch then applies as written.
  - (b) Gate = "at most 3 one-region biomes per seed". This is stricter, but the 3 is fitted to this result (seed 2).
  - (c) Keep the gate. Size 2 does not ship, and the world stays at 176's size.
- **Q2. The known red route test moved.** It was seed 42 (0.5 m Sheer); now seed 42 passes and seed 31337 fails (1.5 m Sheer).
  - The cause is the same, river banks: every Sheer sample has 3-4 wet neighbours, e.g. a 0.9 m bank step at (2475, 3128).
  - (a) **Recommended:** leave it red until C5, as before. (b) Fix the ford banks now.

# 176c-worldgen-apply-biome-size-2: Size 2 applied and proven equal to 176b's plane; BLOCKED on the C1 "2+ regions" gate (not loosened)

## What changed (nothing committed to the generator; the edit is saved as a patch)
- `Assets/TerrainGen/ShapeField.cs`:
  - `CLIMATE_WAVE_R` 0.75 -> 1.5. New `CLIMATE_OCT4` 0.12, added as the 4th octave of `Climate3`.
  - `BlendClimateReach`'s gradient bound gets the `+ OCT4 * 2.03^3` term.
  - `-biomevary 0` takes a different branch, so it is untouched (`VaryOff_IsB10sField` passed).
- `Assets/TerrainGen/Tests/BiomeSizeDump.cs`: `live` = 4 octaves. New check 3 compares the record plane with 176b's stored `sizes/w150` plane.
- The patch is `Screenshots/176c/size2_176c.patch` (committed text). The runner also keeps its own copy in `blocked/`. If the owner picks Q1 (a) or (b): apply the patch, edit the gate, then re-pin the goldens.

## Evidence
- Size 2 is exactly 176b's plane. The record plane differs from `Logs/TerrainGen/c1/sizes/w150` in 73 / 170 / 93 / 23 of 1,048,576 cells (seeds 42 / 7 / 2026 / 31337).
  - That is Burst-vs-Mono residue, the same as 176b's own 0.75 check. Logs: `Logs/TerrainGen/c1/size2c/check.txt`, `Logs/overnight/176c_filter1.out`.
- Whole suite with the patch: **PASS 1058 FAIL 4 SKIP 32** (`Logs/overnight/176c_all.out`, 36 min):
  - `C1Layout_VariesAcrossTheIsland_OnEightSeeds`: the gate in Q1. NOT loosened.
  - `RouteTests.V4_FourSeeds_RoutesJoinTheEasyAreas`: the known river-bank red, now on seed 31337 (Q2).
  - `ShellFillTests.Fill_BytesAsGolden_FastAndReference`: the GOLDEN legitimately moves (the biome under the shell chunks changed). Not re-pinned.
  - `WorldMapBurstTests.Seed42V4_EditorMapIsThePlayersMap`: the pinned player hash legitimately moves. It needs a player log to re-pin; not re-pinned.
- Passed with the patch: every `BiomeBlendTests` case, including `EarlyOut_IsExact` with the new 4-octave bound and `SnoiseGradient_WithinTheEarlyOutBound`. Also `CoverTests` (177's shares) and every other `BiomeLayoutTests` case.

## Not done (waits on Q1)
- Re-pin ShellFill GOLDEN + the player hash.
- The `-worldsurvey` before|after sheet, using 177's 5 `-wsfixed` poses (`Logs/lookreview_20261106_012752.txt` = before).
- TASKS.md paragraph. No launch was made.

## Next
- Owner answers Q1/Q2. If Q1 (a)/(b): queue 176c again with the answer.
  - Steps: `git apply Screenshots/176c/size2_176c.patch`, change the gate in `BiomeLayoutTests` (line 59), re-pin the 2 goldens (the player hash from a PLAYER log; the survey launch gives one), take one survey launch, run `SUITE=all`.

## For the doc
- Biome change walking E-W, 8 gate seeds: 176 ~375 m -> Size 2 **512-701 m**. Area-weighted median patch: 44-65 -> **106-231 ha** (`Screenshots/176c/gates_176c.txt`).
- Longest straight border: 176 1.04-1.65 km -> Size 2 **1.06-1.42 km** (gate 1.7 km).
- Biomes with a single region: 0 of 48 -> **14 of 48** (8 seeds x 6 biomes). This is the failing gate.
- Generator vs 176b's Size 2 plane: 23-170 of 1,048,576 cells differ (Burst-vs-Mono residue).
- Images (176b's, the same plane to within the residue above):
  - `Screenshots/176b/full_seed42.png`: bottom-left = Size 2. Look for ONE desert (ochre) patch and ONE savanna patch, the two one-region biomes on seed 42.
  - `Screenshots/176b/sheet_seed7.png`: bottom-left = Size 2. Seed 7's badlands (red) is a single region.
