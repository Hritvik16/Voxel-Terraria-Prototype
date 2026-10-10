// Assets/CoreEngine/Tests/DeferredEvictionTests.cs
//
// Task 26 (KNOWN_ISSUES "STRAY RESIDENT CHUNKS ALIAS INTO THE MIRROR"):
// EvictOutOfRange saves at most MAX_CHUNK_SAVES_PER_FRAME dirty chunks per
// call. It used to run only when the camera changed chunk, so a teleport that
// left more dirty chunks than the cap out of range, followed by a camera that
// holds still, kept the rest resident outside the window forever -- and their
// mirror uploads landed in the toroidal slot of an in-box chunk. Checked here
// on a small flat world (sizeClass 0, 5x5 chunks):
//   - the teleport frame saves exactly the cap and defers the rest;
//   - holding still, later frames retry, still at most the cap per frame;
//   - every dirty chunk is saved and none stays resident out of range.
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class DeferredEvictionTests
{
    private const int LOAD_RADIUS = 2;
    private const int DIRTY = 10;   // > 2x the cap: needs two retry frames

    [Test]
    public void SaveCappedEvictions_AreRetried_WhileTheCameraHoldsStill()
    {
        var meta = new WorldMetaData { seed = 42, sizeClass = 0 };
        var pool = new BrickDataPool(400000, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        var store = new ChunkStore(pool, alloc);
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        var clip = new TerrainClipmap(mirror, pool.Capacity);
        var cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
        string deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deferred_evict_test_deltas");
        if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        StreamManager sm = null;
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, deltas, LOAD_RADIUS);
            Assert.IsFalse(sm.VerticalStreaming, "sizeClass 0 is a flat world");

            Vector3 cam = new Vector3(64f, 20f, 64f);
            sm.PrimeWindow(cam);
            Settle(sm, cam);
            Assert.AreEqual(0, sm.LoadDeficit(), "start square not resident");
            Assert.GreaterOrEqual(store.ResidentCount, DIRTY, "too few resident chunks to dirty");

            // Mark DIRTY chunks edited (what SetVoxel does to deltaDirty).
            var dirty = new System.Collections.Generic.List<int3>();
            foreach (var c in store.ResidentChunks())
            {
                if (dirty.Count == DIRTY) break;
                c.deltaDirty = true;
                dirty.Add(c.coord);
            }

            // Teleport 2 km and HOLD STILL: only the first frame changes chunk.
            Vector3 far = cam + new Vector3(2000f, 0f, 1500f);
            int cap = EngineConfig.MAX_CHUNK_SAVES_PER_FRAME;
            int saved0 = sm.ChunksSavedTotal;
            sm.Update(far);
            Assert.AreEqual(cap, sm.ChunksSavedTotal - saved0, "the teleport frame must save exactly the cap");
            Assert.IsTrue(sm.EvictionDeferred, "dirty chunks beyond the cap must be deferred");

            int frames = 0;
            while (sm.EvictionDeferred && frames < 20)
            {
                int before = sm.ChunksSavedTotal;
                sm.Update(far);
                Assert.LessOrEqual(sm.ChunksSavedTotal - before, cap, "a retry frame exceeded the save cap");
                frames++;
            }
            Assert.IsFalse(sm.EvictionDeferred, $"still deferred after {frames} still frames");
            Assert.AreEqual(DIRTY, sm.ChunksSavedTotal - saved0, "every dirty chunk must be saved");
            foreach (var coord in dirty)
                Assert.IsFalse(store.IsResident(coord), $"{coord} stayed resident out of range");

            Settle(sm, far);
            Assert.AreEqual(0, sm.LoadDeficit(), "destination square not resident");
            Assert.AreEqual(sm.ExpectedResidentChunks, store.ResidentCount, "resident count != the load square");
            Debug.Log($"[Task26] {DIRTY} dirty chunks saved over 1 + {frames} frames at cap {cap}");
        }
        finally
        {
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
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
