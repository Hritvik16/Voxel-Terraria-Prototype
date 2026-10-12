// Assets/CoreEngine/Tests/ShellStorageTests.cs
//
// Engine step 11a's gate (task 77; Amendment 8.14 rows 1-3, 14, 15, §6's
// "pow2 rings alias" row, ENGINE_PHASE_PLAN.md row 11):
//   Layout     ShellLayout.From(preset) for every preset: pow2 rings (asserted
//              in From and here), §H.2's rows, §2a's High table, the caps.
//   Ledger     memory per shell and per preset at the caps vs §H.2's estimates
//              (writes Logs/Shells/ledger.txt).
//   Store      handcrafted chunks: byte-exact upload (the CPU shadow of every
//              GPU write), budget, refusal without a throw (row 15), a ring
//              alias (c and c + ring share a slot) evicts cleanly.
//   Streamer   (Slow) the real generator, High shells 6-7 around seed 42's
//              spawn, synchronous: stale slots 0, pool errors 0, uploads
//              byte-exact vs a fresh GenerateShellChunk, every frame inside its
//              budget; then the camera moves 1 km and the same holds. Writes
//              Screenshots/77/shells_occupancy_*.png and Logs/Shells/fill.txt.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class ShellStorageTests
{
    // ------------------------------------------------------------------ layout
    [Test]
    public void Layout_EveryPreset_Pow2Rings_AndTheAmendmentsRows()
    {
        int[] rows = { 480, 540, 720, 1080 }, shells = { 6, 7, 8, 7 }, load = { 5, 6, 7, 10 };
        float[] r0 = { 59, 65, 82, 116 };
        int[] capK = { 288, 475, 665, 1158 };
        for (int p = 0; p < 4; p++)
        {
            var L = ShellLayout.From((QualityPreset)p);
            Assert.AreEqual(rows[p], L.SizingRows, $"{L.Preset} rows");
            Assert.AreEqual(shells[p], L.ShellCount, $"{L.Preset} shells");
            Assert.AreEqual(load[p], L.LoadRadiusChunks, $"{L.Preset} load radius");
            Assert.AreEqual(r0[p], L.Shell0RadiusM, 0.5f, $"{L.Preset} 0.1 m window radius");
            for (int k = 0; k < L.ShellCount; k++)
            {
                var s = L[k];
                Assert.IsTrue(ShellLayout.IsPow2(s.Ring.x) && ShellLayout.IsPow2(s.Ring.y) && ShellLayout.IsPow2(s.Ring.z), $"{L.Preset} shell {k} ring {s.Ring}");
                if (k == 0) continue;
                Assert.GreaterOrEqual(s.Ring.x, 2 * s.Reach + 1, $"{L.Preset} shell {k}: +-{s.Reach} must fit the ring");
                Assert.AreEqual(s.InnerM, L[k - 1].OuterM, 1e-3f, $"{L.Preset} shell {k} starts where {k - 1} ends");
                Assert.AreEqual(0.1f * (1 << k), s.VoxelM, 1e-5f);
            }
            float tol = p == 3 ? 0.02f : 0.005f;   // Ultra: §H.2's shell 6 world cut differs (header)
            Assert.AreEqual(capK[p] * 1000, L.ShellsPoolCapTotal, capK[p] * 1000 * tol, $"{L.Preset} pool caps, shells 1+ (§H.2)");
        }
        // §2a, High, shell by shell.
        var H = ShellLayout.From(QualityPreset.High);
        int[] ringY = { 16, 16, 16, 8, 4, 4, 2 }, cols = { 160, 160, 160, 160, 160, 112, 27 };
        int[] bricksK = { 120, 95, 82, 82, 82, 57, 14 };
        float[] outer = { 138, 275, 550, 1100, 2200, 4400, 6600 };
        for (int k = 1; k < 8; k++)
        {
            Assert.AreEqual(new int3(16, ringY[k - 1], 16), H[k].Ring, $"High shell {k} ring");
            Assert.AreEqual(cols[k - 1], H[k].Columns, $"High shell {k} columns");
            Assert.AreEqual(bricksK[k - 1] * 1000, H[k].EstBricks, 1000, $"High shell {k} bricks (est.)");
            Assert.AreEqual(outer[k - 1], H[k].OuterM, 1f, $"High shell {k} hand-over");
        }
        Assert.AreEqual(QualityPreset.High, ShellLayout.Parse(null));
        Assert.AreEqual(QualityPreset.Ultra, ShellLayout.Parse("Ultra"));
    }

    // ------------------------------------------------------------------ ledger
    [Test]
    public void Ledger_PerPreset_AtTheCaps_VsTheAmendmentsEstimates()
    {
        // §H.2: shells 1+ (GPU only) and terrain total (shell 0 + shells + 102 MB far band/ChunkStore).
        float[] estShells = { 169, 277, 388, 677 }, estShell0 = { 255, 305, 364, 718 }, estTotal = { 526, 684, 855, 1497 };
        var lines = new List<string>
        {
            "Task 77 (engine step 11a): shell memory at the caps, per preset, vs Amendment 8.14 §H.2 (MB = 1e6 bytes).",
            "formula = 8.14 §2's MB column (bodies 512 B + 64 B occupancy per brick (est.) + directory); allocated = what 11a's",
            "ShellStore allocates on the GPU today (bodies + 16 KB pages + directory; occupancy/air mips come with 11b).",
            "",
        };
        for (int p = 0; p < 4; p++)
        {
            var L = ShellLayout.From((QualityPreset)p);
            double f = 0, a = 0;
            lines.Add($"{L.Preset} ({L.SizingRows} rows, {L.ShellCount} shells, 0.1 m window {L.Shell0RadiusM:F0} m, load radius {L.LoadRadiusChunks}):");
            for (int k = 1; k < L.ShellCount; k++)
            {
                var s = L[k];
                f += s.BytesAmendmentFormula / 1e6; a += s.BytesAllocated / 1e6;
                lines.Add($"  shell {k}  {s.VoxelM,5:0.0#} m  {s.InnerM,6:F0}-{s.OuterM,6:F0} m  ring {s.Ring.x}x{s.Ring.y}x{s.Ring.z}  hole +-{s.Hole}  cols {s.Columns,4}  " +
                          $"bricks est {s.EstBricks,7} cap {s.PoolCap,7}  pages {s.PageCap,4}  formula {s.BytesAmendmentFormula / 1e6,6:F1} MB  allocated {s.BytesAllocated / 1e6,6:F1} MB");
            }
            double total = estShell0[p] + f + 102;
            lines.Add($"  shells 1+: formula {f:F0} MB vs §H.2 {estShells[p]:F0} MB ({(f / estShells[p] - 1) * 100:+0.0;-0.0}%); allocated {a:F0} MB" +
                      $"  |  terrain total with §H.2's shell 0 + 102 MB: {total:F0} MB vs §H.2 {estTotal[p]:F0} MB");
            Assert.AreEqual(estShells[p], f, estShells[p] * 0.03, $"{L.Preset}: shells 1+ memory vs §H.2");
            Assert.AreEqual(estTotal[p], total, estTotal[p] * 0.03, $"{L.Preset}: terrain total vs §H.2");
        }
        // Allocate High for real once: the store's own byte count must equal the layout's.
        var Hl = ShellLayout.From(QualityPreset.High);
        long got = 0, want = 0;
        for (int k = 1; k < Hl.ShellCount; k++)
        {
            var s = Hl[k];
            using (var st = new ShellStore(k, s.Ring, s.PoolCap, s.PageCap)) got += st.BytesAllocated;
            want += s.BytesAllocated;
        }
        lines.Add("");
        lines.Add($"High allocated for real (ShellStore x 7): {got / 1e6:F1} MB GPU (layout says {want / 1e6:F1} MB).");
        Assert.AreEqual(want, got);
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "ledger.txt"), lines);
        UnityEngine.Debug.Log("[77]\n" + string.Join("\n", lines));
    }

    // ------------------------------------------------------------------ store
    /// A handcrafted shell chunk: `dense` non-uniform bricks (a voxel pattern
    /// from the coordinate), the rest uniform `fill`.
    private static TerrainGenChunkSource.ShellChunk Craft(int shell, int3 coord, int dense, byte fill)
    {
        var sc = new TerrainGenChunkSource.ShellChunk { Shell = shell, Coord = coord, IsUniformAir = false };
        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
        {
            bool d = bi < dense;
            sc.BrickUniform[bi] = !d; sc.BrickMat[bi] = d ? (byte)1 : fill;
            int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
            for (int vz = 0; vz < 8; vz++) for (int vy = 0; vy < 8; vy++) for (int vx = 0; vx < 8; vx++)
            {
                int x = (bx << 3) + vx, y = (by << 3) + vy, z = (bz << 3) + vz;
                sc.Mats[TerrainGenChunkSource.Index(x, y, z)] = d ? (byte)(1 + ((x * 7 + y * 13 + z * 3 + coord.x * 5 + coord.z) % 200)) : fill;
            }
        }
        return sc;
    }

    [Test]
    public void Store_UploadIsByteExact_Budgeted_RefusesWhenFull_AndRingAliasEvicts()
    {
        ShellStore.ShadowUploads = true;
        var ring = new int3(4, 2, 4);
        var st = new ShellStore(3, ring, poolCap: 100, pageCap: 3);
        var a = Craft(3, new int3(1, 0, 1), 40, 20);
        var b = Craft(3, new int3(2, 1, 1), 50, 0);
        var big = Craft(3, new int3(3, 0, 3), 60, 0);
        var uni = Craft(3, new int3(0, 0, 0), 0, 25);
        try
        {
            // Budget under one brick: not started, nothing allocated.
            ShellStore.UploadBytes(a, out int dense, out _, out _);
            Assert.AreEqual(40, dense);
            int budget = 511;
            Assert.AreEqual(ShellStore.StoreResult.OverBudget, st.Store(a, ref budget));
            Assert.AreEqual(511, budget); Assert.AreEqual(0, st.BricksUsed);
            // A chunk bigger than the frame's budget goes up over several frames
            // (row 14); its directory entry appears only when all of it is up.
            budget = 10 * 512;
            Assert.AreEqual(ShellStore.StoreResult.InProgress, st.Store(a, ref budget));
            Assert.AreEqual(0, budget); Assert.AreEqual(40, st.BricksUsed); Assert.IsFalse(st.Holds(a.Coord));
            budget = 1 << 20;
            Assert.AreEqual(ShellStore.StoreResult.Busy, st.Store(b, ref budget), "one upload in progress per shell");
            budget = 30 * 512 + 100;
            Assert.IsFalse(st.Continue(ref budget), "the 16 KB page does not fit the 100 B left");
            Assert.AreEqual(100, budget); Assert.IsFalse(st.Holds(a.Coord));
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out string ep0), ep0);
            budget = 16384;
            Assert.IsTrue(st.Continue(ref budget)); Assert.AreEqual(0, budget); Assert.IsTrue(st.Holds(a.Coord));
            budget = 3 * 1024 * 1024;
            Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(b, ref budget));
            Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(uni, ref budget));
            Assert.IsTrue(st.FlushDirectory(ref budget));
            Assert.AreEqual(3 * 1024 * 1024 - (50 * 512 + 16384) - 4 - st.SlotCount * 4, budget, "every byte charged to the budget");
            Assert.AreEqual(90, st.BricksUsed); Assert.AreEqual(2, st.PagesUsed); Assert.AreEqual(1, st.UniformChunks);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, a, out string ea), ea);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, b, out string eb), eb);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, uni, out string eu), eu);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out string ep), ep);

            // Full pool: refused and counted, no throw, nothing changed (row 15).
            Assert.AreEqual(ShellStore.StoreResult.Refused, st.Store(big, ref budget));
            Assert.AreEqual(1, st.Refusals); Assert.AreEqual(90, st.BricksUsed);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ep), ep);

            // Ring alias: a's coordinate + one ring length lands in a's slot;
            // storing it evicts a, frees its 40 bricks and re-tags the slot.
            var alias = Craft(3, a.Coord + new int3(ring.x, 0, 0), 30, 0);
            try
            {
                Assert.AreEqual(st.Slot(a.Coord), st.Slot(alias.Coord));
                Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(alias, ref budget));
                Assert.IsFalse(st.Holds(a.Coord)); Assert.IsTrue(st.Holds(alias.Coord));
                Assert.AreEqual(80, st.BricksUsed);
                Assert.IsTrue(st.FlushDirectory(ref budget));
                Assert.AreEqual(0, ShellValidator.CompareShadow(st, alias, out string ex), ex);
                Assert.AreEqual(-1, ShellValidator.CompareShadow(st, a, out _));
                Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ep), ep);
                // Now the big one fits after evicting b.
                st.Evict(b.Coord);
                Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(big, ref budget));
                Assert.IsTrue(st.FlushDirectory(ref budget));
                Assert.AreEqual(0, ShellValidator.CompareShadow(st, big, out ex), ex);
                Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ep), ep);
            }
            finally { alias.Dispose(); }
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            a.Dispose(); b.Dispose(); big.Dispose(); uni.Dispose(); st.Dispose();
        }
    }

    // ------------------------------------------------------------------ streamer
    /// Phase4Bootstrapper.Tg2Spawn's spiral (the test assembly cannot see Game):
    /// the first dry column >= 20 m above the sea, eye 2 m above its ground.
    private static float3 Spawn(TerrainGenChunkSource src)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
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

    [Test, Category("Slow")]
    public void Streamer_FillsHighShells6and7_AroundSpawn_StaleFree_ByteExact_InBudget()
    {
        const int BUDGET = 1024 * 1024;   // a third of the 3 MiB cap: what tier 0 might leave
        ShellStore.ShadowUploads = true;
        var src = new TerrainGenChunkSource(42u);
        var L = ShellLayout.From(QualityPreset.High);
        var s = new ShellStreamer(L, src, 1, synchronous: true, firstShell: 6, lastShell: 7);
        var lines = new List<string>();
        try
        {
            float3 cam = Spawn(src);
            for (int leg = 0; leg < 2; leg++)
            {
                if (leg == 1) cam += new float3(1000f, 0f, 300f);   // > one shell-6 chunk (819 m)
                int frames = 0, maxBytes = 0;
                var t = System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    s.Update(cam, BUDGET);
                    maxBytes = math.max(maxBytes, s.LastBytes);
                    Assert.LessOrEqual(s.LastBytes, BUDGET, $"leg {leg} frame {frames}: uploaded over the budget");
                    Assert.Less(++frames, 4000, "the shells never went idle");
                } while (!s.Idle);
                lines.Add($"leg {leg}: camera {cam} m, {frames} updates, {t.Elapsed.TotalSeconds:F0} s, peak {maxBytes} B/update (budget {BUDGET})");
                for (int k = 6; k <= 7; k++)
                {
                    int stale = ShellValidator.CountStaleSlots(s, k, out string ex);
                    Assert.AreEqual(0, stale, ex);
                    Assert.AreEqual(0, ShellValidator.CountPoolErrors(s.Store(k), out ex), ex);
                    Assert.Greater(s.Store(k).ChunksStored, 0, $"shell {k} stored nothing");
                    // Every wanted chunk is stored or refused (a full pool, row 15:
                    // reported, not asserted -- the caps are 8.14's estimates);
                    // check three stored ones voxel for voxel.
                    s.Box(k, out int3 lo, out int3 hi, out int3 cc);
                    int wanted = 0, checkedN = 0;
                    using (var fresh = new TerrainGenChunkSource.ShellChunk())
                        for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
                        {
                            var c = new int3(x, y, z);
                            if (!s.Wanted(k, c)) continue;
                            wanted++;
                            Assert.IsTrue(s.Store(k).Holds(c) || s.IsRefused(k, c), $"shell {k} chunk {c} wanted but neither stored nor refused");
                            if (checkedN < 3 && s.Store(k).Holds(c) && ((x + z) % 4 == 0))
                            {
                                src.GenerateShellChunk(k, c, fresh);
                                Assert.AreEqual(0, ShellValidator.CompareShadow(s.Store(k), fresh, out ex), ex);
                                checkedN++;
                            }
                        }
                    // Why so many dense bricks: those with no air voxel differ only in
                    // material (ores, strata, soil) -- no geometry for the march.
                    long denseN = 0, noAir = 0;
                    var stk = s.Store(k);
                    for (int slot = 0; slot < stk.SlotCount; slot++)
                    {
                        uint e = stk.DirectoryEntry(slot);
                        if ((e & ShellStore.PAGE) == 0) continue;
                        int page = (int)(e & ~ShellStore.PAGE);
                        for (int bi = 0; bi < 4096; bi++)
                        {
                            uint h = stk.ShadowPages[page * 4096 + bi];
                            if ((h & ShellStore.DENSE) == 0) continue;
                            denseN++;
                            int body = (int)(h & 0x3FFFFFFFu);
                            bool air = false;
                            for (int u = 0; u < 128 && !air; u++)
                            {
                                uint w = stk.ShadowBricks[body * 128 + u];
                                air = (w & 0xFF) == 0 || (w & 0xFF00) == 0 || (w & 0xFF0000) == 0 || (w & 0xFF000000) == 0;
                            }
                            if (!air) noAir++;
                        }
                    }
                    lines.Add($"  shell {k}: {denseN} dense bricks stored, {noAir} ({(denseN == 0 ? 0 : 100.0 * noAir / denseN):F1}%) hold no air voxel (material-only)");
                    lines.Add($"  shell {k}: box {lo}..{hi} camera chunk {cc}: {wanted} wanted, {s.Store(k).ChunksStored} stored, refusals {s.Store(k).Refusals} ({s.Store(k).RefusedBricks} dense bricks refused), {checkedN} checked byte-exact, stale 0");
                }
                lines.Add(ShellDebugView.Report(s));
                ShellDebugView.WritePng(s, System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Screenshots", "77", $"shells_occupancy_leg{leg}.png"));
            }
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            s.Dispose(); src.Dispose();
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "fill.txt"), lines);
            UnityEngine.Debug.Log("[77]\n" + string.Join("\n", lines));
        }
    }
}
