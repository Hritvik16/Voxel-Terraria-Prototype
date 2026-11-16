// Assets/CoreEngine/Tests/TreePoolTests.cs
//
// Task 188 (owner 2026-11-07 Q-T (a): trees stay OFF, fix the pool overflow and
// the cost first).
//
//   CompactBricks_StoreIsByteExact_PoolCountsBodies (fast)  ShellBrickPack's compact
//       bodies through a real ShellStore (ShadowUploads): bricks of 1, 2 and 3+ values,
//       voxel for voxel equal after upload (ShellValidator.CompareShadow, which decodes
//       the way the shader does); the pool counts BODIES (full + ceil(compact / 7)); a
//       full pool refuses by bodies; a redraw, an alias eviction and an abort free them.
//   Pools_ForestPose / Pools_JunglePose (Explicit, Slow)  REPORTS: every chunk shells
//       1-5 would STORE with the camera at task 180's forest / jungle pose (seed 42, High;
//       the streamer's own box and hole, ShellStreamer.Slide) generated with trees OFF,
//       ON and the ADVERSARIAL cover (TerrainSpec.treesOn 2 + b: every tree cell that
//       passes the physical rules gets a tree of biome b). Per shell: dense BRICKS (one
//       512 B body each = before task 188) and BODIES (ShellBrickPack = after), pages,
//       against the layout's PoolCap / PageCap; tree voxels; the dense bricks trees ADD;
//       generation time (column / voxel / fill) and upload bytes.
//       -> Logs/TerrainGen/trees188/pools_<pose>.txt
//
// EXACT SHORTCUT: an ON chunk whose fine y range meets no gathered tree's [lo.y,
// hi.y] is the OFF chunk byte for byte (OverlayTrees writes inside primitive
// boxes only; the fill reads the chunk and ShapeField halos, never trees), so it is
// not generated twice: its OFF counts are used. Its column pass still runs (timed).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class TreePoolTests
{
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "trees192");   // task 192 (188's run: trees188)
    /// Task 180's poses (Logs/overnight/180_poses.txt, WSPOSE fixed_forest / fixed_jungle): the eye, metres.
    public static readonly float3 ForestEye = new float3(3366.4f, 544.2f, 4973.0f);
    public static readonly float3 JungleEye = new float3(5772.8f, 546.3f, 5024.2f);

    private static readonly HashSet<byte> TreeEngine = new HashSet<byte>();
    static TreePoolTests()
    {
        foreach (byte g in new byte[] { GenMat.ForestLeaves, GenMat.JungleLeaves, GenMat.SavannaLeaves, GenMat.PineNeedles,
                                        GenMat.ForestWood, GenMat.JungleWood, GenMat.AcaciaWood, GenMat.PineWood, GenMat.Cactus })
            TreeEngine.Add(VoxelEngine.Rendering.FarField.GenToEngine(g));
    }

    // ---------------------------------------------------------------- fast: exactness

    /// A chunk whose first `n` bricks are dense: kind 0 = 3+ values, 1 = air + one value
    /// (a sparse, irregular pattern), 2 = two solids, 3 = one value but marked non-uniform.
    private static TerrainGenChunkSource.ShellChunk Craft(int3 coord, int n, Func<int, int> kindOf, int seed)
    {
        var sc = new TerrainGenChunkSource.ShellChunk { Shell = 2, Coord = coord, IsUniformAir = false };
        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
        {
            bool d = bi < n;
            sc.BrickUniform[bi] = !d; sc.BrickMat[bi] = d ? (byte)1 : (byte)0;
            int kind = d ? kindOf(bi) : -1;
            int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
            for (int vz = 0; vz < 8; vz++) for (int vy = 0; vy < 8; vy++) for (int vx = 0; vx < 8; vx++)
            {
                int x = (bx << 3) + vx, y = (by << 3) + vy, z = (bz << 3) + vz;
                uint hsh = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(seed * 2654435761u);
                hsh ^= hsh >> 13; hsh *= 0x5bd1e995u; hsh ^= hsh >> 15;
                byte m = kind switch
                {
                    -1 => (byte)0,
                    0 => (byte)(1 + (hsh % 5)),
                    1 => (hsh & 3) == 0 ? (byte)(55 + bi % 4) : (byte)0,
                    2 => (hsh & 1) == 0 ? (byte)7 : (byte)200,
                    _ => (byte)(3 + bi % 7),
                };
                sc.Mats[TerrainGenChunkSource.Index(x, y, z)] = m;
            }
        }
        return sc;
    }

    [Test]
    public void CompactBricks_StoreIsByteExact_PoolCountsBodies()
    {
        ShellStore.ShadowUploads = true;
        var st = new ShellStore(2, new int3(4, 2, 4), poolCap: 40, pageCap: 4);
        // a: 10 full (kind 0) + 20 compact (kinds 1, 2, 3) = 10 + ceil(20 / 7) = 13 bodies.
        var a = Craft(new int3(1, 0, 1), 30, bi => bi < 10 ? 0 : 1 + bi % 3, 1);
        // b: 15 compact = 3 bodies (7 + 7 + 1).
        var b = Craft(new int3(2, 1, 1), 15, bi => 1 + bi % 3, 2);
        // big: 26 full -> needs 26 bodies, 24 are left -> refused (by bodies, not bricks).
        var big = Craft(new int3(3, 0, 3), 26, bi => 0, 3);
        // alias of a (same slot): 7 compact = 1 body; a's 13 come back.
        var alias = Craft(new int3(1 + 4, 0, 1), 7, bi => 1, 4);
        // late: a's mix in a free slot, aborted mid-upload.
        var late = Craft(new int3(0, 0, 2), 30, bi => bi < 10 ? 0 : 1 + bi % 3, 5);
        try
        {
            ShellStore.UploadBytes(a, out int bodies, out _, out _, out int full, out int compact);
            Assert.AreEqual((10, 20, 13), (full, compact, bodies));
            int budget = 1 << 22;
            Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(a, ref budget));
            Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(b, ref budget));
            Assert.IsTrue(st.FlushDirectory(ref budget));
            Assert.AreEqual(16, st.BricksUsed, "13 + 3 bodies");
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, a, out string ea), ea);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, b, out string eb), eb);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out string ep), ep);

            Assert.AreEqual(ShellStore.StoreResult.Refused, st.Store(big, ref budget));
            Assert.AreEqual(16, st.BricksUsed);
            // A redraw of b (same bytes) beside the old copy, then the old copy's bodies free on flush.
            Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(b, ref budget, replace: true));
            Assert.AreEqual(16, st.BricksUsed); Assert.AreEqual(3, st.DeferredBricks);
            Assert.IsTrue(st.FlushDirectory(ref budget));
            Assert.AreEqual(0, st.DeferredBricks);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, b, out eb), eb);
            Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(alias, ref budget));
            Assert.IsTrue(st.FlushDirectory(ref budget));
            Assert.AreEqual(4, st.BricksUsed, "a's 13 freed, alias's 1 taken");
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, alias, out string ex), ex);
            Assert.AreEqual(0, ShellValidator.CompareShadow(st, b, out eb), eb);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ep), ep);
            // An upload cut short by the budget, then aborted: its bodies come back.
            budget = 600;
            Assert.AreEqual(ShellStore.StoreResult.InProgress, st.Store(late, ref budget));
            Assert.AreEqual(17, st.BricksUsed);
            st.AbortJob();
            Assert.AreEqual(4, st.BricksUsed);
            Assert.AreEqual(0, ShellValidator.CountPoolErrors(st, out ep), ep);
        }
        finally
        {
            a.Dispose(); b.Dispose(); big.Dispose(); alias.Dispose(); late.Dispose(); st.Dispose();
            ShellStore.ShadowUploads = false;
        }
    }

    // ---------------------------------------------------------------- reports

    public sealed class Tally
    {
        public long chunks, pages, bricks, bodies, treeDense, twoMat, treeVox, columns, trees, generated, solidHidden, bytesOld, bytesNew;
        public double colMs, voxMs, fillMs, offMsSame;
    }

    private struct OffChunk { public int bricks, bodies; public bool uniform; public ulong[] bits; public double ms; }

    /// The chunks shell k stores with the camera at `eye` (ShellStreamer.Slide + Wanted).
    public static List<int3> StoredSet(ShellLayout L, TerrainGenChunkSource src, int k, float3 eye, out int3 cc)
    {
        var s = L[k];
        cc = ShellStreamer.CameraChunk(eye, k);
        int shift = 7 + k, wv = src.Spec.worldSizeVoxels - 1;
        int3 wmax = new int3(wv >> shift, src.ShellTopLayer(k), wv >> shift);
        int3 lo = math.max(cc - s.Reach, int3.zero), hi = math.min(cc + s.Reach, wmax);
        if (hi.y - lo.y + 1 > s.Ring.y)
        {
            lo.y = math.clamp(cc.y - s.Ring.y / 2, lo.y, hi.y - s.Ring.y + 1);
            hi.y = lo.y + s.Ring.y - 1;
        }
        var list = new List<int3>();
        for (int z = lo.z; z <= hi.z; z++)
        for (int x = lo.x; x <= hi.x; x++)
        for (int y = hi.y; y >= lo.y; y--)   // top down: the column pass runs on the first (air) layer
        {
            var c = new int3(x, y, z);
            if (s.Hole >= 0 && math.cmax(math.abs(c - cc)) <= s.Hole) continue;
            list.Add(c);
        }
        return list;
    }

    private static double ChunkMs(TerrainGenChunkSource.ShellChunk sc, out double col, out double vox, out double fill)
    {
        col = sc.ColumnReused ? 0 : sc.ColumnMs; vox = sc.VoxelMs; fill = 0;
        for (int p = 0; p < TerrainGenChunkSource.FILL_PHASES; p++) fill += sc.FillMs[p];
        return col + vox + fill;
    }

    private static ulong[] Bits(TerrainGenChunkSource.ShellChunk sc)
    {
        var b = new ulong[64];
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if (!sc.BrickUniform[i]) b[i >> 6] |= 1ul << (i & 63);
        return b;
    }

    /// Counts of one generated ON chunk (bricks against the OFF chunk's dense bits).
    private static void CountOn(TerrainGenChunkSource.ShellChunk sc, ulong[] offBits, Tally t)
    {
        if (sc.IsUniformAir) return;
        var mats = new byte[4];
        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
        {
            int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
            bool trees = false; int distinct = 0; bool air = false;
            for (int vy = 0; vy < 8; vy++)
            for (int vz = 0; vz < 8; vz++)
            {
                int row = (((by << 3) + vy) << 14) | (((bz << 3) + vz) << 7) | (bx << 3);
                for (int vx = 0; vx < 8; vx++)
                {
                    byte m = sc.Mats[row + vx];
                    if (TreeEngine.Contains(m)) { t.treeVox++; trees = true; }
                    if (m == 0) air = true;
                    if (sc.BrickUniform[bi]) continue;
                    bool seen = false;
                    for (int j = 0; j < distinct && j < 4; j++) if (mats[j] == m) { seen = true; break; }
                    if (!seen) { if (distinct < 4) mats[distinct] = m; distinct++; }
                }
            }
            if (sc.BrickUniform[bi]) continue;
            bool wasDense = offBits != null && (offBits[bi >> 6] & (1ul << (bi & 63))) != 0;
            if (!wasDense) t.treeDense++;
            if (distinct <= 2 && trees) t.twoMat++;
            if (!air && trees) t.solidHidden++;
        }
    }

    private static string Pct(long n, int cap) => $"{100.0 * n / cap,4:F0} %{(n > cap ? " OVER" : "")}";

    private static void RunPose(string pose, float3 eye, (string name, byte mode, byte fill, bool style180)[] arms)
    {
        Directory.CreateDirectory(LogDir);
        string path = Path.Combine(LogDir, $"pools_{pose}.txt");
        var L = ShellLayout.From(QualityPreset.High);
        var src = new TerrainGenChunkSource(42u);
        src.SetTrees2(false);   // task 214: task 188's arms are today's trees (-trees2 0)
        var log = new StringBuilder();
        log.AppendLine($"task 188: seed 42, High, camera at 180's {pose} pose {eye} m. Every chunk each shell STORES (the streamer's box minus the hole), generated.");
        log.AppendLine("arms: OFF (-trees 0), ON (-trees 1), ADV-F / ADV-J (adversarial: every tree cell that passes the physical rules gets a forest / jungle tree).");
        log.AppendLine("task 192: ON-180 = task 180's trees (-treestyle 0); ON-30 / ON-45 / ON-60 = 192's open crowns at -leaffill 30 / 45 / 60; ADV-x60 = adversarial at fill 60.");
        log.AppendLine("bricks = dense bricks = 512 B bodies BEFORE task 188; bodies = AFTER (ShellBrickPack: <= 2-value bricks packed 7 to a body); cap = PoolCap (bodies).");
        log.AppendLine("pages = non-uniform chunks (cap PageCap). tree+ = dense bricks that are uniform with trees OFF; 2mat = dense tree bricks holding <= 2 values;");
        log.AppendLine("hidden = dense tree bricks with no air voxel. upload MB = the stored set's upload bytes before -> after. ms = this test's Editor (Burst) time, summed.");
        using var sc = new TerrainGenChunkSource.ShellChunk();
        try
        {
            for (int k = 1; k <= Trees.MAX_SHELL; k++)
            {
                var S = L[k];
                var set = StoredSet(L, src, k, eye, out int3 cc);
                var off = new Dictionary<int3, OffChunk>(set.Count);
                log.AppendLine($"shell {k} ({S.VoxelM:F1} m voxels, {S.ChunkM:F1} m chunks): {set.Count} chunks in {S.Columns} columns, camera chunk {cc}; caps: bodies {S.PoolCap}, pages {S.PageCap}");
                foreach (var arm in arms)
                {
                    src.SetTreesMode(arm.mode);
                    src.SetTreeLook(arm.fill, arm.style180);   // task 192: the leaf fill arm / task 180's trees
                    var t = new Tally();
                    var sw = Stopwatch.StartNew();
                    int2 col = new int2(int.MinValue, int.MinValue);
                    int spanLo = int.MaxValue, spanHi = int.MinValue;
                    foreach (var c in set)
                    {
                        t.chunks++;
                        bool newCol = c.x != col.x || c.z != col.y;
                        OffChunk o;
                        if (arm.mode != 0 && !newCol)
                        {
                            int y0 = c.y << (7 + k), y1 = ((c.y + 1) << (7 + k)) - 1;
                            if (y1 < spanLo || y0 > spanHi)
                            {   // the exact shortcut (header): this chunk is the OFF chunk
                                o = off[c];
                                t.bricks += o.bricks; t.bodies += o.bodies; if (!o.uniform) t.pages++;
                                t.bytesOld += o.uniform ? 4 : 4 + 16408 + o.bricks * 512L;
                                t.bytesNew += o.uniform ? 4 : 4 + 16408 + o.bodies * 512L;
                                continue;
                            }
                        }
                        src.GenerateShellChunk(k, c, sc);
                        t.generated++;
                        double ms = ChunkMs(sc, out double cm, out double vm, out double fm);
                        t.colMs += cm; t.voxMs += vm; t.fillMs += fm;
                        if (newCol)
                        {
                            col = new int2(c.x, c.z); t.columns++;
                            t.trees += sc.TreeCount;
                            int2 span = sc.TreeSpanY; spanLo = span.x; spanHi = span.y;
                        }
                        int bytes = ShellStore.UploadBytes(sc, out int bodies, out bool uniform, out _, out int full, out int compact);
                        int bricks = full + compact;
                        t.bricks += bricks; t.bodies += bodies; if (!uniform) t.pages++;
                        t.bytesNew += bytes; t.bytesOld += uniform ? 4 : bytes + (bricks - bodies) * 512L;
                        if (arm.mode == 0)
                            off[c] = new OffChunk { bricks = bricks, bodies = bodies, uniform = uniform, bits = bricks > 0 ? Bits(sc) : null, ms = ms - cm };
                        else
                        {
                            t.offMsSame += off[c].ms;
                            CountOn(sc, off[c].bits, t);
                        }
                    }
                    log.AppendLine($"  {arm.name,-7} bricks {t.bricks,7} ({Pct(t.bricks, S.PoolCap)}) -> bodies {t.bodies,7} ({Pct(t.bodies, S.PoolCap)}); " +
                                   $"pages {t.pages,5} ({Pct(t.pages, S.PageCap)}); upload {t.bytesOld / 1048576.0:F1} -> {t.bytesNew / 1048576.0:F1} MB; " +
                                   $"tree+ {t.treeDense}, 2mat {t.twoMat}, hidden {t.solidHidden}, tree voxels {t.treeVox}, trees gathered {t.trees} in {t.columns} column passes; " +
                                   $"generated {t.generated}: column {t.colMs:F0} + voxel {t.voxMs:F0} + fill {t.fillMs:F0} ms" +
                                   (arm.mode != 0 ? $" (OFF on the same chunks, voxel + fill: {t.offMsSame:F0} ms)" : "") + $"; wall {sw.Elapsed.TotalSeconds:F0} s");
                    File.WriteAllText(path, log.ToString());
                }
            }
        }
        finally { src.Dispose(); }
        UnityEngine.Debug.Log(log.ToString());
    }

    [Test, Explicit, Category("Slow"), Timeout(3600000)]
    public void Pools_ForestPose() =>
        RunPose("forest", ForestEye, new (string, byte, byte, bool)[] { ("OFF", 0, 0, false), ("ON-180", 1, 0, true), ("ON-30", 1, 30, false),
            ("ON-45", 1, 45, false), ("ON-60", 1, 60, false), ("ADV-F60", (byte)(2 + GenBiome.Forest), 60, false) });

    [Test, Explicit, Category("Slow"), Timeout(3600000)]
    public void Pools_JunglePose() =>
        RunPose("jungle", JungleEye, new (string, byte, byte, bool)[] { ("OFF", 0, 0, false), ("ON-180", 1, 0, true), ("ON-30", 1, 30, false),
            ("ON-45", 1, 45, false), ("ON-60", 1, 60, false), ("ADV-J60", (byte)(2 + GenBiome.Jungle), 60, false) });
}
