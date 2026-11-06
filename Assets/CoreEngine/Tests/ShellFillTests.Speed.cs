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
    /// RE-MADE by task 178 (2026-11-06, C4 cliffs: face relief + scree move heights on faces, the stone
    /// zone is thick layers; the chunk set itself moved).
    /// RE-MADE by task 176d (2026-11-06, biome Size 2: CLIMATE_WAVE_R 1.5 + a 4th octave moves the
    /// surface biome under the shell chunks, so cover / layer rocks change; same 85-chunk set).
    private const string GOLDEN = @"
1 161 35 150 7329ecc38d28c325
1 150 35 153 69ce95f0a92f6325
1 154 35 156 c5f4dca3eb0b69d3
1 153 34 159 9a8a37322681a756
1 120 20 71 b22e86cc9cc3567e
1 134 20 74 f8f41871dc806b11
1 139 20 184 a5ea06a90919adfe
1 190 23 84 0960580b6a482a0a
1 190 22 84 251783b8b7e05bec
1 203 21 84 2989f6c21d480b4a
1 203 20 84 1fe1614d66a8c325
1 77 21 91 c66c0c4ff8174017
1 77 20 91 72e353146bf38065
2 84 15 72 824fbaff61655356
2 82 16 75 7329ecc38d28c325
2 73 16 78 da8c977ac5d43f9c
2 76 15 81 028678b091847baa
2 60 10 35 d0a0514d9a571b9f
2 67 10 37 f34e9d6f5931710e
2 69 10 92 e0866c61004edbef
2 95 11 42 8972d4a30916051a
2 95 10 42 58874a8f12b36e21
2 101 10 42 b61aa8f09182eff5
2 101 9 42 67d8a1a86a5ee325
2 38 10 45 60d0b95817bf9124
2 38 9 45 079bcc069d4a9385
3 45 4 33 7a2e8ae493dda325
3 44 5 36 d65a4816a2cb55e3
3 43 6 39 746fb704bb2c6a0d
3 43 6 42 ce5a6b52b6c99016
3 30 5 17 b916fe6d43f1b4fb
3 33 5 18 56f52864a1b83e09
3 34 5 46 9fb702769b7b071c
3 47 5 21 de6b95bb5119f7d8
3 47 4 21 e38d00e9ebfbaa33
3 50 5 21 568f2f456aa96ad4
3 50 4 21 1fe1614d66a8c325
3 19 5 22 e64a4f90bdcdf228
3 19 4 22 6744741f2a3a2fd0
4 17 1 13 ed13863ada836325
4 13 2 16 96dfcf2603e0c325
4 21 2 19 3eb7d1eca7302325
4 24 2 22 2407deebbf44c91e
4 15 2 8 c7749141b64dcb02
4 16 2 9 42b85b17d816ad79
4 17 2 23 fbc4e731a2934ed9
4 23 2 10 5fab5322d9ed5833
4 23 1 10 ed13863ada836325
4 25 2 10 4257e4417b655b0e
4 25 1 10 ed13863ada836325
4 9 2 11 1f1d468ea99b467b
4 9 1 11 f11a98606458c325
5 3 0 3 f11a98606458c325
5 6 0 6 afcd08434f85a325
5 15 0 9 afcd08434f85a325
5 11 1 12 e5c06924ff266ade
5 7 1 4 38aad10a3e4711cf
5 8 1 4 df3e5643a9e706f3
5 8 1 11 a6ebcf50b5b079c4
5 11 1 5 22e3f723462a3e72
5 11 0 5 ed13863ada836325
5 12 1 5 694fe51b9ea933a2
5 12 0 5 ed13863ada836325
5 4 1 5 4928a6bccd755408
5 4 0 5 f11a98606458c325
6 0 0 0 6d3f9082b5fb761a
6 2 0 2 bad8b8e5a0738059
6 0 0 5 3dcad55a37241095
6 5 0 7 b5f0f9a038adf600
6 3 0 2 55580f25f79b56f9
6 4 0 2 19bb97375249f221
6 4 0 5 9ce58e0d817b9a1b
6 5 0 2 166425cce23240e4
6 6 0 2 5d2820f40184380f
6 2 0 2 bad8b8e5a0738059
7 0 0 0 83367a139ba0d350
7 4 0 0 af871d09f23e807f
7 4 0 2 1d45ff681a16b892
7 1 0 4 9387d2101556b498
7 1 0 1 bebff19c0f62c267
7 2 0 1 c0cde07145df4822
7 2 0 2 4f9e4d6eeb2b628c
7 2 0 1 c0cde07145df4822
7 3 0 1 3da0851a71a32692
7 1 0 1 bebff19c0f62c267
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
