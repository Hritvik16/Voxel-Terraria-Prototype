// ==========================================
// TorchFloodL3Tests.cs -- task 165 (TORCH_LIGHT_PLAN.md step L3)
//
// The incremental torch flood (TorchFloodPool + TorchFloodBuild):
//   - FROM SCRATCH == ORACLE: the brick flood (direct bricks + sparse Dial)
//     merged from per-torch caches equals TorchFloodPool.Encode(task 157's
//     per-voxel oracle) EXACTLY: sealed axis wall, edge-touching 0.1 m diagonal
//     (escapes), closed box, L-bend, open ground with a torch on a post (12 m)
//   - INCREMENTAL == FROM SCRATCH: random torch place / remove, blocks, digs,
//     0.1 m axis and diagonal walls (fuzz), a doorway sealed and reopened,
//     a chunk evicted and re-admitted -- after each step the union equals a
//     fresh rebuild EXACTLY (TorchFloodPool.Diff: kinds, cells, escapes, counts)
//   - removing every torch frees every brick and chunk table
// Pools here run without GPU buffers (Gpu false) and are flushed synchronously.
// ==========================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public class TorchFloodL3Tests
{
    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditOps _ops;
    private readonly List<TorchFloodPool> _pools = new List<TorchFloodPool>();
    private readonly List<LightFloodReference> _oracles = new List<LightFloodReference>();

    [TearDown]
    public void Teardown()
    {
        foreach (var p in _pools) p.Dispose();
        _pools.Clear();
        foreach (var o in _oracles) o.Dispose();
        _oracles.Clear();
        _pool?.Dispose(); _pool = null;
    }

    private void NewWorld(int3 chunks, Func<int3, byte> fillFor)
    {
        _pool?.Dispose();
        _pool = new BrickDataPool(80000);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(512));
        for (int cz = 0; cz < chunks.z; cz++)
        for (int cy = 0; cy < chunks.y; cy++)
        for (int cx = 0; cx < chunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = fillFor(new int3(cx, cy, cz)) });
        var edits = new EditService();
        edits.AttachWorld(_store, _store, _store, null);
        _ops = new EditOps(EditOpLog.InMemory(165u), edits, _store);
    }

    private void Fill(int3 lo, int3 hi, byte m)
    {
        int3 ext = hi - lo + 1;
        int e = math.cmin(ext);
        if ((e & 1) == 0) e--;
        int s = (e - 1) / 2;
        foreach (int x in Starts(lo.x, hi.x, e))
        foreach (int y in Starts(lo.y, hi.y, e))
        foreach (int z in Starts(lo.z, hi.z, e))
            _ops.Record(new EditOp(EditOpKind.Box, new int3(x, y, z) + s, (ushort)s, m));
    }

    private static IEnumerable<int> Starts(int lo, int hi, int e)
    {
        int a = lo;
        for (; a + e - 1 < hi; a += e) yield return a;
        yield return hi - e + 1;
    }

    private void Put(int3 v, byte m) => _ops.Record(new EditOp(EditOpKind.Box, v, 0, m));

    private static List<int3> Cells(int3 lo, int3 hi)
    {
        var l = new List<int3>();
        for (int z = lo.z; z <= hi.z; z++)
        for (int y = lo.y; y <= hi.y; y++)
        for (int x = lo.x; x <= hi.x; x++) l.Add(new int3(x, y, z));
        return l;
    }

    private TorchFloodPool NewPool(float reachM)
    {
        var p = new TorchFloodPool(_store, reachM, false);
        _pools.Add(p);
        return p;
    }

    private static List<(int3, byte)> Sig(Dictionary<int3, byte> torches)
    {
        var l = new List<(int3, byte)>();
        foreach (var kv in torches) l.Add((kv.Key, kv.Value));
        return l;
    }

    private TorchFloodPool Scratch(Dictionary<int3, byte> torches, float reachM)
    {
        var p = NewPool(reachM);
        p.SetTorches(Sig(torches));
        p.Flush();
        return p;
    }

    private TorchFloodPool.Encoded OracleEncode(Dictionary<int3, byte> torches, float reachM, out LightFloodReference o)
    {
        o = new LightFloodReference(_store);
        _oracles.Add(o);
        var keys = new List<int3>(torches.Keys);
        keys.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y != q.y ? p.y.CompareTo(q.y) : p.z.CompareTo(q.z));
        foreach (int3 k in keys) if (!o.Open(k)) o.AddTorch(k, LightFloodReference.SeedForMaterial(torches[k], reachM));
        return TorchFloodPool.Encode(o, _store);
    }

    private void AssertSame(TorchFloodPool.Encoded a, TorchFloodPool.Encoded b, string what)
    {
        string d = TorchFloodPool.Diff(a, b);
        if (d != null) Assert.Fail($"{what}: {d}");
    }

    // ---------------------------------------------------------------------
    // From scratch == the oracle
    // ---------------------------------------------------------------------

    private static readonly int3 RoomLo = new int3(20, 20, 20), RoomHi = new int3(79, 49, 79);

    private Dictionary<int3, byte> Room(Func<int3, bool> wall, int3 torch)
    {
        NewWorld(new int3(1, 1, 1), c => Materials.Stone);
        Fill(RoomLo, RoomHi, 0);
        foreach (int3 v in Cells(RoomLo, RoomHi)) if (wall(v)) Put(v, Materials.Stone);
        Put(torch, Materials.Torch);
        return new Dictionary<int3, byte> { [torch] = Materials.Torch };
    }

    private void CheckScratchVsOracle(Dictionary<int3, byte> torches, float reachM, string what, Func<int3, bool> mustBeDark = null)
    {
        var sw = Stopwatch.StartNew();
        TorchFloodPool.TimeFloods = true;
        var p = Scratch(torches, reachM);
        TorchFloodPool.TimeFloods = false;
        double poolMs = sw.Elapsed.TotalMilliseconds;
        float floodMs = 0; foreach (float f in p.FloodRunMs) floodMs += f;
        sw.Restart();
        var oe = OracleEncode(torches, reachM, out var o);
        double oracleMs = sw.Elapsed.TotalMilliseconds;
        var e = p.Current;
        AssertSame(e, oe, what);
        Assert.Greater(e.airBricks + e.mixedBricks, 0, $"{what}: something lit");
        if (mustBeDark != null)
            foreach (var (b, _) in o.LitBricks())
                for (int i = 0; i < 512; i++)
                {
                    int3 v = (b << 3) + new int3(i & 7, (i >> 3) & 7, i >> 6);
                    if (o.Open(v) && mustBeDark(v) && math.any(TorchFloodPool.SampleLevels(e, v) != 0f)) Assert.Fail($"{what}: sealed-side voxel {v} lit");
                }
        int reached = 0, direct = 0, lit = 0;
        foreach (var f in p.FloodLog) { reached += (int)f.y; lit += (int)f.z; direct += (int)f.w; }
        UnityEngine.Debug.Log($"[165] {what}: EXACT == oracle encode; lit bricks {lit} ({direct} direct, no search), searched voxels {reached:N0} " +
                              $"vs oracle {o.LastReached:N0}; air {e.airBricks} mixed {e.mixedBricks} escapes {e.escapes}; pool {poolMs:F0} ms (flood jobs alone {floodMs:F1}) " +
                              $"(caches {p.CacheBytes / 1024.0:F0} KB, union {p.UnionBytes / 1024.0:F0} KB), oracle+encode {oracleMs:F0} ms");
    }

    [Test]
    public void Scratch_EqualsOracle_SealedAxisWall()
    {
        var t = Room(v => v.x == 50, new int3(35, 21, 50));
        CheckScratchVsOracle(t, 12f, "axis wall", v => v.x > 50);
    }

    [Test]
    public void Scratch_EqualsOracle_EdgeTouchingDiagonal()
    {
        var t = Room(v => v.x == v.z, new int3(70, 21, 30));
        CheckScratchVsOracle(t, 12f, "x==z diagonal", v => v.x < v.z);
        Assert.Greater(_pools[0].Escapes, 0, "the diagonal splits 0.2 m cells");
    }

    [Test]
    public void Scratch_EqualsOracle_ClosedBoxAndLBend()
    {
        // a closed 0.1 m box round one torch, and an L-shaped 1 m corridor lit from its end
        NewWorld(new int3(1, 1, 1), c => Materials.Stone);
        Fill(new int3(20, 20, 20), new int3(40, 40, 40), 0);
        foreach (int3 v in Cells(new int3(26, 26, 26), new int3(34, 34, 34)))
            if (math.any(v == 26) || math.any(v == 34)) Put(v, Materials.Stone);
        Fill(new int3(60, 30, 20), new int3(69, 39, 100), 0);
        Fill(new int3(60, 30, 91), new int3(110, 39, 100), 0);
        var t = new Dictionary<int3, byte> { [new int3(30, 27, 30)] = Materials.TorchBlue, [new int3(64, 30, 25)] = Materials.Torch };
        foreach (var kv in t) Put(kv.Key, kv.Value);
        CheckScratchVsOracle(t, 12f, "closed box + L-bend",
            v => math.all(v >= 20) && math.all(v <= 40) && !(math.all(v > 26) && math.all(v < 34)));
    }

    [Test]
    public void Scratch_EqualsOracle_OpenGroundTorchOnPost()
    {
        // 3 x 3 x 3 chunks: stone below y 128, a bump, air above; a torch on a 1.5 m post (the outdoor case)
        NewWorld(new int3(3, 3, 3), c => c.y == 0 ? Materials.Stone : (byte)0);
        Fill(new int3(200, 128, 170), new int3(215, 140, 230), Materials.Stone);   // a low wall casting a flood shadow
        var t = new int3(192, 143, 192);
        for (int y = 128; y < 143; y++) Put(new int3(192, y, 192), Materials.Stone);
        Put(t, Materials.Torch);
        var torches = new Dictionary<int3, byte> { [t] = Materials.Torch };
        CheckScratchVsOracle(torches, 12f, "open ground, torch on a post");
        var f = _pools[0].FloodLog[0];
        Assert.Greater(f.w, 1000f, "most air bricks are direct (no search)");
    }

    // ---------------------------------------------------------------------
    // Incremental == from scratch
    // ---------------------------------------------------------------------

    private static readonly byte[] TorchMats = { Materials.Torch, Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple };

    private void Step(TorchFloodPool live, Dictionary<int3, byte> torches, IEnumerable<int3> changed, float reachM, string what, bool oracleToo)
    {
        if (changed != null) foreach (int3 v in changed) live.OnVoxelChanged(v);
        live.SetTorches(Sig(torches));
        live.Flush();
        var fresh = Scratch(torches, reachM);
        AssertSame(live.Current, fresh.Current, what + " (incremental vs scratch)");
        _pools.Remove(fresh); fresh.Dispose();
        if (oracleToo)
        {
            AssertSame(live.Current, OracleEncode(torches, reachM, out var o), what + " (incremental vs oracle)");
            _oracles.Remove(o); o.Dispose();
        }
    }

    private void Fuzz(int ops, int seed)
    {
        const float reach = 3f;
        NewWorld(new int3(1, 1, 1), c => Materials.Stone);
        var rng = new System.Random(seed);
        int3 lo = new int3(40, 40, 40), hi = new int3(87, 87, 87);
        Fill(lo, hi, 0);
        Fill(new int3(60, 40, 40), new int3(60, 87, 87), Materials.Stone);
        Fill(new int3(40, 60, 40), new int3(87, 60, 87), Materials.Stone);
        var torches = new Dictionary<int3, byte>();
        var live = NewPool(reach);
        int3 Rand(int3 a, int3 b) => new int3(rng.Next(a.x, b.x + 1), rng.Next(a.y, b.y + 1), rng.Next(a.z, b.z + 1));
        bool HitsTorch(List<int3> cells) { foreach (int3 c in cells) if (torches.ContainsKey(c)) return true; return false; }
        var counts = new int[5];
        for (int i = 0; i < ops; i++)
        {
            int kind = rng.Next(100);
            string what;
            List<int3> changed;
            if (kind < 22 || torches.Count == 0)
            {
                int3 v = Rand(lo, hi);
                if (torches.ContainsKey(v)) continue;
                byte m = TorchMats[rng.Next(TorchMats.Length)];
                Put(v, m);
                torches[v] = m;
                changed = new List<int3> { v };
                what = $"place torch {v}"; counts[0]++;
            }
            else if (kind < 35)
            {
                var keys = new List<int3>(torches.Keys);
                int3 v = keys[rng.Next(keys.Count)];
                torches.Remove(v);
                Put(v, rng.Next(2) == 0 ? (byte)0 : Materials.Stone);
                changed = new List<int3> { v };
                what = $"remove torch {v}"; counts[1]++;
            }
            else if (kind < 80)
            {
                bool solid = kind < 57;
                int s = rng.Next(0, solid ? 2 : 3);
                int3 c = Rand(lo, hi);
                changed = Cells(c - s, c + s);
                if (HitsTorch(changed)) continue;
                _ops.Record(new EditOp(EditOpKind.Box, c, (ushort)s, solid ? Materials.Stone : (byte)0));
                what = $"{(solid ? "block" : "dig")} {c} s{s}"; counts[solid ? 2 : 3]++;
            }
            else
            {
                bool solid = rng.Next(2) == 0;
                changed = new List<int3>();
                int3 c = Rand(lo, hi);
                int len = rng.Next(4, 16);
                if (rng.Next(2) == 0)
                {
                    int axis = rng.Next(3);
                    foreach (int3 v in Cells(c, c + len)) { int3 w = v; w[axis] = c[axis]; if (!changed.Contains(w)) changed.Add(w); }
                }
                else
                    for (int k = 0; k < len; k++) for (int y = 0; y < len; y++) changed.Add(c + new int3(k, y, k));
                changed.RemoveAll(v => math.any(v < lo) || math.any(v > hi));
                if (changed.Count == 0 || HitsTorch(changed)) continue;
                foreach (int3 v in changed) Put(v, solid ? Materials.Stone : (byte)0);
                what = $"thin {(solid ? "wall" : "cut")} {c} x{changed.Count}"; counts[4]++;
            }
            Step(live, torches, changed, reach, $"op {i} ({what})", i % 10 == 9);
        }
        TestContext.WriteLine($"fuzz seed {seed}: torches placed {counts[0]}, removed {counts[1]}, blocks {counts[2]}, digs {counts[3]}, " +
                              $"thin {counts[4]}; {torches.Count} left; floods {live.FloodsDone}, merges {live.MergesDone}");
        Assert.Greater(live.AirBricks + live.MixedBricks, 0);
        // every torch removed: every brick, escape and chunk table freed
        torches.Clear();
        live.SetTorches(Sig(torches));
        live.Flush();
        Assert.AreEqual(0, live.AirBricks + live.MixedBricks, "every brick freed");
        Assert.AreEqual(0, live.Escapes, "every escape freed");
        Assert.AreEqual(0, live.Chunks, "every chunk table freed");
        Assert.AreEqual(0, live.CacheCount);
    }

    [Test]
    public void Fuzz_IncrementalEqualsScratch_ThenAllFreed() => Fuzz(80, 165);

    [Test, Category("Slow")]
    public void Fuzz_Long() { Fuzz(300, 1651); Fuzz(300, 1652); }

    [Test]
    public void Doorway_SealAndReopen_12m()
    {
        // a wall across the room with a 1 m doorway; torch on the near side; seal the door with a 0.1 m
        // plate, then cut a 0.1 m diagonal slot (edge-touching: must stay dark), then reopen the door
        var torches = Room(v => v.x == 50 && !(v.z >= 45 && v.z <= 54 && v.y <= 39), new int3(35, 21, 50));
        var live = NewPool(12f);
        Step(live, torches, null, 12f, "doorway open", true);
        var open = live.Current;
        Assert.Greater(TorchFloodPool.SampleLevels(open, new int3(60, 25, 50)).x, 0f, "lit through the door");

        var door = Cells(new int3(50, 20, 45), new int3(50, 39, 54));
        foreach (int3 v in door) Put(v, Materials.Stone);
        Step(live, torches, door, 12f, "doorway sealed", true);
        foreach (int3 v in Cells(new int3(51, 20, 20), new int3(79, 49, 79)))
            if (math.any(TorchFloodPool.SampleLevels(live.Current, v) != 0f)) Assert.Fail($"sealed: far-side voxel {v} lit");

        // a 2-voxel-thick plate pierced along an edge-touching diagonal: the cells meet only at edges
        var plate = Cells(new int3(51, 20, 45), new int3(51, 39, 54));
        foreach (int3 v in plate) Put(v, Materials.Stone);
        var slot = new List<int3>();
        for (int k = 0; k < 10; k++) { slot.Add(new int3(50, 25 + k, 45 + k)); slot.Add(new int3(51, 26 + k, 45 + k)); }
        foreach (int3 v in slot) Put(v, 0);
        var ch = new List<int3>(plate); ch.AddRange(slot);
        Step(live, torches, ch, 12f, "diagonal slot through a 0.2 m plate", true);
        foreach (int3 v in Cells(new int3(52, 20, 20), new int3(79, 49, 79)))
            if (math.any(TorchFloodPool.SampleLevels(live.Current, v) != 0f)) Assert.Fail($"diagonal slot: far-side voxel {v} lit");

        foreach (int3 v in door) Put(v, 0);
        foreach (int3 v in plate) Put(v, 0);
        ch = new List<int3>(door); ch.AddRange(plate);
        Step(live, torches, ch, 12f, "doorway reopened", true);
        AssertSame(live.Current, open, "reopened == the first open state");
    }

    [Test]
    public void Streaming_EvictReadmit_EqualsScratch()
    {
        // two chunks side by side, torch 3 m from the seam in chunk 0; the light crosses into chunk 1
        NewWorld(new int3(2, 1, 1), c => (byte)0);
        Fill(new int3(0, 0, 0), new int3(255, 9, 127), Materials.Stone);   // a floor
        var t = new int3(98, 20, 64);
        for (int y = 10; y < 20; y++) Put(new int3(98, y, 64), Materials.Stone);
        Put(t, Materials.Torch);
        var torches = new Dictionary<int3, byte> { [t] = Materials.Torch };
        var live = NewPool(6f);
        Step(live, torches, null, 6f, "both resident", true);
        int litBoth = live.AirBricks + live.MixedBricks;

        _store.EvictChunk(new int3(1, 0, 0));
        Step(live, torches, null, 6f, "chunk 1 evicted", true);
        Assert.Less(live.AirBricks + live.MixedBricks, litBoth, "chunk 1's light is gone");

        _store.InsertChunk(new Chunk { coord = new int3(1, 0, 0), isUniform = true, uniformMaterial = 0 });
        Fill(new int3(128, 0, 0), new int3(255, 9, 127), Materials.Stone);
        Step(live, torches, null, 6f, "chunk 1 re-admitted", true);
        Assert.AreEqual(litBoth, live.AirBricks + live.MixedBricks);
    }

    [Test]
    public void ManyTorches_OverlappingBoxes_RemoveOneEqualsScratch()
    {
        // 6 torches whose boxes overlap: removing one re-floods nobody else, and the union still equals scratch
        NewWorld(new int3(2, 1, 1), c => (byte)0);
        Fill(new int3(0, 0, 0), new int3(255, 9, 127), Materials.Stone);
        var torches = new Dictionary<int3, byte>();
        for (int k = 0; k < 6; k++)
        {
            var t = new int3(40 + 30 * k, 18, 50 + 5 * k);
            for (int y = 10; y < 18; y++) Put(new int3(t.x, y, t.z), Materials.Stone);
            Put(t, TorchMats[k & 3]);
            torches[t] = TorchMats[k & 3];
        }
        var live = NewPool(4f);
        Step(live, torches, null, 4f, "6 torches", true);
        int floods = live.FloodsDone;
        torches.Remove(new int3(100, 18, 60));   // unregistered; the voxel stays solid (no terrain edit)
        Step(live, torches, null, 4f, "one removed", true);
        Assert.AreEqual(floods, live.FloodsDone, "removing a torch re-floods no other torch");
        // a block next to torch 0 re-floods only the torches whose box holds it
        var blk = Cells(new int3(44, 10, 48), new int3(46, 14, 52));
        foreach (int3 v in blk) Put(v, Materials.Stone);
        Step(live, torches, blk, 4f, "block beside torch 0", true);
        Assert.LessOrEqual(live.FloodsDone - floods, 2, "only the torches whose box the block touches re-flood");
    }

    [Test]
    public void Report_FloodPhases_OpenGround()
    {
        if (Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        NewWorld(new int3(3, 3, 3), c => c.y == 0 ? Materials.Stone : (byte)0);
        Fill(new int3(200, 128, 170), new int3(215, 140, 230), Materials.Stone);
        var t = new int3(192, 143, 192);
        for (int y = 128; y < 143; y++) Put(new int3(192, y, 192), Materials.Stone);
        Put(t, Materials.Torch);
        uint seed = LightFloodReference.SeedForMaterial(Materials.Torch, 12f);
        int top = LightFloodReference.MaxChannel(seed);
        LightFloodReference.TorchBox(t, top, out int3 lo, out int3 dims);
        var sc = new TorchFloodScratch();
        var stats = new Unity.Collections.NativeArray<int>(8, Unity.Collections.Allocator.Persistent);
        try
        {
            var line = new System.Text.StringBuilder("[165] open-ground flood phases (min of 5, ms):");
            var sw = new Stopwatch();
            double best = 1e9;
            for (int k = 0; k < 5; k++) { sw.Restart(); TorchFloodBuild.Snapshot(_store, lo, dims >> 3, sc); best = Math.Min(best, sw.Elapsed.TotalMilliseconds); }
            line.Append($" snapshot {best:F2}");
            foreach (int ph in new[] { 1, 2, 0 })
            {
                best = 1e9;
                for (int k = 0; k < 5; k++)
                {
                    var c = new TorchFloodCache();
                    var job = TorchFloodBuild.MakeFloodJob(sc, c, t - lo, dims, dims >> 3, top, stats);
                    job.phaseLimit = ph;
                    sw.Restart(); job.Run(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                    c.Dispose();
                }
                line.Append($" | to {(ph == 1 ? "direct" : ph == 2 ? "seeds" : "end")} {best:F2}");
            }
            line.Append($" | searched {stats[0]:N0} direct {stats[1]} stored {stats[2]} seeds {stats[4]:N0} pops {stats[5]:N0} searched bricks {stats[6]}; scratch {sc.Bytes / 1048576.0:F1} MB");
            UnityEngine.Debug.Log(line.ToString());
        }
        finally { stats.Dispose(); sc.Dispose(); }
    }
}
