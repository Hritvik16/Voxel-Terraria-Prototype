// Assets/TerrainGen/Tests/BiomeLayoutDump.cs
//
// Task 176 (C1) tooling, report only (REPORTS=1): for the 8 layout seeds,
// writes each map's land mask, the record's biome plane and the climate
// offsets to Logs/TerrainGen/c1/, so tools/biome176.py can draw the before |
// after maps and measure the layout statistics outside Unity.
using System.IO;
using NUnit.Framework;

namespace TerrainGen.Tests
{
    public class BiomeLayoutDump
    {
        public static readonly uint[] LayoutSeeds = { 42u, 7u, 2026u, 31337u, 1u, 2u, 3u, 99u };

        [Test]
        [Category("Slow")]
        public void Report_DumpBiomeLayout()
        {
            ReportOnly.Require();
            string tag = System.Environment.GetEnvironmentVariable("C1TAG") ?? "now";
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen", "c1", tag);
            Directory.CreateDirectory(dir);
            var meta = new System.Text.StringBuilder();
            // C1SEEDS=sweep: also EasyAreaTests.SweepSeeds, to check a layout beyond the 8 it is accepted on
            var seeds = new System.Collections.Generic.List<uint>(LayoutSeeds);
            if (System.Environment.GetEnvironmentVariable("C1SEEDS") == "sweep")
                foreach (uint s in EasyAreaTests.SweepSeeds) if (!seeds.Contains(s)) seeds.Add(s);
            foreach (uint seed in seeds)
            {
                var spec = TerrainSpec.SizeClass2(seed);
                var map = SharedMaps.Get(spec);
                int n = spec.mapCells;
                var land = new byte[n * n];
                var biome = new byte[n * n];
                var shade = new byte[n * n];
                for (int i = 0; i < n * n; i++)
                {
                    float h = map.Height[i];
                    land[i] = (byte)(h > 0f ? (map.WaterClass[i] == Hydrology.LAKE || map.WaterClass[i] == Hydrology.RIVER ? 2 : 1) : 0);
                    biome[i] = map.Record.Data[i].Biome;
                    shade[i] = (byte)UnityEngine.Mathf.Clamp(h / 24f, 0f, 255f);   // voxels rel. sea / 24: 0..612 m
                }
                File.WriteAllBytes(Path.Combine(dir, $"seed{seed}_land.bin"), land);
                File.WriteAllBytes(Path.Combine(dir, $"seed{seed}_biome.bin"), biome);
                File.WriteAllBytes(Path.Combine(dir, $"seed{seed}_height.bin"), shade);
                var o = Offsets.For(in spec);
                meta.AppendLine($"{seed} {o.tempA.x:R} {o.tempA.y:R} {o.moistA.x:R} {o.moistA.y:R} {o.biomeScale:R} {spec.islandRadius:R} {spec.mapCellVoxels} {n} {spec.worldSizeVoxels}");
            }
            File.WriteAllText(Path.Combine(dir, "meta.txt"), meta.ToString());
            UnityEngine.Debug.Log($"[C1] dumped {seeds.Count} seeds to {dir}");
        }
    }
}
