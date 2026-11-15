// Assets/CoreEngine/Tests/GpuRetryFitTests.cs
//
// Task 207 (owner Q-AB (a), fix 1 of 2): RetryGpuRefused re-queues only the refused chunks
// whose GPU bodies fit in what is free (minus what earlier re-queued, still-queued chunks were
// promised), nearest to the camera first; the rest stay refused (197's hand-over keeps drawing
// them). Before, ONE freed slot cleared the whole refused set (task 197's "retry gap" churn).
// Real TerrainClipmap (tiny GPU pool, CPU shadow) and real budgeted UploadDirty passes, like
// Tier0LackHandOverTests. A zero byte budget with nothing exempt defers every upload, so a
// re-queued chunk stays in the dirty queue and the retry's own choice can be read.
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class GpuRetryFitTests
{
    private static readonly int3 Box = new int3(4, 2, 4);
    private const int Cap = 8;
    private static readonly int3 Cam = new int3(-2, 0, 0);   // west of the row: (x,0,0) is x+2 chunks away
    private BrickDataPool _pool;
    private ChunkStore _store;
    private TerrainClipmap _clip;
    private bool _ring;

    [SetUp]
    public void SetUp()
    {
        _ring = UploadRing.Enabled;
        UploadRing.Enabled = false;   // the plain byte budget (the ring admits one chunk per pass)
        TerrainClipmap.RetryOnlyWhatFits = true;
        _pool = new BrickDataPool(16384);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(256));
        _clip = new TerrainClipmap(Box, _pool.Capacity, Cap) { RecentreDirtiesStaleSlots = true };
        _clip.EnableCpuShadow();
        _clip.SetWindowOrigin(int3.zero);
    }

    [TearDown]
    public void TearDown()
    {
        _clip?.Dispose();
        _pool?.Dispose();
        UploadRing.Enabled = _ring;
        TerrainClipmap.RetryOnlyWhatFits = true;
    }

    private static int3 Filler(int i) => new int3(i & 3, 0, 2 + (i >> 2));   // rows z = 2, 3
    private static int3 Row(int x) => new int3(x, 0, 0);                    // the refused row, z = 0

    private void InsertDense(int3 c, int n)
    {
        _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air });
        int3 v0 = c * EngineConfig.CHUNK_EDGE_VOXELS;
        for (int i = 0; i < n; i++)
            _store.SetVoxel(v0 + new int3(i & 15, 0, 3) * 8 + new int3(1, 2, 3), Materials.Stone);
        _clip.MarkDirty(c);
    }

    private void Evict(int3 c) { _store.EvictChunk(c); _clip.MarkDirty(c); }

    private void Pass() => _clip.UploadDirty(_store, _pool, 0, Cam, 0);   // nothing exempt, everything deferred

    /// 8 fillers (1 body each) fill the pool; the row's `needs` chunks are then all refused.
    private void FillAndRefuse(params int[] needs)
    {
        for (int i = 0; i < Cap; i++) InsertDense(Filler(i), 1);
        _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(Cap, _clip.GpuPoolInUse, "precondition: pool full");
        for (int x = 0; x < needs.Length; x++) InsertDense(Row(x), needs[x]);
        _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(needs.Length, _clip.GpuPoolRefusedNow, "precondition: the whole row refused");
    }

    /// Frees k bodies (k fillers evicted, their clears land; the retry at that pass's start saw 0 free).
    private void FreeFillers(int k)
    {
        for (int i = 0; i < k; i++) Evict(Filler(i));
        _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(Cap - k, _clip.GpuPoolInUse);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(6)]
    public void FullPool_KFreed_ReQueuesExactlyMinKN_NearestFirst(int k)
    {
        const int N = 4;
        FillAndRefuse(1, 1, 1, 1);
        FreeFillers(k);
        Assert.AreEqual(N, _clip.GpuPoolRefusedNow, "nothing retried before a pass sees the freed slots");

        Pass();
        int expect = math.min(k, N);
        for (int x = 0; x < N; x++)
        {
            bool queued = x < expect;   // nearest = lowest x
            Assert.AreEqual(queued, _clip.IsDirty(Row(x)), $"row {x}: re-queued only if its slot is free (k {k})");
            Assert.AreEqual(!queued, _clip.IsGpuRefused(Row(x)), $"row {x}: what did not fit stays refused");
        }
        Assert.AreEqual(N - expect, _clip.GpuPoolRefusedNow);
        Assert.AreEqual(expect, _clip.GpuPoolRetryPending);
        Assert.AreEqual(expect, _clip.GpuPoolRetriesTotal);

        Pass();   // nothing freed: nothing else leaves the refused set
        Assert.AreEqual(N - expect, _clip.GpuPoolRefusedNow);
        Assert.AreEqual(expect, _clip.GpuPoolRetriesTotal);

        _clip.FlushAllDirty(_store, _pool);   // the re-queued uploads land, none is refused again
        Assert.AreEqual(N, _clip.GpuPoolRefusedTotal, "no re-refusal: every retried chunk had its slot");
        Assert.AreEqual(0, _clip.GpuPoolReRefusedTotal);
        Assert.AreEqual(Cap - k + expect, _clip.GpuPoolInUse);
        Assert.AreEqual(N - expect, _clip.GpuPoolRefusedNow);
        Assert.AreEqual(0, _clip.GpuPoolRetryPending);
        for (int x = 0; x < expect; x++)
            Assert.AreNotEqual(0u, _clip.CpuMirrorHandle(Row(x), new int3(0, 0, 3)), $"row {x} landed");
        var r = ClipmapValidator.ValidateRegion(_clip, _pool, _store);
        Assert.IsTrue(r.pass, r.Describe());
        Assert.AreEqual(N - expect, r.chunksGpuRefused);
    }

    [Test]
    public void QueuedRetries_KeepTheirPromise_ANewSlotTakesOneMore()
    {
        FillAndRefuse(1, 1, 1, 1);
        FreeFillers(2);
        Pass();
        Assert.AreEqual(2, _clip.GpuPoolRetryPending, "rows 0, 1 queued, holding 2 promised bodies");

        // One more filler goes; its clear is exempt (camera on its column), the rows stay deferred.
        int3 f = Filler(2);
        Evict(f);
        _clip.UploadDirty(_store, _pool, 0, f, 0);
        Assert.AreEqual(Cap - 3, _clip.GpuPoolInUse, "the clear freed a 3rd body");
        Assert.AreEqual(2, _clip.GpuPoolRefusedNow, "the retry ran before the clear: still 2 refused");

        _clip.UploadDirty(_store, _pool, 0, f, 0);   // 3 free - 2 promised = 1: exactly one more
        Assert.AreEqual(1, _clip.GpuPoolRefusedNow);
        Assert.AreEqual(3, _clip.GpuPoolRetryPending);
        // From camera (2,0,2) rows 2 and 3 are both 2 chunks away (Chebyshev XZ): the tie goes to the lower x.
        Assert.IsTrue(_clip.IsDirty(Row(2)));
        Assert.IsTrue(_clip.IsGpuRefused(Row(3)));

        _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(4, _clip.GpuPoolRefusedTotal, "all three retried rows had a slot");
        Assert.AreEqual(1, _clip.GpuPoolRefusedNow);
    }

    [Test]
    public void ANearChunkThatDoesNotFit_StaysRefused_AFartherOneThatFitsGoes()
    {
        FillAndRefuse(3, 1);   // row 0 needs 3 bodies, row 1 needs 1
        FreeFillers(2);
        Pass();
        Assert.IsTrue(_clip.IsGpuRefused(Row(0)), "3 > 2 free: stays refused (the shell draws it)");
        Assert.IsFalse(_clip.IsDirty(Row(0)));
        Assert.IsTrue(_clip.IsDirty(Row(1)));
        Assert.IsFalse(_clip.IsGpuRefused(Row(1)));

        _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(2, _clip.GpuPoolRefusedTotal, "row 1 landed first try");
        Evict(Filler(2)); Evict(Filler(3));
        _clip.FlushAllDirty(_store, _pool);     // their clears: 3 free (the retry ran before them)
        Assert.IsTrue(_clip.IsGpuRefused(Row(0)));
        _clip.FlushAllDirty(_store, _pool);     // the retry sees 3 free: row 0 fits now and lands
        Assert.AreEqual(0, _clip.GpuPoolRefusedNow);
        Assert.AreEqual(2, _clip.GpuPoolRefusedTotal);
        Assert.AreNotEqual(0u, _clip.CpuMirrorHandle(Row(0), new int3(2, 0, 3)));
    }

    [Test]
    public void EvictedRefusedChunk_IsForgotten_NoSlotPromised()
    {
        FillAndRefuse(1, 1);
        Evict(Row(0));
        FreeFillers(1);
        Pass();
        Assert.IsFalse(_clip.IsGpuRefused(Row(0)));
        Assert.IsTrue(_clip.IsDirty(Row(1)), "the one free body goes to the resident row");
        Assert.AreEqual(1, _clip.GpuPoolRetryPending);
        Assert.AreEqual(0, _clip.GpuPoolRefusedNow);
    }

    [Test]
    public void OldRetry_RetryFit0_OneFreeSlotReQueuesTheWholeSet()
    {
        FillAndRefuse(1, 1, 1, 1);
        FreeFillers(1);
        TerrainClipmap.RetryOnlyWhatFits = false;
        Pass();
        Assert.AreEqual(0, _clip.GpuPoolRefusedNow, "the pre-207 retry: the whole set out on one free slot");
        for (int x = 0; x < 4; x++) Assert.IsTrue(_clip.IsDirty(Row(x)));
        _clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(4 + 3, _clip.GpuPoolRefusedTotal, "three of them refused again: the churn");
        Assert.AreEqual(3, _clip.GpuPoolReRefusedTotal);
    }
}
