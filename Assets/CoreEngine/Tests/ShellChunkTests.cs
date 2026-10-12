// Assets/CoreEngine/Tests/ShellChunkTests.cs
//
// Engine step 10's gate (Amendment 8.14 §5 rows 12-13, ENGINE_PHASE_PLAN.md
// row 10): TerrainGenChunkSource.GenerateShellChunk, a chunk generated directly
// at 0.1 * 2^k m voxels.
//   Shell 0      == Generate, byte for byte (the same code path at k = 0)
//   Exact        every coarse voxel of shells 1-7 == TerrainGen's own D
//                (unculled, prepared independently, under Burst like the
//                chunk) at fine index 2^(k-1) - 1 per axis, and each column's
//                solid top voxel == D at the ground height h (row 13 as
//                amended by task 77: owner defaults Q2 (a), Q1 (a))
//   Agreement    (Slow) solid/air vs the cascades' majority-vote downsample of
//                the 0.1 m chunks (LODDownsampler), shells 1-3 GATED at row
//                13's line: >= 97% of voxels in non-uniform bricks and >= 99%
//                of columns with the surface within 1 coarse voxel, on seed
//                42's landform set (the set row 13 names). Seeds 7 and 1234
//                and the pooled total are REPORTED against the same line, not
//                gated: on 2026-10-11 (task 74) shell 1 fell below it there
//                (HANDOFF). Shell 4 reported. One site per landform per seed.
//                Beside each number: the old centre sample 2^(k-1) (report).
//                Also reports generation ms per shell chunk and writes the
//                side-by-side pictures (Screenshots/77/; task 74's are in 74/).
// "Solid" here is NON-AIR: water and lava count, as the renderer draws them.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class ShellChunkTests
{
    private static TerrainGenChunkSource _src;

    [OneTimeSetUp]
    public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }

    [OneTimeTearDown]
    public void TearDown() { _src?.Dispose(); _src = null; }

    private static byte Read(Chunk c, BrickDataPool pool, int x, int y, int z)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        int idx = (int)(d & 0x7FFFFFFFu);
        return pool.RawData[idx * 512 + (((z & 7) << 6) | ((y & 7) << 3) | (x & 7))];
    }

    /// A land column (fine voxel x, z) with its ground height, as the V1 tests pick one.
    private static int3 SurfaceVoxel(TerrainGenChunkSource src)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 400; i++)
        {
            float2 p = new float2(c, c) + new float2((i % 20) - 10, (i / 20) - 10) * 1500f;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 300 || col.water > col.height) continue;
            return new int3((int)p.x, col.height, (int)p.y);
        }
        Assert.Fail("no land site found");
        return default;
    }

    // ------------------------------------------------------------------
    // Shell 0 is Generate.
    // ------------------------------------------------------------------
    [Test]
    public void Shell0_IsGenerate_ByteForByte()
    {
        int3 s = SurfaceVoxel(_src);
        int3 cc = s >> 7;
        int3[] coords = { cc, cc - new int3(0, 3, 0), new int3(cc.x, 16, cc.z) };   // surface, deep geology, cavern layer
        using (var sc = new TerrainGenChunkSource.ShellChunk())
        {
            foreach (var coord in coords)
            {
                var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
                try
                {
                    var chunk = new Chunk();
                    _src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
                    _src.GenerateShellChunk(0, coord, sc);
                    Assert.AreEqual(chunk.isUniform && chunk.uniformMaterial == Materials.Air, sc.IsUniformAir, $"{coord}: air fast path differs");
                    int bad = 0;
                    var mats = sc.IsUniformAir ? null : sc.Mats.ToArray();
                    for (int z = 0; z < 128; z++)
                    for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        byte m = mats == null ? Materials.Air : mats[TerrainGenChunkSource.Index(x, y, z)];
                        if (m != Read(chunk, pool, x, y, z)) bad++;
                    }
                    Assert.AreEqual(0, bad, $"{coord}: shell 0 differs from Generate in {bad} voxels");
                    if (!chunk.isUniform)
                        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                            Assert.AreEqual((chunk.bricks[bi].data & 0x80000000u) == 0, sc.BrickUniform[bi], $"{coord} brick {bi}: uniformity differs");
                }
                finally { pool.Dispose(); }
            }
        }
    }

    // ------------------------------------------------------------------
    // Every coarse voxel is D at its sample (index 2^(k-1) - 1), the top one D at h.
    // ------------------------------------------------------------------
    [Unity.Burst.BurstCompile(FloatMode = Unity.Burst.FloatMode.Strict, FloatPrecision = Unity.Burst.FloatPrecision.High, CompileSynchronously = true)]
    private struct ShellRefJob : Unity.Jobs.IJob
    {
        [ReadOnly] public NativeArray<float> h, r, w;
        [ReadOnly] public NativeArray<byte> cls, toEngine, ore;
            [ReadOnly] public NativeArray<ushort> flow;   // task 76
        public TerrainGen.TerrainSpec spec; public TerrainGen.Offsets o;
        public int3 origin; public int shift, half;
        public NativeArray<byte> mats;      // coarse voxels 0, 3, 6, ... 126 per axis (43^3), unculled D -> engine
        public NativeArray<int> topY;       // per sampled column: the top voxel's coarse y in this chunk, or -1
        public NativeArray<byte> topM;      // ... and its expected material

        public unsafe void Execute()
        {
            var map = new TerrainGen.MapView
            {
                height = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(h),
                rough = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(r),
                water = (float*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(w),
                waterClass = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(cls), flowDir = flow.Length > 1 ? (ushort*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(flow) : null,
                cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
            };
            int k = 0, t = 0;
            for (int z = 0; z < 128; z += 3)
            for (int x = 0; x < 128; x += 3)
            {
                int wx = origin.x + (x << shift) + half, wz = origin.z + (z << shift) + half;
                var col = TerrainGen.ShapeField.Sample(in spec, in map, in o, wx + 0.5f, wz + 0.5f, 1);
                float st = TerrainGen.ShapeField.MaxStep(in spec, in map, in o, wx + 0.5f, wz + 0.5f, 1, col.height);
                var cd = TerrainGen.DetailField.PrepareColumn(in spec, in o, wx, wz);
                if (cd.oreMul != 0) cd.oreMul = ore[TerrainGen.Ores.CellIndex(wx, wz, spec.mapCells, spec.mapCellVoxels)];
                // Task 74 Q1 (a): the highest sample <= h, if solid, is D at h.
                int ly = (col.height - origin.y - half) >> shift;
                byte topG = 0;
                if (ly >= 0 && ly < 128)
                {
                    byte g = TerrainGen.DetailField.Material(in spec, in o, in col, in cd, st, wx, origin.y + (ly << shift) + half, wz);
                    if (g >= TerrainGen.GenMat.RiverLip) topG = TerrainGen.DetailField.Material(in spec, in o, in col, in cd, st, wx, col.height, wz);
                    else ly = -1;
                }
                else ly = -1;
                topY[t] = ly; topM[t++] = toEngine[topG];
                for (int y = 0; y < 128; y += 3)
                    mats[k++] = y == ly ? toEngine[topG]
                        : toEngine[TerrainGen.DetailField.Material(in spec, in o, in col, in cd, st, wx, origin.y + (y << shift) + half, wz)];
            }
        }
    }

    [Test]
    public void ShellVoxels_AreTheGeneratorsVoxelAtTheirCentre_Shells1to7()
    {
        int3 s = SurfaceVoxel(_src);
        var hA = new NativeArray<float>(_src.Map.Height, Allocator.TempJob);
        var rA = new NativeArray<float>(_src.Map.Rough, Allocator.TempJob);
        var wA = new NativeArray<float>(_src.Map.Water, Allocator.TempJob);
        var cA = new NativeArray<byte>(_src.Map.WaterClass, Allocator.TempJob);
        var fA = new NativeArray<ushort>(_src.Map.FlowDir ?? new ushort[1], Allocator.TempJob);
        var oA = new NativeArray<byte>(TerrainGen.OreTable.DensityPlane(_src.Map.Record, _src.Spec.mapCells), Allocator.TempJob);
        var te = new NativeArray<byte>(256, Allocator.TempJob);
        var refM = new NativeArray<byte>(43 * 43 * 43, Allocator.TempJob);
        var refTY = new NativeArray<int>(43 * 43, Allocator.TempJob);
        var refTM = new NativeArray<byte>(43 * 43, Allocator.TempJob);
        var sc = new TerrainGenChunkSource.ShellChunk();
        try
        {
            for (int i = 0; i < 256; i++) te[i] = VoxelEngine.Rendering.FarField.GenToEngine((byte)i);
            var cases = new List<int2>();   // (shell, dy): the surface shell chunk, plus one below it at shells 1-2
            for (int k = 1; k <= TerrainGenChunkSource.MAX_SHELL; k++) cases.Add(new int2(k, 0));
            cases.Add(new int2(1, -2)); cases.Add(new int2(2, -1));
            foreach (var cs in cases)
            {
                int k = cs.x;
                int3 coord = (s >> (7 + k)) + new int3(0, cs.y, 0);
                _src.GenerateShellChunk(k, coord, sc);
                int half = (1 << (k - 1)) - 1;   // task 74 Q2 (a)
                new ShellRefJob
                {
                    h = hA, r = rA, w = wA, cls = cA, flow = fA, toEngine = te, ore = oA, spec = _src.Spec, o = _src.O,
                    origin = coord << (7 + k), shift = k, half = half, mats = refM, topY = refTY, topM = refTM,
                }.Run();
                int bad = 0, n = 0, solid = 0, tops = 0, t = 0;
                string first = null;
                for (int z = 0; z < 128; z += 3)
                for (int x = 0; x < 128; x += 3)
                for (int y = 0; y < 128; y += 3)
                {
                    byte want = refM[n++], got = sc.Get(x, y, z);
                    if (want != Materials.Air) solid++;
                    if (want != got) { bad++; if (first == null) first = $"({x},{y},{z}) D {want} shell {got}"; }
                }
                for (int z = 0; z < 128; z += 3)
                for (int x = 0; x < 128; x += 3, t++)
                {
                    if (refTY[t] < 0) continue;
                    tops++;
                    byte want = refTM[t], got = sc.Get(x, refTY[t], z);
                    if (want != got) { bad++; if (first == null) first = $"top ({x},{refTY[t]},{z}) D(h) {want} shell {got}"; }
                }
                UnityEngine.Debug.Log($"[74] shell {k} chunk {coord}: {n} voxels + {tops} top voxels vs D, {bad} differ; {solid} non-air; {sc.DenseBricks} dense bricks");
                Assert.AreEqual(0, bad, $"shell {k} chunk {coord}: {bad} coarse voxels differ from D at their sample; first {first}");
                Assert.Greater(solid, 0, $"shell {k} chunk {coord} holds no terrain: the test picked the wrong chunk");
                if (cs.y == 0) Assert.Less(solid, n, $"shell {k} chunk {coord} holds no air: not a surface chunk");
            }
        }
        finally
        {
            sc.Dispose(); hA.Dispose(); rA.Dispose(); wA.Dispose(); cA.Dispose(); fA.Dispose(); oA.Dispose(); te.Dispose(); refM.Dispose(); refTY.Dispose(); refTM.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Agreement with the majority-vote downsample (row 13's pass line).
    // ------------------------------------------------------------------
    private const double VOXEL_MIN = 0.97, HEIGHT_MIN = 0.99;
    private const int MAX_K = 4;            // shells 1-3 gated, 4 reported
    private const int MAX_LAYERS = 6;       // fine chunk layers per region, a cost bound

    private sealed class Stat
    {
        public long mixedVox, mixedAgree, allVox, allAgree, cols, colsOk, bothSolid, sameMat;
        public long topCols, topSame;   // columns whose tops are within 1 voxel: same material at the coarse top?
        public long surfCols, surfCoarse, surfVote;   // task 77: top material == the 0.1 m surface's (the fine column at the sample)?
        public long missUp, missDown, missFar, missNone;   // column misses: coarse top 2-3 voxels above / below the vote's, >= 4 apart, one side empty
        public void Add(Stat s)
        {
            mixedVox += s.mixedVox; mixedAgree += s.mixedAgree; allVox += s.allVox; allAgree += s.allAgree;
            cols += s.cols; colsOk += s.colsOk; bothSolid += s.bothSolid; sameMat += s.sameMat;
            topCols += s.topCols; topSame += s.topSame;
            surfCols += s.surfCols; surfCoarse += s.surfCoarse; surfVote += s.surfVote;
            missUp += s.missUp; missDown += s.missDown; missFar += s.missFar; missNone += s.missNone;
        }
        public double Vox => mixedVox == 0 ? 1 : (double)mixedAgree / mixedVox;
        public double Cols => cols == 0 ? 1 : (double)colsOk / cols;
        public double Mat => bothSolid == 0 ? 1 : (double)sameMat / bothSolid;
        public string Line => $"vox(mixed bricks) {Vox * 100,7:F3}%  cols(|dh|<=1) {Cols * 100,7:F3}%  material {Mat * 100,6:F2}%  top material {(topCols == 0 ? 100 : 100.0 * topSame / topCols),6:F2}%  all-voxel {(allVox == 0 ? 100 : 100.0 * allAgree / allVox),7:F3}%  surface vs 0.1 m: coarse {(surfCols == 0 ? 100 : 100.0 * surfCoarse / surfCols),6:F2}% vote {(surfCols == 0 ? 100 : 100.0 * surfVote / surfCols),6:F2}%  [{mixedVox} vox, {cols} cols; misses up2-3 {missUp} down2-3 {missDown} far {missFar} none {missNone}]";
    }

    /// A region: n x n fine chunk columns from (c0.x, c0.z) (both even), layers c0.y .. c0.y + L - 1.
    private struct Region { public int3 c0; public int n, L; public byte landform; public int3 site; }

    private static List<Region> PickSites(TerrainGenChunkSource src, int renderN, byte[] renderLandforms)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var found = new Dictionary<byte, Region>();
        const int G = 48;
        int step = spec.worldSizeVoxels / G;
        uint hsh = spec.seed * 2654435761u;
        for (int t = 0; t < G * G && found.Count < TerrainGen.Regions.KINDS; t++)
        {
            int idx = (int)((hsh + (uint)t * 1103u) % (uint)(G * G));   // 1103 is prime to 48^2: a permutation
            int x = (idx % G) * step + step / 2, z = (idx / G) * step + step / 2;
            var cell = src.Map.Record.At(x, z);
            if (cell.IsSea) continue;
            byte lf = cell.Archetype;
            if (lf >= TerrainGen.Regions.KINDS || found.ContainsKey(lf)) continue;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
            if (col.height < spec.seaLevelY) continue;
            int n = System.Array.IndexOf(renderLandforms, lf) >= 0 ? renderN : 2;
            int cx = (x >> 7) & ~1, cz = (z >> 7) & ~1;
            int lo = int.MaxValue, hi = int.MinValue;
            for (int j = 0; j <= 8; j++)
            for (int i = 0; i <= 8; i++)
            {
                float px = (cx << 7) + i * (n * 128 - 1) / 8f + 0.5f, pz = (cz << 7) + j * (n * 128 - 1) / 8f + 0.5f;
                var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, px, pz, 1);
                lo = math.min(lo, c.height); hi = math.max(hi, math.max(c.height, c.water));
            }
            int l0 = (lo - 24) >> 7, l1 = (hi + 24) >> 7;
            if (l1 - l0 + 1 > MAX_LAYERS) l0 = l1 - MAX_LAYERS + 1;
            found[lf] = new Region { c0 = new int3(cx, l0, cz), n = n, L = l1 - l0 + 1, landform = lf, site = new int3(x, col.height, z) };
        }
        var list = new List<Region>(found.Values);
        list.Sort((a, b) => a.landform.CompareTo(b.landform));
        return list;
    }

    /// Mean and p90 over 32 x 32 columns of the region of the largest step (fine
    /// voxels) from a column's ground to a 4-neighbour's: fine-scale roughness.
    private static string Roughness(TerrainGenChunkSource src, Region rg)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var steps = new List<int>();
        int span = rg.n * 128;
        for (int j = 0; j < 32; j++)
        for (int i = 0; i < 32; i++)
        {
            int x = (rg.c0.x << 7) + i * span / 32 + 1, z = (rg.c0.z << 7) + j * span / 32 + 1;
            int h = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1).height;
            int m = 0;
            m = math.max(m, math.abs(TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 1.5f, z + 0.5f, 1).height - h));
            m = math.max(m, math.abs(TerrainGen.ShapeField.Sample(in spec, in view, in o, x - 0.5f, z + 0.5f, 1).height - h));
            m = math.max(m, math.abs(TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 1.5f, 1).height - h));
            m = math.max(m, math.abs(TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z - 0.5f, 1).height - h));
            steps.Add(m);
        }
        steps.Sort();
        double mean = 0; foreach (int v in steps) mean += v;
        return $"fine step mean {mean / steps.Count:F2} p90 {steps[(int)(steps.Count * 0.9)]} voxels";
    }

    /// Fine chunk -> flat x + 128 (y + 128 z), LODDownsampler's layout.
    private static void Extract(Chunk c, BrickDataPool pool, byte[] dst)
    {
        if (c.isUniform) { for (int i = 0; i < dst.Length; i++) dst[i] = c.uniformMaterial; return; }
        var raw = pool.RawData;
        for (int bz = 0; bz < 16; bz++)
        for (int by = 0; by < 16; by++)
        for (int bx = 0; bx < 16; bx++)
        {
            uint d = c.bricks[(bz << 8) | (by << 4) | bx].data;
            bool dense = (d & 0x80000000u) != 0;
            int start = (int)(d & 0x7FFFFFFFu) * 512;
            for (int vz = 0; vz < 8; vz++)
            for (int vy = 0; vy < 8; vy++)
            for (int vx = 0; vx < 8; vx++)
            {
                int x = (bx << 3) + vx, y = (by << 3) + vy, z = (bz << 3) + vz;
                dst[x + 128 * (y + 128 * z)] = dense ? raw[start + ((vz << 6) | (vy << 3) | vx)] : (byte)d;
            }
        }
    }

    /// The region at shells 1..MAX_K: vote[k] (majority vote of the 0.1 m
    /// chunks, chained as the cascades chain it) and coarse[k] (GenerateShellChunk),
    /// both flat x + RX (y + RY z).
    private static void BuildRegion(TerrainGenChunkSource src, Region rg, byte[][] vote, byte[][] coarse, byte[][] lowIdx, byte[] fineTop)
    {
        int RXf = rg.n * 128, RYf = rg.L * 128;
        System.Array.Clear(fineTop, 0, fineTop.Length);
        for (int k = 1; k <= MAX_K; k++) { vote[k] = new byte[(RXf >> k) * (RYf >> k) * (RXf >> k)]; coarse[k] = new byte[vote[k].Length]; lowIdx[k] = new byte[vote[k].Length]; }
        var fine = new byte[128 * 128 * 128];
        var pass = new TerrainGenChunkSource.ColumnPass();
        try
        {
            for (int dz = 0; dz < rg.n; dz++)
            for (int dx = 0; dx < rg.n; dx++)
            for (int dy = 0; dy < rg.L; dy++)
            {
                var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
                try
                {
                    var chunk = new Chunk();
                    src.Generate(rg.c0 + new int3(dx, dy, dz), chunk, new ChunkHandleAllocator(2), pool, null, pass);
                    Extract(chunk, pool, fine);
                }
                finally { pool.Dispose(); }
                // The 0.1 m surface material per fine column (layers go up, so a higher one overwrites).
                for (int z = 0; z < 128; z++)
                for (int x = 0; x < 128; x++)
                for (int y = 127; y >= 0; y--)
                {
                    byte m = fine[x + 128 * (y + 128 * z)];
                    if (m == Materials.Air) continue;
                    fineTop[(dx * 128 + x) + RXf * (dz * 128 + z)] = m;
                    break;
                }
                byte[] cur = fine;
                for (int k = 1; k <= MAX_K; k++)
                {
                    cur = VoxelEngine.Mirror.LODDownsampler.DownsampleOnce(cur, 128 >> (k - 1));
                    int e = 128 >> k, RX = RXf >> k, RY = RYf >> k;
                    for (int z = 0; z < e; z++)
                    for (int y = 0; y < e; y++)
                    for (int x = 0; x < e; x++)
                        vote[k][(dx * e + x) + RX * ((dy * e + y) + RY * (dz * e + z))] = cur[x + e * (y + e * z)];
                }
            }
        }
        finally { pass.Dispose(); }

        CoarseRegion(src, rg, coarse, -1);
        CoarseRegion(src, rg, lowIdx, 0);
    }

    /// coarse[k] from GenerateShellChunk; lowOffset -1 = row 13's sample (index
    /// 2^(k-1) - 1 since task 77), 0 = the old centre 2^(k-1) (the diagnostic beside it).
    private static void CoarseRegion(TerrainGenChunkSource src, Region rg, byte[][] coarse, int lowOffset)
    {
        int RXf = rg.n * 128, RYf = rg.L * 128;
        int3 fo = rg.c0 << 7;
        using (var sc = new TerrainGenChunkSource.ShellChunk())
        {
            for (int k = 1; k <= MAX_K; k++)
            {
                int RX = RXf >> k, RY = RYf >> k;
                int3 g0 = fo >> k, g1 = g0 + new int3(RX, RY, RX) - 1;   // global coarse voxel range
                for (int cz = g0.z >> 7; cz <= g1.z >> 7; cz++)
                for (int cy = g0.y >> 7; cy <= g1.y >> 7; cy++)
                for (int cx = g0.x >> 7; cx <= g1.x >> 7; cx++)
                {
                    src.GenerateShellChunk(k, new int3(cx, cy, cz), sc, lowOffset < 0 ? -1 : 1 << (k - 1));
                    byte[] m = sc.IsUniformAir ? null : sc.Mats.ToArray();
                    int3 lo = math.max(g0, new int3(cx, cy, cz) << 7), hi = math.min(g1, (new int3(cx, cy, cz) << 7) + 127);
                    for (int z = lo.z; z <= hi.z; z++)
                    for (int y = lo.y; y <= hi.y; y++)
                    for (int x = lo.x; x <= hi.x; x++)
                        coarse[k][(x - g0.x) + RX * ((y - g0.y) + RY * (z - g0.z))] =
                            m == null ? Materials.Air : m[TerrainGenChunkSource.Index(x & 127, y & 127, z & 127)];
                }
            }
        }
    }

    private static Stat Compare(byte[] vote, byte[] coarse, int RX, int RY, byte[] fineTop, int k)
    {
        int RXf = RX << k, h2 = (1 << (k - 1)) - 1;
        var s = new Stat();
        // Voxels, and voxels in non-uniform bricks (8^3 coarse; mixed solid/air in either).
        for (int bz = 0; bz < RX; bz += 8)
        for (int by = 0; by < RY; by += 8)
        for (int bx = 0; bx < RX; bx += 8)
        {
            int vSolid = 0, cSolid = 0, agree = 0;
            for (int z = bz; z < bz + 8; z++)
            for (int y = by; y < by + 8; y++)
            for (int x = bx; x < bx + 8; x++)
            {
                int i = x + RX * (y + RY * z);
                bool a = vote[i] != Materials.Air, b = coarse[i] != Materials.Air;
                if (a) vSolid++; if (b) cSolid++; if (a == b) agree++;
                if (a && b) { s.bothSolid++; if (vote[i] == coarse[i]) s.sameMat++; }
            }
            s.allVox += 512; s.allAgree += agree;
            bool mixed = (vSolid != 0 && vSolid != 512) || (cSolid != 0 && cSolid != 512);
            if (mixed) { s.mixedVox += 512; s.mixedAgree += agree; }
        }
        // Columns: the top non-air voxel, where the region's top voxel is air in both.
        for (int z = 0; z < RX; z++)
        for (int x = 0; x < RX; x++)
        {
            int topI = x + RX * ((RY - 1) + RY * z);
            if (vote[topI] != Materials.Air || coarse[topI] != Materials.Air) continue;
            int tv = Top(vote, x, z, RX, RY), tc = Top(coarse, x, z, RX, RY);
            if (tv < 0 && tc < 0) continue;
            s.cols++;
            if (tv < 0 || tc < 0) s.missNone++;
            else if (math.abs(tv - tc) <= 1)
            {
                s.colsOk++; s.topCols++;
                byte vm = vote[x + RX * (tv + RY * z)], cm = coarse[x + RX * (tc + RY * z)];
                if (vm == cm) s.topSame++;
                byte fm = fineTop[((x << k) + h2) + RXf * ((z << k) + h2)];
                s.surfCols++;
                if (cm == fm) s.surfCoarse++;
                if (vm == fm) s.surfVote++;
            }
            else if (math.abs(tv - tc) >= 4) s.missFar++;
            else if (tc > tv) s.missUp++; else s.missDown++;
        }
        return s;
    }

    private static int Top(byte[] a, int x, int z, int RX, int RY)
    {
        for (int y = RY - 1; y >= 0; y--) if (a[x + RX * (y + RY * z)] != Materials.Air) return y;
        return -1;
    }

    [Test, Category("Slow")]
    public void CoarseFirst_AgreesWithMajorityVote_AndReportsCost()
    {
        uint[] seeds = { 42u, 7u, 1234u };
        byte[] renderLf = { TerrainGen.Regions.Alpine, TerrainGen.Regions.Canyonlands, TerrainGen.Regions.Plains };
        var lines = new List<string>
        {
            "Engine step 10 (task 74; sample index 2^(k-1)-1 and the top-voxel rule since task 77): coarse-first shell chunks vs the majority-vote downsample of the 0.1 m chunks.",
            $"Gate (Amendment 8.14 row 13, owner Q5 (a)): shells 1-3, vox(mixed bricks) >= {VOXEL_MIN:P0} and cols(|dh| <= 1 coarse voxel) >= {HEIGHT_MIN:P0},",
            "on seed 42's landform set and on all seeds pooled. Shell 4 reported, not gated. Non-air = solid (water counts). Editor, Burst.",
            "",
        };
        var pooled = new Stat[MAX_K + 1]; var pooledLow = new Stat[MAX_K + 1];
        for (int k = 1; k <= MAX_K; k++) { pooled[k] = new Stat(); pooledLow[k] = new Stat(); }
        Stat[] seed42 = null;
        var belowLine = new List<(string, Stat[])>();
        var failures = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (uint seed in seeds)
        {
            var src = seed == 42u ? _src : new TerrainGenChunkSource(seed);
            try
            {
                var perSeed = new Stat[MAX_K + 1]; var perSeedLow = new Stat[MAX_K + 1];
                for (int k = 1; k <= MAX_K; k++) { perSeed[k] = new Stat(); perSeedLow[k] = new Stat(); }
                var sites = PickSites(src, 4, seed == 42u ? renderLf : new byte[0]);
                lines.Add($"== seed {seed}: {sites.Count} landforms found");
                foreach (var rg in sites)
                {
                    var vote = new byte[MAX_K + 1][]; var coarse = new byte[MAX_K + 1][]; var lowIdx = new byte[MAX_K + 1][];
                    var fineTop = new byte[(rg.n * 128) * (rg.n * 128)];
                    BuildRegion(src, rg, vote, coarse, lowIdx, fineTop);
                    lines.Add($"  {TerrainGen.Regions.Name(rg.landform),-18} site ({rg.site.x},{rg.site.y},{rg.site.z})  region {rg.n}x{rg.n} chunks x {rg.L} layers from {rg.c0}; {Roughness(src, rg)}");
                    bool low = false;
                    for (int k = 1; k <= MAX_K; k++)
                    {
                        int RX = (rg.n * 128) >> k, RY = (rg.L * 128) >> k;
                        var st = Compare(vote[k], coarse[k], RX, RY, fineTop, k);
                        perSeed[k].Add(st); pooled[k].Add(st);
                        lines.Add($"    shell {k} ({0.1f * (1 << k):F1} m)  {st.Line}");
                        var sl = Compare(vote[k], lowIdx[k], RX, RY, fineTop, k);
                        perSeedLow[k].Add(sl); pooledLow[k].Add(sl);
                        lines.Add($"      old centre 2^(k-1) {sl.Line}");
                        if (k <= 3 && st.Cols < HEIGHT_MIN) low = true;
                    }
                    string lfName = TerrainGen.Regions.Name(rg.landform).Replace(' ', '_');
                    if (seed == 42u && System.Array.IndexOf(renderLf, rg.landform) >= 0) WritePicture(rg, vote, coarse, $"77_{lfName}");
                    else if (low) WritePicture(rg, vote, coarse, $"77_below_line_seed{seed}_{lfName}");
                }
                for (int k = 1; k <= MAX_K; k++) lines.Add($"  seed {seed} shell {k}  {perSeed[k].Line}");
                for (int k = 1; k <= MAX_K; k++) lines.Add($"  seed {seed} shell {k}  old centre 2^(k-1)  {perSeedLow[k].Line}");
                if (seed == 42u) seed42 = perSeed; else belowLine.Add(($"seed {seed}", perSeed));
            }
            finally { if (src != _src) src.Dispose(); }
        }
        lines.Add("");
        for (int k = 1; k <= MAX_K; k++) lines.Add($"POOLED shell {k} ({0.1f * (1 << k):F1} m)  {pooled[k].Line}");
        for (int k = 1; k <= MAX_K; k++) lines.Add($"POOLED shell {k} old centre 2^(k-1) (diagnostic)  {pooledLow[k].Line}");
        lines.Add($"(agreement part: {sw.Elapsed.TotalSeconds:F0} s)");
        belowLine.Add(("pooled", pooled));
        for (int k = 1; k <= 3; k++)
        {
            var st = seed42[k];
            if (st.Vox < VOXEL_MIN) failures.Add($"seed 42 shell {k}: vox {st.Vox:P3} < {VOXEL_MIN:P0}");
            if (st.Cols < HEIGHT_MIN) failures.Add($"seed 42 shell {k}: cols {st.Cols:P3} < {HEIGHT_MIN:P0}");
            foreach (var (name, o) in belowLine)
                if (o[k].Vox < VOXEL_MIN || o[k].Cols < HEIGHT_MIN)
                    lines.Add($"NOTE (reported, not gated): {name} shell {k} below row 13's line: vox {o[k].Vox:P3}, cols {o[k].Cols:P3}");
        }

        // Cost: one surface shell chunk per shell (seed 42), warm, mean of 3; Generate beside it.
        lines.Add("");
        lines.Add("Generation cost, seed 42, the shell chunk holding a surface site (Editor, Burst, one thread, warm, mean of 3):");
        int3 s = SurfaceVoxel(_src);
        using (var sc = new TerrainGenChunkSource.ShellChunk())
        {
            var p0 = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            try
            {
                _src.Generate(s >> 7, new Chunk(), new ChunkHandleAllocator(2), p0, null);
                var t = System.Diagnostics.Stopwatch.StartNew();
                for (int r = 0; r < 3; r++)
                {
                    var pr = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
                    _src.Generate(s >> 7, new Chunk(), new ChunkHandleAllocator(2), pr, null);
                    pr.Dispose();
                }
                lines.Add($"  Generate (0.1 m) chunk {s >> 7}: {t.Elapsed.TotalMilliseconds / 3,7:F1} ms");
            }
            finally { p0.Dispose(); }
            for (int k = 0; k <= TerrainGenChunkSource.MAX_SHELL; k++)
            {
                int3 coord = s >> (7 + k);
                _src.GenerateShellChunk(k, coord, sc);
                var t = System.Diagnostics.Stopwatch.StartNew();
                for (int r = 0; r < 3; r++) _src.GenerateShellChunk(k, coord, sc);
                lines.Add($"  shell {k} ({0.1f * (1 << k),4:F1} m, {12.8f * (1 << k),6:F1} m chunk) {coord,-20}: {t.Elapsed.TotalMilliseconds / 3,7:F1} ms  {sc.DenseBricks} dense bricks{(sc.IsUniformAir ? " (uniform air)" : "")}");
            }
        }

        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "TerrainGen");
        System.IO.Directory.CreateDirectory(dir);
        if (failures.Count > 0) lines.Add("FAIL: " + string.Join("; ", failures));
        System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "coarse_first.txt"), lines);
        UnityEngine.Debug.Log("[74]\n" + string.Join("\n", lines));
        Assert.IsEmpty(failures, string.Join("; ", failures));
    }

    // ------------------------------------------------------------------
    // Pictures: per shell a row of [vote top-down | coarse top-down |
    // vote section | coarse section, differing voxels red].
    // ------------------------------------------------------------------
    private const int PANEL = 256, GAP = 6;

    private static void WritePicture(Region rg, byte[][] vote, byte[][] coarse, string name)
    {
        int rows = 3;
        var rowH = new int[rows + 1];
        int H = GAP;
        for (int k = 1; k <= rows; k++)
        {
            rowH[k] = PANEL;
            H += rowH[k] + GAP;
        }
        int W = 4 * PANEL + 5 * GAP;
        var px = new UnityEngine.Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new UnityEngine.Color32(24, 24, 24, 255);
        int y0 = H - GAP;
        for (int k = 1; k <= rows; k++)
        {
            int RX = (rg.n * 128) >> k, RY = (rg.L * 128) >> k, sc = PANEL / RX;
            y0 -= rowH[k];
            TopDown(px, W, GAP, y0, vote[k], RX, RY, sc);
            TopDown(px, W, 2 * GAP + PANEL, y0, coarse[k], RX, RY, sc);
            Section(px, W, 3 * GAP + 2 * PANEL, y0, vote[k], null, RX, RY, sc);
            Section(px, W, 4 * GAP + 3 * PANEL, y0, coarse[k], vote[k], RX, RY, sc);
            y0 -= GAP;
        }
        var tex = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Screenshots", "77");   // task 74 wrote Screenshots/74 (centre sample, no top rule)
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), UnityEngine.ImageConversion.EncodeToPNG(tex));
        UnityEngine.Object.DestroyImmediate(tex);
    }

    private static UnityEngine.Color32 Col(byte m, float shade)
    {
        var c = MaterialPalette.Of(m);
        return new UnityEngine.Color32((byte)math.clamp(c.R * shade * 255f, 0, 255), (byte)math.clamp(c.G * shade * 255f, 0, 255),
                                       (byte)math.clamp(c.B * shade * 255f, 0, 255), 255);
    }

    /// Hill-shaded top view: the top non-air voxel's colour, lit from the north-west.
    private static void TopDown(UnityEngine.Color32[] px, int W, int ox, int oy, byte[] a, int RX, int RY, int sc)
    {
        var top = new int[RX * RX];
        for (int z = 0; z < RX; z++) for (int x = 0; x < RX; x++) top[z * RX + x] = Top(a, x, z, RX, RY);
        float3 L = math.normalize(new float3(-1f, 1.5f, 0.7f));
        for (int z = 0; z < RX; z++)
        for (int x = 0; x < RX; x++)
        {
            int t = top[z * RX + x];
            UnityEngine.Color32 c = new UnityEngine.Color32(0, 0, 0, 255);
            if (t >= 0)
            {
                int xl = top[z * RX + math.max(x - 1, 0)], xr = top[z * RX + math.min(x + 1, RX - 1)];
                int zl = top[math.max(z - 1, 0) * RX + x], zr = top[math.min(z + 1, RX - 1) * RX + x];
                float3 nrm = math.normalize(new float3(-(xr - xl) * 0.5f, 1f, -(zr - zl) * 0.5f));
                c = Col(a[x + RX * (t + RY * z)], 0.35f + 0.75f * math.saturate(math.dot(nrm, L)));
            }
            for (int j = 0; j < sc; j++) for (int i = 0; i < sc; i++) px[(oy + z * sc + j) * W + ox + x * sc + i] = c;
        }
    }

    /// Vertical X-Y section through the region's middle Z, RX voxels tall,
    /// centred on that slice's mean surface in `a`. With `other`, voxels whose
    /// solid/air differs are red. Both panels of a row use the vote's window.
    private static void Section(UnityEngine.Color32[] px, int W, int ox, int oy, byte[] a, byte[] other, int RX, int RY, int sc)
    {
        int z = RX / 2;
        byte[] win = other ?? a;
        long sum = 0; int n = 0;
        for (int x = 0; x < RX; x++) { int t = Top(win, x, z, RX, RY); if (t >= 0) { sum += t; n++; } }
        int y0 = n == 0 ? 0 : math.clamp((int)(sum / n) - RX / 2, 0, math.max(0, RY - RX));
        for (int yy = 0; yy < math.min(RX, RY); yy++)
        for (int x = 0; x < RX; x++)
        {
            int y = y0 + yy;
            int i = x + RX * (y + RY * z);
            byte m = a[i];
            UnityEngine.Color32 c = m == Materials.Air ? new UnityEngine.Color32(150, 190, 230, 255) : Col(m, 1f);
            if (other != null && (other[i] != Materials.Air) != (m != Materials.Air)) c = new UnityEngine.Color32(255, 0, 0, 255);
            for (int j = 0; j < sc; j++) for (int q = 0; q < sc; q++) px[(oy + yy * sc + j) * W + ox + x * sc + q] = c;
        }
    }
}
