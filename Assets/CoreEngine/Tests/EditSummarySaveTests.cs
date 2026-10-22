// Assets/CoreEngine/Tests/EditSummarySaveTests.cs
//
// Task 108 (Amendment 8.15 row E2): SAVED edit summaries, D.5 `summary/`.
//   SaveEvictRestart  a SetVoxel tower + pit in a resident chunk, summarised
//                     live by 93a's streamer; "saved" (D.1 + D.5 as SaveDelta
//                     does) -> the D.5 summary == the live one at every level;
//                     evicted -> the streamer swaps in the saved one with no
//                     redraw; a NEW session (fresh store + streamer, no resident
//                     chunk) draws shell 1 == generator + ops + the live summary,
//                     byte for byte; shells 1-7 generated with the loaded
//                     summary == with the live one. Reports sizes and build ms.
//   Stale / corrupt   D.1 rewritten without its summary (a crash between the
//                     two) -> StaleDelta, ignored, shell 1 == the generator;
//                     one flipped byte, a truncated file, D.1 missing ->
//                     rejected with the reason, never thrown.
//   Ops order         op 1 before the save (S = 1), op 2 after it carving the
//                     tower's top: shells == ops <= S, summary, ops > S (and
//                     != the summary over every op).
//   Cleared           StreamManager.ClearSaveFiles removes summary/ with the
//                     deltas, ops.log and world.state.
//   Far band          a summary saved after the fill patches its chunk's texels
//                     == a fresh FarField built with it; the tower's centre
//                     texel rises to its top (stone), the pit's drops.
// Task 113 (the build off the main thread; every test above saves through the
// scheduled job, Save waits for it):
//   Bytes identical   async .sum == Synchronous .sum == an independent
//                     Snapshot/Run/Extract/Encode, with the chunk evicted, its
//                     pool slots overwritten and the chunk reloaded while the job
//                     runs; pending = Has, NotLoaded (no read), no file; a newer
//                     D.1 supersedes, a deleted D.1 drops; 5 saves on 4 scratches.
//   Evict while pending  the dropped live summary stands in (no redraw); a
//                     reload's live summary wins; the landed one swaps in on the
//                     next eviction with no redraw.
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class EditSummarySaveTests
{
    private const uint SEED = 42u;
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(SEED); }
    [OneTimeTearDown] public void TearDown() { _src?.Dispose(); _src = null; }

    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "esum108_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    /// A dry site (ground within 12 voxels over 4 x 3 m) at chunk origin + 48 in x and z whose
    /// highest ground sits 18-70 voxels into its chunk. y = that highest ground.
    private static int3 Site()
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        int c0 = (spec.worldSizeVoxels >> 1) >> 7;
        for (int i = 0; i < 40000; i++)
        {
            int cx = c0 + (i % 200) - 100, cz = c0 + (i / 200) - 100;
            int x = (cx << 7) + 48, z = (cz << 7) + 48;
            bool ok = true; int gmax = int.MinValue, gmin = int.MaxValue;
            for (int dz = 0; dz < 32 && ok; dz += 4)
            for (int dx = 0; dx < 40 && ok; dx += 4)
            {
                var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + dx, z + dz, 1);
                ok = col.water <= col.height && col.height >= spec.seaLevelY + 50;
                gmax = math.max(gmax, col.height); gmin = math.min(gmin, col.height);
            }
            if (!ok || gmax - gmin > 12) continue;
            int iy = gmax & 127;
            if (iy < 18 || iy > 70) continue;
            return new int3(x, gmax, z);
        }
        Assert.Fail("no site");
        return default;
    }

    private static Chunk Gen(int3 c, ChunkHandleAllocator alloc, BrickDataPool pool)
    {
        var ch = new Chunk { coord = c };
        _src.Generate(c, ch, alloc, pool, null);
        return ch;
    }

    /// A 16 x 48 x 16 stone tower on the ground (x, z = site + 0..15) and an 8 x 17 x 8 pit
    /// open to the sky at x = site + 24..31, y = site - 16 .. site (one brick in x and z, so
    /// every brick it touches holds ground and changes, or is uniform air -- a no-op that
    /// marks nothing: D.1's bricks == the bricks SetVoxel marks). Returns the tower's top y.
    private static int Build(EditService edits, int3 s)
    {
        edits.SetBox(s + new int3(0, 1, 0), s + new int3(15, 48, 15), Materials.Stone);
        edits.SetBox(s + new int3(24, -PIT, 0), s + new int3(31, 0, 7), Materials.Air);
        return s.y + 48;
    }
    private const int PIT = 16;

    /// SaveDelta's steps: baseline = generator + ops 0..S, D.1 encoded and written, then D.5
    /// (scheduled, then waited for: task 113).
    private static byte[] Save(string dir, SavedEditSummaries saved, int3 c, Chunk live, BrickDataPool pool, EditOpSnapshot ops, int S)
    {
        byte[] bytes = Delta(dir, c, live, pool, ops, S);
        saved.DeltaWritten(c, bytes, live, pool, S);
        saved.CompleteAll();
        return bytes;
    }

    /// D.1 of `live` against generator + ops 0..S, written into `dir`.
    private static byte[] Delta(string dir, int3 c, Chunk live, BrickDataPool pool, EditOpSnapshot ops, int S)
    {
        var bpool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var balloc = new ChunkHandleAllocator(2);
        try
        {
            var baseline = Gen(c, balloc, bpool);
            if (S > 0) EditOpApply.ApplyToChunk(baseline, c, balloc, bpool, ops.Ops, 0, S);
            byte[] bytes = DeltaCodec.Encode(c, SEED, live, pool, baseline, bpool);
            Assert.IsNotNull(bytes, "the edits left no delta");
            DeltaCodec.WriteAtomic(DeltaCodec.PathFor(dir, c), bytes);
            return bytes;
        }
        finally { bpool.Dispose(); }
    }

    /// 108's build, by hand (no SavedEditSummaries): the .sum bytes of `live` for D.1 `d1`.
    private static byte[] Reference(int3 c, Chunk live, BrickDataPool pool, byte[] d1, int S)
    {
        using var scr = new EditSummaryOps.Scratch();
        EditSummaryOps.Snapshot(live, pool, scr);
        EditSummaryOps.Run(scr);
        var sum = EditSummaryOps.Extract(c, EditSummaryOps.MaskFromDelta(d1), scr, S);
        return SummaryCodec.Encode(sum, SEED, EditOpLog.TrailerCrc(d1));
    }

    /// Pumps until no build is in flight (the main thread's per-frame Pump, in a loop).
    private static void PumpUntilDone(SavedEditSummaries saved)
    {
        var t = System.Diagnostics.Stopwatch.StartNew();
        while (saved.InFlight > 0)
        {
            saved.Pump();
            Assert.Less(t.Elapsed.TotalSeconds, 60, "a summary build never finished");
            System.Threading.Thread.Sleep(1);
        }
    }

    private static int Diff(TerrainGenChunkSource.ShellChunk a, TerrainGenChunkSource.ShellChunk b, out string ex)
    {
        int n = 0; ex = "";
        for (int y = 0; y < 128; y++)
        for (int z = 0; z < 128; z++)
        for (int x = 0; x < 128; x++)
            if (a.Get(x, y, z) != b.Get(x, y, z))
            {
                if (n++ == 0) ex = $"first at ({x},{y},{z}): {a.Get(x, y, z)} vs {b.Get(x, y, z)}";
            }
        return n;
    }

    private static EditSummarySnapshot Snap(params EditSummary[] s) => new EditSummarySnapshot(s, 1);

    // =====================================================================

    [Test, Timeout(900000)]
    public void SaveEvictRestart_ShellsRegenerateWithTheSavedSummary_EqualsLive()
    {
        const int BUDGET = 3 * 1024 * 1024;
        string dir = TempDir();
        int3 site = Site();
        int3 c = site >> 7, cover = c >> 1;
        var L = ShellLayout.From(QualityPreset.Low);
        int3 camShell = cover + new int3(L[1].Hole + 2, 0, 0);
        float3 cam = new float3(((camShell.x << 8) + 128) * 0.1f, (site.y + 30) * 0.1f, ((camShell.z << 8) + 128) * 0.1f);

        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 4);
        var alloc = new ChunkHandleAllocator(4);
        var log = EditOpLog.InMemory(SEED);
        double clock = 0;
        ShellStore.ShadowUploads = true;
        bool fill = _src.ShellFill;
        _src.ShellFill = false;   // the fill runs after the summaries; this test checks the summary path (ShellFillTests)
        var saved = SavedEditSummaries.Open(dir, SEED);
        var s = new ShellStreamer(L, _src, 1, synchronous: true, firstShell: 1, lastShell: 1)
            { Ops = log, SyncChunksPerUpdate = 32, SummaryClock = () => clock, Saved = saved };
        ShellStreamer s2 = null;
        SavedEditSummaries saved2 = null;
        var a = new TerrainGenChunkSource.ShellChunk();
        var b = new TerrainGenChunkSource.ShellChunk();
        var lines = new List<string>();
        try
        {
            var store = new ChunkStore(pool, alloc);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var live = Gen(c, alloc, pool);
            store.InsertChunk(live);
            s.AttachEdits(store);
            var st = s.Store(1);
            int n = 0;
            while (!st.Holds(cover)) { s.Update(cam, BUDGET); Assert.Less(++n, 3000, "shell 1 never held the cover"); }

            clock = 10;
            int towerTop = Build(edits, site);
            for (int i = 0; i < 60 && (s.SummariesBuilt == 0 || s.RedrawsPending > 0); i++) s.Update(cam, BUDGET);
            Assert.AreEqual(1, s.SummariesBuilt);
            var liveSum = s.Summaries.All[0];
            Assert.AreEqual(int.MaxValue, liveSum.OpsBefore);

            // Save (SaveDelta's order): D.1, then D.5.
            var t = System.Diagnostics.Stopwatch.StartNew();
            byte[] d1 = Save(dir, saved, c, live, pool, log.Snapshot, log.Count);
            double saveMs = t.Elapsed.TotalMilliseconds;
            var mask = EditSummaryOps.MaskFromDelta(d1);
            CollectionAssert.AreEqual(live.editMask, mask, "D.1's bricks != the bricks SetVoxel marked (the test's precondition)");
            string sumPath = SavedEditSummaries.PathFor(dir, c);
            Assert.IsTrue(File.Exists(sumPath));
            long sumBytes = new FileInfo(sumPath).Length;
            int budget = 1;
            Assert.AreEqual(SavedEditSummaries.GetResult.Valid, saved.TryGet(c, ref budget, out var written));
            Assert.IsTrue(written.SameLook(liveSum), "the saved summary != the live 93a summary");
            int edited = 0; foreach (var w in mask) edited += math.countbits(w);
            // Steady build cost (the first one above includes the scratch and Burst's first call):
            // Snapshot + Burst vote + Extract + encode, 7 times, median. Test timing, not a frame time.
            var times = new List<double>();
            using (var scr = new EditSummaryOps.Scratch())
                for (int r = 0; r < 7; r++)
                {
                    var tb = System.Diagnostics.Stopwatch.StartNew();
                    EditSummaryOps.Snapshot(live, pool, scr);
                    EditSummaryOps.Run(scr);
                    var again = EditSummaryOps.Extract(c, mask, scr, log.Count);
                    SummaryCodec.Encode(again, SEED, 1u);
                    times.Add(tb.Elapsed.TotalMilliseconds);
                }
            times.Sort();
            lines.Add($"summary build, steady (median of 7): {times[3]:F2} ms (min {times[0]:F2}, max {times[6]:F2})");
            lines.Add($"tower + pit: {edited} edited bricks; D.1 {d1.Length} B, D.5 {sumBytes} B ({100.0 * sumBytes / d1.Length:F1}% of D.1); " +
                      $"summary build {saved.BuildMsTotal:F2} ms (Snapshot + Burst vote + Extract, main thread), write {saved.WriteMsTotal:F2} ms; " +
                      $"D.1 + D.5 save here {saveMs:F1} ms; in memory {written.Bytes} B; store RAM {saved.RamBytes} B " +
                      $"(index {saved.Count}, far spans {saved.FarSpanCount}, LRU {saved.LruBytes} B, scratch {SavedEditSummaries.ScratchBytes} B)");

            // Evict: the live summary goes, the saved one comes in, same look -> no redraw.
            long redraws0 = s.SummaryRedrawsQueued;
            store.EvictChunk(c);
            s.Update(cam, BUDGET);
            Assert.AreEqual(1, s.SummariesDropped);
            Assert.AreEqual(1, s.SavedHeld);
            Assert.AreEqual(1, s.SummariesHeld);
            Assert.AreEqual(redraws0, s.SummaryRedrawsQueued, "the saved summary redrew the same look");
            Assert.AreEqual(c, s.Summaries.All[0].Coord);
            Assert.AreNotEqual(int.MaxValue, s.Summaries.All[0].OpsBefore);

            // New session: a fresh store reads D.5 from disk; a fresh streamer draws shell 1 with it.
            saved2 = SavedEditSummaries.Open(dir, SEED);
            Assert.AreEqual(1, saved2.Count);
            s2 = new ShellStreamer(L, _src, 1, synchronous: true, firstShell: 1, lastShell: 1)
                { Ops = log, SyncChunksPerUpdate = 32, Saved = saved2 };
            var st2 = s2.Store(1);
            n = 0;
            while (!st2.Holds(cover) || !s2.Idle) { s2.Update(cam, BUDGET); Assert.Less(++n, 3000, "the new session's shell 1 never held the cover"); }
            Assert.AreEqual(1, s2.SavedHeld);
            var loaded = s2.Summaries.All[0];
            Assert.IsTrue(loaded.SameLook(liveSum), "the loaded summary != the live one");
            _src.GenerateShellChunk(1, cover, b, log.Snapshot, Snap(liveSum));
            Assert.AreEqual(0, ShellValidator.CompareShadow(st2, b, out string ex), "new session: " + ex);
            for (int k = 1; k <= EditSummaryOps.MAX_LEVEL; k++)
            {
                _src.GenerateShellChunk(k, c >> k, a, log.Snapshot, Snap(loaded));
                _src.GenerateShellChunk(k, c >> k, b, log.Snapshot, Snap(liveSum));
                Assert.AreEqual(1, a.SummariesApplied, $"shell {k}: the saved summary was not applied");
                Assert.AreEqual(0, Diff(a, b, out ex), $"shell {k}: saved != live: {ex}");
            }
            lines.Add($"new session: shell 1 cover {cover} drawn with the saved summary == generator + live summary (0 voxels differ); shells 1-7 saved == live; " +
                      $"tower top y {towerTop}");
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            _src.ShellFill = fill;
            s.Dispose(); s2?.Dispose(); saved.Dispose(); saved2?.Dispose(); a.Dispose(); b.Dispose(); pool.Dispose();
            Directory.Delete(dir, true);
            Debug.Log("[108] save/evict/restart\n" + string.Join("\n", lines));
        }
    }

    [Test, Timeout(600000)]
    public void StaleCorruptTruncatedMissing_Rejected_ShellShowsTheGenerator()
    {
        const int BUDGET = 3 * 1024 * 1024;
        string dir = TempDir();
        int3 site = Site();
        int3 c = site >> 7, cover = c >> 1;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 4);
        var alloc = new ChunkHandleAllocator(4);
        var log = EditOpLog.InMemory(SEED);
        var lines = new List<string>();
        var saved = SavedEditSummaries.Open(dir, SEED);
        var a = new TerrainGenChunkSource.ShellChunk();
        ShellStreamer s = null;
        bool fill = _src.ShellFill;
        _src.ShellFill = false;
        ShellStore.ShadowUploads = true;
        try
        {
            var store = new ChunkStore(pool, alloc);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var live = Gen(c, alloc, pool);
            store.InsertChunk(live);
            Build(edits, site);
            string sumPath = SavedEditSummaries.PathFor(dir, c);
            byte[] good = null;

            SummaryRejectReason Reopen()
            {
                var r = SavedEditSummaries.Open(dir, SEED);
                try
                {
                    int bud = 1;
                    var g = r.TryGet(c, ref bud, out var sum);
                    Assert.AreEqual(SavedEditSummaries.GetResult.None, g, "a bad summary was accepted");
                    Assert.IsNull(sum);
                    Assert.AreEqual(1, r.Rejected);
                    return r.RejectOf(c);
                }
                finally { r.Dispose(); }
            }

            // Valid first.
            Save(dir, saved, c, live, pool, log.Snapshot, 0);
            good = File.ReadAllBytes(sumPath);
            var ok = SavedEditSummaries.Open(dir, SEED);
            int b1 = 1;
            Assert.AreEqual(SavedEditSummaries.GetResult.Valid, ok.TryGet(c, ref b1, out _));
            ok.Dispose();

            // Stale: D.1 rewritten (another edit) without its summary -- a crash between the two.
            edits.SetBox(site + new int3(0, 49, 0), site + new int3(3, 52, 3), Materials.Stone);
            var bpool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            var baseline = Gen(c, new ChunkHandleAllocator(2), bpool);
            DeltaCodec.WriteAtomic(DeltaCodec.PathFor(dir, c), DeltaCodec.Encode(c, SEED, live, pool, baseline, bpool));
            bpool.Dispose();
            Assert.AreEqual(SummaryRejectReason.StaleDelta, Reopen());
            lines.Add("stale (D.1 CRC mismatch): rejected StaleDelta");

            // ... and the shells show the generator.
            var stale = SavedEditSummaries.Open(dir, SEED);
            var L = ShellLayout.From(QualityPreset.Low);
            int3 camShell = cover + new int3(L[1].Hole + 2, 0, 0);
            float3 cam = new float3(((camShell.x << 8) + 128) * 0.1f, (site.y + 30) * 0.1f, ((camShell.z << 8) + 128) * 0.1f);
            s = new ShellStreamer(L, _src, 1, synchronous: true, firstShell: 1, lastShell: 1) { Ops = log, SyncChunksPerUpdate = 32, Saved = stale };
            int n = 0;
            while (!s.Store(1).Holds(cover) || !s.Idle) { s.Update(cam, BUDGET); Assert.Less(++n, 3000); }
            Assert.AreEqual(0, s.SavedHeld);
            _src.GenerateShellChunk(1, cover, a, log.Snapshot);
            Assert.AreEqual(0, ShellValidator.CompareShadow(s.Store(1), a, out string ex), "stale summary drawn: " + ex);
            stale.Dispose();

            // Corrupt: one byte flipped (the CRC catches it).
            Save(dir, saved, c, live, pool, log.Snapshot, 0);   // valid again, for the new D.1
            good = File.ReadAllBytes(sumPath);
            var bad = (byte[])good.Clone();
            bad[bad.Length / 2] ^= 0x5A;
            File.WriteAllBytes(sumPath, bad);
            Assert.AreEqual(SummaryRejectReason.CrcMismatch, Reopen());
            // Truncated: every cut is rejected, none throws.
            foreach (int len in new[] { 0, 3, SummaryCodec.HEADER_BYTES, good.Length / 2, good.Length - 1 })
            {
                var cut = new byte[len]; System.Array.Copy(good, cut, len); File.WriteAllBytes(sumPath, cut);
                var why = Reopen();
                Assert.IsTrue(why == SummaryRejectReason.TooShort || why == SummaryRejectReason.CrcMismatch, $"cut to {len}: {why}");
            }
            // Codec: a valid CRC over a bad body (wrong chunk) is still rejected.
            Assert.IsFalse(SummaryCodec.TryDecode(good, SEED, c + new int3(1, 0, 0), out _, out _, out var wrongChunk));
            Assert.AreEqual(SummaryRejectReason.BadHeader, wrongChunk);
            Assert.IsFalse(SummaryCodec.TryDecode(good, SEED + 1, c, out _, out _, out var wrongSeed));
            Assert.AreEqual(SummaryRejectReason.BadHeader, wrongSeed);
            // D.1 missing.
            File.WriteAllBytes(sumPath, good);
            File.Delete(DeltaCodec.PathFor(dir, c));
            Assert.AreEqual(SummaryRejectReason.DeltaMissing, Reopen());
            lines.Add($"corrupt byte: CrcMismatch; truncations rejected; wrong chunk/seed: BadHeader; D.1 missing: DeltaMissing; shell 1 == generator while stale");
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            _src.ShellFill = fill;
            s?.Dispose(); saved.Dispose(); a.Dispose(); pool.Dispose();
            Directory.Delete(dir, true);
            Debug.Log("[108] stale/corrupt\n" + string.Join("\n", lines));
        }
    }

    [Test, Timeout(600000)]
    public void OpsOrder_OpsUpToS_ThenTheSummary_ThenOpsAfterS()
    {
        string dir = TempDir();
        int3 site = Site();
        int3 c = site >> 7;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 4);
        var alloc = new ChunkHandleAllocator(4);
        var log = EditOpLog.InMemory(SEED);
        var saved = SavedEditSummaries.Open(dir, SEED);
        var a = new TerrainGenChunkSource.ShellChunk();
        var b = new TerrainGenChunkSource.ShellChunk();
        var w = new TerrainGenChunkSource.ShellChunk();
        bool fill = _src.ShellFill;
        _src.ShellFill = false;
        try
        {
            var store = new ChunkStore(pool, alloc);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var ops = new EditOps(log, edits, store);
            var live = Gen(c, alloc, pool);
            store.InsertChunk(live);
            // Op 1 (before the save): a 2 m mound beside the tower site; applied to the resident chunk at once.
            ops.Record(new EditOp(EditOpKind.Box, site + new int3(8, 10, 40), 10, Materials.Sandstone));
            int top = Build(edits, site);
            Save(dir, saved, c, live, pool, log.Snapshot, log.Count);
            Assert.AreEqual(1, log.Count);
            int budget = 1;
            Assert.AreEqual(SavedEditSummaries.GetResult.Valid, saved.TryGet(c, ref budget, out var sum));
            Assert.AreEqual(1, sum.OpsBefore);
            store.EvictChunk(c);
            // Op 2 (after the save): carves the tower's top 2.4 m.
            var carve = new EditOp(EditOpKind.Box, new int3(site.x + 8, top - 4, site.z + 8), 12, Materials.Air);
            log.Append(carve);
            var only1 = EditOpLog.InMemory(SEED);
            only1.Append(log.Snapshot.Ops[0]);
            var asLive = Snap(Relabel(sum));
            int differOrder = 0;
            for (int k = 1; k <= 3; k++)
            {
                int3 sc = c >> k;
                // Expected: ops <= S, the summary, then op 2.
                _src.GenerateShellChunk(k, sc, w, only1.Snapshot, Snap(sum));
                EditOpApply.ApplyToShell(w, log.Snapshot.Ops, 1, 2);
                _src.GenerateShellChunk(k, sc, a, log.Snapshot, Snap(sum));
                Assert.AreEqual(0, Diff(a, w, out string ex), $"shell {k}: not ops <= S, summary, ops > S: {ex}");
                // The summary over every op (93a's live order) shows the carved top as stone.
                _src.GenerateShellChunk(k, sc, b, log.Snapshot, asLive);
                differOrder += Diff(a, b, out _);
            }
            Assert.Greater(differOrder, 0, "op 2 never touched the summary: the order test proves nothing");
            Debug.Log($"[108] ops order: shells 1-3 == ops <= S, summary, ops > S; {differOrder} voxels differ from summary-after-every-op");
        }
        finally
        {
            _src.ShellFill = fill;
            saved.Dispose(); a.Dispose(); b.Dispose(); w.Dispose(); pool.Dispose();
            Directory.Delete(dir, true);
        }
    }

    /// The same summary with S = int.MaxValue (applied after every op, like a live one).
    private static EditSummary Relabel(EditSummary s)
    {
        var bytes = SummaryCodec.Encode(s, SEED, 7u);
        // OpsBefore is the header's uint at offset 26: rewrite it, then the CRC.
        bytes[26] = bytes[27] = bytes[28] = 0xFF; bytes[29] = 0x7F;
        uint crc = WorldMeta.Crc32(bytes, 0, bytes.Length - 4);
        bytes[bytes.Length - 4] = (byte)crc; bytes[bytes.Length - 3] = (byte)(crc >> 8);
        bytes[bytes.Length - 2] = (byte)(crc >> 16); bytes[bytes.Length - 1] = (byte)(crc >> 24);
        Assert.IsTrue(SummaryCodec.TryDecode(bytes, SEED, s.Coord, out var r, out _, out var why), why.ToString());
        Assert.AreEqual(int.MaxValue, r.OpsBefore);
        return r;
    }

    [Test]
    public void ClearSaveFiles_RemovesSummariesWithTheDeltas()
    {
        string dir = TempDir();
        try
        {
            string sumDir = Path.Combine(dir, SavedEditSummaries.DIR_NAME);
            Directory.CreateDirectory(sumDir);
            File.WriteAllBytes(Path.Combine(sumDir, SavedEditSummaries.FileName(new int3(1, 0, 2))), new byte[40]);
            File.WriteAllBytes(Path.Combine(sumDir, SavedEditSummaries.FileName(new int3(1, 0, 3)) + ".tmp"), new byte[4]);
            File.WriteAllBytes(DeltaCodec.PathFor(dir, new int3(1, 0, 2)), new byte[30]);
            File.WriteAllBytes(Path.Combine(dir, EditOpLog.FILE_NAME), new byte[10]);
            File.WriteAllBytes(Path.Combine(dir, WorldState.FILE_NAME), new byte[10]);
            var before = SavedEditSummaries.Open(dir, SEED);
            Assert.AreEqual(1, before.Count);
            Assert.IsFalse(File.Exists(Path.Combine(sumDir, SavedEditSummaries.FileName(new int3(1, 0, 3)) + ".tmp")), "a stale .tmp survived Open");
            StreamManager.ClearSaveFiles(dir);
            Assert.IsFalse(Directory.Exists(sumDir));
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.delta").Length);
            Assert.IsFalse(File.Exists(Path.Combine(dir, EditOpLog.FILE_NAME)));
            Assert.IsFalse(File.Exists(Path.Combine(dir, WorldState.FILE_NAME)));
            Assert.AreEqual(0, SavedEditSummaries.Open(dir, SEED).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test, Timeout(600000)]
    public void FarBand_SavedSummaryFoldsIntoColumnHeightAndMaterial_PatchEqualsFreshBuild()
    {
        string dir = TempDir();
        int3 site = Site();
        int3 c = site >> 7;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 4);
        var alloc = new ChunkHandleAllocator(4);
        var log = EditOpLog.InMemory(SEED);
        var saved = SavedEditSummaries.Open(dir, SEED);
        var cam = new Vector3(site.x * 0.1f, site.y * 0.1f + 100f, site.z * 0.1f);
        FarField plain = null, A = null, B = null;
        try
        {
            var store = new ChunkStore(pool, alloc);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var live = Gen(c, alloc, pool);
            store.InsertChunk(live);
            int top = Build(edits, site);

            plain = Far(cam, null);
            A = Far(cam, saved);   // empty when it fills
            Save(dir, saved, c, live, pool, log.Snapshot, 0);   // AFTER A's fill
            int updates = 0;
            do
            {
                A.Update(cam);
                A.PendingScatters.Clear();
                if (A.PatchesPending > 0) System.Threading.Thread.Sleep(1);
                Assert.Less(++updates, 5000, "the summary patches never went up");
            } while (A.PatchesPending > 0);
            Assert.AreEqual(1, A.SummaryPatchChunks);
            B = Far(cam, saved);

            var lines = new List<string>();
            int total = 0;
            for (int L = 0; L < FarField.LEVELS; L++)
            {
                int diff = 0, changed = 0;
                for (int i = L * FarField.LEVEL_TEXELS; i < (L + 1) * FarField.LEVEL_TEXELS; i++)
                {
                    if (A.Shadow[i] != B.Shadow[i]) diff++;
                    if (plain.Shadow[i] != B.Shadow[i]) changed++;
                }
                total += diff;
                lines.Add($"  level {L}: {diff} differ from the fresh build; {changed} changed by the summary");
            }
            Assert.AreEqual(0, total, "patched far band != a fresh build with the summary");

            uint Texel(FarField f, int L, int px, int pz)
            {
                int sh = 3 + L, half = 4 << L;
                int tx = (px - half) >> sh, tz = (pz - half) >> sh;
                return f.Shadow[L * FarField.LEVEL_TEXELS + ((tz & FarField.EDGE_MASK) << 10) + (tx & FarField.EDGE_MASK)];
            }
            // Tower: level 0 samples x & 7 == 4 -> (site + 4, site + 4) is inside it. Its top at 0.2 m:
            int oy = c.y << 7;
            int want = oy + (((top - oy) >> 1) << 1) + 2;
            uint tt = Texel(B, 0, site.x + 4, site.z + 4), tp = Texel(plain, 0, site.x + 4, site.z + 4);
            lines.Add($"  tower texel: top {tt & 0xFFFF} (plain {tp & 0xFFFF}, tower top voxel {top}, want {want}), material {(tt >> 16) & 0xFF}");
            Assert.AreEqual(want, (int)(tt & 0xFFFF), "the tower texel's top");
            Assert.AreEqual(Materials.Stone, (byte)(tt >> 16));
            // Level 1 samples x & 15 == 8 -> (site + 8, site + 8), also inside.
            Assert.AreEqual(want, (int)(Texel(B, 1, site.x + 8, site.z + 8) & 0xFFFF), "level 1 tower texel");
            // Pit: x = site + 28 (& 7 == 4), z = site + 4: dropped to about its floor (site.y - PIT).
            uint pt = Texel(B, 0, site.x + 28, site.z + 4), pp = Texel(plain, 0, site.x + 28, site.z + 4);
            lines.Add($"  pit texel: top {pt & 0xFFFF} (plain {pp & 0xFFFF}, pit floor air {site.y - PIT}), material {(pt >> 16) & 0xFF}");
            Assert.Less((int)(pt & 0xFFFF), (int)(pp & 0xFFFF), "the pit did not lower its texel");
            Assert.LessOrEqual(math.abs((int)(pt & 0xFFFF) - (site.y - PIT)), 2, "the pit texel's top is not at its floor");
            Debug.Log("[108] far band\n" + string.Join("\n", lines));
        }
        finally
        {
            plain?.Dispose(); A?.Dispose(); B?.Dispose();
            saved.Dispose(); pool.Dispose();
            Directory.Delete(dir, true);
        }
    }

    private static FarField Far(Vector3 cam, SavedEditSummaries saved)
    {
        FarField.ShadowTexels = true;
        try { return new FarField(new WorldMetaData { seed = SEED, sizeClass = 2 }, cam, _src.Map, null, saved); }
        finally { FarField.ShadowTexels = false; }
    }

    // =====================================================================
    // Task 113: the build off the main thread
    // =====================================================================

    [Test, Timeout(600000)]
    public void AsyncBuild_BytesIdenticalToSynchronous_EvenWithThePoolOverwrittenWhilePending()
    {
        string dirA = TempDir(), dirB = TempDir();
        int3 site = Site();
        int3 c = site >> 7;
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 4);
        var alloc = new ChunkHandleAllocator(4);
        var log = EditOpLog.InMemory(SEED);
        var sync = SavedEditSummaries.Open(dirA, SEED);
        sync.Synchronous = true;
        var asy = SavedEditSummaries.Open(dirB, SEED);
        var lines = new List<string>();
        try
        {
            var store = new ChunkStore(pool, alloc);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var live = Gen(c, alloc, pool);
            store.InsertChunk(live);
            Build(edits, site);
            byte[] d1 = Delta(dirA, c, live, pool, log.Snapshot, log.Count);
            File.Copy(DeltaCodec.PathFor(dirA, c), DeltaCodec.PathFor(dirB, c));
            byte[] reference = Reference(c, live, pool, d1, log.Count);
            string pathA = SavedEditSummaries.PathFor(dirA, c), pathB = SavedEditSummaries.PathFor(dirB, c);

            Assert.IsNotNull(sync.DeltaWritten(c, d1, live, pool, log.Count), "Synchronous returns the summary");
            Assert.IsNull(asy.DeltaWritten(c, d1, live, pool, log.Count), "async returns nothing until it lands");
            Assert.AreEqual(1, asy.JobsScheduled);
            Assert.IsTrue(asy.IsPending(c) && asy.Has(c) && asy.PendingCount == 1);
            Assert.IsFalse(File.Exists(pathB), "a pending summary was written before Pump");
            int budget = 1;
            Assert.AreEqual(SavedEditSummaries.GetResult.NotLoaded, asy.TryGet(c, ref budget, out _));
            Assert.AreEqual(1, budget, "a pending summary cost a disk read");
            var coords = new List<int3>();
            asy.CopyCoords(coords);
            CollectionAssert.AreEqual(new[] { c }, coords);

            // While the job runs: evict, overwrite every pool byte, reload (generate + the same edits).
            store.EvictChunk(c);
            var raw = pool.RawData;
            for (int i = 0; i < raw.Length; i++) raw[i] = 0xA5;
            var live2 = Gen(c, alloc, pool);
            store.InsertChunk(live2);
            Build(edits, site);
            Assert.IsTrue(asy.IsPending(c), "nothing pumped yet");
            PumpUntilDone(asy);
            Assert.IsFalse(asy.IsPending(c));
            CollectionAssert.AreEqual(reference, File.ReadAllBytes(pathA), "Synchronous != the hand build");
            CollectionAssert.AreEqual(reference, File.ReadAllBytes(pathB), "async != the hand build (the pool was overwritten while pending)");
            budget = 1;
            Assert.AreEqual(SavedEditSummaries.GetResult.Valid, asy.TryGet(c, ref budget, out var sa));
            Assert.AreEqual(SavedEditSummaries.GetResult.Valid, sync.TryGet(c, ref budget, out var ss));
            Assert.IsTrue(sa.SameLook(ss), "async != sync at some level");
            lines.Add($"async == sync == hand build: {reference.Length} B, byte for byte (pool overwritten + chunk reloaded while pending)");

            // A newer D.1 supersedes the pending one; only the newest is written.
            edits.SetBox(site + new int3(0, 49, 0), site + new int3(7, 52, 7), Materials.Stone);
            byte[] d1b = Delta(dirB, c, live2, pool, log.Snapshot, log.Count);
            byte[] referenceB = Reference(c, live2, pool, d1b, log.Count);
            asy.DeltaWritten(c, d1, live2, pool, log.Count);    // (stale bytes, as an older save)
            asy.DeltaWritten(c, d1b, live2, pool, log.Count);
            Assert.AreEqual(1, asy.PendingCount);
            PumpUntilDone(asy);
            Assert.AreEqual(1, asy.Superseded);
            Assert.AreEqual(2, asy.Written);
            CollectionAssert.AreEqual(referenceB, File.ReadAllBytes(pathB), "the superseding save's bytes");

            // A D.1 deleted while pending: no file, no entry.
            asy.DeltaWritten(c, d1b, live2, pool, log.Count);
            asy.DeltaDeleted(c);
            Assert.IsFalse(asy.Has(c));
            asy.CompleteAll();
            Assert.AreEqual(2, asy.Superseded);
            Assert.AreEqual(2, asy.Written);
            Assert.IsFalse(File.Exists(pathB), "a dropped build wrote its file");

            // Five saves in one frame on four scratches: the oldest finishes early; all identical.
            for (int i = 1; i <= 5; i++)
            {
                int3 ci = c + new int3(0, 0, i);
                sync.DeltaWritten(ci, d1b, live2, pool, log.Count);
                asy.DeltaWritten(ci, d1b, live2, pool, log.Count);
            }
            Assert.AreEqual(1, asy.SyncFallbacks);
            Assert.AreEqual(EngineConfig.SAVED_SUMMARY_SCRATCH, asy.ScratchCount);
            Assert.AreEqual(EngineConfig.SAVED_SUMMARY_SCRATCH, asy.PendingCount);
            asy.CompleteAll();
            for (int i = 1; i <= 5; i++)
            {
                int3 ci = c + new int3(0, 0, i);
                CollectionAssert.AreEqual(File.ReadAllBytes(SavedEditSummaries.PathFor(dirA, ci)), File.ReadAllBytes(SavedEditSummaries.PathFor(dirB, ci)), $"chunk {ci}");
            }
            lines.Add($"superseded {asy.Superseded}, written {asy.Written}, sync fallbacks {asy.SyncFallbacks}, scratches {asy.ScratchCount}; " +
                      $"main-thread build ms total: async {asy.BuildMsTotal:F2} vs sync {sync.BuildMsTotal:F2} (test timing, not a frame time)");
        }
        finally
        {
            sync.Dispose(); asy.Dispose(); pool.Dispose();
            Directory.Delete(dirA, true); Directory.Delete(dirB, true);
            Debug.Log("[113] async summary build\n" + string.Join("\n", lines));
        }
    }

    [Test, Timeout(900000)]
    public void EvictWhilePending_LiveSummaryStandsIn_ReloadSeesLive_LandedSwapsInWithoutRedraw()
    {
        const int BUDGET = 3 * 1024 * 1024;
        string dir = TempDir();
        int3 site = Site();
        int3 c = site >> 7, cover = c >> 1;
        var L = ShellLayout.From(QualityPreset.Low);
        int3 camShell = cover + new int3(L[1].Hole + 2, 0, 0);
        float3 cam = new float3(((camShell.x << 8) + 128) * 0.1f, (site.y + 30) * 0.1f, ((camShell.z << 8) + 128) * 0.1f);

        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK * 4);
        var alloc = new ChunkHandleAllocator(4);
        var log = EditOpLog.InMemory(SEED);
        double clock = 0;
        ShellStore.ShadowUploads = true;
        bool fill = _src.ShellFill;
        _src.ShellFill = false;
        var saved = SavedEditSummaries.Open(dir, SEED);
        var s = new ShellStreamer(L, _src, 1, synchronous: true, firstShell: 1, lastShell: 1)
            { Ops = log, SyncChunksPerUpdate = 32, SummaryClock = () => clock, Saved = saved };
        try
        {
            var store = new ChunkStore(pool, alloc);
            var edits = new EditService();
            edits.AttachWorld(store, store, store, null);
            var live = Gen(c, alloc, pool);
            store.InsertChunk(live);
            s.AttachEdits(store);
            int n = 0;
            while (!s.Store(1).Holds(cover)) { s.Update(cam, BUDGET); Assert.Less(++n, 3000, "shell 1 never held the cover"); }
            clock = 10;
            Build(edits, site);
            for (int i = 0; i < 60 && (s.SummariesBuilt == 0 || s.RedrawsPending > 0); i++) s.Update(cam, BUDGET);
            Assert.AreEqual(1, s.SummariesBuilt);
            var liveSum = s.Summaries.All[0];

            // Save (scheduled, not landed), then evict: the live summary stands in.
            byte[] d1 = Delta(dir, c, live, pool, log.Snapshot, log.Count);
            byte[] reference = Reference(c, live, pool, d1, log.Count);
            saved.DeltaWritten(c, d1, live, pool, log.Count);
            Assert.IsTrue(saved.IsPending(c));
            long redraws0 = s.SummaryRedrawsQueued;
            store.EvictChunk(c);
            for (int i = 0; i < 3; i++) s.Update(cam, BUDGET);
            Assert.IsTrue(saved.IsPending(c), "nothing pumped");
            Assert.AreEqual(1, s.SummariesDropped);
            Assert.AreEqual(1, s.SavedHeld, "the stand-in");
            Assert.AreEqual(1, s.SummariesHeld);
            Assert.AreSame(liveSum, s.Summaries.All[0], "the published look while pending == the dropped live summary");
            Assert.AreEqual(redraws0, s.SummaryRedrawsQueued, "the pending window redrew");

            // Reload while still pending (generate + the same edits): its live summary wins.
            var live2 = Gen(c, alloc, pool);
            store.InsertChunk(live2);
            clock = 20;
            Build(edits, site);
            for (int i = 0; i < 60 && (s.SummariesBuilt < 2 || s.RedrawsPending > 0); i++) s.Update(cam, BUDGET);
            Assert.AreEqual(2, s.SummariesBuilt);
            Assert.AreEqual(0, s.SavedHeld, "the stand-in outlived the reload's live summary");
            Assert.AreEqual(1, s.SummariesHeld);
            var liveSum2 = s.Summaries.All[0];
            Assert.AreEqual(int.MaxValue, liveSum2.OpsBefore, "live");
            Assert.IsTrue(liveSum2.SameLook(liveSum));

            // It lands: written, == the hand build; the shells keep the live one while resident.
            PumpUntilDone(saved);
            CollectionAssert.AreEqual(reference, File.ReadAllBytes(SavedEditSummaries.PathFor(dir, c)), "landed bytes");
            s.Update(cam, BUDGET);
            Assert.AreSame(liveSum2, s.Summaries.All[0]);

            // Evicted again (unchanged since that save): the landed summary swaps in, no redraw.
            long redraws1 = s.SummaryRedrawsQueued;
            store.EvictChunk(c);
            s.Update(cam, BUDGET);
            Assert.AreEqual(1, s.SavedHeld);
            Assert.AreEqual(1, s.SummariesHeld);
            Assert.AreNotEqual(int.MaxValue, s.Summaries.All[0].OpsBefore, "the saved summary");
            Assert.IsTrue(s.Summaries.All[0].SameLook(liveSum2));
            Assert.AreEqual(redraws1, s.SummaryRedrawsQueued, "the landed summary redrew the same look");
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            _src.ShellFill = fill;
            s.Dispose(); saved.Dispose(); pool.Dispose();
            Directory.Delete(dir, true);
        }
    }
}
