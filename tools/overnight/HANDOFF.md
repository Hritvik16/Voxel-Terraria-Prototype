STOP_FOR_OWNER -- B1's streaming pass line is NOT met on the walk arm. Worker chunk-gen p50 is v4 373-389 ms vs v3 187-372 ms on the same build: +79% against v3's median (210 ms), +3% against v3's slowest arm. Fly 14 m/s passes (v4 463-498 vs v3 314-542). Question: (a) accept B1 (my read: v3 has a run-level "fast mode" v4 has not hit yet, not a per-chunk v4 cost -- evidence below), (b) approve a diagnostic -- a `-biomescale` player flag and an A/B of v4 x1 vs v4 x3 on one build -- or (c) something else?
Owner pick: biome scale x2/x3/x4 (shipped x3) -- Logs/overnight/b1_maps/seed{42,7,2026,31337}_biome_scales.png

# 15-B1-mapv4-record-biome-scale: code done and committed; maps pass; streaming walk fails the 5% line (owner call)

## What changed (commit d1e5587 on surface-rebuild)
- `TerrainSpec.biomeScale` (0 = version default: x3 on v4, ignored below v4) and `SizeClass2(seed, mapVersion)`. `Offsets.biomeScale` and `Offsets.For(spec)`. `ShapeField.Biome` divides by `15000f*s` and `12000f*s`.
- `WorldMap.BuildV4` = `BuildV3` + the new `WorldRecord.cs`: 6 B/cell, 1024^2 cells = **6,291,456 B**.
  - Byte layout: archetype|depth, biome, fresh-water distance, then hazard, flags and site, all three zero for now.
  - Depth is in 32 m steps; fresh water is in 16 m steps, 250 = 4 km+. Both are integer 5/7 chamfer distances.
  - Query with `At(x, z)` in voxels.
  - The biome plane runs on a worker while BuildV3 runs, so it adds ~50 ms to the critical path.
- Player: `-mapversion 4` (`TerrainGenChunkSource.MapVersionArg`). FarField, DevMap and FarBand now take Offsets from the spec. `run-tg2world.sh` accepts `MAPV=4`. The player log prints `world map v4 (biome x3, record 6291456 B)`.
- `PlayMetricsReport`: `PLAYMETRICS_MAPV=3,4` (versions built back to back), biome-scale strips, `b1_biome_scale.txt`, and `[Timeout]` (8 builds take more than 180 s).
- New `WorldRecordTests` (4 tests): v3 biome equals the pre-B1 formula; v4 map equals the v3 map cell for cell; the record is deterministic, differs by seed and is <= 7 MB; biome change >= 2.5x.
- Queued `18-coast-pose-black-bar-check.md`. It is pre-existing, see below.

## Evidence -- maps (EditMode; Logs/overnight/b1_maps/, also Logs/TerrainGen/playmetrics/)
- `SUITE=all` **PASS 622 FAIL 0 SKIP 12** (630 + 4 new): `Logs/overnight/b1_suite_all.out`.
- **v3 unchanged.** Against the 2026-10-02 files (copies in `Logs/overnight/b1_v3_ref/`):
  - `playmetrics.txt` is identical apart from build times.
  - The seed 42 `map.bin` dump (heights, region, water class, biome) is byte-identical.
  - All 20 v3 PNGs are byte-identical.
- **Biome change walking east-west, v3 -> v4:** 297->856 (x2.88), 288->746 (x2.59), 265->852 (x3.22), 295->997 (x3.38). **PASS >= 2.5x on all four.** Seed 42's target was >= 740 m.
- **Median biome patch >= 50 ha: PASS read as the area-weighted median** (v3 64/53/42/42 -> v4 252/187/317/167 ha).
  - The plain count median is 0.3/0.4/0.5/0.2 ha on v3 -> 0.1/0.2/0.2/0.1 ha on v4.
  - The count median is set by coastal specks: 144 of seed 42's 191 sub-1 ha patches touch the coast, and 59 of its 69 land pieces are islets under 1 ha. Scaling biomes cannot move it.
  - The median of patches of 1 ha or more is 5.5-9.3 ha. If you meant the count median, this check fails.
- **Record:** 6,291,456 B (<= 7 MB). Hashes: 42 `8E63407409FB4CE4`, 7 `7CFE2DD0EE320F75`, 2026 `E196E2AD581CC5EC`, 31337 `A72DAE0F99DE7C24`. Rebuilds match; seeds differ; 0 cells differ from ShapeField.Biome.
- **Build time v3 -> v4, same run:** +0.3 / -0.4 / +0.1 / -0.1 s. The limit was +2 s. A single-threaded first version measured +7 s, which is why the biome plane moved to a worker.

## Images (each strip, left to right: v3 (x1) | x2 | x3 | x4; desert sand-yellow, snow white, forest light green, jungle dark green)
- `b1_maps/seed42_biome_scales.png`: v3 is dozens of interleaved ~300 m patches. At x3 one desert band runs from the west coast diagonally through the centre to the south. Snow holds the north, east and south-west, with forest between. At x4 snow takes the western half and desert the north-east.
- `seed7_biome_scales.png`: at x3 forest fills the centre and east, snow the west and north-east coasts, desert the south. x4 adds a big northern snow cap.
- `seed2026_biome_scales.png`: at x3 desert covers the north-west third plus a strip past the volcano (east-centre). Forest is centre/south-west, snow only the south tip.
- `seed31337_biome_scales.png`: at x3 snow is about half the island. **At x4 it is about two-thirds snow**: the clearest case of x4 letting one biome own a world.

## Evidence -- streaming (one player build; 14 launches; AC 100%, no owner-pause NOTE on any arm)
- Full tables: `Logs/overnight/b1_stream_summary.txt`. Arm logs: `Logs/overnight/b1_arm_*.out`.
- **Walk 5 m/s, p50, v3 x4 reps | v4 x4 reps** (reps 3-4 added to chase an outlier):
  - **Worker gen:** 207/372/213/187 | **389/376/376/373 ms**: worse.
  - **Admitted:** 42/25/32/54 | 24/23/23/24 per s: worse.
  - **Deficit p50:** 1988/2676/2577/1819 | 2677/2654/2667/2554: worse.
  - **Isolated gen:** 61/56/54/49 | 58/54/53/53 ms: unchanged.
  - **Upload pass:** 0.30-0.37 | 0.27-0.31 ms: unchanged. **Bytes per frame p95:** ~1.47 MB in both, unchanged.
- **Fly 14 m/s, p50, v3 x3 | v4 x2:**
  - **Worker gen:** 498/542/314 | 498/463 ms: unchanged or better.
  - **Admitted:** 22/20/34 | 19/25 per s: unchanged. **Deficit:** 2911/2920/2843 | 2931/2892: unchanged.
  - **Isolated gen:** 51/53/51 | 42/43 ms: better.
- **Why I read it as a run-level mode, not a v4 cost.** I matched the same chunk coordinates across arms in `streamprobe_chunks.tsv`:
  - In fast v3 arms the same chunks take 0.51-0.55x v4's time. The slow v3 arm matches v4 at 1.02x.
  - Fast arms generate 4,200-4,800 solid chunks; the others ~2,700.
  - Heights, and so the solid/air chunk sets, are identical in v3 and v4; isolated cost is equal.
  - Task 10's two v3 walks (349/354 ms) were also in the slow mode.
  - Running v4 first after a fly arm did not help: v4 373 ms, then v3 187 ms. Startup and PrimeWindow times do not separate the modes.
  - Still, v4 reached the fast mode 0 times in 4. I can't rule out a cause tied to v4 (its startup `Task.Run`, or biome-driven materials) without the flag A/B in (b).
- **`MODE=poses` on v4** (`FarBand/2026-10-03_161645_world/`, peak footprint 3414 MB; walk peaks overlap v3, 3094-3258 vs 3138-3252 MB): deficit 0 at every pose, MIRROR lost/lag/stale 0.
  - `map_7000m_noon.png`: the island from above shows the x3 seed-42 layout (desert diagonal W->S, snow N/E/SW). The dark disc with an orange dot at right is the volcano's ash and caldera, the same as in v3's `2026-10-03_074726_world`. No holes and no floating geometry.
  - `eye_level_1_noon.png`: dark ash foreground, with a lit summit centre-top that has a desert-orange rim. The frame is otherwise the same as v3's.
  - `coast_from_sea_30m_noon.png`: about 50% sea. **A black horizontal bar sits on the water left of centre, about 2/3 down, with two dark arcs below it.** It is identical in v3's 07:47 run, so it predates B1 and is not in KNOWN_ISSUES. Queued as task 18.

## Next
Your answer on the streaming line decides whether 17 (B2) proceeds on v4. If (b): add `-biomescale` (one arg read next to `-mapversion`), rebuild, and run walk v4 x1 vs v4 x3 vs v3, 2 reps each, interleaved.
