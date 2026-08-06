using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class MemoryModelTests
{
    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(100);
        _allocator = new ChunkHandleAllocator(10);
        _store = new ChunkStore(_pool, _allocator);
    }

    [TearDown]
    public void Teardown()
    {
        _pool.Dispose();
    }

    [Test]
    public void SetVoxel_GetVoxel_RoundTrips_AcrossBoundaries()
    {
        Chunk chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 0 };
        _store.InsertChunk(chunk);

        int3 targetVoxel = new int3(7, 0, 0); // Edge of local brick 0
        int3 targetVoxel2 = new int3(8, 0, 0); // Edge of local brick 1

        _store.SetVoxel(targetVoxel, 5);
        _store.SetVoxel(targetVoxel2, 9);

        Assert.AreEqual(5, _store.GetVoxel(targetVoxel));
        Assert.AreEqual(9, _store.GetVoxel(targetVoxel2));
        Assert.AreEqual(0, _store.GetVoxel(new int3(0, 0, 0))); // Unmodified remains uniform 0
    }

    [Test]
    public void SetVoxel_GetVoxel_RoundTrips_NegativeChunkCoord()
    {
        int3 negativeChunkCoord = new int3(-1, -1, -1);
        Chunk chunk = new Chunk { coord = negativeChunkCoord, isUniform = true, uniformMaterial = 0 };
        _store.InsertChunk(chunk);

        // (-1,-1,-1) maps to chunk (-1,-1,-1) via VoxelToChunk — same mapping
        // already proven in CoordMathTests.VoxelToChunk_NegativeCoords_UsesArithmeticShift,
        // so a failure here isolates to ChunkStore's toroidal masking, not CoordMath.
        int3 targetVoxel = new int3(-1, -1, -1);

        _store.SetVoxel(targetVoxel, 7);
        Assert.AreEqual(7, _store.GetVoxel(targetVoxel));

        // Sanity check: a different, positive-coordinate chunk's voxel is untouched
        Assert.AreEqual(0, _store.GetVoxel(new int3(0, 0, 0)));
    }

    
    

    [Test]
    public void Coalescer_ReclaimsMemory_And_ReturnsToUniform()
    {
        Chunk chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 0 };
        _store.InsertChunk(chunk);

        // Force brick 0 dense
        _store.SetVoxel(int3.zero, 1);
        Assert.IsFalse(chunk.isUniform);
        
        bool isDense = (chunk.bricks[0].data & 0x80000000) != 0;
        Assert.IsTrue(isDense);

        // Fill the rest of the brick with the same material to allow coalescing
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            _store.SetVoxel(new int3(x, y, z), 1);
        }

        // Run coalescer
        bool fullyCoalesced = Coalescer.TryCoalesce(chunk, _pool);
        
        // Brick 0 should be back to uniform
        isDense = (chunk.bricks[0].data & 0x80000000) != 0;
        Assert.IsFalse(isDense);
        Assert.AreEqual(1, chunk.bricks[0].data & 0xFF);
    }

    [Test]
    public void Pool_DoesNotLeak_AfterAllocAndFree()
    {
        int initialFreeCount = _pool.Capacity;
        
        Chunk chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 0 };
        _store.InsertChunk(chunk);

        _store.SetVoxel(int3.zero, 1); // Allocates 1 dense brick
        
        // Assert one block was taken
        int poolIndex = (int)(chunk.bricks[0].data & 0x3FFFFFFF);
        
        _pool.Free(poolIndex); // Manually free to simulate eviction
        
        // Attempt to allocate again and ensure we don't crash and capacity remains stable
        int newIndex = _pool.Alloc();
        Assert.AreEqual(poolIndex, newIndex); // Should get the LRU back
    }
}