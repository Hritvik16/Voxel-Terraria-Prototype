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
    [Category("Nightly")]   // task 184b: test-suite tier
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
    /// RE-MADE by task 178 (2026-11-06, C4 cliffs: face relief + scree move heights on faces, the stone
    /// zone is thick layers; the chunk set itself moved).
    /// RE-MADE by task 176d (2026-11-06, biome Size 2: CLIMATE_WAVE_R 1.5 + a 4th octave moves the
    /// surface biome under the shell chunks, so cover / layer rocks change; same 85-chunk set).
    /// RE-MADE by task 177b (2026-11-06, owner palette tweaks: desert steep and savanna flat variants,
    /// badlands bands gone; 18 of the 85 chunks changed (36 counting fast + reference), same set).
    /// RE-MADE by task 178c (2026-11-06, cliff look rework: rock biomes, close layer tones, the per-brick
    /// rock mix, smooth fluting, one bench per layer; 68 of the 82 distinct chunks changed, same set).
    /// RE-MADE by task 179 (2026-11-06, C5 rivers that meander and carve: river courses, levels and carved
    /// banks moved; 23 of the 85 hash lines changed, same set).
    private const string GOLDEN = @"
1 161 35 150 7329ecc38d28c325
1 150 35 153 873734db8ce61569
1 154 35 156 b70f7011a0432b81
1 153 34 159 b3c6e56221712ba1
1 120 20 71 bff6c3ea7abc5a0b
1 134 20 74 3bb37bdef93ce336
1 139 20 184 c5686963f7bcbfb4
1 190 23 84 b2363d525c8d84cc
1 190 22 84 d068f5517aedffc2
1 203 21 84 c541adb89e655507
1 203 20 84 ce1d9b0ea5598325
1 77 21 91 70cf8a01bcf658b1
1 77 20 91 1d4b96b986bfbcc2
2 84 15 72 ac859cd2ce79dadd
2 82 16 75 7329ecc38d28c325
2 73 16 78 a3f5e00036a386bc
2 76 15 81 6e9ccc585472feb7
2 60 10 35 890380c4f87aad3b
2 67 10 37 83941a56e3f286e2
2 69 10 92 2c09772a787c225e
2 95 11 42 d7670bdda699374a
2 95 10 42 c454a7ba7e7d81ab
2 101 10 42 ee31468ef748a1ac
2 101 9 42 573f003a8af42325
2 38 10 45 a9dbf5e301b44e34
2 38 9 45 804384fe7c0a4e4c
3 45 4 33 7a2e8ae493dda325
3 44 5 36 c735fe6748344dfd
3 43 6 39 89f96e13ef756456
3 43 6 42 1a37dbbf25c2bc83
3 30 5 17 fe9d1ea7815bdb05
3 33 5 18 835270ce24fcceb0
3 34 5 46 ecb595baca95258e
3 47 5 21 dfe9fcd4a985b420
3 47 4 21 1cc9651bd25af8e6
3 50 5 21 14ed9ba3ce81a000
3 50 4 21 1fe1614d66a8c325
3 19 5 22 4ad9acc0791502d8
3 19 4 22 9b9bfecfe892cefd
4 17 1 13 ed13863ada836325
4 13 2 16 a0cd1ec277d2e325
4 21 2 19 69ce95f0a92f6325
4 24 2 22 7e2dc0036476f5ea
4 15 2 8 058f1a0e28c65f10
4 16 2 9 2d5da1c6eba9d012
4 17 2 23 0a741049af381eb8
4 23 2 10 6370ee6c96337247
4 23 1 10 ed13863ada836325
4 25 2 10 9c1ba1e9c87066d1
4 25 1 10 ed13863ada836325
4 9 2 11 7afc1afafb8f8373
4 9 1 11 f11a98606458c325
5 3 0 3 f11a98606458c325
5 6 0 6 afcd08434f85a325
5 15 0 9 afcd08434f85a325
5 11 1 12 4fac235bcb25b14b
5 7 1 4 c5501c9d4365dbad
5 8 1 4 3afaae4aa76cd4b4
5 8 1 11 2a68d0c105c17949
5 11 1 5 f29d77ca26923a7f
5 11 0 5 ed13863ada836325
5 12 1 5 ae32d509a3d26fc8
5 12 0 5 ed13863ada836325
5 4 1 5 023be2b0a35a8535
5 4 0 5 f11a98606458c325
6 0 0 0 b9646af074436b16
6 2 0 2 8e36ddbc91fec844
6 0 0 5 5619a2c7123a205e
6 5 0 7 cea98f45a3e0a061
6 3 0 2 ae518217b942f90b
6 4 0 2 6e9327876d322eeb
6 4 0 5 c5936dfe032d9159
6 5 0 2 8e43b263d841f338
6 6 0 2 6240c24135fd7fc5
6 2 0 2 8e36ddbc91fec844
7 0 0 0 963a8e90ff33444a
7 4 0 0 7f52b46dedefc607
7 4 0 2 627182fe3d84919f
7 1 0 4 125ca8032e3b7a56
7 1 0 1 1dcb4fef36655039
7 2 0 1 7e95857f2761a27b
7 2 0 2 2bb34356b5d273a2
7 2 0 1 7e95857f2761a27b
7 3 0 1 01585fb6dcb88d24
7 1 0 1 1dcb4fef36655039
";

    /// Every chunk of HashChunks() hashes as GOLDEN, on the fast path and on the
    /// reference path (ShellFillFast off).
    [Test, Category("Slow"), Timeout(1800000)]
    [Category("Nightly")]   // task 184b: test-suite tier
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
    [Category("Nightly")]   // task 184b: test-suite tier
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
    [Category("Slow")][Category("Nightly")]   // task 184b: test-suite tier
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
