// Assets/CoreEngine/Tests/SunFarCoarseTests.cs
//
// TASK 228 (performance, owner Q-AN 2026-11-16 "coarse far shadows"): the CPU twin of the shader's coarse far
// sun-shadow rule (Raymarch.compute _SunFarVoxels / SunCoarseY; ShadowRayReference.Trace(coarseY)).
// A far receiver's sun ray, once two bricks above its origin, counts any brick holding solid as solid.
// Proven here: near receivers are exact; the rule only ADDS occlusion; bumpy lit ground does not self-shadow;
// the adversarial thin pole and tall wall widen by at most their own bricks.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class SunFarCoarseTests
{
    private static readonly int3 WindowBricks = new int3(64, 64, 64);   // 512 voxels per axis, as ShadowRayTests
    private const int Floor = 20;                                        // ground top: y in [0, 20) is stone

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(20000);
        _allocator = new ChunkHandleAllocator(256);
        _store = new ChunkStore(_pool, _allocator);
        int3 wc = WindowBricks / 16;
        for (int cz = 0; cz < wc.z; cz++)
        for (int cy = 0; cy < wc.y; cy++)
        for (int cx = 0; cx < wc.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
    }

    [TearDown]
    public void Teardown() => _pool.Dispose();

    private void Box(int3 min, int3 size)
    {
        for (int z = 0; z < size.z; z++)
        for (int y = 0; y < size.y; y++)
        for (int x = 0; x < size.x; x++)
            _store.SetVoxel(min + new int3(x, y, z), Materials.Stone);
    }

    // The camera sits far from every receiver used here (> 150 voxels), and sunFar = 100 voxels (10 m).
    private static readonly float3 Cam = new float3(1f, 6f, 1f);   // metres -> voxel (10, 60, 10)
    private const float SunFarVox = 100f;

    private ShadowRayReference.ShadowResult Exact(int3 hit, float3 sun) =>
        ShadowRayReference.Trace(_store, hit, new int3(0, 1, 0), sun, Cam, 0f, 60f);

    private ShadowRayReference.ShadowResult Coarse(int3 hit, float3 sun)
    {
        int3 o = hit + new int3(0, 1, 0);
        float dist = math.distance((new float3(hit.x, hit.y, hit.z) + 0.5f), Cam * 10f);
        int cy = ShadowRayReference.SunCoarseY(dist, SunFarVox, o.y, 0);
        Assert.Less(cy, int.MaxValue, "test setup: every receiver here must be FAR");
        return ShadowRayReference.Trace(_store, hit, new int3(0, 1, 0), sun, Cam, 0f, 60f, cy);
    }

    [Test]
    public void SunCoarseY_NearReceiverOrOffIsExact()
    {
        Assert.AreEqual(int.MaxValue, ShadowRayReference.SunCoarseY(99.9f, 100f, 20, 0), "near receiver");
        Assert.AreEqual(int.MaxValue, ShadowRayReference.SunCoarseY(5000f, 0f, 20, 0), "switch off");
        Assert.AreEqual(36, ShadowRayReference.SunCoarseY(100f, 100f, 20, 0), "tier 0: two 8-voxel bricks above");
        Assert.AreEqual(52, ShadowRayReference.SunCoarseY(100f, 100f, 20, 1), "shell 1: two 16-voxel bricks above");
        Assert.AreEqual(84, ShadowRayReference.SunCoarseY(100f, 100f, 20, 2), "shell 2");
    }

    /// A scattered "forest" of random blobs over a floor: on every receiver x 3 sun angles, an exact hit is a
    /// coarse hit (the rule only adds shadow), and a coarse-only hit really passed a brick holding solid.
    [Test]
    public void Coarse_OnlyAddsOcclusion()
    {
        Box(new int3(160, 0, 160), new int3(192, Floor, 192));
        var rng = new Unity.Mathematics.Random(228u);
        for (int i = 0; i < 40; i++)
        {
            int3 c = new int3(rng.NextInt(170, 340), rng.NextInt(Floor + 4, Floor + 70), rng.NextInt(170, 340));
            int3 s = new int3(rng.NextInt(1, 9), rng.NextInt(1, 9), rng.NextInt(1, 9));
            for (int k = 0; k < 30; k++)   // a sparse blob: canopy-like, mixed bricks
                _store.SetVoxel(c + new int3(rng.NextInt(0, s.x), rng.NextInt(0, s.y), rng.NextInt(0, s.z)), Materials.Stone);
        }
        float3[] suns = { math.normalize(new float3(0.2f, 1f, 0.1f)), math.normalize(new float3(1f, 0.6f, 0.3f)), math.normalize(new float3(-1f, 0.3f, 0.5f)) };
        int exactHits = 0, coarseHits = 0, added = 0;
        for (int z = 180; z < 330; z += 5)
        for (int x = 180; x < 330; x += 5)
        foreach (float3 sun in suns)
        {
            var hit = new int3(x, Floor - 1, z);
            if (_store.GetVoxel(hit + new int3(0, 1, 0)) != 0) continue;   // receiver buried under a blob voxel
            var e = Exact(hit, sun);
            var c = Coarse(hit, sun);
            if (e.occluded) { exactHits++; Assert.IsTrue(c.occluded, $"exact hit lost by coarse at {hit} sun {sun}"); }
            if (c.occluded) coarseHits++;
            if (c.occluded && !e.occluded) added++;
        }
        UnityEngine.Debug.Log($"[228] coarse only adds: exact hits {exactHits}, coarse hits {coarseHits}, added {added}");
        Assert.Greater(exactHits, 20, "test setup: the blobs must cast shadows");
    }

    /// Bumpy lit ground (bumps <= 3 voxels): the climb guard keeps a far receiver's own surface exact, at a low,
    /// a mid and a high sun. Not one receiver may go dark.
    [Test]
    public void Coarse_BumpyGroundDoesNotSelfShadow()
    {
        Box(new int3(160, 0, 160), new int3(192, Floor, 192));
        var rng = new Unity.Mathematics.Random(7u);
        for (int i = 0; i < 600; i++)
            Box(new int3(rng.NextInt(160, 352), Floor, rng.NextInt(160, 352)), new int3(1, rng.NextInt(1, 4), 1));
        float3[] suns = { math.normalize(new float3(1f, math.tan(math.radians(15f)), 0.2f)),
                          math.normalize(new float3(-0.3f, math.tan(math.radians(30f)), 1f)),
                          math.normalize(new float3(0.1f, math.tan(math.radians(78f)), 0.1f)) };
        int lit = 0;
        for (int z = 200; z < 320; z += 3)
        for (int x = 200; x < 320; x += 3)
        foreach (float3 sun in suns)
        {
            var hit = new int3(x, Floor - 1, z);
            if (_store.GetVoxel(hit + new int3(0, 1, 0)) != 0) continue;
            var e = Exact(hit, sun);
            var c = Coarse(hit, sun);
            Assert.AreEqual(e.occluded, c.occluded, $"bumpy ground: coarse differs at {hit} sun {sun}");
            if (!e.occluded) lit++;
        }
        Assert.Greater(lit, 100, "test setup: most of the ground must be lit");
    }

    /// ADVERSARIAL: a 1 x 1 voxel pole, 100 voxels tall, far away. Sun in the x-y plane, receivers along z across
    /// its shadow. Exact: only z = 200. Coarse: inside the pole's brick [200, 208) and nothing else.
    [Test]
    public void Coarse_ThinPoleWidensAtMostItsBrick()
    {
        Box(new int3(160, 0, 160), new int3(192, Floor, 120));
        Box(new int3(200, Floor, 200), new int3(1, 100, 1));   // the pole at (x 200, z 200)
        var sun = math.normalize(new float3(-1f, 1f, 0f));
        for (int x = 230; x <= 260; x += 10)
        {
            int ex = 0, co = 0;
            for (int z = 180; z < 230; z++)
            {
                var hit = new int3(x, Floor - 1, z);
                bool e = Exact(hit, sun).occluded, c = Coarse(hit, sun).occluded;
                if (e) { ex++; Assert.AreEqual(200, z, "exact pole shadow is one voxel wide"); }
                if (c) { co++; Assert.IsTrue(z >= 200 && z < 208, $"coarse pole shadow at z {z} is outside the pole's brick"); }
                if (e) Assert.IsTrue(c, "coarse lost the pole's exact shadow");
            }
            Assert.AreEqual(1, ex, $"x {x}: exact pole shadow");
            Assert.LessOrEqual(co, 8, $"x {x}: coarse pole shadow wider than one brick");
        }
    }

    /// ADVERSARIAL: a tall wall far away (z in [203, 251), 100 voxels tall). Its coarse shadow edge moves by at
    /// most the bricks the wall occupies: [200, 256), and it keeps the whole exact shadow.
    [Test]
    public void Coarse_TallWallEdgeMovesAtMostOneBrick()
    {
        Box(new int3(160, 0, 160), new int3(192, Floor, 140));
        Box(new int3(200, Floor, 203), new int3(1, 100, 48));
        var sun = math.normalize(new float3(-1f, 1.3f, 0f));
        int ex = 0, co = 0;
        for (int z = 180; z < 280; z++)
        {
            var hit = new int3(250, Floor - 1, z);
            bool e = Exact(hit, sun).occluded, c = Coarse(hit, sun).occluded;
            if (e) { ex++; Assert.IsTrue(z >= 203 && z < 251, $"exact wall shadow at z {z}"); Assert.IsTrue(c, $"coarse lost the wall's shadow at z {z}"); }
            if (c) { co++; Assert.IsTrue(z >= 200 && z < 256, $"coarse wall shadow at z {z} outside the wall's bricks"); }
        }
        Assert.AreEqual(48, ex, "exact wall shadow width");
        Assert.LessOrEqual(co - ex, 8, "coarse widens the wall's shadow by at most one brick in total (3 + 5 here)");
    }
}
