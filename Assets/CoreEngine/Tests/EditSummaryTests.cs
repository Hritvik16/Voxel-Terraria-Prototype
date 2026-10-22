// Assets/CoreEngine/Tests/EditSummaryTests.cs
//
// Task 93a (Amendment 8.15 §5 "Live", engine row E1): resident edited 0.1 m
// chunks redraw into the shells that cover them as EDIT SUMMARIES.
//   Vote          EditSummaryOps.Vote == LODDownsampler.MajorityVote (fuzz).
//   Levels 1-7    a resident chunk with a pillar built and a pit dug (SetVoxel):
//                 GenerateShellChunk with its summary == a reference built here
//                 independently (generator, then LODDownsampler.DownsampleOnce
//                 chained k times over the resident chunk, written only where
//                 the coarse voxel's fine cube meets an edited brick), byte for
//                 byte, every shell 1-7 (cave fill off: it runs after, task 83).
//   D.1           MaskFromDelta(Encode(..)) == the bricks the edits changed.
//   Streamer      shell 1 holds the covering chunk; an unedited resident chunk
//                 and an op-only change summarise nothing; a SetVoxel pillar is
//                 summarised and the covering chunk redrawn == the reference
//                 (uploaded shadow); a second edit 0.5 s later waits for the
//                 1 s interval; three edited chunks at once start ONE build per
//                 frame (the rest refused and counted, built later); eviction
//                 drops the summary.
//   Shells off    no sink: SetVoxel marks nothing; GenerateShellChunk without
//                 summaries is unchanged (null snapshot == the old overload).
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class EditSummaryTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }
    [OneTimeTearDown] public void TearDown() { _src?.Dispose(); _src = null; }

    private sealed class CountingSink : IChunkEditSink
    {
        public readonly List<int3> Seen = new List<int3>();
        public void ChunkEdited(int3 c) => Seen.Add(c);
    }

    /// A dry land column at fine x, z = chunk origin + 48 whose ground sits 16-72
    /// voxels into its chunk (room for a pit below and a pillar above).
    private static int3 Site(int skip = 0)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        int c0 = (spec.worldSizeVoxels >> 1) >> 7;
        for (int i = 0; i < 4000; i++)
        {
            int cx = c0 + (i % 40) - 20, cz = c0 + (i / 40) - 50;
            int x = (cx << 7) + 48, z = (cz << 7) + 48;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, x, z, 1);
            if (col.water > col.height || col.height < spec.seaLevelY + 50) continue;
            int iy = col.height & 127;
            if (iy < 16 || iy > 72) continue;
            if (skip-- > 0) continue;
            return new int3(x, col.height, z);
        }
        Assert.Fail("no site");
        return default;
    }

    private static Chunk Resident(ChunkStore store, ChunkHandleAllocator alloc, BrickDataPool pool, int3 c)
    {
        var ch = new Chunk { coord = c };
        _src.Generate(c, ch, alloc, pool, null);
        store.InsertChunk(ch);
        return ch;
    }

    /// A 4x40x4 stone pillar on the ground and an 8x12x8 pit beside it; returns
    /// the bricks whose voxels it actually changed.
    private static HashSet<int> Build(EditService edits, ChunkStore store, int3 s)
    {
        var bricks = new HashSet<int>();
        void Box(int3 lo, int3 hi, byte m)
        {
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var v = new int3(x, y, z);
                if (store.GetVoxel(v) != m) bricks.Add(BrickOf(v));
            }
            edits.SetBox(lo, hi, m);
        }
        Box(s + new int3(0, 1, 0), s + new int3(3, 40, 3), Materials.Stone);
        Box(s + new int3(10, -12, 0), s + new int3(17, 0, 7), Materials.Air);
        return bricks;
    }

    private static int BrickOf(int3 v) => CoordMath.LocalBrickIndex(CoordMath.LocalBrickIndex3D(v >> 3));

    /// The reference, built without EditSummaryOps: generator (+ ops), then for
    /// each edited resident chunk inside shell chunk `cover`, the chained
    /// majority vote of its voxels written where a coarse voxel meets an edited brick.
    private static void Reference(int k, int3 cover, EditOpSnapshot ops, ChunkStore store,
                                  Dictionary<int3, HashSet<int>> edited, TerrainGenChunkSource.ShellChunk outp,
                                  Dictionary<int3, byte[][]> chains = null)
    {
        _src.GenerateShellChunk(k, cover, outp, ops);
        if (outp.IsUniformAir)
        {
            for (int i = 0; i < outp.Mats.Length; i++) outp.Mats[i] = Materials.Air;
            outp.IsUniformAir = false;
        }
        foreach (var kv in edited)
        {
            int3 f = kv.Key;
            if (!(f >> k).Equals(cover)) continue;
            if (chains == null || !chains.TryGetValue(f, out var chain))
            {
                chain = new byte[8][];
                var fine = chain[0] = new byte[128 * 128 * 128];
                int3 o = f << 7;
                for (int z = 0; z < 128; z++)
                for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    fine[x + 128 * (y + 128 * z)] = store.GetVoxel(o + new int3(x, y, z));
                for (int j = 0; j < 7; j++) chain[j + 1] = LODDownsampler.DownsampleOnce(chain[j], 128 >> j);
                if (chains != null) chains[f] = chain;
            }
            byte[] lvl = chain[k]; int e = 128 >> k;
            int3 region = (f & ((1 << k) - 1)) * e;
            for (int z = 0; z < e; z++)
            for (int y = 0; y < e; y++)
            for (int x = 0; x < e; x++)
            {
                int3 p = new int3(x, y, z);
                int3 b0 = (p << k) >> 3, b1 = (((p + 1) << k) - 1) >> 3;
                bool hit = false;
                for (int bz = b0.z; bz <= b1.z && !hit; bz++)
                for (int by = b0.y; by <= b1.y && !hit; by++)
                for (int bx = b0.x; bx <= b1.x && !hit; bx++)
                    hit = kv.Value.Contains(CoordMath.LocalBrickIndex(new int3(bx, by, bz)));
                if (!hit) continue;
                int3 q = region + p;
                outp.Mats[TerrainGenChunkSource.Index(q.x, q.y, q.z)] = lvl[x + e * (y + e * z)];
            }
        }
    }

    private static int Differ(TerrainGenChunkSource.ShellChunk a, TerrainGenChunkSource.ShellChunk b, out string first)
    {
        int n = 0; first = null;
        for (int z = 0; z < 128; z++)
        for (int y = 0; y < 128; y++)
        for (int x = 0; x < 128; x++)
        {
            byte p = a.Get(x, y, z), q = b.Get(x, y, z);
            if (p == q) continue;
            if (n++ == 0) first = $"({x},{y},{z}): {p} vs {q}";
        }
        return n;
    }

    // ==================================================================

    [Test]
    public void Vote_MatchesLODDownsamplerMajorityVote()
    {
        var rng = new System.Random(93);
        for (int i = 0; i < 200000; i++)
        {
            var v = new byte[8];
            int alphabet = 2 + (i % 5);
            for (int j = 0; j < 8; j++) v[j] = (byte)(rng.Next(alphabet) == 0 ? 0 : 1 + rng.Next(i % 7 == 0 ? 250 : alphabet));
            ulong p = 0;
            for (int j = 0; j < 8; j++) p |= (ulong)v[j] << (j << 3);
            Assert.AreEqual(LODDownsampler.MajorityVote(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]), EditSummaryOps.Vote(p),
                            $"samples {string.Join(",", v)}");
        }
    }

    [Test]
    public void Summary_EveryShellLevel_EqualsGeneratorPlusMajorityVoteOfTheResidentChunk()
    {
        int3 site = Site();
        int3 c = site >> 7;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
        var alloc = new ChunkHandleAllocator(2);
        var scratch = new EditSummaryOps.Scratch();
        var got = new TerrainGenChunkSource.ShellChunk();
        var want = new TerrainGenChunkSource.ShellChunk();
        bool fill = _src.ShellFill;
        _src.ShellFill = false;
        var lines = new List<string>();
        try
        {
            var store = new ChunkStore(pool, alloc);
            var sink = new CountingSink();
            store.EditSink = sink;
            var ch = Resident(store, alloc, pool, c);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var bricks = Build(edits, store, site);
            Assert.AreEqual(1, sink.Seen.Count, "one notice per chunk until the snapshot");
            int marked = 0;
            for (int b = 0; b < EngineConfig.BRICKS_PER_CHUNK; b++)
                if ((ch.editMask[b >> 6] & (1UL << (b & 63))) != 0) { marked++; Assert.IsTrue(bricks.Contains(b), $"brick {b} marked, not edited"); }
            Assert.AreEqual(bricks.Count, marked, "edited bricks not all marked");

            EditSummaryOps.Snapshot(ch, pool, scratch);
            EditSummaryOps.Run(scratch);
            var sum = EditSummaryOps.Extract(c, ch.editMask, scratch);
            var snap = new EditSummarySnapshot(new[] { sum }, 1);
            var edited = new Dictionary<int3, HashSet<int>> { [c] = bricks };
            var chains = new Dictionary<int3, byte[][]>();
            for (int k = 1; k <= EditSummaryOps.MAX_LEVEL; k++)
            {
                int3 cover = c >> k;
                _src.GenerateShellChunk(k, cover, got, null, snap);
                Assert.AreEqual(1, got.SummariesApplied, $"shell {k}");
                Reference(k, cover, null, store, edited, want, chains);
                int d = Differ(got, want, out string ex);
                // the summary changed something (else the test proves nothing)
                _src.GenerateShellChunk(k, cover, want, (EditOpSnapshot)null);
                int changed = Differ(got, want, out _);
                lines.Add($"shell {k}: chunk {cover}, {sum.VoxelsAt(k)} summary voxels, {changed} differ from the generator, {d} from the reference");
                Assert.AreEqual(0, d, $"shell {k}: {ex}");
                if (k <= 3) Assert.Greater(changed, 0, $"shell {k}: the summary changed nothing");
                // bricks are consistent with Mats
                int badBricks = 0;
                for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                {
                    if (!got.BrickUniform[bi]) continue;
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    bool ok = true;
                    for (int z = 0; z < 8 && ok; z++) for (int y = 0; y < 8 && ok; y++) for (int x = 0; x < 8 && ok; x++)
                        ok = got.BrickMat[bi] == got.Get((bx << 3) + x, (by << 3) + y, (bz << 3) + z);
                    if (!ok) badBricks++;
                }
                Assert.AreEqual(0, badBricks, $"shell {k}: bricks marked uniform that are not");
            }
            lines.Add($"chunk {c}: {bricks.Count} edited bricks, summary {sum.Bytes} B");
        }
        finally
        {
            _src.ShellFill = fill;
            scratch.Dispose(); got.Dispose(); want.Dispose(); pool.Dispose();
            Debug.Log("[93a] levels\n" + string.Join("\n", lines));
        }
    }

    [Test]
    public void MaskFromDelta_IsTheBricksTheEditsChanged()
    {
        int3 site = Site();
        int3 c = site >> 7;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 3);
        var alloc = new ChunkHandleAllocator(3);
        try
        {
            var store = new ChunkStore(pool, alloc);
            store.EditSink = new CountingSink();
            var live = Resident(store, alloc, pool, c);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            Build(edits, store, site);
            var baseline = new Chunk { coord = c };
            _src.Generate(c, baseline, alloc, pool, null);
            byte[] bytes = DeltaCodec.Encode(c, 42u, live, pool, baseline, pool);
            var mask = EditSummaryOps.MaskFromDelta(bytes);
            Assert.IsNotNull(mask);
            CollectionAssert.AreEqual(live.editMask, mask, "D.1 records != the marked bricks");

            // An unedited chunk's delta records nothing.
            byte[] none = DeltaCodec.Encode(c, 42u, baseline, pool, baseline, pool);
            Assert.IsNull(EditSummaryOps.MaskFromDelta(none));
        }
        finally { pool.Dispose(); }
    }

    [Test]
    public void ShellsOff_SetVoxelMarksNothing_GenerationUnchanged()
    {
        int3 site = Site();
        int3 c = site >> 7;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 2);
        var alloc = new ChunkHandleAllocator(2);
        var a = new TerrainGenChunkSource.ShellChunk();
        var b = new TerrainGenChunkSource.ShellChunk();
        try
        {
            var store = new ChunkStore(pool, alloc);   // no EditSink: shells off
            var ch = Resident(store, alloc, pool, c);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            Build(edits, store, site);
            Assert.IsNull(ch.editMask);
            Assert.IsFalse(ch.editPending);
            Assert.AreEqual(Materials.Stone, store.GetVoxel(site + new int3(1, 5, 1)));

            var log = EditOpLog.InMemory(42u);
            log.Append(new EditOp(EditOpKind.Box, site, 30, Materials.Stone));
            _src.GenerateShellChunk(1, c >> 1, a, log.Snapshot);
            _src.GenerateShellChunk(1, c >> 1, b, log.Snapshot, null);
            Assert.AreEqual(0, Differ(a, b, out string ex), ex);
            Assert.AreEqual(0, b.SummariesApplied);
        }
        finally { a.Dispose(); b.Dispose(); pool.Dispose(); }
    }

    [Test, Timeout(600000)]
    public void Streamer_EditedChunkRedrawsItsShellChunk_ByteExact_IntervalCapEviction()
    {
        const int BUDGET = 3 * 1024 * 1024;
        int3 site = Site();
        int3 c = site >> 7;
        int3 cover = c >> 1;
        var L = ShellLayout.From(QualityPreset.Low);
        Assert.GreaterOrEqual(L[1].Reach, 2);
        // Camera two shell-1 chunks east of the cover (outside shell 1's hole), above the ground.
        Assert.GreaterOrEqual(L[1].Reach, L[1].Hole + 2);
        int3 camShell = cover + new int3(L[1].Hole + 2, 0, 0);   // cover and cover + x both outside the hole
        float3 cam = new float3(((camShell.x << 8) + 128) * 0.1f, (site.y + 30) * 0.1f, ((camShell.z << 8) + 128) * 0.1f);

        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 8);
        var alloc = new ChunkHandleAllocator(8);
        var log = EditOpLog.InMemory(42u);
        double clock = 0;
        ShellStore.ShadowUploads = true;
        var s = new ShellStreamer(L, _src, 1, synchronous: true, firstShell: 1, lastShell: 1)
            { Ops = log, SyncChunksPerUpdate = 32, SummaryClock = () => clock };
        var want = new TerrainGenChunkSource.ShellChunk();
        var lines = new List<string>();
        // Shell 1 is a fill shell since task 93b2; the reference writes the summary over
        // the generator, the streamer fills after it (summary boxes as seeds). This test
        // checks the redraw pipeline, so the fill is off on both sides (ShellFillTests).
        bool fill = _src.ShellFill;
        _src.ShellFill = false;
        try
        {
            var store = new ChunkStore(pool, alloc);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var ops = new EditOps(log, edits, store);
            // c and its +x, +z neighbours (same or next cover chunk, all covered), and an
            // unedited chunk; c2 for the op-only change.
            int3 cx = c + new int3(1, 0, 0), cz = c + new int3(0, 0, 1), c2 = c + new int3(0, 0, -1);
            foreach (var q in new[] { c, cx, cz, c2 }) Resident(store, alloc, pool, q);
            s.AttachEdits(store);
            Assert.AreEqual(store.EditSink, s);

            var st = s.Store(1);
            int n = 0;
            var t = System.Diagnostics.Stopwatch.StartNew();
            while (!st.Holds(cover) || !st.Holds(cx >> 1) || !st.Holds(cz >> 1) || !st.Holds(c2 >> 1))
            { s.Update(cam, BUDGET); Assert.Less(++n, 3000, "shell 1 never held the cover chunks"); }
            lines.Add($"shell 1 holds the covers after {n} updates, {s.Generated} generated, {t.Elapsed.TotalSeconds:F1} s");
            Assert.IsTrue(s.SummaryCovered(c));

            // Unedited resident chunks: nothing to summarise, nothing to redraw.
            for (int i = 0; i < 3; i++) s.Update(cam, BUDGET);
            Assert.AreEqual(0, s.SummaryNotices);
            Assert.AreEqual(0, s.RedrawsQueued);

            // An op-only change (EditOps over a resident chunk): the op redraw, no summary.
            ops.Record(new EditOp(EditOpKind.Box, (c2 << 7) + 64, 6, Materials.Stone));
            for (int i = 0; i < 40 && (s.RedrawsPending > 0 || i < 3); i++) s.Update(cam, BUDGET);
            Assert.IsNull(store.GetChunk(c2).editMask, "an op marked a brick");
            Assert.AreEqual(0, s.SummaryNotices);
            Assert.AreEqual(0, s.SummariesBuilt);
            Assert.Greater(s.RedrawsQueued, 0, "the op redraw did not happen");
            long opRedraws = s.RedrawsQueued;

            // A SetVoxel pillar + pit in c: summarised, the cover redrawn == the reference.
            clock = 10;
            var bricks = Build(edits, store, site);
            Assert.AreEqual(1, s.SummaryNotices);
            s.Update(cam, BUDGET);   // snapshot + build (synchronous)
            Assert.AreEqual(0, s.SummariesBuilt);
            s.Update(cam, BUDGET);   // extract, publish, queue the redraw
            Assert.AreEqual(1, s.SummariesBuilt);
            Assert.AreEqual(1, s.SummariesHeld);
            Assert.AreEqual(1, s.SummaryRedrawsQueued);
            for (int i = 0; i < 40 && s.RedrawsPending > 0; i++) s.Update(cam, BUDGET);
            Assert.AreEqual(0, s.RedrawsPending);
            Reference(1, cover, log.Snapshot, store, new Dictionary<int3, HashSet<int>> { [c] = bricks }, want);
            int d = ShellValidator.CompareShadow(st, want, out string ex);
            Assert.AreEqual(0, d, ex);
            Assert.AreEqual(0, ShellValidator.CountStaleSlots(s, 1, out ex), ex);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ex), ex);
            lines.Add($"pillar + pit: {bricks.Count} bricks, summary {s.SummaryBytes} B, cover {cover} redrawn ({s.RedrawsQueued - opRedraws} redraws), 0 voxels differ from the reference");

            // Once per second: an edit 0.5 s later waits; at 1.0 s it builds.
            clock = 10.5;
            edits.SetVoxel(site + new int3(1, 41, 1), Materials.Stone);
            bricks.Add(BrickOf(site + new int3(1, 41, 1)));
            for (int i = 0; i < 5; i++) s.Update(cam, BUDGET);
            Assert.AreEqual(1, s.SummariesBuilt, "rebuilt inside the interval");
            clock = 11.0;
            s.Update(cam, BUDGET); s.Update(cam, BUDGET);
            Assert.AreEqual(2, s.SummariesBuilt, "not rebuilt after the interval");
            for (int i = 0; i < 40 && s.RedrawsPending > 0; i++) s.Update(cam, BUDGET);
            Reference(1, cover, log.Snapshot, store, new Dictionary<int3, HashSet<int>> { [c] = bricks }, want);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, want, out ex), "after the second edit: " + ex);

            // Per-frame cap: three chunks edited at once start one build per frame.
            clock = 20;
            var bx = Build(edits, store, new int3((cx.x << 7) + 48, site.y, (cx.z << 7) + 48));
            var bz = Build(edits, store, new int3((cz.x << 7) + 48, site.y, (cz.z << 7) + 48));
            Assert.Greater(bx.Count, 0); Assert.Greater(bz.Count, 0);
            edits.SetVoxel(site + new int3(2, 41, 2), Materials.Stone);
            bricks.Add(BrickOf(site + new int3(2, 41, 2)));
            long built0 = s.SummariesBuilt, refused0 = s.SummariesRefused;
            s.Update(cam, BUDGET);
            Assert.AreEqual(EngineConfig.EDIT_SUMMARIES_PER_FRAME, 1, "this test assumes a cap of 1");
            Assert.AreEqual(2, s.SummariesRefused - refused0, "three ready, one started: two refused and counted");
            s.Update(cam, BUDGET);
            Assert.AreEqual(built0 + 1, s.SummariesBuilt);
            for (int i = 0; i < 60 && (s.SummariesBuilt < built0 + 3 || s.RedrawsPending > 0); i++) s.Update(cam, BUDGET);
            Assert.AreEqual(built0 + 3, s.SummariesBuilt, "the refused builds never ran");
            Assert.AreEqual(3, s.SummariesHeld);
            var edited = new Dictionary<int3, HashSet<int>> { [c] = bricks, [cx] = bx, [cz] = bz };
            foreach (var q in new[] { cover, cx >> 1, cz >> 1 })
            {
                Reference(1, q, log.Snapshot, store, edited, want);
                Assert.AreEqual(0, ShellValidator.CompareShadow(st, want, out ex), $"cover {q}: {ex}");
            }
            lines.Add($"cap: 3 chunks edited in one frame -> {s.SummariesRefused - refused0} refusals, all built; {s.SummariesBuilt} built in total");

            // Eviction keeps nothing.
            store.EvictChunk(cx);
            s.Update(cam, BUDGET);
            Assert.AreEqual(2, s.SummariesHeld);
            Assert.AreEqual(1, s.SummariesDropped);
            lines.Add($"eviction: {s.SummariesDropped} dropped, {s.SummariesHeld} held; main-thread summary ms {s.SummaryBuildMsMain:F1}");
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            _src.ShellFill = fill;
            s.Dispose(); want.Dispose(); pool.Dispose();
            Debug.Log("[93a] streamer\n" + string.Join("\n", lines));
        }
    }
}
