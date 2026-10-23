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
    /// fast and reference paths are both checked against it, so they still agree.
    private const string GOLDEN = @"
1 161 35 150 69ce95f0a92f6325
1 150 35 153 ce1d9b0ea5598325
1 154 35 156 781083e7594139fc
1 153 34 159 7e7aecba3d9c045d
1 120 20 71 b6584f30549651d9
1 134 20 74 7e4b6e06ac6d1627
1 139 20 184 56d02bd843cb3500
1 190 23 84 64a18e9ae42d29c2
1 190 22 84 65ba49adc069c71c
1 197 24 84 095f7b436438f0c8
1 197 23 84 73556efe1170d1e0
1 203 21 84 d0e631815c55a724
1 203 20 84 ce1d9b0ea5598325
2 84 15 72 673e0832ab2e8e9a
2 82 16 75 69ce95f0a92f6325
2 73 16 78 91b3a0f340815060
2 77 15 81 6f09594393b2b369
2 60 10 35 cd7ed68b14258d14
2 67 10 37 1ce8925bff2a0e7a
2 69 10 92 950c1e7322d4a9ea
2 95 11 42 63374f041ff78f1a
2 95 10 42 b91b8760568762e7
2 98 12 42 b74d26ac12ac7cec
2 98 11 42 380d052ac241d6de
2 101 10 42 59c4cd2392114df9
2 101 9 42 ce1d9b0ea5598325
3 45 4 33 ce1d9b0ea5598325
3 44 5 36 cf3ce24f575cfee4
3 43 6 39 2a93793c4a2d3708
3 43 6 42 757f7d2ef3a88882
3 30 5 17 c0da271524ba39f5
3 33 5 18 c3633595da31b489
3 34 5 46 5c7b2aeac26748ac
3 47 5 21 6b48ffd74a5d2a1a
3 47 4 21 dc2a901b1bce7e5e
3 49 6 21 62a35fdddca7b87f
3 49 5 21 4c6728f073dfd7cd
3 50 5 21 95f6ca0e97b1cff7
3 50 4 21 1fe1614d66a8c325
4 17 1 13 f11a98606458c325
4 13 2 16 7329ecc38d28c325
4 21 2 19 69ce95f0a92f6325
4 24 2 22 da23e04c61e583e2
4 15 2 8 efde2ee3578dca30
4 16 2 9 a656b0b8933aa259
4 17 2 23 3ee1fbe9614b8fd7
4 23 2 10 fa708f2eb6ea9f82
4 23 1 10 ed13863ada836325
4 24 3 10 3f137eb8e90ebbdb
4 24 2 10 bbf2c5e3709cc699
4 25 2 10 d0d4793be2b5e203
4 25 1 10 ed13863ada836325
5 3 0 3 f11a98606458c325
5 6 0 6 afcd08434f85a325
5 15 0 9 afcd08434f85a325
5 11 1 12 de34c72180f9a836
5 7 1 4 a1b6999fb996c1ed
5 8 1 4 772383f01cdb3523
5 8 1 11 069bddd39dbc71ba
5 11 1 5 39a57a805179815d
5 11 0 5 ed13863ada836325
5 12 1 5 3533b507390f6b6d
5 12 0 5 ed13863ada836325
5 12 1 5 3533b507390f6b6d
5 12 0 5 ed13863ada836325
6 0 0 0 c18a3f30b1798041
6 2 0 2 3b25a1c70a012d5a
6 0 0 5 9b7da32e0ab44cfd
6 5 0 7 8a2fe47158dd595d
6 3 0 2 c1a4df2aa092ec6d
6 4 0 2 1b23b64a81409ffb
6 4 0 5 f82dfce2a4d22b8b
6 5 0 2 0ffe79365a8c2cca
6 6 0 2 44e726b199da65de
6 6 0 2 44e726b199da65de
7 0 0 0 c01b41a67af74778
7 4 0 0 98cf65b2772a54b7
7 4 0 2 de316513bbe3956d
7 1 0 4 0bf9863313938d54
7 1 0 1 7b50056a52f6279f
7 2 0 1 a5aad4a5fb9b6d12
7 2 0 2 49a4c18b388131cf
7 2 0 1 a5aad4a5fb9b6d12
7 3 0 1 4599ce0bb5f85898
7 3 0 1 4599ce0bb5f85898";

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
