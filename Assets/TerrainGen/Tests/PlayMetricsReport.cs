// Assets/TerrainGen/Tests/PlayMetricsReport.cs
//
// Surface rebuild plan, step B0 (owner-approved 2026-10-02): a report-only
// harness that measures a world's play numbers from its 8 m map. No terrain
// change. On seed 42 v3 it must reproduce the terrain review (2026-10-01):
// median slope 30 deg, 29% steeper than 45 deg, 4% open, 73% mountainous, a
// biome change every 360 m walking east-west, 11 town sites, a landmark in
// every direction from 2.2% of land -- each within 1 point or 10% (the plan's
// tolerance). Later B steps read their acceptance numbers off this report.
// Asserts nothing; writes Logs/TerrainGen/playmetrics/playmetrics.txt and
// top-down PNGs per seed. ~1 min per seed, so opt-in:
//   PLAYMETRICS=1 tools/run-editmode-filter.sh PlayMetricsReport
//   PLAYMETRICS_SEEDS=42,7 ... to choose seeds (default 42, 7, 2026, 31337).
//
// Definitions (land = map height above sea; "8 m scale" = the map grid):
//   slope        central differences over the 8 m map, degrees.
//   relief       highest minus lowest map height in the 250 m square centred
//                on a cell (31 x 31 cells, sea floor as it is): < 20 m "open",
//                > 80 m "mountainous" (the review's words). The review did not
//                write the window down; this one reproduces its 4% / 73%
//                (a +-250 m window gives 0.1% / 94.7%, clamping the sea floor
//                to sea level 3.3% / 85.6%).
//   landform     the plan's rule 2 classes of Regions' archetypes.
//   bands        Walk < 15, Scramble 15-30, Climb 30-45, Sheer > 45 deg.
//                PROVISIONAL: the plan names the bands but not their angles;
//                these use the review's own 15/45 cut points. B6 fixes them.
//   easy ground  Walk band + gentle landform + lowest third of the land's
//                height range (plan rule 3, "from the land alone").
//   town site    a 4-connected patch of land (rivers and lakes included) under
//                8 deg -- the plan's rule 4 "flat room" angle -- of at least
//                4 ha. The review gave the count, not the rule; this rule
//                reproduces its 11 (the report prints a sweep of the angle).
//   biome change mean land distance walked east-west per biome change, rows
//                every 32 m, sampled every 8 m; the same for regions. The
//                review's 360 m is NOT reproduced by any sampling of today's
//                ShapeField.Biome at 8-64 m (297-318 m); only a 128 m step
//                reaches it (printed alongside). Its method was not recorded.
//   landmark     TerrainGenTests.Report_LandmarkInEveryDirection's metric,
//                verbatim (same grid, summit rule, RNG and 1,500 points): the
//                share of land points with a visible landmark within 3 km in
//                all four 90-degree sectors.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class PlayMetricsReport
    {
        private static readonly uint[] DefaultSeeds = { 42u, 7u, 2026u, 31337u };

        private const float WALK = 15f, SCRAMBLE = 30f, CLIMB = 45f;   // band upper edges, degrees
        private const float OPEN_M = 20f, MOUNTAIN_M = 80f, RELIEF_WINDOW_M = 250f;
        private const float TOWN_SLOPE = 8f, TOWN_HA = 4f;
        private static readonly float[] TownSlopeSweep = { 5f, 6f, 7f, 8f, 10f };

        public const byte Gentle = 0, Broken = 1, Steep = 2;
        private static readonly string[] ClassNames = { "gentle", "broken", "steep" };

        /// Plan rule 2: gentle = river plains, rolling highlands, wetland basin,
        /// plateau tops; broken = badlands, dunes, canyonlands; steep = alpine,
        /// sea cliffs, volcanic.
        public static byte LandformClass(byte region)
        {
            switch (region)
            {
                case Regions.Plains: case Regions.Highlands: case Regions.Wetland: case Regions.Plateau: return Gentle;
                case Regions.Badlands: case Regions.Dunes: case Regions.Canyonlands: return Broken;
                default: return Steep;
            }
        }

        private sealed class Metrics
        {
            public uint seed;
            public double buildMs, measureMs;
            public int land;
            public float landKm2, medianSlope, under15, over45, open, mountainous;
            public float[] bandShare = new float[4];
            public float[] classShare = new float[3];
            public float[] classMedianSlope = new float[3];
            public float[] regionMedianSlope = new float[Regions.KINDS];
            public float[] regionShare = new float[Regions.KINDS];
            public float lowThirdM, easyShare, gentleLowShare;
            public float biomeChangeM, biomeChange128M, regionChangeM, biomePatchMedianHa, biomePatchAreaMedianHa;
            public int biomePatches;
            public int towns; public string townRegions; public string townSweep;
            public float landmarkAll4At3, landmarkAll4At15; public int landmarks;
            public int caveMouths; public float mouthsPerKm2;
            public float riverKm; public int lakes;
        }

        [Test]
        public void Report_PlayMetrics()
        {
            if (Environment.GetEnvironmentVariable("PLAYMETRICS") != "1")
                Assert.Ignore("report only: set PLAYMETRICS=1 (tools/run-editmode-filter.sh PlayMetricsReport)");
            uint[] seeds = DefaultSeeds;
            string env = Environment.GetEnvironmentVariable("PLAYMETRICS_SEEDS");
            if (!string.IsNullOrEmpty(env)) seeds = env.Split(',').Select(s => uint.Parse(s.Trim())).ToArray();

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "playmetrics");
            Directory.CreateDirectory(dir);
            var all = new List<Metrics>();
            var sb = new StringBuilder();
            foreach (uint seed in seeds)
            {
                var m = Measure(seed, dir);
                all.Add(m);
                sb.Append(Describe(m));
            }
            sb.Append(Summary(all));
            foreach (var m in all)
                if (m.seed == 42u) sb.Append(Reproduction(m));
            File.WriteAllText(Path.Combine(dir, "playmetrics.txt"), sb.ToString());
            Debug.Log("[PlayMetrics]\n" + sb);
        }

        // ------------------------------------------------------------------ measure

        private static Metrics Measure(uint seed, string dir)
        {
            var m = new Metrics { seed = seed };
            var spec = TerrainSpec.SizeClass2(seed);
            var o = Offsets.For(seed);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var map = WorldMap.Build(spec);
            m.buildMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            int n = spec.mapCells;
            float cellM = spec.mapCellVoxels / 10f;
            float[] h = map.Height;                       // voxels relative to sea
            var isLand = new bool[n * n];
            for (int i = 0; i < n * n; i++) { isLand[i] = h[i] > 0f; if (isLand[i]) m.land++; }
            m.landKm2 = m.land * cellM * cellM * 1e-6f;

            // slope (degrees) at 8 m, central differences
            var slope = new float[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int x0 = math.max(x - 1, 0), x1 = math.min(x + 1, n - 1);
                int z0 = math.max(z - 1, 0), z1 = math.min(z + 1, n - 1);
                float gx = (h[z * n + x1] - h[z * n + x0]) / ((x1 - x0) * spec.mapCellVoxels);
                float gz = (h[z1 * n + x] - h[z0 * n + x]) / ((z1 - z0) * spec.mapCellVoxels);
                slope[z * n + x] = math.degrees(math.atan(math.sqrt(gx * gx + gz * gz)));
            }

            // relief in the 250 m square centred on each cell
            float[] relief = Relief(h, n, (int)(RELIEF_WINDOW_M / cellM) >> 1);

            // biome per cell (computed on demand today: ShapeField.Biome)
            var biome = new byte[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                biome[z * n + x] = ShapeField.Biome(in o, new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels));

            // land height range -> lowest third
            float hiLand = 0f;
            for (int i = 0; i < n * n; i++) if (isLand[i]) hiLand = math.max(hiLand, h[i]);
            float lowThird = hiLand / 3f;
            m.lowThirdM = lowThird / 10f;

            var landSlopes = new List<float>(m.land);
            var classSlopes = new[] { new List<float>(), new List<float>(), new List<float>() };
            var regionSlopes = new List<float>[Regions.KINDS];
            for (int k = 0; k < Regions.KINDS; k++) regionSlopes[k] = new List<float>();
            int under15 = 0, over45 = 0, open = 0, mountain = 0, easy = 0, gentleLow = 0;
            var bands = new int[4]; var classN = new int[3]; var regionN = new int[Regions.KINDS];
            var easyMask = new bool[n * n];
            for (int i = 0; i < n * n; i++)
            {
                if (!isLand[i]) continue;
                float s = slope[i];
                landSlopes.Add(s);
                if (s < 15f) under15++;
                if (s > 45f) over45++;
                bands[Band(s)]++;
                if (relief[i] < OPEN_M * 10f) open++;
                if (relief[i] > MOUNTAIN_M * 10f) mountain++;
                byte reg = map.Region[i];
                if (reg == 255) continue;
                byte cls = LandformClass(reg);
                classN[cls]++; classSlopes[cls].Add(s);
                regionN[reg]++; regionSlopes[reg].Add(s);
                if (cls == Gentle && h[i] < lowThird)
                {
                    gentleLow++;
                    if (s < WALK) { easy++; easyMask[i] = true; }
                }
            }
            float L = m.land;
            m.medianSlope = Median(landSlopes);
            m.under15 = under15 / L; m.over45 = over45 / L;
            m.open = open / L; m.mountainous = mountain / L;
            for (int b = 0; b < 4; b++) m.bandShare[b] = bands[b] / L;
            for (int c = 0; c < 3; c++) { m.classShare[c] = classN[c] / L; m.classMedianSlope[c] = Median(classSlopes[c]); }
            for (int k = 0; k < Regions.KINDS; k++) { m.regionShare[k] = regionN[k] / L; m.regionMedianSlope[k] = Median(regionSlopes[k]); }
            m.easyShare = easy / L; m.gentleLowShare = gentleLow / L;

            // biome / region change walking east-west, rows every 32 m
            long walked = 0; int bChanges = 0, rChanges = 0;
            for (int z = 0; z < n; z += 4)
            for (int x = 0; x + 1 < n; x++)
            {
                int a = z * n + x;
                if (!isLand[a] || !isLand[a + 1]) continue;
                walked++;
                if (biome[a] != biome[a + 1]) bChanges++;
                if (map.Region[a] != map.Region[a + 1]) rChanges++;
            }
            m.biomeChangeM = bChanges > 0 ? walked * cellM / bChanges : float.PositiveInfinity;
            m.regionChangeM = rChanges > 0 ? walked * cellM / rChanges : float.PositiveInfinity;
            long walked128 = 0; int bChanges128 = 0;
            for (int z = 0; z < n; z += 4)
            for (int x = 0; x + 16 < n; x += 16)
            {
                int a = z * n + x;
                if (!isLand[a] || !isLand[a + 16]) continue;
                walked128++;
                if (biome[a] != biome[a + 16]) bChanges128++;
            }
            m.biomeChange128M = bChanges128 > 0 ? walked128 * 16 * cellM / bChanges128 : float.PositiveInfinity;

            // biome patches on land (4-connected, same biome)
            var patches = Components(n, i => isLand[i], (a, b) => biome[a] == biome[b]);
            float haPerCell = cellM * cellM / 10000f;
            m.biomePatches = patches.Count;
            var patchHa = patches.Select(p => p.Count * haPerCell).OrderBy(v => v).ToList();
            m.biomePatchMedianHa = patchHa.Count > 0 ? patchHa[patchHa.Count / 2] : 0f;
            m.biomePatchAreaMedianHa = AreaWeightedMedian(patchHa);

            // town sites: land under the slope threshold, patches of 4 ha+
            int minCells = (int)math.ceil(TOWN_HA / haPerCell);
            var sweep = new StringBuilder();
            List<List<int>> towns = null;
            foreach (float t in TownSlopeSweep)
            {
                float tt = t;
                var comps = Components(n, i => isLand[i] && slope[i] < tt, (a, b) => true).Where(c => c.Count >= minCells).ToList();
                sweep.Append($"{t:F0}deg {comps.Count}  ");
                if (math.abs(t - TOWN_SLOPE) < 0.01f) towns = comps;
            }
            m.townSweep = sweep.ToString().TrimEnd();
            m.towns = towns.Count;
            var townByRegion = new int[Regions.KINDS + 1];
            foreach (var c in towns)
            {
                var votes = new int[256];
                foreach (int i in c) votes[map.Region[i]]++;
                int best = 0; for (int k = 1; k < 256; k++) if (votes[k] > votes[best]) best = k;
                townByRegion[best == 255 ? Regions.KINDS : best]++;
            }
            m.townRegions = string.Join(", ", Enumerable.Range(0, Regions.KINDS).Where(k => townByRegion[k] > 0)
                .OrderByDescending(k => townByRegion[k]).Select(k => $"{Regions.Name((byte)k)} {townByRegion[k]}"));

            // landmarks: TerrainGenTests' metric
            Landmarks(spec, h, out m.landmarks, out m.landmarkAll4At15, out m.landmarkAll4At3);

            // cave mouths on land (StampTests' rule: the site sits 3 m+ above sea)
            var v = map.View;
            int cells = spec.worldSizeVoxels >> Stamps.CELL_SHIFT;
            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                if (!Stamps.CellStamp(in o, gx, gz, out var st) || st.kind != Stamps.CaveMouth) continue;
                Stamps.SampleSiteHeight(in spec, in v, st.pos, out int g);
                if (g > spec.seaLevelY + 30) m.caveMouths++;
            }
            m.mouthsPerKm2 = m.caveMouths / m.landKm2;
            m.riverKm = map.Hydro.riverCells * cellM / 1000f;
            m.lakes = map.Hydro.lakes;
            m.measureMs = sw.Elapsed.TotalMilliseconds;

            WritePngs(dir, seed, n, h, isLand, slope, relief, biome, map.Region, map.WaterClass, easyMask, towns);
            // PLAYMETRICS_DUMP=1: the map behind these numbers, for offline checks
            // (int n; float height[n*n] in voxels rel. sea; byte region, water class, biome [n*n] each).
            if (Environment.GetEnvironmentVariable("PLAYMETRICS_DUMP") == "1")
                using (var f = new BinaryWriter(File.Create(Path.Combine(dir, $"seed{seed}_map.bin"))))
                {
                    f.Write(n);
                    foreach (float x in h) f.Write(x);
                    f.Write(map.Region); f.Write(map.WaterClass); f.Write(biome);
                }
            return m;
        }

        private static int Band(float s) => s < WALK ? 0 : s < SCRAMBLE ? 1 : s < CLIMB ? 2 : 3;

        private static float Median(List<float> v)
        {
            if (v.Count == 0) return float.NaN;
            v.Sort();
            return v[v.Count / 2];
        }

        /// Patch size at which half of the patched AREA lies in larger patches.
        private static float AreaWeightedMedian(List<float> sortedHa)
        {
            double total = sortedHa.Sum(x => (double)x), acc = 0;
            foreach (float a in sortedHa) { acc += a; if (acc >= total * 0.5) return a; }
            return 0f;
        }

        /// max - min over the (2r+1)^2 square around each cell: two separable
        /// sliding-window passes per extreme (monotone deque, O(n^2)).
        private static float[] Relief(float[] src, int n, int r)
        {
            var tmp = new float[n * n]; var hi = new float[n * n]; var lo = new float[n * n];
            Slide(src, tmp, n, r, true, true); Slide(tmp, hi, n, r, true, false);
            Slide(src, tmp, n, r, false, true); Slide(tmp, lo, n, r, false, false);
            for (int i = 0; i < n * n; i++) hi[i] -= lo[i];
            return hi;
        }

        private static void Slide(float[] src, float[] dst, int n, int r, bool max, bool alongX)
        {
            var q = new int[n];
            for (int line = 0; line < n; line++)
            {
                int Idx(int k) => alongX ? line * n + k : k * n + line;
                int head = 0, tail = 0, j = 0;
                for (int i = 0; i < n; i++)
                {
                    int last = math.min(n - 1, i + r);
                    for (; j <= last; j++)
                    {
                        float val = src[Idx(j)];
                        while (tail > head && (max ? src[Idx(q[tail - 1])] <= val : src[Idx(q[tail - 1])] >= val)) tail--;
                        q[tail++] = j;
                    }
                    while (q[head] < i - r) head++;
                    dst[Idx(i)] = src[Idx(q[head])];
                }
            }
        }

        /// 4-connected components of cells passing `inside`, joined where
        /// `same(a, b)` holds. Returns each component's cell list.
        private static List<List<int>> Components(int n, Func<int, bool> inside, Func<int, int, bool> same)
        {
            var label = new int[n * n];
            var comps = new List<List<int>>();
            var stack = new Stack<int>();
            for (int s = 0; s < n * n; s++)
            {
                if (label[s] != 0 || !inside(s)) continue;
                var c = new List<int>();
                int id = comps.Count + 1;
                label[s] = id; stack.Push(s);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(); c.Add(i);
                    int x = i & (n - 1), z = i / n;   // n is a power of two (mapCells)
                    if (x > 0) Visit(i - 1); if (x < n - 1) Visit(i + 1);
                    if (z > 0) Visit(i - n); if (z < n - 1) Visit(i + n);
                    void Visit(int k)
                    {
                        if (label[k] != 0 || !inside(k) || !same(i, k)) return;
                        label[k] = id; stack.Push(k);
                    }
                }
                comps.Add(c);
            }
            return comps;
        }

        /// TerrainGenTests.Report_LandmarkInEveryDirection, verbatim in what it
        /// measures (32 m max grid; summit = highest within 384 m and >= 60 m
        /// above its 1 km mean; line of sight from 2 m; RNG 7; 1,500 land points).
        private static void Landmarks(in TerrainSpec spec, float[] height, out int count, out float all4At15, out float all4At3)
        {
            const int F = 4;
            int n = spec.mapCells / F;
            float cellM = spec.mapCellVoxels * F / 10f;
            var g = new float[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float mx = float.MinValue;
                for (int j = 0; j < F; j++) for (int i = 0; i < F; i++)
                    mx = math.max(mx, height[(z * F + j) * spec.mapCells + x * F + i]);
                g[z * n + x] = mx / 10f;
            }
            var sat = new double[(n + 1) * (n + 1)];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                sat[(z + 1) * (n + 1) + x + 1] = g[z * n + x] + sat[z * (n + 1) + x + 1] + sat[(z + 1) * (n + 1) + x] - sat[z * (n + 1) + x];
            double Mean(int x0, int z0, int x1, int z1)
            {
                x0 = math.max(0, x0); z0 = math.max(0, z0); x1 = math.min(n - 1, x1); z1 = math.min(n - 1, z1);
                double s = sat[(z1 + 1) * (n + 1) + x1 + 1] - sat[z0 * (n + 1) + x1 + 1] - sat[(z1 + 1) * (n + 1) + x0] + sat[z0 * (n + 1) + x0];
                return s / ((x1 - x0 + 1) * (z1 - z0 + 1));
            }
            int rLocal = (int)(384 / cellM), rMean = (int)(500 / cellM);
            var marks = new List<int2>();
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float hh = g[z * n + x];
                if (hh < 20f) continue;
                bool isMax = true;
                for (int j = -rLocal; j <= rLocal && isMax; j++)
                for (int i = -rLocal; i <= rLocal; i++)
                {
                    int xx = x + i, zz = z + j;
                    if (xx < 0 || zz < 0 || xx >= n || zz >= n || (i == 0 && j == 0)) continue;
                    if (g[zz * n + xx] > hh) { isMax = false; break; }
                }
                if (isMax && hh - Mean(x - rMean, z - rMean, x + rMean, z + rMean) >= 60.0) marks.Add(new int2(x, z));
            }
            bool Visible(int2 a, int2 b)
            {
                float ha = g[a.y * n + a.x] + 2f, hb = g[b.y * n + b.x];
                int steps = math.max(math.abs(b.x - a.x), math.abs(b.y - a.y));
                for (int k = 1; k < steps; k++)
                {
                    float t = k / (float)steps;
                    int xx = (int)math.round(a.x + (b.x - a.x) * t), zz = (int)math.round(a.y + (b.y - a.y) * t);
                    if (g[zz * n + xx] > ha + (hb - ha) * t) return false;
                }
                return true;
            }
            var rng = new Unity.Mathematics.Random(7);
            int pts = 0, all4 = 0, all4b = 0;
            float r15 = 1500f / cellM, r30 = 3000f / cellM;
            while (pts < 1500)
            {
                int x = rng.NextInt(0, n), z = rng.NextInt(0, n);
                if (g[z * n + x] <= 0f) continue;
                pts++;
                var seen = new bool[4]; var seen3 = new bool[4];
                foreach (var mk in marks)
                {
                    float dx = mk.x - x, dz = mk.y - z;
                    float d = math.sqrt(dx * dx + dz * dz);
                    if (d < 1f || d > r30) continue;
                    int sector = math.abs(dz) >= math.abs(dx) ? (dz > 0 ? 0 : 2) : (dx > 0 ? 1 : 3);
                    if (seen3[sector] && (seen[sector] || d > r15)) continue;
                    if (!Visible(new int2(x, z), mk)) continue;
                    seen3[sector] = true;
                    if (d <= r15) seen[sector] = true;
                }
                if (seen3[0] && seen3[1] && seen3[2] && seen3[3]) all4b++;
                if (seen[0] && seen[1] && seen[2] && seen[3]) all4++;
            }
            count = marks.Count; all4At15 = all4 / (float)pts; all4At3 = all4b / (float)pts;
        }

        // ------------------------------------------------------------------ report

        private static string Pct(float f) => $"{100f * f:F1}%";

        private static string Describe(Metrics m)
        {
            var s = new StringBuilder();
            s.AppendLine($"=== seed {m.seed} (sizeClass 2, map v3): map built in {m.buildMs / 1000:F1} s, measured in {m.measureMs / 1000:F1} s");
            s.AppendLine($"  land {m.landKm2:F2} km^2; land height range 0..{m.lowThirdM * 3:F0} m, lowest third under {m.lowThirdM:F0} m");
            s.AppendLine($"  slope (8 m): median {m.medianSlope:F1} deg; under 15 deg {Pct(m.under15)}; steeper than 45 deg {Pct(m.over45)}");
            s.AppendLine($"  bands: Walk {Pct(m.bandShare[0])}, Scramble {Pct(m.bandShare[1])}, Climb {Pct(m.bandShare[2])}, Sheer {Pct(m.bandShare[3])}  (provisional 15/30/45 deg)");
            s.AppendLine($"  relief in a 250 m square: open (< 20 m) {Pct(m.open)}, mountainous (> 80 m) {Pct(m.mountainous)}");
            s.AppendLine($"  landform class share of land: gentle {Pct(m.classShare[0])}, broken {Pct(m.classShare[1])}, steep {Pct(m.classShare[2])}; " +
                         $"median slope {m.classMedianSlope[0]:F0} / {m.classMedianSlope[1]:F0} / {m.classMedianSlope[2]:F0} deg");
            s.AppendLine("  by region (share of land, median slope): " + string.Join(", ",
                Enumerable.Range(0, Regions.KINDS).Where(k => m.regionShare[k] > 0f).OrderBy(k => m.regionMedianSlope[k])
                    .Select(k => $"{Regions.Name((byte)k)} {Pct(m.regionShare[k])} {m.regionMedianSlope[k]:F0}deg")));
            s.AppendLine($"  easy ground (Walk + gentle + lowest third): {Pct(m.easyShare)} of land (gentle + lowest third, any slope: {Pct(m.gentleLowShare)})");
            s.AppendLine($"  biome change walking east-west every {m.biomeChangeM:F0} m (sampled every 128 m: {m.biomeChange128M:F0} m); region change every {m.regionChangeM:F0} m");
            s.AppendLine($"  biome patches on land: {m.biomePatches}; median {m.biomePatchMedianHa:F1} ha; area-weighted median {m.biomePatchAreaMedianHa:F0} ha");
            s.AppendLine($"  town sites (land under {TOWN_SLOPE:F0} deg, {TOWN_HA:F0} ha+): {m.towns}  [{m.townRegions}]");
            s.AppendLine($"     sweep of the slope threshold: {m.townSweep}");
            s.AppendLine($"  landmarks: {m.landmarks}; land with one in all 4 directions within 3 km {Pct(m.landmarkAll4At3)} (within 1.5 km {Pct(m.landmarkAll4At15)})");
            s.AppendLine($"  cave mouths on land: {m.caveMouths} ({m.mouthsPerKm2:F2} per km^2); river {m.riverKm:F1} km (river cells x 8 m); lakes {m.lakes}");
            return s.ToString();
        }

        private static string Summary(List<Metrics> all)
        {
            var s = new StringBuilder("\n=== baselines (v3)\n");
            s.AppendLine("  seed    land km2  median  <15deg  >45deg  open   mount.  gentle  easy   biome m  region m  towns  landmark3km  mouths/km2  river km");
            foreach (var m in all)
                s.AppendLine($"  {m.seed,-6}  {m.landKm2,8:F2}  {m.medianSlope,5:F1}d  {Pct(m.under15),6}  {Pct(m.over45),6}  {Pct(m.open),5}  {Pct(m.mountainous),6}  " +
                             $"{Pct(m.classShare[0]),6}  {Pct(m.easyShare),5}  {m.biomeChangeM,7:F0}  {m.regionChangeM,8:F0}  {m.towns,5}  {Pct(m.landmarkAll4At3),11}  {m.mouthsPerKm2,10:F2}  {m.riverKm,8:F1}");
            return s.ToString();
        }

        /// The plan's B0 gate: each review number within 1 point or 10%.
        private static string Reproduction(Metrics m)
        {
            var rows = new (string name, float review, float measured, string unit)[]
            {
                ("median slope", 30f, m.medianSlope, "deg"),
                ("steeper than 45 deg", 29f, 100f * m.over45, "%"),
                ("open (< 20 m, 250 m square)", 4f, 100f * m.open, "%"),
                ("mountainous (> 80 m, 250 m sq.)", 73f, 100f * m.mountainous, "%"),
                ("biome change east-west", 360f, m.biomeChangeM, "m"),
                ("town sites 4 ha+", 11f, m.towns, ""),
                ("landmark every direction 3 km", 2.2f, 100f * m.landmarkAll4At3, "%"),
            };
            var s = new StringBuilder("\n=== seed 42 against the terrain review (2026-10-01); tolerance 1 point or 10%\n");
            int ok = 0;
            foreach (var r in rows)
            {
                float tol = math.max(1f, 0.1f * r.review);
                bool pass = math.abs(r.measured - r.review) <= tol;
                if (pass) ok++;
                s.AppendLine($"  {r.name,-32} review {r.review,6:0.#}{r.unit,-3}  measured {r.measured,7:F1}{r.unit,-3}  tol +-{tol:0.#}  {(pass ? "OK" : "MISS")}");
            }
            s.AppendLine($"  REPRODUCED {ok}/{rows.Length}");
            return s.ToString();
        }

        // ------------------------------------------------------------------ images

        private const int PNG = 512;   // 16 m per pixel

        private static void WritePngs(string dir, uint seed, int n, float[] h, bool[] isLand, float[] slope, float[] relief,
                                      byte[] biome, byte[] region, byte[] water, bool[] easy, List<List<int>> towns)
        {
            var townMask = new bool[n * n];
            foreach (var c in towns) foreach (int i in c) townMask[i] = true;
            // town sites drawn as outlines (2 cells wide) so the easy ground under them shows
            bool TownEdge(int i)
            {
                if (!townMask[i]) return false;
                int x = i & (n - 1), z = i / n;
                return x < 2 || z < 2 || x >= n - 2 || z >= n - 2 ||
                       !townMask[i - 2] || !townMask[i + 2] || !townMask[i - 2 * n] || !townMask[i + 2 * n];
            }
            var classCol = new[] { new Color(0.55f, 0.78f, 0.40f), new Color(0.90f, 0.62f, 0.30f), new Color(0.62f, 0.55f, 0.70f) };
            var bandCol = new[] { new Color(0.45f, 0.75f, 0.35f), new Color(0.95f, 0.85f, 0.35f), new Color(0.92f, 0.50f, 0.25f), new Color(0.70f, 0.15f, 0.20f) };
            var biomeCol = new[] { new Color(0.25f, 0.55f, 0.25f), new Color(0.92f, 0.80f, 0.50f), new Color(0.92f, 0.94f, 0.98f), new Color(0.10f, 0.45f, 0.30f) };
            Write(dir, $"seed{seed}_landform_class.png", n, h, isLand, water, i => region[i] == 255 ? Color.gray : classCol[LandformClass(region[i])]);
            Write(dir, $"seed{seed}_slope_bands.png", n, h, isLand, water, i => bandCol[Band(slope[i])]);
            Write(dir, $"seed{seed}_relief.png", n, h, isLand, water,
                  i => relief[i] < OPEN_M * 10f ? new Color(0.45f, 0.80f, 0.40f) : relief[i] > MOUNTAIN_M * 10f ? new Color(0.60f, 0.45f, 0.45f) : new Color(0.85f, 0.80f, 0.55f));
            Write(dir, $"seed{seed}_easy_ground.png", n, h, isLand, water,
                  i => TownEdge(i) ? new Color(0.95f, 0.20f, 0.85f) : easy[i] ? new Color(0.30f, 0.85f, 0.30f) : new Color(0.62f, 0.62f, 0.62f));
            Write(dir, $"seed{seed}_biome.png", n, h, isLand, water, i => biomeCol[biome[i]]);
        }

        /// Top-down, north (+z) up, 2x2 cells per pixel, tinted and hillshaded;
        /// sea dark blue, lakes and rivers light blue.
        private static void Write(string dir, string name, int n, float[] h, bool[] isLand, byte[] water, Func<int, Color> tint)
        {
            int f = n / PNG;
            var tex = new Texture2D(PNG, PNG, TextureFormat.RGB24, false);
            var px = new Color32[PNG * PNG];
            for (int pz = 0; pz < PNG; pz++)
            for (int pxX = 0; pxX < PNG; pxX++)
            {
                int x = pxX * f, z = pz * f, i = z * n + x;
                Color c;
                if (!isLand[i]) c = new Color(0.10f, 0.18f, 0.35f);
                else if (water[i] != Hydrology.DRY) c = new Color(0.35f, 0.60f, 0.90f);
                else
                {
                    int x1 = math.min(x + 1, n - 1), z1 = math.min(z + 1, n - 1);
                    float gx = (h[z * n + x1] - h[i]) / 80f, gz = (h[z1 * n + x] - h[i]) / 80f;
                    var nrm = math.normalize(new float3(-gx, 1f, -gz));
                    float shade = math.clamp(0.35f + 0.75f * math.dot(nrm, math.normalize(new float3(-0.5f, 0.75f, 0.45f))), 0.3f, 1.15f);
                    c = tint(i) * shade;
                }
                px[pz * PNG + pxX] = c;
            }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
