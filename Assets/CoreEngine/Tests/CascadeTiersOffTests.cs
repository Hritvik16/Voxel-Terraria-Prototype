// Assets/CoreEngine/Tests/CascadeTiersOffTests.cs
//
// Task 93d (amendment 8.15 row D1, owner Q49): with the shells on, cascade
// tiers 1-2 never draw, so Phase4Bootstrapper does not create them (task 55's
// ledger: 193 MB). The streamer then runs with no LODCascadeManager at all:
//   - the start-up rule: only tg2 + `-shells 1` skips them (`-keeptiers 1` = keep);
//   - a StreamManager built without cascades streams, edits, evicts and
//     re-admits (the edit survives) with no worker downsample and no
//     DownsampleScratch held, and the tier-0 mirror drains; built WITH cascades it holds one per scratch
//     context (so the counter measures something).
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class CascadeTiersOffTests
{
    [Test]
    public void StartUpRule_SkipsTiersOnlyWhenShellsDraw()
    {
        Assert.IsFalse(LODCascadeManager.TiersWanted(true, 1, 0), "tg2 + -shells 1: tiers 1-2 must not be allocated");
        Assert.IsTrue(LODCascadeManager.TiersWanted(true, 1, 1), "-keeptiers 1 allocates them anyway");
        Assert.IsTrue(LODCascadeManager.TiersWanted(true, 0, 0), "shells off (rigs, capture): allocated as before");
        Assert.IsTrue(LODCascadeManager.TiersWanted(false, 1, 0), "-shells is tg2 only; other worlds keep their tiers");
        Assert.IsTrue(LODCascadeManager.TiersWanted(false, 0, 0));
    }

    [Test]
    public void StreamerWithoutCascades_StreamsEditsEvicts_WithNoDownsample()
    {
        var activeBefore = LODCascadeManager.Active;
        Run(withCascades: false, (sm, store, pool, clip) =>
        {
            Assert.IsFalse(sm.CascadeTiersOn);
            Assert.AreEqual(0, sm.ScratchDownsampleBuffers, "a scratch context holds a DownsampleScratch with no tiers");
            Assert.AreSame(activeBefore, LODCascadeManager.Active, "no LODCascadeManager may be created");
        });
    }

    [Test]
    public void StreamerWithCascades_HoldsOneDownsampleScratchPerContext()
    {
        Run(withCascades: true, (sm, store, pool, clip) =>
        {
            Assert.IsTrue(sm.CascadeTiersOn);
            Assert.Greater(sm.ScratchDownsampleBuffers, 0, "the shells-off path must keep its worker downsample buffers");
        });
    }

    private static void Run(bool withCascades, System.Action<StreamManager, ChunkStore, BrickDataPool, TerrainClipmap> check)
    {
        var meta = new WorldMetaData { seed = 42, sizeClass = 0 };
        var pool = new BrickDataPool(400000, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        var store = new ChunkStore(pool, alloc);
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        var clip = new TerrainClipmap(mirror, pool.Capacity);
        var cascades = withCascades ? new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier)) : null;
        string deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cascade_tiers_off_test_deltas");
        if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        StreamManager sm = null;
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, deltas, 2);
            Vector3 cam = new Vector3(64f, 20f, 64f);
            sm.PrimeWindow(cam);
            Settle(sm, cam);
            Assert.AreEqual(0, sm.LoadDeficit(), "start square not resident");

            // An edit (SetVoxel -> chunk.dirty -> the drain's mark path), saved on eviction.
            int3 edit = new int3(640, 100, 640);
            Assert.IsTrue(store.IsResident(CoordMath.VoxelToChunk(edit)), "edit chunk not resident");
            byte want = store.GetVoxel(edit) == 0 ? (byte)3 : (byte)0;
            store.SetVoxel(edit, want);
            Settle(sm, cam);

            // Teleport (evict + mark path) and back (re-admit from the delta).
            Vector3 far = cam + new Vector3(2000f, 0f, 1500f);
            Settle(sm, far);
            Assert.AreEqual(0, sm.LoadDeficit(), "destination square not resident");
            Settle(sm, cam);
            Assert.AreEqual(0, sm.LoadDeficit(), "return square not resident");
            Assert.AreEqual(want, store.GetVoxel(edit), "the edit did not survive eviction and reload");

            // The tier-0 mirror still drains (ClipmapValidator reads the GPU buffer back,
            // which is all zeros under -nographics, so it cannot run here).
            clip.UploadDirty(store, pool);
            Assert.AreEqual(0, clip.DirtyCount, "tier-0 mirror left chunks queued");

            check(sm, store, pool, clip);
        }
        finally
        {
            sm?.Dispose();
            cascades?.Dispose(); clip.Dispose(); pool.Dispose();
            if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        }
    }

    private static void Settle(StreamManager sm, Vector3 cam)
    {
        var clk = System.Diagnostics.Stopwatch.StartNew();
        do { sm.Update(cam); sm.WaitForIdle(); }
        while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0) && clk.Elapsed.TotalSeconds < 120);
        for (int i = 0; i < 2; i++) { sm.Update(cam); sm.WaitForIdle(); }
    }
}
