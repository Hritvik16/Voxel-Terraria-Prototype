STOP_FOR_OWNER -- task 176b: "Which size?" Viewer (full size): `Screenshots/176b/viewer.html` (arrows = seed, 1-4 = fit/100/200/300 %).
Each sheet shows six maps. Top row: BEFORE (B10 belts) | NOW (176, 0.75) | SIZE 1. Bottom row: SIZE 2 | SIZE 3 | SIZE 4.

| | Wave x island radius | Biome change walking E-W (4 seeds, real generator) | Longest straight border | Biome missing? (20 seeds, Python model) |
|---|---|---|---|---|
| Before (B10) | ~1.6, 1 octave | 1,027 m | 1.8-2.9 km | -- |
| Now (176) | 0.75 | 375 m | 1.06-1.27 km | 1 of 20 |
| Size 1 | 1.0 | 479 m | 1.17-1.37 km | 0 of 20 |
| **Size 2 (recommended)** | 1.5 + a 4th octave | **596 m** | **1.06-1.42 km** | 0 of 20 |
| Size 3 | 2.0 | 911 m | 1.39-1.89 km | 1 of 20 (31337: desert 1 %) |
| Size 4 | 3.5 | 1,374 m | 1.42-1.82 km | 1 of 20 |

- **(2) Recommended: Size 2.** Changes come 1.6x further apart than now, and the median patch grows 2-3x (106-186 ha vs 44-65 ha).
  - Its borders are as curvy as 176's or curvier: on 20 seeds the median straight border is 1.19 km (176: 1.25 km).
  - All six biomes are on every seed (each at least 6 % of the land).
  - It is the largest size that keeps the reason for 176, "no straight belts".
  - Cost: some biomes sit in one region only (33 of 120 on 20 seeds; allowed now).
- (3) If larger matters more than straight borders: Size 3 gets close to B10's size, about 900 m.
  - Long straight borders come back: up to 1.9 km here, 2.1 km on 20 seeds. The C1 gate is 1.7 km.
  - A biome can almost vanish: seed 31337's desert is 1 % of the land.
- (4) Size 4: about one big region per biome, the "bold" extreme.
  - Region sizes swing a lot between seeds (783-1,752 m).
  - The smallest biome is often 3-6 % of the land.
- (1) Size 1 is only a little larger than now.
- **Applying the size is a follow-up task** (`CLIMATE_WAVE_R`, re-pinned goldens, whole suite).
  - Size 2 also adds a 4th climate octave (amplitude 0.12): `Climate3` plus the bound in `BlendClimateReach`.
  - Size 3 or 4 would also need your OK to loosen the `BiomeLayoutTests` gates (straight <= 1.7 km, share >= 5 %, 2+ regions).

# 176b-worldgen-biome-size-choice: 4 region sizes rendered on the real generator, 4 seeds; recommend Size 2 (1.5 x R)

## What changed (commit 1ea882f "176b: ..." on engine-phase; no generator code, no default changed)
- `Assets/TerrainGen/Tests/BiomeSizeDump.cs` (Slow, report-only; run with `REPORTS=1` and a `-testFilter`).
  - It builds the real v4 map for seeds 42 / 7 / 2026 / 31337 and writes these biome planes on that map's land mask: b10, c176 and the `C1SIZES` variants.
  - The variants use ShapeField's octave chain, the per-world normalisation and the nearest-centre rule.
- `tools/biome176b.py`: stats + sheets + viewer (it reuses biome176.py's metrics). TASKS.md has the 176b paragraph.

## Evidence
- The dump run: `Logs/overnight/176b_dump.log`, check lines in `Logs/TerrainGen/c1/sizes/check.txt`.
  - The octave chain matches `ShapeField.Climate3` within 1.2e-7 (14,374 of 20,000 points bit-equal).
  - The 0.75 variant reproduces the generator's record plane except 50 / 134 / 97 / 27 of 1,048,576 cells (seeds 42 / 7 / 2026 / 31337).
  - That residue is Burst vs Mono float rounding (KNOWN_ISSUES). So these maps are the generator's own.
- Per-plane, per-seed stats: `Screenshots/176b/sizes_176b.txt` (change, straight, median ha, regions per biome, shares, one-region and absent biomes).
- 20-seed check of the same variants in the Python port: `Screenshots/176b/sizes_20seeds_python.txt`. The port matches the generator on 98-99 % of land cells (176's land masks).
- Fast suite: **PASS 1000 FAIL 0 SKIP 16** (`Logs/overnight/176b_fast.out`).
- I looked at all 4 sheets:
  - Size 2, seed 42 keeps a winding NW-SE ochre band that echoes B10. It is the same base noise at nearly the same wavelength, but it is bent, not straight.
  - Size 4 is 5-6 big blobs per island. On seed 42, a central red badlands disc sits in a forest ring.
  - Size 3 brings back a 1.9 km straight snow | ochre edge on seed 42.
- Land mask = the current (176) map for every plane. The map's heights depend on the biome only through the route easing, so a size change barely moves the coast.
- Unmeasured: a longer wavelength should let the blend early-out skip columns again. 176 measured shell fill go from ~85 to ~100 ms per chunk when it stopped doing so. Check this in the performance stage.

## Known, not fixed
- The Slow route test is red (river ford bank). The owner leaves it red until C5.

## Next
- When the owner picks a size, queue the follow-up: set the constant(s), adjust the BiomeLayoutTests gates if Size 3/4, re-pin the goldens, run `SUITE=all`.

## For the doc
- Biome change walking E-W: B10 1,027 m | now 375 m | sizes **479 / 596 / 911 / 1,374 m** (`Screenshots/176b/sizes_176b.txt`).
- Longest straight border: B10 1.8-2.9 km | now 1.06-1.27 km | sizes 1.17-1.37 / **1.06-1.42** / 1.39-1.89 / 1.42-1.82 km.
- Area-weighted median patch: now 44-65 ha | Size 2 106-186 ha | Size 3 175-255 ha | Size 4 271-430 ha.
- Seeds with a biome missing (20 seeds, Python): sizes 0 / 0 / 1 / 1 of 20. One-region biomes: 10 / 33 / 46 / 81 of 120 (`Screenshots/176b/sizes_20seeds_python.txt`).
- Images (all six panels per seed):
  - `Screenshots/176b/full_seed42.png`: full size. Top row B10 | 176 | Size 1, bottom Size 2 | 3 | 4. The colour patches grow left to right; magenta = the longest straight border. Compare top-left's straight belt with the bottom row.
  - `Screenshots/176b/sheet_seed7.png`: the same 6 panels as thumbnails. Bottom-right (Size 4) is about one region per biome; bottom-left (Size 2) is about 10 regions of ~1 km.
  - `Screenshots/176b/sheet_seed31337.png`: bottom-middle (Size 3) has no desert left (1 % of the land). That is the risk of the larger sizes.
