using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class RaymarchTests
{
    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(10);
        _allocator = new ChunkHandleAllocator(2);
        _store = new ChunkStore(_pool, _allocator);
        
        Chunk chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 0 };
        _store.InsertChunk(chunk);
    }

    [TearDown]
    public void Teardown()
    {
        _pool.Dispose();
    }

    [Test]
    public void TracerRaycast_HitsKnownVoxel_ReturnsCorrectMaterialAndNormal()
    {
        int3 targetVoxel = new int3(5, 0, 0);
        _store.SetVoxel(targetVoxel, 7);

        float3 origin = new float3(0f, 0.05f, 0.05f);
        float3 dir = new float3(1f, 0f, 0f);

        RaymarchReference.RayHit hit = RaymarchReference.TracerRaycast(origin, dir, _store, 10f);

        Assert.IsTrue(hit.hit, "Ray did not hit target voxel.");
        Assert.AreEqual(targetVoxel, hit.voxelCoord, "Ray hit incorrect voxel coordinate.");
        Assert.AreEqual(7, hit.material, "Ray returned incorrect material.");
        Assert.AreEqual(new int3(-1, 0, 0), hit.normal, "Ray returned incorrect face normal.");
    }
}
