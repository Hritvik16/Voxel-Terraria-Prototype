// Assets/CoreEngine/Tests/EditOpTests.cs
//
// Engine step 13a (task 81), the plan's E5 tests, part a (Amendment 8.14
// rows 8-10, D.4 `ops.log`):
//   Codec        round trip, deterministic bytes, `.tmp` + rename, a corrupt
//                trailer / body / truncation / wrong seed REJECTED (moved
//                aside, log starts empty), reserved prefab kind refused,
//                main-thread only.
//   Near == far  an op recorded through IEditOps on a RESIDENT tg2 chunk
//                (EditService path) == the same op applied on generation
//                (EditOpApply), byte for byte; and the same end to end through
//                StreamManager (op made near vs op made far, then streamed in).
//   Precedence   ops <= S, then D.1, then ops > S (row 10, Q3 (a)), end to end
//                through StreamManager: a hand edit made AFTER op 1 survives
//                op 1; op 2 recorded after the save lands over the hand edit;
//                a side-table CRC mismatch falls back to "all ops, then D.1";
//                the log reloads from disk with the save.
//   Old save     no ops recorded -> no ops.log written, reload byte-identical.
//   Shells       a shell chunk with ops == the shell chunk without them, with
//                every voxel whose row-13 sample an op covers set to the op's
//                material (shells 1-7); shell 1 also == the edited 0.1 m world
//                at its samples except the top voxels ShellChunkTests already
//                excuses (the same rule).
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class EditOpTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }
    [OneTimeTearDown] public void TearDown() { _src?.Dispose(); _src = null; }
    [TearDown] public void FillOn() { if (_src != null) _src.ShellFill = true; }

    private static string TempDir(string name)
    {
        string d = Path.Combine(Path.GetTempPath(), "editop_test_" + name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    private static byte Read(Chunk c, BrickDataPool pool, int x, int y, int z)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        int idx = (int)(d & 0x3FFFFFFFu);
        return pool.RawData[idx * 512 + (((z & 7) << 6) | ((y & 7) << 3) | (x & 7))];
    }

    private static byte[] Content(Chunk c, BrickDataPool pool)
    {
        var a = new byte[128 * 128 * 128];
        for (int z = 0; z < 128; z++)
        for (int y = 0; y < 128; y++)
        for (int x = 0; x < 128; x++)
            a[(z << 14) | (y << 7) | x] = Read(c, pool, x, y, z);
        return a;
    }

    private static int Diff(byte[] a, byte[] b, out string first)
    {
        int n = 0; first = null;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) { if (first == null) first = $"local ({i & 127},{(i >> 7) & 127},{i >> 14}) {a[i]} vs {b[i]}"; n++; }
        return n;
    }

    /// A land column (fine voxel x, z) with its ground height (as ShellChunkTests picks one).
    private static int3 SurfaceVoxel(TerrainGenChunkSource src)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 400; i++)
        {
            float2 p = new float2(c, c) + new float2((i % 20) - 10, (i / 20) - 10) * 1500f;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 300 || col.water > col.height) continue;
            return new int3((int)p.x, col.height, (int)p.y);
        }
        Assert.Fail("no land site found");
        return default;
    }

    // ==================================================================
    // Codec
    // ==================================================================

    [Test]
    public void OpsLog_RoundTrip_AtomicWrite_Deterministic()
    {
        string dir = TempDir("roundtrip");
        try
        {
            var log = EditOpLog.Open(dir, 42u);
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(File.Exists(Path.Combine(dir, EditOpLog.FILE_NAME)), "an empty log must not write a file");
            Assert.AreEqual(1u, log.Append(new EditOp(EditOpKind.Sphere, new int3(-5, 300, 70000), 200, Materials.Air)));
            Assert.AreEqual(2u, log.Append(new EditOp(EditOpKind.Box, new int3(1, 2, 3), 7, Materials.Stone)));
            var delta = new byte[] { 1, 2, 3, 4, 0xEF, 0xBE, 0xAD, 0xDE };
            log.NoteDeltaAboutToBeWritten(new int3(9, 0, -4), delta);

            string path = Path.Combine(dir, EditOpLog.FILE_NAME);
            Assert.IsTrue(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".tmp"), ".tmp must be renamed away");
            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreEqual(EditOpLog.HEADER_BYTES + 2 * EditOpLog.OP_BYTES + 4 + EditOpLog.SIDE_BYTES + EditOpLog.TRAILER_BYTES, bytes.Length);
            CollectionAssert.AreEqual(log.Encode(), bytes, "file != Encode()");

            var back = EditOpLog.Open(dir, 42u);
            Assert.AreEqual(OpLogRejectReason.None, back.LoadReject);
            Assert.AreEqual(2, back.Count);
            var s = back.Snapshot;
            Assert.AreEqual(EditOpKind.Sphere, s.Ops[0].kind); Assert.AreEqual(new int3(-5, 300, 70000), s.Ops[0].centreVoxel);
            Assert.AreEqual((ushort)200, s.Ops[0].size); Assert.AreEqual(Materials.Air, s.Ops[0].material); Assert.AreEqual(1u, s.Ops[0].seq);
            Assert.AreEqual(EditOpKind.Box, s.Ops[1].kind); Assert.AreEqual(Materials.Stone, s.Ops[1].material); Assert.AreEqual(2u, s.Ops[1].seq);
            Assert.IsTrue(s.TryGetSide(new int3(9, 0, -4), out var e));
            Assert.AreEqual(2u, e.seqAtSave); Assert.AreEqual(0xDEADBEEFu, e.deltaCrc);
            CollectionAssert.AreEqual(bytes, back.Encode(), "re-encode of the loaded log differs");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void OpsLog_Corrupt_IsRejected_AndMovedAside()
    {
        string dir = TempDir("corrupt");
        try
        {
            var log = EditOpLog.Open(dir, 42u);
            log.Append(new EditOp(EditOpKind.Sphere, new int3(10, 20, 30), 5, Materials.Air));
            string path = Path.Combine(dir, EditOpLog.FILE_NAME);
            byte[] good = File.ReadAllBytes(path);

            void Check(byte[] bytes, uint seed, string what)
            {
                File.WriteAllBytes(path, bytes);
                var l = EditOpLog.Open(dir, seed);
                Assert.AreNotEqual(OpLogRejectReason.None, l.LoadReject, what + ": accepted");
                Assert.AreEqual(0, l.Count, what + ": ops loaded from a rejected file");
                Assert.IsTrue(File.Exists(path + ".rejected"), what + ": not moved aside");
                Assert.IsFalse(File.Exists(path), what + ": rejected file left in place to be overwritten");
                Debug.Log($"[81] {what}: {l.LoadReject}");
            }

            var t = (byte[])good.Clone(); t[t.Length - 1] ^= 0x5A; Check(t, 42u, "corrupt trailer");
            t = (byte[])good.Clone(); t[EditOpLog.HEADER_BYTES + 6] ^= 1; Check(t, 42u, "corrupt body");
            t = new byte[good.Length - 7]; System.Array.Copy(good, t, t.Length); Check(t, 42u, "truncated");
            Check(good, 7u, "wrong seed");
            Assert.AreEqual(OpLogRejectReason.SeedMismatch, EditOpLog.TryDecode(good, 7u, out _, out _, out var r) ? OpLogRejectReason.None : r);
            Assert.AreEqual(OpLogRejectReason.CrcMismatch, EditOpLog.TryDecode(new byte[good.Length], 42u, out _, out _, out r) ? OpLogRejectReason.None : r);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void OpsLog_ReservedPrefabKind_And_WorkerThreads_AreRefused()
    {
        var log = EditOpLog.InMemory(42u);
        Assert.Throws<System.ArgumentException>(() => log.Append(new EditOp(EditOpKind.PrefabReserved, int3.zero, 1, 1)));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => log.Append(new EditOp(EditOpKind.Sphere, int3.zero, EditOpLog.MAX_SIZE + 1, 0)));
        System.Exception caught = null;
        var th = new System.Threading.Thread(() =>
        {
            try { log.Append(new EditOp(EditOpKind.Sphere, int3.zero, 1, 0)); } catch (System.Exception e) { caught = e; }
        });
        th.Start(); th.Join();
        Assert.IsInstanceOf<System.InvalidOperationException>(caught, "Append from a worker thread must throw");
        Assert.AreEqual(0, log.Count);
    }

    [Test]
    public void SideTable_S_Rules()
    {
        var log = EditOpLog.InMemory(42u);
        var d1 = new byte[] { 0, 0, 0, 0, 1, 0, 0, 0 };
        var d2 = new byte[] { 0, 0, 0, 0, 2, 0, 0, 0 };
        var c = new int3(3, 0, 3);
        log.NoteDeltaAboutToBeWritten(c, d1);                          // no ops: nothing recorded (S = 0 implied)
        Assert.AreEqual(0, log.Snapshot.SideCount);
        log.Append(new EditOp(EditOpKind.Sphere, int3.zero, 1, 0));
        Assert.AreEqual(0, log.Snapshot.OpsBeforeDelta(c, 1u), "a delta older than every op: S = 0");
        log.NoteDeltaAboutToBeWritten(c, d1);
        log.Append(new EditOp(EditOpKind.Sphere, int3.zero, 1, 0));
        var s = log.Snapshot;
        Assert.AreEqual(1, s.OpsBeforeDelta(c, 1u), "matching CRC: S = op count at save");
        Assert.AreEqual(2, s.OpsBeforeDelta(c, EditOpLog.TrailerCrc(d2)), "CRC mismatch: all ops, then D.1");
        log.NoteDeltaDeleted(c);
        Assert.AreEqual(0, log.Snapshot.SideCount);
    }

    // ==================================================================
    // Near == far on a tg2 chunk (unit level)
    // ==================================================================

    private static EditOp[] SampleOps(int3 s)
    {
        return new[]
        {
            new EditOp(EditOpKind.Sphere, s + new int3(3, 0, -5), 30, Materials.Air),         // a crater: whole bricks + partial ones
            new EditOp(EditOpKind.Box, s + new int3(-10, -4, 8), 5, Materials.Stone),          // a block in it
            new EditOp(EditOpKind.Sphere, s + new int3(12, -12, 0), 6, Materials.Water),      // a pocket of water
            new EditOp(EditOpKind.Box, s + new int3(0, -40, 0), 9, Materials.Air),             // underground
        };
    }

    [Test]
    public void ApplyOnGenerate_Equals_NearEdit_OnAResidentChunk()
    {
        int3 s = SurfaceVoxel(_src);
        int3 cc = s >> 7;
        var ops = new List<EditOp>(SampleOps((cc << 7) + new int3(64, s.y & 127, 64)));   // the site inside the chunk on x/z
        ops.Add(new EditOp(EditOpKind.Box, (cc << 7) + new int3(40, -6, 70), 12, Materials.Air));        // across the seam with the chunk below
        foreach (var coord in new[] { cc, cc - new int3(0, 1, 0) })
        {
            var poolN = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
            var poolF = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
            try
            {
                // Near: generated, made resident, then edited through IEditOps -> EditService.
                var allocN = new ChunkHandleAllocator(2);
                var near = new Chunk();
                _src.Generate(coord, near, allocN, poolN, null);
                near.coord = coord;
                var store = new ChunkStore(poolN, allocN);
                store.InsertChunk(near);
                var edits = new EditService();
                edits.AttachWorld(store, store, store, null);
                var api = new EditOps(EditOpLog.InMemory(42u), edits, store);
                foreach (var op in ops) api.Record(op);

                // Far: generated with the log's ops applied (the worker's path).
                var allocF = new ChunkHandleAllocator(2);
                var far = new Chunk();
                _src.Generate(coord, far, allocF, poolF, null);
                var snap = api.Log.Snapshot;
                int w = EditOpApply.ApplyToChunk(far, coord, allocF, poolF, snap.Ops, 0, snap.Count);

                int bad = Diff(Content(store.GetChunk(coord), poolN), Content(far, poolF), out string first);
                Debug.Log($"[81] chunk {coord}: near wrote {api.NearVoxelsWritten} voxels, far {w}; {bad} differ");
                Assert.Greater(api.NearVoxelsWritten, 0, $"{coord}: the ops changed nothing (bad site)");
                Assert.AreEqual(0, bad, $"{coord}: near and far differ in {bad} voxels, first {first}");
            }
            finally { poolN.Dispose(); poolF.Dispose(); }
        }
    }

    // ==================================================================
    // End to end through StreamManager (flat sizeClass 0 world, fast)
    // ==================================================================

    private sealed class World : System.IDisposable
    {
        public readonly BrickDataPool pool;
        public readonly ChunkStore store;
        public readonly StreamManager sm;
        public readonly EditService edits;
        public readonly EditOps ops;
        private readonly TerrainClipmap _clip;
        private readonly LODCascadeManager _cascades;
        private bool _primed;

        public World(string dir)
        {
            var meta = new WorldMetaData { seed = 42, sizeClass = 0 };
            pool = new BrickDataPool(400000, rangeAware: true);
            var alloc = new ChunkHandleAllocator(1024);
            store = new ChunkStore(pool, alloc);
            var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
            _clip = new TerrainClipmap(mirror, pool.Capacity);
            _cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
            sm = new StreamManager(store, pool, alloc, _clip, _cascades, meta, dir, 2);
            edits = new EditService();
            edits.AttachWorld(store, store, store, _clip);
            ops = new EditOps(sm.Ops, edits, store);
        }

        public void Go(Vector3 cam)
        {
            if (!_primed) { sm.PrimeWindow(cam); _primed = true; }   // later moves stream (a teleport)
            var clk = System.Diagnostics.Stopwatch.StartNew();
            do { sm.Update(cam); sm.WaitForIdle(); }
            while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0 || sm.EvictionDeferred) && clk.Elapsed.TotalSeconds < 120);
            for (int i = 0; i < 2; i++) { sm.Update(cam); sm.WaitForIdle(); }
            Assert.AreEqual(0, sm.LoadDeficit(), "window not filled");
            Assert.IsFalse(sm.EvictionDeferred, "evictions still deferred");
        }

        public byte[] Content(int3 c)
        {
            var ch = store.GetChunk(c);
            Assert.IsNotNull(ch, $"{c} not resident");
            return EditOpTests.Content(ch, pool);
        }

        public void Dispose() { sm.Dispose(); _cascades.Dispose(); _clip.Dispose(); pool.Dispose(); }
    }

    private static readonly Vector3 CAM = new Vector3(64f, 20f, 64f);
    private static readonly Vector3 AWAY = CAM + new Vector3(2000f, 0f, 1500f);

    /// A resident non-uniform chunk and a surface voxel at its local (64, y, 64)
    /// with 12 voxels of room above and below.
    private static void Site(World w, out int3 c, out int3 centre)
    {
        int3 camChunk = new int3((int)(CAM.x * 10) >> 7, 0, (int)(CAM.z * 10) >> 7);
        var list = new List<Chunk>(w.store.ResidentChunks());
        list.Sort((a, b) => math.lengthsq(a.coord - camChunk).CompareTo(math.lengthsq(b.coord - camChunk)));
        foreach (var ch in list)
        {
            if (ch.isUniform) continue;
            int3 v0 = ch.coord << 7;
            for (int y = 115; y >= 12; y--)
                if (w.store.GetVoxel(v0 + new int3(64, y, 64)) != Materials.Air &&
                    w.store.GetVoxel(v0 + new int3(64, y + 1, 64)) == Materials.Air)
                { c = ch.coord; centre = v0 + new int3(64, y, 64); return; }
        }
        Assert.Fail("no surface site in the resident window");
        c = default; centre = default;
    }

    private static byte[] WithOps(byte[] content, int3 c, params EditOp[] ops)
    {
        var r = (byte[])content.Clone();
        int3 v0 = c << 7;
        for (int i = 0; i < r.Length; i++)
        {
            int3 v = v0 + new int3(i & 127, (i >> 7) & 127, i >> 14);
            foreach (var op in ops) if (EditOpApply.Covers(op, v)) r[i] = op.material;
        }
        return r;
    }

    [Test]
    public void StreamManager_FarOp_Equals_NearOp()
    {
        string dirN = TempDir("near"), dirF = TempDir("far");
        try
        {
            int3 c, centre; byte[] near;
            EditOp op;
            using (var w = new World(dirN))
            {
                w.Go(CAM);
                Site(w, out c, out centre);
                op = new EditOp(EditOpKind.Sphere, centre, 10, Materials.Air);
                w.ops.Record(op);
                Assert.Greater(w.ops.NearVoxelsWritten, 0);
                near = w.Content(c);
            }
            using (var w = new World(dirF))
            {
                w.Go(AWAY);
                Assert.IsFalse(w.store.IsResident(c));
                w.ops.Record(op);
                Assert.AreEqual(0, w.ops.NearVoxelsWritten, "nothing resident there: the op is only logged");
                w.Go(CAM);
                int bad = Diff(near, w.Content(c), out string first);
                Assert.AreEqual(0, bad, $"far op streamed in differs from the near op in {bad} voxels, first {first}");
            }
        }
        finally { Directory.Delete(dirN, true); Directory.Delete(dirF, true); }
    }

    [Test]
    public void StreamManager_Precedence_S_And_CrcFallback_And_Reload()
    {
        string dir = TempDir("precedence");
        try
        {
            int3 c, centre; byte[] expectF;
            using (var w = new World(dir))
            {
                w.Go(CAM);
                Site(w, out c, out centre);
                string deltaPath = DeltaCodec.PathFor(dir, c);
                string logPath = Path.Combine(dir, EditOpLog.FILE_NAME);

                // op 1 (near), then a hand edit INSIDE its crater (later in time).
                var op1 = new EditOp(EditOpKind.Sphere, centre, 8, Materials.Air);
                w.ops.Record(op1);
                Assert.AreEqual(Materials.Air, w.store.GetVoxel(centre));
                w.edits.SetVoxel(centre, Materials.Stone);
                int3 hv1 = centre + new int3(0, 0, -5);                  // inside op 1, outside op 2/3
                Assert.AreEqual(Materials.Air, w.store.GetVoxel(hv1));
                w.edits.SetVoxel(hv1, Materials.Stone);
                byte[] L1 = w.Content(c);

                // Leave: the delta is saved with S = 1.
                w.Go(AWAY);
                Assert.IsTrue(File.Exists(deltaPath), "no delta saved");
                byte[] oldDelta = File.ReadAllBytes(deltaPath);
                Assert.IsTrue(EditOpLog.TryDecode(File.ReadAllBytes(logPath), 42u, out _, out var side, out _));
                Assert.AreEqual(1u, side[c].seqAtSave, "S != op count at save");
                Assert.AreEqual(EditOpLog.TrailerCrc(oldDelta), side[c].deltaCrc);

                // op 2, far, over the hand edit: later than the save, so it lands on top.
                var op2 = new EditOp(EditOpKind.Box, centre, 2, Materials.Sand);
                w.ops.Record(op2);
                w.Go(CAM);
                byte[] R = w.Content(c);
                Assert.AreEqual(Materials.Sand, R[Local(c, centre)], "op 2 (after the save) must land over the hand edit");
                int bad = Diff(WithOps(L1, c, op2), R, out string first);
                Assert.AreEqual(0, bad, $"ops<=S, D.1, ops>S: {bad} voxels differ, first {first}");
                Assert.AreEqual(Materials.Stone, R[Local(c, hv1)], "the hand edit made after op 1 must survive it (op 1 <= S runs BEFORE D.1)");

                // Crash between the two writes: edit, leave (log gets S = 2 and the new CRC), then
                // put the OLD delta back. op 3 after that save. Fallback: all ops, then the old D.1.
                int3 hv = centre + new int3(0, 0, 20);
                w.edits.SetVoxel(hv, w.store.GetVoxel(hv) == Materials.Stone ? Materials.Deepstone : Materials.Stone);
                w.Go(AWAY);
                Assert.IsTrue(EditOpLog.TryDecode(File.ReadAllBytes(logPath), 42u, out _, out side, out _));
                Assert.AreEqual(2u, side[c].seqAtSave);
                var op3 = new EditOp(EditOpKind.Box, centre, 1, Materials.Snow);
                w.ops.Record(op3);
                File.WriteAllBytes(deltaPath, oldDelta);
                w.Go(CAM);
                byte[] F = w.Content(c);

                var recorded = DeltaBricks(oldDelta);
                Assert.IsTrue(recorded.Contains(BrickOf(c, centre)), "the hand-edit brick must be in the old delta");
                byte[] rest = WithOps(L1, c, op2, op3);
                expectF = new byte[L1.Length];
                for (int i = 0; i < L1.Length; i++)
                {
                    int bi = ((i >> 17) << 8) | (((i >> 10) & 15) << 4) | ((i & 127) >> 3);
                    expectF[i] = recorded.Contains(bi) ? L1[i] : rest[i];
                }
                bad = Diff(expectF, F, out first);
                Assert.AreEqual(0, bad, $"CRC-mismatch fallback (all ops, then D.1): {bad} voxels differ, first {first}");
                Assert.AreEqual(Materials.Stone, F[Local(c, centre)], "fallback: the old D.1 brick wins over op 2/3");
                Debug.Log($"[81] precedence: old delta {oldDelta.Length} B, {recorded.Count} bricks; S checks pass; ops {w.sm.Ops.Count}");
            }

            // The save reloads: ops.log from disk, the same voxels.
            using (var w = new World(dir))
            {
                Assert.AreEqual(OpLogRejectReason.None, w.sm.Ops.LoadReject);
                Assert.AreEqual(3, w.sm.Ops.Count, "ops.log did not reload");
                w.Go(CAM);
                int bad = Diff(expectF, w.Content(c), out string first);
                Assert.AreEqual(0, bad, $"after reopening the save: {bad} voxels differ, first {first}");
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void StreamManager_OldSave_NoOps_LoadsUnchanged()
    {
        string dir = TempDir("oldsave");
        try
        {
            using (var w = new World(dir))
            {
                w.Go(CAM);
                Site(w, out int3 c, out int3 centre);
                w.edits.SetVoxel(centre + new int3(0, 1, 0), Materials.Stone);
                w.edits.SetVoxel(centre, Materials.Air);
                byte[] before = w.Content(c);
                w.Go(AWAY);
                Assert.IsTrue(File.Exists(DeltaCodec.PathFor(dir, c)));
                Assert.IsFalse(File.Exists(Path.Combine(dir, EditOpLog.FILE_NAME)), "no ops: no ops.log may be written");
                w.Go(CAM);
                int bad = Diff(before, w.Content(c), out string first);
                Assert.AreEqual(0, bad, $"old save reload differs in {bad} voxels, first {first}");
                Assert.AreEqual(0, w.sm.Ops.Count);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    private static int Local(int3 c, int3 v) { int3 l = v - (c << 7); return (l.z << 14) | (l.y << 7) | l.x; }
    private static int BrickOf(int3 c, int3 v) { int3 l = (v - (c << 7)) >> 3; return (l.z << 8) | (l.y << 4) | l.x; }

    /// Brick indices recorded in a D.1 file (Appendix D.1 layout).
    private static HashSet<int> DeltaBricks(byte[] d)
    {
        var set = new HashSet<int>();
        int p = DeltaCodec.HEADER_BYTES, end = d.Length - DeltaCodec.TRAILER_BYTES;
        while (p < end)
        {
            int bi = d[p] | (d[p + 1] << 8);
            byte kind = d[p + 2];
            p += 3 + (kind == 0 ? 1 : 512);
            set.Add(bi);
        }
        return set;
    }

    // ==================================================================
    // Shell chunks
    // ==================================================================

    [Test]
    public void ShellChunk_WithOps_IsTheCoarseSampleOfTheEditedWorld_Shells1to7()
    {
        int3 s = SurfaceVoxel(_src);
        using (var plain = new TerrainGenChunkSource.ShellChunk())
        using (var edited = new TerrainGenChunkSource.ShellChunk())
        {
            for (int k = 1; k <= TerrainGenChunkSource.MAX_SHELL; k++)
            {
                int3 cs = s >> (7 + k);
                int r = math.max(40, 3 << k);
                var log = EditOpLog.InMemory(42u);
                log.Append(new EditOp(EditOpKind.Sphere, s, (ushort)r, Materials.Air));
                log.Append(new EditOp(EditOpKind.Box, s + new int3(0, -r / 2, 0), (ushort)(r / 3), Materials.Stone));
                log.Append(new EditOp(EditOpKind.Box, s + new int3(0, 4 * r, 0), (ushort)(r / 2), Materials.Snow));   // in the air
                var snap = log.Snapshot;

                _src.ShellFill = false;   // ops on the generator's voxels (task 83's fill runs after them: ShellFillTests)
                _src.GenerateShellChunk(k, cs, plain);
                Assert.AreEqual(snap.Count, _src.GenerateShellChunk(k, cs, edited, snap));
                _src.ShellFill = true;
                int half = (1 << (k - 1)) - 1;
                int3 o = (cs << (7 + k)) + half;
                int covered = 0, bad = 0; string first = null;
                for (int ly = 0; ly < 128; ly++)
                for (int lz = 0; lz < 128; lz++)
                for (int lx = 0; lx < 128; lx++)
                {
                    int3 p = o + (new int3(lx, ly, lz) << k);
                    byte want = plain.Get(lx, ly, lz);
                    bool cov = false;
                    foreach (var op in snap.Ops) if (EditOpApply.Covers(op, p)) { want = op.material; cov = true; }
                    if (cov) covered++;
                    byte got = edited.Get(lx, ly, lz);
                    if (got != want) { bad++; if (first == null) first = $"({lx},{ly},{lz}) want {want} got {got}"; }
                }
                int badBricks = 0;
                if (!edited.IsUniformAir)
                    for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                    {
                        int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                        byte f = edited.Get(bx << 3, by << 3, bz << 3); bool uni = true;
                        for (int v = 0; v < 512 && uni; v++)
                            if (edited.Get((bx << 3) + (v & 7), (by << 3) + ((v >> 3) & 7), (bz << 3) + (v >> 6)) != f) uni = false;
                        if (uni != edited.BrickUniform[bi] || (uni && edited.BrickMat[bi] != f)) badBricks++;
                    }
                Debug.Log($"[81] shell {k} chunk {cs}: {covered} coarse voxels under ops, {bad} wrong, {badBricks} brick flags wrong");
                Assert.Greater(covered, 0, $"shell {k}: the ops reach no coarse sample");
                Assert.AreEqual(0, bad, $"shell {k}: first {first}");
                Assert.AreEqual(0, badBricks, $"shell {k}: BrickUniform/BrickMat stale after the ops");
            }
        }
    }

    [Test]
    public void Shell1_WithOps_MatchesTheEdited01mWorld_AtItsSamples()
    {
        int3 s = SurfaceVoxel(_src);
        int3 cs = s >> 8;
        int3 fineOrigin = cs << 8;
        var ops = SampleOps(fineOrigin + new int3(128, s.y - fineOrigin.y, 128));   // across the fine chunks' seams
        var log = EditOpLog.InMemory(42u);
        foreach (var op in ops) log.Append(op);
        var snap = log.Snapshot;

        // The 2x2x2 fine chunks under the shell-1 chunk, generated with and without the ops.
        var fineEdited = new byte[256 * 256 * 256];
        var finePlain = new byte[256 * 256 * 256];
        for (int dz = 0; dz < 2; dz++)
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++)
        {
            int3 fc = (cs << 1) + new int3(dx, dy, dz);
            var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
            try
            {
                var alloc = new ChunkHandleAllocator(2);
                var ch = new Chunk();
                _src.Generate(fc, ch, alloc, pool, null);
                for (int z = 0; z < 128; z++) for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                    finePlain[(((dz << 7) + z) << 16) | (((dy << 7) + y) << 8) | ((dx << 7) + x)] = Read(ch, pool, x, y, z);
                EditOpApply.ApplyToChunk(ch, fc, alloc, pool, snap.Ops, 0, snap.Count);
                for (int z = 0; z < 128; z++) for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                    fineEdited[(((dz << 7) + z) << 16) | (((dy << 7) + y) << 8) | ((dx << 7) + x)] = Read(ch, pool, x, y, z);
            }
            finally { pool.Dispose(); }
        }

        using (var plain = new TerrainGenChunkSource.ShellChunk())
        using (var edited = new TerrainGenChunkSource.ShellChunk())
        {
            _src.GenerateShellChunk(1, cs, plain);
            _src.GenerateShellChunk(1, cs, edited, snap);
            int covered = 0, bad = 0, excused = 0; string first = null;
            for (int z = 0; z < 128; z++)
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                int fi = ((z << 1) << 16) | ((y << 1) << 8) | (x << 1);   // shell 1: half = 0, sample = 2 l
                bool cov = false;
                foreach (var op in ops) if (EditOpApply.Covers(op, fineOrigin + (new int3(x, y, z) << 1))) cov = true;
                if (cov) covered++;
                if (edited.Get(x, y, z) == fineEdited[fi]) continue;
                // ShellChunkTests' one exception: an uncovered top voxel takes D(h), not D(sample).
                if (!cov && plain.Get(x, y, z) != finePlain[fi]) { excused++; continue; }
                bad++; if (first == null) first = $"({x},{y},{z}) shell {edited.Get(x, y, z)} fine {fineEdited[fi]} covered {cov}";
            }
            Debug.Log($"[81] shell 1 chunk {cs} vs the edited 0.1 m world: {covered} covered samples, {bad} differ, {excused} top voxels (row 13)");
            Assert.Greater(covered, 0);
            Assert.AreEqual(0, bad, first);
        }
    }
}
