// Assets/CoreEngine/Tests/EvictionSlicingTests.cs
//
// Task 123: eviction slicing. A chunk crossing used to evict the whole
// out-of-range slab in the crossing frame (task 115: ~256 chunks, 58-64 ms;
// worst 1,157). Out-of-range chunks still inside the store window are now
// queued and evicted at most StreamManager.EvictSlicePerFrame per frame.
// Checked on the small flat world of DeferredEvictionTests (sizeClass 0,
// load radius 2 -> a 5x5 square, evict radius 4). A 7-chunk jump puts the
// whole start square out of range but still inside the 32-wide window:
//   - the crossing frame evicts exactly the slice, the rest is queued;
//   - holding still, each frame evicts at most the slice until none is left;
//   - every resident chunk stays inside the window all along (no aliasing:
//     an in-window coord owns its ring and cascade slot, tasks 26/32);
//   - a queued chunk that comes back into range is kept, not evicted;
//   - under pool pressure the queue is evicted first, no in-range victim;
//   - slice 0 = the old one-frame slab.
// Never call WaitForIdle between the jump and the checks: it flushes the queue.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class EvictionSlicingTests
{
    private const int LOAD_RADIUS = 2;
    private static readonly Vector3 START = new Vector3(64f, 20f, 64f);
    private static readonly Vector3 JUMP = START + new Vector3(90f, 0f, 0f);   // 7 chunks of 12.8 m

    private sealed class Rig : System.IDisposable
    {
        public BrickDataPool pool; public ChunkHandleAllocator alloc; public ChunkStore store;
        public TerrainClipmap clip; public LODCascadeManager cascades; public StreamManager sm;
        string _deltas; int _sliceWas;

        public Rig(int slice, string name)
        {
            _sliceWas = StreamManager.EvictSlicePerFrame;
            StreamManager.EvictSlicePerFrame = slice;
            var meta = new WorldMetaData { seed = 42, sizeClass = 0 };
            pool = new BrickDataPool(400000, rangeAware: true);
            alloc = new ChunkHandleAllocator(1024);
            store = new ChunkStore(pool, alloc);
            var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
            clip = new TerrainClipmap(mirror, pool.Capacity);
            cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
            _deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "evict_slice_test_" + name);
            if (System.IO.Directory.Exists(_deltas)) System.IO.Directory.Delete(_deltas, true);
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, _deltas, LOAD_RADIUS);
            Assert.IsFalse(sm.VerticalStreaming, "sizeClass 0 is a flat world");
            sm.PrimeWindow(START);
            Settle(sm, START);
            Assert.AreEqual(0, sm.LoadDeficit(), "start square not resident");
            Assert.AreEqual(0, sm.EvictQueueCount, "queue not empty after settling");
        }

        public void Dispose()
        {
            StreamManager.EvictSlicePerFrame = _sliceWas;
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
            if (System.IO.Directory.Exists(_deltas)) System.IO.Directory.Delete(_deltas, true);
        }
    }

    private static List<int3> Resident(ChunkStore store)
    {
        var list = new List<int3>();
        foreach (var c in store.ResidentChunks()) list.Add(c.coord);
        return list;
    }

    private static void AssertAllInWindow(ChunkStore store, string when)
    {
        foreach (var c in store.ResidentChunks())
            Assert.IsTrue(store.IsInWindow(c.coord), $"{when}: {c.coord} resident outside the window");
    }

    [Test]
    public void Crossing_EvictsAtMostTheSlicePerFrame_UntilTheSlabIsGone()
    {
        const int SLICE = 4;
        using var r = new Rig(SLICE, "slice");
        var start = Resident(r.store);
        Assert.Greater(start.Count, SLICE * 2, "start square too small to slice");

        long e0 = r.sm.ChunksEvictedTotal;
        r.sm.Update(JUMP);
        Assert.AreEqual(SLICE, r.sm.ChunksEvictedTotal - e0, "the crossing frame must evict exactly the slice");
        Assert.AreEqual(start.Count - SLICE, r.sm.EvictQueueCount, "the rest of the slab must be queued");
        Assert.IsTrue(r.sm.EvictionDeferred, "a queued slab reads as deferred");
        AssertAllInWindow(r.store, "crossing frame");

        int frames = 0;
        while (r.sm.EvictQueueCount > 0 && frames < 64)
        {
            long before = r.sm.ChunksEvictedTotal;
            r.sm.Update(JUMP);
            Assert.LessOrEqual(r.sm.ChunksEvictedTotal - before, SLICE, "a drain frame exceeded the slice");
            AssertAllInWindow(r.store, $"drain frame {frames}");
            frames++;
        }
        Assert.AreEqual(0, r.sm.EvictQueueCount, $"queue not empty after {frames} frames");
        foreach (var coord in start)
            Assert.IsFalse(r.store.IsResident(coord), $"{coord} stayed resident out of range");
        int expectFrames = (start.Count - SLICE + SLICE - 1) / SLICE;
        Assert.AreEqual(expectFrames, frames, "the slab must drain one full slice per frame");

        Settle(r.sm, JUMP);
        Assert.AreEqual(0, r.sm.LoadDeficit(), "destination square not resident");
        Assert.AreEqual(r.sm.ExpectedResidentChunks, r.store.ResidentCount, "resident count != the load square");
        Debug.Log($"[Task123] slab {start.Count} evicted over 1 + {frames} frames at slice {SLICE}");
    }

    [Test]
    public void SliceZero_EvictsTheWholeSlabInTheCrossingFrame()
    {
        using var r = new Rig(0, "zero");
        int n = r.store.ResidentCount;
        long e0 = r.sm.ChunksEvictedTotal;
        r.sm.Update(JUMP);
        Assert.AreEqual(n, r.sm.ChunksEvictedTotal - e0, "slice 0 must evict the whole slab at once (old)");
        Assert.AreEqual(0, r.sm.EvictQueueCount);
    }

    [Test]
    public void QueuedChunk_BackInRange_IsKept()
    {
        const int SLICE = 1;
        using var r = new Rig(SLICE, "reentry");
        var start = Resident(r.store);

        r.sm.Update(JUMP);                       // queue the start square, evict 1
        Assert.AreEqual(start.Count - SLICE, r.sm.EvictQueueCount);
        long e0 = r.sm.ChunksEvictedTotal;

        // Walk straight back before the slab drains: every queued start chunk is
        // in range again and must survive its turn in the queue.
        int frames = 0;
        do { r.sm.Update(START); AssertAllInWindow(r.store, $"return frame {frames}"); frames++; }
        while (r.sm.EvictQueueCount > 0 && frames < 256);
        Assert.AreEqual(0, r.sm.EvictQueueCount, "queue not empty after the return");

        int kept = 0;
        foreach (var coord in start) if (r.store.IsResident(coord)) kept++;
        Assert.GreaterOrEqual(kept, start.Count - SLICE, "a queued chunk back in range was evicted");
        Assert.GreaterOrEqual(r.sm.EvictQueueKeptTotal, start.Count - SLICE, "kept count not reported");
        // Whatever the return frames did evict is the far square's chunks, not the start's.
        Debug.Log($"[Task123] return: {kept}/{start.Count} start chunks kept, {r.sm.ChunksEvictedTotal - e0} far chunks evicted over {frames} frames");

        Settle(r.sm, START);
        Assert.AreEqual(0, r.sm.LoadDeficit());
        Assert.AreEqual(r.sm.ExpectedResidentChunks, r.store.ResidentCount, "resident count != the load square");
    }

    [Test]
    public void PoolPressure_EvictsTheQueueFirst_NotAnInRangeChunk()
    {
        const int SLICE = 1;
        using var r = new Rig(SLICE, "pressure");
        Assert.Greater(r.store.DenseBricksHeld, 0, "start square holds no dense bricks -- nothing to relieve");

        // In range: the far square's chunks admitted so far. Hold a few frames
        // (one queued chunk leaves per frame) so some are resident to protect.
        int3 cam = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(new float3(JUMP.x, JUMP.y, JUMP.z)));
        var inRange = new List<int3>();
        for (int f = 0; f < 12 && inRange.Count < 3; f++)
        {
            r.sm.Update(JUMP);
            System.Threading.Thread.Sleep(30);
            inRange.Clear();
            foreach (var c in Resident(r.store))
                if (math.max(math.abs(c.x - cam.x), math.abs(c.z - cam.z)) <= LOAD_RADIUS + EngineConfig.HYSTERESIS_RING_CHUNKS)
                    inRange.Add(c);
        }
        int queued = r.sm.EvictQueueCount;
        Assert.Greater(queued, 1);

        // Valve line at HALF the bricks held: the frame's one-chunk slice cannot
        // relieve it, the queued start chunks (most of what is held) can.
        r.store.HighWaterBricksOverride = r.store.DenseBricksHeld / 2;
        r.sm.Update(JUMP);
        r.store.HighWaterBricksOverride = -1;

        Assert.Greater(r.sm.EvictQueueFlushedTotal, 0, "pressure did not take from the queue");
        Assert.Less(r.sm.EvictQueueCount, queued - SLICE, "pressure evicted no more than the slice");
        foreach (var c in inRange)
            Assert.IsTrue(r.store.IsResident(c), $"in-range {c} was evicted while queued chunks waited");
        AssertAllInWindow(r.store, "pressure frame");
        Debug.Log($"[Task123] pressure: queue {queued} -> {r.sm.EvictQueueCount}, flushed {r.sm.EvictQueueFlushedTotal}, in-range kept {inRange.Count}");
    }

    private static void Settle(StreamManager sm, Vector3 cam)
    {
        var clk = System.Diagnostics.Stopwatch.StartNew();
        do { sm.Update(cam); sm.WaitForIdle(); }
        while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0) && clk.Elapsed.TotalSeconds < 120);
        for (int i = 0; i < 2; i++) { sm.Update(cam); sm.WaitForIdle(); }
    }
}
