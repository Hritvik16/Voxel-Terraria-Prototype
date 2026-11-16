// Assets/CoreEngine/Tests/GpuViewEvictTests.cs
//
// Task 216 (owner Q-AI (a)): when a chunk in view (or in the camera's 5x5) is refused by a
// full GPU body pool, mirror-box chunks OUT OF VIEW and FARTHER give up their bodies first,
// farthest first, and only if they cover the whole shortfall; the victims join the refused
// set (the shell hand-over draws them, 207's retry brings them back), and the retry ranks
// in-view chunks first. Real TerrainClipmap (tiny GPU pool, CPU shadow), like GpuRetryFitTests.
//
// Layout: box 4 x 2 x 4 chunks at the origin, camera chunk (-2, 0, 0), so (x, 0, z) is
// max(x + 2, |z|) chunks away (Chebyshev XZ). Fillers (1 body each) hold rows z = 2, 3:
//   (0,0,2) d2 (5x5: never a victim)  (1,0,2) d3  (2,0,2) d4  (3,0,2) d5
//   (0,0,3) d3                        (1,0,3) d3  (2,0,3) d4  (3,0,3) d5
// Box slot = x + 4 * (y + 2 * z): ties at one distance go to the higher slot (z = 3) first.
using System;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class GpuViewEvictTests
{
    private static readonly int3 Box = new int3(4, 2, 4);
    private const int Cap = 8;
    private static readonly int3 Cam = new int3(-2, 0, 0);
    private BrickDataPool _pool;
    private ChunkStore _store;
    private TerrainClipmap _clip;
    private bool _ring, _viewEvict;
    private int _version;

    [SetUp]
    public void SetUp()
    {
        _ring = UploadRing.Enabled;
        _viewEvict = TerrainClipmap.ViewEvict;
        UploadRing.Enabled = false;
        TerrainClipmap.RetryOnlyWhatFits = true;
        TerrainClipmap.ViewEvict = true;
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
        TerrainClipmap.ViewEvict = _viewEvict;
    }

    private static int3 Filler(int i) => new int3(i & 3, 0, 2 + (i >> 2));
    private static int3 Row(int x) => new int3(x, 0, 0);

    private void View(Func<int3, bool> inView) => _clip.SetView(inView, ++_version);

    private void InsertDense(int3 c, int n)
    {
        _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air });
        int3 v0 = c * EngineConfig.CHUNK_EDGE_VOXELS;
        for (int i = 0; i < n; i++)
            _store.SetVoxel(v0 + new int3(i & 15, 0, 3) * 8 + new int3(1, 2, 3), Materials.Stone);
        _clip.MarkDirty(c);
    }

    private void Flush() => _clip.UploadDirty(_store, _pool, int.MaxValue, Cam, int.MaxValue);
    private void Pass() => _clip.UploadDirty(_store, _pool, 0, Cam, 0);   // nothing exempt, everything deferred

    private void Fill()
    {
        for (int i = 0; i < Cap; i++) InsertDense(Filler(i), 1);
        Flush();
        Assert.AreEqual(Cap, _clip.GpuPoolInUse, "precondition: pool full");
    }

    private bool Held(int3 c) => _clip.CpuMirrorHandle(c, new int3(0, 0, 3)) != 0u && !_clip.IsGpuRefused(c);

    private void AssertValid(int refusedNow)
    {
        var r = ClipmapValidator.ValidateRegion(_clip, _pool, _store);
        Assert.IsTrue(r.pass, r.Describe());
        Assert.AreEqual(refusedNow, r.chunksGpuRefused);
        Assert.AreEqual(refusedNow, _clip.GpuPoolRefusedNow);
    }

    [Test]
    public void InViewRequester_EvictsOutOfViewFarthestFirst_AndLands()
    {
        View(c => c.z == 0);   // only row z = 0 in view: every filler is a candidate
        Fill();
        InsertDense(Row(0), 3);   // d2, in view, needs 3 bodies
        Flush();

        Assert.IsTrue(Held(Row(0)), "the in-view chunk landed");
        Assert.AreEqual(0, _clip.GpuPoolRefusedTotal, "no refusal counted");
        Assert.AreEqual(3, _clip.GpuViewEvictedTotal);
        Assert.AreEqual(3, _clip.GpuViewEvictedBodiesTotal);
        Assert.AreEqual(1, _clip.GpuViewEvictSavedTotal);
        // farthest first; at d5 the higher slot (z = 3) first; then d4 z = 3
        foreach (int3 v in new[] { new int3(3, 0, 3), new int3(3, 0, 2), new int3(2, 0, 3) })
        {
            Assert.IsTrue(_clip.IsGpuRefused(v), $"{v} evicted (refused set: the hand-over draws it)");
            Assert.AreEqual(0u, _clip.CpuMirrorHandle(v, new int3(0, 0, 3)), $"{v} written as air");
        }
        foreach (int3 k in new[] { new int3(0, 0, 2), new int3(1, 0, 2), new int3(2, 0, 2), new int3(0, 0, 3), new int3(1, 0, 3) })
            Assert.IsTrue(Held(k), $"{k} kept (nearer)");
        Assert.AreEqual(Cap, _clip.GpuPoolInUse);
        AssertValid(3);
    }

    [Test]
    public void VictimsMustBeFartherThanTheRequester_AndCoverTheWholeShortfall()
    {
        View(c => c.z == 0);
        Fill();
        InsertDense(Row(2), 3);   // d4: only the two d5 fillers are farther -> 2 < 3, evict nothing
        Flush();
        Assert.IsTrue(_clip.IsGpuRefused(Row(2)), "not enough farther victims: refused as today");
        Assert.AreEqual(0, _clip.GpuViewEvictedTotal, "nothing cleared for a requester refused anyway");
        Assert.AreEqual(1, _clip.GpuPoolRefusedTotal);

        InsertDense(Row(1), 2);   // d3: the two d5 fillers (d4 ones too) cover 2
        Flush();
        Assert.IsTrue(Held(Row(1)));
        Assert.AreEqual(2, _clip.GpuViewEvictedTotal);
        Assert.IsTrue(_clip.IsGpuRefused(new int3(3, 0, 3)));
        Assert.IsTrue(_clip.IsGpuRefused(new int3(3, 0, 2)));
        Assert.IsTrue(Held(new int3(2, 0, 3)), "d4 kept: the two farthest were enough");
    }

    [Test]
    public void OutOfViewFarRequester_DoesNotEvict()
    {
        View(c => c.z == 0 && c.x == 0);
        Fill();
        InsertDense(Row(1), 1);   // d3, out of view, outside the 5x5
        Flush();
        Assert.IsTrue(_clip.IsGpuRefused(Row(1)));
        Assert.AreEqual(0, _clip.GpuViewEvictedTotal);
    }

    [Test]
    public void OutOfViewRequesterInThe5x5_Evicts()
    {
        View(c => false);   // nothing in view (looking at the sky): the 5x5 still counts
        Fill();
        InsertDense(Row(0), 1);   // d2
        Flush();
        Assert.IsTrue(Held(Row(0)));
        Assert.AreEqual(1, _clip.GpuViewEvictedTotal);
        Assert.IsTrue(_clip.IsGpuRefused(new int3(3, 0, 3)), "the farthest went");
    }

    [Test]
    public void InViewChunksAreNeverVictims()
    {
        View(c => true);
        Fill();
        InsertDense(Row(0), 1);
        Flush();
        Assert.IsTrue(_clip.IsGpuRefused(Row(0)), "everything in view: nothing to drop");
        Assert.AreEqual(0, _clip.GpuViewEvictedTotal);
        for (int i = 0; i < Cap; i++) Assert.IsTrue(Held(Filler(i)));
    }

    [TestCase(false, true)]   // -gpuviewevict 0
    [TestCase(true, false)]   // no view (SetView(null))
    public void Off_OrNoView_IsToday(bool flag, bool view)
    {
        TerrainClipmap.ViewEvict = flag;
        if (view) View(c => c.z == 0); else _clip.SetView(null, 0);
        Fill();
        InsertDense(Row(0), 3);
        Flush();
        Assert.IsTrue(_clip.IsGpuRefused(Row(0)));
        Assert.AreEqual(1, _clip.GpuPoolRefusedTotal);
        Assert.AreEqual(0, _clip.GpuViewEvictedTotal);
        for (int i = 0; i < Cap; i++) Assert.IsTrue(Held(Filler(i)));
        AssertValid(1);
    }

    [Test]
    public void Retry_WhenTheViewTurns_AnInViewRefusedChunkEvictsAndLands()
    {
        View(c => true);   // everything in view: the chunk is refused, no victims
        Fill();
        InsertDense(Row(0), 2);
        Flush();
        Assert.IsTrue(_clip.IsGpuRefused(Row(0)));

        View(c => c.z == 0);   // the camera turned: the fillers left the view
        Pass();                // the retry alone (budget 0): victims cleared, the chunk re-queued
        Assert.AreEqual(2, _clip.GpuViewEvictedTotal);
        Assert.IsFalse(_clip.IsGpuRefused(Row(0)));
        Assert.IsTrue(_clip.IsDirty(Row(0)));
        Assert.AreEqual(1, _clip.GpuPoolRetryPending, "re-queued with its bodies promised");
        Assert.AreEqual(2, _clip.GpuPoolCapacity - _clip.GpuPoolInUse, "the 2 freed bodies wait for it");
        Flush();
        Assert.IsTrue(Held(Row(0)));
        Assert.AreEqual(1, _clip.GpuPoolRefusedTotal, "landed: no second refusal");
        AssertValid(2);
    }

    [Test]
    public void Retry_RanksInViewChunksFirst_AVictimWaits()
    {
        _clip.SetView(null, 0);
        Fill();
        int3 r = Row(2);                 // d4, will be in view
        int3 v = new int3(1, 0, 1);      // d3, nearer, out of view
        InsertDense(r, 1);
        InsertDense(v, 1);
        Flush();
        Assert.IsTrue(_clip.IsGpuRefused(r) && _clip.IsGpuRefused(v));

        View(c => c.z != 1);             // the fillers are in view: nothing to evict
        _store.EvictChunk(Filler(0)); _clip.MarkDirty(Filler(0));
        Flush();                         // the clear frees 1 body (the retry ran before it)
        Assert.AreEqual(Cap - 1, _clip.GpuPoolInUse);
        Pass();                          // 1 free: the in-view chunk gets it although v is nearer
        Assert.IsTrue(_clip.IsDirty(r));
        Assert.IsTrue(_clip.IsGpuRefused(v), "out of view: waits");
        Assert.AreEqual(0, _clip.GpuViewEvictedTotal);
    }

    [Test]
    public void Retry_OffKeeps207sNearestFirstOrder()
    {
        TerrainClipmap.ViewEvict = false;
        _clip.SetView(null, 0);
        Fill();
        int3 r = Row(2), v = new int3(1, 0, 1);
        InsertDense(r, 1);
        InsertDense(v, 1);
        Flush();
        View(c => c.z != 1);
        _store.EvictChunk(Filler(0)); _clip.MarkDirty(Filler(0));
        Flush();
        Pass();
        Assert.IsTrue(_clip.IsDirty(v), "207's order: nearest first");
        Assert.IsTrue(_clip.IsGpuRefused(r));
    }

    [Test]
    public void Victims_ComeBackThroughTheRetry_WhenBodiesFree()
    {
        View(c => c.z == 0);
        Fill();
        InsertDense(Row(0), 1);
        Flush();
        int3 victim = new int3(3, 0, 3);
        Assert.IsTrue(_clip.IsGpuRefused(victim));

        _store.EvictChunk(Row(0)); _clip.MarkDirty(Row(0));
        Flush();   // the clear frees the body (the retry ran before it)
        Flush();   // the retry: the victim fits again and lands
        Assert.IsTrue(Held(victim));
        Assert.AreEqual(0, _clip.GpuPoolRefusedNow);
        AssertValid(0);
    }
}
