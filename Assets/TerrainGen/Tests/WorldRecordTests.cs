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
//   Six biomes      B10 (owner Q10 (a)): savanna and badlands on v4. B1's numbers
//                   for the new set -- change walking E-W >= 900 m, area-weighted
//                   median patch >= 50 ha -- each new biome on >= 3 of 4 seeds,
//                   v4 on biomeSet 4 is B8's field exactly. Report + B8|B10 PNGs.
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var v3 = TerrainSpec.SizeClass2(seed, 3);
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
            var v4 = TerrainSpec.SizeClass2(1u, 4);
            Assert.AreEqual(6, v4.BiomeSet, "v4 default is B10's six biomes");
            Assert.AreEqual(3.25f, v4.BiomeScale, "v4's six-biome default is x3.25");
            v4.biomeSet = 4;
            Assert.AreEqual(3f, v4.BiomeScale, "v4 on B1-B8's four biomes stays x3");
            var v3b = TerrainSpec.SizeClass2(1u, 3); v3b.biomeSet = 6;
            Assert.AreEqual(4, v3b.BiomeSet, "biomeSet is ignored below v4");
            Assert.AreEqual(4, TerrainSpec.SizeClass2(1u, 0).mapVersion, "mapVersion 0 keeps the default (v4 since owner Q35 6a, 2026-10-09)");
            Assert.AreEqual(4, TerrainSpec.SizeClass2(1u).mapVersion, "the default world is map v4");
        }

        [Test]
        [Category("Slow")]   // two v4 builds + the shared v3 one
        public void V4_Deterministic_SeedDependent_DiffersFromV3()
        {
            var v3 = SharedMaps.Get(TerrainSpec.SizeClass2(42u, 3));
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
            // rebuilt as WorldMap builds it: after the routes, the easy areas the
            // routes were found on are kept (a fresh growth on the eased map can
            // differ by a few cells -- 2 on seed 42 after B5b; WorldMap never does that)
            var again = WorldRecord.Build(a, plane, a.Routes != null ? a.Record : null);
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
                // B5: FLAG_FEATURE and site (the feature's index + 1) go together
                bool siteOk = c.IsFeature ? c.site >= 1 && c.site <= rec.FeatureList.Length : c.site == 0;
                if ((c.hazard >> 5) != 0 || (c.flags & ~(WorldRecord.FLAG_EASY | WorldRecord.FLAG_FEATURE | WorldRecord.FLAG_ROUTE)) != 0 || !siteOk || (c.biome >> 4) != 0) reserved++;
                bool border = (x > 0 && rec.Data[i - 1].Archetype != c.Archetype) || (x < n - 1 && rec.Data[i + 1].Archetype != c.Archetype)
                           || (z > 0 && rec.Data[i - n].Archetype != c.Archetype) || (z < n - 1 && rec.Data[i + n].Archetype != c.Archetype);
                if (border && c.Depth != 0) depthBad++;
                if (!c.IsSea) { depthSeen.Add(c.Depth); if (c.Depth == 15) deep++; }
            }
            Assert.AreEqual(0, biomeBad, "record biome != ShapeField.Biome at the cell centre");
            Assert.AreEqual(0, archBad, "record archetype != map.Region");
            Assert.AreEqual(0, waterBad, "lake/river cells must read 0 m to fresh water");
            Assert.AreEqual(0, reserved, "reserved fields must be zero (B4's easy flag, B5's feature flag + site, B6's hazard bits 0-4 and B8's route flag aside)");
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
                var v3 = TerrainSpec.SizeClass2(seed, 3);
                var map = SharedMaps.Get(v3);   // v4's land is this, cell for cell
                float m3 = ChangeM(map, Offsets.For(in v3));
                float m4 = ChangeM(map, Offsets.For(TerrainSpec.SizeClass2(seed, 4)));
                lines.Add($"seed {seed}: v3 {m3:F0} m, v4 {m4:F0} m, x{m4 / m3:F2}");
                Assert.GreaterOrEqual(m4, 2.5f * m3, $"seed {seed}: v4 biome change {m4:F0} m < 2.5 x v3's {m3:F0} m");
            }
            UnityEngine.Debug.Log("[WorldRecord] biome change E-W\n" + string.Join("\n", lines));
        }

        [Test]
        [Category("Slow")]   // the four shared v4 maps
        public void V4SixBiomes_B10Gates()
        {
            const float PRESENT = 0.01f;   // a biome is present on a seed with >= 1% of its land
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== B10: map v4 with six biomes (biomeSet 6, x3.25, one-octave climate) against B8's four (biomeSet 4, x3)");
            report.AppendLine("Land = map height > 0. Change = land metres walked east-west per biome change (rows every 32 m). Patch = 4-connected land of one biome; median is area-weighted (B1's reading).");
            report.AppendLine("  seed    change m B8 -> B10   area-wtd median ha B8 -> B10   shares of land (forest desert snow jungle savanna badlands)   biome plane ms B8 / B10   map build s v3 / v4");
            var fails = new System.Text.StringBuilder();
            int savSeeds = 0, badSeeds = 0;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "playmetrics");
            Directory.CreateDirectory(dir);
            foreach (uint seed in Seeds)
            {
                var spec6 = TerrainSpec.SizeClass2(seed, 4);
                var e4 = SharedMaps.GetEntry(spec6);
                var map = e4.Map;
                var spec4 = spec6; spec4.biomeSet = 4;
                int n = spec6.mapCells;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                byte[] b8 = WorldRecord.BiomePlane(spec4);
                double ms8 = sw.Elapsed.TotalMilliseconds; sw.Restart();
                byte[] b10 = WorldRecord.BiomePlane(spec6);
                double ms10 = sw.Elapsed.TotalMilliseconds;

                // the record carries the B10 field; biomeSet 4 is B8's formula
                int recBad = 0, b8Bad = 0;
                var o4 = Offsets.For(in spec4);
                for (int i = 0; i < n * n; i++)
                {
                    if (map.Record.Data[i].Biome != b10[i]) recBad++;
                    if ((i & 63) == 0 && b8[i] != BiomeFour(in o4, new float2(((i % n) + 0.5f) * spec6.mapCellVoxels, ((i / n) + 0.5f) * spec6.mapCellVoxels))) b8Bad++;
                }
                if (recBad > 0) fails.AppendLine($"seed {seed}: {recBad} record cells differ from the B10 biome plane");
                if (b8Bad > 0) fails.AppendLine($"seed {seed}: biomeSet 4 differs from B8's four-biome field at {b8Bad} cells");

                var isLand = new bool[n * n]; long land = 0;
                for (int i = 0; i < n * n; i++) { isLand[i] = map.Height[i] > 0f; if (isLand[i]) land++; }
                float c8 = Change(isLand, b8, n, spec6.mapCellVoxels / 10f), c10 = Change(isLand, b10, n, spec6.mapCellVoxels / 10f);
                float p8 = AreaMedianHa(isLand, b8, n, spec6.mapCellVoxels / 10f), p10 = AreaMedianHa(isLand, b10, n, spec6.mapCellVoxels / 10f);
                var share = new double[GenBiome.Count];
                for (int i = 0; i < n * n; i++) if (isLand[i]) share[b10[i]]++;
                for (int k = 0; k < share.Length; k++) share[k] /= land;
                if (share[GenBiome.Savanna] >= PRESENT) savSeeds++;
                if (share[GenBiome.Badlands] >= PRESENT) badSeeds++;
                double build3 = SharedMaps.GetEntry(TerrainSpec.SizeClass2(seed, 3)).BuildMs / 1000.0, build4 = e4.BuildMs / 1000.0;

                report.AppendLine($"  {seed,-6}  {c8,6:F0} -> {c10,6:F0}          {p8,6:F0} -> {p10,6:F0}                 " +
                                  string.Join(" ", share.Select(v => $"{100 * v,4:F1}%")) + $"                {ms8,5:F0} / {ms10,5:F0}             {build3:F1} / {build4:F1}");
                if (c10 < 900f) fails.AppendLine($"seed {seed}: biome change walking east-west {c10:F0} m < 900 m");
                if (p10 < 50f) fails.AppendLine($"seed {seed}: area-weighted median biome patch {p10:F0} ha < 50 ha");
                if (map.Record.Bytes > 7L * 1000 * 1000) fails.AppendLine($"seed {seed}: record {map.Record.Bytes} B > 7 MB");

                WriteB8B10(Path.Combine(dir, $"seed{seed}_b8_b10_biome.png"), n, isLand, map.WaterClass, b8, b10);
            }
            report.AppendLine($"savanna present (>= {100 * PRESENT:F0}% of land) on {savSeeds} of 4 seeds, badlands on {badSeeds} of 4 (want >= 3 each)");
            if (savSeeds < 3) fails.AppendLine($"savanna present on only {savSeeds} of 4 seeds");
            if (badSeeds < 3) fails.AppendLine($"badlands present on only {badSeeds} of 4 seeds");
            File.WriteAllText(Path.Combine(dir, "b10_biomes.txt"), report.ToString() + (fails.Length > 0 ? "FAIL\n" + fails : "PASS\n"));
            UnityEngine.Debug.Log("[WorldRecord] B10\n" + report);
            Assert.AreEqual("", fails.ToString(), fails.ToString());
        }

        /// ShapeField.Biome's four-biome formula at a given scale (B1-B8).
        private static byte BiomeFour(in Offsets o, float2 p)
        {
            float s = o.biomeScale;
            float t = Noise.Fbm(p / (15000f * s) + o.tempA, 3) * 2.4f;
            float m = Noise.Fbm(p / (12000f * s) + o.moistA, 3) * 2.4f;
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

        private static float Change(bool[] isLand, byte[] biome, int n, float cellM)
        {
            long walked = 0; int changes = 0;
            for (int z = 0; z < n; z += 4)
            for (int x = 0; x + 1 < n; x++)
            {
                int a = z * n + x;
                if (!isLand[a] || !isLand[a + 1]) continue;
                walked++;
                if (biome[a] != biome[a + 1]) changes++;
            }
            return changes > 0 ? walked * cellM / changes : float.PositiveInfinity;
        }

        /// Area-weighted median of 4-connected same-biome land patches, in ha.
        private static float AreaMedianHa(bool[] isLand, byte[] biome, int n, float cellM)
        {
            var seen = new bool[n * n]; var sizes = new List<int>(); var stack = new Stack<int>();
            for (int s0 = 0; s0 < n * n; s0++)
            {
                if (!isLand[s0] || seen[s0]) continue;
                int count = 0; seen[s0] = true; stack.Push(s0);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(); count++;
                    int x = i % n, z = i / n;
                    void Try(int j) { if (isLand[j] && !seen[j] && biome[j] == biome[i]) { seen[j] = true; stack.Push(j); } }
                    if (x > 0) Try(i - 1); if (x < n - 1) Try(i + 1); if (z > 0) Try(i - n); if (z < n - 1) Try(i + n);
                }
                sizes.Add(count);
            }
            sizes.Sort();
            double total = sizes.Sum(v => (double)v), acc = 0, ha = cellM * cellM / 10000.0;
            foreach (int v in sizes) { acc += v; if (acc >= total * 0.5) return (float)(v * ha); }
            return 0f;
        }

        private static readonly UnityEngine.Color[] B10Col =
        {
            new UnityEngine.Color(0.25f, 0.55f, 0.25f), new UnityEngine.Color(0.92f, 0.80f, 0.50f), new UnityEngine.Color(0.92f, 0.94f, 0.98f),
            new UnityEngine.Color(0.10f, 0.45f, 0.30f), new UnityEngine.Color(0.79f, 0.65f, 0.25f), new UnityEngine.Color(0.76f, 0.34f, 0.23f),
        };

        /// Left B8 (four biomes), right B10 (six), 512 px each (16 m / px); sea
        /// dark blue, lakes and rivers light blue.
        private static void WriteB8B10(string path, int n, bool[] isLand, byte[] waterClass, byte[] b8, byte[] b10)
        {
            const int P = 512, GAP = 8; int k = n / P;
            var tex = new UnityEngine.Texture2D(2 * P + GAP, P, UnityEngine.TextureFormat.RGB24, false);
            var px = new UnityEngine.Color32[(2 * P + GAP) * P];
            for (int i = 0; i < px.Length; i++) px[i] = new UnityEngine.Color32(255, 255, 255, 255);
            for (int panel = 0; panel < 2; panel++)
            for (int y = 0; y < P; y++)
            for (int x = 0; x < P; x++)
            {
                int i = (y * k) * n + x * k;
                UnityEngine.Color c = !isLand[i] ? new UnityEngine.Color(0.10f, 0.20f, 0.35f)
                    : (waterClass[i] == Hydrology.LAKE || waterClass[i] == Hydrology.RIVER) ? new UnityEngine.Color(0.45f, 0.65f, 0.90f)
                    : B10Col[(panel == 0 ? b8 : b10)[i]];
                px[y * (2 * P + GAP) + panel * (P + GAP) + x] = c;
            }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.DestroyImmediate(tex);
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
