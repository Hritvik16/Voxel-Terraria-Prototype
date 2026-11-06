// Assets/TerrainGen/Tests/BiomeLayoutTests.cs
//
// Task 176 (C1, WORLD_GEN_LOOK_PLAN.md; owner 2026-11-05 Q2 (a) "vary climate"):
// the default v4 biome layout, measured on the record's biome plane of 8 seeds
// (never a look at seed 42). Land = map height > 0 (lakes and rivers count).
//   straight   no land biome border runs straight: the longest straight line that
//              stays within STRAIGHT_TOL_M of a border along its whole length is
//              <= STRAIGHT_MAX_M (the plan's "about 1.5 km"; B10 had 1.7-2.9 km)
//   regions    every biome is in >= 2 separate 4-connected land regions of
//              >= REGION_MIN_HA (B10: 7 of 8 seeds had a biome in one region)
//   shares     every biome holds SHARE_MIN..SHARE_MAX of the land (six biomes,
//              the brief's Decided count; B10 ranged 1-53 %)
//   not confetti  biome change walking east-west >= CHANGE_MIN_M (above v3's
//              ~290 m) and the area-weighted median patch >= MEDIAN_MIN_HA
//   B10 still  biomeVaryOff = 1 is B10's one-octave field, bit for bit
// tools/biome176.py computes the same numbers from BiomeLayoutDump's planes.
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class BiomeLayoutTests
    {
        public const float STRAIGHT_TOL_M = 30f, STRAIGHT_MAX_M = 1700f;
        public const float REGION_MIN_HA = 25f;
        public const double SHARE_MIN = 0.05, SHARE_MAX = 0.35;
        public const float CHANGE_MIN_M = 300f, MEDIAN_MIN_HA = 35f;

        [Test]
        [Category("Slow")]   // 8 shared maps
        public void C1Layout_VariesAcrossTheIsland_OnEightSeeds()
        {
            var report = new StringBuilder();
            var fails = new StringBuilder();
            report.AppendLine($"=== Task 176 C1 biome layout on {BiomeLayoutDump.LayoutSeeds.Length} seeds (record plane, 8 m cells; land = map height > 0)");
            report.AppendLine($"  seed    shares % (F D S J Sv B)          regions >= {REGION_MIN_HA:F0} ha   straight m (tol {STRAIGHT_TOL_M:F0})   change m   median ha");
            foreach (uint seed in BiomeLayoutDump.LayoutSeeds)
            {
                var spec = TerrainSpec.SizeClass2(seed);
                Assert.IsTrue(spec.BiomeVary, "v4 default: the varied climate");
                var map = SharedMaps.Get(spec);
                int n = spec.mapCells; float cellM = spec.mapCellVoxels / 10f;
                var isLand = new bool[n * n]; var biome = new byte[n * n]; long land = 0;
                for (int i = 0; i < n * n; i++) { isLand[i] = map.Height[i] > 0f; biome[i] = map.Record.Data[i].Biome; if (isLand[i]) land++; }
                var share = new double[GenBiome.Count];
                for (int i = 0; i < n * n; i++) if (isLand[i]) share[biome[i]]++;
                var regions = Regions(isLand, biome, n, cellM);
                float straight = LongestStraightBorderM(isLand, biome, n, cellM, out _);
                float change = WorldRecordTests.Change(isLand, biome, n, cellM), median = WorldRecordTests.AreaMedianHa(isLand, biome, n, cellM);
                var sb = new StringBuilder();
                for (int b = 0; b < GenBiome.Count; b++)
                {
                    share[b] /= land;
                    sb.Append($"{100 * share[b],5:F1}");
                    if (share[b] < SHARE_MIN || share[b] > SHARE_MAX) fails.AppendLine($"seed {seed}: biome {b} holds {100 * share[b]:F1} % of the land");
                    if (regions[b] < 2) fails.AppendLine($"seed {seed}: biome {b} is in {regions[b]} region(s) of >= {REGION_MIN_HA} ha");
                }
                if (straight > STRAIGHT_MAX_M) fails.AppendLine($"seed {seed}: a border runs straight for {straight:F0} m");
                if (change < CHANGE_MIN_M) fails.AppendLine($"seed {seed}: biome change every {change:F0} m walking east-west");
                if (median < MEDIAN_MIN_HA) fails.AppendLine($"seed {seed}: area-weighted median patch {median:F0} ha");
                report.AppendLine($"  {seed,-6} {sb}   {string.Join(" ", regions)}              {straight,6:F0}                {change,5:F0}     {median,5:F0}");
            }
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "c1");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "c1_layout.txt"), report + (fails.Length > 0 ? "FAIL\n" + fails : "PASS\n"));
            UnityEngine.Debug.Log("[C1]\n" + report);
            Assert.AreEqual("", fails.ToString(), fails.ToString());
        }

        [Test]
        public void VaryOff_IsB10sField_AndTheFlagIsScoped()
        {
            var on = TerrainSpec.SizeClass2(42u);
            var off = on; off.biomeVaryOff = 1;
            Assert.IsTrue(on.BiomeVary);
            Assert.IsFalse(off.BiomeVary);
            Assert.IsFalse(TerrainSpec.SizeClass2(42u, 3).BiomeVary, "map v3 keeps its bytes");
            var s4 = on; s4.biomeSet = 4;
            Assert.IsFalse(s4.BiomeVary, "biomeSet 4 keeps B8's field");
            var oOff = Offsets.For(in off);
            Assert.AreEqual(0, oOff.biomeVary);
            var oOn = Offsets.For(in on);
            Assert.AreEqual(1, oOn.biomeVary);
            Assert.AreEqual(ShapeField.CLIMATE_WAVE_R * on.islandRadius, oOn.climWave.y, 1e-3f);
            var rng = new Unity.Mathematics.Random(0xc1u);
            int differ = 0;
            for (int i = 0; i < 4000; i++)
            {
                float2 p = rng.NextFloat2(0f, on.worldSizeVoxels);
                Assert.AreEqual(B10(in oOff, p), ShapeField.Biome(in oOff, p), $"biomeVaryOff 1 != B10's formula at {p}");
                if (ShapeField.Biome(in oOn, p) != ShapeField.Biome(in oOff, p)) differ++;
            }
            Assert.Greater(differ, 400, "the default layout is not B10's");
        }

        /// B10's six-biome formula (pre-176), verbatim.
        private static byte B10(in Offsets o, float2 p)
        {
            float s = o.biomeScale;
            float t = Noise.Fbm(p / (15000f * s) + o.tempA, 1) * 1.6f;
            float m = Noise.Fbm(p / (12000f * s) + o.moistA, 1) * 1.6f;
            float df = t * t + m * m;
            float dd = (t - 0.8f) * (t - 0.8f) + (m + 0.8f) * (m + 0.8f);
            float ds = (t + 0.9f) * (t + 0.9f) + (m - 0.1f) * (m - 0.1f);
            float dj = (t - 0.8f) * (t - 0.8f) + (m - 0.8f) * (m - 0.8f);
            float dv = (t - 0.8f) * (t - 0.8f) + m * m;
            float db = (t + 0.3f) * (t + 0.3f) + (m + 0.8f) * (m + 0.8f);
            byte b = GenBiome.Forest; float best = df;
            if (dd < best) { best = dd; b = GenBiome.Desert; }
            if (ds < best) { best = ds; b = GenBiome.Snow; }
            if (dj < best) { best = dj; b = GenBiome.Jungle; }
            if (dv < best) { best = dv; b = GenBiome.Savanna; }
            if (db < best) { b = GenBiome.Badlands; }
            return b;
        }

        /// Per biome, the number of 4-connected land patches of >= REGION_MIN_HA.
        public static int[] Regions(bool[] isLand, byte[] biome, int n, float cellM)
        {
            var count = new int[GenBiome.Count];
            var seen = new bool[n * n]; var stack = new Stack<int>();
            int minCells = (int)math.ceil(REGION_MIN_HA * 10000f / (cellM * cellM));
            for (int s0 = 0; s0 < n * n; s0++)
            {
                if (!isLand[s0] || seen[s0]) continue;
                int size = 0; byte b = biome[s0]; seen[s0] = true; stack.Push(s0);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(); size++;
                    int x = i % n, z = i / n;
                    void Try(int j) { if (isLand[j] && !seen[j] && biome[j] == b) { seen[j] = true; stack.Push(j); } }
                    if (x > 0) Try(i - 1); if (x < n - 1) Try(i + 1); if (z > 0) Try(i - n); if (z < n - 1) Try(i + n);
                }
                if (size >= minCells) count[b]++;
            }
            return count;
        }

        /// The longest straight segment (48 directions, from up to 2,500 border
        /// cells) that stays within STRAIGHT_TOL_M of a land biome border along its
        /// whole length, in m. A border cell is a land cell with a land 4-neighbour
        /// of another biome. `seg` = (x0, z0, x1, z1) in cells.
        public static float LongestStraightBorderM(bool[] isLand, byte[] biome, int n, float cellM, out float4 seg)
        {
            var border = new bool[n * n];
            var cellsB = new List<int>();
            for (int z = 1; z < n - 1; z++)
            for (int x = 1; x < n - 1; x++)
            {
                int i = z * n + x;
                if (!isLand[i]) continue;
                bool b = (isLand[i - 1] && biome[i - 1] != biome[i]) || (isLand[i + 1] && biome[i + 1] != biome[i])
                      || (isLand[i - n] && biome[i - n] != biome[i]) || (isLand[i + n] && biome[i + n] != biome[i]);
                if (b) { border[i] = true; cellsB.Add(i); }
            }
            seg = default;
            if (cellsB.Count == 0) return 0f;
            int r = (int)math.round(STRAIGHT_TOL_M / cellM);
            var band = new bool[n * n];
            foreach (int i in cellsB)
            {
                int x = i % n, z = i / n;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dz * dz > r * r) continue;
                    int xx = x + dx, zz = z + dz;
                    if (xx >= 0 && xx < n && zz >= 0 && zz < n) band[zz * n + xx] = true;
                }
            }
            var rng = new Unity.Mathematics.Random(0x5717u);
            int starts = math.min(2500, cellsB.Count);
            const int DIRS = 48;
            float best = 0f;
            for (int k = 0; k < starts; k++)
            {
                int i = cellsB.Count > 2500 ? cellsB[rng.NextInt(cellsB.Count)] : cellsB[k];
                float x0 = i % n, z0 = i / n;
                for (int d = 0; d < DIRS; d++)
                {
                    float a = d * math.PI / DIRS, dx = math.cos(a), dz = math.sin(a);
                    int f = Walk(band, n, x0, z0, dx, dz), b = Walk(band, n, x0, z0, -dx, -dz);
                    float len = (f + b) * cellM;
                    if (len > best) { best = len; seg = new float4(x0 + f * dx, z0 + f * dz, x0 - b * dx, z0 - b * dz); }
                }
            }
            return best;
        }

        private static int Walk(bool[] band, int n, float x0, float z0, float dx, float dz)
        {
            int s = 1;
            for (; ; s++)
            {
                int x = (int)math.round(x0 + s * dx), z = (int)math.round(z0 + s * dz);
                if (x < 0 || x >= n || z < 0 || z >= n || !band[z * n + x]) return s - 1;
            }
        }
    }
}
