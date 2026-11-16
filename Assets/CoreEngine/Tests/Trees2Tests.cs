// Assets/CoreEngine/Tests/Trees2Tests.cs
//
// Task 210 (-trees2 1): the world space-colonization trees (TerrainGen.Trees2,
// TerrainGenChunkSource.Trees2.cs). Today's trees (the default) are pinned by
// TreeGalleryTests.TodayTrees_Pinned and the tree tests; these check the new path:
//   Library_Turns_Budget          the library builds, deterministically, inside its bounds; quarter turns
//                                 map the voxel and brick lattices onto themselves; every biome's expected
//                                 bricks per 51.2 m stay within its budget (Logs/TerrainGen/trees210/library.txt)
//   Chunks_Deterministic_Border_PointAndShellsAgree   an oak across a chunk border: two runs equal, only
//                                 air becomes tree, the point query and shells 0-3 agree with the chunks
//   Dense_Density_FootprintsAgree task 213: the denser forest (trees2On 1) vs 210's placement (trees2On 2,
//                                 pinned to 210's counts): more trees, world tree bricks <= 1.40 x 210's,
//                                 chunk-size and 256 m gathers agree (Logs/TerrainGen/trees213/density.txt)
//   Dense_Sweep (Explicit)        task 213's budget / share calibration (sweep.txt)
//   Poses210 (Explicit)           the shot poses for Logs/overnight/210_shots.sh
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class Trees2Tests
{
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "trees210");

    private static TerrainGenChunkSource Source(uint seed)
    {
        int keep = TerrainGenChunkSource.Trees2TestOverride;
        TerrainGenChunkSource.Trees2TestOverride = 1;
        try { var s = new TerrainGenChunkSource(seed); s.SetTrees(true); return s; }
        finally { TerrainGenChunkSource.Trees2TestOverride = keep; }
    }

    private static bool IsTreeGen(byte g) => GenMat.IsTreeTrunk(g) || GenMat.IsTreeLeaf(g) || g == GenMat.Leaves;
    private static readonly HashSet<byte> TreeEngine = new HashSet<byte>();
    static Trees2Tests()
    {
        for (int g = 1; g < 96; g++) if (IsTreeGen((byte)g)) TreeEngine.Add(VoxelEngine.Rendering.FarField.GenToEngine((byte)g));
    }

    // ==================================================================
    [Test, Category("Slow")]
    public unsafe void Library_Turns_Budget()
    {
        // determinism of one build (a cheap species), byte for byte
        var a = Trees2.BuildProto(TreeGallery.SP_BIRCH, 0);
        var b = Trees2.BuildProto(TreeGallery.SP_BIRCH, 0);
        FastAssert.BytesCollectionEqual(a.codes, b.codes, "birch v0: two builds differ (codes)");
        CollectionAssert.AreEqual(a.index, b.index, "birch v0: two builds differ (index)");

        // turns: inverse pairs; bricks onto bricks
        var rng = new Unity.Mathematics.Random(210u);
        for (int k = 0; k < 4000; k++)
        {
            int3 p = rng.NextInt3(new int3(-300, -50, -300), new int3(300, 800, 300));
            for (int r = 0; r < 4; r++)
            {
                Assert.AreEqual(p, Trees2.ToProto(Trees2.FromProto(p, r), r), $"turn {r}: To(From(p)) != p at {p}");
                Assert.AreEqual(Trees2.FromProto(p >> 3, r), Trees2.FromProto(p, r) >> 3, $"turn {r}: the brick of {p} does not map to a brick");
            }
        }

        Trees2Library.Acquire();
        try
        {
            ref var lib = ref Trees2Shared.Lib.Data;
            Assert.AreEqual(1, lib.ready, "library not ready");
            var log = new StringBuilder();
            log.AppendLine("# task 210: the Trees2 prototype library (TreeGallery.Build M_SPACE, world roots), one process build");
            log.AppendLine("# " + Trees2Library.Summary);
            log.AppendLine("species  var  seed  bricks  wood_vox  leaf_vox  reach  keep  top  leafBottom  leafReach  footR  feet  ms");
            foreach (var pb in Trees2Library.Built)
                log.AppendLine($"{TreeGallery.SpeciesName[pb.species],-8} {pb.variant,3} {pb.seed,5} {pb.bricks,7} {pb.wood,9} {pb.leaf,9} {pb.reach,6} {pb.keep,5} {pb.top,4} {pb.leafBottom,11} {pb.leafReach,10} {pb.footR,6} {pb.feet.Length,5} {pb.ms,5:F0}");
            for (int s = 0; s < Trees2.SP_COUNT; s++)
            {
                Assert.Greater(lib.variants[s], 0, $"no {TreeGallery.SpeciesName[s]} prototype fits its bounds");
                for (int i = lib.first[s]; i < lib.first[s] + lib.variants[s]; i++)
                {
                    var P = lib.protos[i];
                    Assert.AreEqual(s, P.species);
                    Assert.LessOrEqual(P.reach, P.giant != 0 ? Trees2.REACH_G : Trees2.REACH_N, $"prototype {i} reach");
                    Assert.LessOrEqual(P.top, Trees2.MAX_HEIGHT, $"prototype {i} height");
                    Assert.AreEqual(0, P.lo.x & 7, "prototype box not brick aligned");
                }
            }
            log.AppendLine();
            log.AppendLine("biome      budget  expected  trees/51.2m  (full plane, flat; bricks per 51.2 m x 51.2 m)");
            string[] bn = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
            for (int bi = 0; bi < GenBiome.Count; bi++)
            {
                double exp = 0, cnt = 0;
                for (int s = 0; s < Trees2.SP_COUNT; s++)
                {
                    double p = lib.pq[bi * 8 + s] / 65536.0;
                    exp += p * 16 * Trees2Library.MeanBricks[s]; cnt += p * 16;
                }
                log.AppendLine($"{bn[bi],-9} {Trees2.Budget(bi),7} {exp,9:F0} {cnt,11:F1}");
                Assert.LessOrEqual(exp, Trees2.Budget(bi) * 1.001 + 1, $"{bn[bi]}: expected bricks over the budget");
            }
            log.AppendLine("task 213 dense mode (tier 1 on the 12.8 m grid, tier 2 on the 6.4 m grid):");
            for (int bi = 0; bi < GenBiome.Count; bi++)
            {
                double exp = 0, cnt = 0;
                for (int s = 0; s < Trees2.SP_COUNT; s++)
                {
                    double pd = lib.pqD[bi * 8 + s] / 65536.0, pf = lib.pqF[bi * 8 + s] / 65536.0;
                    Assert.IsTrue(pd == 0 || Trees2.Tier(s) == 1, $"{bn[bi]}: a tier-{Trees2.Tier(s)} species on the dense normal grid");
                    Assert.IsTrue(pf == 0 || Trees2.Tier(s) == 2, $"{bn[bi]}: a tier-{Trees2.Tier(s)} species on the fine grid");
                    exp += (pd * 16 + pf * 64) * Trees2Library.MeanBricks[s]; cnt += pd * 16 + pf * 64;
                }
                log.AppendLine($"{bn[bi],-9} {Trees2.BudgetD(bi),7} {exp,9:F0} {cnt,11:F1}");
                Assert.LessOrEqual(exp, Trees2.BudgetD(bi) * 1.001 + 1, $"{bn[bi]}: dense expected bricks over the budget");
            }
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "library.txt"), log.ToString());
            UnityEngine.Debug.Log(log.ToString());
        }
        finally { Trees2Library.Release(); }
    }

    // ------------------------------------------------------------------
    private static byte Read(Chunk c, BrickDataPool pool, int3 local)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((local.z >> 3) << 8) | ((local.y >> 3) << 4) | (local.x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        return pool.RawData[(int)(d & 0x3FFFFFFFu) * 512 + (((local.z & 7) << 6) | ((local.y & 7) << 3) | (local.x & 7))];
    }

    private static byte[] Gen(TerrainGenChunkSource src, int3 coord)
    {
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
        try
        {
            var chunk = new Chunk();
            src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
            var v = new byte[128 * 128 * 128];
            if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return v; }
            for (int y = 0; y < 128; y++)
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
                v[TerrainGenChunkSource.Index(x, y, z)] = Read(chunk, pool, new int3(x, y, z));
            return v;
        }
        finally { pool.Dispose(); }
    }

    private sealed class Empty : IDisposable
    {
        private readonly BrickDataPool _pool = new BrickDataPool(16);
        public readonly ChunkStore store;
        public Empty() { store = new ChunkStore(_pool, new ChunkHandleAllocator(1)); }
        public void Dispose() => _pool.Dispose();
    }

    [Test, Category("Slow")]
    public void Chunks_Deterministic_Border_PointAndShellsAgree()
    {
        using var src = Source(42u);
        // an OAK near task 192's forest pose whose box crosses a chunk border in x
        int cx = 33664, cz = 49280;
        var found = src.LibTreesIn(cx - 1024, cz - 1024, cx + 1024, cz + 1024);
        int pick = -1;
        for (int pass = 0; pass < 2 && pick < 0; pass++)
            for (int i = 0; i < found.Length && pick < 0; i++)
            {
                var t = found[i];
                if (pass == 0 && TerrainGenChunkSource.LibSpeciesName(t) != "oak") continue;
                if ((t.lo.x >> 7) != (t.hi.x >> 7) && (t.hi.z >> 7) - (t.lo.z >> 7) <= 1) pick = i;
            }
        Assert.Greater(found.Length, 0, "no library trees round the forest pose");
        Assert.GreaterOrEqual(pick, 0, "no library tree crossing a chunk border");
        var tree = found[pick];
        var log = new StringBuilder();
        var counts = new Dictionary<string, int>();
        foreach (var t in found) { string n = TerrainGenChunkSource.LibSpeciesName(t); counts[n] = counts.TryGetValue(n, out int k) ? k + 1 : 1; }
        log.Append($"seed 42, 204.8 m round ({cx}, {cz}): {found.Length} library trees:");
        foreach (var kv in counts) log.Append($" {kv.Key} {kv.Value}");
        log.AppendLine();
        log.AppendLine($"border tree: {TerrainGenChunkSource.LibSpeciesName(tree)} root {tree.root} lo {tree.lo} hi {tree.hi} turn {tree.shape & 3}");

        int3 c0 = tree.lo >> 7, c1 = tree.hi >> 7;
        var on = new Dictionary<int3, byte[]>();
        int sideA = 0, sideB = 0;
        for (int z = c0.z; z <= c1.z; z++)
        for (int y = c0.y; y <= c1.y; y++)
        for (int x = c0.x; x <= c1.x; x++)
        {
            var cc = new int3(x, y, z);
            src.SetTrees(true);
            var a = Gen(src, cc);
            var b = Gen(src, cc);
            FastAssert.BytesCollectionEqual(a, b, $"chunk {cc}: two ON runs differ (not deterministic)");
            src.SetTrees(false);
            var off = Gen(src, cc);
            src.SetTrees(true);
            int trees = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == off[i]) continue;
                if (off[i] != Materials.Air) Assert.AreEqual(Materials.Air, off[i], $"chunk {cc} voxel {i}: a tree replaced a non-air voxel ({off[i]})");
                if (!TreeEngine.Contains(a[i])) Assert.IsTrue(TreeEngine.Contains(a[i]), $"chunk {cc} voxel {i}: ON differs from OFF by a non-tree material {a[i]}");
                trees++;
            }
            if (x == c0.x) sideA += trees; else sideB += trees;
            on[cc] = a;
            log.AppendLine($"  chunk {cc}: {trees} tree voxels");
        }
        Assert.Greater(sideA, 0, "no tree voxels west of the border");
        Assert.Greater(sideB, 0, "no tree voxels east of the border");

        // the point path (FarQuery): a quarter of the tree voxels + random points
        var rng = new Unity.Mathematics.Random(210u);
        var pts = new List<int3>(); var want = new List<byte>();
        foreach (var kv in on)
        {
            int3 o = kv.Key << 7; var a = kv.Value;
            for (int i = 0; i < a.Length; i++)
                if (TreeEngine.Contains(a[i]) && (i & 3) == 0) { pts.Add(o + new int3(i & 127, i >> 14, (i >> 7) & 127)); want.Add(a[i]); }
            for (int k = 0; k < 4000; k++)
            {
                int3 l = rng.NextInt3(new int3(0), new int3(128));
                pts.Add(o + l); want.Add(a[TerrainGenChunkSource.Index(l.x, l.y, l.z)]);
            }
        }
        using (var empty = new Empty())
        using (var fq = new FarQuery(empty.store, empty.store, src, EditOpLog.InMemory(42u), null, 42u))
        {
            var p = new NativeArray<int3>(pts.ToArray(), Allocator.TempJob);
            var res = new NativeArray<byte>(pts.Count, Allocator.TempJob);
            try
            {
                fq.GetVoxels(p, res);
                for (int i = 0; i < pts.Count; i++)
                    if (want[i] != res[i]) Assert.AreEqual(want[i], res[i], $"point {pts[i]}: FarQuery {res[i]} != chunk {want[i]}");
            }
            finally { p.Dispose(); res.Dispose(); }
        }
        log.AppendLine($"  point path: {pts.Count} points equal");

        using var sc = new TerrainGenChunkSource.ShellChunk();
        foreach (var kv in on)
        {
            src.GenerateShellChunk(0, kv.Key, sc);
            var a = kv.Value;
            for (int i = 0; i < a.Length; i++)
            {
                byte g = sc.Get(i & 127, i >> 14, (i >> 7) & 127);
                if (g != a[i]) Assert.Fail($"shell 0 chunk {kv.Key} voxel {i}: {g} != Generate {a[i]}");
            }
        }
        src.ShellFill = false;
        for (int k = 1; k <= 3; k++)
        {
            int3 sc3 = tree.root >> (7 + k);
            src.GenerateShellChunk(k, sc3, sc);
            int3 origin = sc3 << (7 + k);
            int half = (1 << (k - 1)) - 1;
            int compared = 0, treeVox = 0;
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
            {
                int gx = origin.x + (x << k) + half, gz = origin.z + (z << k) + half;
                int ground = sc.GroundY(x, z);
                for (int y = 0; y < 128; y++)
                {
                    int gy = origin.y + (y << k) + half;
                    if (gy <= ground) continue;
                    int3 f = new int3(gx, gy, gz);
                    if (!on.TryGetValue(f >> 7, out var fine)) continue;
                    int3 l = f & 127;
                    byte want1 = fine[TerrainGenChunkSource.Index(l.x, l.y, l.z)];
                    byte got = sc.Get(x, y, z);
                    if (!TreeEngine.Contains(want1) && !TreeEngine.Contains(got)) continue;
                    compared++;
                    if (TreeEngine.Contains(got)) treeVox++;
                    if (want1 != got) Assert.AreEqual(want1, got, $"shell {k} voxel ({x},{y},{z}) sample {f}: {got} != 0.1 m voxel {want1}");
                }
            }
            Assert.Greater(treeVox, 0, $"shell {k} shows none of the tree");
            log.AppendLine($"  shell {k}: {compared} tree voxels compared, {treeVox} in the shell chunk, all equal");
        }
        src.ShellFill = true;
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "chunks.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
    }

    // ==================================================================
    // Task 213: the denser forest (trees2On 1) vs task 210's placement (trees2On 2)
    // ==================================================================
    private static unsafe long BrickKey(int3 b) => ((long)(b.x & 0xFFFFF) << 40) | ((long)(b.y & 0xFFFFF) << 20) | (long)(b.z & 0xFFFFF);

    /// World bricks a library tree's prototype occupies (roots are multiples of 8 and turns map bricks to bricks).
    private static unsafe void AddBricks(in Tree t, HashSet<long> set)
    {
        ref var P = ref Trees2Shared.Lib.Data.protos[t.prim0];
        int rot = t.shape & 3;
        int3 rb = t.root >> 3, lb = P.lo >> 3;
        for (int by = 0; by < P.nb.y; by++)
        for (int bz = 0; bz < P.nb.z; bz++)
        for (int bx = 0; bx < P.nb.x; bx++)
            if (P.index[(by * P.nb.z + bz) * P.nb.x + bx] >= 0)
                set.Add(BrickKey(rb + Trees2.FromProto(lb + new int3(bx, by, bz), rot)));
    }

    /// Dense_Density_FootprintsAgree: in a 256 m forest window round 210's forest pose, the dense mode
    /// has more trees than 210's and its world tree bricks stay within the body-pool room 210 left
    /// (<= 1.40 x 210's bricks: the forest poses had ~30 k of 254 k bodies free over 210's 68.9-78.4 k);
    /// 210's placement is unchanged (its chunks.txt counts); and the dense gather is footprint-independent:
    /// 16 chunk-size gathers give exactly the trees a 256 m gather gives over the same 51.2 m square.
    [Test, Category("Slow")]
    public void Dense_Density_FootprintsAgree()
    {
        using var src = Source(42u);
        var log = new StringBuilder();
        log.AppendLine("# task 213: denser forest (trees2On 1) vs task 210 (trees2On 2), seed 42; library: " + Trees2Library.Summary);
        int cx = 33664, cz = 49280;
        // 210's placement pinned: its chunks.txt round the same point
        src.SetTrees2Mode(2);
        var t210 = src.LibTreesIn(cx - 1024, cz - 1024, cx + 1024, cz + 1024);
        int o210 = 0, b210 = 0;
        foreach (var t in t210) { string n = TerrainGenChunkSource.LibSpeciesName(t); if (n == "oak") o210++; else if (n == "birch") b210++; }
        log.AppendLine($"210 placement, 204.8 m round ({cx}, {cz}): {t210.Length} trees, oak {o210} birch {b210} (210's chunks.txt: 109, oak 17 birch 92)");
        Assert.AreEqual(109, t210.Length, "task 210's placement changed (trees2On 2 must be 210 byte for byte)");
        Assert.AreEqual(17, o210); Assert.AreEqual(92, b210);

        // density: a 256 m window centred on the forest pose (roots inside the window only)
        int x0 = 33664 - 1280, z0 = 49280 - 1280, x1 = x0 + 2559, z1 = z0 + 2559;
        var bricks = new long[3]; var trees = new int[3];
        foreach (byte mode in new byte[] { 2, 1 })
        {
            src.SetTrees2Mode(mode);
            var all = src.LibTreesIn(x0, z0, x1, z1);
            var set = new HashSet<long>(); long sum = 0;
            var counts = new SortedDictionary<string, int>();
            int n = 0;
            foreach (var t in all)
            {
                if (t.root.x < x0 || t.root.x > x1 || t.root.z < z0 || t.root.z > z1) continue;
                n++;
                string nm = TerrainGenChunkSource.LibSpeciesName(t);
                counts[nm] = counts.TryGetValue(nm, out int k) ? k + 1 : 1;
                AddBricks(in t, set);
                unsafe { sum += Trees2Shared.Lib.Data.protos[t.prim0].bricks; }
            }
            bricks[mode] = set.Count; trees[mode] = n;
            log.Append($"mode {mode} ({(mode == 1 ? "213 dense" : "210")}): 256 m window, {n} trees ({n / 25.0:F1} per 51.2 m square):");
            foreach (var kv in counts) log.Append($" {kv.Key} {kv.Value}");
            log.AppendLine($" | world tree bricks {set.Count:N0} (sum of prototypes {sum:N0})");
        }
        double ratio = (double)bricks[1] / math.max(1, bricks[2]);
        log.AppendLine($"dense / 210: trees {(double)trees[1] / math.max(1, trees[2]):F2}x, world tree bricks {ratio:F2}x");
        Directory.CreateDirectory(Path.Combine("Logs", "TerrainGen", "trees213"));
        File.WriteAllText(Path.Combine("Logs", "TerrainGen", "trees213", "density.txt"), log.ToString());
        Assert.Greater(trees[1], trees[2] * 3 / 2, "the dense forest has not got denser");
        Assert.LessOrEqual(ratio, 1.40, "dense forest bricks over the room 210 left in the GPU body pool");

        // footprint independence (dense): 16 chunk gathers over a 51.2 m square == the 256 m gather there
        src.SetTrees2Mode(1);
        int sx = cx - 256, sz = cz - 256;
        var big = new HashSet<string>(); var small = new HashSet<string>();
        string Key(in Tree t) => $"{t.root} {t.prim0} {t.shape}";
        foreach (var t in src.LibTreesIn(x0, z0, x1, z1))
            if (!(t.hi.x < sx || t.lo.x > sx + 511 || t.hi.z < sz || t.lo.z > sz + 511)) big.Add(Key(in t));
        for (int qz = 0; qz < 4; qz++)
        for (int qx = 0; qx < 4; qx++)
            foreach (var t in src.LibTreesIn(sx + qx * 128, sz + qz * 128, sx + qx * 128 + 127, sz + qz * 128 + 127)) small.Add(Key(in t));
        log.AppendLine($"footprints: 256 m gather {big.Count} trees touch the 51.2 m square, 16 chunk gathers {small.Count}");
        Assert.Greater(big.Count, 0);
        Assert.IsTrue(big.SetEquals(small), $"dense gather depends on the footprint: big {big.Count} vs chunks {small.Count}");

        Directory.CreateDirectory(Path.Combine("Logs", "TerrainGen", "trees213"));
        File.WriteAllText(Path.Combine("Logs", "TerrainGen", "trees213", "density.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
    }

    /// Task 213's calibration (Explicit): forest budget x k and oak / birch shares -> trees, world tree bricks and
    /// crown area in the 256 m forest window (Logs/TerrainGen/trees213/sweep.txt). Restores the shipped values.
    [Test, Explicit("task 213 calibration sweep: writes Logs/TerrainGen/trees213/sweep.txt")]
    public void Dense_Sweep()
    {
        using var src = Source(42u);
        var log = new StringBuilder();
        int x0 = 33664 - 1280, z0 = 49280 - 1280, x1 = x0 + 2559, z1 = z0 + 2559;
        int b0 = Trees2.BUDGET_D[0]; byte so = Trees2.SHARE_OAK_D[0], sb = Trees2.SHARE_BIRCH_D[0];
        var cfg = new (int k100, byte oak, byte birch)[] { (100, 10, 6), (125, 8, 8), (130, 8, 8), (140, 8, 8), (130, 7, 9), (140, 7, 9), (150, 6, 10), (140, 10, 6), (130, 12, 4), (160, 6, 10), (150, 8, 8) };
        foreach (byte mode in new byte[] { 2, 1 })
        foreach (var c in cfg)
        {
            if (mode == 2 && c.k100 != 100) continue;
            Trees2.BUDGET_D[0] = 800 * 64 * 266 / 1000 * c.k100 / 100; Trees2.SHARE_OAK_D[0] = c.oak; Trees2.SHARE_BIRCH_D[0] = c.birch;
            Trees2.Probabilities(ref Trees2Shared.Lib.Data, Trees2Library.MeanBricks);
            src.SetTrees2Mode(mode);
            var set = new HashSet<long>(); int n = 0, oaks = 0, birches = 0; double cover = 0;
            foreach (var t in src.LibTreesIn(x0, z0, x1, z1))
            {
                if (t.root.x < x0 || t.root.x > x1 || t.root.z < z0 || t.root.z > z1) continue;
                n++; string nm = TerrainGenChunkSource.LibSpeciesName(t);
                if (nm == "oak") oaks++; if (nm == "birch") birches++;
                unsafe { float lr = Trees2Shared.Lib.Data.protos[t.prim0].leafReach * 0.1f; cover += math.PI * lr * lr; }
                AddBricks(in t, set);
            }
            log.AppendLine($"mode {mode} k {c.k100 / 100.0:F2} oak {c.oak}/16 birch {c.birch}/16: trees {n} (oak {oaks} birch {birches}) bricks {set.Count:N0} crown area {cover:F0} m2 ({cover / (256.0 * 256.0) * 100:F0} % of the window, overlap counted)");
        }
        Trees2.BUDGET_D[0] = b0; Trees2.SHARE_OAK_D[0] = so; Trees2.SHARE_BIRCH_D[0] = sb;
        Trees2.Probabilities(ref Trees2Shared.Lib.Data, Trees2Library.MeanBricks);
        Directory.CreateDirectory(Path.Combine("Logs", "TerrainGen", "trees213"));
        File.WriteAllText(Path.Combine("Logs", "TerrainGen", "trees213", "sweep.txt"), log.ToString());
    }

    // ==================================================================
    // Shot poses (Explicit): eye height and ~100 m per biome, sun behind the camera
    // (camera on +z looking -z, as tasks 180 / 200), a redwood grove and a banyan from far.
    // ==================================================================
    [Test, Explicit("writes Logs/TerrainGen/trees210/poses.txt for -wsfixed")]
    public void Poses210()
    {
        using var src = Source(42u);
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float G(float xm, float zm) => ShapeField.Sample(in spec, in view, in o, xm * 10f + 0.5f, zm * 10f + 0.5f, 1).height * 0.1f;
        var poses = new List<string>();
        var log = new StringBuilder();
        void Pair(string name, float tx, float tz)
        {
            float gt = G(tx, tz);
            float ge = G(tx, tz + 22f);
            poses.Add($"{name}_eye|{tx:F1},{ge + 1.7f:F1},{tz + 22f:F1}|{tx:F1},{gt + 5f:F1},{tz:F1}");
            poses.Add($"{name}_100m|{tx:F1},{gt + 55f:F1},{tz + 85f:F1}|{tx:F1},{gt + 2f:F1},{tz:F1}");
        }
        // task 180's densest spots per biome (Logs/TerrainGen/trees180/poses.txt targets)
        Pair("forest", 3366.4f, 4928.0f);
        Pair("jungle", 5772.8f, 4979.2f);
        Pair("savanna", 2060.8f, 4928.0f);
        Pair("snow", 4595.2f, 5747.2f);
        Pair("desert", 3750.4f, 2470.4f);
        // giants: the nearest redwood grove and banyan to the world centre, from 160 m and 320 m
        Tree? red = null, ban = null; float dr = float.MaxValue, db = float.MaxValue;
        int W = spec.worldSizeVoxels;
        for (int z = 8192; z < W - 8192; z += 4096)
        for (int x = 8192; x < W - 8192; x += 4096)
        {
            foreach (var t in src.LibTreesIn(x, z, x + 4095, z + 4095))
            {
                string n = TerrainGenChunkSource.LibSpeciesName(t);
                float d = math.distance(new float2(t.root.x, t.root.z), new float2(W / 2, W / 2));
                if (n == "redwood" && d < dr) { dr = d; red = t; }
                if (n == "banyan" && d < db) { db = d; ban = t; }
            }
        }
        foreach (var (nm, t) in new[] { ("redwood", red), ("banyan", ban) })
        {
            if (t == null) { log.AppendLine($"no {nm} found"); continue; }
            var r = t.Value;
            float tx = r.root.x * 0.1f, tz = r.root.z * 0.1f, ty = r.root.y * 0.1f;
            float h = (r.hi.y - r.root.y) * 0.1f;
            poses.Add($"{nm}_near|{tx:F1},{ty + h * 0.5f + 10f:F1},{tz + 60f:F1}|{tx:F1},{ty + h * 0.45f:F1},{tz:F1}");
            poses.Add($"{nm}_far1|{tx:F1},{ty + 60f:F1},{tz + 300f:F1}|{tx:F1},{ty + h * 0.5f:F1},{tz:F1}");
            poses.Add($"{nm}_far2|{tx:F1},{ty + 120f:F1},{tz + 900f:F1}|{tx:F1},{ty + h * 0.5f:F1},{tz:F1}");
            log.AppendLine($"{nm}: root {r.root} hi {r.hi} ({h:F0} m)");
        }
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "poses.txt"), string.Join(";", poses));
        File.WriteAllText(Path.Combine(LogDir, "poses_log.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString() + string.Join("\n", poses));
    }
}
