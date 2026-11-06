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
    private const string GOLDEN = @"
1 161 35 150 164d8e303a098325
1 150 35 153 164d8e303a098325
1 154 35 156 fe97b146c1a6ef96
1 153 34 159 481e059fad2a81f0
1 120 20 71 b6584f30549651d9
1 134 20 74 36d27f87ba7fdbda
1 139 20 184 559ede9456c2b13a
1 190 23 84 58bdf279101fcdca
1 190 22 84 e139b2514ac9ae7b
1 197 24 84 eacb16e92d3738cd
1 197 23 84 4e1bb49d20b441fe
1 203 21 84 dbd898518401ad57
1 203 20 84 7329ecc38d28c325
2 84 15 72 401d24f1c5d56a32
2 82 16 75 164d8e303a098325
2 73 16 78 af6b45429fd04777
2 77 15 81 699a5738410e65bc
2 60 10 35 cd7ed68b14258d14
2 67 10 37 1d9fc643419ff109
2 69 10 92 a92951f402d7c683
2 95 11 42 beab3deaebd5fbf4
2 95 10 42 218dbfcbe7dee6fa
2 98 12 42 5be8f313456dab94
2 98 11 42 048f39776ee0d8d7
2 101 10 42 a121d12bb31b51fe
2 101 9 42 7329ecc38d28c325
3 45 4 33 7329ecc38d28c325
3 44 5 36 acbee9732583127d
3 43 6 39 e395912661b24c94
3 43 6 42 aaa1b9e568106512
3 30 5 17 8b6e375dac1c404b
3 33 5 18 cce23b93d9abfa91
3 34 5 46 53a8f0247e4f568c
3 47 5 21 c05f5c08adb11ef6
3 47 4 21 120e24d6f2c93422
3 49 6 21 d6d29f86931182c0
3 49 5 21 ac93fbd8626182f1
3 50 5 21 10c170c8925d556e
3 50 4 21 3eb7d1eca7302325
4 17 1 13 ed13863ada836325
4 13 2 16 164d8e303a098325
4 21 2 19 96dfcf2603e0c325
4 24 2 22 afb8f4b524cceeea
4 15 2 8 e0a8455b699a276b
4 16 2 9 201e22fafa923522
4 17 2 23 df922040bf3f3b10
4 23 2 10 008b90f636d57700
4 23 1 10 f11a98606458c325
4 24 3 10 a5b8e0201c80f4b8
4 24 2 10 631e5831007c147f
4 25 2 10 7c1bde46cd8df64d
4 25 1 10 7202ffe68f118325
5 3 0 3 afcd08434f85a325
5 6 0 6 7202ffe68f118325
5 15 0 9 ed13863ada836325
5 11 1 12 14ebdefe93a7970b
5 7 1 4 2fe3a71e7cb6ed5b
5 8 1 4 99d48063c4b04519
5 8 1 11 04fb3fb97064c0c1
5 11 1 5 b2d4e8987f0194d3
5 11 0 5 afcd08434f85a325
5 12 1 5 87e08a6d6cbcb67c
5 12 0 5 afcd08434f85a325
5 12 1 5 87e08a6d6cbcb67c
5 12 0 5 afcd08434f85a325
6 0 0 0 a1c57c021b012b7b
6 2 0 2 6cd7f43b16b35608
6 0 0 5 b77c68debbfdf6e3
6 5 0 7 8e7fead7ac4553a7
6 3 0 2 7f54dde290bf1d57
6 4 0 2 4863fff0f8d68ffd
6 4 0 5 ebc469cce2f721ad
6 5 0 2 7470a95fa9daec5b
6 6 0 2 4eaa2d5e26689000
6 6 0 2 4eaa2d5e26689000
7 0 0 0 ae5a0977e9778edc
7 4 0 0 615fb0c53cf590db
7 4 0 2 30570e73fbe22d90
7 1 0 4 dc7edfce50cf7194
7 1 0 1 7b54269e277c728e
7 2 0 1 e8838407d4960af6
7 2 0 2 7d8184c9a1d7fbe7
7 2 0 1 e8838407d4960af6
7 3 0 1 547015c32bcfa114
7 3 0 1 547015c32bcfa114";

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
