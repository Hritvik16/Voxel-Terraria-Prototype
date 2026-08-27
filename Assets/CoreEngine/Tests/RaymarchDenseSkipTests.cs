// Assets/CoreEngine/Tests/RaymarchDenseSkipTests.cs
//
// Same correctness bar as every other tracer sibling in this codebase:
// TracerRaycastMipReseedClosedFormDenseSkip must return BIT-IDENTICAL final
// ray outcomes (hit, voxel, material, normal) to TracerRaycast (the oracle).
// The dense-skip optimization is argued to be a pure redundant-check
// elimination in RaymarchDenseSkip.cs's file header - this is what actually
// checks that argument against real geometry rather than trusting it.
//
// Mirrors RaymarchMipReseedTests.cs's ClosedForm_* structure so this gets
// the same coverage discipline, plus a work-saving proof specific to this
// optimization (mipProbeCalls should collapse on a dense-heavy ray).

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class RaymarchDenseSkipTests
{
    private static readonly int3 SmallWindowBricks = new int3(64, 64, 64);

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(4000);
        _allocator = new ChunkHandleAllocator(128);
        _store = new ChunkStore(_pool, _allocator);
    }

    [TearDown]
    public void Teardown()
    {
        _pool.Dispose();
    }

    private void FillSmallWindowWithAir()
    {
        int3 windowChunks = SmallWindowBricks / 16;
        for (int cz = 0; cz < windowChunks.z; cz++)
        for (int cy = 0; cy < windowChunks.y; cy++)
        for (int cx = 0; cx < windowChunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
    }

    private AirMipData BuildMips() => AirMip.BuildFromStore(_store, SmallWindowBricks, 4);

    private static float3 VoxelCentreWorld(int3 voxel) =>
        (new float3(voxel.x, voxel.y, voxel.z) + 0.5f) * 0.1f;

    private void AssertDenseSkipAgreesWithOracle(float3 origin, float3 dir, float maxDist, string label)
    {
        AirMipData mips = BuildMips();
        var oracle = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var denseSkip = RaymarchReference.TracerRaycastMipReseedClosedFormDenseSkip(origin, dir, _store, mips, maxDist);

        Assert.AreEqual(oracle.hit, denseSkip.hit, $"{label}: hit flag disagrees");
        if (oracle.hit)
        {
            Assert.AreEqual(oracle.voxelCoord, denseSkip.voxelCoord, $"{label}: hit voxel disagrees");
            Assert.AreEqual(oracle.material, denseSkip.material, $"{label}: material disagrees");
            Assert.AreEqual(oracle.normal, denseSkip.normal, $"{label}: normal disagrees");
        }
    }

    // ---------------------------------------------------------------------
    //  Structured geometries - same shapes RaymarchMipReseedTests already
    //  covers for ClosedForm, since DenseSkip must be at least as correct on
    //  every case that suite already covers.
    // ---------------------------------------------------------------------

    [Test]
    public void DenseSkip_LongAirSpan_Down_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertDenseSkipAgreesWithOracle(origin, new float3(0, -1, 0), 128f, "densesk-long-air-down");
    }

    [Test]
    public void DenseSkip_Diagonal_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(100 + d, 100 + d, 100 + d), 6);
        float3 origin = VoxelCentreWorld(int3.zero);
        AssertDenseSkipAgreesWithOracle(origin, math.normalize(new float3(1, 1, 1)), 128f, "densesk-diagonal");
    }

    [Test]
    public void DenseSkip_Miss_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertDenseSkipAgreesWithOracle(origin, new float3(0, -1, 0), 8f, "densesk-miss");
    }

    // ---------------------------------------------------------------------
    //  The case this optimization actually exists for: a genuinely mixed
    //  brick - a small pocket of air voxels sitting inside a mostly-solid
    //  brick, forcing several per-voxel steps INSIDE ONE dense brick before
    //  the ray exits it or hits solid. Mirrors the mineshaft's real shape
    //  (a tunnel dug through otherwise-solid rock) at brick scale.
    // ---------------------------------------------------------------------

    [Test]
    public void DenseSkip_TunnelThroughDenseBrick_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        // Fill an 8x8x8 brick's worth of terrain solid, then carve a
        // straight air tunnel through several voxels of it - a genuinely
        // mixed/dense brick with real internal structure, not just one
        // solid voxel.
        int3 brickBase = new int3(80, 80, 80); // brick-aligned (multiple of 8)
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            _store.SetVoxel(brickBase + new int3(x, y, z), 2);
        for (int x = 0; x < 8; x++)
            _store.SetVoxel(brickBase + new int3(x, 4, 4), 0); // air tunnel along X through the middle

        float3 origin = VoxelCentreWorld(brickBase + new int3(-5, 4, 4));
        AssertDenseSkipAgreesWithOracle(origin, new float3(1, 0, 0), 30f, "densesk-tunnel-through-dense-brick");
    }

    [Test]
    public void DenseSkip_TunnelThroughDenseBrick_ShallowAngle_AgreesWithOracle()
    {
        // Same tunnel, off-axis approach - exercises the brick-exit check
        // (CoordMath.VoxelToBrick(voxel).Equals(brickCoord)) on a ray that
        // doesn't traverse the brick edge-to-edge on a single axis.
        FillSmallWindowWithAir();
        int3 brickBase = new int3(80, 80, 80);
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            _store.SetVoxel(brickBase + new int3(x, y, z), 2);
        for (int x = 0; x < 8; x++)
            _store.SetVoxel(brickBase + new int3(x, 4, 4), 0);

        float3 origin = VoxelCentreWorld(brickBase + new int3(-5, 4, 4));
        float3 dir = math.normalize(new float3(1f, 0.02f, 0.03f));
        AssertDenseSkipAgreesWithOracle(origin, dir, 30f, "densesk-tunnel-shallow-angle");
    }

    // ---------------------------------------------------------------------
    //  Work-saving proof: on the tunnel case, mipProbeCalls must be small
    //  relative to denseSkipSteps - otherwise the optimization compiled but
    //  isn't actually engaging.
    // ---------------------------------------------------------------------

    [Test]
    public void DenseSkip_TunnelCase_MipProbeCallsCollapseRelativeToDenseSteps()
    {
        FillSmallWindowWithAir();
        int3 brickBase = new int3(80, 80, 80);
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            _store.SetVoxel(brickBase + new int3(x, y, z), 2);
        for (int x = 0; x < 8; x++)
            _store.SetVoxel(brickBase + new int3(x, 4, 4), 0);

        AirMipData mips = BuildMips();
        float3 origin = VoxelCentreWorld(brickBase + new int3(-5, 4, 4));
        var (hit, mipProbeCalls, denseSkipSteps) =
            RaymarchReference.TracerRaycastMipReseedClosedFormDenseSkipWithCounts(origin, new float3(1, 0, 0), _store, mips, 30f);

        Assert.IsFalse(hit.hit, "this ray should exit through the tunnel and miss, not hit the tunnel wall");
        Assert.Greater(denseSkipSteps, 0, "the dense-skip inner loop should have engaged on the tunnel");
        Assert.Less(mipProbeCalls, denseSkipSteps,
            $"mip probe calls ({mipProbeCalls}) should be far fewer than dense-skip steps ({denseSkipSteps}) - " +
            "otherwise the optimization compiled but isn't actually collapsing the redundant re-probes.");
    }

    // ---------------------------------------------------------------------
    //  Randomized fuzz - the real guard. Same regime split as the other
    //  fuzz tests in this codebase (half near-axis-aligned, half uniform-
    //  random), but the solid field is built with GENUINE mixed bricks
    //  (scattered small air pockets carved into otherwise-solid regions)
    //  rather than isolated single solid voxels in open air - since that's
    //  the specific case this optimization touches and the earlier fuzz
    //  suites never exercised.
    // ---------------------------------------------------------------------

    [Test]
    public void DenseSkip_RandomizedFuzz_AgreesWithOracle()
    {
        var pool = new BrickDataPool(30000);
        var allocator = new ChunkHandleAllocator(300);
        var store = new ChunkStore(pool, allocator);
        try
        {
            int3 windowChunks = SmallWindowBricks / 16;
            for (int cz = 0; cz < windowChunks.z; cz++)
            for (int cy = 0; cy < windowChunks.y; cy++)
            for (int cx = 0; cx < windowChunks.x; cx++)
                store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });

            var rng = new System.Random(20260819);

            // Solid block of terrain, then carve genuine mixed-brick tunnels
            // through it - the shape that actually exercises DenseSkip.
            for (int x = 0; x < 150; x++)
            for (int y = 0; y < 60; y++)
            for (int z = 0; z < 150; z++)
                store.SetVoxel(new int3(x, y, z), (byte)(2 + rng.Next(6)));

            // Carve ~40 short random tunnels through the solid block.
            for (int t = 0; t < 40; t++)
            {
                int3 tunnelStart = new int3(rng.Next(10, 140), rng.Next(10, 50), rng.Next(10, 140));
                int3 tunnelDir = new int3(rng.Next(-1, 2), rng.Next(-1, 2), rng.Next(-1, 2));
                if (tunnelDir.Equals(int3.zero)) tunnelDir = new int3(1, 0, 0);
                int3 pos = tunnelStart;
                int len = rng.Next(5, 20);
                for (int i = 0; i < len; i++)
                {
                    store.SetVoxel(pos, 0);
                    pos += tunnelDir;
                }
            }

            AirMipData mips = AirMip.BuildFromStore(store, SmallWindowBricks, 4);

            for (int ray = 0; ray < 4000; ray++)
            {
                float ox = (float)(rng.NextDouble() * 15 + 1);
                float oy = (float)(rng.NextDouble() * 6 + 0.5);
                float oz = (float)(rng.NextDouble() * 15 + 1);
                float3 origin = new float3(ox, oy, oz);

                float3 dir;
                if (ray % 2 == 0)
                {
                    float dx = (float)((rng.NextDouble() * 2 - 1) * 0.02);
                    float dz = (float)((rng.NextDouble() * 2 - 1) * 0.02);
                    dir = math.normalize(new float3(dx, rng.NextDouble() < 0.5 ? -1f : 1f, dz));
                }
                else
                {
                    float dx = (float)(rng.NextDouble() * 2 - 1);
                    float dy = (float)(rng.NextDouble() * 2 - 1);
                    float dz = (float)(rng.NextDouble() * 2 - 1);
                    if (math.abs(dx) < 1e-4f && math.abs(dy) < 1e-4f && math.abs(dz) < 1e-4f) dy = -1f;
                    dir = math.normalize(new float3(dx, dy, dz));
                }

                var oracle = RaymarchReference.TracerRaycast(origin, dir, store, 200f);
                var denseSkip = RaymarchReference.TracerRaycastMipReseedClosedFormDenseSkip(origin, dir, store, mips, 200f);

                Assert.AreEqual(oracle.hit, denseSkip.hit, $"densesk-fuzz(ray={ray}): hit flag disagrees, dir={dir}");
                if (oracle.hit)
                {
                    Assert.AreEqual(oracle.voxelCoord, denseSkip.voxelCoord, $"densesk-fuzz(ray={ray}): hit voxel disagrees, dir={dir}");
                    Assert.AreEqual(oracle.material, denseSkip.material, $"densesk-fuzz(ray={ray}): material disagrees, dir={dir}");
                    Assert.AreEqual(oracle.normal, denseSkip.normal, $"densesk-fuzz(ray={ray}): normal disagrees, dir={dir}");
                }
            }
        }
        finally
        {
            pool.Dispose();
        }
    }
}