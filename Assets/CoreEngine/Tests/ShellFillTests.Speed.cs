// Assets/CoreEngine/Tests/ShellFillTests.Speed.cs
//
// Task 93b2d (owner Q56 (a)): the shell fill's worker cost, a pure speed change.
//   Bytes      filled chunks of shells 1-7 (walk start pose, cave entrances, cliff
//              sites) hash as 52aad5e's fill did (GOLDEN, made on 52aad5e's code
//              by FillHashes_Report), on the fast path and on the reference path.
//   Phases     the fill's time per phase per chunk, shells 1-3 (Editor, Burst;
//              report only, Logs/Shells/fill93b2d_phases.txt).
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public partial class ShellFillTests
{
    /// 93b2's walk start pose (Logs/tg2world_20261016_145541.txt), metres.
    private static readonly float3 WalkStart = new float3(4017.20f, 887.30f, 4018.80f);

    /// Up to `per` non-air wanted chunks of shell k around the walk start, spread
    /// over the wanted set (every n-th in box order).
    private static List<int3> WalkChunks(int k, int per)
    {
        var L = ShellLayout.From(QualityPreset.High);
        var list = new List<int3>();
        var s = new VoxelEngine.Mirror.ShellStreamer(L, _src, 1, synchronous: true) { SyncChunksPerUpdate = 0 };
        try
        {
            s.Update(WalkStart, 1 << 30);
            s.Box(k, out int3 lo, out int3 hi, out _);
            var wanted = new List<int3>();
            for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
                if (s.Wanted(k, new int3(x, y, z))) wanted.Add(new int3(x, y, z));
            // Only layers at or below the top content layer can hold anything; take
            // the ones whose column ground reaches them (cheap: one ShapeField sample).
            var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
            var cand = new List<int3>();
            foreach (var c in wanted)
            {
                int3 f = c << (7 + k);
                int g = TerrainGen.ShapeField.Sample(in spec, in view, in o, f.x + (64 << k) + 0.5f, f.z + (64 << k) + 0.5f, 1).height;
                if (g >= f.y && g < f.y + (128 << k) + (128 << k)) cand.Add(c);
            }
            int step = math.max(1, cand.Count / math.max(1, per));
            for (int i = 0; i < cand.Count && list.Count < per; i += step) list.Add(cand[i]);
        }
        finally { s.Dispose(); }
        return list;
    }

    /// The chunks the byte test hashes: shells 1-7 x (walk start, entrances, cliff sites).
    private static List<(int, int3)> HashChunks()
    {
        var chunks = new List<(int, int3)>();
        var ents = Entrances(1);
        var cliffs = CliffSites(3);
        for (int k = 1; k <= TerrainGenChunkSource.MAX_SHELL; k++)
        {
            foreach (var c in WalkChunks(k, 4)) chunks.Add((k, c));
            foreach (var e in ents) chunks.Add((k, e.Site >> (7 + k)));
            foreach (var s in cliffs) { chunks.Add((k, s >> (7 + k))); if ((s.y >> (7 + k)) > 0) chunks.Add((k, (s >> (7 + k)) - new int3(0, 1, 0))); }
        }
        return chunks;
    }

    private static ulong Hash(TerrainGenChunkSource.ShellChunk sc)
    {
        ulong h = 14695981039346656037ul;
        void Mix(byte b) { h ^= b; h *= 1099511628211ul; }
        if (sc.IsUniformAir) { Mix(0xAA); return h; }
        var m = sc.Mats;
        for (int i = 0; i < m.Length; i++) Mix(m[i]);
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) { Mix(sc.BrickMat[i]); Mix(sc.BrickUniform[i] ? (byte)1 : (byte)0); }
        return h;
    }

    private static string HashLine(int k, int3 c, ulong h) => $"{k} {c.x} {c.y} {c.z} {h:x16}";

    /// Report only: the hashes of HashChunks() under the code as it is; written to
    /// Logs/Shells/fill93b2d_hashes.txt. Made once on 52aad5e's fill -> GOLDEN below.
    [Test, Category("Slow"), Timeout(1800000)]
    public void FillHashes_Report()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var lines = new List<string>();
        using (var sc = new TerrainGenChunkSource.ShellChunk())
            foreach (var (k, c) in HashChunks())
            {
                _src.GenerateShellChunk(k, c, sc);
                lines.Add(HashLine(k, c, Hash(sc)));
            }
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "fill93b2d_hashes.txt"), lines);
        Debug.Log("[93b2d] hashes\n" + string.Join("\n", lines));
    }

    /// FillHashes_Report on 52aad5e's fill (2026-10-16): "shell x y z hash". RE-MADE by task 122
    /// (2026-10-23, biome border blend: 32 of 85 chunks hold border columns and changed); the
    /// fast and reference paths are both checked against it, so they still agree. RE-MADE by task
    /// 176 (2026-11-05, C1 biome layout + patch transitions: the surface biome moved everywhere).
    /// RE-MADE by task 177 (2026-11-06, C2+C3 cover slots: every land surface's skin, soil and
    /// steep-ground rock changed; heights and caves did not).
    private const string GOLDEN = @"
1 161 35 150 164d8e303a098325
1 150 35 153 164d8e303a098325
1 154 35 156 5e2012c7dbd29969
1 153 34 159 20370d273dcb93c2
1 120 20 71 4284c0225488c6cd
1 134 20 74 1b979a471a7b4912
1 139 20 184 d98f4bc842a55256
1 190 23 84 9ec35f119c67c089
1 190 22 84 fa8e1a293dbe681a
1 197 24 84 ebb1678138ee2578
1 197 23 84 4e04bdc473241221
1 203 21 84 2adbec27bc849892
1 203 20 84 7329ecc38d28c325
2 84 15 72 bf1d9d4829975572
2 82 16 75 164d8e303a098325
2 73 16 78 c78dbfa9554ae47e
2 77 15 81 e89d34b7662b9b7e
2 60 10 35 1441e47855de1906
2 67 10 37 dc6076ca6d959c04
2 69 10 92 4faf42e8a19f2cea
2 95 11 42 41eae73429d43383
2 95 10 42 6bc2e7bb5eb91de4
2 98 12 42 6b29ad2820658ae4
2 98 11 42 f201c07b4ccd8013
2 101 10 42 9385301baed78581
2 101 9 42 7329ecc38d28c325
3 45 4 33 7329ecc38d28c325
3 44 5 36 146094a8144455d0
3 43 6 39 8da536cdac4195ea
3 43 6 42 03a77d00c36205e8
3 30 5 17 97a396c949eba590
3 33 5 18 6c8eeb5003340150
3 34 5 46 39246f6d9adef40c
3 47 5 21 a17cfcd62e8d0f6a
3 47 4 21 20509379416622c9
3 49 6 21 d0270c49065e844d
3 49 5 21 253e9c17b3ca77ce
3 50 5 21 42a47d72b1836381
3 50 4 21 3eb7d1eca7302325
4 17 1 13 ed13863ada836325
4 13 2 16 164d8e303a098325
4 21 2 19 96dfcf2603e0c325
4 24 2 22 c67c07f1e40a78b7
4 15 2 8 04712064d8f1871d
4 16 2 9 396ca1ef07e63d42
4 17 2 23 1fbf35b907098890
4 23 2 10 4f80ab1b4f3835da
4 23 1 10 f11a98606458c325
4 24 3 10 0049795ddcb21ebb
4 24 2 10 e5b5a32e2ed0e2a3
4 25 2 10 790b83c41296a3ea
4 25 1 10 7202ffe68f118325
5 3 0 3 afcd08434f85a325
5 6 0 6 7202ffe68f118325
5 15 0 9 ed13863ada836325
5 11 1 12 bd957f07e14992b5
5 7 1 4 58bb7109dcf4890a
5 8 1 4 0c41710a97e179fc
5 8 1 11 762e9a8573200d03
5 11 1 5 1947a9d8c8fba596
5 11 0 5 afcd08434f85a325
5 12 1 5 da2f69022991a655
5 12 0 5 afcd08434f85a325
5 12 1 5 da2f69022991a655
5 12 0 5 afcd08434f85a325
6 0 0 0 8956d8cf0bf3848b
6 2 0 2 91bdd1a34f6f0773
6 0 0 5 20fdbefd76ed0d32
6 5 0 7 a86d54758c3aab0e
6 3 0 2 14ec94f443e0e606
6 4 0 2 c685235d8564ede1
6 4 0 5 51220937eea89195
6 5 0 2 cbd5bf7fae25ecda
6 6 0 2 aec8e6d80dfd04cd
6 6 0 2 aec8e6d80dfd04cd
7 0 0 0 7c839cd3b6ce34ae
7 4 0 0 e0a632bbfe2b71c4
7 4 0 2 cfdc587f3e79559c
7 1 0 4 836a84a320ac2112
7 1 0 1 528f6d0492fa83a6
7 2 0 1 ec0fc52261303c08
7 2 0 2 d6af583ba2f8fe59
7 2 0 1 ec0fc52261303c08
7 3 0 1 2ca625efe5805b1c
7 3 0 1 2ca625efe5805b1c
";

    /// Every chunk of HashChunks() hashes as GOLDEN, on the fast path and on the
    /// reference path (ShellFillFast off).
    [Test, Category("Slow"), Timeout(1800000)]
    public void Fill_BytesAsGolden_FastAndReference()
    {
        var gold = new Dictionary<string, string>();
        foreach (var line in GOLDEN.Split('\n'))
        {
            var t = line.Trim(); if (t.Length == 0) continue;
            int sp = t.LastIndexOf(' '); gold[t.Substring(0, sp)] = t.Substring(sp + 1);
        }
        var chunks = HashChunks();
        var keys = new HashSet<string>(); foreach (var (k, c) in chunks) keys.Add($"{k} {c.x} {c.y} {c.z}");
        Assert.AreEqual(gold.Count, keys.Count, "the chunk set moved: GOLDEN no longer covers it");
        int bad = 0; string first = null;
        using (var sc = new TerrainGenChunkSource.ShellChunk())
            foreach (bool fast in new[] { true, false })
            {
                _src.ShellFillFast = fast;
                foreach (var (k, c) in chunks)
                {
                    _src.GenerateShellChunk(k, c, sc);
                    string key = $"{k} {c.x} {c.y} {c.z}", hs = Hash(sc).ToString("x16");
                    Assert.IsTrue(gold.ContainsKey(key), $"chunk {key} not in GOLDEN");
                    if (gold[key] != hs) { bad++; if (first == null) first = $"{(fast ? "fast" : "reference")} shell {k} chunk {c}: {hs} vs {gold[key]}"; }
                }
            }
        _src.ShellFillFast = true;
        Debug.Log($"[93b2d] {chunks.Count} chunks x 2 paths vs 52aad5e: {bad} differ");
        Assert.AreEqual(0, bad, first);
    }

    /// Report only: the fill's time per phase per filled chunk (Editor, Burst),
    /// shells 1-3 at the walk start, fast vs reference path. Logs/Shells/fill93b2d_phases.txt.
    [Test, Category("Slow"), Timeout(1800000)]
    public void FillPhases_Report()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var lines = new List<string> { "fill ms per filled chunk (Editor, Burst), walk start pose, phases " +
            string.Join("+", TerrainGenChunkSource.FillPhaseNames) + " = fill | column + voxel" };
        using (var sc = new TerrainGenChunkSource.ShellChunk())
            for (int k = 1; k <= 3; k++)
            {
                var chunks = WalkChunks(k, 12);
                foreach (bool fast in new[] { false, true })
                {
                    _src.ShellFillFast = fast;
                    var ph = new double[TerrainGenChunkSource.FILL_PHASES]; double gen = 0; int n = 0;
                    long cand = _src.FillStats[6], cols = _src.FillStats[7];
                    foreach (var c in chunks)
                    {
                        _src.ShellColumnReuse = false;
                        _src.GenerateShellChunk(k, c, sc);
                        if (!sc.FillRan) continue;
                        n++; gen += sc.ColumnMs + sc.VoxelMs;
                        for (int p = 0; p < ph.Length; p++) ph[p] += sc.FillMs[p];
                    }
                    double tot = 0; var parts = new string[ph.Length];
                    for (int p = 0; p < ph.Length; p++) { tot += ph[p] / math.max(1, n); parts[p] = (ph[p] / math.max(1, n)).ToString("F1"); }
                    lines.Add($"  shell {k} {(fast ? "fast" : "ref ")}: {n} chunks: {string.Join("+", parts)} = {tot:F1} | {gen / math.max(1, n):F1}" +
                              $"   (mouth candidates sub-sampled {(_src.FillStats[6] - cand) / (double)math.max(1, n):F0}, columns {(_src.FillStats[7] - cols) / (double)math.max(1, n):F0} per chunk)");
                }
            }
        _src.ShellFillFast = true; _src.ShellColumnReuse = true;
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "fill93b2d_phases.txt"), lines);
        Debug.Log("[93b2d] phases\n" + string.Join("\n", lines));
    }

    /// The fast path's op-box bound (ScanLo): a far crater at a cliff site and a
    /// buried carve 40 m under it (an op seed with no sky above), shells 1-3, give
    /// the reference path's bytes.
    [Test]
    public void FastPath_WithOps_SameBytesAsReference()
    {
        int3 s = CliffSites(1)[0];
        var log = VoxelEngine.Streaming.EditOpLog.InMemory(42u);
        log.Append(new VoxelEngine.Streaming.EditOp(VoxelEngine.Streaming.EditOpKind.Sphere, s, (ushort)200, Materials.Air));
        log.Append(new VoxelEngine.Streaming.EditOp(VoxelEngine.Streaming.EditOpKind.Sphere, s - new int3(0, 400, 0), (ushort)60, Materials.Air));
        int n = 0, bad = 0; string first = null;
        using (var sc = new TerrainGenChunkSource.ShellChunk())
            for (int k = 1; k <= 3; k++)
            for (int dy = -1; dy <= 0; dy++)
            {
                int3 c = (s >> (7 + k)) + new int3(0, dy, 0);
                if (c.y < 0) continue;
                _src.ShellFillFast = false; _src.GenerateShellChunk(k, c, sc, log.Snapshot); ulong a = Hash(sc);
                _src.ShellFillFast = true;  _src.GenerateShellChunk(k, c, sc, log.Snapshot); ulong b = Hash(sc);
                n++;
                if (a != b) { bad++; if (first == null) first = $"shell {k} chunk {c}"; }
            }
        Assert.Greater(n, 0);
        Assert.AreEqual(0, bad, $"fast path differs with ops: {first}");
    }
}
