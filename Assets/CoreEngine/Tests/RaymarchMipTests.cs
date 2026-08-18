// Assets/CoreEngine/Tests/RaymarchMipTests.cs
//
// Amendment 8.7 — B2 tests. The air-mip tracer (TracerRaycastMip) must return
// BIT-IDENTICAL results to the per-voxel walk (TracerRaycast) on every ray —
// same hit, voxel, material, normal — while crossing air in coarse cell leaps.
//
// These mirror the O1_* geometries and add:
//   Mip_SavesWork      : mip outer-steps strictly < walk steps AND < O1 steps on
//                        long-air cases (guards against the pyramid silently not
//                        engaging and degrading to the L0 path).
//   Mip_RandomizedFuzz : 3000 random rays over a random field, own pool. This is
//                        the real guard on the span-128 non-exit landing — the
//                        one numerics change B2 introduced.
//
// TRUST MODEL: the tracer reads a pyramid built by AirMip.BuildFromStore over
// the SAME store the walk reads. AirMipTests separately proves the pyramid is
// correct; these tests prove the traversal is correct given a correct pyramid.
//
// Tests use a SMALL window (64^3 bricks) so BuildFromStore + Build are cheap.
// One integration test uses a larger window to exercise multi-level leaps.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class RaymarchMipTests
{
    // Small window so BuildFromStore's full-window walk is cheap: 64 bricks/axis
    // = 4 chunks/axis. Levels: L1 32^3, L2 16^3, L3 8^3, L4 4^3. All >= 1, so 4
    // levels build. Big enough for real L3/L4 cell leaps (L4 cell = 128 voxels).
    private static readonly int3 SmallWindowBricks = new int3(64, 64, 64);

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(2000);
        _allocator = new ChunkHandleAllocator(64);
        _store = new ChunkStore(_pool, _allocator);
    }

    [TearDown]
    public void Teardown()
    {
        _pool.Dispose();
    }

    // Fill the small window with air chunks so every brick is uniform-air (0)
    // until a test writes solids. ChunkStore's window is EngineConfig-sized
    // (512x256x512), but we only insert the chunks the small window covers; the
    // rest read as null/air, and BuildFromStore(SmallWindowBricks) only walks
    // the small window.
    private void FillSmallWindowWithAir()
    {
        int3 windowChunks = SmallWindowBricks / 16; // 4x4x4
        for (int cz = 0; cz < windowChunks.z; cz++)
        for (int cy = 0; cy < windowChunks.y; cy++)
        for (int cx = 0; cx < windowChunks.x; cx++)
        {
            var chunk = new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 };
            _store.InsertChunk(chunk);
        }
    }

    private AirMipData BuildMips()
    {
        return AirMip.BuildFromStore(_store, SmallWindowBricks, 4);
    }

    private static float3 VoxelCentreWorld(int3 voxel)
    {
        return (new float3(voxel.x, voxel.y, voxel.z) + 0.5f) * 0.1f;
    }

    // Core assertion: mip tracer == per-voxel walk on everything but step count.
    private void AssertMipAgrees(float3 origin, float3 dir, float maxDist, string label)
    {
        AirMipData mips = BuildMips();
        var reference = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var mip = RaymarchReference.TracerRaycastMip(origin, dir, _store, mips, maxDist);

        Assert.AreEqual(reference.hit, mip.hit, $"{label}: hit flag disagrees");
        if (reference.hit)
        {
            Assert.AreEqual(reference.voxelCoord, mip.voxelCoord, $"{label}: hit voxel disagrees");
            Assert.AreEqual(reference.material, mip.material, $"{label}: material disagrees");
            Assert.AreEqual(reference.normal, mip.normal, $"{label}: normal disagrees");
        }
    }

    // ---------------------------------------------------------------------
    //  Structured geometries mirroring the O1_* suite.
    // ---------------------------------------------------------------------

    [Test]
    public void Mip_LongAirSpan_Down_AgreesWithReference()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20)); // high above, long air
        AssertMipAgrees(origin, new float3(0, -1, 0), 128f, "mip-long-air-down");
    }

    [Test]
    public void Mip_LongAirSpan_Up_AgreesWithReference()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 400, 20), 7);
        float3 origin = VoxelCentreWorld(new int3(20, 4, 20));
        AssertMipAgrees(origin, new float3(0, 1, 0), 128f, "mip-long-air-up");
    }

    [Test]
    public void Mip_Diagonal_AgreesWithReference()
    {
        FillSmallWindowWithAir();
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(100 + d, 100 + d, 100 + d), 6);
        float3 origin = VoxelCentreWorld(int3.zero);
        AssertMipAgrees(origin, math.normalize(new float3(1, 1, 1)), 128f, "mip-diagonal");
    }

    [Test]
    public void Mip_NegativeXYZ_Diagonal_AgreesWithReference()
    {
        // All-negative diagonal. Origin high in the window, solid near the low
        // corner. Exercises the step<0 branch of the span exit math at cell
        // scale. (Stays within the small window's positive brick coords.)
        FillSmallWindowWithAir();
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(10 - d, 10 - d, 10 - d), 6);
        float3 origin = VoxelCentreWorld(new int3(300, 300, 300));
        AssertMipAgrees(origin, math.normalize(new float3(-1, -1, -1)), 128f, "mip-neg-diagonal");
    }

    [Test]
    public void Mip_OriginOnCellBoundary_NegativeDir_AgreesWithReference()
    {
        // Origin exactly on an L4 cell boundary (128 voxels = 12.8m) travelling -Y.
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);
        float3 origin = new float3(2.05f, 12.8f, 2.05f); // Y=12.8m = 128 voxels = L4 boundary
        AssertMipAgrees(origin, new float3(0, -1, 0), 128f, "mip-boundary-neg");
    }

    [Test]
    public void Mip_OriginOnCellBoundary_PositiveDir_AgreesWithReference()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 400, 20), 7);
        float3 origin = new float3(2.05f, 12.8f, 2.05f);
        AssertMipAgrees(origin, new float3(0, 1, 0), 128f, "mip-boundary-pos");
    }

    [Test]
    public void Mip_NearAxisAligned_HugeTDelta_AgreesWithReference()
    {
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);
        float3 origin = VoxelCentreWorld(new int3(20, 300, 20));
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));
        AssertMipAgrees(origin, dir, 128f, "mip-near-axis");
    }

    [Test]
    public void Mip_ShallowGrazing_AgreesWithReference()
    {
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        float3 origin = VoxelCentreWorld(new int3(2, 40, 2));
        float3 dir = math.normalize(new float3(1f, -0.25f, 0.7f));
        AssertMipAgrees(origin, dir, 200f, "mip-shallow");
    }

    [Test]
    public void Mip_Miss_AgreesWithReference()
    {
        FillSmallWindowWithAir();
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertMipAgrees(origin, new float3(0, -1, 0), 8f, "mip-miss");
    }

    [Test]
    public void Mip_FineSweep_ManyAngles_AgreeWithReference()
    {
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        for (int p = 0; p < 4; p++)
            for (int h = 2; h < 6; h++)
                _store.SetVoxel(new int3(10 + p * 12, h, 10 + p * 12), 6);

        AirMipData mips = BuildMips();
        float3 origin = VoxelCentreWorld(new int3(6, 55, 6));
        for (int i = -12; i <= 12; i++)
            for (int j = -12; j <= 12; j++)
            {
                float3 dir = math.normalize(new float3(i * 0.03f, -1f, j * 0.03f));
                var reference = RaymarchReference.TracerRaycast(origin, dir, _store, 200f);
                var mip = RaymarchReference.TracerRaycastMip(origin, dir, _store, mips, 200f);
                Assert.AreEqual(reference.hit, mip.hit, $"finesweep({i},{j}): hit disagrees");
                if (reference.hit)
                {
                    Assert.AreEqual(reference.voxelCoord, mip.voxelCoord, $"finesweep({i},{j}): voxel disagrees");
                    Assert.AreEqual(reference.material, mip.material, $"finesweep({i},{j}): material disagrees");
                    Assert.AreEqual(reference.normal, mip.normal, $"finesweep({i},{j}): normal disagrees");
                }
            }
    }

    // ---------------------------------------------------------------------
    //  Work-saving: the pyramid must actually reduce iterations vs BOTH the walk
    //  and the O1 brick leap on a long-air case. Otherwise a "leap" that silently
    //  fell through to L0 would pass every equivalence test while fixing nothing.
    // ---------------------------------------------------------------------

    [Test]
    public void Mip_SavesWork_VsWalkAndO1()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);

        AirMipData mips = BuildMips();
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        float3 dir = new float3(0, -1, 0);

        var walk = RaymarchReference.TracerRaycast(origin, dir, _store, 128f);
        var o1 = RaymarchReference.TracerRaycastO1(origin, dir, _store, 128f);
        var mip = RaymarchReference.TracerRaycastMip(origin, dir, _store, mips, 128f);

        Assert.Less(mip.steps, walk.steps,
            $"mip steps ({mip.steps}) must be < walk steps ({walk.steps})");
        Assert.Less(mip.steps, o1.steps,
            $"mip steps ({mip.steps}) must be < O1 steps ({o1.steps}) - the pyramid must beat per-brick leaping on long air");
    }

    // ---------------------------------------------------------------------
    //  Randomized fuzz — the real guard on the span-128 non-exit landing.
    //  3000 rays, own larger pool, deterministic seed. Uses the small window so
    //  BuildFromStore stays cheap, but a tall window (many L3/L4 leaps) so the
    //  large-span path is genuinely exercised.
    // ---------------------------------------------------------------------

    [Test]
    public void Mip_RandomizedFuzz_AgreesWithReference()
    {
        var pool = new BrickDataPool(30000);
        var allocator = new ChunkHandleAllocator(300);
        var store = new ChunkStore(pool, allocator);
        try
        {
            // Fill the small window with air chunks.
            int3 windowChunks = SmallWindowBricks / 16;
            for (int cz = 0; cz < windowChunks.z; cz++)
            for (int cy = 0; cy < windowChunks.y; cy++)
            for (int cx = 0; cx < windowChunks.x; cx++)
                store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });

            var rng = new System.Random(20260807);

            // Random floor + scattered solids across the window's voxel extent.
            // Window is 64 bricks = 512 voxels per axis.
            int floorY = 1;
            for (int x = 0; x < 200; x++)
                for (int z = 0; z < 200; z++)
                    if (rng.NextDouble() < 0.9)
                        store.SetVoxel(new int3(x, floorY, z), (byte)(2 + rng.Next(6)));
            for (int b = 0; b < 600; b++)
                store.SetVoxel(
                    new int3(rng.Next(0, 200), rng.Next(floorY, floorY + 300), rng.Next(0, 200)),
                    (byte)(2 + rng.Next(6)));

            AirMipData mips = AirMip.BuildFromStore(store, SmallWindowBricks, 4);

            for (int ray = 0; ray < 3000; ray++)
            {
                // Origins high in the window so rays have long air to leap.
                float ox = (float)(rng.NextDouble() * 20 + 2);
                float oy = (float)((floorY + 40 + rng.NextDouble() * 260) * 0.1);
                float oz = (float)(rng.NextDouble() * 20 + 2);
                float3 origin = new float3(ox, oy, oz);

                float dx = (float)(rng.NextDouble() * 2 - 1);
                float dy = (float)(rng.NextDouble() * 2 - 1);
                float dz = (float)(rng.NextDouble() * 2 - 1);
                if (math.abs(dx) < 1e-4f && math.abs(dy) < 1e-4f && math.abs(dz) < 1e-4f) dy = -1f;
                float3 dir = math.normalize(new float3(dx, dy, dz));

                var reference = RaymarchReference.TracerRaycast(origin, dir, store, 200f);
                var mip = RaymarchReference.TracerRaycastMip(origin, dir, store, mips, 200f);

                Assert.AreEqual(reference.hit, mip.hit, $"mip-fuzz(ray={ray}): hit flag disagrees");
                if (reference.hit)
                {
                    Assert.AreEqual(reference.voxelCoord, mip.voxelCoord, $"mip-fuzz(ray={ray}): hit voxel disagrees");
                    Assert.AreEqual(reference.material, mip.material, $"mip-fuzz(ray={ray}): material disagrees");
                    Assert.AreEqual(reference.normal, mip.normal, $"mip-fuzz(ray={ray}): normal disagrees");
                }
            }
        }
        finally
        {
            pool.Dispose();
        }
    }
}