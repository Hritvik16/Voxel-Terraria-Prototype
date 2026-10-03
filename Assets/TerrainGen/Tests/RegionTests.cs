// Assets/TerrainGen/Tests/RegionTests.cs
//
// TerrainGen v3, T1 + T2 (design doc §13): regions and erosion.
//   Variety      every one of four seeds has >= 7 distinct archetypes on land
//                ("one island, lots of varying topographies")
//   Determinism  two builds of the same seed are identical, cell for cell
//   Report       per seed: build time, erosion stats, each region's archetype,
//                area and height range; and Logs/TerrainGen/regions/seed_N.bin
//                (int32 n, then n*n float32 heights rel. sea, then n*n region
//                bytes) for tools/region-map.py's relief map.
//                The maps come from SharedMaps (built once per run). With
//                REGIONS_EROSION_DUMP=1 each seed is built fresh instead, so the
//                erosion inputs can be written to Logs/TerrainGen/regions/erosion_in_N.bin.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGen.Tests
{
    public class RegionTests
    {
        private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };

        [Test]
        [Category("Slow")]   // > 10 s: SUITE=all only (task 11)
        public void Regions_VarietyAndReport()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "regions");
            Directory.CreateDirectory(dir);
            var lines = new List<string>();
            bool dump = System.Environment.GetEnvironmentVariable("REGIONS_EROSION_DUMP") == "1";
            if (dump) WorldMap.DumpErosionInputsTo = Path.Combine(dir, "erosion_in");
            foreach (uint seed in Seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var shared = dump ? null : SharedMaps.GetEntry(spec);
                var map = dump ? WorldMap.Build(spec) : shared.Map;
                double ms = dump ? sw.Elapsed.TotalMilliseconds : shared.BuildMs;
                var er = dump ? WorldMap.LastErosion : shared.ErosionStats;
                int n = spec.mapCells;
                var area = new int[Regions.KINDS]; var lo = new float[Regions.KINDS]; var hi = new float[Regions.KINDS];
                for (int k = 0; k < Regions.KINDS; k++) { lo[k] = float.MaxValue; hi[k] = float.MinValue; }
                for (int i = 0; i < n * n; i++)
                {
                    byte k = map.Region[i];
                    if (k == 255 || map.Height[i] <= 0f) continue;
                    area[k]++; lo[k] = math.min(lo[k], map.Height[i]); hi[k] = math.max(hi[k], map.Height[i]);
                }
                int kinds = 0; for (int k = 0; k < Regions.KINDS; k++) if (area[k] > 400) kinds++;
                lines.Add($"seed {seed}: map v{spec.mapVersion} built in {ms:F0} ms; erosion {er.iterations} iterations, max cut {er.maxCut / 10f:F0} m, {er.outlets} outlets; " +
                          $"{map.RegionLayout.Count} regions, {kinds} distinct archetypes on land; lakes {map.Hydro.lakes}, river cells {map.Hydro.riverCells}");
                for (int s = 0; s < map.RegionLayout.Count; s++)
                    lines.Add($"   region {s,2}: {Regions.Name(map.RegionLayout.kind[s]),-16} at {map.RegionLayout.seeds[s]}");
                for (int k = 0; k < Regions.KINDS; k++)
                    if (area[k] > 0) lines.Add($"   {Regions.Name((byte)k),-16} {area[k] * 0.000064f,6:F2} km^2   {lo[k] / 10f,5:F0} .. {hi[k] / 10f,4:F0} m");
                using (var f = new BinaryWriter(File.Create(Path.Combine(dir, $"seed_{seed}.bin"))))
                {
                    f.Write(n);
                    foreach (float v in map.Height) f.Write(v);
                    f.Write(map.Region);
                }
                if (dump) map.Dispose();
                Assert.GreaterOrEqual(kinds, 7, $"seed {seed}: only {kinds} archetypes on land");
            }
            WorldMap.DumpErosionInputsTo = null;
            File.WriteAllLines(Path.Combine(dir, "regions.txt"), lines);
            Debug.Log("[Regions]\n" + string.Join("\n", lines));
        }

        [Test]
        [Category("Slow")]   // > 10 s: SUITE=all only (task 11)
        public void MapV3_IsDeterministic()
        {
            var spec = TerrainSpec.SizeClass2(7u);
            var a = SharedMaps.Get(spec);       // one build (shared, built earlier in the run) ...
            using var b = WorldMap.Build(spec); // ... and a second, fresh one
            int diff = 0;
            for (int i = 0; i < a.Height.Length; i++) if (a.Height[i] != b.Height[i] || a.Region[i] != b.Region[i]) diff++;
            Assert.AreEqual(0, diff, "two builds of the same seed differ");
        }
    }
}
