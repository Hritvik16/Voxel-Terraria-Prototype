// Assets/CoreEngine/Tests/ShellOrderTests.cs
//
// Task 85 (engine step 12; owner Q47 (a), 2026-10-12: "near terrain loads
// first"; Amendment 8.14 §3 "Order" and row 5):
//   * NEAR FIRST. A fixed camera at seed 42's spawn, the High layout, every
//     missing shell chunk taken in the order the streamer would request it:
//     no shell >= 3 chunk farther than a shell-1 chunk is requested before it
//     (the old coarsest-first order requested every shell-7 chunk first).
//   * The velocity bias (§4.3 prefetch) prefers the chunk ahead at equal distance.
//   * PREFETCH MARGIN > 0 by construction (row 5): every shell stores past its
//     hand-over distance on every preset; the margin and the seconds it buys at
//     7 / 11 / 60 m/s go to Logs/Shells/margin85.txt.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class ShellOrderTests
{
    static TerrainGenChunkSource _src;
    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }
    [OneTimeTearDown] public void TearDown() { _src?.Dispose(); _src = null; }

    /// Land near the middle of the island (ShellStorageTests' spawn rule).
    static float3 Spawn(TerrainGenChunkSource src)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 2000; i++)
        {
            float a = i * 2.39996f, r = 40f * i;
            float2 p = new float2(c + r * math.cos(a), c + r * math.sin(a));
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 200 || col.water > col.height) continue;
            return new float3(p.x * 0.1f, (col.height + 1) * 0.1f + 2f, p.y * 0.1f);
        }
        return new float3(c * 0.1f, (spec.seaLevelY + 800) * 0.1f, c * 0.1f);
    }

    [Test]
    public void FixedCamera_EveryShell1Chunk_BeforeAnyFartherCoarseChunk()
    {
        var L = ShellLayout.From(QualityPreset.High);
        var s = new ShellStreamer(L, _src, 1, synchronous: true) { SyncChunksPerUpdate = 0 };
        try
        {
            float3 cam = Spawn(_src);
            s.Update(cam, 1 << 30);   // slides the boxes; generates nothing
            int total = 0;
            for (int k = 1; k < L.ShellCount; k++) total += s.Missing(k);
            var order = new List<(int k, int3 c)>();
            s.TakeRequestOrder(order, total);
            Assert.Greater(order.Count, 0);

            float3 camVox = cam * 10f;
            float maxCoarse = -1f, prevKey = -1f;
            int firstCoarse = -1, lastS1 = -1, s1 = 0;
            var perShell = new int[L.ShellCount];
            for (int i = 0; i < order.Count; i++)
            {
                var (k, c) = order[i];
                perShell[k]++;
                float key = ShellStreamer.OrderKey(L, k, c, camVox, float3.zero, out float d);
                Assert.GreaterOrEqual(key, prevKey, $"request {i} (shell {k} {c}): key {key} after {prevKey}");
                prevKey = key;
                if (k >= 3) { maxCoarse = math.max(maxCoarse, d); if (firstCoarse < 0) firstCoarse = i; }
                if (k == 1)
                {
                    s1++; lastS1 = i;
                    Assert.LessOrEqual(maxCoarse, d,
                        $"request {i}: shell-1 chunk {c} at {d * 0.1f:F1} m came after a shell >= 3 chunk at {maxCoarse * 0.1f:F1} m");
                }
            }
            Assert.Greater(s1, 0, "no shell-1 chunk requested");
            UnityEngine.Debug.Log($"[ShellOrder] {order.Count} requests at {cam} m; per shell {string.Join("/", perShell)}; " +
                                  $"shell 1: {s1} chunks, last at #{lastS1}; first shell >= 3 at #{firstCoarse}; first request shell {order[0].k}");
        }
        finally { s.Dispose(); }
    }

    [Test]
    public void VelocityBias_PrefersTheChunkAhead()
    {
        var L = ShellLayout.From(QualityPreset.High);
        float3 cam = new float3(40000.5f, 16484.5f, 40000.5f);   // voxels
        int3 cc = (int3)math.floor(cam) >> 8;                     // shell 1 chunk
        int3 ahead = cc + new int3(3, 0, 0), behind = cc - new int3(3, 0, 0);
        float3 vx = new float3(1f, 0f, 0f);
        float ka = ShellStreamer.OrderKey(L, 1, ahead, cam, vx, out float da);
        float kb = ShellStreamer.OrderKey(L, 1, behind, cam, vx, out float db);
        Assert.Less(ka, kb, "moving +x: the chunk ahead first");
        float k0a = ShellStreamer.OrderKey(L, 1, ahead, cam, float3.zero, out _);
        float k0b = ShellStreamer.OrderKey(L, 1, behind, cam, float3.zero, out _);
        Assert.AreEqual(k0a, da, 1e-3f, "no velocity, beyond the inner bound: the key is the distance");
        Assert.AreEqual(math.max(da, db), math.max(k0a, k0b), 1e-3f);
        // Inside the inner hand-over distance a chunk's key is that distance (nothing nearer is drawn).
        float kIn = ShellStreamer.OrderKey(L, 3, ((int3)math.floor(cam) >> 10) + new int3(2, 0, 0), cam, float3.zero, out float dIn);
        Assert.AreEqual(math.max(dIn, L[3].InnerM * 10f), kIn, 1e-3f);
    }

    [Test]
    public void PrefetchMargin_IsPositive_OnEveryPreset()
    {
        // The box is +-R chunks around the camera's chunk, so from anywhere in that
        // chunk it reaches >= R x E on every axis; a point at Euclidean distance r has
        // every axis offset <= r. Margin = R x E - r (shell 0: its load radius).
        var lines = new List<string> { "task 85: prefetch margin = stored reach (R x E, Chebyshev, from anywhere in the camera's chunk) - hand-over distance",
                                       "preset shell  hand-over m  reach m  margin m   s at 7 / 11 / 60 m/s" };
        foreach (QualityPreset p in new[] { QualityPreset.Low, QualityPreset.Medium, QualityPreset.High, QualityPreset.Ultra })
        {
            var L = ShellLayout.From(p);
            for (int k = 0; k < L.ShellCount; k++)
            {
                float r = L[k].OuterM;
                float reach = k == 0 ? L.LoadRadiusChunks * 12.8f : L[k].Reach * L[k].ChunkM;
                float m = reach - r;
                lines.Add($"{p,-6} {k,5} {r,12:F1} {reach,8:F1} {m,9:F1}   {m / 7f:F1} / {m / 11f:F1} / {m / 60f:F2}");
                if (k == 0) Assert.GreaterOrEqual(m, ShellLayout.SHELL0_MARGIN_M, $"{p}: shell 0 margin {m} m < 12.8 m");
                else Assert.Greater(m, 0f, $"{p}: shell {k} stores only to {reach} m, hand-over at {r} m");
            }
        }
        var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "margin85.txt"), lines);
        UnityEngine.Debug.Log("[ShellOrder] " + string.Join("\n", lines));
    }
}
