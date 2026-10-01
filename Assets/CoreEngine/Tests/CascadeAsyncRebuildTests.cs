// Assets/CoreEngine/Tests/CascadeAsyncRebuildTests.cs
//
// LODCascadeManager's ASYNC REBUILD (2026-09-26) must write exactly what the
// synchronous path writes. Two managers over the SAME store and pool: one
// synchronous, one async. A chunk with varied content is edited (MarkDirty),
// both upload until clean, and every coarse brick of every tier is compared
// byte for byte. Then the async one is edited AGAIN while a rebuild is in
// flight: the chunk must stay dirty and converge to the newer content.
using System.Threading;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class CascadeAsyncRebuildTests
{
    private static void Fill(ChunkStore store, int3 c, int seed)
    {
        // Strata-like bands plus scattered pockets: many dense bricks.
        for (int z = 0; z < 128; z += 1)
        for (int y = 0; y < 128; y += 1)
        for (int x = 0; x < 128; x += 1)
        {
            int h = (x * 7 + z * 3 + seed * 11) % 23;
            byte m = y < 60 + (h >> 1) ? (byte)(Materials.TG2_BASE + 30 + ((y + h) / 5) % 5) : Materials.Air;
            if (((x * 31 + y * 17 + z * 13 + seed) & 63) == 0) m = Materials.Stone;
            if (m != Materials.Air) store.SetVoxel(new int3(c.x * 128 + x, c.y * 128 + y, c.z * 128 + z), m);
        }
    }

    private static void Pump(LODCascadeManager m, ChunkStore store, BrickDataPool pool, int3 c)
    {
        for (int i = 0; i < 2000; i++)
        {
            m.UploadDirty(store, pool);
            if (!m.TierPool(1).IsDirty(c) && !m.TierPool(2).IsDirty(c) && !m.IsRebuildInFlight(c)) return;
            Thread.Sleep(1);
        }
        Assert.Fail("cascade rebuild did not converge");
    }

    [Test]
    public void AsyncRebuild_WritesExactlyWhatTheSyncPathWrites_AndSurvivesAnEditInFlight()
    {
        var pool = new BrickDataPool(20000, rangeAware: true);
        var alloc = new ChunkHandleAllocator(8);
        var store = new ChunkStore(pool, alloc, new int3(4, 4, 4));
        var c = new int3(1, 0, 1);
        store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air });
        Fill(store, c, 1);
        bool saved = LODCascadeManager.AsyncRebuildEnabled;
        LODCascadeManager sync = null, async = null;
        try
        {
            LODCascadeManager.AsyncRebuildEnabled = false;
            sync = new LODCascadeManager(new int3(4, 4, 4), t => 20000);
            LODCascadeManager.AsyncRebuildEnabled = true;
            async = new LODCascadeManager(new int3(4, 4, 4), t => 20000);

            sync.MarkDirty(c); async.MarkDirty(c);
            Pump(sync, store, pool, c); Pump(async, store, pool, c);
            Assert.Greater(async.AsyncRebuildsTotal, 0, "the async path never ran");
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                CollectionAssert.AreEqual(sync.TierPool(tier).CpuChunkSnapshot(c), async.TierPool(tier).CpuChunkSnapshot(c),
                                          $"tier {tier}: async differs from sync");

            // Edit while a rebuild is in flight: mark, submit, edit again before it lands.
            async.MarkDirty(c);
            async.UploadDirty(store, pool);                 // snapshot of content 1 in flight
            Fill(store, c, 2);                              // content 2
            async.MarkDirty(c);
            sync.MarkDirty(c);
            Pump(sync, store, pool, c); Pump(async, store, pool, c);
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                CollectionAssert.AreEqual(sync.TierPool(tier).CpuChunkSnapshot(c), async.TierPool(tier).CpuChunkSnapshot(c),
                                          $"tier {tier}: after an edit in flight, async did not converge to the newer content");
        }
        finally
        {
            LODCascadeManager.AsyncRebuildEnabled = saved;
            sync?.Dispose(); async?.Dispose(); pool.Dispose();
        }
    }
}
