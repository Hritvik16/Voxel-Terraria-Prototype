// Assets/CoreEngine/Tests/CpuBrickCapTests.cs
//
// Task 93g (engine 14d4, Amendment 8.15 row D4, §2 item 4): the tg2 world's CPU
// 0.1 m brick cap is set from a measured peak x 1.5 (players build and blast),
// rounded up to a 10 k multiple, never above the old 750,000 without the owner.
// §3.6's LRU valve line stays at 0.85 x cap. Checked here:
//   - the cap's value and derivation, the valve line, the flat world's cap
//     untouched;
//   - at the NEW cap, pushing the store over the valve line evicts by §3.6's
//     LRU (coldest first, never the camera's 5x5), saves every edited victim's
//     D.1 delta BEFORE dropping it, and the edits come back on reload.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class CpuBrickCapTests
{
    // Task 93g's measurement: max(task 55's 379,527, this task's play peak) x 1.5.
    private const int MEASURED_PEAK = 379527;
    private const int EXPECTED_CAP = 570000;

    [Test]
    public void Tg2Cap_IsMeasuredPeakTimesOnePointFive_AndTheValveStaysAt085()
    {
        int cap = EngineConfig.TG2_BRICK_POOL_CAP;
        Assert.AreEqual(EXPECTED_CAP, cap, "TG2_BRICK_POOL_CAP");
        Assert.AreEqual(0, cap % 10000, "rounded up to a 10 k multiple");
        Assert.GreaterOrEqual(cap, (int)System.Math.Ceiling(MEASURED_PEAK * 1.5), "cap below peak x 1.5");
        Assert.Less(cap - 10000, (int)System.Math.Ceiling(MEASURED_PEAK * 1.5), "rounded up by more than one 10 k step");
        Assert.LessOrEqual(cap, 750000, "no raise above 750,000 without the owner");

        Assert.AreEqual(0.85f, EngineConfig.BRICK_POOL_HIGH_WATER_FRACTION, "§3.6 valve fraction changed");
        Assert.AreEqual((int)(cap * 0.85f), EngineConfig.HighWaterBricksFor(cap), "valve line != 0.85 x cap");
        Assert.AreEqual(484500, EngineConfig.HighWaterBricksFor(cap));
        Assert.Greater(EngineConfig.HighWaterBricksFor(cap), MEASURED_PEAK, "the valve would fire at the measured peak");

        Assert.AreEqual(500000, EngineConfig.BRICK_POOL_CAP, "the Phase 4 rigs' cap must not move");
        Assert.AreEqual(EngineConfig.BrickPoolHighWaterBricks, EngineConfig.HighWaterBricksFor(EngineConfig.BRICK_POOL_CAP));
    }

    private const int LOAD_RADIUS = 6;   // 13 x 13 chunks: 144 outside the camera's 5x5

    [Test]
    public void ValveAtTheNewCap_EvictsColdestFirst_AndSavesEditsBeforeDropping()
    {
        int cap = EngineConfig.TG2_BRICK_POOL_CAP;
        int highWater = EngineConfig.HighWaterBricksFor(cap);
        var meta = new WorldMetaData { seed = 42, sizeClass = 0 };
        var pool = new BrickDataPool(cap, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        var store = new ChunkStore(pool, alloc) { HighWaterBricksOverride = highWater };
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        var clip = new TerrainClipmap(mirror, pool.Capacity);
        var cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
        string deltas = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cpu_brick_cap_test_deltas");
        if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        StreamManager sm = null;
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, deltas, LOAD_RADIUS);
            Vector3 cam = new Vector3(64f, 20f, 64f);
            sm.PrimeWindow(cam);
            Settle(sm, cam);
            Assert.AreEqual(0, sm.LoadDeficit(), "start square not resident");
            Assert.IsFalse(store.IsUnderPoolPressure, "pressure before any edit");
            int3 camChunk = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel((float3)cam));

            // Edit chunks outside the camera's 5x5, coldest first (the valve's own
            // order: last touched, then farthest), until the dense count passes
            // the valve line by one chunk: one voxel per brick, so every brick
            // turns dense and every edited chunk is deltaDirty (a player edit).
            var edited = new Dictionary<int3, List<(int3 v, byte m)>>();
            var candidates = new List<Chunk>();
            foreach (var c in store.ResidentChunks())
                if (Cheb(c.coord, camChunk) > 2) candidates.Add(c);
            candidates.Sort((a, b) =>
            {
                int t = sm.GetRecord(a.coord).lastTouchedFrame.CompareTo(sm.GetRecord(b.coord).lastTouchedFrame);
                return t != 0 ? t : Cheb(b.coord, camChunk).CompareTo(Cheb(a.coord, camChunk));
            });
            int target = highWater + EngineConfig.BRICKS_PER_CHUNK;
            foreach (var c in candidates)
            {
                if (store.DenseBricksHeld >= target) break;
                var list = new List<(int3, byte)>();
                int3 o = c.coord * EngineConfig.CHUNK_EDGE_VOXELS;
                for (int bz = 0; bz < EngineConfig.CHUNK_EDGE_BRICKS; bz++)
                for (int by = 0; by < EngineConfig.CHUNK_EDGE_BRICKS; by++)
                for (int bx = 0; bx < EngineConfig.CHUNK_EDGE_BRICKS; bx++)
                {
                    int3 v = o + new int3(bx, by, bz) * EngineConfig.BRICK_EDGE + new int3(3, 3, 3);
                    byte m = store.GetVoxel(v) == Materials.Stone ? Materials.MossyStone : Materials.Stone;
                    store.SetVoxel(v, m);
                    list.Add((v, m));
                }
                edited[c.coord] = list;
            }
            Assert.GreaterOrEqual(store.DenseBricksHeld, target, $"only {store.DenseBricksHeld} dense bricks after editing every candidate");
            Assert.IsTrue(store.IsUnderPoolPressure, "over the valve line but no pressure");
            Assert.LessOrEqual(pool.InUse, cap);

            // Snapshot the LRU keys, then one frame: §3.6's valve runs.
            var touched = new Dictionary<int3, uint>();
            foreach (var c in store.ResidentChunks()) touched[c.coord] = sm.GetRecord(c.coord).lastTouchedFrame;
            int lru0 = sm.LruEvictionsTotal, saved0 = sm.ChunksSavedTotal;
            sm.Update(cam);

            Assert.IsFalse(store.IsUnderPoolPressure, $"valve left {store.DenseBricksHeld} dense bricks >= {highWater}");
            var victims = new List<int3>();
            foreach (var coord in touched.Keys) if (!store.IsResident(coord)) victims.Add(coord);
            Assert.Greater(victims.Count, 0, "nothing evicted");
            Assert.AreEqual(victims.Count, sm.LruEvictionsTotal - lru0, "evictions not counted as LRU evictions");
            uint victimMax = 0, survivorMin = uint.MaxValue;
            int dirtyVictims = 0;
            foreach (var v in victims)
            {
                Assert.Greater(Cheb(v, camChunk), 2, $"{v} inside the camera's 5x5 was evicted");
                victimMax = math.max(victimMax, touched[v]);
                if (edited.ContainsKey(v)) dirtyVictims++;
            }
            foreach (var kv in touched)
                if (store.IsResident(kv.Key) && Cheb(kv.Key, camChunk) > 2) survivorMin = math.min(survivorMin, kv.Value);
            Assert.LessOrEqual(victimMax, survivorMin, "a warmer chunk was evicted before a colder one (§3.6 LRU)");
            Assert.AreEqual(victims.Count, dirtyVictims, "the coldest chunks were edited, so every victim must be an edited one");
            Assert.AreEqual(dirtyVictims, sm.ChunksSavedTotal - saved0, "an edited victim was dropped without its D.1 save");
            Assert.AreEqual(0, sm.DeltaSaveFailuresTotal, "a D.1 save failed");

            // Lift the valve and reload: every edit of every victim must be back.
            store.HighWaterBricksOverride = int.MaxValue;
            Settle(sm, cam);
            int checkedVoxels = 0;
            foreach (var v in victims)
            {
                Assert.IsTrue(store.IsResident(v), $"{v} did not reload");
                if (!edited.TryGetValue(v, out var list)) continue;
                foreach (var (p, m) in list) { Assert.AreEqual(m, store.GetVoxel(p), $"edit at {p} lost"); checkedVoxels++; }
            }
            Debug.Log($"[Task93g] cap {cap:N0}, valve {highWater:N0}: {victims.Count} LRU victims ({dirtyVictims} edited, saved first), " +
                      $"{checkedVoxels:N0} edited voxels back after reload");
        }
        finally
        {
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
            if (System.IO.Directory.Exists(deltas)) System.IO.Directory.Delete(deltas, true);
        }
    }

    private static int Cheb(int3 a, int3 b) => math.max(math.abs(a.x - b.x), math.abs(a.z - b.z));

    private static void Settle(StreamManager sm, Vector3 cam)
    {
        var clk = System.Diagnostics.Stopwatch.StartNew();
        do { sm.Update(cam); sm.WaitForIdle(); }
        while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0) && clk.Elapsed.TotalSeconds < 120);
        for (int i = 0; i < 2; i++) { sm.Update(cam); sm.WaitForIdle(); }
    }
}
