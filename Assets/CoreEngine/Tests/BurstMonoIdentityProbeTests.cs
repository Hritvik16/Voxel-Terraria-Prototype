// Assets/CoreEngine/Tests/BurstMonoIdentityProbeTests.cs
//
// PROBE, NOT A GATE (asserts nothing): does TODAY's generator -- ColumnSampleJob,
// whose header says "the benchmark that uses this job asserts Burst and Mono
// produce byte-identical output" -- actually match Burst vs Mono INSIDE THE
// EDITOR? That benchmark runs in Phase4AcceptanceRig, i.e. in an IL2CPP player,
// not under the Editor's Mono JIT.
//
// Written 2026-09-25 because TerrainGen's new S disagreed Burst-vs-Mono in the
// Editor at the last bit of plain arithmetic (Logs/TerrainGen/determinism_diag.txt).
// If today's generator disagrees here too, the difference is the Editor's JIT,
// not the new code, and the design's "Burst-vs-Mono byte identity" requirement
// has to be restated as "every authoritative evaluation runs one compiled path".
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using VoxelEngine.WorldGen;

public class BurstMonoIdentityProbeTests
{
    [Test]
    public void Probe_TodaysColumnSampler_BurstVsEditorMono()
    {
        var meta = AnchorPlanner.Plan(42, 1);
        using var st = ColumnSampler.CreateState(meta);
        const int edge = 128;
        int differ = 0, total = 0;
        using var h = new NativeArray<int>(edge * edge, Allocator.TempJob);
        using var b = new NativeArray<byte>(edge * edge, Allocator.TempJob);
        // 16 chunk-sized patches across the island, including anchor features.
        for (int pz = 0; pz < 4; pz++)
        for (int px = 0; px < 4; px++)
        {
            int bx = 6000 + px * 3500, bz = 6000 + pz * 3500;
            new ColumnSampleJob { st = st, baseVoxelX = bx, baseVoxelZ = bz, edge = edge, heights = h, biomes = b }.Run();
            for (int lz = 0; lz < edge; lz++)
            for (int lx = 0; lx < edge; lx++)
            {
                ColumnSampler.SampleColumn(in st, bx + lx, bz + lz, out int mh, out byte mb);
                total++;
                if (mh != h[lz * edge + lx] || mb != b[lz * edge + lx]) differ++;
            }
        }
        string line = $"today's ColumnSampleJob, Burst .Run() vs Editor Mono: {differ} of {total} columns differ " +
                      $"({100.0 * differ / total:F3}%). Burst enabled in Editor: {Unity.Burst.BurstCompiler.IsEnabled}";
        UnityEngine.Debug.Log("[BurstMonoProbe] " + line);
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "burst_mono_probe_today.txt"), line + "\n");
        Assert.Pass(line);
    }
}
