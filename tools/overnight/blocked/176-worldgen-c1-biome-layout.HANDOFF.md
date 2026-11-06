STOP_FOR_OWNER -- task 176 (C1 biome layout): "Keep this layout?" Viewer: `Screenshots/176/viewer.html` (arrows = image, 1-4 = fit/100/200/300 %).
- (a) **Recommended: keep it.** The climate's wavelength is 0.75 x the island radius, and every biome is in 2+ regions on all 8 seeds.
  - Cost: biomes change every ~375 m walking E-W. B10 was ~1,050 m and v3 ~290 m, so this undoes most of B1's "larger biomes" (owner Q5).
- (b) Coarser, at 1.0 x the radius (a Python model only, `c1_optionB_k1.0_4seeds.png`): a change every ~480 m, but ~1 seed in 2-3 has a biome in only one region.
- (c) Revert to B10's belts. `-biomevary 0` already gives them, bit for bit.
- **Also needs you: 1 test is red.** `RouteTests.V4_FourSeeds_RoutesJoinTheEasyAreas`, seed 42: "0.5 m of Sheer on the voxel ground".
  - The biome feeds danger, so seed 42's route 0-1 now takes a new path. It fords a river at (3086, 4781) m with a 2.3 m bank (voxel 5140 vs 5117 beside it).
  - The bank existed before; this is 173's finding 3, "rivers as canals with levees" (C5's ground). I did NOT loosen the gate.
  - Options: ease ford banks in `Routes`, or leave it red until C5 carves the banks.
- Saves: biome borders move on every seed, so existing saves' deltas would land on shifted terrain. Seed 42's map heights also moved: the routes ease the map.

# 176-worldgen-c1-biome-layout: the climate varies across the island and transitions are patch-wise; 8-seed gates pass; 1 route test red

## What changed (commit "176: ..." on engine-phase; `git log -1 --grep '^176:'`)
- `ShapeField.cs`: `Biome6` uses the new climate when `o.biomeVary` is set:
  - `Climate3` has 3 octaves (1 / 0.35 / 0.2).
  - Wavelengths come from `CLIMATE_WAVE_R` x islandRadius.
  - `ClimateNormalise` sets mean 0 and sd 0.7 over the island disc, per world, using seed noise only.
  - No domain warp; the six climate centres are unchanged.
  - Blend: the per-cell hash dither is replaced by a 10 m patch octave. The early-out bound is updated.
- `TerrainSpec.biomeVaryOff` / `BiomeVary`, `Offsets.biomeVary/climMean/climGain/climWave`, and the `-biomevary 0` flag (chunk source + FarField).
- Tests:
  - New `BiomeLayoutTests` (the C1 gates on 8 seeds; `-biomevary 0` is still B10, bit for bit).
  - New `BiomeLayoutDump` (report: the planes for the tool).
  - `WorldRecordTests`: the B1/B10 gates now measure the B10 field (`biomeVaryOff 1`), the record check runs under Burst, and `Change` / `AreaMedianHa` are internal.
  - `BiomeBlendTests`: the band share gate goes from N/20 to N/8. The island has 2.7x the border length (border cells 5,961 -> 16,253, 20-seed mean); the band itself is as wide as before.
  - `ShellFillTests` GOLDEN re-made (83/85 chunks moved; fast == reference still).
  - `WorldMapBurstTests` player hash re-pinned to 74C7E69A614F8996, read from the PLAYER's `Logs/tg2world_20261105_201249.txt`. It equals the Editor's, so there is still one generator path.
- `PlaygroundCapture.WorldSurvey.cs`: `-wsfixed` adds fixed poses. `tools/biome176.py`: stats, maps, viewer, plus a Python snoise port used to choose the parameters on 20 seeds. TASKS.md: task 176 paragraph.

## Evidence
- 8-seed gates: `Screenshots/176/c1_layout.txt`. Before | after on 20 seeds: `Screenshots/176/c1_layout_20seeds_before_after.txt`.
- Whole suite, run 1: PASS 1052 FAIL 3 SKIP 30 (`Logs/overnight/176_all.out`, 33 min). Two of the failures were pins that legitimately moved and were fixed (filter run: PASS 2).
  - Run 2: **PASS 1054 FAIL 1 SKIP 30** (`Logs/overnight/176_all2.out`, 33 min). The one failure is the route test above.
- Survey launch: `Logs/lookreview_20261105_191830.txt`. Shots in `Screenshots/176/shots/`. The "before" shots are 173's native shots (`Screenshots/173/shots/`), same cameras.
  - Shells s3-s7 were mostly missing after the 30 s cap, so the far terrain is the far band.
- I looked at every pair:
  - Peak: 173's straight vertical ochre|red seam is now a diagonal red badlands | grey snow split. It is still two biomes across the peak.
  - Start: same spot. Its cell is now snow x alpine range (was desert), with badlands strata below.
  - Flat border: savanna | forest interfingering in metre-scale patches instead of pixel speckle (`crop_border_before_after.png`).
- Rough cost (look-stage rule, from these runs; no timing arms):
  - Shell fill on a worker: ~85 ms -> ~100 ms per chunk (same poses, 173's log vs this one; warm chip, rough).
  - The blend early-out now skips no columns: its Lipschitz bound is loose at the shorter wavelengths, so every column runs 14 extra snoise.
  - Map build: v4 1.4-1.6 s -> 1.5-1.7 s.

## Next
- Owner answers (a)/(b)/(c) and the route question. If (a): queue C2 (ground cover).
- Optional, perf stage: a tighter early-out (e.g. a per-world gradient bound) to win back the shell fill time.

## For the doc
- Longest straight biome border, 8 seeds: 1.7-2.9 km -> **1.04-1.65 km** (7 of 8 seeds <= 1.27 km). Measured at +-30 m (`Screenshots/176/c1_layout.txt`).
- Seeds with a biome in only one region: 7 of 8 -> **0 of 8** (20 seeds: 19 -> 2).
- Biome shares of land: 1.0-52.9 % -> **9.2-26.5 %**.
- Biome change walking E-W: ~1,050 m -> **~375 m** (v3 ~290 m). Area-weighted median patch: 144-886 -> 41-94 ha.
- Shell fill: ~85 -> ~100 ms per chunk (rough).
- Images:
  - `Screenshots/176/c1_maps_4seeds.png`: seeds 42/7/2026/31337 as rows. Left B10, right C1. Look at seed 42's straight NW-SE belts (left) vs the patchwork (right); the magenta line is each map's longest straight border.
  - `Screenshots/176/pose_great-peak-0_before_after.png`: the same camera. Left, the straight vertical seam down the 600 m peak; right, a diagonal red | grey biome split.
  - `Screenshots/176/crop_border_before_after.png`: full-res. Top, 173's per-pixel speckle at a border; bottom, C1's patches of metres.
