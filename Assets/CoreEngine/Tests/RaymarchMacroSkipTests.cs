using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

// The oracle property for the macro-skip: for the SAME ray, TracerRaycastMacroSkip
// must return BIT-IDENTICAL results to the proven per-voxel TracerRaycast - same
// hit, same voxel, same material, same normal. A correct macro-skip only visits
// fewer voxels; it never lands anywhere different. Any disagreement is a
// macro-skip bug by construction.
//
// These are the tests that would have caught the tear visible in the Y=40
// screenshot (long uniform-air spans, where macro-skip does the most work and
// where the artifact is worst). If a case here fails, it localizes the bug to a
// specific ray geometry the GPU kernel shares.
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

    [Test]
    public void MacroSkip_LongAirSpan_Down_AgreesWithReference()
    {
        // A solid floor many bricks below the origin - the leap must cross
        // several uniform-air bricks and still land on the exact floor voxel.
        MakeAirChunk(int3.zero);
        int3 solid = new int3(4, 3, 4);
        _store.SetVoxel(solid, 5);

        // Origin ~60 voxels up: forces multiple brick leaps straight down.
        float3 origin = VoxelCentreWorld(new int3(4, 60, 4));
        AssertWalkersAgree(origin, new float3(0, -1, 0), 128f, "long-air-span-down");
    }

    [Test]
    public void MacroSkip_OriginOnBrickBoundary_NegativeDir_AgreesWithReference()
    {
        // Degenerate case #1 from the GPU kernel's own comment: origin sits
        // exactly on an integer brick boundary, travelling negative on that
        // axis. This is where tExit could evaluate to 0 and stall the leap.
        MakeAirChunk(int3.zero);
        int3 solid = new int3(4, 3, 4);
        _store.SetVoxel(solid, 5);

        // Y = 8.0m places the origin exactly on the boundary between brick
        // rows (voxel 80 = brick 10 boundary), travelling -Y.
        float3 origin = new float3(0.45f, 8.0f, 0.45f);
        AssertWalkersAgree(origin, new float3(0, -1, 0), 128f, "boundary-negative-dir");
    }

    [Test]
    public void MacroSkip_NearAxisAligned_HugeTDelta_AgreesWithReference()
    {
        // Degenerate case #2: a ray almost but not quite axis-aligned, so one
        // axis has a huge tDelta. This is the "one-voxel settling ambiguity"
        // the kernel comment describes and the likeliest source of the
        // diagonal-line tear in the Y=40 screenshot.
        MakeAirChunk(int3.zero);
        // A broad solid floor so the ray reliably lands on something.
        for (int x = 0; x < 40; x++)
            for (int z = 0; z < 40; z++)
                _store.SetVoxel(new int3(x, 2, z), 5);

        float3 origin = VoxelCentreWorld(new int3(4, 50, 4));
        // Mostly -Y, with a tiny X/Z tilt -> huge tDelta on X and Z.
        float3 dir = math.normalize(new float3(0.001f, -1f, 0.0015f));
        AssertWalkersAgree(origin, dir, 128f, "near-axis-aligned");
    }

    [Test]
    public void MacroSkip_Diagonal_AgreesWithReference()
    {
        // A clean 3-axis diagonal - all tDelta finite, the "easy" case that
        // should always work if the leap math is right at all.
        MakeAirChunk(int3.zero);
        for (int d = 0; d < 4; d++)
            _store.SetVoxel(new int3(30 + d, 30 + d, 30 + d), 6);

        float3 origin = VoxelCentreWorld(int3.zero);
        AssertWalkersAgree(origin, math.normalize(new float3(1, 1, 1)), 128f, "diagonal");
    }

    [Test]
    public void MacroSkip_ShallowGrazing_AgreesWithReference()
    {
        // A shallow, near-horizontal ray over a floor - long spans of air with
        // a low-angle approach, the geometry that fills most of the Y=40 frame.
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
        // Pure air, no solid: both must miss and terminate on distance.
        MakeAirChunk(int3.zero);
        float3 origin = VoxelCentreWorld(new int3(4, 60, 4));
        AssertWalkersAgree(origin, new float3(0, -1, 0), 8f, "miss");
    }

    [Test]
    public void MacroSkip_Sweep_ManyDirections_AgreeWithReference()
    {
        // The screenshot showed a per-pixel pattern - neighboring rays behaving
        // differently. This sweeps a fan of directions from one origin over a
        // floor and asserts EVERY ray agrees, the CPU analog of the pixel sweep
        // that found "zero anomalies" but couldn't see the macro-skip because
        // the old reference had none.
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
}