// Assets/CoreEngine/Tests/CavernCellCacheTests.cs
//
// Task 88: DetailField.PrepareColumn(.., ref NodeCache) computes the cavern
// candidates once per 102.4 m cavern cell (DetailField.CavernCells) instead of
// per column. No output byte may move.
//   Columns  (Slow, ~17 s in Mono) PrepareColumn cached == the uncached form on 16,384 random columns
//            (a cache miss nearly every column) and on row-major blocks that
//            cross a cavern cell corner; shift 0 cached == PrepareColumnExact;
//            the cache-off switch (spec.cavernCellCacheOff) == cached.
//   Chunks   0.1 m chunks on both sides of a cavern cell corner, through
//            a cavern, cache off vs on: equal FNV-1a of the chunk bytes.
//   Shells   shells 1, 3, 5, 7 at the same corner (shells >= 4 span many
//            cavern cells), cavern layer and surface: equal FNV-1a.
// Report: Logs/TerrainGen/cavern_cache_identical.txt.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class CavernCellCacheTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp]
    public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (_src != null) _src.SetCavernCellCache(true);
        _src?.Dispose(); _src = null;
    }

    private static bool HasCavern(in TerrainGen.DetailField.ColumnDetail cd) => cd.cav0Hi > cd.cav0Lo;

    /// The first cavern cell corner (scan of the island) where a cavern is open
    /// on both sides of it: columns (bx - 1, bz - 1) and (bx, bz).
    private static int3 Corner(out int lo, out int hi)
    {
        var spec = _src.Spec; var o = _src.O;
        int cellsN = spec.worldSizeVoxels >> TerrainGen.DetailField.CAVERN_CELL_SHIFT;
        int3 any = new int3(-1); lo = hi = 0; int alo = 0, ahi = 0;
        for (int t = 0; t < cellsN * cellsN; t++)
        {
            int idx = (int)((uint)t * 1103u % (uint)(cellsN * cellsN));
            int bx = (1 + idx % (cellsN - 1)) << TerrainGen.DetailField.CAVERN_CELL_SHIFT;
            int bz = (1 + idx / cellsN % (cellsN - 1)) << TerrainGen.DetailField.CAVERN_CELL_SHIFT;
            var a = TerrainGen.DetailField.PrepareColumnExact(in spec, in o, bx - 1, bz - 1);
            var b = TerrainGen.DetailField.PrepareColumnExact(in spec, in o, bx, bz);
            if (HasCavern(in a) && HasCavern(in b))
            {
                lo = math.min(a.cav0Lo, b.cav0Lo); hi = math.max(a.cav0Hi, b.cav0Hi);
                return new int3(bx, 0, bz);
            }
            if (any.x < 0 && (HasCavern(in a) || HasCavern(in b)))
            {
                any = new int3(bx, 0, bz);
                var c = HasCavern(in a) ? a : b; alo = c.cav0Lo; ahi = c.cav0Hi;
            }
        }
        Assert.GreaterOrEqual(any.x, 0, "no cavern at any cell corner");
        lo = alo; hi = ahi;
        return any;
    }

    private const uint FNV_BASIS = 2166136261u, FNV_PRIME = 16777619u;
    private static uint Fnv(uint h, uint v)
    {
        for (int i = 0; i < 4; i++) { h = (h ^ (v & 0xFFu)) * FNV_PRIME; v >>= 8; }
        return h;
    }

    /// FNV-1a of a generated chunk: uniformity, every brick handle's kind and
    /// uniform value, every dense brick's 512 voxels (not its pool slot).
    private static uint ChunkHash(Chunk c, BrickDataPool pool)
    {
        uint h = Fnv(FNV_BASIS, c.isUniform ? 1u : 0u);
        if (c.isUniform) return Fnv(h, c.uniformMaterial);
        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
        {
            uint d = c.bricks[bi].data;
            if ((d & 0x80000000u) == 0) { h = Fnv(h, d); continue; }
            h = Fnv(h, 0x80000000u);
            int st = (int)(d & 0x7FFFFFFFu) * 512;
            for (int v = 0; v < 512; v++) h = (h ^ pool.RawData[st + v]) * FNV_PRIME;
        }
        return h;
    }

    private static uint ShellHash(TerrainGenChunkSource.ShellChunk s)
    {
        uint h = Fnv(FNV_BASIS, s.IsUniformAir ? 1u : 0u);
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            h = Fnv(h, (s.BrickUniform[i] ? 0x100u : 0u) | s.BrickMat[i]);
        if (s.IsUniformAir) return h;
        for (int i = 0; i < s.Mats.Length; i++) h = (h ^ s.Mats[i]) * FNV_PRIME;
        return h;
    }

    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    public void Columns_CachedEqualsUncached_AndReference()
    {
        var spec = _src.Spec; var o = _src.O;
        Assert.AreEqual(0, spec.cavernCellCacheOff, "the cache is on by default");
        var spec0 = spec; spec0.coarseColumnShift = 0;
        var off = spec; off.cavernCellCacheOff = 1;
        var cache = new TerrainGen.DetailField.NodeCache();
        var cache0 = new TerrainGen.DetailField.NodeCache();
        var cacheOff = new TerrainGen.DetailField.NodeCache();
        int n = 0, cav = 0, cav2 = 0;
        void Check(int x, int z)
        {
            var r = TerrainGen.DetailField.PrepareColumn(in spec, in o, x, z);
            var a = TerrainGen.DetailField.PrepareColumn(in spec, in o, x, z, ref cache);
            Assert.IsTrue(a.Equals(r), $"cached differs at ({x},{z})");
            var b = TerrainGen.DetailField.PrepareColumn(in off, in o, x, z, ref cacheOff);
            Assert.IsTrue(b.Equals(r), $"cache-off differs at ({x},{z})");
            var e = TerrainGen.DetailField.PrepareColumnExact(in spec, in o, x, z);
            var c0 = TerrainGen.DetailField.PrepareColumn(in spec0, in o, x, z, ref cache0);
            Assert.IsTrue(c0.Equals(e), $"shift 0 cached differs from the exact reference at ({x},{z})");
            n++; if (HasCavern(in r)) cav++; if (r.cav1Hi > r.cav1Lo) cav2++;
        }
        uint h = 88u;
        for (int i = 0; i < 16384; i++)
        {
            h = h * 1664525u + 1013904223u; int x = (int)(h % (uint)spec.worldSizeVoxels);
            h = h * 1664525u + 1013904223u; int z = (int)(h % (uint)spec.worldSizeVoxels);
            Check(x, z);
        }
        int scatterMisses = cache.Caverns.Misses;
        var corner = Corner(out _, out _);
        for (int z = corner.z - 48; z < corner.z + 48; z++)
        for (int x = corner.x - 48; x < corner.x + 48; x++)
            Check(x, z);
        int blockMisses = cache.Caverns.Misses - scatterMisses;
        var lines = new List<string>
        {
            $"Task 88 columns, seed 42 map v{spec.mapVersion}: {n} columns cached == uncached == cache off; shift 0 cached == PrepareColumnExact",
            $"  {cav} columns with a cavern ({cav2} with two); 16,384 random columns: {scatterMisses} cell fills;",
            $"  96 x 96 block round cell corner ({corner.x},{corner.z}): {blockMisses} cell fills (4 cells)",
        };
        Write("cavern_cache_columns.txt", lines);
        Assert.Greater(cav, 100, "the set must include caverns");
        Assert.Greater(cav2, 0, "the set must include a column with two caverns");
        Assert.LessOrEqual(blockMisses, 8, "row-major over a cell corner: each cell about once");
    }

    // ------------------------------------------------------------------
    [Test]
    public void ChunksAndShells_CacheOffAndOn_ByteIdentical()
    {
        var corner = Corner(out int lo, out int hi);
        var lines = new List<string> { $"Task 88 byte-identical, seed 42 map v{_src.Spec.mapVersion}: cavern cell corner ({corner.x},{corner.z}), cavern y {lo}..{hi}" };
        var pa = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        var pb = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        int chunks = 0, dense = 0;
        try
        {
            var view = _src.Map.View; var spec = _src.Spec; var o = _src.O;
            for (int dz = -1; dz <= 0; dz++)
            for (int dx = -1; dx <= 0; dx++)
            {
                int cx = (corner.x >> 7) + dx, cz = (corner.z >> 7) + dz;
                var top = TerrainGen.ShapeField.Sample(in spec, in view, in o, (cx << 7) + 64.5f, (cz << 7) + 64.5f, 1);
                var ys = new SortedSet<int>();
                for (int y = lo >> 7; y <= hi >> 7; y++) ys.Add(y);
                ys.Add(top.height >> 7);
                foreach (int y in ys)
                {
                    var c = new int3(cx, y, cz);
                    pa.Reset(); pb.Reset();
                    var a = new Chunk(); var b = new Chunk();
                    using (var pass = new TerrainGenChunkSource.ColumnPass())
                    { _src.SetCavernCellCache(false); _src.Generate(c, a, new ChunkHandleAllocator(2), pa, null, pass); }
                    using (var pass = new TerrainGenChunkSource.ColumnPass())
                    { _src.SetCavernCellCache(true); _src.Generate(c, b, new ChunkHandleAllocator(2), pb, null, pass); }
                    uint ha = ChunkHash(a, pa), hb = ChunkHash(b, pb);
                    Assert.AreEqual(ha, hb, $"chunk {c}: cache on differs from off");
                    lines.Add($"  chunk {c}  fnv {ha:x8}  {(a.isUniform ? "uniform" : "mixed")}");
                    chunks++; if (!a.isUniform) dense++;
                }
            }
            using (var sa = new TerrainGenChunkSource.ShellChunk())
            using (var sb = new TerrainGenChunkSource.ShellChunk())
                foreach (int k in new[] { 1, 3, 5, 7 })
                {
                    int sh = 7 + k;
                    var set = new HashSet<int3>();
                    var top = TerrainGen.ShapeField.Sample(in spec, in view, in o, corner.x + 0.5f, corner.z + 0.5f, 1);
                    foreach (int y in new[] { (lo + hi) >> 1, top.height })
                    {
                        if ((y >> sh) > _src.ShellTopLayer(k)) continue;
                        set.Add(new int3((corner.x - 1) >> sh, y >> sh, (corner.z - 1) >> sh));
                        set.Add(new int3(corner.x >> sh, y >> sh, corner.z >> sh));
                    }
                    foreach (var c in set)
                    {
                        _src.SetCavernCellCache(false); _src.GenerateShellChunk(k, c, sa);
                        _src.SetCavernCellCache(true);  _src.GenerateShellChunk(k, c, sb);
                        uint ha = ShellHash(sa), hb = ShellHash(sb);
                        Assert.AreEqual(ha, hb, $"shell {k} chunk {c}: cache on differs from off");
                        lines.Add($"  shell {k} chunk {c}  fnv {ha:x8}  {(sa.IsUniformAir ? "air" : "content")}");
                        chunks++;
                    }
                }
        }
        finally { _src.SetCavernCellCache(true); pa.Dispose(); pb.Dispose(); }
        lines.Add($"{chunks} chunks byte-identical ({dense} mixed 0.1 m chunks)");
        Write("cavern_cache_identical.txt", lines);
        Assert.Greater(dense, 0, "the set must include mixed chunks");
    }

    private static void Write(string file, List<string> lines)
    {
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, file), lines);
        UnityEngine.Debug.Log("[task 88]\n" + string.Join("\n", lines));
    }
}
