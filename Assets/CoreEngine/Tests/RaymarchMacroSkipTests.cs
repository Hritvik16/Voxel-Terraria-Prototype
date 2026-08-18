using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

// The oracle property for the macro-skip: for the SAME ray, TracerRaycastMacroSkip
// must return BIT-IDENTICAL results to the proven per-voxel TracerRaycast - same
// hit, same voxel, same material, same normal. A correct macro-skip only visits
// fewer voxels; it never lands anywhere different. Any disagreement is a
// macro-skip bug by construction.
//
// With the tMax-based integer leap (no float floor), equivalence is structural:
// the leap runs the walk's own StepDda transition across an air brick without
// reading material, so it MUST land on the same voxel/tMax/normal the walk does.
// These tests guard that structure against regression and double as the
// behavioural spec the GPU port is diffed against.
public class RaymarchMacroSkipTests
{
    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(400);
        _allocator = new ChunkHandleAllocator(10);
        _store = new ChunkStore(_pool, _allocator);
    }

    [TearDown]
    public void Teardown()
    {
        _pool.Dispose();
    }

    private Chunk MakeAirChunk(int3 chunkCoord)
    {
        Chunk chunk = new Chunk { coord = chunkCoord, isUniform = true, uniformMaterial = 0 };
        _store.InsertChunk(chunk);
        return chunk;
    }

    private static float3 VoxelCentreWorld(int3 voxel)
    {
        return (new float3(voxel.x, voxel.y, voxel.z) + 0.5f) * 0.1f;
    }

    // The core assertion: the two walkers must agree on everything but step count.
    private void AssertWalkersAgree(float3 origin, float3 dir, float maxDist, string label)
    {
        var reference = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var macroSkip = RaymarchReference.TracerRaycastMacroSkip(origin, dir, _store, maxDist);

        Assert.AreEqual(reference.hit, macroSkip.hit, $"{label}: hit flag disagrees");
        if (reference.hit)
        {
            Assert.AreEqual(reference.voxelCoord, macroSkip.voxelCoord, $"{label}: hit voxel disagrees");
            Assert.AreEqual(reference.material, macroSkip.material, $"{label}: material disagrees");
            Assert.AreEqual(reference.normal, macroSkip.normal, $"{label}: normal disagrees");
        }
    }

    // The O(1) leap - the version ported to the shader - must ALSO agree with
    // the per-voxel walk on everything but step count. This is the assertion
    // that actually guards the shipped algorithm.
    private void AssertO1Agrees(float3 origin, float3 dir, float maxDist, string label)
    {
        var reference = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var o1 = RaymarchReference.TracerRaycastO1(origin, dir, _store, maxDist);

        Assert.AreEqual(reference.hit, o1.hit, $"{label}: O1 hit flag disagrees");
        if (reference.hit)
        {
            Assert.AreEqual(reference.voxelCoord, o1.voxelCoord, $"{label}: O1 hit voxel disagrees");
            Assert.AreEqual(reference.material, o1.material, $"{label}: O1 material disagrees");
            Assert.AreEqual(reference.normal, o1.normal, $"{label}: O1 normal disagrees");
        }
    }

    // Also assert the leap actually SAVED work - otherwise a "leap" that silently
    // degraded to the per-voxel walk would pass every equivalence test while
    // fixing nothing. macro-skip outer iterations must be strictly fewer than the
    // walk's voxel visits whenever a long air span exists.
    private void AssertLeapSavesWork(float3 origin, float3 dir, float maxDist, string label)
    {
        var reference = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var macroSkip = RaymarchReference.TracerRaycastMacroSkip(origin, dir, _store, maxDist);
        Assert.Less(macroSkip.steps, reference.steps,
            $"{label}: macro-skip did not reduce iterations ({macroSkip.steps} vs walk {reference.steps}) - leap degenerated to a walk");
    }

    [Test]
    public void MacroSkip_LongAirSpan_Down_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        int3 solid = new int3(4, 3, 4);
        _store.SetVoxel(solid, 5);

        float3 origin = VoxelCentreWorld(new int3(4, 60, 4));
        AssertWalkersAgree(origin, new float3(0, -1, 0), 128f, "long-air-span-down");
        AssertLeapSavesWork(origin, new float3(0, -1, 0), 128f, "long-air-span-down");
    }

    [Test]
    public void MacroSkip_OriginOnBrickBoundary_NegativeDir_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        int3 solid = new int3(4, 3, 4);
        _store.SetVoxel(solid, 5);

        // Y = 8.0m places the origin exactly on a brick boundary, travelling -Y.
        float3 origin = new float3(0.45f, 8.0f, 0.45f);
        AssertWalkersAgree(origin, new float3(0, -1, 0), 128f, "boundary-negative-dir");
    }

    [Test]
    public void MacroSkip_NearAxisAligned_HugeTDelta_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int x = 0; x < 40; x++)
            for (int z = 0; z < 40; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);

        float3 origin = VoxelCentreWorld(new int3(4, 50, 4));
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));
        AssertWalkersAgree(origin, dir, 128f, "near-axis-aligned");
    }

    [Test]
    public void MacroSkip_Diagonal_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(30 + d, 30 + d, 30 + d), 6);

        float3 origin = VoxelCentreWorld(int3.zero);
        AssertWalkersAgree(origin, math.normalize(new float3(1, 1, 1)), 128f, "diagonal");
    }

    [Test]
    public void MacroSkip_ShallowGrazing_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);

        float3 origin = VoxelCentreWorld(new int3(2, 20, 2));
        float3 dir = math.normalize(new float3(1f, -0.25f, 0.7f));
        AssertWalkersAgree(origin, dir, 200f, "shallow-grazing");
    }

    [Test]
    public void MacroSkip_Miss_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        float3 origin = VoxelCentreWorld(new int3(4, 60, 4));
        AssertWalkersAgree(origin, new float3(0, -1, 0), 8f, "miss");
    }

    [Test]
    public void MacroSkip_Sweep_ManyDirections_AgreeWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int x = 0; x < 80; x++)
            for (int z = 0; z < 80; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);

        float3 origin = VoxelCentreWorld(new int3(8, 40, 8));
        for (int i = -8; i <= 8; i++)
            for (int j = -8; j <= 8; j++)
            {
                float3 dir = math.normalize(new float3(i * 0.05f, -1f, j * 0.05f));
                AssertWalkersAgree(origin, dir, 200f, $"sweep({i},{j})");
            }
    }

    // ---------------------------------------------------------------------
    //  New leap-specific cases: the geometries where a tMax re-seed bug would
    //  land one voxel off. These are the cases the OLD float-floor sibling
    //  could not have caught (CPU float rounded right); with the tMax leap they
    //  must pass by construction, and if a future edit breaks the structure
    //  they localize where.
    // ---------------------------------------------------------------------

    [Test]
    public void MacroSkip_PositiveDir_LongAirSpan_Up_AgreesWithReference()
    {
        // Leap in the +Y direction (step.y > 0): exercises the step>0 branch of
        // the tMax seed, the mirror of the -Y cases above.
        MakeAirChunk(int3.zero);
        int3 solid = new int3(4, 100, 4);
        _store.SetVoxel(solid, 7);

        float3 origin = VoxelCentreWorld(new int3(4, 4, 4));
        AssertWalkersAgree(origin, new float3(0, 1, 0), 128f, "long-air-span-up");
        AssertLeapSavesWork(origin, new float3(0, 1, 0), 128f, "long-air-span-up");
    }

    [Test]
    public void MacroSkip_NegativeXYZ_Diagonal_AgreesWithReference()
    {
        // All three step components negative: exercises the else-branches of the
        // tMax seed on every axis simultaneously, the quadrant the historical
        // negative-coordinate bugs lived in.
        MakeAirChunk(int3.zero);
        MakeAirChunk(new int3(-1, -1, -1));
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(2 - d, 2 - d, 2 - d), 6);

        float3 origin = VoxelCentreWorld(new int3(60, 60, 60));
        AssertWalkersAgree(origin, math.normalize(new float3(-1, -1, -1)), 200f, "neg-xyz-diagonal");
        AssertLeapSavesWork(origin, math.normalize(new float3(-1, -1, -1)), 200f, "neg-xyz-diagonal");
    }

    [Test]
    public void MacroSkip_OriginOnBrickBoundary_PositiveDir_AgreesWithReference()
    {
        // Origin exactly on a brick boundary travelling +Y: the tExit-could-be-
        // zero degenerate case, mirror of the negative-dir boundary test.
        MakeAirChunk(int3.zero);
        int3 solid = new int3(4, 100, 4);
        _store.SetVoxel(solid, 7);

        float3 origin = new float3(0.45f, 8.0f, 0.45f);
        AssertWalkersAgree(origin, new float3(0, 1, 0), 128f, "boundary-positive-dir");
    }

    [Test]
    public void MacroSkip_FineSweep_ManyAngles_AgreeWithReference()
    {
        // A denser angular sweep than the coarse one, over a floor with a few
        // raised pillars so different rays terminate at different depths - the
        // strongest single guard against an angle-dependent one-voxel skew.
        MakeAirChunk(int3.zero);
        for (int x = 0; x < 100; x++)
            for (int z = 0; z < 100; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        // pillars
        for (int p = 0; p < 5; p++)
            for (int h = 2; h < 6; h++)
                _store.SetVoxel(new int3(20 + p * 12, h, 20 + p * 12), 6);

        float3 origin = VoxelCentreWorld(new int3(10, 55, 10));
        for (int i = -12; i <= 12; i++)
            for (int j = -12; j <= 12; j++)
            {
                float3 dir = math.normalize(new float3(i * 0.03f, -1f, j * 0.03f));
                AssertWalkersAgree(origin, dir, 200f, $"finesweep({i},{j})");
            }
    }

    // ---------------------------------------------------------------------
    //  O(1) LEAP cases - the version actually ported to Raymarch.compute.
    //  Every geometry the walk-style leap is tested on, re-asserted for the
    //  O(1) leap, plus a randomized differential fuzz that is the real guard
    //  against an angle-dependent one-voxel skew in the single-step crossing.
    // ---------------------------------------------------------------------

    [Test]
    public void O1_LongAirSpan_Down_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        _store.SetVoxel(new int3(4, 3, 4), 5);
        float3 origin = VoxelCentreWorld(new int3(4, 60, 4));
        AssertO1Agrees(origin, new float3(0, -1, 0), 128f, "o1-long-air-down");
    }

    [Test]
    public void O1_LongAirSpan_Up_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        _store.SetVoxel(new int3(4, 100, 4), 7);
        float3 origin = VoxelCentreWorld(new int3(4, 4, 4));
        AssertO1Agrees(origin, new float3(0, 1, 0), 128f, "o1-long-air-up");
    }

    [Test]
    public void O1_Diagonal_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(30 + d, 30 + d, 30 + d), 6);
        float3 origin = VoxelCentreWorld(int3.zero);
        AssertO1Agrees(origin, math.normalize(new float3(1, 1, 1)), 128f, "o1-diagonal");
    }

    [Test]
    public void O1_NegativeXYZ_Diagonal_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        MakeAirChunk(new int3(-1, -1, -1));
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(2 - d, 2 - d, 2 - d), 6);
        float3 origin = VoxelCentreWorld(new int3(60, 60, 60));
        AssertO1Agrees(origin, math.normalize(new float3(-1, -1, -1)), 200f, "o1-neg-diagonal");
    }

    [Test]
    public void O1_OriginOnBrickBoundary_NegativeDir_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        _store.SetVoxel(new int3(4, 3, 4), 5);
        float3 origin = new float3(0.45f, 8.0f, 0.45f);
        AssertO1Agrees(origin, new float3(0, -1, 0), 128f, "o1-boundary-neg");
    }

    [Test]
    public void O1_OriginOnBrickBoundary_PositiveDir_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        _store.SetVoxel(new int3(4, 100, 4), 7);
        float3 origin = new float3(0.45f, 8.0f, 0.45f);
        AssertO1Agrees(origin, new float3(0, 1, 0), 128f, "o1-boundary-pos");
    }

    [Test]
    public void O1_NearAxisAligned_HugeTDelta_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int x = 0; x < 40; x++)
            for (int z = 0; z < 40; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);
        float3 origin = VoxelCentreWorld(new int3(4, 50, 4));
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));
        AssertO1Agrees(origin, dir, 128f, "o1-near-axis");
    }

    [Test]
    public void O1_ShallowGrazing_AgreesWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        float3 origin = VoxelCentreWorld(new int3(2, 20, 2));
        float3 dir = math.normalize(new float3(1f, -0.25f, 0.7f));
        AssertO1Agrees(origin, dir, 200f, "o1-shallow");
    }

    [Test]
    public void O1_FineSweep_ManyAngles_AgreeWithReference()
    {
        MakeAirChunk(int3.zero);
        for (int x = 0; x < 100; x++)
            for (int z = 0; z < 100; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        for (int p = 0; p < 5; p++)
            for (int h = 2; h < 6; h++)
                _store.SetVoxel(new int3(20 + p * 12, h, 20 + p * 12), 6);

        float3 origin = VoxelCentreWorld(new int3(10, 55, 10));
        for (int i = -12; i <= 12; i++)
            for (int j = -12; j <= 12; j++)
            {
                float3 dir = math.normalize(new float3(i * 0.03f, -1f, j * 0.03f));
                AssertO1Agrees(origin, dir, 200f, $"o1-finesweep({i},{j})");
            }
    }

    [Test]
    public void O1_RandomizedFuzz_AgreesWithReference()
    {
        // The real guard: many random rays over a random solid field. A
        // deterministic seed makes any failure reproducible. This is the case
        // the old float-floor sibling could never have vetted (CPU float
        // rounded right); the O(1) leap must pass it by integer construction.
        //
        // This test uses its OWN larger BrickDataPool rather than the shared
        // [SetUp] pool(400): the random field touches more distinct bricks than
        // 400 during scene construction, and exhausting the pool in SetUp is a
        // test-data limit, not a leap bug. Every other test keeps the small
        // shared pool untouched. The larger field also keeps the fuzz's air
        // spans long, which is what actually stresses the leap.
        var pool = new BrickDataPool(20000);
        var allocator = new ChunkHandleAllocator(64);
        var store = new ChunkStore(pool, allocator);
        try
        {
            Chunk airChunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 0 };
            store.InsertChunk(airChunk);

            var rng = new System.Random(20260806);

            int floorY = 1;
            for (int x = 0; x < 120; x++)
                for (int z = 0; z < 120; z++)
                    if (rng.NextDouble() < 0.9)
                        store.SetVoxel(new int3(x, floorY, z), (byte)(2 + rng.Next(6)));
            for (int b = 0; b < 400; b++)
                store.SetVoxel(
                    new int3(rng.Next(0, 120), rng.Next(floorY, floorY + 90), rng.Next(0, 120)),
                    (byte)(2 + rng.Next(6)));

            for (int ray = 0; ray < 3000; ray++)
            {
                float ox = (float)(rng.NextDouble() * 10 + 2);
                float oy = (float)((floorY + 5 + rng.NextDouble() * 85) * 0.1);
                float oz = (float)(rng.NextDouble() * 10 + 2);
                float3 origin = new float3(ox, oy, oz);

                float dx = (float)(rng.NextDouble() * 2 - 1);
                float dy = (float)(rng.NextDouble() * 2 - 1);
                float dz = (float)(rng.NextDouble() * 2 - 1);
                if (math.abs(dx) < 1e-4f && math.abs(dy) < 1e-4f && math.abs(dz) < 1e-4f) dy = -1f;
                float3 dir = math.normalize(new float3(dx, dy, dz));

                var reference = RaymarchReference.TracerRaycast(origin, dir, store, 200f);
                var o1 = RaymarchReference.TracerRaycastO1(origin, dir, store, 200f);
                Assert.AreEqual(reference.hit, o1.hit, $"o1-fuzz(ray={ray}): hit flag disagrees");
                if (reference.hit)
                {
                    Assert.AreEqual(reference.voxelCoord, o1.voxelCoord, $"o1-fuzz(ray={ray}): hit voxel disagrees");
                    Assert.AreEqual(reference.material, o1.material, $"o1-fuzz(ray={ray}): material disagrees");
                    Assert.AreEqual(reference.normal, o1.normal, $"o1-fuzz(ray={ray}): normal disagrees");
                }
            }
        }
        finally
        {
            pool.Dispose();
        }
    }
}