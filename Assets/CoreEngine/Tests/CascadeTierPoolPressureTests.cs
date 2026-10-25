// Assets/CoreEngine/Tests/CascadeTierPoolPressureTests.cs
//
// Task 140: "BrickDataPool exhausted. LRU valve failed or cap is too low." thrown
// from CascadeTierPool.WriteChunkFromDownsampled <- SubmitPrecomputed <-
// StreamManager.DrainCompletions, ~2,800 times per stream walk. An evicted chunk's
// cascade slots come back only in UploadDirty's pass-1 clear at the END of the
// frame; one throw in admission skips that frame's UploadDirty, so the evicted
// chunks keep holding slots and every later admission throws too. The fix: a
// SubmitPrecomputed that is handed the store clears pending evicted chunks first
// when the pool cannot take a whole chunk.
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class CascadeTierPoolPressureTests
{
    // Tier 1: 64^3 downsampled voxels, 8^3 coarse bricks of 8^3. Material flips with
    // x parity, so every one of the chunk's 512 coarse bricks is non-uniform.
    private static byte[] AllDenseTier1()
    {
        const int e = 64;
        var d = new byte[e * e * e];
        for (int z = 0; z < e; z++)
        for (int y = 0; y < e; y++)
        for (int x = 0; x < e; x++)
            d[x + e * (y + e * z)] = (byte)((x & 1) == 0 ? 2 : 5);
        return d;
    }

    // A pool for 1.5 chunks: one chunk fits, two do not -- unless the evicted one
    // is cleared first.
    private const int Cap = 768;

    private static (ChunkStore store, CascadeTierPool tier) Setup(int3 a, byte[] dense)
    {
        var store = new ChunkStore(new BrickDataPool(64), new ChunkHandleAllocator());
        store.InsertChunk(new Chunk { coord = a, isUniform = true, uniformMaterial = 2 });
        var tier = new CascadeTierPool(1, new int3(4, 4, 4), Cap);
        tier.SubmitPrecomputed(a, dense, store);
        Assert.AreEqual(512, tier.BrickPool.InUse, "chunk A should hold 512 dense coarse bricks");
        // StreamManager.EvictChunk's order: the store drops it, THEN the mirrors are marked dirty.
        store.EvictChunk(a);
        tier.MarkDirty(a);
        return (store, tier);
    }

    [Test]
    public void Admission_AfterEviction_WithoutTheStore_Exhausts()
    {
        // The pre-fix call shape reproduces the exhaustion: A's slots are still held.
        byte[] dense = AllDenseTier1();
        int3 a = new int3(0, 0, 0), b = new int3(1, 0, 0);
        var (store, tier) = Setup(a, dense);
        var ex = Assert.Throws<System.InvalidOperationException>(() => tier.SubmitPrecomputed(b, dense));
        StringAssert.Contains("BrickDataPool exhausted", ex.Message);
        tier.Dispose();
    }

    [Test]
    public void Admission_AfterEviction_WithTheStore_ClearsEvictedFirst()
    {
        byte[] dense = AllDenseTier1();
        int3 a = new int3(0, 0, 0), b = new int3(1, 0, 0);
        var (store, tier) = Setup(a, dense);

        Assert.DoesNotThrow(() => tier.SubmitPrecomputed(b, dense, store));
        Assert.AreEqual(512, tier.BrickPool.InUse, "only B's bricks should be held");
        Assert.AreEqual(1, tier.PressureReliefsTotal);
        Assert.AreEqual(512, tier.PressureReliefSlotsFreed);
        Assert.IsFalse(tier.IsDirty(a), "A's clear was done, not left for UploadDirty");

        // A reads back as air, B as its dense content.
        byte[] snapA = tier.CpuChunkSnapshot(a);
        for (int i = 0; i < snapA.Length; i++)
            if (snapA[i] != 0) { Assert.Fail($"evicted chunk A still has material {snapA[i]} at byte {i}"); break; }
        byte[] snapB = tier.CpuChunkSnapshot(b);
        Assert.AreEqual(2, snapB[0]);
        Assert.AreEqual(5, snapB[1]);

        // Steady churn: evict the newest, admit the next, many times over -- never exhausts.
        int3 prev = b;
        for (int i = 2; i < 40; i++)
        {
            int3 next = new int3(i & 3, (i >> 2) & 3, 0);
            if (next.Equals(prev)) next.z = 1;
            store.EvictChunk(prev);   // a no-op for chunks never inserted; the mark is what matters
            tier.MarkDirty(prev);
            int3 n = next;
            Assert.DoesNotThrow(() => tier.SubmitPrecomputed(n, dense, store), $"churn step {i}");
            prev = next;
        }
        Assert.AreEqual(512, tier.BrickPool.InUse);
        tier.Dispose();
    }

    [Test]
    public void Admission_WithRoom_DoesNotClearEarly()
    {
        // No pressure -> no relief: the evicted chunk is left to UploadDirty as before.
        byte[] dense = AllDenseTier1();
        int3 a = new int3(0, 0, 0), b = new int3(1, 0, 0);
        var store = new ChunkStore(new BrickDataPool(64), new ChunkHandleAllocator());
        var tier = new CascadeTierPool(1, new int3(4, 4, 4), 2048);
        tier.SubmitPrecomputed(a, dense, store);
        tier.MarkDirty(a);
        tier.SubmitPrecomputed(b, dense, store);
        Assert.AreEqual(0, tier.PressureReliefsTotal);
        Assert.IsTrue(tier.IsDirty(a));
        Assert.AreEqual(1024, tier.BrickPool.InUse);
        tier.UploadDirty(store, new BrickDataPool(64));
        Assert.AreEqual(512, tier.BrickPool.InUse, "UploadDirty's pass 1 still clears it");
        tier.Dispose();
    }
}
