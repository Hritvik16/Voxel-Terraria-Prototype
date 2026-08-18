// Assets/CoreEngine/Tests/RaymarchMipReseedTests.cs
//
// Amendment 8.7 — Divergence fix attempt #3 (reseed), CPU proof.
//
// CORRECTNESS BAR: the same one every tracer in RaymarchReference.cs is held
// to - TracerRaycastMipReseed must return BIT-IDENTICAL final ray outcomes
// (hit, voxel, material, normal) to TracerRaycast (the oracle), on every ray.
// This is NOT a "does tMax match the loop's tMax" test (that question was
// shown to be the wrong one - see the file-header comment in
// RaymarchReference.cs for why bit-exact tMax agreement is neither achievable
// nor necessary). Internal tMax representations may differ between the reseed
// variant and the original LeapSpan; only the final observable outcome must
// agree, exactly like O1 vs walk and Mip vs walk already do.
//
// Mirrors RaymarchMipTests.cs structure/geometries so this gets the same
// coverage the shipped mip tracer already has, plus the fuzz is the real guard.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class RaymarchMipReseedTests
{
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

    private void FillSmallWindowWithAir()
    {
        int3 windowChunks = SmallWindowBricks / 16;
        for (int cz = 0; cz < windowChunks.z; cz++)
        for (int cy = 0; cy < windowChunks.y; cy++)
        for (int cx = 0; cx < windowChunks.x; cx++)
        {
            var chunk = new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 };
            _store.InsertChunk(chunk);
        }
    }

    private AirMipData BuildMips() => AirMip.BuildFromStore(_store, SmallWindowBricks, 4);

    private static float3 VoxelCentreWorld(int3 voxel) =>
        (new float3(voxel.x, voxel.y, voxel.z) + 0.5f) * 0.1f;

    // The core assertion: RESEED variant vs the ORACLE (not vs the original
    // Mip tracer). This is the correct comparison per the reframed bar.
    private void AssertReseedAgreesWithOracle(float3 origin, float3 dir, float maxDist, string label)
    {
        AirMipData mips = BuildMips();
        var oracle = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var reseed = RaymarchReference.TracerRaycastMipReseed(origin, dir, _store, mips, maxDist);

        Assert.AreEqual(oracle.hit, reseed.hit, $"{label}: hit flag disagrees");
        if (oracle.hit)
        {
            Assert.AreEqual(oracle.voxelCoord, reseed.voxelCoord, $"{label}: hit voxel disagrees");
            Assert.AreEqual(oracle.material, reseed.material, $"{label}: material disagrees");
            Assert.AreEqual(oracle.normal, reseed.normal, $"{label}: normal disagrees");
        }
    }

    // ---------------------------------------------------------------------
    //  Structured geometries mirroring RaymarchMipTests.
    // ---------------------------------------------------------------------

    [Test]
    public void Reseed_LongAirSpan_Down_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertReseedAgreesWithOracle(origin, new float3(0, -1, 0), 128f, "reseed-long-air-down");
    }

    [Test]
    public void Reseed_LongAirSpan_Up_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 400, 20), 7);
        float3 origin = VoxelCentreWorld(new int3(20, 4, 20));
        AssertReseedAgreesWithOracle(origin, new float3(0, 1, 0), 128f, "reseed-long-air-up");
    }

    [Test]
    public void Reseed_Diagonal_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(100 + d, 100 + d, 100 + d), 6);
        float3 origin = VoxelCentreWorld(int3.zero);
        AssertReseedAgreesWithOracle(origin, math.normalize(new float3(1, 1, 1)), 128f, "reseed-diagonal");
    }

    [Test]
    public void Reseed_NegativeXYZ_Diagonal_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(10 - d, 10 - d, 10 - d), 6);
        float3 origin = VoxelCentreWorld(new int3(300, 300, 300));
        AssertReseedAgreesWithOracle(origin, math.normalize(new float3(-1, -1, -1)), 128f, "reseed-neg-diagonal");
    }

    [Test]
    public void Reseed_OriginOnCellBoundary_NegativeDir_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);
        float3 origin = new float3(2.05f, 12.8f, 2.05f);
        AssertReseedAgreesWithOracle(origin, new float3(0, -1, 0), 128f, "reseed-boundary-neg");
    }

    [Test]
    public void Reseed_OriginOnCellBoundary_PositiveDir_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 400, 20), 7);
        float3 origin = new float3(2.05f, 12.8f, 2.05f);
        AssertReseedAgreesWithOracle(origin, new float3(0, 1, 0), 128f, "reseed-boundary-pos");
    }

    [Test]
    public void Reseed_NearAxisAligned_HugeTDelta_AgreesWithOracle()
    {
        // This is the EXACT pathology that motivated the fix: a near-vertical
        // ray with tDelta ~935 on the horizontal axes, matching the real
        // measured pixel (rayDir ~ (0.00107,-0.99999,0.00107)).
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);
        float3 origin = VoxelCentreWorld(new int3(20, 300, 20));
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));
        AssertReseedAgreesWithOracle(origin, dir, 128f, "reseed-near-axis-PATHOLOGICAL-CASE");
    }

    [Test]
    public void Reseed_ShallowGrazing_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        float3 origin = VoxelCentreWorld(new int3(2, 40, 2));
        float3 dir = math.normalize(new float3(1f, -0.25f, 0.7f));
        AssertReseedAgreesWithOracle(origin, dir, 200f, "reseed-shallow");
    }

    [Test]
    public void Reseed_Miss_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertReseedAgreesWithOracle(origin, new float3(0, -1, 0), 8f, "reseed-miss");
    }

    [Test]
    public void Reseed_FineSweep_ManyAngles_AgreeWithOracle()
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
                var oracle = RaymarchReference.TracerRaycast(origin, dir, _store, 200f);
                var reseed = RaymarchReference.TracerRaycastMipReseed(origin, dir, _store, mips, 200f);
                Assert.AreEqual(oracle.hit, reseed.hit, $"finesweep({i},{j}): hit disagrees");
                if (oracle.hit)
                {
                    Assert.AreEqual(oracle.voxelCoord, reseed.voxelCoord, $"finesweep({i},{j}): voxel disagrees");
                    Assert.AreEqual(oracle.material, reseed.material, $"finesweep({i},{j}): material disagrees");
                    Assert.AreEqual(oracle.normal, reseed.normal, $"finesweep({i},{j}): normal disagrees");
                }
            }
    }

    // ---------------------------------------------------------------------
    //  Work-saving sanity: exit-axis work should collapse from O(exitCount)
    //  loop iterations to O(1) per leap (one reseed each), on the exact
    //  pathological ray that originally measured 825 iterations, all on the
    //  exit axis.
    // ---------------------------------------------------------------------

    [Test]
    public void Reseed_CollapsesExitAxisWork_OnPathologicalRay()
    {
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);

        AirMipData mips = BuildMips();
        float3 origin = VoxelCentreWorld(new int3(20, 300, 20));
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));

        var (hit, exitLeaps, nonExitIters) =
            RaymarchReference.TracerRaycastMipReseedWithCounts(origin, dir, _store, mips, 128f);

        // exitLeaps now counts LEAPS (O(1) each), not raw loop iterations - it
        // should be small (roughly equal to outer step count), nowhere near
        // the 825 raw iterations the loop version accumulated on the real
        // measured pixel.
        Assert.Less(exitLeaps, 50,
            $"exit-axis work should collapse to ~one O(1) op per leap, got {exitLeaps} leaps - " +
            "if this is large, the reseed isn't actually engaging on this geometry.");
    }

    // ---------------------------------------------------------------------
    //  Randomized fuzz — the real guard. Reseed variant vs oracle, 5000 rays,
    //  biased toward near-axis-aligned directions (the pathological regime)
    //  in addition to uniform-random, since that's where the fix matters most
    //  and where any subtle bug would most likely surface.
    // ---------------------------------------------------------------------

    [Test]
    public void Reseed_RandomizedFuzz_AgreesWithOracle()
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

            var rng = new System.Random(20260812);

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

            for (int ray = 0; ray < 5000; ray++)
            {
                float ox = (float)(rng.NextDouble() * 20 + 2);
                float oy = (float)((floorY + 40 + rng.NextDouble() * 260) * 0.1);
                float oz = (float)(rng.NextDouble() * 20 + 2);
                float3 origin = new float3(ox, oy, oz);

                float3 dir;
                if (ray % 2 == 0)
                {
                    // Half the fuzz: near-axis-aligned (the pathological regime -
                    // tiny X/Z components, dominant Y), matching the real case.
                    float dx = (float)((rng.NextDouble() * 2 - 1) * 0.01);
                    float dz = (float)((rng.NextDouble() * 2 - 1) * 0.01);
                    float dy = rng.NextDouble() < 0.5 ? -1f : 1f;
                    dir = math.normalize(new float3(dx, dy, dz));
                }
                else
                {
                    // Other half: uniform-random directions, general coverage.
                    float dx = (float)(rng.NextDouble() * 2 - 1);
                    float dy = (float)(rng.NextDouble() * 2 - 1);
                    float dz = (float)(rng.NextDouble() * 2 - 1);
                    if (math.abs(dx) < 1e-4f && math.abs(dy) < 1e-4f && math.abs(dz) < 1e-4f) dy = -1f;
                    dir = math.normalize(new float3(dx, dy, dz));
                }

                var oracle = RaymarchReference.TracerRaycast(origin, dir, store, 200f);
                var reseed = RaymarchReference.TracerRaycastMipReseed(origin, dir, store, mips, 200f);

                Assert.AreEqual(oracle.hit, reseed.hit, $"reseed-fuzz(ray={ray}): hit flag disagrees, dir={dir}");
                if (oracle.hit)
                {
                    Assert.AreEqual(oracle.voxelCoord, reseed.voxelCoord, $"reseed-fuzz(ray={ray}): hit voxel disagrees, dir={dir}");
                    Assert.AreEqual(oracle.material, reseed.material, $"reseed-fuzz(ray={ray}): material disagrees, dir={dir}");
                    Assert.AreEqual(oracle.normal, reseed.normal, $"reseed-fuzz(ray={ray}): normal disagrees, dir={dir}");
                }
            }
        }
        finally
        {
            pool.Dispose();
        }
    }

    // =======================================================================
    //  VARIANT A (CLOSED-FORM), Amendment 8.8/8.9 — mirrors every test above
    //  exactly, substituting TracerRaycastMipReseedClosedForm for
    //  TracerRaycastMipReseed. Same correctness bar: bit-identical final ray
    //  outcome (hit/voxel/material/normal) vs the oracle, TracerRaycast.
    // =======================================================================

    private void AssertClosedFormAgreesWithOracle(float3 origin, float3 dir, float maxDist, string label)
    {
        AirMipData mips = BuildMips();
        var oracle = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var closedForm = RaymarchReference.TracerRaycastMipReseedClosedForm(origin, dir, _store, mips, maxDist);

        Assert.AreEqual(oracle.hit, closedForm.hit, $"{label}: hit flag disagrees");
        if (oracle.hit)
        {
            Assert.AreEqual(oracle.voxelCoord, closedForm.voxelCoord, $"{label}: hit voxel disagrees");
            Assert.AreEqual(oracle.material, closedForm.material, $"{label}: material disagrees");
            Assert.AreEqual(oracle.normal, closedForm.normal, $"{label}: normal disagrees");
        }
    }

    [Test]
    public void ClosedForm_LongAirSpan_Down_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertClosedFormAgreesWithOracle(origin, new float3(0, -1, 0), 128f, "closedform-long-air-down");
    }

    [Test]
    public void ClosedForm_LongAirSpan_Up_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 400, 20), 7);
        float3 origin = VoxelCentreWorld(new int3(20, 4, 20));
        AssertClosedFormAgreesWithOracle(origin, new float3(0, 1, 0), 128f, "closedform-long-air-up");
    }

    [Test]
    public void ClosedForm_Diagonal_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(100 + d, 100 + d, 100 + d), 6);
        float3 origin = VoxelCentreWorld(int3.zero);
        AssertClosedFormAgreesWithOracle(origin, math.normalize(new float3(1, 1, 1)), 128f, "closedform-diagonal");
    }

    [Test]
    public void ClosedForm_NegativeXYZ_Diagonal_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(10 - d, 10 - d, 10 - d), 6);
        float3 origin = VoxelCentreWorld(new int3(300, 300, 300));
        AssertClosedFormAgreesWithOracle(origin, math.normalize(new float3(-1, -1, -1)), 128f, "closedform-neg-diagonal");
    }

    [Test]
    public void ClosedForm_OriginOnCellBoundary_NegativeDir_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 3, 20), 5);
        float3 origin = new float3(2.05f, 12.8f, 2.05f);
        AssertClosedFormAgreesWithOracle(origin, new float3(0, -1, 0), 128f, "closedform-boundary-neg");
    }

    [Test]
    public void ClosedForm_OriginOnCellBoundary_PositiveDir_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        _store.SetVoxel(new int3(20, 400, 20), 7);
        float3 origin = new float3(2.05f, 12.8f, 2.05f);
        AssertClosedFormAgreesWithOracle(origin, new float3(0, 1, 0), 128f, "closedform-boundary-pos");
    }

    [Test]
    public void ClosedForm_NearAxisAligned_HugeTDelta_AgreesWithOracle()
    {
        // The exact pathology that originally motivated this whole pass: a
        // near-vertical ray with huge tDelta on the horizontal (non-exit)
        // axes - matching the real measured pixel this session traced the
        // cost to.
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);
        float3 origin = VoxelCentreWorld(new int3(20, 300, 20));
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));
        AssertClosedFormAgreesWithOracle(origin, dir, 128f, "closedform-near-axis-PATHOLOGICAL-CASE");
    }

    [Test]
    public void ClosedForm_ShallowGrazing_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        float3 origin = VoxelCentreWorld(new int3(2, 40, 2));
        float3 dir = math.normalize(new float3(1f, -0.25f, 0.7f));
        AssertClosedFormAgreesWithOracle(origin, dir, 200f, "closedform-shallow");
    }

    [Test]
    public void ClosedForm_Miss_AgreesWithOracle()
    {
        FillSmallWindowWithAir();
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertClosedFormAgreesWithOracle(origin, new float3(0, -1, 0), 8f, "closedform-miss");
    }

    [Test]
    public void ClosedForm_FineSweep_ManyAngles_AgreeWithOracle()
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
                var oracle = RaymarchReference.TracerRaycast(origin, dir, _store, 200f);
                var closedForm = RaymarchReference.TracerRaycastMipReseedClosedForm(origin, dir, _store, mips, 200f);
                Assert.AreEqual(oracle.hit, closedForm.hit, $"cf-finesweep({i},{j}): hit disagrees");
                if (oracle.hit)
                {
                    Assert.AreEqual(oracle.voxelCoord, closedForm.voxelCoord, $"cf-finesweep({i},{j}): voxel disagrees");
                    Assert.AreEqual(oracle.material, closedForm.material, $"cf-finesweep({i},{j}): material disagrees");
                    Assert.AreEqual(oracle.normal, closedForm.normal, $"cf-finesweep({i},{j}): normal disagrees");
                }
            }
    }

    // Same regime split as Reseed_RandomizedFuzz_AgreesWithOracle (half
    // near-axis-aligned/pathological, half uniform-random), same ray count
    // and seed for direct comparability between the two proofs.
    [Test]
    public void ClosedForm_RandomizedFuzz_AgreesWithOracle()
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

            var rng = new System.Random(20260812);

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

            for (int ray = 0; ray < 5000; ray++)
            {
                float ox = (float)(rng.NextDouble() * 20 + 2);
                float oy = (float)((floorY + 40 + rng.NextDouble() * 260) * 0.1);
                float oz = (float)(rng.NextDouble() * 20 + 2);
                float3 origin = new float3(ox, oy, oz);

                float3 dir;
                if (ray % 2 == 0)
                {
                    float dx = (float)((rng.NextDouble() * 2 - 1) * 0.01);
                    float dz = (float)((rng.NextDouble() * 2 - 1) * 0.01);
                    float dy = rng.NextDouble() < 0.5 ? -1f : 1f;
                    dir = math.normalize(new float3(dx, dy, dz));
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
                var closedForm = RaymarchReference.TracerRaycastMipReseedClosedForm(origin, dir, store, mips, 200f);

                Assert.AreEqual(oracle.hit, closedForm.hit, $"cf-fuzz(ray={ray}): hit flag disagrees, dir={dir}");
                if (oracle.hit)
                {
                    Assert.AreEqual(oracle.voxelCoord, closedForm.voxelCoord, $"cf-fuzz(ray={ray}): hit voxel disagrees, dir={dir}");
                    Assert.AreEqual(oracle.material, closedForm.material, $"cf-fuzz(ray={ray}): material disagrees, dir={dir}");
                    Assert.AreEqual(oracle.normal, closedForm.normal, $"cf-fuzz(ray={ray}): normal disagrees, dir={dir}");
                }
            }
        }
        finally
        {
            pool.Dispose();
        }
    }
}