// ==========================================
// FloodTwinRuleTests.cs -- task 229 (the torch-flood twin only when torches are near)
//
// FloodTwinRule.Needed decides, per frame, whether the tier-0 window can see any
// flood or torch light; when it says no, the renderer draws the plain kernel
// instead of CSMainFarShadeFlood. Proven here:
//   - the window box (one brick of margin) and the light-sphere test, with a
//     light just inside / just outside the range on an axis and on a diagonal
//     corner (a sphere, not a box), range 0;
//   - SOUNDNESS on 20k random lights x random points: whenever the rule says
//     "no light reaches", every point the shader can shade (window +- 1 voxel)
//     is outside every light's range (the shader's own float test);
//   - the pool's lit-chunk test on a real flood: one torch lights exactly one
//     chunk; a window ending one voxel short of it -> plain kernel, one voxel
//     into it -> twin; the torch removed -> plain; placed again -> twin;
//   - shader pins: the gates the rule relies on are still in the source.
// Pools run without GPU buffers (Gpu false), flushed synchronously.
// ==========================================

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public class FloodTwinRuleTests
{
    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditOps _ops;
    private readonly List<TorchFloodPool> _pools = new List<TorchFloodPool>();

    [TearDown]
    public void Teardown()
    {
        foreach (var p in _pools) p.Dispose();
        _pools.Clear();
        _pool?.Dispose(); _pool = null;
    }

    private static LightSource Light(float3 p, float range) =>
        new LightSource { x = p.x, y = p.y, z = p.z, r = 1f, g = 0.6f, b = 0.3f, range = range, k = 0.35f };

    [Test]
    public void WindowBox_HasOneBrickMargin()
    {
        FloodTwinRule.WindowBoxVox(new int3(-16, 8, 32), new int3(64, 32, 64), out int3 lo, out int3 hi);
        Assert.AreEqual(new int3(-136, 56, 248), lo);
        Assert.AreEqual(new int3((-16 + 64 + 1) * 8 - 1, (8 + 32 + 1) * 8 - 1, (32 + 64 + 1) * 8 - 1), hi);
    }

    [Test]
    public void Light_JustInsideAndJustOutsideRange()
    {
        // window bricks [0, 16)^3 -> box voxels [-8, 135] -> metres [-0.8, 13.6]
        var lo = new int3(-8); var hi = new int3(135);
        const float R = 6f;
        var l = new LightSource[1];
        l[0] = Light(new float3(13.6f + R - 0.01f, 5f, 5f), R);
        Assert.IsTrue(FloodTwinRule.AnyLightReaches(l, 1, lo, hi), "+x side, 1 cm inside the range");
        l[0] = Light(new float3(13.6f + R + 0.01f, 5f, 5f), R);
        Assert.IsFalse(FloodTwinRule.AnyLightReaches(l, 1, lo, hi), "+x side, 1 cm outside the range");
        l[0] = Light(new float3(-0.8f - R + 0.01f, 5f, 5f), R);
        Assert.IsTrue(FloodTwinRule.AnyLightReaches(l, 1, lo, hi), "-x side, inside");
        l[0] = Light(new float3(-0.8f - R - 0.01f, 5f, 5f), R);
        Assert.IsFalse(FloodTwinRule.AnyLightReaches(l, 1, lo, hi), "-x side, outside");
        // corner: each axis within R, the corner itself farther than R -> a sphere, not a box
        float a = R / math.sqrt(3f);
        l[0] = Light(new float3(13.6f + a + 0.02f, 13.6f + a + 0.02f, 13.6f + a + 0.02f), R);
        Assert.IsFalse(FloodTwinRule.AnyLightReaches(l, 1, lo, hi), "diagonal corner just outside");
        l[0] = Light(new float3(13.6f + a - 0.02f, 13.6f + a - 0.02f, 13.6f + a - 0.02f), R);
        Assert.IsTrue(FloodTwinRule.AnyLightReaches(l, 1, lo, hi), "diagonal corner just inside");
        // inside the box, range 0, count 0, only entries < count are read
        l[0] = Light(new float3(5f), 0f);
        Assert.IsFalse(FloodTwinRule.AnyLightReaches(l, 1, lo, hi), "range 0");
        l[0] = Light(new float3(5f), R);
        Assert.IsFalse(FloodTwinRule.AnyLightReaches(l, 0, lo, hi), "count 0 reads nothing");
        Assert.IsFalse(FloodTwinRule.AnyLightReaches(null, 4, lo, hi), "no buffer");
        var two = new[] { Light(new float3(100f), R), Light(new float3(5f), R) };
        Assert.IsFalse(FloodTwinRule.AnyLightReaches(two, 1, lo, hi), "the second light is past the count");
        Assert.IsTrue(FloodTwinRule.AnyLightReaches(two, 2, lo, hi), "the second light reaches");
    }

    /// Soundness: "no light reaches" => the shader's own per-light gate skips every point it can shade.
    [Test]
    public void Light_NoReach_ImpliesShaderSkipsEveryWindowPoint()
    {
        var rng = new Unity.Mathematics.Random(229u);
        int checkedFalse = 0;
        for (int trial = 0; trial < 20000; trial++)
        {
            int3 o = rng.NextInt3(new int3(-64), new int3(64));
            int3 d = rng.NextInt3(new int3(1), new int3(24));
            FloodTwinRule.WindowBoxVox(o, d, out int3 lo, out int3 hi);
            // the points the shader shades: hit point / voxel centre / voxel + normal, all within the window +- 1 voxel
            float3 wLo = ((float3)(o << 3) - 1f) * 0.1f, wHi = ((float3)((o + d) << 3) + 1f) * 0.1f;
            float range = rng.NextFloat(0.5f, 16f);
            float3 c = rng.NextFloat3(wLo - range - 2f, wHi + range + 2f);
            var l = new[] { Light(c, range) };
            if (FloodTwinRule.AnyLightReaches(l, 1, lo, hi)) continue;
            checkedFalse++;
            float r2 = range * range;
            for (int s = 0; s < 24; s++)
            {
                // the box's nearest point to the light (worst case), then random points
                float3 p = s == 0 ? math.clamp(c, wLo, wHi) : rng.NextFloat3(wLo, wHi);
                float3 dv = c - p;
                Assert.Greater(math.dot(dv, dv), r2, $"trial {trial}: light {c} r {range} reaches {p} but the rule said no");
            }
        }
        Assert.Greater(checkedFalse, 2000, "enough negative cases exercised");
    }

    // ---------------------------------------------------------------------
    // The pool's lit chunks (a real flood)
    // ---------------------------------------------------------------------

    private void NewWorld(int3 chunks)
    {
        _pool?.Dispose();
        _pool = new BrickDataPool(80000);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(512));
        for (int cz = 0; cz < chunks.z; cz++)
        for (int cy = 0; cy < chunks.y; cy++)
        for (int cx = 0; cx < chunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = cy == 0 ? Materials.Stone : (byte)0 });
        var edits = new EditService();
        edits.AttachWorld(_store, _store, _store, null);
        _ops = new EditOps(EditOpLog.InMemory(229u), edits, _store);
    }

    private void Put(int3 v, byte m) => _ops.Record(new EditOp(EditOpKind.Box, v, 0, m));

    [Test]
    public void Pool_LitChunk_InsideOutside_PlacedRemoved()
    {
        // 5 x 2 x 5 chunks, stone below y 128; a torch on the ground in the middle of chunk (2, 1, 2), reach 3 m:
        // the flood stays inside that one chunk (voxels [256, 383] in x and z, [128, 255] in y).
        NewWorld(new int3(5, 2, 5));
        var t = new int3(320, 128, 320);
        Put(t, Materials.Torch);
        var pool = new TorchFloodPool(_store, 3f, false);
        _pools.Add(pool);
        Assert.IsFalse(pool.LitChunkIn(new int3(0), new int3(639)), "no torch yet: nothing lit");
        pool.SetTorches(new List<(int3, byte)> { (t, Materials.Torch) });
        pool.Flush();
        Assert.AreEqual(1, pool.Chunks, "the flood lit exactly one chunk");
        var e = pool.Current;
        Assert.AreEqual(new int3(2, 1, 2), e.origin, "that chunk is (2, 1, 2)");

        Assert.IsTrue(pool.LitChunkIn(new int3(0), new int3(639)), "the whole world");
        Assert.IsTrue(pool.LitChunkIn(new int3(256, 128, 256), new int3(256, 128, 256)), "one voxel, the chunk's corner");
        Assert.IsFalse(pool.LitChunkIn(new int3(0, 0, 0), new int3(255, 639, 639)), "-x: ends one voxel short");
        Assert.IsTrue(pool.LitChunkIn(new int3(0, 0, 0), new int3(256, 639, 639)), "-x: one voxel in");
        Assert.IsFalse(pool.LitChunkIn(new int3(384, 0, 0), new int3(639, 639, 639)), "+x: starts one voxel past");
        Assert.IsTrue(pool.LitChunkIn(new int3(383, 0, 0), new int3(639, 639, 639)), "+x: last voxel");
        Assert.IsFalse(pool.LitChunkIn(new int3(0, 0, 0), new int3(639, 127, 639)), "the stone chunks below hold no light");
        Assert.IsFalse(pool.LitChunkIn(new int3(-600, 0, 0), new int3(-1, 639, 639)), "negative side, outside the directory");

        // Needed() with a window: widened hi = ((o + d + 1) << 3) - 1. d.x 31 -> hi 255 (short), 32 -> 263 (in).
        var none = new LightSource[0];
        Assert.IsFalse(FloodTwinRule.Needed(pool, none, 0, new int3(0, 0, 0), new int3(31, 64, 64)), "window one voxel short of the lit chunk");
        Assert.IsTrue(FloodTwinRule.Needed(pool, none, 0, new int3(0, 0, 0), new int3(32, 64, 64)), "window reaches the lit chunk");
        // a torch light near the window still needs the twin even with nothing lit in it
        var near = new[] { Light(new float3(25.6f + 2f, 20f, 20f), 3f) };   // window +x edge (31 + 1) * 0.8 = 25.6 m
        Assert.IsTrue(FloodTwinRule.Needed(pool, near, 1, new int3(0, 0, 0), new int3(31, 64, 64)), "a light's range reaches the window");
        Assert.IsFalse(FloodTwinRule.Needed(pool, near, 0, new int3(0, 0, 0), new int3(31, 64, 64)), "the same light, not active");

        // removed -> nothing lit -> plain kernel; placed again -> twin
        pool.SetTorches(new List<(int3, byte)>());
        pool.Flush();
        Assert.AreEqual(0, pool.Chunks, "removing the torch frees its chunk");
        Assert.IsFalse(pool.LitChunkIn(new int3(0), new int3(639)), "removed: nothing lit");
        Assert.IsFalse(FloodTwinRule.Needed(pool, none, 0, new int3(0), new int3(80, 32, 80)), "removed: plain kernel everywhere");
        pool.SetTorches(new List<(int3, byte)> { (t, Materials.Torch) });
        pool.Flush();
        Assert.IsTrue(FloodTwinRule.Needed(pool, none, 0, new int3(0), new int3(80, 32, 80)), "placed again: the twin");
    }

    [Test]
    public void ShaderPins_GatesTheRuleRelyOn()
    {
        string rm = File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        string tf = File.ReadAllText("Assets/CoreEngine/Rendering/TorchFlood.hlsl");
        // FloodLevels: 0 outside the directory / for an empty entry; FloodLight: 0 when every level is 0
        StringAssert.Contains("if (any(c < 0) || any(c >= _FloodDirDims.xyz)) return float3(0.0, 0.0, 0.0);", tf);
        StringAssert.Contains("if (t == 0u) return float3(0.0, 0.0, 0.0);", tf);
        StringAssert.Contains("int3 c = (v >> 7) - _FloodDirOrigin.xyz;", tf);   // 128-voxel chunks, as LitChunkIn
        StringAssert.Contains("if (m <= 0.0) return float3(0.0, 0.0, 0.0);", tf);
        // the twin samples the flood at the air voxel in front of a tier-0 face (within one voxel of the window)
        Assert.AreEqual(2, Count(rm, "float3 fl = FloodLight(voxel + normal, normal);"), "both flood blocks sample voxel + normal");
        Assert.AreEqual(2, Count(rm, "if (tierAtHit == 0 && !selfLit)\n            {\n                float3 fl = FloodLight("), "the flood only on tier-0 hits");
        // every light term is gated by its range (FloodOtherLights / ...Omni, SumPointLights*)
        Assert.GreaterOrEqual(Count(rm, "if (dist2 > L.range * L.range) continue;"), 4, "range gates");
        StringAssert.Contains("if (dist2 > r2) continue;", rm);
        StringAssert.Contains("if (dO2 <= r2) omniTotal +=", rm);
    }

    private static int Count(string s, string sub)
    {
        int n = 0;
        for (int i = s.IndexOf(sub, System.StringComparison.Ordinal); i >= 0; i = s.IndexOf(sub, i + 1, System.StringComparison.Ordinal)) n++;
        return n;
    }
}
