// Assets/TerrainGen/Tests/OreTests.cs
//
// Gameplay step 3 (task 66, GAMEPLAY_PHASE_PLAN.md): ores by depth and biome x
// landform, map v4 (Ores.cs). The plan's acceptance:
//   - same seed -> same ore hash (twice), and Burst == Mono for the placement;
//   - every ore cell inside rock, within its layer's depth band, deeper than 6 m;
//   - per-pair density within +-25% of the table;
//   - total ore < 0.5% of rock;
//   - v3 has no ore.
// Real chunks: seed 42, map v4, four land sites x five depth layers, under Burst
// with the engine's brick geology (TerrainGenChunkSource.BRICK_GEOLOGY_DEPTH),
// each chunk twice (ores off / on). The report (REPORTS=1) writes ore counts per
// layer x biome and the mixed-brick share before -> after:
// Logs/TerrainGen/ore_report.txt.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class OreTests
    {
        private const uint SEED = 42;
        private const int CHUNK = 128;
        private const int BRICK_GEOLOGY_DEPTH = 200;   // TerrainGenChunkSource's

        // ---- stats layout (OreJob) ----
        private const int S_DENSE = 0, S_ROCK = 1, S_NOT_ROCK = 2, S_BAND = 3, S_SHALLOW = 4, S_HASH = 5, S_ORE = 6;
        private const int S_ORE_LB = 8;                       // + layer * 6 + biome
        private const int S_ROCK_LB = S_ORE_LB + 30;          // + layer * 6 + biome
        private const int S_ORE_PAIR = S_ROCK_LB + 30;        // + pair multiplier (0..63): ore voxels
        private const int S_ROCK_PAIR = S_ORE_PAIR + 64;      // + pair multiplier: rock voxels in a column of that multiplier
        private const int S_LEN = S_ROCK_PAIR + 64;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct OreJob : IJob
        {
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls, plane;
            public TerrainSpec spec; public Offsets o;
            public int3 origin;
            public bool ores;
            public NativeArray<ColumnSample> colS;
            public NativeArray<float> colStep;
            public NativeArray<DetailField.ColumnDetail> colCd;
            public NativeArray<long> stats;

            public unsafe void Execute()
            {
                var map = new MapView { height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                                        water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                                        cells = spec.mapCells, cellVoxels = spec.mapCellVoxels };
                for (int k = 0; k < CHUNK; k++)
                for (int i = 0; i < CHUNK; i++)
                {
                    int x = origin.x + i, z = origin.z + k, ci = k * CHUNK + i;
                    var c = ShapeField.Sample(in spec, in map, in o, x + 0.5f, z + 0.5f, 1);
                    colS[ci] = c;
                    colStep[ci] = ShapeField.MaxStep(in spec, in map, in o, x + 0.5f, z + 0.5f, 1, c.height);
                    var cd = DetailField.PrepareColumn(in spec, in o, x, z);
                    if (cd.oreMul != 0) cd.oreMul = ores ? plane[Ores.CellIndex(x, z, spec.mapCells, spec.mapCellVoxels)] : (byte)0;
                    colCd[ci] = cd;
                }
                uint hash = 2166136261u;
                for (int bz = 0; bz < CHUNK; bz += 8)
                for (int by = 0; by < CHUNK; by += 8)
                for (int bx = 0; bx < CHUNK; bx += 8)
                {
                    var cr = DetailField.CullBrick(in spec, in o, origin.x + bx, origin.y + by, origin.z + bz);
                    byte first = 0; bool dense = false;
                    for (int dz = 0; dz < 8; dz++)
                    for (int dx = 0; dx < 8; dx++)
                    {
                        int ci = (bz + dz) * CHUNK + bx + dx;
                        var c = colS[ci]; var cd = colCd[ci]; float st = colStep[ci];
                        int x = origin.x + bx + dx, z = origin.z + bz + dz;
                        for (int dy = 0; dy < 8; dy++)
                        {
                            int y = origin.y + by + dy;
                            byte m = DetailField.Material(in spec, in o, in c, in cd, st, x, y, z, in cr);
                            if (dz == 0 && dx == 0 && dy == 0) first = m;
                            else if (m != first) dense = true;
                            if (m < 30 || m > 74) continue;                  // not layer rock (nor ore)
                            int layer = Ores.Layer(in spec, y, cd.deepOffset, cd.cavernOffset, cd.underOffset);
                            int pair = math.min((int)cd.oreMul, 63);
                            stats[S_ROCK]++;
                            stats[S_ROCK_LB + layer * 6 + c.biome]++;
                            stats[S_ROCK_PAIR + pair]++;
                            if (!Ores.IsOre(m)) continue;
                            stats[S_ORE]++;
                            int ol = Ores.LayerOf(m);
                            stats[S_ORE_LB + ol * 6 + c.biome]++;
                            stats[S_ORE_PAIR + pair]++;
                            hash = (hash ^ (uint)((((y - origin.y) << 14) | ((z - origin.z) << 7) | (x - origin.x)) * 131 + m)) * 16777619u;
                            // Inside rock: the same voxel without ore is layer rock.
                            var cd0 = cd; cd0.oreMul = 0;
                            byte m0 = DetailField.Material(in spec, in o, in c, in cd0, st, x, y, z, in cr);
                            if (m0 < 30 || m0 > 74 || Ores.IsOre(m0)) stats[S_NOT_ROCK]++;
                            // Deeper than 6 m under the column top.
                            if (y > c.height - Ores.MIN_DEPTH) stats[S_SHALLOW]++;
                            // Within the layer's nominal band, widened by the boundary
                            // waves (deep 8 m, cavern 2.5 m, underworld 2 m) and half a
                            // brick (brick geology decides at the brick centre).
                            int sea = spec.seaLevelY, lo, hi;
                            const int WD = 80 + 8, WC = 25 + 8, WU = 20 + 8;
                            switch (ol)
                            {
                                case 0: lo = sea - DetailField.STONE_BOTTOM_BELOW_SEA - WD; hi = int.MaxValue; break;
                                case 1: lo = sea - Ores.GOLD_TOP_BELOW_SEA - WD; hi = sea - DetailField.STONE_BOTTOM_BELOW_SEA + WD; break;
                                case 2: lo = spec.CavernTopY - WC; hi = sea - Ores.GOLD_TOP_BELOW_SEA + WD; break;
                                case 3: lo = spec.UnderworldTopY - WU; hi = spec.CavernTopY + WC; break;
                                default: lo = int.MinValue; hi = spec.UnderworldTopY + WU; break;
                            }
                            if (y < lo || y > hi) stats[S_BAND]++;
                        }
                    }
                    if (dense) stats[S_DENSE]++;
                }
                stats[S_HASH] = hash;
            }
        }

        /// The placement alone over a grid, every layer and biome: integer only,
        /// so Burst and Mono must agree bit for bit.
        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
        private struct GridJob : IJob
        {
            public TerrainSpec spec;
            public int3 origin;
            public NativeArray<long> result;   // [0] hash, [1] ore voxels

            public void Execute()
            {
                uint hash = 2166136261u; long n = 0;
                for (int layer = 0; layer < Ores.LAYERS; layer++)
                for (byte biome = 0; biome < GenBiome.Count; biome++)
                for (int y = 0; y < 64; y++)
                for (int z = 0; z < 64; z++)
                for (int x = 0; x < 64; x++)
                {
                    byte m = Ores.At(in spec, 24, biome, layer, origin.x + x, origin.y + y - layer * 64, origin.z + z);
                    if (m == 0) continue;
                    n++;
                    hash = (hash ^ (uint)(((((layer * 6 + biome) * 64 + y) * 64 + z) * 64 + x) * 7 + m)) * 16777619u;
                }
                result[0] = hash; result[1] = n;
            }
        }

        private static TerrainSpec _spec;
        private static Offsets _o;
        private static WorldMap _map;
        private static byte[] _plane;
        private static List<(string name, int3 origin, bool ring)> _chunks;
        private static long _ringOff, _ringOn, _ringChunks;
        private static long[] _on, _off;
        private static long _hash1, _hash2;
        private static readonly List<(string name, long denseOff, long denseOn, long ore)> _perChunk = new List<(string, long, long, long)>();

        private static void EnsureMap()
        {
            if (_map != null) return;
            _spec = TerrainSpec.SizeClass2(SEED);   // map v4
            _spec.brickGeologyDepth = BRICK_GEOLOGY_DEPTH;
            _o = Offsets.For(in _spec);
            var mapSpec = TerrainSpec.SizeClass2(SEED);
            _map = SharedMaps.Get(mapSpec);
            Assert.IsNotNull(_map.Record, "map v4 has no world record");
            _plane = OreTable.DensityPlane(_map.Record, _spec.mapCells);
        }

        private static long[] Run(int3 origin, bool ores)
        {
            using var hA = new NativeArray<float>(_map.Height, Allocator.TempJob);
            using var rA = new NativeArray<float>(_map.Rough, Allocator.TempJob);
            using var wA = new NativeArray<float>(_map.Water, Allocator.TempJob);
            using var cA = new NativeArray<byte>(_map.WaterClass, Allocator.TempJob);
            using var pA = new NativeArray<byte>(_plane, Allocator.TempJob);
            using var colS = new NativeArray<ColumnSample>(CHUNK * CHUNK, Allocator.TempJob);
            using var colStep = new NativeArray<float>(CHUNK * CHUNK, Allocator.TempJob);
            using var colCd = new NativeArray<DetailField.ColumnDetail>(CHUNK * CHUNK, Allocator.TempJob);
            using var stats = new NativeArray<long>(S_LEN, Allocator.TempJob);
            new OreJob { h = hA, r = rA, w = wA, cls = cA, plane = pA, spec = _spec, o = _o, origin = origin, ores = ores,
                         colS = colS, colStep = colStep, colCd = colCd, stats = stats }.Run();
            return stats.ToArray();
        }

        /// Four land sites (one per quadrant, 30-75% of the island radius out,
        /// >= 10 m above sea). Per site the RING set -- what V0's ring projection
        /// holds per column with the camera on the surface: the chunk holding the
        /// top and the 7 below it -- and the DEEP set, one chunk in each lower
        /// layer: deep stone (iron), deeper deep stone (gold), cavern rock, the
        /// underworld rock over the chamber.
        private static void EnsureChunks()
        {
            EnsureMap();
            if (_chunks != null) return;
            _chunks = new List<(string, int3, bool)>();
            float c = _spec.worldSizeVoxels * 0.5f;
            var view = _map.View;
            float2[] dirs = { new float2(0.8f, 0.6f), new float2(-0.6f, 0.8f), new float2(-0.8f, -0.6f), new float2(0.6f, -0.8f) };
            int sea = _spec.seaLevelY;
            for (int d = 0; d < dirs.Length; d++)
            for (float f = 0.30f; f < 0.80f; f += 0.05f)
            {
                float2 p = new float2(c, c) + dirs[d] * (_spec.islandRadius * f);
                var col = ShapeField.Sample(in _spec, in view, in _o, p.x, p.y, 1);
                if (col.height < sea + 100 || col.water > col.height) continue;
                int x = (int)p.x & ~(CHUNK - 1), z = (int)p.y & ~(CHUNK - 1), top = col.height & ~(CHUNK - 1);
                for (int k = 0; k < 8; k++) _chunks.Add(($"site{d} ring {(k == 0 ? "top" : "-" + k)}", new int3(x, top - k * CHUNK, z), true));
                int[] ys = { sea - 750, sea - 1250, sea - 2500, _spec.UnderworldTopY - 200 };
                string[] names = { "deep (iron)", "deeper (gold)", "cavern", "underworld" };
                for (int k = 0; k < ys.Length; k++) _chunks.Add(($"site{d} {names[k]}", new int3(x, ys[k] & ~(CHUNK - 1), z), false));
                break;
            }
            Assert.GreaterOrEqual(_chunks.Count, 36, "fewer than 3 land sites found");
            _on = new long[S_LEN]; _off = new long[S_LEN];
            uint h1 = 2166136261u, h2 = 2166136261u;
            foreach (var (name, origin, ring) in _chunks)
            {
                var on = Run(origin, true);
                var on2 = Run(origin, true);
                var off = Run(origin, false);
                for (int i = 0; i < S_LEN; i++) { if (i == S_HASH) continue; _on[i] += on[i]; _off[i] += off[i]; }
                if (ring) { _ringOff += off[S_DENSE]; _ringOn += on[S_DENSE]; _ringChunks++; }
                h1 = (h1 ^ (uint)on[S_HASH]) * 16777619u;
                h2 = (h2 ^ (uint)on2[S_HASH]) * 16777619u;
                _perChunk.Add((name, off[S_DENSE], on[S_DENSE], on[S_ORE]));
            }
            _hash1 = h1; _hash2 = h2;
        }

        /// Mixed-brick rise on the ring set: (on - off) / off.
        private static double RingRise => (_ringOn - _ringOff) / (double)math.max(1, _ringOff);

        // ------------------------------------------------------------------

        [Test]
        public void Placement_BurstMatchesMono_AndRepeats()
        {
            var spec = TerrainSpec.SizeClass2(SEED);
            var origin = new int3(40000, 4500, 40000);
            using var r1 = new NativeArray<long>(2, Allocator.TempJob);
            using var r2 = new NativeArray<long>(2, Allocator.TempJob);
            using var r3 = new NativeArray<long>(2, Allocator.TempJob);
            new GridJob { spec = spec, origin = origin, result = r1 }.Run();          // Burst (when enabled)
            new GridJob { spec = spec, origin = origin, result = r2 }.Run();
            new GridJob { spec = spec, origin = origin, result = r3 }.Execute();      // managed (Mono)
            Assert.Greater(r1[1], 0, "no ore in the grid at all");
            Assert.AreEqual(r1[0], r2[0], "two Burst runs disagree");
            Assert.AreEqual(r1[1], r3[1], "Burst and Mono place a different number of ore voxels");
            Assert.AreEqual(r1[0], r3[0], "Burst and Mono place ore differently");

            var other = spec; other.seed = SEED + 1;
            using var r4 = new NativeArray<long>(2, Allocator.TempJob);
            new GridJob { spec = other, origin = origin, result = r4 }.Run();
            Assert.AreNotEqual(r1[0], r4[0], "another seed places the same ore");
        }

        [Test]
        public void MapV3_HasNoOre()
        {
            var v3 = TerrainSpec.SizeClass2(SEED, 3);
            var v4 = TerrainSpec.SizeClass2(SEED);
            var o = Offsets.For(in v3);
            Assert.AreEqual(0, DetailField.PrepareColumn(in v3, in o, 40000, 40000).oreMul);
            var o4 = Offsets.For(in v4);
            Assert.AreEqual(Ores.DEFAULT_MUL, DetailField.PrepareColumn(in v4, in o4, 40000, 40000).oreMul);
            Assert.IsTrue(System.Array.TrueForAll(OreTable.DensityPlane(null, 16), b => b == 0));
        }

        [Test]
        public void CellChance_MatchesTable_PerPair_Within25Percent()
        {
            // For every landform x biome entry of the table and every layer: the
            // share of 32^3 cells holding a blob, over random cells, is the
            // table's chance within +-25% (expected count >= 400 per entry).
            var spec = TerrainSpec.SizeClass2(SEED);
            uint rng = 12345u;
            int checkedPairs = 0;
            for (int row = 0; row < OreTable.ROWS; row++)
            for (byte biome = 0; biome < GenBiome.Count; biome++)
            {
                byte arch = row == 10 ? WorldRecord.SEA : (byte)row;
                int mul = OreTable.PairMul(arch, biome);
                for (int layer = 0; layer < Ores.LAYERS; layer++)
                {
                    int chance = Ores.CellChance(layer, biome, mul);
                    Assert.Greater(chance, 0);
                    int n = (int)math.max(2000L, 400L * 65536 / chance);
                    int hits = 0;
                    for (int s = 0; s < n; s++)
                    {
                        rng = Ores.Mix(rng + 0x9E3779B9u);
                        int cx = (int)(rng & 4095u), cy = (int)((rng >> 12) & 255u), cz = (int)((rng >> 20) & 4095u);
                        if ((int)(Ores.Hash(spec.seed, cx, cy, cz) & 0xFFFFu) < chance) hits++;
                    }
                    double want = math.min(1.0, chance / 65536.0), got = hits / (double)n;
                    Assert.That(got, Is.InRange(want * 0.75, want * 1.25),
                        $"arch {row} biome {biome} layer {layer}: blob share {got:P2}, table {want:P2}");
                    checkedPairs++;
                }
            }
            Assert.AreEqual(OreTable.ROWS * GenBiome.Count * Ores.LAYERS, checkedPairs);
        }

        [Test, Category("Slow")]
        public void RealChunks_OreInsideRock_InItsBand_Deterministic_UnderHalfPercent()
        {
            EnsureChunks();
            Assert.Greater(_on[S_ORE], 0, "no ore in any sampled chunk");
            Assert.AreEqual(0, _off[S_ORE], "ores off still placed ore");
            Assert.AreEqual(_hash1, _hash2, "the same seed placed different ore");
            Assert.AreEqual(0, _on[S_NOT_ROCK], "ore replaced something that is not layer rock");
            Assert.AreEqual(0, _on[S_BAND], "ore outside its layer's depth band");
            Assert.AreEqual(0, _on[S_SHALLOW], "ore within 6 m of the column top");
            double share = _on[S_ORE] / (double)_on[S_ROCK];
            Assert.Less(share, 0.005, $"ore is {share:P3} of rock");
            // Every layer got some ore in the sample.
            for (int l = 0; l < Ores.LAYERS; l++)
            {
                long n = 0;
                for (int b = 0; b < 6; b++) n += _on[S_ORE_LB + l * 6 + b];
                Assert.Greater(n, 0, $"no {Ores.OreOf(l)} ore in the sample (layer {l})");
            }
            // Rock count unchanged by ore (ore only replaces rock).
            Assert.AreEqual(_off[S_ROCK], _on[S_ROCK], "ore changed how much rock there is");
            // Memory (plan risk "Ores and memory"): mixed bricks on the ring-shaped
            // set rise by at most 5%.
            Assert.LessOrEqual(RingRise, 0.05, $"mixed bricks on the ring set rise {RingRise:P1} ({_ringOff} -> {_ringOn})");
        }

        [Test, Category("Slow")]
        public void Report_OreCountsAndMixedBricks()
        {
            ReportOnly.Require();
            EnsureChunks();
            string[] ln = { "copper (stone)", "iron (deep)", "gold (deeper)", "cobalt (cavern)", "ember (underworld)" };
            string[] bn = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
            var lines = new List<string>
            {
                "Ores (gameplay step 3), seed 42, map v4, brick geology 200, Burst",
                $"{_chunks.Count} chunks of 128^3, 4 land sites: the RING set (top chunk + 7 below) and the DEEP set (sea-75 m, sea-125 m, sea-250 m, underworld top-20 m)",
                "",
                $"ore voxels / layer-rock voxels: {_on[S_ORE]:N0} / {_on[S_ROCK]:N0} = {100.0 * _on[S_ORE] / _on[S_ROCK]:F3}%",
                "",
                "ORE VOXELS per layer x column biome (rock voxels of that layer x biome in brackets):",
                $"{"layer",-20}" + string.Concat(System.Array.ConvertAll(bn, b => $"{b,22}")),
            };
            for (int l = 0; l < Ores.LAYERS; l++)
            {
                string row = $"{ln[l],-20}";
                for (int b = 0; b < 6; b++)
                {
                    long ore = _on[S_ORE_LB + l * 6 + b], rock = _on[S_ROCK_LB + l * 6 + b];
                    row += rock == 0 ? $"{"-",22}" : $"{$"{ore:N0} ({100.0 * ore / rock:F2}%)",22}";
                }
                lines.Add(row);
            }
            lines.Add("");
            lines.Add("ORE SHARE per pair multiplier (column's OreTable entry, 16 = x1): ore / rock");
            for (int m = 0; m < 64; m++)
            {
                long rock = _on[S_ROCK_PAIR + m];
                if (rock == 0) continue;
                lines.Add($"  x{m / 16.0:F2}  {_on[S_ORE_PAIR + m],10:N0} / {rock,14:N0} = {100.0 * _on[S_ORE_PAIR + m] / rock:F3}%");
            }
            lines.Add("");
            lines.Add($"MIXED BRICKS, RING SET ({_ringChunks} chunks: per site the top chunk + 7 below, V0's ring projection):");
            lines.Add($"  {_ringOff:N0} -> {_ringOn:N0}  (+{100.0 * RingRise:F1}%; +{(_ringOn - _ringOff) / (double)math.max(1, _ringChunks):F1} bricks = " +
                      $"{(_ringOn - _ringOff) * 512.0 / math.max(1, _ringChunks) / 1024:F1} KB per chunk)");
            lines.Add($"  projected on V0's 16-layer ring (32 x 32 columns x 8 chunks): +{(_ringOn - _ringOff) / (double)math.max(1, _ringChunks) * 8192 * 512 / 1048576:F0} MB");
            long denseOff = _off[S_DENSE], denseOn = _on[S_DENSE], bricks = (long)_chunks.Count * 4096;
            lines.Add($"MIXED BRICKS, ALL {_chunks.Count} chunks: {denseOff:N0} -> {denseOn:N0} of {bricks:N0} " +
                      $"({100.0 * denseOff / bricks:F2}% -> {100.0 * denseOn / bricks:F2}% of all bricks; " +
                      $"+{100.0 * (denseOn - denseOff) / math.max(1, denseOff):F1}% relative)");
            lines.Add("  per chunk (mixed off -> on, ore voxels):");
            foreach (var (name, a, b, ore) in _perChunk) lines.Add($"    {name,-26}{a,6} -> {b,6}   {ore,8:N0}");
            string dir = Path.Combine("Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "ore_report.txt"), lines);
            UnityEngine.Debug.Log(string.Join("\n", lines));
        }
    }
}
