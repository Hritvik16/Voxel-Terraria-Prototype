// Assets/CoreEngine/Tests/AdmitBudgetTests.cs
//
// Task 168d (cut E): the streamer's per-frame admission drain under a time
// budget (StreamManager.AdmitBudgetMs). The budget only spreads the same
// completions over more frames, in the same FIFO order, so at rest the world
// must be EXACTLY the unbudgeted one. Checked on the small flat world of
// EvictionSlicingTests (sizeClass 0, load radius 2): two rigs prime the same
// start square, jump 7 chunks, and are driven with Update() ONLY (WaitForIdle
// drains unbudgeted, so it is never called after the jump) until both are
// idle with no load deficit. Then:
//   - the resident set is identical;
//   - every resident chunk holds the same voxels (strided compare) and the same
//     uniform flag / material / dense brick count;
//   - the budgeted rig admitted at most one chunk per frame (a ~0 ms budget
//     still admits one: progress is guaranteed) and its budget actually stopped
//     a drain with completions queued (so the test exercised the slicing).
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class AdmitBudgetTests
{
    private const int LOAD_RADIUS = 2;
    private static readonly Vector3 START = new Vector3(64f, 20f, 64f);
    private static readonly Vector3 JUMP = START + new Vector3(90f, 0f, 0f);   // 7 chunks of 12.8 m
    private const int CHUNK_EDGE_VOXELS = EngineConfig.CHUNK_EDGE_BRICKS * EngineConfig.BRICK_EDGE;

    private sealed class Rig : System.IDisposable
    {
        public BrickDataPool pool; public ChunkHandleAllocator alloc; public ChunkStore store;
        public TerrainClipmap clip; public LODCascadeManager cascades; public StreamManager sm;
        readonly string _deltas;

        public Rig(string name)
        {
            var meta = new WorldMetaData { seed = 42, sizeClass = 0 };
            pool = new BrickDataPool(400000, rangeAware: true);
            alloc = new ChunkHandleAllocator(1024);
            store = new ChunkStore(pool, alloc);
            var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
            clip = new TerrainClipmap(mirror, pool.Capacity);
            cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
            _deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "admit_budget_test_" + name);
            if (System.IO.Directory.Exists(_deltas)) System.IO.Directory.Delete(_deltas, true);
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, _deltas, LOAD_RADIUS);
            sm.PrimeWindow(START);
            var clk = System.Diagnostics.Stopwatch.StartNew();
            do { sm.Update(START); sm.WaitForIdle(); }
            while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0) && clk.Elapsed.TotalSeconds < 120);
            Assert.AreEqual(0, sm.LoadDeficit(), "start square not resident");
        }

        public void Dispose()
        {
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
            if (System.IO.Directory.Exists(_deltas)) System.IO.Directory.Delete(_deltas, true);
        }
    }

    /// Update() only, until idle with no deficit. Returns the frame count and the
    /// largest number of chunks one frame admitted.
    private static int DriveToRest(StreamManager sm, Vector3 cam, out int maxAdmitted)
    {
        maxAdmitted = 0;
        int frames = 0, idleFrames = 0;
        var clk = System.Diagnostics.Stopwatch.StartNew();
        while (idleFrames < 3 && clk.Elapsed.TotalSeconds < 180)
        {
            sm.Update(cam);
            maxAdmitted = math.max(maxAdmitted, sm.LastAdmitted);
            frames++;
            bool idle = sm.LoadDeficit() == 0 && sm.PendingLoads == 0 && sm.InFlightLoads == 0
                        && sm.EvictQueueCount == 0 && sm.LastAdmitted == 0;
            idleFrames = idle ? idleFrames + 1 : 0;
            // Let completions pile up between frames, so an unbudgeted drain
            // takes several at once and the budgeted one has to slice.
            System.Threading.Thread.Sleep(20);
        }
        return frames;
    }

    private static Dictionary<int3, long> Fingerprint(ChunkStore store)
    {
        var d = new Dictionary<int3, long>();
        foreach (var c in store.ResidentChunks())
        {
            long h = c.isUniform ? 1000003L * (c.uniformMaterial + 1) : 17;
            int dense = 0;
            if (c.bricks != null)
                foreach (var b in c.bricks)
                {
                    // Dense = bit 31; its pool index depends on allocation order, so
                    // only the dense/uniform layout and uniform materials are compared.
                    if ((b.data & 0x80000000u) != 0) dense++;
                    else h = h * 31 + b.data;
                }
            h = h * 31 + dense;
            int3 o = c.coord * CHUNK_EDGE_VOXELS;
            for (int z = 0; z < CHUNK_EDGE_VOXELS; z += 3)
            for (int y = 0; y < CHUNK_EDGE_VOXELS; y += 3)
            for (int x = 0; x < CHUNK_EDGE_VOXELS; x += 3)
                h = h * 1099511628211L ^ store.GetVoxel(o + new int3(x, y, z));
            d[c.coord] = h;
        }
        return d;
    }

    [Test]
    public void BudgetedDrain_ReachesTheSameWorldAsUnbudgeted()
    {
        double was = StreamManager.AdmitBudgetMs;
        try
        {
            Dictionary<int3, long> a, b;
            int framesA, framesB, maxA, maxB;
            long stopsB;

            StreamManager.AdmitBudgetMs = 0;
            using (var r = new Rig("off"))
            {
                framesA = DriveToRest(r.sm, JUMP, out maxA);
                Assert.AreEqual(0, r.sm.LoadDeficit(), "unbudgeted: destination square not resident");
                Assert.AreEqual(0, r.sm.AdmitBudgetStops, "budget OFF must never stop a drain");
                a = Fingerprint(r.store);
            }

            StreamManager.AdmitBudgetMs = 1e-6;   // ~0 ms: exactly one admission per frame
            using (var r = new Rig("on"))
            {
                framesB = DriveToRest(r.sm, JUMP, out maxB);
                Assert.AreEqual(0, r.sm.LoadDeficit(), "budgeted: destination square not resident");
                stopsB = r.sm.AdmitBudgetStops;
                Assert.Greater(r.sm.AdmitWaitCount, 0, "admit-wait stats not recorded");
                b = Fingerprint(r.store);
            }

            Assert.LessOrEqual(maxB, 1, "a ~0 ms budget must admit exactly one chunk per frame");
            Assert.Greater(stopsB, 0, "the budget never stopped a drain: the test did not exercise slicing");
            Assert.AreEqual(a.Count, b.Count, "resident chunk count differs");
            foreach (var kv in a)
            {
                Assert.IsTrue(b.TryGetValue(kv.Key, out long hb), $"{kv.Key} resident unbudgeted but not budgeted");
                Assert.AreEqual(kv.Value, hb, $"{kv.Key}: voxels / brick layout differ between the two drains");
            }
            Debug.Log($"[Task168d] at rest: {a.Count} chunks identical; unbudgeted {framesA} frames (max {maxA}/frame), " +
                      $"budgeted {framesB} frames (max {maxB}/frame, {stopsB} budget stops)");
        }
        finally { StreamManager.AdmitBudgetMs = was; }
    }

    [Test]
    public void BudgetOff_KeepsTheCountCap()
    {
        double was = StreamManager.AdmitBudgetMs;
        try
        {
            StreamManager.AdmitBudgetMs = 0;
            using var r = new Rig("cap");
            DriveToRest(r.sm, JUMP, out int max);
            Assert.LessOrEqual(max, EngineConfig.MAX_CHUNK_LOADS_PER_FRAME, "count cap exceeded");
        }
        finally { StreamManager.AdmitBudgetMs = was; }
    }
}
