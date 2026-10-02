// Assets/TerrainGen/Tests/FarSlopeRockReport.cs
//
// Task 03 (owner 2026-10-02, "2c"): the far band should show rock where the
// voxels do. The voxel rule is DetailField's `stepAtTop > 1.5` with stepAtTop
// = MaxStep at spacing 1 -- integer heights, detail octaves included. The far
// bake sees MaxStep at texel spacing with the fine octaves band-limited away,
// so the task's hypothesis was that 1.5 under-reports rock there. This measures,
// on the sizeClass 2 island (seed 42), the fraction of voxel columns in a far
// texel's footprint that are steep, binned by the texel-scale slope, and the
// texel-slope threshold that best agrees with the voxels' majority.
// Asserts nothing; writes the table to the log. ~80 s, so opt-in:
//   FARSLOPE=1 tools/run-editmode-filter.sh FarSlopeRockReport
// Result 2026-10-02: voxel rock crosses 50% at texel slope ~1.35 (F0-F2);
// the bake's 1.5 agrees with the voxel majority on ~93% of land texels.
using System;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;

namespace TerrainGen.Tests
{
    public class FarSlopeRockReport
    {
        [Test]
        public void Report_FarSlopeVsVoxelRock()
        {
            if (Environment.GetEnvironmentVariable("FARSLOPE") != "1")
                Assert.Ignore("report only: set FARSLOPE=1 (tools/run-editmode-filter.sh FarSlopeRockReport)");
            var spec = TerrainSpec.SizeClass2(42);
            var o = Offsets.For(42);
            var map = WorldMap.Build(spec);
            var v = map.View;
            var rng = new Unity.Mathematics.Random(12345u);
            var sb = new StringBuilder("[FarSlopeRock] sizeClass 2 seed 42\n");
            const int BINS = 30;   // slope bins of 0.1, last bin open
            const int GRID = 8;    // voxel columns sampled per footprint edge
            for (int level = 0; level < 3; level++)
            {
                int tv = 8 << level;
                var binN = new int[BINS];
                var binRock = new double[BINS];
                var sList = new System.Collections.Generic.List<float>();
                var fList = new System.Collections.Generic.List<float>();
                int tries = 0;
                while (sList.Count < 3000 && tries++ < 200000)
                {
                    int tx = rng.NextInt(0, spec.worldSizeVoxels / tv), tz = rng.NextInt(0, spec.worldSizeVoxels / tv);
                    float gx = tx * tv + (tv >> 1), gz = tz * tv + (tv >> 1);
                    var c = ShapeField.Sample(in spec, in v, in o, gx, gz, tv);
                    if (c.height <= spec.seaLevelY + 22 || c.water > c.height || c.stampMat != 0) continue;
                    float s = ShapeField.MaxStep(in spec, in v, in o, gx, gz, tv, c.height);
                    int rock = 0;
                    for (int j = 0; j < GRID; j++)
                    for (int i = 0; i < GRID; i++)
                    {
                        float x = tx * tv + (i * tv) / GRID + 0.5f, z = tz * tv + (j * tv) / GRID + 0.5f;
                        var cv = ShapeField.Sample(in spec, in v, in o, x, z, 1);
                        if (ShapeField.MaxStep(in spec, in v, in o, x, z, 1, cv.height) > 1.5f) rock++;
                    }
                    float f = rock / (float)(GRID * GRID);
                    int b = math.min((int)(s * 10f), BINS - 1);
                    binN[b]++; binRock[b] += f;
                    sList.Add(s); fList.Add(f);
                }
                sb.AppendLine($"level F{level} (texel {tv} voxels), {sList.Count} land texels:");
                for (int b = 0; b < BINS; b++)
                    if (binN[b] > 0)
                        sb.AppendLine($"  s {b * 0.1f:F1}-{(b + 1) * 0.1f:F1}  n {binN[b],5}  voxel rock {binRock[b] / binN[b]:P0}");
                float bestT = 0; int bestAgree = -1;
                for (float t = 0.3f; t <= 1.6f; t += 0.05f)
                {
                    int agree = 0;
                    for (int k = 0; k < sList.Count; k++) if ((sList[k] > t) == (fList[k] > 0.5f)) agree++;
                    if (agree > bestAgree) { bestAgree = agree; bestT = t; }
                    if (math.abs(t - 1.5f) < 0.01f)
                        sb.AppendLine($"  current rule s > 1.5: agrees with voxel majority on {agree / (float)sList.Count:P1}");
                }
                sb.AppendLine($"  best threshold s > {bestT:F2}: agrees on {bestAgree / (float)sList.Count:P1}");
            }
            UnityEngine.Debug.Log(sb.ToString());
        }
    }
}
