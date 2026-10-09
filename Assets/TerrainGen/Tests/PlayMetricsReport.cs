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
//   PLAYMETRICS_MAPV=3,4 ... map versions (default 3). Per seed the versions
//   are built back to back, so build times compare on the same machine state.
//   v3 writes playmetrics.txt and seed<N>_*.png exactly as before; v4 (B1)
//   writes playmetrics_v4.txt, seed<N>_v4_*.png, a biome-scale strip per seed
//   (seed<N>_biome_scales.png: v3 then x PLAYMETRICS_BIOMESCALES, default
//   2,3,4) and, when both ran, b1_biome_scale.txt (the B1 acceptance table).
//   PLAYMETRICS_GENTLE=0.4,0.5,0.6 ... v4 gentle-class land shares (B2; default:
//   the version default only). A non-default share writes playmetrics_v4_g<NN>.txt
//   and seed<N>_v4_g<NN>_*.png; any v4 run writes b2_gentle_variants.txt, and a
//   run with v3 and the default v4 writes b2_landform.txt (the B2 table).
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
//   easy areas   (B4, v4) the world record's FLAG_EASY cells: easy ground (next
//                entry) inside the seeded easy areas only (EasyAreas.cs). This is
//                B4's acceptance share; easy ground anywhere is ~25% of v4 land.
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
        public static byte LandformClass(byte region) => Regions.ClassOf(region);   // B2 moved it into the generator

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
            public int mapVersion; public float biomeScale;
            public float gentleTarget; public int inselbergs; public string tag;
            public double recordMs; public long recordBytes; public ulong recordHash; public int recordBiomeMismatch;
            public float biomePatchMedian1HaHa;
            public List<(float scale, BiomeStats st)> scales = new List<(float, BiomeStats)>();
            public int towns; public string townRegions; public string townSweep;
            public float landmarkAll4At3, landmarkAll4At15; public int landmarks;
            public int caveMouths; public float mouthsPerKm2;
            public float riverKm; public int lakes;
            // B4 (v4): the record's easy areas
            public float easyFlagShare; public int easyAreaCount, easyAreaWant, easyFlagNotEasy;
            public float[] easyAreaHa = new float[0]; public string easyAreaDesc = "";
            // B5 (v4): signature features
            public bool hasFeatures; public string featNumbers = "", featFails = "", featDesc = "", featShots = "";
            public int featCount, featWant, featRare, featWayDown; public float featVisible, featHiddenNear, featMinSepM;
            public string peakBroad = "-";
        }

        public struct BiomeStats
        {
            public float changeM, medianHa, areaMedianHa, median1HaHa;
            public int patches;
        }

        [Test]
        [Timeout(3600000)]   // 8 map builds with PLAYMETRICS_MAPV=3,4 (~10 min)
        public void Report_PlayMetrics()
        {
            if (Environment.GetEnvironmentVariable("PLAYMETRICS") != "1")
                Assert.Ignore("report only: set PLAYMETRICS=1 (tools/run-editmode-filter.sh PlayMetricsReport)");
            uint[] seeds = DefaultSeeds;
            string env = Environment.GetEnvironmentVariable("PLAYMETRICS_SEEDS");
            if (!string.IsNullOrEmpty(env)) seeds = env.Split(',').Select(s => uint.Parse(s.Trim())).ToArray();

            int[] versions = { 3 };
            string envV = Environment.GetEnvironmentVariable("PLAYMETRICS_MAPV");
            if (!string.IsNullOrEmpty(envV)) versions = envV.Split(',').Select(v => int.Parse(v.Trim())).ToArray();
            float[] scales = { 2f, 3f, 4f };
            string envS = Environment.GetEnvironmentVariable("PLAYMETRICS_BIOMESCALES");
            if (!string.IsNullOrEmpty(envS)) scales = envS.Split(',').Select(v => float.Parse(v.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "playmetrics");
            Directory.CreateDirectory(dir);
            float[] gentles = { 0f };   // 0 = the version default
            string envG = Environment.GetEnvironmentVariable("PLAYMETRICS_GENTLE");
            if (!string.IsNullOrEmpty(envG)) gentles = envG.Split(',').Select(v => float.Parse(v.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();

            // one list per output tag ("" = v3, "_v4" = v4 at its default share, "_v4_g40" ...)
            var byTag = new Dictionary<string, List<Metrics>>();
            var tagOrder = new List<string>();
            foreach (uint seed in seeds)
            foreach (int v in versions)
            foreach (float g in v >= 4 ? gentles : new[] { 0f })
            {
                var m = Measure(seed, v, g, scales, dir);
                if (!byTag.ContainsKey(m.tag)) { byTag[m.tag] = new List<Metrics>(); tagOrder.Add(m.tag); }
                byTag[m.tag].Add(m);
            }
            foreach (string tag in tagOrder)
            {
                var all = byTag[tag];
                var sb = new StringBuilder();
                foreach (var m in all) sb.Append(Describe(m));
                sb.Append(Summary(all));
                foreach (var m in all)
                    if (m.seed == 42u && tag == "") sb.Append(Reproduction(m));
                File.WriteAllText(Path.Combine(dir, $"playmetrics{tag}.txt"), sb.ToString());
                Debug.Log($"[PlayMetrics{tag}]\n" + sb);
            }
            if (byTag.ContainsKey("") && byTag.ContainsKey("_v4"))
            {
                string b1 = B1Table(byTag[""], byTag["_v4"]);
                File.WriteAllText(Path.Combine(dir, "b1_biome_scale.txt"), b1);
                Debug.Log("[PlayMetrics B1]\n" + b1);
                string b2 = B2Table(byTag[""], byTag["_v4"]);
                File.WriteAllText(Path.Combine(dir, "b2_landform.txt"), b2);
                Debug.Log("[PlayMetrics B2]\n" + b2);
            }
            if (byTag.ContainsKey("_v4"))
            {
                string b4 = B4Table(byTag.TryGetValue("", out var v3b) ? v3b : new List<Metrics>(), byTag["_v4"]);
                File.WriteAllText(Path.Combine(dir, "b4_easy_areas.txt"), b4);
                Debug.Log("[PlayMetrics B4]\n" + b4);
            }
            if (byTag.ContainsKey("_v4"))
            {
                string b5 = B5Table(byTag.TryGetValue("", out var v3c) ? v3c : new List<Metrics>(), byTag["_v4"]);
                File.WriteAllText(Path.Combine(dir, "b5_features.txt"), b5);
                Debug.Log("[PlayMetrics B5]\n" + b5);
                foreach (var m5 in byTag["_v4"])
                    if (m5.featShots.Length > 0) File.WriteAllText(Path.Combine(dir, $"b5_shots_seed{m5.seed}.txt"), m5.featShots);
            }
            var v4All = tagOrder.Where(t => t.StartsWith("_v4")).SelectMany(t => byTag[t]).ToList();
            if (v4All.Count > 0)
            {
                string gv = GentleVariants(v4All, byTag.TryGetValue("", out var v3l) ? v3l : new List<Metrics>());
                File.WriteAllText(Path.Combine(dir, "b2_gentle_variants.txt"), gv);
                Debug.Log("[PlayMetrics B2 variants]\n" + gv);
            }
        }

        /// The B2 acceptance table (plan rule 2): v4's gentle-class share of land
        /// 50 +- 5 points at the default, all three classes, no inselbergs, map
        /// build <= v3 + 2 s; slopes reported beside v3, not gated.
        private static string B2Table(List<Metrics> v3, List<Metrics> v4)
        {
            var s = new StringBuilder("=== B2: map v4 (landform by flatness, no inselbergs) against v3, same run\n");
            s.AppendLine("  seed    gentle v3 -> v4 (target)        broken v3 -> v4   steep v3 -> v4   classes  inselbergs v3 -> v4   build s v3 -> v4 (delta, <= +2)   record hash");
            foreach (var a in v3)
            {
                var b = v4.FirstOrDefault(x => x.seed == a.seed);
                if (b == null) continue;
                bool gOk = math.abs(b.classShare[0] - b.gentleTarget) <= 0.05f;
                bool all3 = b.classShare[0] > 0f && b.classShare[1] > 0f && b.classShare[2] > 0f;
                double dt = (b.buildMs - a.buildMs) / 1000;
                s.AppendLine($"  {a.seed,-6}  {Pct(a.classShare[0]),6} -> {Pct(b.classShare[0]),6} ({Pct(b.gentleTarget)}) {(gOk ? "PASS" : "MISS")}   " +
                             $"{Pct(a.classShare[1]),6} -> {Pct(b.classShare[1]),6}   {Pct(a.classShare[2]),6} -> {Pct(b.classShare[2]),6}   {(all3 ? "PASS" : "MISS"),-7}  " +
                             $"{a.inselbergs,3} -> {b.inselbergs,3} {(b.inselbergs == 0 ? "PASS" : "MISS")}       " +
                             $"{a.buildMs / 1000:F1} -> {b.buildMs / 1000:F1} ({dt:+0.0;-0.0}) {(dt <= 2.0 ? "PASS" : "MISS")}          {b.recordHash:X16}");
            }
            s.AppendLine("\n  slopes, reported not gated (v3 -> v4):");
            s.AppendLine("  seed    median deg      <15 deg          >45 deg          open (<20 m)     mountainous      easy ground      class median slope g/b/s v3 -> v4");
            foreach (var a in v3)
            {
                var b = v4.FirstOrDefault(x => x.seed == a.seed);
                if (b == null) continue;
                s.AppendLine($"  {a.seed,-6}  {a.medianSlope,4:F1} -> {b.medianSlope,4:F1}    {Pct(a.under15),6} -> {Pct(b.under15),6}  {Pct(a.over45),6} -> {Pct(b.over45),6}  " +
                             $"{Pct(a.open),6} -> {Pct(b.open),6}  {Pct(a.mountainous),6} -> {Pct(b.mountainous),6}  {Pct(a.easyShare),6} -> {Pct(b.easyShare),6}  " +
                             $"{a.classMedianSlope[0]:F0}/{a.classMedianSlope[1]:F0}/{a.classMedianSlope[2]:F0} -> {b.classMedianSlope[0]:F0}/{b.classMedianSlope[1]:F0}/{b.classMedianSlope[2]:F0}");
            }
            return s.ToString();
        }

        /// The B4 acceptance table (plan rule 3; owner Q1 (a), Q2 (a)): the
        /// record's easy ground 8-12% of land, the size's area count, every area
        /// >= 25 ha; B2/B3 still green; slopes beside v3 (B3's own numbers are in
        /// its report, Logs/overnight/b3/b3_i60_s06/playmetrics_v4.txt).
        private static string B4Table(List<Metrics> v3, List<Metrics> v4)
        {
            var s = new StringBuilder("=== B4: easy areas on map v4 (record flag + list)\n");
            s.AppendLine("  seed    easy share (8-12%)   areas (want)   area ha (>= 25 each)            flagged-but-not-easy  gentle (50+-5)  inselbergs  build s (v3)   record hash");
            foreach (var b in v4)
            {
                var a = v3.FirstOrDefault(x => x.seed == b.seed);
                bool share = b.easyFlagShare >= 0.08f && b.easyFlagShare <= 0.12f;
                bool cnt = b.easyAreaCount == b.easyAreaWant;
                bool size = b.easyAreaHa.Length > 0 && b.easyAreaHa.All(x => x >= EasyAreas.MIN_HA);
                bool g = math.abs(b.classShare[0] - b.gentleTarget) <= 0.05f && b.classShare[1] > 0f && b.classShare[2] > 0f;
                string build = a != null ? $"{b.buildMs / 1000:F1} ({a.buildMs / 1000:F1}, {(b.buildMs - a.buildMs) / 1000:+0.0;-0.0}) {((b.buildMs - a.buildMs) <= 2000 ? "PASS" : "MISS")}" : $"{b.buildMs / 1000:F1}";
                s.AppendLine($"  {b.seed,-6}  {Pct(b.easyFlagShare),6} {(share ? "PASS" : "MISS"),-12}  {b.easyAreaCount} ({b.easyAreaWant}) {(cnt ? "PASS" : "MISS")}     " +
                             $"{string.Join(" ", b.easyAreaHa.Select(x => x.ToString("F0"))),-22} {(size ? "PASS" : "MISS")}    {b.easyFlagNotEasy,5}                 " +
                             $"{Pct(b.classShare[0]),6} {(g ? "PASS" : "MISS")}   {b.inselbergs,3} {(b.inselbergs == 0 ? "PASS" : "MISS")}    {build}   {b.recordHash:X16}");
            }
            s.AppendLine("\n  areas: centre (voxels), ha, farthest flagged cell from the centre, mean height (rel. sea), dominant archetype");
            foreach (var b in v4) s.Append($"  seed {b.seed}:\n{b.easyAreaDesc}");
            s.AppendLine("\n  slopes (B3 targets, reported not gated)  v3 -> v4:");
            s.AppendLine("  seed    median deg      <15 deg          >45 deg          open (<20 m)     river km        gentle-low Walk anywhere (old 'easy')");
            foreach (var b in v4)
            {
                var a = v3.FirstOrDefault(x => x.seed == b.seed);
                string A(Func<Metrics, string> f) => a != null ? f(a) : "  -  ";
                s.AppendLine($"  {b.seed,-6}  {A(x => x.medianSlope.ToString("F1")),4} -> {b.medianSlope,4:F1}    {A(x => Pct(x.under15)),6} -> {Pct(b.under15),6}  " +
                             $"{A(x => Pct(x.over45)),6} -> {Pct(b.over45),6}  {A(x => Pct(x.open)),6} -> {Pct(b.open),6}  {A(x => x.riverKm.ToString("F1")),5} -> {b.riverKm,5:F1}    {Pct(b.easyShare)}");
            }
            return s.ToString();
        }

        /// The B5 acceptance table (plan rule 6; owner Q6 (a)): Medium 5
        /// features, <= 1 rare wonder, >= 1 way down, centres >= 1.2 km apart,
        /// >= 80% of land sees one within 3 km; B4 still green; slopes beside v3.
        private static string B5Table(List<Metrics> v3, List<Metrics> v4)
        {
            var s = new StringBuilder("=== B5: signature features on map v4 (pool draw, record list + FLAG_FEATURE)\n");
            s.AppendLine("  visibility: eyes on a 128 m grid of land (every 16th 8 m cell), 2 m above the map ground; per feature its targets");
            s.AppendLine("  (summit area + upper flanks / caldera rim + upper cone / mesa top edge / crater rim / canyon walls at mid-height), one ray each to the target");
            s.AppendLine("  surface + 1 m, sampled every 8 m cell against the ground or the water surface; seen = any ray clear within 3 km,");
            s.AppendLine("  or the eye stands on the feature's footprint. Eyes on a lake or river stand 2 m above its surface.");
            s.AppendLine("  seed    features (want)  rare  way down  min spacing (>= 1200 m)  sees one in 3 km (>= 80%)  hidden-near  easy share / areas   gentle  build s (v3)   record hash   checks");
            foreach (var b in v4)
            {
                if (!b.hasFeatures) continue;
                var a = v3.FirstOrDefault(x => x.seed == b.seed);
                bool cnt = b.featCount == b.featWant, rare = b.featRare <= 1, way = b.featWayDown >= 1, sep = b.featMinSepM >= 1200f, vis = b.featVisible >= 0.80f;
                bool easy = b.easyFlagShare >= 0.08f && b.easyFlagShare <= 0.12f && b.easyAreaCount == b.easyAreaWant && b.easyAreaHa.All(x => x >= EasyAreas.MIN_HA);
                string build = a != null ? $"{b.buildMs / 1000:F1} ({a.buildMs / 1000:F1}, {(b.buildMs - a.buildMs) / 1000:+0.0;-0.0}) {((b.buildMs - a.buildMs) <= 2000 ? "PASS" : "MISS")}" : $"{b.buildMs / 1000:F1}";
                s.AppendLine($"  {b.seed,-6}  {b.featCount} ({b.featWant}) {(cnt ? "PASS" : "MISS")}      {b.featRare} {(rare ? "PASS" : "MISS")}  {b.featWayDown} {(way ? "PASS" : "MISS")}    " +
                             $"{b.featMinSepM,5:F0} m {(sep ? "PASS" : "MISS")}               {Pct(b.featVisible),6} {(vis ? "PASS" : "MISS")}                {Pct(b.featHiddenNear),6}      " +
                             $"{Pct(b.easyFlagShare)} / {b.easyAreaCount} {(easy ? "PASS" : "MISS")}     {Pct(b.classShare[0]),6}  {build}   {b.recordHash:X16}   {(b.featFails.Length == 0 ? "all PASS" : b.featFails)}");
            }
            s.AppendLine("\n  features per seed (slot order; footprint = the record's FLAG_FEATURE cells):");
            foreach (var b in v4) if (b.hasFeatures) s.Append($"  seed {b.seed}: great peak {b.peakBroad}\n{b.featDesc}");
            s.AppendLine("\n  slopes by landform class, v3 -> v4 (reported, not gated):");
            s.AppendLine("  seed    gentle med deg   broken med deg   steep med deg    median deg      <15 deg          >45 deg          open (<20 m)     easy ground      easy areas");
            foreach (var b in v4)
            {
                var a = v3.FirstOrDefault(x => x.seed == b.seed);
                string A(Func<Metrics, string> f) => a != null ? f(a) : "  -  ";
                s.AppendLine($"  {b.seed,-6}  {A(x => x.classMedianSlope[0].ToString("F0")),3} -> {b.classMedianSlope[0],3:F0}       {A(x => x.classMedianSlope[1].ToString("F0")),3} -> {b.classMedianSlope[1],3:F0}       " +
                             $"{A(x => x.classMedianSlope[2].ToString("F0")),3} -> {b.classMedianSlope[2],3:F0}       {A(x => x.medianSlope.ToString("F1")),4} -> {b.medianSlope,4:F1}    " +
                             $"{A(x => Pct(x.under15)),6} -> {Pct(b.under15),6}  {A(x => Pct(x.over45)),6} -> {Pct(b.over45),6}  {A(x => Pct(x.open)),6} -> {Pct(b.open),6}  " +
                             $"{A(x => Pct(x.easyShare)),6} -> {Pct(b.easyShare),6}  {Pct(b.easyFlagShare)}");
            }
            return s.ToString();
        }

        /// The owner's pick (Q9): every v4 gentle share measured, with v3 beside.
        private static string GentleVariants(List<Metrics> v4, List<Metrics> v3)
        {
            var s = new StringBuilder("=== B2: gentle-share variants (owner Q9: 40 / 50 / 60%)\n");
            s.AppendLine("  seed    map          gentle  broken  steep   median  <15deg  >45deg  open    mount.  easy    inselbergs  build s  pngs");
            foreach (var m in v3.Concat(v4).OrderBy(x => x.seed).ThenBy(x => x.mapVersion).ThenBy(x => x.gentleTarget))
            {
                string name = m.mapVersion >= 4 ? $"v4 g{m.gentleTarget * 100:0}" : "v3";
                s.AppendLine($"  {m.seed,-6}  {name,-11}  {Pct(m.classShare[0]),6}  {Pct(m.classShare[1]),6}  {Pct(m.classShare[2]),6}  {m.medianSlope,5:F1}d  " +
                             $"{Pct(m.under15),6}  {Pct(m.over45),6}  {Pct(m.open),6}  {Pct(m.mountainous),6}  {Pct(m.easyShare),6}  {m.inselbergs,10}  {m.buildMs / 1000,7:F1}  seed{m.seed}{m.tag}_*.png");
            }
            return s.ToString();
        }

        /// The B1 acceptance table (owner Q5, 2026-10-03): v4's biome change
        /// walking east-west must be >= 2.5x the same seed's v3 value.
        private static string B1Table(List<Metrics> v3, List<Metrics> v4)
        {
            var s = new StringBuilder("=== B1: map v4 (biome x3 + world record) against v3, same run\n");
            s.AppendLine("  seed    biome m v3 -> v4   ratio  (>= 2.5x)   patch median ha v3 -> v4   area-wtd ha v3 -> v4   >=1ha median v3 -> v4   build s v3 -> v4 (delta)  record B / ms");
            foreach (var a in v3)
            {
                var b = v4.FirstOrDefault(x => x.seed == a.seed);
                if (b == null) continue;
                float ratio = b.biomeChangeM / a.biomeChangeM;
                s.AppendLine($"  {a.seed,-6}  {a.biomeChangeM,6:F0} -> {b.biomeChangeM,6:F0}  {ratio,5:F2}  {(ratio >= 2.5f ? "PASS" : "MISS"),-9}   " +
                             $"{a.biomePatchMedianHa,7:F1} -> {b.biomePatchMedianHa,7:F1}        {a.biomePatchAreaMedianHa,6:F0} -> {b.biomePatchAreaMedianHa,6:F0}        " +
                             $"{a.biomePatchMedian1HaHa,6:F1} -> {b.biomePatchMedian1HaHa,6:F1}        {a.buildMs / 1000:F1} -> {b.buildMs / 1000:F1} ({(b.buildMs - a.buildMs) / 1000:+0.0;-0.0})   " +
                             $"{b.recordBytes} / {b.recordMs:F0}  hash {b.recordHash:X16}  record biome != ShapeField.Biome: {b.recordBiomeMismatch}");
            }
            s.AppendLine("\n  biome scale sweep (v4 map, biomeScale overridden; x1 is v3's biome field):");
            s.AppendLine("  seed    scale  biome m  patches  median ha  area-wtd ha  >=1ha median ha");
            foreach (var b in v4)
                foreach (var (sc, st) in b.scales)
                    s.AppendLine($"  {b.seed,-6}  x{sc,-4:0.#}  {st.changeM,7:F0}  {st.patches,7}  {st.medianHa,9:F1}  {st.areaMedianHa,11:F0}  {st.median1HaHa,15:F1}");
            return s.ToString();
        }

        /// Biome change walking east-west and land patch sizes for one biome grid.
        private static BiomeStats Biomes(int n, bool[] isLand, byte[] biome, float cellM)
        {
            var st = new BiomeStats();
            long walked = 0; int changes = 0;
            for (int z = 0; z < n; z += 4)
            for (int x = 0; x + 1 < n; x++)
            {
                int a = z * n + x;
                if (!isLand[a] || !isLand[a + 1]) continue;
                walked++;
                if (biome[a] != biome[a + 1]) changes++;
            }
            st.changeM = changes > 0 ? walked * cellM / changes : float.PositiveInfinity;
            var patches = Components(n, i => isLand[i], (a, b) => biome[a] == biome[b]);
            float haPerCell = cellM * cellM / 10000f;
            st.patches = patches.Count;
            var patchHa = patches.Select(p => p.Count * haPerCell).OrderBy(v => v).ToList();
            st.medianHa = patchHa.Count > 0 ? patchHa[patchHa.Count / 2] : 0f;
            st.areaMedianHa = AreaWeightedMedian(patchHa);
            var big = patchHa.Where(v => v >= 1f).ToList();
            st.median1HaHa = big.Count > 0 ? big[big.Count / 2] : 0f;
            return st;
        }

        // ------------------------------------------------------------------ measure

        private static Metrics Measure(uint seed, int mapVersion, float gentle, float[] biomeScales, string dir)
        {
            var spec = TerrainSpec.SizeClass2(seed, mapVersion);
            if (spec.mapVersion >= 4 && gentle > 0f && gentle != TerrainSpec.V4_GENTLE_SHARE) spec.gentleShare = gentle;
            bool defaultV4 = spec.mapVersion >= 4 && spec.gentleShare == 0f;
            string tag = spec.mapVersion == 3 ? "" : $"_v{spec.mapVersion}" + (spec.gentleShare > 0f ? $"_g{math.round(spec.gentleShare * 100f):0}" : "");
            var m = new Metrics { seed = seed, mapVersion = spec.mapVersion, biomeScale = spec.BiomeScale, gentleTarget = spec.GentleShare, tag = tag };
            var o = Offsets.For(in spec);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var map = WorldMap.Build(spec);
            m.buildMs = sw.Elapsed.TotalMilliseconds;
            m.inselbergs = map.InselbergCount;
            if (map.Record != null)
            {
                // the record's critical-path cost (its biome plane runs beside
                // BuildV3), and a second build that must match it
                var plane = WorldRecord.BiomePlane(spec);
                sw.Restart();
                var again = WorldRecord.Build(map, plane);
                m.recordMs = sw.Elapsed.TotalMilliseconds;
                m.recordBytes = map.Record.Bytes;
                m.recordHash = map.Record.Hash();
                Assert.AreEqual(m.recordHash, again.Hash(), "the world record must rebuild identically");
            }
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
            if (map.Record != null)
                for (int i = 0; i < n * n; i++) if (map.Record.Data[i].Biome != biome[i]) m.recordBiomeMismatch++;

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

            // B4: the record's easy areas (flag + list); each flagged cell is
            // re-checked against this report's own definition (atan slope).
            bool[] easyFlag = null;
            if (map.Record != null)
            {
                easyFlag = new bool[n * n];
                int flagged = 0;
                for (int i = 0; i < n * n; i++)
                {
                    if (!map.Record.Data[i].IsEasy) continue;
                    easyFlag[i] = true;
                    if (isLand[i]) flagged++;
                    if (!isLand[i] || !easyMask[i] || map.WaterClass[i] != Hydrology.DRY) m.easyFlagNotEasy++;
                }
                m.easyFlagShare = flagged / L;
                var areas = map.Record.EasyAreaList;
                m.easyAreaWant = EasyAreas.CountFor(in spec);
                m.easyAreaHa = areas.Select(a => a.hectares).ToArray();
                m.easyAreaCount = areas.Count(a => a.cellCount > 0);
                var d = new StringBuilder();
                foreach (var a in areas)
                {
                    var votes = new int[256];
                    foreach (int i in a.cells) votes[map.Region[i]]++;
                    int best = 0; for (int k = 1; k < 256; k++) if (votes[k] > votes[best]) best = k;
                    d.AppendLine($"    ({a.centre.x}, {a.centre.y})  {a.hectares,5:F1} ha  r {a.radiusM,4:F0} m  {a.meanHeightM,5:F1} m  {(best == 255 ? "sea" : Regions.Name((byte)best))}");
                }
                m.easyAreaDesc = d.ToString();
            }
            // B5: the signature features (FeatureTests' acceptance, the shot list)
            byte[] featKind = null, featEye = null;
            if (map.SignatureFeatures != null && map.Record != null)
            {
                var ff = map.SignatureFeatures;
                m.hasFeatures = true;
                m.featFails = string.Join("; ", FeatureTests.Check(map, out m.featNumbers, out var vis));
                m.featCount = ff.Length; m.featWant = Features.CountFor(in spec);
                m.featRare = ff.Count(x => x.tier == Features.Rare); m.featWayDown = ff.Count(x => x.wayDown);
                m.featVisible = vis.Share; m.featHiddenNear = vis.HiddenNear;
                m.featMinSepM = float.MaxValue;
                for (int a = 0; a < ff.Length; a++)
                for (int b = a + 1; b < ff.Length; b++)
                    m.featMinSepM = math.min(m.featMinSepM, math.distance((float2)ff[a].centre, (float2)ff[b].centre) / 10f);
                featKind = new byte[n * n];
                featEye = new byte[n * n];   // 1 an eye that sees a feature within 3 km, 2 one that sees none
                for (int e = 0; e < vis.eyeCell.Count; e++)
                {
                    int ec = vis.eyeCell[e], ex = ec % n, ez = ec / n;
                    for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++)
                    {
                        int xx = ex + dx, zz = ez + dz;
                        if (xx >= 0 && zz >= 0 && xx < n && zz < n) featEye[zz * n + xx] = (byte)(vis.eyeSees[e] ? 1 : 2);
                    }
                }
                for (int i = 0; i < n * n; i++) { var c = map.Record.Data[i]; if (c.IsFeature) featKind[i] = ff[c.site - 1].kind; }
                var d = new StringBuilder();
                var shots = new StringBuilder("# B5 shots (metres): name eyeX eyeZ lookX lookZ -- eye on dry land, 2 m eye, most targets in sight\n");
                for (int k = 0; k < ff.Length; k++)
                {
                    var f = ff[k];
                    d.AppendLine($"    [{k + 1}] {Features.Name(f.kind),-12} {(f.tier == Features.Common ? "common" : f.tier == Features.Uncommon ? "uncommon" : "RARE"),-8} " +
                                 $"centre ({f.centre.x / 10f:F0}, {f.centre.y / 10f:F0}) m  footprint r {f.radiusM:F0} m  {f.cellCount} cells ({f.cellCount * 0.0064f:F0} ha)" +
                                 $"{(f.wayDown ? "  WAY DOWN" : "")}  seen by {vis.seenBy[k]} of {vis.eyes} eyes" +
                                 (f.kind == Features.Mesa ? $"  top {(f.baseH + f.rise) / 10f:F0} m (rise {f.rise / 10f:F0} m)" :
                                  f.kind == Features.CraterLake ? $"  bowl r {f.R / 10f:F0} m depth {f.B / 10f:F0} m rim {f.rise / 10f:F0} m" :
                                  f.kind == Features.GreatCanyon ? $"  length {(f.path.Length - 1) * Features.CANYON_STEP / 10f:F0} m, floor {f.floor[0] / 10f:F0} -> {f.floor[f.floor.Length - 1] / 10f:F0} m" : ""));
                    if (Shot(map, f, 20000f, out float2 eye, out int seen))
                        shots.AppendLine($"f{k + 1}_{Features.Name(f.kind).Replace(' ', '_')} {eye.x / 10f:F1} {eye.y / 10f:F1} {f.centre.x / 10f:F1} {f.centre.y / 10f:F1}  # {seen} targets in sight, 2 km");
                    else shots.AppendLine($"# f{k + 1} {Features.Name(f.kind)}: no dry vantage 2 km out with a target in sight");
                }
                // the far-band shot: the tallest raised feature from ~4.5 km
                var tall = ff.Where(x => x.kind == Features.GreatPeak).Concat(ff.Where(x => x.kind == Features.Volcano)).Concat(ff.Where(x => x.kind == Features.Mesa)).FirstOrDefault();
                if (tall != null && Shot(map, tall, 45000f, out float2 fe, out int fs))
                    shots.AppendLine($"far_{Features.Name(tall.kind).Replace(' ', '_')} {fe.x / 10f:F1} {fe.y / 10f:F1} {tall.centre.x / 10f:F1} {tall.centre.y / 10f:F1}  # {fs} targets in sight, 4.5 km");
                m.featDesc = d.ToString(); m.featShots = shots.ToString();
                var pk = ff.FirstOrDefault(x => x.kind == Features.GreatPeak);
                if (pk != null) m.peakBroad = PeakBroadness(map, pk);
            }

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
            var bigPatches = patchHa.Where(v => v >= 1f).ToList();
            m.biomePatchMedian1HaHa = bigPatches.Count > 0 ? bigPatches[bigPatches.Count / 2] : 0f;

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

            WritePngs(dir, $"seed{seed}{tag}", n, h, isLand, slope, relief, biome, map.Region, map.WaterClass, easyMask, towns, easyFlag, featKind, featEye);

            // B1: the biome field at other scales on this same map (the map does
            // not depend on biomes), as numbers and one strip: v3 (x1), then each.
            if (defaultV4)
            {
                var grids = new List<byte[]> { BiomeGrid(spec, 1f) };
                m.scales.Add((1f, Biomes(n, isLand, grids[0], cellM)));
                foreach (float sc in biomeScales)
                {
                    var g = BiomeGrid(spec, sc);
                    grids.Add(g);
                    m.scales.Add((sc, Biomes(n, isLand, g, cellM)));
                }
                WriteStrip(dir, $"seed{seed}_biome_scales.png", n, h, isLand, map.WaterClass,
                           grids.Select(g => (Func<int, Color>)(i => BiomeCol[g[i]])).ToArray());
            }
            // PLAYMETRICS_DUMP=1: the map behind these numbers, for offline checks
            // (int n; float height[n*n] in voxels rel. sea; byte region, water class, biome [n*n] each).
            if (Environment.GetEnvironmentVariable("PLAYMETRICS_DUMP") == "1")
                using (var f = new BinaryWriter(File.Create(Path.Combine(dir, $"seed{seed}{tag}_map.bin"))))
                {
                    f.Write(n);
                    foreach (float x in h) f.Write(x);
                    f.Write(map.Region); f.Write(map.WaterClass); f.Write(biome);
                }
            return m;
        }

        private static byte[] BiomeGrid(TerrainSpec spec, float scale)
        {
            spec.biomeScale = scale;
            var o = Offsets.For(in spec);
            int n = spec.mapCells;
            var g = new byte[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                g[z * n + x] = ShapeField.Biome(in o, new float2((x + 0.5f) * spec.mapCellVoxels, (z + 0.5f) * spec.mapCellVoxels));
            return g;
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
            string ver = m.mapVersion >= 4 ? $"map v{m.mapVersion}, biome x{m.biomeScale:0.#}" : $"map v{m.mapVersion}";
            s.AppendLine($"=== seed {m.seed} (sizeClass 2, {ver}): map built in {m.buildMs / 1000:F1} s, measured in {m.measureMs / 1000:F1} s");
            s.AppendLine($"  land {m.landKm2:F2} km^2; land height range 0..{m.lowThirdM * 3:F0} m, lowest third under {m.lowThirdM:F0} m");
            s.AppendLine($"  slope (8 m): median {m.medianSlope:F1} deg; under 15 deg {Pct(m.under15)}; steeper than 45 deg {Pct(m.over45)}");
            s.AppendLine($"  bands: Walk {Pct(m.bandShare[0])}, Scramble {Pct(m.bandShare[1])}, Climb {Pct(m.bandShare[2])}, Sheer {Pct(m.bandShare[3])}  (provisional 15/30/45 deg)");
            s.AppendLine($"  relief in a 250 m square: open (< 20 m) {Pct(m.open)}, mountainous (> 80 m) {Pct(m.mountainous)}");
            if (m.mapVersion >= 4)
                s.AppendLine($"  B2: gentle-class share target {Pct(m.gentleTarget)} of land (broken : steep 3 : 2 of the rest); inselbergs {m.inselbergs}");
            else
                s.AppendLine($"  inselbergs (T6): {m.inselbergs}");
            s.AppendLine($"  landform class share of land: gentle {Pct(m.classShare[0])}, broken {Pct(m.classShare[1])}, steep {Pct(m.classShare[2])}; " +
                         $"median slope {m.classMedianSlope[0]:F0} / {m.classMedianSlope[1]:F0} / {m.classMedianSlope[2]:F0} deg");
            s.AppendLine("  by region (share of land, median slope): " + string.Join(", ",
                Enumerable.Range(0, Regions.KINDS).Where(k => m.regionShare[k] > 0f).OrderBy(k => m.regionMedianSlope[k])
                    .Select(k => $"{Regions.Name((byte)k)} {Pct(m.regionShare[k])} {m.regionMedianSlope[k]:F0}deg")));
            s.AppendLine($"  easy ground (Walk + gentle + lowest third): {Pct(m.easyShare)} of land (gentle + lowest third, any slope: {Pct(m.gentleLowShare)})");
            if (m.mapVersion >= 4)
                s.AppendLine($"  B4 easy areas (record flag): {Pct(m.easyFlagShare)} of land in {m.easyAreaCount} areas (want {m.easyAreaWant}): " +
                             $"{string.Join(", ", m.easyAreaHa.Select(x => x.ToString("F0") + " ha"))}; flagged cells failing the definition {m.easyFlagNotEasy}");
            if (m.hasFeatures)
                s.AppendLine($"  B5 signature features: {m.featCount} (want {m.featWant}), way down {m.featWayDown}, rare {m.featRare}; min spacing {m.featMinSepM:F0} m; " +
                             $"land seeing one within 3 km {Pct(m.featVisible)} (hidden-near {Pct(m.featHiddenNear)}); {(m.featFails.Length == 0 ? "checks PASS" : "FAILS: " + m.featFails)}");
            s.AppendLine($"  biome change walking east-west every {m.biomeChangeM:F0} m (sampled every 128 m: {m.biomeChange128M:F0} m); region change every {m.regionChangeM:F0} m");
            s.AppendLine($"  biome patches on land: {m.biomePatches}; median {m.biomePatchMedianHa:F1} ha; area-weighted median {m.biomePatchAreaMedianHa:F0} ha");
            if (m.mapVersion >= 4)
                s.AppendLine($"  biome patches of 1 ha+: median {m.biomePatchMedian1HaHa:F1} ha; world record {m.recordBytes} bytes, after the map (biome plane beside BuildV3) {m.recordMs:F0} ms, hash {m.recordHash:X16}");
            s.AppendLine($"  town sites (land under {TOWN_SLOPE:F0} deg, {TOWN_HA:F0} ha+): {m.towns}  [{m.townRegions}]");
            s.AppendLine($"     sweep of the slope threshold: {m.townSweep}");
            s.AppendLine($"  landmarks: {m.landmarks}; land with one in all 4 directions within 3 km {Pct(m.landmarkAll4At3)} (within 1.5 km {Pct(m.landmarkAll4At15)})");
            s.AppendLine($"  cave mouths on land: {m.caveMouths} ({m.mouthsPerKm2:F2} per km^2); river {m.riverKm:F1} km (river cells x 8 m); lakes {m.lakes}");
            return s.ToString();
        }

        private static string Summary(List<Metrics> all)
        {
            var s = new StringBuilder($"\n=== baselines (v{(all.Count > 0 ? all[0].mapVersion : 3)})\n");
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

        private static readonly Color[] BiomeCol = { new Color(0.25f, 0.55f, 0.25f), new Color(0.92f, 0.80f, 0.50f), new Color(0.92f, 0.94f, 0.98f), new Color(0.10f, 0.45f, 0.30f) };

        private static void WritePngs(string dir, string prefix, int n, float[] h, bool[] isLand, float[] slope, float[] relief,
                                      byte[] biome, byte[] region, byte[] water, bool[] easy, List<List<int>> towns, bool[] easyFlag, byte[] featKind = null, byte[] featEye = null)
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
            var biomeCol = BiomeCol;
            Write(dir, $"{prefix}_landform_class.png", n, h, isLand, water, i => region[i] == 255 ? Color.gray : classCol[LandformClass(region[i])]);
            Write(dir, $"{prefix}_slope_bands.png", n, h, isLand, water, i => bandCol[Band(slope[i])]);
            Write(dir, $"{prefix}_relief.png", n, h, isLand, water,
                  i => relief[i] < OPEN_M * 10f ? new Color(0.45f, 0.80f, 0.40f) : relief[i] > MOUNTAIN_M * 10f ? new Color(0.60f, 0.45f, 0.45f) : new Color(0.85f, 0.80f, 0.55f));
            Write(dir, $"{prefix}_easy_ground.png", n, h, isLand, water,
                  i => TownEdge(i) ? new Color(0.95f, 0.20f, 0.85f) : easy[i] ? new Color(0.30f, 0.85f, 0.30f) : new Color(0.62f, 0.62f, 0.62f));
            Write(dir, $"{prefix}_biome.png", n, h, isLand, water, i => biomeCol[biome[i]]);
            // B4: the record's easy cells (bright yellow) over the region-class map
            if (easyFlag != null)
                Write(dir, $"{prefix}_easy_areas.png", n, h, isLand, water,
                      i => easyFlag[i] ? new Color(1f, 0.92f, 0.10f) : region[i] == 255 ? Color.gray : classCol[LandformClass(region[i])] * 0.8f);
            // B5: the feature footprints (FeatCol by kind) and the easy areas (yellow) over the region-class map
            if (featKind != null)
                Write(dir, $"{prefix}_features.png", n, h, isLand, water,
                      i => featKind[i] != 0 ? FeatCol[featKind[i]] : easyFlag != null && easyFlag[i] ? new Color(1f, 0.92f, 0.10f)
                         : region[i] == 255 ? Color.gray : classCol[LandformClass(region[i])] * 0.7f);
            // B5: the visibility rule's eyes -- green sees a feature within 3 km, red sees none
            if (featKind != null && featEye != null)
                Write(dir, $"{prefix}_feature_view.png", n, h, isLand, water,
                      i => featEye[i] == 1 ? new Color(0.20f, 0.85f, 0.25f) : featEye[i] == 2 ? new Color(0.95f, 0.10f, 0.10f)
                         : featKind[i] != 0 ? FeatCol[featKind[i]] : new Color(0.55f, 0.55f, 0.55f));
        }

        /// B5 footprint colours: peak white, canyon red, mesa brown, volcano black, crater lake cyan.
        private static readonly Color[] FeatCol =
        {
            Color.gray, new Color(0.97f, 0.97f, 0.97f), new Color(0.90f, 0.12f, 0.10f), new Color(0.62f, 0.36f, 0.18f),
            new Color(0.12f, 0.10f, 0.12f), new Color(0.15f, 0.85f, 0.90f),
        };

        /// A dry vantage `dist` voxels from the feature (24 directions) with a
        /// 2 m eye that sees the most of its targets; the canyon from its middle.
        private static bool Shot(WorldMap map, Feature f, float dist, out float2 eye, out int seen)
        {
            eye = default; seen = 0;
            var tg = Features.Targets(f, map);
            int n = map.Spec.mapCells, cv = map.Spec.mapCellVoxels;
            for (int a = 0; a < 24; a++)
            {
                float ang = a * 2f * math.PI / 24f;
                float2 e = (float2)f.centre + new float2(math.cos(ang), math.sin(ang)) * dist;
                int x = (int)(e.x / cv), z = (int)(e.y / cv);
                if (x < 1 || z < 1 || x >= n - 1 || z >= n - 1) continue;
                int i = z * n + x;
                if (map.Height[i] <= 50f || map.WaterClass[i] != Hydrology.DRY || map.Record.Data[i].IsFeature) continue;
                int c = 0;
                foreach (var t in tg)
                {
                    int tx = math.clamp((int)(t.x / cv), 0, n - 1), tz = math.clamp((int)(t.y / cv), 0, n - 1);
                    if (Features.LineOfSight(map, e, map.Height[i] + 20f, t, Features.Surface(map, tz * n + tx) + 10f)) c++;
                }
                if (c > seen) { seen = c; eye = e; }
            }
            return seen > 0;
        }

        /// "Signature peaks stay broad": the summit, and the mean ground 150 /
        /// 300 / 600 m out (16 directions), in metres above sea.
        private static string PeakBroadness(WorldMap map, Feature pk)
        {
            var tg = Features.Targets(pk, map);
            float2 top = tg[0];
            int n = map.Spec.mapCells, cv = map.Spec.mapCellVoxels;
            float H(float2 p) { int x = math.clamp((int)(p.x / cv), 0, n - 1), z = math.clamp((int)(p.y / cv), 0, n - 1); return map.Height[z * n + x] / 10f; }
            string Ring(float r)
            {
                float sum = 0f;
                for (int a = 0; a < 16; a++) { float ang = a * 2f * math.PI / 16f; sum += H(top + new float2(math.cos(ang), math.sin(ang)) * r); }
                return (sum / 16f).ToString("F0");
            }
            return $"summit {H(top):F0} m, mean ground 150/300/600 m out {Ring(1500f)}/{Ring(3000f)}/{Ring(6000f)} m";
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
                px[pz * PNG + pxX] = Pixel(n, h, isLand, water, tint, pxX * f, pz * f);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static Color Pixel(int n, float[] h, bool[] isLand, byte[] water, Func<int, Color> tint, int x, int z)
        {
            int i = z * n + x;
            if (!isLand[i]) return new Color(0.10f, 0.18f, 0.35f);
            if (water[i] != Hydrology.DRY) return new Color(0.35f, 0.60f, 0.90f);
            int x1 = math.min(x + 1, n - 1), z1 = math.min(z + 1, n - 1);
            float gx = (h[z * n + x1] - h[i]) / 80f, gz = (h[z1 * n + x] - h[i]) / 80f;
            var nrm = math.normalize(new float3(-gx, 1f, -gz));
            float shade = math.clamp(0.35f + 0.75f * math.dot(nrm, math.normalize(new float3(-0.5f, 0.75f, 0.45f))), 0.3f, 1.15f);
            return tint(i) * shade;
        }

        /// Several top-down panels of the same map side by side, 16 px white gap.
        private static void WriteStrip(string dir, string name, int n, float[] h, bool[] isLand, byte[] water, Func<int, Color>[] tints)
        {
            const int GAP = 16;
            int f = n / PNG, w = tints.Length * PNG + (tints.Length - 1) * GAP;
            var tex = new Texture2D(w, PNG, TextureFormat.RGB24, false);
            var px = new Color32[w * PNG];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            for (int t = 0; t < tints.Length; t++)
            for (int pz = 0; pz < PNG; pz++)
            for (int pxX = 0; pxX < PNG; pxX++)
                px[pz * w + t * (PNG + GAP) + pxX] = Pixel(n, h, isLand, water, tints[t], pxX * f, pz * f);
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
