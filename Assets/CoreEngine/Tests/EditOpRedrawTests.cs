// Assets/CoreEngine/Tests/EditOpRedrawTests.cs
//
// Engine step 13b (task 82), the plan's E5 tests, part b: an edit op recorded
// AFTER the shells and the far band hold their data is redrawn at once
// (Amendment 8.14 rows 9 and 14).
//   Replace     ShellStore's gap-free re-store: the old copy stays on the GPU
//               until the new one is complete, its page and bodies are freed
//               only after the next directory flush; no room for both ->
//               Refused + ReplaceNoRoom (not a refusal), the old copy kept.
//   Shells 1-7  a High layout filled around the spawn, then ops recorded in a
//               held chunk of every shell -> every touched stored chunk's
//               uploaded shadow == a fresh GenerateShellChunk with the ops
//               (0 differ), stale slots 0, pool books balanced. Then the camera
//               moves and an op lands on a chunk still generating -> caught up
//               on arrival (the same bytes).
//   Far band    a FarField filled, then a crater, a mound and a carve under the
//               sea recorded -> after the patches go up, every texel of every
//               level == a fresh FarField built with the ops; the crater is
//               lower, the mound higher, the sea deeper (2.5D: what it can show).
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class EditOpRedrawTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }
    [OneTimeTearDown] public void TearDown() { _src?.Dispose(); _src = null; }

    /// A dry land column (fine voxel x, z) near `near` with its ground height.
    private static bool Land(float2 near, float spreadVoxels, out int3 site)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        for (int i = 0; i < 400; i++)
        {
            float2 p = near + new float2((i % 20) - 10, (i / 20) - 10) * spreadVoxels;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 300 || col.water > col.height) continue;
            site = new int3((int)p.x, col.height, (int)p.y);
            return true;
        }
        site = default;
        return false;
    }

    private static TerrainGen.ColumnSample Column(int x, int z)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        return TerrainGen.ShapeField.Sample(in spec, in view, in o, x, z, 1);
    }

    // ==================================================================
    // ShellStore replace
    // ==================================================================

    [Test]
    public void ShellStore_Replace_KeepsTheOldCopyUntilComplete_DefersFrees_NoRoomKeepsOld()
    {
        Assert.IsTrue(Land(new float2(_src.Spec.worldSizeVoxels * 0.5f), 1500f, out int3 s));
        const int K = 3;
        int3 cs = s >> (7 + K);
        ShellStore.ShadowUploads = true;
        var a = new TerrainGenChunkSource.ShellChunk();
        var b = new TerrainGenChunkSource.ShellChunk();
        ShellStore st = null, tight = null;
        try
        {
            _src.GenerateShellChunk(K, cs, a);
            var log = EditOpLog.InMemory(42u);
            log.Append(new EditOp(EditOpKind.Sphere, s, 400, Materials.Air));
            _src.GenerateShellChunk(K, cs, b, log.Snapshot);
            Assert.Greater(a.DenseBricks, 0, "the site's chunk is uniform: pick another");
            ShellStore.UploadBytes(b, out int denseB, out _, out _);

            st = new ShellStore(K, new int3(8, 8, 8), a.DenseBricks + denseB + 8, 4);
            int big = 64 << 20;
            Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(a, ref big));
            Assert.IsTrue(st.FlushDirectory(ref big));
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, a, out string ex), ex);

            // Replace over several small budgets: the old copy is what the directory shows meanwhile.
            int budget = 64 * 1024;
            Assert.AreEqual(ShellStore.StoreResult.InProgress, st.Store(b, ref budget, replace: true));
            int frames = 1;
            while (true)
            {
                budget = 64 * 1024;
                Assert.IsTrue(st.FlushDirectory(ref budget));
                Assert.AreEqual(0, ShellValidator.CompareShadow(st, a, out ex), "mid-replace: " + ex);
                Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ex), ex);
                if (st.Continue(ref budget)) break;
                Assert.Less(++frames, 1000);
            }
            Assert.AreEqual(1, st.DeferredPages, "the old page is deferred until the directory goes up");
            Assert.AreEqual(a.DenseBricks, st.DeferredBricks);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ex), ex);
            budget = big;
            Assert.IsTrue(st.FlushDirectory(ref budget));
            Assert.AreEqual(0, st.DeferredPages);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, b, out ex), ex);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ex), ex);
            Assert.AreEqual(1, st.Replaced);
            Assert.AreEqual(1, st.ChunksStored);

            // No room for both copies: refused as a replace, the old copy stays.
            tight = new ShellStore(K, new int3(8, 8, 8), a.DenseBricks + 1, 4);
            budget = big;
            Assert.AreEqual(ShellStore.StoreResult.Stored, tight.Store(a, ref budget));
            Assert.IsTrue(tight.FlushDirectory(ref budget));
            Assert.AreEqual(ShellStore.StoreResult.Refused, tight.Store(b, ref budget, replace: true));
            Assert.AreEqual(1, tight.ReplaceNoRoom);
            Assert.AreEqual(0, tight.Refusals);
            Assert.AreEqual(0, ShellValidator.CompareShadow(tight, a, out ex), ex);
            Debug.Log($"[82] replace: shell {K} chunk {cs}, {a.DenseBricks} -> {denseB} dense bricks, {frames} budget slices of 64 KB; no-room case kept the old copy");
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            st?.Dispose(); tight?.Dispose(); a.Dispose(); b.Dispose();
        }
    }

    // ==================================================================
    // Shells 1-7 redraw
    // ==================================================================

    private static float3 Spawn()
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 2000; i++)
        {
            float a = i * 2.39996f, r = 40f * i;
            float2 p = new float2(c + r * math.cos(a), c + r * math.sin(a));
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 200 || col.water > col.height) continue;
            return new float3(p.x * 0.1f, (col.height + 1) * 0.1f + 2f, p.y * 0.1f);
        }
        return new float3(c * 0.1f, (spec.seaLevelY + 800) * 0.1f, c * 0.1f);
    }

    /// A ground point inside a chunk shell k holds (its column's ground lies in
    /// the chunk): dry land first, else a sea or lake bed.
    private static bool SiteInHeldChunk(ShellStreamer s, int k, out int3 site)
        => SiteInHeldChunk(s, k, true, out site) || SiteInHeldChunk(s, k, false, out site);

    private static bool SiteInHeldChunk(ShellStreamer s, int k, bool dry, out int3 site)
    {
        var st = s.Store(k);
        s.Box(k, out int3 lo, out int3 hi, out _);
        int sh = 7 + k;
        for (int z = lo.z; z <= hi.z; z++)
        for (int x = lo.x; x <= hi.x; x++)
        for (int y = lo.y; y <= hi.y; y++)
        {
            var c = new int3(x, y, z);
            if (!st.Holds(c)) continue;
            int px = (x << sh) + (64 << k), pz = (z << sh) + (64 << k);
            var col = Column(px, pz);
            if ((dry && (col.water > col.height || col.height < _src.Spec.seaLevelY)) || (col.height >> sh) != y) continue;
            site = new int3(px, col.height, pz);
            return true;
        }
        site = default;
        return false;
    }

    private static void Fill(ShellStreamer s, float3 cam, int budget, string what)
    {
        int frames = 0;
        do
        {
            s.Update(cam, budget);
            Assert.LessOrEqual(s.LastBytes, budget, $"{what}: over the budget");
            Assert.Less(++frames, 20000, $"{what}: the shells never went idle");
        } while (!s.Idle);
    }

    /// Every stored chunk an op of `snap` touches: shadow vs a fresh GenerateShellChunk with the ops.
    private static int CheckTouched(ShellStreamer s, EditOpSnapshot snap, int[] perShell, out int differing, out string first)
    {
        int touched = 0; differing = 0; first = null;
        using (var fresh = new TerrainGenChunkSource.ShellChunk())
            for (int k = s.FirstShell; k <= s.LastShell; k++)
            {
                var st = s.Store(k);
                for (int slot = 0; slot < st.SlotCount; slot++)
                {
                    if (!st.SlotTagged(slot, out int3 c)) continue;
                    bool hit = false;
                    foreach (var op in snap.Ops) if (EditOpApply.TouchesChunk(op, c, k)) { hit = true; break; }
                    if (!hit) continue;
                    touched++; if (perShell != null) perShell[k]++;
                    _src.GenerateShellChunk(k, c, fresh, snap);
                    int d = ShellValidator.CompareShadow(st, fresh, out string ex);
                    if (d != 0) { differing++; if (first == null) first = $"shell {k} chunk {c}: {d} voxels, {ex}"; }
                }
            }
        return touched;
    }

    [Test, Category("Slow"), Timeout(1200000)]   // the High fill alone is ~190 s
    public void Shells1to7_OpRecordedAfterTheFill_RedrawsEveryTouchedChunk_ByteExact_StaleFree()
    {
        const int BUDGET = 3 * 1024 * 1024;
        ShellStore.ShadowUploads = true;
        var L = ShellLayout.From(QualityPreset.High);
        var log = EditOpLog.InMemory(42u);
        var s = new ShellStreamer(L, _src, 1, synchronous: true) { Ops = log, SyncChunksPerUpdate = 32 };
        var lines = new List<string>();
        try
        {
            float3 cam = Spawn();
            var t = System.Diagnostics.Stopwatch.StartNew();
            Fill(s, cam, BUDGET, "fill");
            lines.Add($"fill: camera {cam} m, {t.Elapsed.TotalSeconds:F0} s");

            // One carve + one fill in a held chunk of every shell.
            for (int k = s.FirstShell; k <= s.LastShell; k++)
            {
                if (!SiteInHeldChunk(s, k, out int3 site)) { lines.Add($"shell {k}: no held ground chunk"); continue; }
                int r = math.max(40, 3 << k);
                log.Append(new EditOp(EditOpKind.Sphere, site, (ushort)r, Materials.Air));
                log.Append(new EditOp(EditOpKind.Box, site + new int3(2 * r, 0, 0), (ushort)(r / 2), Materials.Stone));
            }
            var snap = log.Snapshot;
            Assert.Greater(snap.Count, 0, "no shell holds a ground chunk");
            CheckTouched(s, snap, null, out int staleBefore, out _);
            Assert.Greater(staleBefore, 0, "the ops change no stored chunk: nothing to redraw");

            long gen0 = s.Generated;
            int frames = 0;
            do { s.Update(cam, BUDGET); frames++; Assert.Less(frames, 5000); } while (!s.Idle || s.RedrawsPending > 0);
            var per = new int[8];
            int touched = CheckTouched(s, snap, per, out int differ, out string first);
            lines.Add($"op redraw: {snap.Count} ops, {s.RedrawsQueued} chunks queued, {s.Redrawn} redrawn in {frames} updates " +
                      $"({s.Generated - gen0} generated); {touched} touched stored chunks ({staleBefore} differed before), {differ} differ after");
            lines.Add($"  touched per shell 1-7: {per[1]} {per[2]} {per[3]} {per[4]} {per[5]} {per[6]} {per[7]}");
            Assert.AreEqual(0, differ, first);
            for (int k = s.FirstShell; k <= s.LastShell; k++)
            {
                Assert.AreEqual(0, ShellValidator.CountStaleSlots(s, k, out string ex), ex);
                Assert.AreEqual(0, ShellValidator.CountPoolErrors(s.Store(k), out ex), ex);
                lines.Add($"  shell {k}: stored {s.Store(k).ChunksStored}, replaced {s.Store(k).Replaced}, no room {s.Store(k).ReplaceNoRoom}, stale 0");
            }

            // An op on chunks still GENERATING: caught up on arrival.
            cam += new float3(60f, 0f, 0f);   // shells 1-2 slide a column: a few new chunks
            s.Update(cam, BUDGET);
            int added = 0;
            for (int k = s.FirstShell; k <= s.LastShell && added < 4; k++)
            {
                s.Box(k, out int3 lo, out int3 hi, out _);
                for (int z = lo.z; z <= hi.z && added < 4; z++)
                for (int y = lo.y; y <= hi.y && added < 4; y++)
                for (int x = lo.x; x <= hi.x && added < 4; x++)
                {
                    var c = new int3(x, y, z);
                    if (!s.IsInFlight(k, c)) continue;
                    int3 centre = (c << (7 + k)) + (64 << k);
                    log.Append(new EditOp(EditOpKind.Box, centre, (ushort)(16 << k), Materials.Stone));
                    added++;
                }
            }
            Assert.Greater(added, 0, "nothing in flight after the move");
            long caught0 = s.OpsCaughtUp;
            Fill(s, cam, BUDGET, "after the move");
            snap = log.Snapshot;
            touched = CheckTouched(s, snap, null, out differ, out first);
            lines.Add($"in flight: {added} ops on generating chunks, {s.OpsCaughtUp - caught0} chunks caught up on arrival; {touched} touched stored chunks, {differ} differ");
            Assert.Greater(s.OpsCaughtUp - caught0, 0, "no chunk was caught up");
            Assert.AreEqual(0, differ, first);
            for (int k = s.FirstShell; k <= s.LastShell; k++)
            {
                Assert.AreEqual(0, ShellValidator.CountStaleSlots(s, k, out string ex), ex);
                Assert.AreEqual(0, ShellValidator.CountPoolErrors(s.Store(k), out ex), ex);
            }
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            s.Dispose();
            Debug.Log("[82] shells\n" + string.Join("\n", lines));
        }
    }

    // ==================================================================
    // Far band
    // ==================================================================

    private static FarField Far(Vector3 cam, EditOpLog ops)
    {
        FarField.ShadowTexels = true;
        try { return new FarField(new WorldMetaData { seed = 42, sizeClass = 2 }, cam, _src.Map, ops); }
        finally { FarField.ShadowTexels = false; }
    }

    [Test]
    public void FarBand_OpRecordedAfterTheFill_PatchedTexels_EqualAFreshBuildWithTheOp()
    {
        var spec = _src.Spec;
        Assert.IsTrue(Land(new float2(spec.worldSizeVoxels * 0.5f), 1500f, out int3 crater));
        Assert.IsTrue(Land(new float2(crater.x + 1500, crater.z), 200f, out int3 mound));
        // Shallow sea (depth < 12 m) within 6 km of the crater: walk out along 16 headings.
        int3 sea = default; bool found = false;
        for (int h = 0; h < 16 && !found; h++)
            for (int r = 2000; r <= 60000 && !found; r += 250)
            {
                float a = h * math.PI / 8f;
                int x = crater.x + (int)(math.sin(a) * r), z = crater.z + (int)(math.cos(a) * r);
                var col = Column(x, z);
                if (col.height < spec.seaLevelY && spec.seaLevelY - col.height < 100 && col.height > spec.seaLevelY - 100)
                { sea = new int3(x, col.height, z); found = true; }
            }
        var ops = new[]
        {
            new EditOp(EditOpKind.Sphere, crater, 200, Materials.Air),                     // a 20 m crater
            new EditOp(EditOpKind.Box, mound + new int3(0, 20, 0), 30, Materials.Stone),   // a 6 m mound
            new EditOp(EditOpKind.Sphere, sea, 80, Materials.Air),                         // a pit in the sea bed
        };
        int nOps = found ? 3 : 2;
        var cam = new Vector3(crater.x * 0.1f, crater.y * 0.1f + 100f, crater.z * 0.1f);

        var logA = EditOpLog.InMemory(42u);
        var logB = EditOpLog.InMemory(42u);
        for (int i = 0; i < nOps; i++) logB.Append(ops[i]);
        FarField plain = null, A = null, B = null;
        try
        {
            plain = Far(cam, null);
            A = Far(cam, logA);
            for (int i = 0; i < nOps; i++) logA.Append(ops[i]);   // AFTER A's fill
            int updates = 0;
            do
            {
                A.Update(cam);
                A.PendingScatters.Clear();   // the render pass's FarScatter (Shadow already has it)
                if (A.PatchesPending > 0) System.Threading.Thread.Sleep(1);
                Assert.Less(++updates, 5000, "the patches never went up");
            } while (A.PatchesPending > 0);
            B = Far(cam, logB);

            var lines = new List<string> { $"far band: crater {crater}, mound {mound}, sea pit {(found ? sea.ToString() : "none found")}; {A.PatchesApplied} patches ({A.PatchTexels} texels) in {updates} updates" };
            int totalDiff = 0, lowered0 = 0, raised0 = 0, deeper = 0;
            for (int L = 0; L < FarField.LEVELS; L++)
            {
                int diff = 0, lowered = 0, raised = 0, deep = 0;
                for (int i = L * FarField.LEVEL_TEXELS; i < (L + 1) * FarField.LEVEL_TEXELS; i++)
                {
                    if (A.Shadow[i] != B.Shadow[i]) diff++;
                    uint p = plain.Shadow[i], e = B.Shadow[i];
                    if (p == e) continue;
                    int hp = (int)(p & 0xFFFF), he = (int)(e & 0xFFFF);
                    if (he < hp) lowered++;
                    if (he > hp) raised++;
                    if (((p >> 24) & 1) != 0 && ((e >> 24) & 1) != 0 && (e >> 25) > (p >> 25)) deep++;
                }
                totalDiff += diff;
                if (L == 0) { lowered0 = lowered; raised0 = raised; }
                deeper += deep;
                lines.Add($"  level {L} ({FarField.TexelVoxels(L) * 0.1f:F1} m texels): {diff} differ from the fresh build; vs no ops {lowered} lower, {raised} higher, {deep} deeper water");
            }
            // The crater's centre texel at level 0: its top is the sphere's bottom.
            FarField.OpTexelRect(ops[0], 0, out int2 t0, out int2 t1);
            int2 tc = (t0 + t1) >> 1;
            uint ct = B.Shadow[((tc.y & FarField.EDGE_MASK) << 10) + (tc.x & FarField.EDGE_MASK)];
            int px = (tc.x << 3) + 4, pz = (tc.y << 3) + 4;
            FarField.OpColumnSpan(ops[0], px, pz, out int ylo, out _);
            lines.Add($"  crater centre texel {tc}: top {ct & 0xFFFF} (op bottom {ylo}, ground was {crater.y + 1}), material {(ct >> 16) & 0xFF}");
            Debug.Log("[82] " + string.Join("\n", lines));
            Assert.AreEqual(0, totalDiff, "patched far band != a fresh build with the ops");
            Assert.Greater(lowered0, 0, "the crater lowers no level-0 texel");
            Assert.Greater(raised0, 0, "the mound raises no level-0 texel");
            if (found) Assert.Greater(deeper, 0, "the sea pit deepens no water texel");
            Assert.AreEqual(ylo, (int)(ct & 0xFFFF));
        }
        finally
        {
            plain?.Dispose(); A?.Dispose(); B?.Dispose();
        }
    }

    /// The 2.5D rules on hand-made texels (header EDITS of FarField).
    [Test]
    public void FarBand_ApplyOp_WhatTwoAndAHalfDCanShow()
    {
        uint land = 100u | ((uint)Materials.Grass << 16);                                                  // first air y 100
        uint water = 100u | ((uint)Materials.Water << 16) | (FarField.FLAG_WATER << 24) | FarField.DepthBits(20);   // bed first air 80
        var carve = new EditOp(EditOpKind.Box, new int3(0, 95, 0), 10, Materials.Air);   // y 85..105
        var tunnel = new EditOp(EditOpKind.Box, new int3(0, 60, 0), 10, Materials.Air);  // y 50..70
        var mound = new EditOp(EditOpKind.Box, new int3(0, 105, 0), 10, Materials.Stone); // y 95..115
        var blob = new EditOp(EditOpKind.Box, new int3(0, 150, 0), 10, Materials.Stone);  // y 140..160
        var pit = new EditOp(EditOpKind.Box, new int3(0, 75, 0), 10, Materials.Air);     // y 65..85
        var isle = new EditOp(EditOpKind.Box, new int3(0, 95, 0), 20, Materials.Stone);   // y 75..115

        uint Ap(uint t, EditOp op, byte floor = 7)
        {
            Assert.IsTrue(FarField.OpColumnSpan(op, 0, 0, out int lo, out int hi));
            return FarField.ApplyOp(t, op, lo, hi, floor);
        }
        Assert.AreEqual(85u | (7u << 16), Ap(land, carve), "crater: top drops to the carve's bottom, floor material");
        Assert.AreEqual(land, Ap(land, tunnel), "tunnel: invisible in 2.5D");
        Assert.AreEqual(116u | ((uint)Materials.Stone << 16), Ap(land, mound), "mound: top rises to the fill's top");
        Assert.AreEqual(land, Ap(land, blob), "floating blob: invisible in 2.5D");
        uint w2 = Ap(water, pit);
        Assert.AreEqual(water & 0x01FFFFFFu, w2 & 0x01FFFFFFu, "water surface kept");
        Assert.AreEqual(35u, w2 >> 25, "the pit under water: depth 100 - 65");
        Assert.AreEqual(116u | ((uint)Materials.Stone << 16), Ap(water, isle), "an island out of the water: dry land");
        Assert.IsFalse(FarField.OpColumnSpan(new EditOp(EditOpKind.Sphere, new int3(30, 0, 0), 20, Materials.Air), 0, 0, out _, out _));
        Assert.IsTrue(FarField.OpColumnSpan(new EditOp(EditOpKind.Sphere, new int3(12, 50, 0), 20, Materials.Air), 0, 0, out int a, out int b));
        Assert.AreEqual(50 - 16, a); Assert.AreEqual(50 + 16, b);   // isqrt(400 - 144) = 16
    }
}
