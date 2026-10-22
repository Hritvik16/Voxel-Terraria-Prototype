// Assets/CoreEngine/Tests/BrickSkipTests.cs
//
// Engine step 15's gate (ENGINE_PHASE_PLAN.md row 15, task 82b): the brick
// skip (TerrainGenChunkSource.BrickSkip; DetailField.UniformSamples) changes
// no output byte.
//   Chunks   (Slow) seed 42 map v4, one site per landform plus coast, sea,
//            lake, river, lava lake and stamp columns: the surface band and a
//            spread of deep layers (caves, caverns, pockets, ores, the
//            underworld) generated with the skip off and on -- equal byte for
//            byte (uniformity, every brick handle, every dense body).
//   Shells   (Slow) shells 1-7 at a subset of those sites, surface band and
//            deep layers: Mats, BrickMat, BrickUniform and IsUniformAir equal.
//   Timing   (Slow, REPORTS=1) task 30's kind of isolated check: ms per chunk
//            and per shell chunk, skip off vs on, the same chunk set, ABBA,
//            plus the share of bricks proven air / fluid / solid. Editor Burst,
//            one thread: a direction check, not a streaming or frame-time claim.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class BrickSkipTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp]
    public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (_src != null) _src.BrickSkip = true;
        _src?.Dispose(); _src = null;
    }

    private struct Site { public string name; public int x, z, h; }

    /// One land site per landform, then coast, sea, lake, river, lava and
    /// stamp columns, scanned over a grid (finer if a kind is still missing).
    private static List<Site> Sites(TerrainGenChunkSource src)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var found = new Dictionary<string, Site>();
        string[] special = { "coast", "sea", "lake", "river", "lava", "stamp" };
        foreach (int G in new[] { 64, 256 })
        {
            int step = spec.worldSizeVoxels / G;
            for (int t = 0; t < G * G; t++)
            {
                int idx = (int)((uint)t * 1103u % (uint)(G * G));   // 1103 is prime to G^2: a permutation
                int x = (idx % G) * step + step / 2, z = (idx / G) * step + step / 2;
                var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                var cell = src.Map.Record.At(x, z);
                string k = null;
                if (c.waterKind == 1 && c.water > c.height) k = "lava";
                else if (c.lipLo != 0 || c.riverFloor != 0) k = "river";
                else if (c.water > c.height && c.water > spec.seaLevelY) k = "lake";
                else if (c.stamp3D || c.stampMat != 0 || c.open1Hi > c.open1Lo || c.open3Hi > c.open3Lo) k = "stamp";
                else if (c.height < spec.seaLevelY - 300) k = "sea";
                else if (c.height >= spec.seaLevelY - 30 && c.height <= spec.seaLevelY + 20) k = "coast";
                if (k != null && !found.ContainsKey(k)) found[k] = new Site { name = k, x = x, z = z, h = c.height };
                if (!cell.IsSea && c.height > spec.seaLevelY + 40 && cell.Archetype < TerrainGen.Regions.KINDS)
                {
                    string lf = TerrainGen.Regions.Name(cell.Archetype);
                    if (!found.ContainsKey(lf)) found[lf] = new Site { name = lf, x = x, z = z, h = c.height };
                }
            }
            bool all = true;
            foreach (var k in special) all &= found.ContainsKey(k);
            if (all) break;
        }
        var list = new List<Site>(found.Values);
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    /// The surface band (4 below the surface layer, 2 above) and deep layers:
    /// underworld (< 8), caverns (8-28), tunnel layers 1-2 and pockets (28-36).
    private static readonly int[] DeepLayers = { 1, 4, 8, 12, 16, 20, 24, 28, 30, 32, 34, 36 };

    private static List<int3> ChunkSet(Site s)
    {
        var l = new List<int3>();
        int hl = s.h >> 7;
        foreach (int y in DeepLayers) if (y < hl - 4) l.Add(new int3(s.x >> 7, y, s.z >> 7));
        for (int dy = -4; dy <= 2; dy++) l.Add(new int3(s.x >> 7, hl + dy, s.z >> 7));
        return l;
    }

    /// Bytes of two generated chunks (TerrainGenChunkSourceTests' rule).
    private static string FirstDifference(Chunk a, BrickDataPool pa, Chunk b, BrickDataPool pb)
    {
        if (a.isUniform != b.isUniform) return $"isUniform {a.isUniform} vs {b.isUniform}";
        if (a.isUniform) return a.uniformMaterial == b.uniformMaterial ? null : $"uniform {a.uniformMaterial} vs {b.uniformMaterial}";
        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
        {
            uint da = a.bricks[bi].data, db = b.bricks[bi].data;
            bool dense = (da & 0x80000000u) != 0;
            if (dense != ((db & 0x80000000u) != 0)) return $"brick {bi}: dense {dense} vs {!dense}";
            if (!dense) { if (da != db) return $"brick {bi}: uniform {da} vs {db}"; continue; }
            int sa = (int)(da & 0x7FFFFFFFu) * 512, sb = (int)(db & 0x7FFFFFFFu) * 512;
            for (int v = 0; v < 512; v++)
                if (pa.RawData[sa + v] != pb.RawData[sb + v]) return $"brick {bi} voxel {v}: {pa.RawData[sa + v]} vs {pb.RawData[sb + v]}";
        }
        return null;
    }

    private static string ShellDifference(TerrainGenChunkSource.ShellChunk a, TerrainGenChunkSource.ShellChunk b)
    {
        if (a.IsUniformAir != b.IsUniformAir) return $"IsUniformAir {a.IsUniformAir} vs {b.IsUniformAir}";
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
        {
            if (a.BrickUniform[i] != b.BrickUniform[i]) return $"brick {i}: uniform {a.BrickUniform[i]} vs {b.BrickUniform[i]}";
            if (a.BrickMat[i] != b.BrickMat[i]) return $"brick {i}: mat {a.BrickMat[i]} vs {b.BrickMat[i]}";
        }
        if (a.IsUniformAir) return null;
        for (int i = 0; i < a.Mats.Length; i++)
            if (a.Mats[i] != b.Mats[i]) return $"voxel {i}: {a.Mats[i]} vs {b.Mats[i]}";
        return null;
    }

    private static long[] Snap() => (long[])_src.SkipStats.Clone();

    private static string Share(long[] s0, long[] s1, int at)
    {
        long air = s1[at] - s0[at], fl = s1[at + 1] - s0[at + 1], so = s1[at + 2] - s0[at + 2], n = s1[at + 3] - s0[at + 3];
        double p(long v) => n == 0 ? 0 : 100.0 * v / n;
        return $"bricks proven uniform: air {p(air):F1}%  fluid {p(fl):F1}%  solid {p(so):F1}%  total {p(air + fl + so):F1}% of {n}";
    }

    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    public void Chunks_SkipOnAndOff_ByteIdentical()
    {
        var sites = Sites(_src);
        var lines = new List<string> { $"Task 82b byte-identical, seed 42 map v{_src.Spec.mapVersion}: {sites.Count} sites" };
        byte[] oreEng = new byte[5];
        for (int i = 0; i < 5; i++) oreEng[i] = VoxelEngine.Rendering.FarField.GenToEngine((byte)(TerrainGen.Ores.Copper + i));
        int chunks = 0, dense = 0, deepDense = 0, oreVoxels = 0, deepAir = 0;
        long[] s0 = Snap();
        var pass = new TerrainGenChunkSource.ColumnPass();
        var pa = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var pb = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        try
        {
            foreach (var s in sites)
            {
                var set = ChunkSet(s);
                lines.Add($"  {s.name,-18} ({s.x},{s.h},{s.z})  layers {set[0].y}..{set[set.Count - 1].y} ({set.Count} chunks)");
                foreach (int3 c in set)
                {
                    pa.Reset(); pb.Reset();
                    var a = new Chunk(); var b = new Chunk();
                    _src.BrickSkip = false; _src.Generate(c, a, new ChunkHandleAllocator(2), pa, null, pass);
                    _src.BrickSkip = true;  _src.Generate(c, b, new ChunkHandleAllocator(2), pb, null, pass);
                    string diff = FirstDifference(a, pa, b, pb);
                    Assert.IsNull(diff, $"{s.name} chunk {c}: skip on differs from off: {diff}");
                    chunks++;
                    if (a.isUniform) continue;
                    bool deep = c.y < (s.h >> 7) - 4;
                    for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                    {
                        uint d = a.bricks[bi].data;
                        if ((d & 0x80000000u) == 0) continue;
                        dense++; if (deep) deepDense++;
                        int st = (int)(d & 0x7FFFFFFFu) * 512;
                        for (int v = 0; v < 512; v++)
                        {
                            byte m = pa.RawData[st + v];
                            if (System.Array.IndexOf(oreEng, m) >= 0) oreVoxels++;
                            else if (deep && m == Materials.Air) deepAir++;
                        }
                    }
                }
            }
        }
        finally { _src.BrickSkip = true; pass.Dispose(); pa.Dispose(); pb.Dispose(); }
        lines.Add($"{chunks} chunks byte-identical; {dense} dense bricks ({deepDense} in deep layers), {oreVoxels} ore voxels, {deepAir} cave-air voxels in deep dense bricks");
        lines.Add(Share(s0, Snap(), 0) + "  (skip-on arm and off arm together; only the on arm proves)");
        Write("brick_skip_identical.txt", lines);
        Assert.Greater(chunks, 200, "a few hundred chunks");
        Assert.Greater(oreVoxels, 0, "the set must include ores");
        Assert.Greater(deepAir, 0, "the set must include caves");
    }

    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    public void Shells_SkipOnAndOff_ByteIdentical()
    {
        var sites = Sites(_src);
        var pick = new List<Site>();
        foreach (var s in sites) if (s.name == "coast" || s.name == "lake" || s.name == "river" || s.name == "lava" || s.name == "stamp" || s.name == "sea") pick.Add(s);
        for (int i = 0; i < sites.Count && pick.Count < 9; i++) if (!pick.Contains(sites[i])) pick.Add(sites[i]);
        var lines = new List<string> { $"Task 82b shells byte-identical, seed 42 map v{_src.Spec.mapVersion}, {pick.Count} sites" };
        long[] s0 = Snap();
        int total = 0;
        using (var a = new TerrainGenChunkSource.ShellChunk())
        using (var b = new TerrainGenChunkSource.ShellChunk())
        {
            try
            {
                for (int k = 1; k <= TerrainGenChunkSource.MAX_SHELL; k++)
                {
                    int n = 0, sh = 7 + k;
                    foreach (var s in pick)
                    {
                        var layers = new SortedSet<int>();
                        int hl = s.h >> sh, top = _src.ShellTopLayer(k);
                        for (int dy = -2; dy <= 1; dy++) if (hl + dy >= 0 && hl + dy <= top) layers.Add(hl + dy);
                        layers.Add(0); layers.Add((_src.Spec.CavernTopY >> sh)); layers.Add((_src.Spec.seaLevelY - 600) >> sh);
                        foreach (int ly in layers)
                        {
                            var c = new int3(s.x >> sh, ly, s.z >> sh);
                            _src.BrickSkip = false; _src.GenerateShellChunk(k, c, a);
                            _src.BrickSkip = true;  _src.GenerateShellChunk(k, c, b);
                            string diff = ShellDifference(a, b);
                            Assert.IsNull(diff, $"shell {k} {s.name} chunk {c}: skip on differs from off: {diff}");
                            n++;
                        }
                    }
                    lines.Add($"  shell {k}: {n} chunks byte-identical");
                    total += n;
                }
            }
            finally { _src.BrickSkip = true; }
        }
        lines.Add($"{total} shell chunks byte-identical");
        lines.Add(Share(s0, Snap(), 4) + "  (both arms counted)");
        Write("brick_skip_shells_identical.txt", lines);
        Assert.Greater(total, 100);
    }

    // ------------------------------------------------------------------
    /// REPORT ONLY (REPORTS=1). The surface band (4 below, 2 above, as the
    /// stream generates it) of 6 land sites + coast + lake, one fresh shared
    /// ColumnPass per column per arm (the stream's task 30 path), arms in ABBA
    /// order (A = skip off). Shells 1-7: the same sites' surface band (-2..+1),
    /// ABBA too. Writes Logs/TerrainGen/brick_skip_timing.txt.
    [Test, Category("Slow")]
    public void BrickSkip_TimingReport()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var all = Sites(_src);
        var sites = new List<Site>();
        foreach (var s in all) if (s.name == "coast" || s.name == "lake") sites.Add(s);
        foreach (var s in all) if (sites.Count < 8 && !sites.Contains(s) && s.name != "sea" && s.name != "lava") sites.Add(s);
        var lines = new List<string>
        {
            $"Task 82b brick skip timing, seed 42 map v{_src.Spec.mapVersion}, {sites.Count} sites ({string.Join(", ", sites.ConvertAll(s => s.name))})",
            "Editor Burst, one thread, ABBA (A = skip off, B = on); isolated generation only, not a streaming or frame-time claim",
            "",
        };
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        try
        {
            // Warm Burst on both arms.
            using (var wp = new TerrainGenChunkSource.ColumnPass())
                foreach (bool on in new[] { false, true })
                { _src.BrickSkip = on; pool.Reset(); _src.Generate(new int3(sites[0].x >> 7, (sites[0].h >> 7) - 1, sites[0].z >> 7), new Chunk(), new ChunkHandleAllocator(2), pool, null, wp); }

            double[] ms = new double[2]; int[] n = new int[2];
            long[] sOn0 = null, sOn1 = null;
            foreach (bool on in new[] { false, true, true, false })
            {
                _src.BrickSkip = on;
                long[] st0 = Snap();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var s in sites)
                    using (var pass = new TerrainGenChunkSource.ColumnPass())
                        for (int dy = -4; dy <= 2; dy++)
                        {
                            pool.Reset();
                            _src.Generate(new int3(s.x >> 7, (s.h >> 7) + dy, s.z >> 7), new Chunk(), new ChunkHandleAllocator(2), pool, null, pass);
                            n[on ? 1 : 0]++;
                        }
                ms[on ? 1 : 0] += sw.Elapsed.TotalMilliseconds;
                if (on && sOn0 == null) { sOn0 = st0; sOn1 = Snap(); }
            }
            lines.Add("0.1 m chunks (surface band, 7 layers per column, shared column pass):");
            lines.Add($"  skip off  {ms[0] / n[0],8:F1} ms per chunk   ({n[0]} generations)");
            lines.Add($"  skip on   {ms[1] / n[1],8:F1} ms per chunk   ({n[1]} generations)   speed-up {ms[0] / ms[1]:F2}x");
            lines.Add("  " + Share(sOn0, sOn1, 0));
            lines.Add("");

            using (var sc = new TerrainGenChunkSource.ShellChunk())
            {
                lines.Add("Shell chunks (surface band -2..+1 per site; Generate's column pass is not shared here):");
                for (int k = 1; k <= TerrainGenChunkSource.MAX_SHELL; k++)
                {
                    int sh = 7 + k;
                    var set = new List<int3>();
                    foreach (var s in sites)
                        for (int dy = -2; dy <= 1; dy++)
                        {
                            var c = new int3(s.x >> sh, (s.h >> sh) + dy, s.z >> sh);
                            if (c.y >= 0 && !set.Contains(c)) set.Add(c);
                        }
                    _src.BrickSkip = false; _src.GenerateShellChunk(k, set[0], sc);   // warm
                    _src.BrickSkip = true; _src.GenerateShellChunk(k, set[0], sc);
                    double[] sm = new double[2]; int[] sn = new int[2];
                    long[] k0 = null, k1 = null;
                    foreach (bool on in new[] { false, true, true, false })
                    {
                        _src.BrickSkip = on;
                        long[] st0 = Snap();
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        foreach (var c in set) { _src.GenerateShellChunk(k, c, sc); sn[on ? 1 : 0]++; }
                        sm[on ? 1 : 0] += sw.Elapsed.TotalMilliseconds;
                        if (on && k0 == null) { k0 = st0; k1 = Snap(); }
                    }
                    lines.Add($"  shell {k}: off {sm[0] / sn[0],8:F1} ms   on {sm[1] / sn[1],8:F1} ms per chunk   speed-up {sm[0] / sm[1]:F2}x   " +
                              $"({set.Count} chunks x 2 per arm)   {Share(k0, k1, 4)}");
                }
            }
        }
        finally { _src.BrickSkip = true; pool.Dispose(); }
        Write("brick_skip_timing.txt", lines);
    }

    private static void Write(string file, List<string> lines)
    {
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, file), lines);
        UnityEngine.Debug.Log("[task 82b]\n" + string.Join("\n", lines));
    }
}
