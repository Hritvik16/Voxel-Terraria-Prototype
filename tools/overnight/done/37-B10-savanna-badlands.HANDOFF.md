Owner pick: savanna / badlands colours -- Screenshots/B10/candidates_noon.png, candidates_golden.png, palette_candidates_swatches.png (a shipped; b, c rendered in the same build pipeline at the same poses)
# 37-B10-savanna-badlands: DONE. Map v4 now has 6 biomes (savanna, badlands added); every B10 gate passes on seeds 42/7/2026/31337; SUITE=all PASS 654 FAIL 0 SKIP 17. Commits 671219d (code), 4c7f368 (evidence).

## What changed
- **Biome field (v4 only, `TerrainSpec.BiomeSet`; 6 = default on v4, 4 = B1-B8's field, so it is reversible).** `ShapeField.Biome6`.
  - Nearest climate centre in (temperature, moisture). The four old centres are unchanged.
  - **Savanna (0.8, 0):** hot, between desert's dryness and jungle's wet.
  - **Badlands (-0.3, -0.8):** dry and cool-temperate, the cold side of desert.
  - To keep >= 900 m with six biomes: ONE climate octave instead of three (octaves 2-3 only added km-scale meanders = extra biome changes), amplitude 1.6 (was 2.4), scale x3.25 (was x3).
  - v3 is byte-identical (the v3 biome test and the TerrainGen suite pass).
- **Materials (GenMat, palette D, `tools/palette_defs.py` `B10_CANDIDATES`).** 12 new: savanna grass 15, badlands clay 16, red earth 24, hardpan 25, kopje granite / ironstone / gneiss 35-37, red / cream / ochre band 45-47, umber mudstone 48, plum shale 49.
  - Badlands' exposed rock is 3.2 m height bands (`DetailField.BadlandsBand`). Slope rock rule (step > 1.5) unchanged; acid dissolves the new materials; none is emissive or falling.
  - `gen-tg2-materials.py --b10 b|c` writes a candidate into the engine; plain `--write` restores a.
- **Danger tiers:** savanna 0, badlands 1.
- **B8 fix needed for its gate (`Routes.StampKeepOut`).** The new danger tiers moved seed 42's 0-1 link to run UNDER a fossil's ribs, 64 m from the skull. That gave 8 m of Sheer.
  - The keep-out was 28 m round the skull only; now 73 m round the rib cage's centre (ribs reach 100 m along the axis). Other stamps' keep-outs checked: correct.
- **Tests:** new `WorldRecordTests.V4SixBiomes_B10Gates` (Slow), writes `Logs/TerrainGen/playmetrics/b10_biomes.txt` + the B8|B10 PNGs. v3 biome-mix test reads `GenBiome.V3Count`; DevMap names, PlayMetricsReport colours, FarField log ("6 biomes") updated.

## Evidence (`Screenshots/B10/b10_biomes.txt`)
| seed | biome change E-W, B8 -> B10 | area-wtd median patch | savanna / badlands share of land | v3 / v4 map build |
|---|---|---|---|---|
| 42 | 861 -> 1001 m | 247 -> 320 ha | 8.9 / 8.0% | 26.8 / 26.7 s |
| 7 | 757 -> 996 m | 186 -> 153 ha | 11.4 / 11.0% | 26.4 / 28.8 s |
| 2026 | 850 -> 1031 m | 316 -> 245 ha | 19.7 / 15.3% | 27.3 / 26.3 s |
| 31337 | 1005 -> 1074 m | 865 -> 226 ha | 4.0 / 31.9% | 26.3 / 24.0 s |

- **Gates:** both new biomes are on 4 of 4 seeds. The record is 6,291,456 B. The biome plane takes 2.3 s (B8's took 6.9 s; it runs beside the map build). On seed 7 the v4 build is +2.4 s over v3, but the two builds ran at different points of one test run (SharedMaps); the biome code itself got cheaper.
- **B4/B5/B6/B8:** all pass. B8 has 0 m of Sheer on every seed (seed 42 Walk+Scramble 93.2%). The hash/determinism tests and `PaletteSyncTests` pass. `PaletteSampleTests` is report-only (skipped). Other biomes' shares are in the txt (forest 12-27%; desert 1% on 31337; jungle 4% on 42).
- **`MODE=poses` on v4:** `FarBand/2026-10-09_081647_world/`, log `Logs/overnight/b10/poses_v4.out`. MIRROR lost/lag/stale 0 and deficit 0 at every pose. Peak footprint 3345 MB, which is the known 3 GB-cap issue (B1 measured 3414).

## Pictures (`Screenshots/B10/`)
- **`seed{42,7,2026,31337}_b8_b10_biome.png`** (left B8, right B10; savanna gold, badlands terracotta, desert sand, snow white, forest green, jungle dark green):
  - 7, 2026 and 31337 show large organic blobs.
  - **Seed 42 is parallel diagonal STRIPES** (snow | badlands | forest | desert | savanna | forest | jungle | snow). Its climate field is nearly a linear gradient over the island. A weak 2nd octave bends the stripes but drops seed 2026 to 852 m (< 900).
- **`candidates_noon.png` / `candidates_golden.png`** (rows: savanna gentle, savanna broken, badlands gentle, badlands broken; columns a | b | c):
  - a's gold-straw savanna is distinct from desert sand.
  - b is olive-khaki; c is tawny orange, close to desert.
  - Badlands: a and b are pink-terracotta clay; c is pale buff.
  - The red/cream/ochre bands show on the far slopes in the badlands-gentle row (top left of each frame). Golden hour reads them best.
  - The savanna-broken golden-hour row is mostly in a hill's shadow: judge that pose at noon.
- **`far_views_a.png`** (60 m up, about 1.5-2 km out, noon | golden):
  - The far band shows the new colours.
  - **Biome borders are hard straight lines** in the far band (no blending). Most visible on seed 42's stripes.
- **`poses_v4_contact.png`** (12 v4 poses at noon):
  - No holes and no floating geometry. The coast pose is mostly sea by design.
  - The black oval in `ledge_view` is task 40. The map views show the B10 stripes.
- **Raw shots** (uncommitted, on disk): `Screenshots/B10/cand_{a,b,c}/`.
  - `cand_b/old_badlands_poses/` holds the first, north-facing badlands poses. They were too dark, and the poses were re-aimed east.
  - Pose list: `Screenshots/B10/tools/shots2.sh`.

## For the owner (no stop)
1. **Colours:** pick a, b or c per biome, or name a mix (e.g. savanna a + badlands c). It is a palette_defs edit plus `gen-tg2-materials.py --write`.
2. **Seed 42's stripes and the hard straight biome borders in the far band:** acceptable, or do you want an edge-blend / boundary-warp step? That would be new design work: it costs biome changes, and the 900 m line would need another look.
3. **Biome scale is now x3.25** (B1's pick was x3), with one climate octave. This is what holding 900 m with six biomes took.
4. **Naming:** `Regions.Badlands` (a LANDFORM) and `GenBiome.Badlands` (a BIOME) are now separate things with the same name. Rename the landform (e.g. "breaks")?

## Next
(Session note: the first B10 session hit the usage limit at 08:27 right after its evidence commit; a restarted session at 08:47 checked the work -- SUITE=all 654/0/17 at 06:29, after the last code change; tree clean except the usual Playground.unity fileID churn, left uncommitted -- and changed only this file.)
Task 37b-B5b-deepen-canyons (37b and 37c both required this task).
