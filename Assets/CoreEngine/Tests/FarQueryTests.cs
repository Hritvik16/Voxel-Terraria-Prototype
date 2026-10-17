// Assets/CoreEngine/Tests/FarQueryTests.cs
//
// Amendment 8.15 row Q1 (task 104): IFarQuery.GetVoxels == what the chunk
// would load, point for point.
//   Generator   random points, chunk/brick border planes and edges vs chunks
//               Generate made (surface, below it, deep, the sea at negative
//               x/z, below y 0, above the world's top), in ONE call, shuffled.
//               Also the cost per point (single thread, random columns and
//               vertical runs; parallel wall) -- a log line, not asserted.
//   Load order  ops <= S, D.1, ops > S against the stream worker's own steps
//               (Generate, EditOpApply, DeltaCodec.TryDecodeOnto, EditOpApply):
//               a delta with S = 1, a side-table CRC mismatch (all ops, then
//               D.1), a corrupt delta (no D.1), a deleted one; an op across the
//               seam into the chunk below (no delta).
//   Resident    resident chunk -> the store's voxels (an edit the generator
//               does not have, an op applied near); its neighbour -> generator.
//   Streamed    (Slow) a tg2 StreamManager: far answers for a chunk with a
//               saved delta and a later op == the chunk once streamed back in;
//               SaveDelta's ForgetDelta keeps the delta cache honest.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class FarQueryTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }
    [OneTimeTearDown] public void TearDown() { _src?.Dispose(); _src = null; }

    private static string TempDir(string name)
    {
        string d = Path.Combine(Path.GetTempPath(), "farquery_test_" + name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    private static byte Read(Chunk c, BrickDataPool pool, int3 local)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((local.z >> 3) << 8) | ((local.y >> 3) << 4) | (local.x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        return pool.RawData[(int)(d & 0x3FFFFFFFu) * 512 + (((local.z & 7) << 6) | ((local.y & 7) << 3) | (local.x & 7))];
    }

    /// A chunk as the stream worker loads it (StreamManager.GenerateOnWorker's order).
    private sealed class Loaded : System.IDisposable
    {
        public readonly Chunk chunk = new Chunk();
        public readonly BrickDataPool pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
        public readonly int3 coord;
        public bool deltaApplied;

        public Loaded(int3 coord, EditOpSnapshot ops = null, string dir = null)
        {
            this.coord = coord;
            var alloc = new ChunkHandleAllocator(2);
            _src.Generate(coord, chunk, alloc, pool, null);
            ops ??= EditOpSnapshot.Empty;
            int before = ops.Count;
            byte[] bytes = null;
            bool have = dir != null && DeltaCodec.TryReadFile(DeltaCodec.PathFor(dir, coord), out bytes, out _);
            if (ops.Count > 0)
            {
                if (have && bytes.Length >= DeltaCodec.TRAILER_BYTES) before = ops.OpsBeforeDelta(coord, EditOpLog.TrailerCrc(bytes));
                EditOpApply.ApplyToChunk(chunk, coord, alloc, pool, ops.Ops, 0, before);
            }
            if (have) deltaApplied = DeltaCodec.TryDecodeOnto(bytes, coord, 42u, chunk, pool, out _);
            if (before < ops.Count) EditOpApply.ApplyToChunk(chunk, coord, alloc, pool, ops.Ops, before, ops.Count);
        }

        public byte At(int3 v) => Read(chunk, pool, v - (coord << 7));
        public void Dispose() => pool.Dispose();
    }

    private static int3 SurfaceVoxel(out int3 seaVoxel)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        int3 land = default; bool haveLand = false; seaVoxel = default; bool haveSea = false;
        for (int i = 0; i < 400 && !(haveLand && haveSea); i++)
        {
            float2 p = new float2(c, c) + new float2((i % 20) - 10, (i / 20) - 10) * 1500f;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (!haveLand && col.height >= spec.seaLevelY + 300 && col.water <= col.height)
            { land = new int3((int)p.x, col.height, (int)p.y); haveLand = true; }
            if (!haveSea && col.height < spec.seaLevelY - 40)
            { seaVoxel = new int3((int)p.x, spec.seaLevelY, (int)p.y); haveSea = true; }
        }
        Assert.IsTrue(haveLand, "no land site found");
        if (!haveSea) seaVoxel = new int3(-60, spec.seaLevelY, -60);   // off the island: the generator's open sea
        return land;
    }

    /// Random points in `c`, plus the planes local x = 0 (chunk border) and z = 8 (brick border).
    private static void AddChunkPoints(List<int3> pts, int3 c, ref Unity.Mathematics.Random rng, int random, bool planes)
    {
        int3 o = c << 7;
        for (int i = 0; i < random; i++) pts.Add(o + rng.NextInt3(new int3(0), new int3(128)));
        if (!planes) return;
        for (int a = 0; a < 128; a++)
        for (int b = 0; b < 128; b++)
        {
            pts.Add(o + new int3(0, a, b));
            pts.Add(o + new int3(b, a, 8));
        }
        // the 12 edges (every corner included)
        for (int t = 0; t < 128; t++)
            for (int e = 0; e < 4; e++)
            {
                int p = (e & 1) * 127, q = (e >> 1) * 127;
                pts.Add(o + new int3(t, p, q)); pts.Add(o + new int3(p, t, q)); pts.Add(o + new int3(p, q, t));
            }
    }

    private static void Shuffle(List<int3> pts, ref Unity.Mathematics.Random rng)
    {
        for (int i = pts.Count - 1; i > 0; i--) { int j = rng.NextInt(0, i + 1); (pts[i], pts[j]) = (pts[j], pts[i]); }
    }

    /// A store with nothing resident (its pool is disposed with it).
    private sealed class Empty : System.IDisposable
    {
        private readonly BrickDataPool _pool = new BrickDataPool(16);
        public readonly ChunkStore store;
        public Empty() { store = new ChunkStore(_pool, new ChunkHandleAllocator(1)); }
        public void Dispose() => _pool.Dispose();
    }

    private static byte[] Query(IFarQuery q, List<int3> pts)
    {
        var p = new NativeArray<int3>(pts.ToArray(), Allocator.TempJob);
        var r = new NativeArray<byte>(pts.Count, Allocator.TempJob);
        try { q.GetVoxels(p, r); return r.ToArray(); }
        finally { p.Dispose(); r.Dispose(); }
    }

    // ==================================================================
    // Generator
    // ==================================================================

    [Test]
    public void Generator_EqualsGeneratedChunks_RandomPoints_Borders_NegativeCoords_AboveAndBelowTheWorld()
    {
        int3 s = SurfaceVoxel(out int3 sea);
        int3 cc = s >> 7;
        var chunks = new[]
        {
            cc, cc - new int3(0, 1, 0), cc - new int3(0, 6, 0),             // surface, below it, deep (caves, strata, ore)
            sea >> 7, new int3(-1, sea.y >> 7, -1),                          // the sea; negative x and z
            new int3(cc.x, -1, cc.z), new int3(-2, -1, 3),                    // below y 0
            new int3(cc.x, _src.TopLayerChunkY + 1, cc.z),                    // above every content layer
        };
        var rng = new Unity.Mathematics.Random(104u);
        var pts = new List<int3>();
        var loaded = new Dictionary<int3, Loaded>();
        var empty = new Empty();
        var fq = new FarQuery(empty.store, empty.store, _src, EditOpLog.InMemory(42u), null, 42u);
        try
        {
            foreach (var c in chunks)
            {
                if (loaded.ContainsKey(c)) continue;
                loaded[c] = new Loaded(c);
                AddChunkPoints(pts, c, ref rng, 4096, planes: true);
            }
            var mats = new HashSet<byte>();
            for (int z = 0; z < 128; z += 3) for (int y = 0; y < 128; y += 3) for (int x = 0; x < 128; x += 3)
                mats.Add(loaded[cc].At((cc << 7) + new int3(x, y, z)));
            Assert.GreaterOrEqual(mats.Count, 3, "the surface chunk is too plain to test anything");

            Shuffle(pts, ref rng);
            byte[] got = Query(fq, pts);
            int bad = 0; string first = null;
            var perChunk = new Dictionary<int3, int>();
            for (int i = 0; i < pts.Count; i++)
            {
                int3 c = pts[i] >> 7;
                byte want = loaded[c].At(pts[i]);
                if (got[i] != want) { bad++; first ??= $"{pts[i]} (chunk {c}): {got[i]} vs {want}"; }
                perChunk[c] = perChunk.TryGetValue(c, out int k) ? k + 1 : 1;
            }
            Debug.Log($"[104] generator: {pts.Count} points in {loaded.Count} chunks, {bad} differ; surface chunk {cc} has {mats.Count} materials");
            Assert.AreEqual(0, bad, $"{bad} of {pts.Count} points differ from Generate, first {first}");
            Assert.AreEqual((long)pts.Count, fq.PointsGenerated);

            ReportCost(fq, s);
        }
        finally { foreach (var l in loaded.Values) l.Dispose(); fq.Dispose(); empty.Dispose(); }
    }

    /// Cost per point (task 104's acceptance asks for a number; nothing asserted).
    private static void ReportCost(FarQuery fq, int3 s)
    {
        var rng = new Unity.Mathematics.Random(7u);
        const int N = 20000;
        var scattered = new List<int3>(N);
        for (int i = 0; i < N; i++)
        {
            int3 d = rng.NextInt3(new int3(-5000, -200, -5000), new int3(5000, 200, 5000));   // 1 km square, +-20 m of a surface
            scattered.Add(s + d);
        }
        var runs = new List<int3>(N);
        for (int i = 0; runs.Count < N; i++)
        {
            int3 b = s + rng.NextInt3(new int3(-5000, -200, -5000), new int3(5000, 200, 5000));
            for (int k = 0; k < 64; k++) runs.Add(b + new int3(0, k, 0));   // a 6.4 m vertical probe
        }
        double Time(List<int3> pts, bool par)
        {
            fq.Parallel = par;
            Query(fq, pts.GetRange(0, 512));   // warm (Burst is compiled synchronously on first use)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Query(fq, pts);
            return sw.Elapsed.TotalMilliseconds * 1000.0 / pts.Count;
        }
        double single = Time(scattered, false), singleRuns = Time(runs, false), par = Time(scattered, true);
        fq.Parallel = true;
        Debug.Log($"[104] cost per point: {single:F2} us single thread (every point a new column), " +
                  $"{singleRuns:F2} us single thread (64-voxel vertical runs), {par:F2} us wall parallel " +
                  $"({Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount} job workers), {N} points each, Editor");
    }

    // ==================================================================
    // Load order: ops <= S, D.1, ops > S
    // ==================================================================

    [Test]
    public void OpsAndDelta_FollowTheWorkersLoadOrder_S_CrcMismatch_Corrupt_Deleted()
    {
        int3 s = SurfaceVoxel(out _);
        int3 c = s >> 7, below = c - new int3(0, 1, 0);
        int3 centre = s;
        string dir = TempDir("loadorder");
        var log = EditOpLog.InMemory(42u);
        var empty = new Empty();
        var fq = new FarQuery(empty.store, empty.store, _src, log, dir, 42u);
        var poolL = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
        try
        {
            // op 1, then hand edits (later in time) made on the "live" chunk, then the save (S = 1).
            log.Append(new EditOp(EditOpKind.Sphere, centre, 20, Materials.Air));
            var allocL = new ChunkHandleAllocator(2);
            var live = new Chunk();
            _src.Generate(c, live, allocL, poolL, null);
            live.coord = c;
            var snap1 = log.Snapshot;
            EditOpApply.ApplyToChunk(live, c, allocL, poolL, snap1.Ops, 0, snap1.Count);
            var liveStore = new ChunkStore(poolL, allocL);
            liveStore.InsertChunk(live);
            int3 hv1 = centre + new int3(0, 0, -5);                      // inside op 1's crater
            liveStore.SetVoxel(centre, Materials.Stone);
            liveStore.SetVoxel(hv1, Materials.Stone);
            int3 brick0 = ((centre + new int3(24, 0, 0)) >> 3) << 3;     // a whole brick -> a UNIFORM record
            for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                liveStore.SetVoxel(brick0 + new int3(x, y, z), Materials.Snow);

            byte[] SaveDelta(bool note)
            {
                var poolB = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
                try
                {
                    var allocB = new ChunkHandleAllocator(2);
                    var baseline = new Chunk();
                    _src.Generate(c, baseline, allocB, poolB, null);
                    var sn = log.Snapshot;
                    EditOpApply.ApplyToChunk(baseline, c, allocB, poolB, sn.Ops, 0, sn.Count);
                    byte[] b = DeltaCodec.Encode(c, 42u, liveStore.GetChunk(c), poolL, baseline, poolB);
                    Assert.IsNotNull(b, "the hand edits made no delta");
                    if (note) log.NoteDeltaAboutToBeWritten(c, b);
                    DeltaCodec.WriteAtomic(DeltaCodec.PathFor(dir, c), b);
                    fq.ForgetDelta(c);
                    return b;
                }
                finally { poolB.Dispose(); }
            }
            byte[] d1 = SaveDelta(note: true);

            // ops after the save: one over the hand edit, one across the seam into the chunk below.
            log.Append(new EditOp(EditOpKind.Box, centre, 2, Materials.Sand));
            log.Append(new EditOp(EditOpKind.Box, new int3(centre.x - 30, (c.y << 7) + 2, centre.z + 10), 12, Materials.Air));

            var rng = new Unity.Mathematics.Random(81u);
            var pts = new List<int3>();
            AddChunkPoints(pts, c, ref rng, 6000, planes: false);
            AddChunkPoints(pts, below, ref rng, 3000, planes: false);
            for (int z = -22; z <= 22; z += 2) for (int y = -22; y <= 22; y += 2) for (int x = -22; x <= 22; x += 2)
                pts.Add(centre + new int3(x, y, z));
            for (int z = -1; z <= 8; z++) for (int y = -1; y <= 8; y++) for (int x = -1; x <= 8; x++)
                pts.Add(brick0 + new int3(x, y, z));
            for (int z = -13; z <= 13; z++) for (int y = -13; y <= 13; y++)
                pts.Add(new int3(centre.x - 30, (c.y << 7) + 2 + y, centre.z + 10 + z));
            pts.Add(hv1);
            Shuffle(pts, ref rng);

            void Check(string label, bool expectDelta)
            {
                byte[] got = Query(fq, pts);
                using var lc = new Loaded(c, log.Snapshot, dir);
                using var lb = new Loaded(below, log.Snapshot, dir);
                Assert.AreEqual(expectDelta, lc.deltaApplied, $"{label}: the worker's decode outcome");
                int bad = 0; string first = null;
                for (int i = 0; i < pts.Count; i++)
                {
                    int3 pc = pts[i] >> 7;
                    byte want = math.all(pc == c) ? lc.At(pts[i]) : math.all(pc == below) ? lb.At(pts[i]) : (byte)255;
                    Assert.AreNotEqual((byte)255, want, $"point {pts[i]} outside the two chunks");
                    if (got[i] != want) { bad++; first ??= $"{pts[i]}: {got[i]} vs {want}"; }
                }
                Debug.Log($"[104] {label}: {pts.Count} points, {bad} differ (delta pts {fq.PointsDelta}, parsed {fq.DeltaFilesParsed})");
                Assert.AreEqual(0, bad, $"{label}: {bad} points differ from the worker's load, first {first}");
            }

            // (a) delta with S = 1: op 1 under the hand edits, op 2 over them.
            Assert.AreEqual(1, log.Snapshot.OpsBeforeDelta(c, EditOpLog.TrailerCrc(d1)));
            long deltaBefore = fq.PointsDelta;
            Check("S = 1", true);
            Assert.Greater(fq.PointsDelta, deltaBefore, "no point was answered from the delta");
            byte[] one = Query(fq, new List<int3> { centre, hv1, brick0 + 3 });
            Assert.AreEqual(Materials.Sand, one[0], "op 2 (after the save) lands over the hand edit");
            Assert.AreEqual(Materials.Stone, one[1], "the hand edit made after op 1 survives it");
            Assert.AreEqual(Materials.Snow, one[2], "the uniform D.1 record");

            // (b) side-table CRC mismatch (the delta on disk is not the one noted): all ops, then D.1.
            liveStore.SetVoxel(centre + new int3(0, 0, 20), Materials.CooledStone);
            byte[] d2 = SaveDelta(note: false);
            Assert.AreEqual(log.Snapshot.Count, log.Snapshot.OpsBeforeDelta(c, EditOpLog.TrailerCrc(d2)));
            Check("CRC mismatch", true);
            Assert.AreEqual(Materials.Stone, Query(fq, new List<int3> { centre })[0], "fallback: the D.1 brick wins over op 2");

            // (c) a corrupt delta is no delta: generation + every op.
            byte[] bad2 = (byte[])d2.Clone();
            bad2[DeltaCodec.HEADER_BYTES + 5] ^= 0x5A;
            File.WriteAllBytes(DeltaCodec.PathFor(dir, c), bad2);
            fq.ForgetDelta(c);
            Check("corrupt delta", false);

            // (d) deleted.
            File.Delete(DeltaCodec.PathFor(dir, c));
            fq.ForgetDelta(c);
            Check("no delta", false);
        }
        finally { fq.Dispose(); empty.Dispose(); poolL.Dispose(); Directory.Delete(dir, true); }
    }

    // ==================================================================
    // Resident
    // ==================================================================

    [Test]
    public void Resident_AnswersFromTheStore_NeighbourFromTheGenerator()
    {
        int3 s = SurfaceVoxel(out _);
        int3 c = s >> 7, east = c + new int3(1, 0, 0);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
        var alloc = new ChunkHandleAllocator(2);
        try
        {
            var near = new Chunk();
            _src.Generate(c, near, alloc, pool, null);
            near.coord = c;
            var store = new ChunkStore(pool, alloc);
            store.InsertChunk(near);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var log = EditOpLog.InMemory(42u);
            var api = new EditOps(log, edits, store);
            // An op across the east border: applied near to c now, on generation to east.
            int3 opC = (east << 7) + new int3(0, s.y & 127, 64);
            api.Record(new EditOp(EditOpKind.Sphere, opC, 14, Materials.Glass));   // a fill: writes whatever is there
            Assert.Greater(api.NearVoxelsWritten, 0);
            // A hand edit the generator does not have.
            byte gen;
            using (var empty = new Empty())
            using (var g = new FarQuery(empty.store, empty.store, _src, EditOpLog.InMemory(42u), null, 42u))
                gen = Query(g, new List<int3> { s })[0];
            byte hand = gen == Materials.CooledStone ? Materials.Stone : Materials.CooledStone;
            store.SetVoxel(s, hand);

            var fq = new FarQuery(store, store, _src, log, null, 42u);
            try
            {
                var rng = new Unity.Mathematics.Random(5u);
                var pts = new List<int3> { s };
                AddChunkPoints(pts, c, ref rng, 3000, planes: false);
                AddChunkPoints(pts, east, ref rng, 3000, planes: false);
                for (int z = -15; z <= 15; z++) for (int y = -15; y <= 15; y++) for (int x = -15; x <= 15; x++)
                    pts.Add(opC + new int3(x, y, z));
                byte[] got = Query(fq, pts);
                Assert.AreEqual(hand, got[0], "the resident chunk's hand edit");
                using var le = new Loaded(east, log.Snapshot);
                int bad = 0; string first = null;
                for (int i = 0; i < pts.Count; i++)
                {
                    byte want = math.all(pts[i] >> 7 == c) ? store.GetVoxel(pts[i]) : le.At(pts[i]);
                    if (got[i] != want) { bad++; first ??= $"{pts[i]}: {got[i]} vs {want}"; }
                }
                Debug.Log($"[104] resident: {pts.Count} points ({fq.PointsResident} resident, {fq.PointsGenerated} generated), {bad} differ");
                Assert.AreEqual(0, bad, $"{bad} points differ, first {first}");
                Assert.Greater(fq.PointsResident, 0); Assert.Greater(fq.PointsGenerated, 0);
            }
            finally { fq.Dispose(); }
        }
        finally { pool.Dispose(); }
    }

    // ==================================================================
    // End to end: a tg2 StreamManager
    // ==================================================================

    [Test, Category("Slow"), Timeout(600000)]
    public void StreamManager_FarAnswers_EqualTheChunkOnceStreamedIn()
    {
        string dir = TempDir("streamed");
        var meta = new WorldMetaData { seed = 42, sizeClass = TerrainGenChunkSource.SIZE_CLASS };
        var pool = new BrickDataPool(400000, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        var store = new ChunkStore(pool, alloc);
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, 8, EngineConfig.WINDOW_CHUNKS_XZ);
        var clip = new TerrainClipmap(mirror, pool.Capacity);
        var cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
        bool aware = StreamManager.TerrainAwareBand;
        StreamManager.TerrainAwareBand = false;
        StreamManager sm = null;
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, dir, 2);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, clip);
            var ops = new EditOps(sm.Ops, edits, store);
            var fq = sm.FarQuery;
            Assert.IsNotNull(fq, "a tg2 world has a far query");

            int3 s = SurfaceVoxel(out _);
            var cam = new Vector3(s.x * 0.1f, (s.y + 20) * 0.1f, s.z * 0.1f);
            var away = cam + new Vector3(300f, 0f, 0f);
            int3 c = s >> 7;
            bool primed = false;
            void Go(Vector3 p)
            {
                if (!primed) { sm.PrimeWindow(p); primed = true; }
                var clk = System.Diagnostics.Stopwatch.StartNew();
                do { sm.Update(p); sm.WaitForIdle(); }
                while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0 || sm.EvictionDeferred) && clk.Elapsed.TotalSeconds < 120);
                for (int i = 0; i < 2; i++) { sm.Update(p); sm.WaitForIdle(); }
            }

            var rng = new Unity.Mathematics.Random(93u);
            var pts = new List<int3>();
            AddChunkPoints(pts, c, ref rng, 6000, planes: false);
            for (int z = -12; z <= 12; z++) for (int y = -12; y <= 12; y++) for (int x = -12; x <= 12; x++)
                pts.Add(s + new int3(x, y, z));

            // Away first: c is generated on query and its "no delta" is cached.
            Go(away);
            Assert.IsFalse(store.IsResident(c));
            Query(fq, pts);
            Assert.Greater(fq.DeltaCacheCount, 0);

            // Near: hand edits and an op, then leave -> the delta is saved (SaveDelta forgets the cache entry).
            Go(cam);
            Assert.IsTrue(store.IsResident(c), "the surface chunk did not stream in");
            ops.Record(new EditOp(EditOpKind.Sphere, s, 6, Materials.Air));
            edits.SetVoxel(s, Materials.Stone);
            edits.SetVoxel(s + new int3(3, 0, 0), Materials.Glass);
            byte[] nearAnswers = Query(fq, pts);
            Go(away);
            Assert.IsFalse(store.IsResident(c));
            Assert.IsTrue(File.Exists(DeltaCodec.PathFor(dir, c)), "no delta saved on eviction");
            ops.Record(new EditOp(EditOpKind.Box, s + new int3(0, 1, 0), 2, Materials.Sand));   // after the save
            byte[] far = Query(fq, pts);

            // Back: the chunk as it streams in must be what the far query said.
            Go(cam);
            Assert.IsTrue(store.IsResident(c));
            int bad = 0, compared = 0; string first = null;
            for (int i = 0; i < pts.Count; i++)
            {
                if (!store.IsResident(pts[i] >> 7)) continue;   // a neighbour of c outside the band: nothing to compare
                compared++;
                byte want = store.GetVoxel(pts[i]);
                if (far[i] != want) { bad++; first ??= $"{pts[i]}: far {far[i]} vs streamed {want}"; }
            }
            Assert.GreaterOrEqual(compared, 6000, "c's own points must all be compared");
            Debug.Log($"[104] streamed: {compared} of {pts.Count} points compared, {bad} differ; delta points {fq.PointsDelta}, files parsed {fq.DeltaFilesParsed}");
            Assert.AreEqual(0, bad, $"far answers differ from the streamed chunk in {bad} points, first {first}");
            Assert.Greater(fq.PointsDelta, 0, "the saved delta was never read (stale cache?)");
            Assert.AreEqual(Materials.Stone, nearAnswers[pts.IndexOf(s)]);
        }
        finally
        {
            StreamManager.TerrainAwareBand = aware;
            sm?.Dispose(); cascades.Dispose(); clip.Dispose(); pool.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
