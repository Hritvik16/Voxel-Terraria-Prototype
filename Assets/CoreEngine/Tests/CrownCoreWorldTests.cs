// Assets/CoreEngine/Tests/CrownCoreWorldTests.cs
//
// Task 243: solid crown cores in the WORLD (TerrainSpec.crownCore, `-crowncore 1`, default OFF).
//   World_SamePlacement_OnlyAirBecomesLeaf   seed 42, the chunks of one forest oak: ON gathers the same
//                                trees as OFF, and every voxel that differs was AIR (OFF) and is a tree
//                                LEAF (ON) -- no terrain, wood or other content touched
//   Report_LeafBrickFill (REPORTS=1)  the brick fill histogram (0.8 m bricks, % of voxels non-air) of the
//                                LEAF bricks (>= 1 leaf voxel) in forest_eye's 82 m footprint (the 0.1 m
//                                window round the eye 3366.4, 4950.0 m), for task 210's trees (trees2On 2)
//                                and the owner's dense trees (213, Q-AJ, the default), cores OFF vs ON:
//                                fully solid leaf bricks, CPU dense bodies, inner sealed bricks (201a's
//                                rule, TerrainClipmap.IsSealSolid) -> Logs/TerrainGen/trees243/histogram.txt
// These need the chunk source and the library (CoreEngine), so they build it like Trees2Tests
// (the TerrainGen test assembly's SharedMaps cannot reach the chunk path).
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class CrownCoreWorldTests
{
    private static TerrainGenChunkSource Source(uint seed)
    {
        int keep = TerrainGenChunkSource.Trees2TestOverride;
        TerrainGenChunkSource.Trees2TestOverride = 1;
        try { var s = new TerrainGenChunkSource(seed); s.SetTrees(true); return s; }
        finally { TerrainGenChunkSource.Trees2TestOverride = keep; }
    }

    private static readonly bool[] Leaf = new bool[256], Tree = new bool[256];
    static CrownCoreWorldTests()
    {
        for (int g = 1; g < 96; g++)
        {
            bool l = GenMat.IsTreeLeaf((byte)g) || g == GenMat.Leaves, t = l || GenMat.IsTreeTrunk((byte)g);
            byte e = VoxelEngine.Rendering.FarField.GenToEngine((byte)g);
            if (l) Leaf[e] = true;
            if (t) Tree[e] = true;
        }
    }

    /// One generated chunk's voxels, chunk layout (TerrainGenChunkSource.Index), plus its dense-brick flags.
    private sealed class Gen
    {
        public readonly byte[] v = new byte[128 * 128 * 128];
        public readonly bool[] dense = new bool[4096];
        public Gen(TerrainGenChunkSource src, int3 coord)
        {
            var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
            try
            {
                var chunk = new Chunk();
                src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
                if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return; }
                var raw = pool.RawData;
                for (int bi = 0; bi < 4096; bi++)
                {
                    uint d = chunk.bricks[bi].data;
                    int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                    bool dn = (d & 0x80000000u) != 0; dense[bi] = dn;
                    int body = (int)(d & 0x3FFFFFFFu) * 512;
                    for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        v[TerrainGenChunkSource.Index(bx * 8 + x, by * 8 + y, bz * 8 + z)] = dn ? raw[body + ((z << 6) | (y << 3) | x)] : (byte)d;
                }
            }
            finally { pool.Dispose(); }
        }
        public byte At(int x, int y, int z) => v[TerrainGenChunkSource.Index(x, y, z)];
    }

    private static string Key(in TerrainGen.Tree t) => $"{t.root} {t.prim0} {t.shape} {t.lo} {t.hi}";

    // ==================================================================
    [Test, Category("Slow")]
    public void World_SamePlacement_OnlyAirBecomesLeaf()
    {
        using var src = Source(42u);
        int cx = 33664, cz = 49500;   // forest_eye
        var off = src.LibTreesIn(cx - 512, cz - 512, cx + 511, cz + 511);
        Assert.Greater(off.Length, 0, "no library trees at forest_eye");
        // an oak (the biggest forest crown); else any tree
        int pick = 0;
        for (int i = 0; i < off.Length; i++) if (TerrainGenChunkSource.LibSpeciesName(off[i]) == "oak") { pick = i; break; }
        var tree = off[pick];
        int3 c0 = tree.lo >> 7, c1 = tree.hi >> 7;
        var offGen = new Dictionary<int3, Gen>();
        for (int z = c0.z; z <= c1.z; z++) for (int y = c0.y; y <= c1.y; y++) for (int x = c0.x; x <= c1.x; x++)
            offGen[new int3(x, y, z)] = new Gen(src, new int3(x, y, z));

        for (byte mode = 1; mode <= 2; mode++)
        {
        src.SetCrownCore(mode);
        try
        {
            Assert.AreEqual(mode, Trees2Library.CoreMode, "library not rebuilt in core mode");
            var on = src.LibTreesIn(cx - 512, cz - 512, cx + 511, cz + 511);
            var a = new HashSet<string>(); var b = new HashSet<string>();
            foreach (var t in off) a.Add(Key(in t));
            foreach (var t in on) b.Add(Key(in t));
            Assert.IsTrue(a.SetEquals(b), $"crown cores changed the placement: off {a.Count} on {b.Count}");
            long changed = 0, bad = 0, leafSwap = 0;
            var pairs = new Dictionary<int, int>();
            foreach (var kv in offGen)
            {
                var g = new Gen(src, kv.Key);
                for (int i = 0; i < g.v.Length; i++)
                {
                    byte o = kv.Value.v[i], n = g.v[i];
                    if (o == n) continue;
                    if (o == 0 && Leaf[n]) changed++;
                    else if (Tree[o] && Leaf[n]) leafSwap++;   // another tree's voxel here: this crown's core (library precedence: instance order) now wins
                    else { bad++; int k = o * 256 + n; pairs[k] = pairs.TryGetValue(k, out int c) ? c + 1 : 1; }
                }
            }
            UnityEngine.Debug.Log($"[crowncore] world mode {mode}: {TerrainGenChunkSource.LibSpeciesName(tree)} at {tree.root}, {offGen.Count} chunks: {changed:N0} air -> leaf, {leafSwap:N0} tree -> leaf (overlapping crowns), {bad} other changes");
            foreach (var kv in pairs) UnityEngine.Debug.Log($"[crowncore]   other change {kv.Key >> 8} -> {kv.Key & 255}: {kv.Value}");
            Assert.AreEqual(0, bad, "crown cores changed something other than air -> leaf");
            Assert.Greater(changed, 0, "crown cores filled nothing in an oak's chunks");
        }
        finally { src.SetCrownCore(false); }
        }
    }

    // ==================================================================
    [Test, Category("Slow"), Timeout(3600000)]
    public void Report_LeafBrickFill()
    {
        // CoreEngine.Tests does not reference TerrainGen.Tests (ReportOnly); same rule.
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var src = Source(42u);
        int ex = 33664, ez = 49500, half = 410;   // forest_eye; the 82 m 0.1 m window
        int x0 = ex - half, z0 = ez - half, x1 = ex + half - 1, z1 = ez + half - 1;
        var log = new StringBuilder();
        log.AppendLine("# task 243: leaf-brick fill in forest_eye's 82 m footprint (seed 42, map v4; eye 3366.4, 4950.0 m;");
        log.AppendLine($"#   voxels x [{x0}, {x1}] z [{z0}, {z1}]); bricks 0.8 m (8^3); 'leaf brick' = >= 1 leaf voxel; fill = non-air voxels / 512.");
        log.AppendLine("#   'uniform' = one material (no CPU body); 'sealed' = 201a's inner sealed rule (bricks 1..14 of a chunk; all 512 + the 1-voxel");
        log.AppendLine("#   shell seal-solid) -> no GPU body. GPU bodies ~ dense - sealed (chunk-border sealed bricks still take one, 201a).");
        string[] hdr = { "0-9", "10-19", "20-29", "30-39", "40-49", "50-59", "60-69", "70-79", "80-89", "90-99", "100" };
        foreach (byte mode in new byte[] { 2, 1 })
        {
            src.SetTrees2Mode(mode);
            src.SetCrownCore(false);
            var trees = src.LibTreesIn(x0, z0, x1, z1);
            int yLo = int.MaxValue, yHi = int.MinValue;
            foreach (var t in trees) { yLo = math.min(yLo, t.lo.y); yHi = math.max(yHi, t.hi.y); }
            log.AppendLine();
            log.AppendLine($"## trees2On {mode} ({(mode == 1 ? "213 dense, the default (owner Q-AJ)" : "task 210's placement")}): {trees.Length} library trees touch the footprint, y [{yLo}, {yHi}]");
            log.AppendLine($"{"cores",-8} {"leaf bricks",11} {"fully solid",12} {"solid %",7} {"uniform",8} {"leaf dense",10} {"leaf sealed",11} | {"all dense",9} {"all sealed",10} {"dense-sealed",12} | fill histogram, % of leaf bricks: " + string.Join(" ", hdr));
            if (trees.Length == 0) continue;
            foreach (byte core in new byte[] { 0, 1, 2 })
            {
                src.SetCrownCore(core);
                var hist = new long[11];
                long leafB = 0, full = 0, uni = 0, leafDense = 0, leafSealed = 0, allDense = 0, allSealed = 0;
                for (int cz = z0 >> 7; cz <= z1 >> 7; cz++)
                for (int cy = yLo >> 7; cy <= yHi >> 7; cy++)
                for (int cx = x0 >> 7; cx <= x1 >> 7; cx++)
                {
                    var g = new Gen(src, new int3(cx, cy, cz));
                    for (int bi = 0; bi < 4096; bi++)
                    {
                        int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                        int wx = cx * 128 + bx * 8, wz = cz * 128 + bz * 8;
                        if (wx < x0 || wx > x1 || wz < z0 || wz > z1) continue;
                        int filled = 0, leaf = 0;
                        byte m0 = g.At(bx * 8, by * 8, bz * 8); bool one = true;
                        for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        {
                            byte m = g.At(bx * 8 + x, by * 8 + y, bz * 8 + z);
                            if (m != 0) filled++;
                            if (Leaf[m]) leaf++;
                            if (m != m0) one = false;
                        }
                        bool sealedB = g.dense[bi] && Sealed(g, bx, by, bz);
                        if (g.dense[bi]) allDense++;
                        if (sealedB) allSealed++;
                        if (leaf == 0) continue;
                        leafB++;
                        if (filled == 512) full++;
                        if (one) uni++;
                        if (g.dense[bi]) leafDense++;
                        if (sealedB) leafSealed++;
                        hist[filled == 512 ? 10 : filled * 10 / 512]++;
                    }
                }
                var hs = new StringBuilder();
                for (int i = 0; i < 11; i++) hs.Append($" {100.0 * hist[i] / math.max(1, leafB),5:F1}");
                log.AppendLine($"{(core == 0 ? "OFF" : core == 1 ? "1" : "2 closed"),-8} {leafB,11:N0} {full,12:N0} {100.0 * full / math.max(1, leafB),7:F1} {uni,8:N0} {leafDense,10:N0} {leafSealed,11:N0} | {allDense,9:N0} {allSealed,10:N0} {allDense - allSealed,12:N0} |{hs}");
            }
        }
        src.SetCrownCore(false);
        log.AppendLine();
        log.AppendLine($"# library: {Trees2Library.Summary}");
        log.AppendLine($"# {sw.Elapsed.TotalSeconds:F0} s");
        Directory.CreateDirectory(Path.Combine("Logs", "TerrainGen", "trees243"));
        File.WriteAllText(Path.Combine("Logs", "TerrainGen", "trees243", "histogram.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
    }

    /// 201a's inner sealed rule on the CPU copy: bricks 1..14 on every axis, all 1,000 voxels of the 10^3 box seal-solid.
    private static bool Sealed(Gen g, int bx, int by, int bz)
    {
        if (bx < 1 || by < 1 || bz < 1 || bx > 14 || by > 14 || bz > 14) return false;
        for (int z = -1; z <= 8; z++) for (int y = -1; y <= 8; y++) for (int x = -1; x <= 8; x++)
            if (!TerrainClipmap.IsSealSolid(g.At(bx * 8 + x, by * 8 + y, bz * 8 + z))) return false;
        return true;
    }
}
