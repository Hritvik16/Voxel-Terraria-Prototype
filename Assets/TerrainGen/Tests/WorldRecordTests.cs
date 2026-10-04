// Assets/TerrainGen/Tests/WorldRecordTests.cs
//
// Surface rebuild B1 (owner 2026-10-03): map v4 = v3 + the world record +
// larger biomes, OPT-IN.
//   v3 untouched    Biome at scale 1 is the pre-B1 formula bit for bit; a v3
//                   spec ignores biomeScale; a v3 map has no record.
//   v4 = v3 + B1    every map array of a v4 build equalled the v3 build's
//                   until B2 (landform by flatness) changed v4's regions; now:
//                   two v4 builds agree array for array, and differ from v3.
//   Record          the size budget (<= 7 MB), what each field holds, that it
//                   rebuilds to the same hash and differs across seeds, and its
//                   cost (B1: map build <= v3 + 2 s; the record is the whole
//                   difference).
//   Larger biomes   biome change walking east-west >= 2.5x v3 (owner Q5) on all
//                   four seeds, computed on the shared v3 maps (the biome field
//                   does not read the map; v4's coast is v3's).
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class WorldRecordTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };

        /// ShapeField.Biome as it was before B1, verbatim.
        private static byte BiomePreB1(in Offsets o, float2 p)
        {
            float t = Noise.Fbm(p / 15000f + o.tempA, 3) * 2.4f;
            float m = Noise.Fbm(p / 12000f + o.moistA, 3) * 2.4f;
            float df = t * t + m * m;
            float dd = (t - 0.8f) * (t - 0.8f) + (m + 0.8f) * (m + 0.8f);
            float ds = (t + 0.9f) * (t + 0.9f) + (m - 0.1f) * (m - 0.1f);
            float dj = (t - 0.8f) * (t - 0.8f) + (m - 0.8f) * (m - 0.8f);
            byte b = GenBiome.Forest; float best = df;
            if (dd < best) { best = dd; b = GenBiome.Desert; }
            if (ds < best) { best = ds; b = GenBiome.Snow; }
            if (dj < best) { b = GenBiome.Jungle; }
            return b;
        }

        [Test]
        public void V3Biome_IsThePreB1Formula_AndIgnoresBiomeScale()
        {
            foreach (uint seed in Seeds)
            {
                var v3 = TerrainSpec.SizeClass2(seed);
                v3.biomeScale = 5f;                                   // must be ignored below v4
                Assert.AreEqual(1f, v3.BiomeScale);
                var oSeed = Offsets.For(seed);
                var oSpec = Offsets.For(in v3);
                var rng = new Random(seed + 1u);
                int diff = 0;
                for (int k = 0; k < 20000; k++)
                {
                    float2 p = rng.NextFloat2(0f, v3.worldSizeVoxels);
                    byte want = BiomePreB1(in oSeed, p);
                    if (ShapeField.Biome(in oSeed, p) != want || ShapeField.Biome(in oSpec, p) != want) diff++;
                }
                Assert.AreEqual(0, diff, $"seed {seed}: v3 biomes changed");
            }
            Assert.AreEqual(3f, TerrainSpec.SizeClass2(1u, 4).BiomeScale, "v4 default is x3");
            Assert.AreEqual(3, TerrainSpec.SizeClass2(1u, 0).mapVersion, "mapVersion 0 keeps the default (v3)");
        }

        [Test]
        [Category("Slow")]   // two v4 builds + the shared v3 one
        public void V4_Deterministic_SeedDependent_DiffersFromV3()
        {
            var v3 = SharedMaps.Get(TerrainSpec.SizeClass2(42u));
            Assert.IsNull(v3.Record, "a v3 map has no world record");
            var spec4 = TerrainSpec.SizeClass2(42u, 4);
            var a = SharedMaps.Get(spec4);
            Assert.IsNotNull(a.Record);
            int fromV3 = 0;
            for (int i = 0; i < v3.Height.Length; i++)
                if (v3.Height[i] != a.Height[i] || v3.Region[i] != a.Region[i]) fromV3++;
            Assert.Greater(fromV3, 0, "B2: v4's regions are dealt by landform class, so its map must differ from v3's");

            using var b = WorldMap.Build(spec4);
            int diff = 0;
            for (int i = 0; i < a.Height.Length; i++)
                if (a.Height[i] != b.Height[i] || a.Rough[i] != b.Rough[i] || a.Water[i] != b.Water[i] ||
                    a.Flow[i] != b.Flow[i] || a.WaterClass[i] != b.WaterClass[i] || a.Region[i] != b.Region[i]) diff++;
            Assert.AreEqual(0, diff, "two v4 builds of one seed differ");
            ulong ha = a.Record.Hash(), hb = b.Record.Hash();
            Assert.AreEqual(ha, hb, "two v4 builds of one seed must give the same record");

            var c = SharedMaps.Get(TerrainSpec.SizeClass2(7u, 4));
            Assert.AreNotEqual(ha, c.Record.Hash(), "seeds 42 and 7 gave the same record");

            // B1: the map may take 2 s over v3. The biome plane runs beside
            // BuildV3 (WorldMap.BuildV4); the rest is on the critical path.
            var plane = WorldRecord.BiomePlane(spec4);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var again = WorldRecord.Build(a, plane);
            double ms = sw.Elapsed.TotalMilliseconds;
            Assert.AreEqual(ha, again.Hash());
            Assert.Less(ms, 2000.0, $"record build after the map {ms:F0} ms: B1 allows the map 2 s over v3");
            UnityEngine.Debug.Log($"[WorldRecord] seed 42 hash {ha:X16}, seed 7 {c.Record.Hash():X16}, {a.Record.Bytes} bytes, built in {ms:F0} ms");
        }

        [Test]
        [Category("Slow")]
        public void V4Record_HoldsWhatTheMapSays()
        {
            var spec = TerrainSpec.SizeClass2(42u, 4);
            var map = SharedMaps.Get(spec);
            var rec = map.Record;
            int n = spec.mapCells;
            Assert.LessOrEqual(rec.Bytes, 7L * 1000 * 1000, "B1 budget: record <= 7 MB");
            Assert.AreEqual((long)n * n * WorldRecord.BYTES_PER_CELL, rec.Bytes);
            Assert.AreEqual(WorldRecord.BYTES_PER_CELL, System.Runtime.InteropServices.Marshal.SizeOf<RecordCell>());

            var o = Offsets.For(in spec);
            int biomeBad = 0, archBad = 0, waterBad = 0, reserved = 0, depthBad = 0, deep = 0;
            var depthSeen = new HashSet<int>();
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                var c = rec.Data[i];
                if ((z & 7) == 0 && (x & 7) == 0 &&
                    c.Biome != ShapeField.Biome(in o, new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels))) biomeBad++;
                byte want = map.Region[i] == 255 ? WorldRecord.SEA : map.Region[i];
                if (c.Archetype != want) archBad++;
                bool fresh = map.WaterClass[i] == Hydrology.LAKE || map.WaterClass[i] == Hydrology.RIVER;
                if (fresh && c.freshWater != 0) waterBad++;
                if (c.hazard != 0 || c.flags != 0 || c.site != 0 || (c.biome >> 4) != 0) reserved++;
                bool border = (x > 0 && rec.Data[i - 1].Archetype != c.Archetype) || (x < n - 1 && rec.Data[i + 1].Archetype != c.Archetype)
                           || (z > 0 && rec.Data[i - n].Archetype != c.Archetype) || (z < n - 1 && rec.Data[i + n].Archetype != c.Archetype);
                if (border && c.Depth != 0) depthBad++;
                if (!c.IsSea) { depthSeen.Add(c.Depth); if (c.Depth == 15) deep++; }
            }
            Assert.AreEqual(0, biomeBad, "record biome != ShapeField.Biome at the cell centre");
            Assert.AreEqual(0, archBad, "record archetype != map.Region");
            Assert.AreEqual(0, waterBad, "lake/river cells must read 0 m to fresh water");
            Assert.AreEqual(0, reserved, "fields reserved for B4-B8 must be zero");
            Assert.AreEqual(0, depthBad, "a cell on an archetype border must have depth 0");
            Assert.GreaterOrEqual(depthSeen.Count, 12, "land depths should span the 0-15 range");

            // spot values: the point query is the cell, and dry land a long way
            // from water says so
            Assert.AreEqual(rec.Data[300 * n + 400].landform, rec.At(400 * spec.mapCellVoxels + 79, 300 * spec.mapCellVoxels).landform);
            int far = 0; foreach (var c in rec.Data) if (!c.IsSea && c.FreshWaterM >= 500) far++;
            UnityEngine.Debug.Log($"[WorldRecord] land depth 15 (>= 480 m in): {deep} cells; land >= 500 m from fresh water: {far} cells; depths seen {depthSeen.Count}");
        }

        [Test]
        [Category("Slow")]   // the four shared v3 maps
        public void V4Biomes_ChangeAtLeast2_5xLessOftenWalkingEastWest()
        {
            var lines = new List<string>();
            foreach (uint seed in Seeds)
            {
                var v3 = TerrainSpec.SizeClass2(seed);
                var map = SharedMaps.Get(v3);   // v4's land is this, cell for cell
                float m3 = ChangeM(map, Offsets.For(in v3));
                float m4 = ChangeM(map, Offsets.For(TerrainSpec.SizeClass2(seed, 4)));
                lines.Add($"seed {seed}: v3 {m3:F0} m, v4 {m4:F0} m, x{m4 / m3:F2}");
                Assert.GreaterOrEqual(m4, 2.5f * m3, $"seed {seed}: v4 biome change {m4:F0} m < 2.5 x v3's {m3:F0} m");
            }
            UnityEngine.Debug.Log("[WorldRecord] biome change E-W\n" + string.Join("\n", lines));
        }

        /// PlayMetricsReport's metric: land metres walked east-west per biome
        /// change, rows every 32 m, 8 m steps.
        private static float ChangeM(WorldMap map, Offsets o)
        {
            var spec = map.Spec; int n = spec.mapCells;
            var row = new byte[n];
            long walked = 0; int changes = 0;
            for (int z = 0; z < n; z += 4)
            {
                for (int x = 0; x < n; x++)
                    row[x] = ShapeField.Biome(in o, new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels));
                for (int x = 0; x + 1 < n; x++)
                {
                    int a = z * n + x;
                    if (map.Height[a] <= 0f || map.Height[a + 1] <= 0f) continue;
                    walked++;
                    if (row[x] != row[x + 1]) changes++;
                }
            }
            return changes > 0 ? walked * (spec.mapCellVoxels / 10f) / changes : float.PositiveInfinity;
        }
    }
}
