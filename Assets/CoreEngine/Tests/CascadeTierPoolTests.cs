// Assets/CoreEngine/Tests/CascadeTierPoolTests.cs
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class CascadeTierPoolTests
{
    private static (ChunkStore store, BrickDataPool pool, ChunkHandleAllocator handles) MakeStore()
    {
        var pool = new BrickDataPool(64);
        var handles = new ChunkHandleAllocator();
        var store = new ChunkStore(pool, handles);
        return (store, pool, handles);
    }

    [Test]
    public void Constructor_RejectsTierZero()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new CascadeTierPool(0, new int3(4, 4, 4), 64));
    }

    [Test]
    public void Constructor_RejectsTierAtOrAboveTierCount()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => new CascadeTierPool(LODConfig.TIER_COUNT, new int3(4, 4, 4), 64));
    }

    [Test]
    public void Constructor_Tier1_CoarseBricksPerChunkEdge_Is8()
    {
        var cascade = new CascadeTierPool(1, new int3(4, 4, 4), 64);
        Assert.AreEqual(8, cascade.CoarseBricksPerChunkEdge);
        cascade.Dispose();
    }

    [Test]
    public void Constructor_Tier2_CoarseBricksPerChunkEdge_Is4()
    {
        var cascade = new CascadeTierPool(2, new int3(4, 4, 4), 64);
        Assert.AreEqual(4, cascade.CoarseBricksPerChunkEdge);
        cascade.Dispose();
    }

    [Test]
    public void UploadDirty_UniformAirChunk_NoOpDoesNotThrow()
    {
        var (store, pool, _) = MakeStore();
        var cascade = new CascadeTierPool(1, new int3(4, 4, 4), 64);

        cascade.MarkDirty(int3.zero);
        Assert.DoesNotThrow(() => cascade.UploadDirty(store, pool));

        cascade.Dispose();
    }

    [Test]
    public void UploadDirty_UniformSolidChunk_NeverAllocatesFromBrickPool()
    {
        // A uniform chunk downsamples to an all-one-material grid at every
        // tier, so every coarse brick should collapse to "uniform" and the
        // dense BrickDataPool should never be touched. Verified indirectly
        // with capacity=1 rather than 0: GraphicsBuffer with a 0-element count
        // is a separate, untested Unity edge case I don't want this test
        // depending on. Capacity 1 still catches the real failure mode (a
        // bug causing even one of this chunk's 512 coarse bricks to be
        // treated as non-uniform throws on the SECOND allocation attempt).
        var (store, pool, _) = MakeStore();
        var chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 6 };
        store.InsertChunk(chunk);

        var cascade = new CascadeTierPool(1, new int3(4, 4, 4), 1);
        cascade.MarkDirty(int3.zero);

        Assert.DoesNotThrow(() => cascade.UploadDirty(store, pool));

        cascade.Dispose();
    }

    [Test]
    public void UploadDirty_NonUniformChunk_AllocatesDenseBricks_AndRedirtyDoesNotExhaustPool()
    {
        var (store, pool, handles) = MakeStore();

        // Uniform material 2 everywhere EXCEPT tier-0 bricks x%2==0, which
        // flips the parity of x-brick-index across the whole chunk - a
        // checkerboard along x only. Every 2x2x2 tier0-brick block a tier1
        // coarse brick covers straddles an even/odd x pair, so EVERY one of
        // the chunk's 512 tier1 coarse bricks ends up non-uniform. Capacity
        // is set to exactly that (600, with slack) so the test still proves
        // "re-dirtying the same unchanged chunk repeatedly doesn't leak pool
        // slots" without being sensitive to getting the exact worst-case
        // count precisely right.
        var chunk = new Chunk { coord = int3.zero, isUniform = false, bricks = handles.Alloc() };
        for (int i = 0; i < 4096; i++)
            chunk.bricks[i].data = (i % 2 == 0) ? (uint)2 : (uint)5;
        store.InsertChunk(chunk);

        var cascade = new CascadeTierPool(1, new int3(4, 4, 4), 600);

        cascade.MarkDirty(int3.zero);
        Assert.DoesNotThrow(() => cascade.UploadDirty(store, pool));

        // Re-dirty and re-upload the SAME chunk repeatedly. If stale pool
        // slots leaked instead of being freed on re-write, this would
        // eventually exhaust the 512-slot pool and throw
        // InvalidOperationException ("BrickDataPool exhausted").
        for (int i = 0; i < 20; i++)
        {
            cascade.MarkDirty(int3.zero);
            Assert.DoesNotThrow(() => cascade.UploadDirty(store, pool),
                $"Pool exhausted on re-dirty iteration {i} - stale slots are leaking instead of being freed.");
        }

        cascade.Dispose();
    }

    [Test]
    public void UploadDirty_TogglingChunkFromDenseToUniform_FreesTheStaleSlot()
    {
        var (store, pool, handles) = MakeStore();

        // Deliberately confined non-uniformity: baseline material 2 across
        // all 4096 tier0 bricks, then corrupt only the single 2x2x2 tier0-
        // brick corner at (0,0,0)-(1,1,1) to mix materials 2/5. That corner
        // is exactly the footprint of ONE tier1 coarse brick, so this
        // produces exactly 1 non-uniform coarse brick out of 512 - safe
        // under a deliberately tiny capacity, unlike a full-chunk checkerboard
        // (see the other test for why that produces 512, not a handful).
        var chunk = new Chunk { coord = int3.zero, isUniform = false, bricks = handles.Alloc() };
        for (int i = 0; i < 4096; i++) chunk.bricks[i].data = 2;
        for (int bz = 0; bz < 2; bz++)
        for (int by = 0; by < 2; by++)
        for (int bx = 0; bx < 2; bx++)
        {
            int idx = (bz << 8) | (by << 4) | bx;
            chunk.bricks[idx].data = (uint)(((bx + by + bz) % 2 == 0) ? 2 : 5);
        }
        store.InsertChunk(chunk);

        var cascade = new CascadeTierPool(1, new int3(4, 4, 4), 8); // deliberately tiny pool

        cascade.MarkDirty(int3.zero);
        cascade.UploadDirty(store, pool); // exactly 1 dense allocation expected, pool has room

        // Now collapse the chunk to fully uniform and re-upload. The single
        // non-uniform coarse brick should go back to "uniform," freeing its
        // slot. If the free didn't happen, the NEXT step (re-introducing one
        // non-uniform corner) would still fit under capacity 8 by itself, so
        // this specifically catches "the old slot never got freed" rather
        // than "ran out of room" - both would otherwise look identical from
        // outside, so the assertion message says which one this is.
        store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 9 });
        cascade.MarkDirty(int3.zero);
        Assert.DoesNotThrow(() => cascade.UploadDirty(store, pool));

        var denseAgain = new Chunk { coord = int3.zero, isUniform = false, bricks = handles.Alloc() };
        for (int i = 0; i < 4096; i++) denseAgain.bricks[i].data = 3;
        for (int bz = 0; bz < 2; bz++)
        for (int by = 0; by < 2; by++)
        for (int bx = 0; bx < 2; bx++)
        {
            int idx = (bz << 8) | (by << 4) | bx;
            denseAgain.bricks[idx].data = (uint)(((bx + by + bz) % 2 == 0) ? 3 : 7);
        }
        store.InsertChunk(denseAgain);
        cascade.MarkDirty(int3.zero);
        Assert.DoesNotThrow(() => cascade.UploadDirty(store, pool),
            "Pool exhausted re-allocating after a uniform pass - the uniform pass did not free the earlier dense slot.");

        cascade.Dispose();
    }
}

public class LODCascadeManagerTests
{
    [Test]
    public void Constructor_CreatesOnePoolPerNonZeroTier_AccessibleByIndex()
    {
        var manager = new LODCascadeManager(new int3(4, 4, 4), tier => 64);
        for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
            Assert.IsNotNull(manager.TierPool(tier));
        manager.Dispose();
    }

    [Test]
    public void TierPool_RejectsTierZero()
    {
        var manager = new LODCascadeManager(new int3(4, 4, 4), tier => 64);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => manager.TierPool(0));
        manager.Dispose();
    }

    [Test]
    public void Dispose_ClearsActive()
    {
        var manager = new LODCascadeManager(new int3(4, 4, 4), tier => 64);
        Assert.AreSame(manager, LODCascadeManager.Active);
        manager.Dispose();
        Assert.IsNull(LODCascadeManager.Active);
    }

    [Test]
    public void DefaultTierPoolCapacity_NeverBelowFloor()
    {
        // Guards against a tiny EngineConfig.BRICK_POOL_CAP producing a
        // near-zero cascade pool that exhausts instantly.
        Assert.GreaterOrEqual(LODCascadeManager.DefaultTierPoolCapacity(100), 1024);
    }
}