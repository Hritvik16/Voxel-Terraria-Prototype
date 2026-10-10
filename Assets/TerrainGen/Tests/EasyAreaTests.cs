// Assets/TerrainGen/Tests/EasyAreaTests.cs
//
// Surface rebuild B4 (plan rule 3; owner Q1 (a) Small 3 / Medium 4 / Large 6,
// Q2 (a) land only): easy areas seeded before shaping, flagged in the world
// record with their list.
//   Seed 42         count = the size's (Medium 4), every area >= 25 ha, the
//                   flagged share 8-12% of land, the flag is exactly the list's
//                   cells, every flagged cell is easy ground, a v3 map has none.
//   20-seed sweep   the same three numbers on 20 seeds, map only. ~8 min, so
//                   opt-in (EASYSWEEP=1 tools/run-editmode-filter.sh EasyAreaTests);
//                   writes Logs/TerrainGen/easysweep.txt and FAILS naming every
//                   failing seed.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace TerrainGen.Tests
{
    public class EasyAreaTests
    {
        public static readonly uint[] SweepSeeds =
            { 42u, 7u, 2026u, 31337u, 1u, 2u, 3u, 4u, 5u, 6u, 8u, 9u, 10u, 11u, 12u, 13u, 99u, 123u, 4242u, 65537u };

        /// Every failure of one map, empty when it passes; the numbers either way.
        public static List<string> Check(WorldMap map, out string numbers)
        {
            var fail = new List<string>();
            var rec = map.Record;
            int n = map.Spec.mapCells, land = 0, flagged = 0, notEasy = 0;
            float hi = 0f;
            foreach (float v in map.Height) if (v > 0f) { land++; hi = System.Math.Max(hi, v); }
            var inList = new bool[n * n];
            foreach (var a in rec.EasyAreaList) foreach (int i in a.cells) inList[i] = true;
            int mismatch = 0;
            for (int i = 0; i < n * n; i++)
            {
                if (rec.Data[i].IsEasy != inList[i]) mismatch++;
                if (!rec.Data[i].IsEasy) continue;
                flagged++;
                if (!EasyAreas.IsEasyGround(map, i, hi / 3f)) notEasy++;
            }
            int want = EasyAreas.CountFor(in map.Spec);
            float share = (float)flagged / land;
            var ha = rec.EasyAreaList.Select(a => a.hectares).ToArray();
            numbers = $"share {share * 100f:F1}%  areas {ha.Length} (want {want})  ha {string.Join(" ", ha.Select(x => x.ToString("F0")))}";
            if (rec.MapVersion != map.Spec.mapVersion) fail.Add($"record mapVersion {rec.MapVersion} != spec {map.Spec.mapVersion}");
            if (ha.Length != want) fail.Add($"{ha.Length} easy areas, want {want}");
            foreach (float x in ha) if (x < EasyAreas.MIN_HA) fail.Add($"an area of {x:F1} ha (< {EasyAreas.MIN_HA} ha)");
            if (share < 0.08f || share > 0.12f) fail.Add($"easy share {share * 100f:F1}% outside 8-12%");
            if (mismatch != 0) fail.Add($"{mismatch} cells where the flag and the list disagree");
            if (notEasy != 0) fail.Add($"{notEasy} flagged cells are not easy ground");
            return fail;
        }

        [Test]
        [Category("Slow")]   // the shared v4 seed-42 map
        public void V4Seed42_EasyAreas_CountSizeShare()
        {
            var map = SharedMaps.Get(TerrainSpec.SizeClass2(42u, 4));
            UnityEngine.Debug.Log($"[EasyAreas] seed 42 pick: {EasyAreas.LastPick}");
            Assert.AreEqual(4, EasyAreas.CountFor(in map.Spec), "sizeClass 2 is Medium: 4 areas (Q1 (a))");
            var fail = Check(map, out string numbers);
            UnityEngine.Debug.Log($"[EasyAreas] seed 42 v4: {numbers}");
            Assert.IsEmpty(fail, "seed 42: " + string.Join("; ", fail));
            Assert.IsNull(SharedMaps.Get(TerrainSpec.SizeClass2(42u, 3)).EasySites, "a v3 map seeds no easy areas");
        }

        [Test]
        [Timeout(3600000)]
        public void Sweep20_EasyAreas_MapOnly()
        {
            if (System.Environment.GetEnvironmentVariable("EASYSWEEP") != "1")
                Assert.Ignore("opt-in, ~8 min: EASYSWEEP=1 tools/run-editmode-filter.sh EasyAreaTests");
            var sb = new StringBuilder("=== B4 20-seed map-only sweep (v4): count, sizes (>= 25 ha), share 8-12%\n");
            var failed = new List<string>();
            uint[] seeds = SweepSeeds;
            string env = System.Environment.GetEnvironmentVariable("EASYSWEEP_SEEDS");
            if (!string.IsNullOrEmpty(env)) seeds = env.Split(',').Select(x => uint.Parse(x.Trim())).ToArray();
            foreach (uint seed in seeds)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var map = WorldMap.Build(TerrainSpec.SizeClass2(seed, 4));
                var fail = Check(map, out string numbers);
                sb.AppendLine($"  seed {seed,-6} {(fail.Count == 0 ? "PASS" : "FAIL")}  {numbers}  build {sw.Elapsed.TotalSeconds:F1} s" +
                              (fail.Count > 0 ? "  -- " + string.Join("; ", fail) : ""));
                sb.AppendLine($"      pick: {EasyAreas.LastPick}\n      mark: {EasyAreas.LastMark}");
                if (fail.Count > 0) failed.Add($"{seed}: {string.Join("; ", fail)}");
            }
            sb.AppendLine($"  {seeds.Length - failed.Count} of {seeds.Length} pass");
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "easysweep.txt"), sb.ToString());
            UnityEngine.Debug.Log(sb.ToString());
            Assert.IsEmpty(failed, $"{failed.Count} of {seeds.Length} seeds fail:\n" + string.Join("\n", failed));
        }
    }
}
