// Assets/CoreEngine/Tests/RaymarchOccupancyTests.cs
//
// Amendment 8.8 — Phase B tests.
//
// CORRECTNESS BAR: identical to every other tracer sibling in this codebase —
// TracerRaycastOccupancy must return BIT-IDENTICAL final ray outcomes (hit,
// voxel, material, normal) to TracerRaycast (the oracle). Chaining sibling
// leaps changes ONLY how many times the already-proven LeapSpanReseed is
// called per outer iteration, not any arithmetic within it — but that
// argument is exactly the kind of reasoning this project's own history
// (Amendment 8.7's three reseed attempts) says not to trust without a fuzz
// checking the actual final outcome. So it's checked directly here, same as
// everywhere else.
//
// WORK-SAVING: the entire point of Phase B is fewer OUTER iterations on long
// straight air runs. Mip_ChainingSavesOuterSteps constructs a tall vertical
// air column spanning multiple L4 cells (the Y=84 top-down worst-case shape)
// and asserts TracerRaycastOccupancy's outer step count is strictly less
// than TracerRaycastMipReseed's on the SAME ray — otherwise this whole
// mechanism would be free (per the correctness tests) but pointless.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class RaymarchOccupancyTests
{
    private static readonly int3 SmallWindowBricks = new int3(64, 64, 64);
    // A taller window, used only by the chaining-savings test, so there are
    // enough stacked L4 cells (16 bricks each) vertically to actually exercise
    // multi-leap chaining rather than a single cell.
    private static readonly int3 TallWindowBricks = new int3(64, 256, 64);

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

    private void FillWindowWithAir(int3 windowBricks)
    {
        int3 windowChunks = windowBricks / 16;
        for (int cz = 0; cz < windowChunks.z; cz++)
        for (int cy = 0; cy < windowChunks.y; cy++)
        for (int cx = 0; cx < windowChunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
    }

    private (AirMipData mips, OccupancyMaskData occupancy) BuildStructures(int3 windowBricks)
    {
        uint[] l0 = AirMip.BuildL0FromStore(_store, windowBricks);
        AirMipData mips = AirMip.Build(l0, windowBricks, 4);
        OccupancyMaskData occupancy = OccupancyMask.Build(l0, mips);
        return (mips, occupancy);
    }

    private static float3 VoxelCentreWorld(int3 voxel) =>
        (new float3(voxel.x, voxel.y, voxel.z) + 0.5f) * 0.1f;

    private void AssertOccupancyAgreesWithOracle(float3 origin, float3 dir, float maxDist,
        AirMipData mips, OccupancyMaskData occupancy, string label)
    {
        var oracle = RaymarchReference.TracerRaycast(origin, dir, _store, maxDist);
        var occ = RaymarchReference.TracerRaycastOccupancy(origin, dir, _store, mips, occupancy, maxDist);

        Assert.AreEqual(oracle.hit, occ.hit, $"{label}: hit flag disagrees");
        if (oracle.hit)
        {
            Assert.AreEqual(oracle.voxelCoord, occ.voxelCoord, $"{label}: hit voxel disagrees");
            Assert.AreEqual(oracle.material, occ.material, $"{label}: material disagrees");
            Assert.AreEqual(oracle.normal, occ.normal, $"{label}: normal disagrees");
        }
    }

    // ---------------------------------------------------------------------
    //  Structured geometries — mirrors RaymarchMipReseedTests' own suite,
    //  since this tracer must be at least as correct on every case that
    //  suite already covers.
    // ---------------------------------------------------------------------

    [Test]
    public void Occupancy_LongAirSpan_Down_AgreesWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        _store.SetVoxel(new int3(20, 3, 20), 5);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertOccupancyAgreesWithOracle(origin, new float3(0, -1, 0), 128f, mips, occ, "occ-long-air-down");
    }

    [Test]
    public void Occupancy_LongAirSpan_Up_AgreesWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        _store.SetVoxel(new int3(20, 400, 20), 7);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(new int3(20, 4, 20));
        AssertOccupancyAgreesWithOracle(origin, new float3(0, 1, 0), 128f, mips, occ, "occ-long-air-up");
    }

    [Test]
    public void Occupancy_Diagonal_AgreesWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(100 + d, 100 + d, 100 + d), 6);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(int3.zero);
        AssertOccupancyAgreesWithOracle(origin, math.normalize(new float3(1, 1, 1)), 128f, mips, occ, "occ-diagonal");
    }

    [Test]
    public void Occupancy_NegativeXYZ_Diagonal_AgreesWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(10 - d, 10 - d, 10 - d), 6);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(new int3(300, 300, 300));
        AssertOccupancyAgreesWithOracle(origin, math.normalize(new float3(-1, -1, -1)), 128f, mips, occ, "occ-neg-diagonal");
    }

    [Test]
    public void Occupancy_OriginOnCellBoundary_NegativeDir_AgreesWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        _store.SetVoxel(new int3(20, 3, 20), 5);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = new float3(2.05f, 12.8f, 2.05f); // L4 cell boundary
        AssertOccupancyAgreesWithOracle(origin, new float3(0, -1, 0), 128f, mips, occ, "occ-boundary-neg");
    }

    [Test]
    public void Occupancy_NearAxisAligned_HugeTDelta_AgreesWithOracle()
    {
        // The exact pathological shape (near-vertical, tiny X/Z components)
        // that motivated the whole investigation.
        FillWindowWithAir(SmallWindowBricks);
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(new int3(20, 300, 20));
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));
        AssertOccupancyAgreesWithOracle(origin, dir, 128f, mips, occ, "occ-near-axis-PATHOLOGICAL-CASE");
    }

    [Test]
    public void Occupancy_ShallowGrazing_AgreesWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(new int3(2, 40, 2));
        float3 dir = math.normalize(new float3(1f, -0.25f, 0.7f));
        AssertOccupancyAgreesWithOracle(origin, dir, 200f, mips, occ, "occ-shallow");
    }

    [Test]
    public void Occupancy_Miss_AgreesWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(new int3(20, 400, 20));
        AssertOccupancyAgreesWithOracle(origin, new float3(0, -1, 0), 8f, mips, occ, "occ-miss");
    }

    [Test]
    public void Occupancy_FineSweep_ManyAngles_AgreeWithOracle()
    {
        FillWindowWithAir(SmallWindowBricks);
        for (int x = 0; x < 60; x++)
            for (int z = 0; z < 60; z++)
                _store.SetVoxel(new int3(x, 1, z), 5);
        for (int p = 0; p < 4; p++)
            for (int h = 2; h < 6; h++)
                _store.SetVoxel(new int3(10 + p * 12, h, 10 + p * 12), 6);
        var (mips, occ) = BuildStructures(SmallWindowBricks);
        float3 origin = VoxelCentreWorld(new int3(6, 55, 6));
        for (int i = -12; i <= 12; i++)
            for (int j = -12; j <= 12; j++)
            {
                float3 dir = math.normalize(new float3(i * 0.03f, -1f, j * 0.03f));
                AssertOccupancyAgreesWithOracle(origin, dir, 200f, mips, occ, $"occ-finesweep({i},{j})");
            }
    }

    // ---------------------------------------------------------------------
    //  Work-saving: a tall vertical air column spanning multiple L4 cells
    //  (the Y=84 top-down worst-case shape) must produce STRICTLY FEWER
    //  outer steps than TracerRaycastMipReseed on the identical ray -
    //  otherwise chaining is buying nothing.
    // ---------------------------------------------------------------------
    [Test]
    public void Occupancy_ChainingReducesOuterSteps_OnTallVerticalColumn()
    {
        FillWindowWithAir(TallWindowBricks);
        // Floor near the bottom, ceiling far above - the ray must cross many
        // stacked L4 cells (16 bricks = 128 voxels each) of pure air.
        _store.SetVoxel(new int3(20, 3, 20), 5);
        uint[] l0 = AirMip.BuildL0FromStore(_store, TallWindowBricks);
        AirMipData mips = AirMip.Build(l0, TallWindowBricks, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        float3 origin = VoxelCentreWorld(new int3(20, 2000, 20)); // near the top of the tall window
        float3 dir = new float3(0, -1, 0);

        var reseed = RaymarchReference.TracerRaycastMipReseed(origin, dir, _store, mips, 300f);
        var occupancy = RaymarchReference.TracerRaycastOccupancy(origin, dir, _store, mips, occ, 300f);

        Assert.AreEqual(reseed.hit, occupancy.hit, "must still agree on hit");
        if (reseed.hit)
        {
            Assert.AreEqual(reseed.voxelCoord, occupancy.voxelCoord, "must still agree on hit voxel");
        }
        Assert.Less(occupancy.steps, reseed.steps,
            $"chaining should reduce outer steps on a tall vertical air column: " +
            $"occupancy={occupancy.steps} reseed={reseed.steps}");
    }

    // ---------------------------------------------------------------------
    //  Randomized fuzz — the real guard. Occupancy tracer vs oracle, biased
    //  toward near-axis-aligned directions (matches the dominant-axis chaining
    //  path this tracer specifically adds) plus uniform-random for coverage.
    // ---------------------------------------------------------------------
    [Test]
    public void Occupancy_RandomizedFuzz_AgreesWithOracle()
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

            var rng = new System.Random(20260813);
            int floorY = 1;
            for (int x = 0; x < 200; x++)
                for (int z = 0; z < 200; z++)
                    if (rng.NextDouble() < 0.9)
                        store.SetVoxel(new int3(x, floorY, z), (byte)(2 + rng.Next(6)));
            for (int b = 0; b < 600; b++)
                store.SetVoxel(
                    new int3(rng.Next(0, 200), rng.Next(floorY, floorY + 300), rng.Next(0, 200)),
                    (byte)(2 + rng.Next(6)));

            uint[] l0 = AirMip.BuildL0FromStore(store, SmallWindowBricks);
            AirMipData mips = AirMip.Build(l0, SmallWindowBricks, 4);
            OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

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
                var occupancy = RaymarchReference.TracerRaycastOccupancy(origin, dir, store, mips, occ, 200f);

                Assert.AreEqual(oracle.hit, occupancy.hit, $"occ-fuzz(ray={ray}): hit flag disagrees, dir={dir}");
                if (oracle.hit)
                {
                    Assert.AreEqual(oracle.voxelCoord, occupancy.voxelCoord, $"occ-fuzz(ray={ray}): hit voxel disagrees, dir={dir}");
                    Assert.AreEqual(oracle.material, occupancy.material, $"occ-fuzz(ray={ray}): material disagrees, dir={dir}");
                    Assert.AreEqual(oracle.normal, occupancy.normal, $"occ-fuzz(ray={ray}): normal disagrees, dir={dir}");
                }
            }
        }
        finally
        {
            pool.Dispose();
        }
    }
}