// Assets/CoreEngine/Tests/TreeTests.cs
//
// Task 180 (placement, first test, `-trees 1`): generated voxel trees
// (TerrainGen.Trees; TerrainGenChunkSource.Trees.cs).
//   Chunks_Deterministic_Border_PointAndShellsAgree   seed 42, a forest tree whose
//       crown crosses a chunk border: ON twice = same bytes; ON vs OFF differ only
//       where OFF is air and ON is a tree material, on BOTH sides of the border;
//       FarQuery's point path = the chunk bytes; shell 0 = Generate; shells 1-3:
//       every tree voxel above the ground equals the 0.1 m voxel at its sample.
//   FourSeeds_Rules_Sheets (Slow)   seeds 42 / 7 / 2026 / 31337, 1/16 of the world
//       scanned: no tree on a sea / route / feature / Sheer record cell, in water or
//       on a > 45 deg foot; 30 trees per biome family (+ the steepest 30) voxelised
//       over the LOD-0 ground: none floating (every tree voxel 6-connected to one
//       resting on the ground) or rock-embedded (crown voxels under the ground
//       <= 2 %); sheets Screenshots/180/sheet_<seed>_<biome>.png (side view,
//       rejected trees framed red); log Logs/TerrainGen/trees180/rules.txt.
//   Cost_OnVsOff (Slow)   dense bricks and generation time, ON vs OFF, over a forest
//       and a jungle patch at LOD 0 and shells 1-5 -> Logs/TerrainGen/trees180/cost.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class TreeTests
{
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "trees180");
    private static readonly string ShotDir = Path.Combine("Screenshots", "180");

    // ------------------------------------------------------------------
    // One seed's source + the map arrays the test jobs read
    // ------------------------------------------------------------------
    private sealed class Ctx : IDisposable
    {
        public readonly TerrainGenChunkSource src;
        public NativeArray<float> h, r, w;
        public NativeArray<byte> cls, route, plane;
        public NativeArray<ushort> flow;
        public Ctx(uint seed)
        {
            src = new TerrainGenChunkSource(seed);
            src.SetTrees(true);
            var m = src.Map;
            h = new NativeArray<float>(m.Height, Allocator.Persistent);
            r = new NativeArray<float>(m.Rough, Allocator.Persistent);
            w = new NativeArray<float>(m.Water, Allocator.Persistent);
            cls = new NativeArray<byte>(m.WaterClass, Allocator.Persistent);
            flow = new NativeArray<ushort>(m.FlowDir ?? new ushort[1], Allocator.Persistent);
            route = new NativeArray<byte>(m.RouteMask ?? new byte[1], Allocator.Persistent);
            plane = new NativeArray<byte>(TreeTable.DensityPlane(m.Record, src.Spec.mapCells), Allocator.Persistent);
        }
        public void Dispose()
        {
            h.Dispose(); r.Dispose(); w.Dispose(); cls.Dispose(); flow.Dispose(); route.Dispose(); plane.Dispose();
            src.Dispose();
        }
    }

    private static unsafe MapView View(NativeArray<float> h, NativeArray<float> r, NativeArray<float> w, NativeArray<byte> cls,
                                       NativeArray<ushort> flow, NativeArray<byte> route, NativeArray<byte> plane, in TerrainSpec spec)
        => new MapView
        {
            height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
            water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
            flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,
            route = route.Length > 1 ? (byte*)route.GetUnsafeReadOnlyPtr() : null,
            trees = plane.Length > 1 ? (byte*)plane.GetUnsafeReadOnlyPtr() : null,
            cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
        };

    // ------------------------------------------------------------------
    // Burst jobs: scan tree cells; voxelise one tree over the LOD-0 ground
    // ------------------------------------------------------------------
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    private struct ScanJob : IJob
    {
        [ReadOnly] public NativeArray<float> h, r, w;
        [ReadOnly] public NativeArray<byte> cls, route, plane;
        [ReadOnly] public NativeArray<ushort> flow;
        public TerrainSpec spec; public Offsets o;
        [ReadOnly] public NativeArray<int4> windows;   // tree-cell rectangles (cx0, cz0, cx1, cz1), inclusive
        public NativeList<Tree> trees;
        public NativeList<TreePrim> prims;
        public NativeList<int2> info;                  // per tree: (cover byte of the root sample, water over root? 1 : 0)

        public unsafe void Execute()
        {
            var map = View(h, r, w, cls, flow, route, plane, in spec);
            var buf = stackalloc TreePrim[Trees.MAX_PRIMS];
            for (int k = 0; k < windows.Length; k++)
            {
                int4 win = windows[k];
                for (int cz = win.y; cz <= win.w; cz++)
                for (int cx = win.x; cx <= win.z; cx++)
                {
                    int n = 0;
                    if (!Trees.Build(in spec, in map, in o, cx, cz, buf, ref n, out Tree t)) continue;
                    t.prim0 = prims.Length;
                    for (int i = 0; i < n; i++) prims.Add(buf[i]);
                    trees.Add(t);
                    var c = ShapeField.Sample(in spec, in map, in o, t.root.x + 0.5f, t.root.z + 0.5f, 1);
                    info.Add(new int2(c.cover, c.water != ColumnSample.NO_WATER_Y && c.water >= c.height ? 1 : 0));
                }
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
    private struct VoxeliseJob : IJob
    {
        [ReadOnly] public NativeArray<float> h, r, w;
        [ReadOnly] public NativeArray<byte> cls, route, plane;
        [ReadOnly] public NativeArray<ushort> flow;
        public TerrainSpec spec; public Offsets o;
        [ReadOnly] public NativeArray<TreePrim> prims;   // this tree's
        public int3 lo, dim;
        public NativeArray<int> ground;                  // dim.x * dim.z
        public NativeArray<byte> mats;                   // dim.x * dim.y * dim.z, tree material ABOVE the ground, else 0
        public NativeArray<int> counts;                  // [0] crown voxels under the ground, [1] trunk voxels under the ground above the
                                                         // trunk's foot (max ground within the trunk radius + 2, + 2), [2] crown voxels total

        public unsafe void Execute()
        {
            var map = View(h, r, w, cls, flow, route, plane, in spec);
            var p = (TreePrim*)prims.GetUnsafeReadOnlyPtr();
            int3 root = prims[0].root;
            float fr = math.max(prims[0].r0, prims[0].r1) + 2f;
            int foot = int.MinValue;
            for (int z = 0; z < dim.z; z++)
            for (int x = 0; x < dim.x; x++)
            {
                int gx = lo.x + x, gz = lo.z + z;
                int g = ShapeField.Sample(in spec, in map, in o, gx + 0.5f, gz + 0.5f, 1, false).height;
                ground[z * dim.x + x] = g;
                float dx = gx - root.x, dz = gz - root.z;
                if (dx * dx + dz * dz <= fr * fr) foot = math.max(foot, g);
            }
            for (int z = 0; z < dim.z; z++)
            for (int x = 0; x < dim.x; x++)
            {
                int gx = lo.x + x, gz = lo.z + z;
                int g = ground[z * dim.x + x];
                for (int y = 0; y < dim.y; y++)
                {
                    int gy = lo.y + y;
                    byte m = Trees.MaterialAt(p, prims.Length, new int3(gx, gy, gz));
                    if (m == GenMat.Air) { mats[(y * dim.z + z) * dim.x + x] = 0; continue; }
                    bool leaf = GenMat.IsTreeLeaf(m);
                    if (leaf) counts[2]++;
                    if (gy <= g)
                    {
                        if (leaf) counts[0]++;
                        else if (gy > foot + 2) counts[1]++;
                        mats[(y * dim.z + z) * dim.x + x] = 0;
                    }
                    else mats[(y * dim.z + z) * dim.x + x] = m;
                }
            }
        }
    }

    private sealed class Scan : IDisposable
    {
        public NativeList<Tree> trees = new NativeList<Tree>(1024, Allocator.Persistent);
        public NativeList<TreePrim> prims = new NativeList<TreePrim>(8192, Allocator.Persistent);
        public NativeList<int2> info = new NativeList<int2>(1024, Allocator.Persistent);
        public void Dispose() { trees.Dispose(); prims.Dispose(); info.Dispose(); }
    }

    private static Scan RunScan(Ctx c, List<int4> windows)
    {
        var s = new Scan();
        var win = new NativeArray<int4>(windows.ToArray(), Allocator.TempJob);
        try
        {
            new ScanJob
            {
                h = c.h, r = c.r, w = c.w, cls = c.cls, route = c.route, plane = c.plane, flow = c.flow,
                spec = c.src.Spec, o = c.src.O, windows = win, trees = s.trees, prims = s.prims, info = s.info,
            }.Run();
        }
        finally { win.Dispose(); }
        return s;
    }

    /// One tree voxelised over the LOD-0 ground and checked.
    private sealed class TreeVox
    {
        public Tree t; public int3 lo, dim; public int[] ground; public byte[] mats;
        public int crownUnder, trunkUnder, crownTotal, voxels, floating;
        public bool trunkGrounded;
        public bool Rejected => floating > 0 || !trunkGrounded || crownTotal > 0 && crownUnder * 50 > crownTotal || trunkUnder > 0;
        public string Why => floating > 0 ? $"floating {floating} vox" : !trunkGrounded ? "trunk off the ground"
                           : trunkUnder > 0 ? $"trunk in rock {trunkUnder}" : $"crown in rock {crownUnder}/{crownTotal}";
    }

    private static TreeVox Voxelise(Ctx c, Scan s, int ti)
    {
        var t = s.trees[ti];
        var v = new TreeVox { t = t, lo = t.lo, dim = t.hi - t.lo + 1 };
        var prims = new NativeArray<TreePrim>(t.primCount, Allocator.TempJob);
        for (int i = 0; i < t.primCount; i++) prims[i] = s.prims[t.prim0 + i];
        var ground = new NativeArray<int>(v.dim.x * v.dim.z, Allocator.TempJob);
        var mats = new NativeArray<byte>(v.dim.x * v.dim.y * v.dim.z, Allocator.TempJob);
        var counts = new NativeArray<int>(3, Allocator.TempJob);
        try
        {
            new VoxeliseJob
            {
                h = c.h, r = c.r, w = c.w, cls = c.cls, route = c.route, plane = c.plane, flow = c.flow,
                spec = c.src.Spec, o = c.src.O, prims = prims, lo = v.lo, dim = v.dim, ground = ground, mats = mats, counts = counts,
            }.Run();
            v.ground = ground.ToArray(); v.mats = mats.ToArray();
            v.crownUnder = counts[0]; v.trunkUnder = counts[1]; v.crownTotal = counts[2];
        }
        finally { prims.Dispose(); ground.Dispose(); mats.Dispose(); counts.Dispose(); }

        // FLOATING: flood from every tree voxel resting on the ground through tree voxels (6-connected).
        int nx = v.dim.x, ny = v.dim.y, nz = v.dim.z;
        var seen = new bool[v.mats.Length];
        var q = new Queue<int>();
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int y = v.ground[z * nx + x] + 1 - v.lo.y;
            if (y < 0 || y >= ny) continue;
            int i = (y * nz + z) * nx + x;
            if (v.mats[i] != 0 && !seen[i]) { seen[i] = true; q.Enqueue(i); }
        }
        // the trunk stands on the ground at its root column
        int rx = t.root.x - v.lo.x, rz = t.root.z - v.lo.z, ry = v.ground[rz * nx + rx] + 1 - v.lo.y;
        v.trunkGrounded = ry >= 0 && ry < ny && GenMat.IsTreeTrunk(v.mats[(ry * nz + rz) * nx + rx]);
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % nx, z = (i / nx) % nz, y = i / (nx * nz);
            for (int d = 0; d < 6; d++)
            {
                int ax = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ay = y + (d == 2 ? 1 : d == 3 ? -1 : 0), az = z + (d == 4 ? 1 : d == 5 ? -1 : 0);
                if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz) continue;
                int j = (ay * nz + az) * nx + ax;
                if (seen[j] || v.mats[j] == 0) continue;
                seen[j] = true; q.Enqueue(j);
            }
        }
        for (int i = 0; i < v.mats.Length; i++)
            if (v.mats[i] != 0) { v.voxels++; if (!seen[i]) v.floating++; }
        return v;
    }

    // ------------------------------------------------------------------
    // Sheets: side view (looking along +z), 6 x 5 panels, rejected framed red
    // ------------------------------------------------------------------
    private const int PANEL_W = 132, PANEL_H = 262, SHEET_COLS = 6, SHEET_ROWS = 5;

    private static void WriteSheet(string path, List<TreeVox> vox, string title)
    {
        int W = PANEL_W * SHEET_COLS, H = PANEL_H * SHEET_ROWS;
        var px = new UnityEngine.Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new UnityEngine.Color32(196, 214, 232, 255);
        for (int k = 0; k < vox.Count && k < SHEET_COLS * SHEET_ROWS; k++)
        {
            var v = vox[k];
            int ox = (k % SHEET_COLS) * PANEL_W, oyTop = (k / SHEET_COLS) * PANEL_H;
            int nx = v.dim.x, ny = v.dim.y, nz = v.dim.z;
            int baseY = v.t.root.y - 12;   // panel bottom row = 12 voxels under the root
            int cx = (PANEL_W - nx) / 2;
            int rz = v.t.root.z - v.lo.z;
            for (int x = 0; x < nx; x++)
            {
                int g = v.ground[rz * nx + x];
                for (int py = 0; py < PANEL_H - 2; py++)
                {
                    int wy = baseY + py;
                    int sx = ox + cx + x, sy = H - 1 - (oyTop + (PANEL_H - 2 - py));
                    if (sx < ox || sx >= ox + PANEL_W || sy < 0 || sy >= H) continue;
                    UnityEngine.Color32 col;
                    int ly = wy - v.lo.y;
                    byte m = 0; int depth = 0;
                    if (ly >= 0 && ly < ny)
                        for (int z = 0; z < nz; z++) { byte mm = v.mats[(ly * nz + z) * nx + x]; if (mm != 0) { m = mm; depth = z; break; } }
                    if (m != 0)
                    {
                        var c = MaterialPalette.Of(VoxelEngine.Rendering.FarField.GenToEngine(m));
                        float shade = 1.15f - 0.55f * depth / math.max(1, nz);
                        col = new UnityEngine.Color32((byte)math.clamp(c.R * 255 * shade, 0, 255), (byte)math.clamp(c.G * 255 * shade, 0, 255),
                                                      (byte)math.clamp(c.B * 255 * shade, 0, 255), 255);
                    }
                    else if (wy <= g) col = new UnityEngine.Color32(110, 96, 80, 255);
                    else continue;
                    px[sy * W + sx] = col;
                }
            }
            if (v.Rejected)
            {
                var red = new UnityEngine.Color32(230, 30, 30, 255);
                int y0 = H - (oyTop + PANEL_H), y1 = H - 1 - oyTop;   // this panel's rows in the image
                for (int b = 0; b < 3; b++)
                {
                    for (int i = 0; i < PANEL_W; i++) { px[(y0 + b) * W + ox + i] = red; px[(y1 - b) * W + ox + i] = red; }
                    for (int j = y0; j <= y1; j++) { px[j * W + ox + b] = red; px[j * W + ox + PANEL_W - 1 - b] = red; }
                }
            }
        }
        var tex = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(tex));
        UnityEngine.Object.DestroyImmediate(tex);
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), title);
    }

    // ------------------------------------------------------------------
    // Chunk helpers
    // ------------------------------------------------------------------
    private static byte Read(Chunk c, BrickDataPool pool, int3 local)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((local.z >> 3) << 8) | ((local.y >> 3) << 4) | (local.x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        return pool.RawData[(int)(d & 0x3FFFFFFFu) * 512 + (((local.z & 7) << 6) | ((local.y & 7) << 3) | (local.x & 7))];
    }

    /// Chunk `coord`'s 128^3 engine voxels (Index order), its dense brick count and generation ms.
    private static byte[] Gen(TerrainGenChunkSource src, int3 coord, out int dense, out double ms)
    {
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
        try
        {
            var chunk = new Chunk();
            var sw = Stopwatch.StartNew();
            src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
            ms = sw.Elapsed.TotalMilliseconds;
            dense = 0;
            var v = new byte[128 * 128 * 128];
            if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return v; }
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if ((chunk.bricks[i].data & 0x80000000u) != 0) dense++;
            for (int y = 0; y < 128; y++)
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
                v[TerrainGenChunkSource.Index(x, y, z)] = Read(chunk, pool, new int3(x, y, z));
            return v;
        }
        finally { pool.Dispose(); }
    }

    private static readonly HashSet<byte> TreeEngine = new HashSet<byte>();
    static TreeTests()
    {
        foreach (byte g in new byte[] { GenMat.ForestLeaves, GenMat.JungleLeaves, GenMat.SavannaLeaves, GenMat.PineNeedles,
                                        GenMat.ForestWood, GenMat.JungleWood, GenMat.AcaciaWood, GenMat.PineWood, GenMat.Cactus })
            TreeEngine.Add(VoxelEngine.Rendering.FarField.GenToEngine(g));
    }

    private sealed class Empty : IDisposable
    {
        private readonly BrickDataPool _pool = new BrickDataPool(16);
        public readonly ChunkStore store;
        public Empty() { store = new ChunkStore(_pool, new ChunkHandleAllocator(1)); }
        public void Dispose() => _pool.Dispose();
    }

    /// Tree-cell windows: a 16 x 16-cell block every 64 cells over the world (1/16 of it).
    private static List<int4> SparseWindows(in TerrainSpec spec)
    {
        var l = new List<int4>();
        int n = spec.worldSizeVoxels >> Trees.CELL_SHIFT;
        for (int z = 0; z + 16 <= n; z += 64)
        for (int x = 0; x + 16 <= n; x += 64)
            l.Add(new int4(x, z, x + 15, z + 15));
        return l;
    }

    private static string BiomeName(byte b) => b == GenBiome.Forest ? "forest" : b == GenBiome.Desert ? "desert" : b == GenBiome.Snow ? "snow"
        : b == GenBiome.Jungle ? "jungle" : b == GenBiome.Savanna ? "savanna" : "badlands";

    // ==================================================================
    // Chunks: determinism, a tree across a chunk border, point + shells agree
    // ==================================================================
    [Test, Category("Slow")]
    public void Chunks_Deterministic_Border_PointAndShellsAgree()
    {
        using var c = new Ctx(42u);
        var src = c.src;
        using var s = RunScan(c, SparseWindows(src.Spec));
        // a FOREST tree whose crown crosses a chunk border in x, root well inside the world
        int pick = -1;
        for (int i = 0; i < s.trees.Length && pick < 0; i++)
        {
            var t = s.trees[i];
            if (t.biome != GenBiome.Forest) continue;
            if ((t.lo.x >> 7) != (t.hi.x >> 7) && (t.lo.z >> 7) == (t.hi.z >> 7) && (t.lo.y >> 7) == (t.hi.y >> 7)) pick = i;
        }
        Assert.GreaterOrEqual(pick, 0, "no forest tree crossing exactly one chunk border found");
        var tree = s.trees[pick];
        var log = new StringBuilder();
        log.AppendLine($"seed 42 border tree: root {tree.root} lo {tree.lo} hi {tree.hi} shape {tree.shape}");

        int3 c0 = tree.lo >> 7, c1 = tree.hi >> 7;
        var on = new Dictionary<int3, byte[]>();
        int sideA = 0, sideB = 0;
        for (int cz = c0.z; cz <= c1.z; cz++)
        for (int cy = c0.y; cy <= c1.y; cy++)
        for (int cx = c0.x; cx <= c1.x; cx++)
        {
            var cc = new int3(cx, cy, cz);
            src.SetTrees(true);
            var a = Gen(src, cc, out _, out _);
            var b = Gen(src, cc, out _, out _);
            FastAssert.BytesCollectionEqual(a, b, $"chunk {cc}: two ON runs differ (not deterministic)");
            src.SetTrees(false);
            var off = Gen(src, cc, out _, out _);
            src.SetTrees(true);
            int trees = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == off[i]) continue;
                if (off[i] != Materials.Air) Assert.AreEqual(Materials.Air, off[i], $"chunk {cc} voxel {i}: a tree replaced a non-air voxel ({off[i]})");   // 184b: guarded, same check
                if (!TreeEngine.Contains(a[i])) Assert.IsTrue(TreeEngine.Contains(a[i]), $"chunk {cc} voxel {i}: ON differs from OFF by a non-tree material {a[i]}");   // 184b: guarded, same check
                trees++;
            }
            if (cx == c0.x) sideA += trees; else sideB += trees;
            on[cc] = a;
            log.AppendLine($"  chunk {cc}: {trees} tree voxels");
        }
        Assert.Greater(sideA, 0, "no tree voxels west of the border");
        Assert.Greater(sideB, 0, "no tree voxels east of the border");

        // FarQuery's point path: every tree voxel + random points of those chunks
        var rng = new Unity.Mathematics.Random(180u);
        var pts = new List<int3>(); var want = new List<byte>();
        foreach (var kv in on)
        {
            int3 o = kv.Key << 7; var a = kv.Value;
            for (int i = 0; i < a.Length; i++)
                if (TreeEngine.Contains(a[i]) && (i & 3) == 0)
                { pts.Add(o + new int3(i & 127, i >> 14, (i >> 7) & 127)); want.Add(a[i]); }
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
                    Assert.AreEqual(want[i], res[i], $"point {pts[i]}: FarQuery {res[i]} != chunk {want[i]}");
            }
            finally { p.Dispose(); res.Dispose(); }
        }
        log.AppendLine($"  point path: {pts.Count} points equal");

        // shell 0 = Generate; shells 1-3: tree voxels above the ground = the 0.1 m voxel at the sample
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
        src.ShellFill = false;   // compare the generator's voxels (the fill may flatten a buried mixed brick)
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
                    if (want1 != got) Assert.AreEqual(want1, got, $"shell {k} voxel ({x},{y},{z}) sample {f}: {got} != 0.1 m voxel {want1}");   // 184b: guarded, same check
                }
            }
            Assert.Greater(treeVox, 0, $"shell {k} shows none of the tree");
            log.AppendLine($"  shell {k}: {compared} tree voxels compared, {treeVox} tree voxels in the shell chunk, all equal");
        }
        src.ShellFill = true;
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "chunks.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
    }

    // ==================================================================
    // 4 seeds: placement rules, 30-tree sheets per biome, floating / embedded
    // ==================================================================
    [Test, Category("Slow")]
    public void FourSeeds_Rules_Sheets()
    {
        var log = new StringBuilder();
        var fails = new List<string>();
        foreach (uint seed in new uint[] { 42u, 7u, 2026u, 31337u })
        {
            using var c = new Ctx(seed);
            var spec = c.src.Spec; var rec = c.src.Map.Record;
            var windows = SparseWindows(spec);
            var sw = Stopwatch.StartNew();
            using var s = RunScan(c, windows);
            double scanMs = sw.Elapsed.TotalMilliseconds;
            int cellsScanned = windows.Count * 256;

            // land cells per biome in the scanned windows (record, at each tree cell's centre)
            var land = new int[GenBiome.Count]; var count = new int[GenBiome.Count];
            var shapes = new int[GenBiome.Count * 3]; var slots = new int[Cover.SlotCount];
            foreach (var win in windows)
                for (int cz = win.y; cz <= win.w; cz++)
                for (int cx = win.x; cx <= win.z; cx++)
                {
                    var rc = rec.At((cx << 6) + 32, (cz << 6) + 32);
                    if (!rc.IsSea && rc.Biome < GenBiome.Count) land[rc.Biome]++;
                }
            int maxReach = 0, maxH = 0;
            for (int i = 0; i < s.trees.Length; i++)
            {
                var t = s.trees[i];
                count[t.biome]++; shapes[t.biome * 3 + t.shape]++; slots[Cover.Slot((byte)s.info[i].x)]++;
                var rc = rec.At(t.root.x, t.root.z);
                if (rc.IsSea || rc.IsRoute || rc.IsFeature || rc.MoveBand == Danger.Sheer)
                    fails.Add($"seed {seed} tree {t.root}: on a sea/route/feature/sheer record cell");
                if (s.info[i].y != 0) fails.Add($"seed {seed} tree {t.root}: in water");
                if (t.biome == GenBiome.Badlands) fails.Add($"seed {seed} tree {t.root}: in badlands");
                maxReach = math.max(maxReach, math.cmax(math.abs(new int4(t.lo.xz - t.root.xz, t.hi.xz - t.root.xz))));
                maxH = math.max(maxH, t.hi.y - t.root.y);
            }
            if (maxReach > Trees.REACH) fails.Add($"seed {seed}: reach {maxReach} > REACH");
            log.AppendLine($"== seed {seed}: {s.trees.Length} trees in {cellsScanned} tree cells (1/16 of the world), scan {scanMs:F0} ms; max reach {maxReach} vox, max height {maxH} vox");
            for (byte b = 0; b < GenBiome.Count; b++)
            {
                double km2 = land[b] * (Trees.CELL * 0.1) * (Trees.CELL * 0.1) / 1e6;
                log.AppendLine($"  {BiomeName(b),-8} land cells {land[b],7}  trees {count[b],6}  per land cell {(land[b] > 0 ? (double)count[b] / land[b] : 0):F3}"
                             + $"  per km2 {(km2 > 0 ? count[b] / km2 : 0),6:F0}  shapes {shapes[b * 3]}/{shapes[b * 3 + 1]}/{shapes[b * 3 + 2]}");
            }
            log.AppendLine($"  root cover slot: flat {slots[0]} steep {slots[1]} hollow {slots[2]} high {slots[3]} shore {slots[4]}");

            // 30 per biome family + the 30 on the steepest feet (slot steep), voxelised and checked
            var groups = new List<(string name, List<int> idx)>();
            foreach (byte b in new byte[] { GenBiome.Forest, GenBiome.Jungle, GenBiome.Savanna, GenBiome.Snow, GenBiome.Desert })
            {
                var l = new List<int>();
                for (int i = 0; i < s.trees.Length && l.Count < 30; i++) if (s.trees[i].biome == b) l.Add(i);
                groups.Add((BiomeName(b), l));
            }
            var steep = new List<int>();
            for (int i = 0; i < s.trees.Length && steep.Count < 30; i++) if (Cover.Slot((byte)s.info[i].x) == Cover.Steep) steep.Add(i);
            groups.Add(("steep", steep));
            foreach (var (name, idx) in groups)
            {
                var vox = new List<TreeVox>();
                int rejected = 0; long voxels = 0; double hSum = 0;
                foreach (int i in idx)
                {
                    var v = Voxelise(c, s, i);
                    vox.Add(v); voxels += v.voxels; hSum += (v.t.hi.y - v.t.root.y) * 0.1;
                    if (v.Rejected) { rejected++; fails.Add($"seed {seed} {name} tree {v.t.root} shape {v.t.shape}: {v.Why}"); }
                }
                log.AppendLine($"  sheet {name,-8}: {idx.Count} trees, rejected {rejected}, mean height {(idx.Count > 0 ? hSum / idx.Count : 0):F1} m, mean voxels {(idx.Count > 0 ? voxels / idx.Count : 0)}");
                if (vox.Count > 0)
                    WriteSheet(Path.Combine(ShotDir, $"sheet_{seed}_{name}.png"), vox,
                               $"seed {seed} {name}: {vox.Count} trees, side view along +z, rejected framed red ({rejected})");
            }
        }
        Directory.CreateDirectory(LogDir);
        log.AppendLine($"FAILS {fails.Count}");
        foreach (var f in fails) log.AppendLine("  " + f);
        File.WriteAllText(Path.Combine(LogDir, "rules.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
        Assert.IsEmpty(fails, string.Join("\n", fails.GetRange(0, math.min(20, fails.Count))));
    }

    // ==================================================================
    // Cost: dense bricks + generation time, ON vs OFF (LOD 0 and shells 1-5)
    // ==================================================================
    [Test, Category("Slow")]
    [Category("Nightly")]   // task 184b: test-suite tier
    public void Cost_OnVsOff()
    {
        using var c = new Ctx(42u);
        var src = c.src;
        using var s = RunScan(c, SparseWindows(src.Spec));
        var log = new StringBuilder();
        log.AppendLine("seed 42. LOD 0: a 3 x 3 chunk-column patch round the densest tree spot of each biome; every layer from the lowest ground - 1 to the top.");
        log.AppendLine("Dense = non-uniform 8^3 bricks (a 512-byte body each on CPU + GPU). Times: this test's own Generate calls, ON and OFF interleaved (not a warm rig).");
        foreach (byte b in new byte[] { GenBiome.Forest, GenBiome.Jungle, GenBiome.Snow, GenBiome.Savanna })
        {
            // the chunk column holding the most trees of this biome
            var perCol = new Dictionary<int2, int>();
            for (int i = 0; i < s.trees.Length; i++)
                if (s.trees[i].biome == b) { var k = s.trees[i].root.xz >> 7; perCol[k] = perCol.TryGetValue(k, out int n) ? n + 1 : 1; }
            if (perCol.Count == 0) { log.AppendLine($"{BiomeName(b)}: no trees"); continue; }
            int2 best = default; int bestN = -1;
            foreach (var kv in perCol) if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
            long dOn = 0, dOff = 0; double tOn = 0, tOff = 0; int chunksOn = 0, chunksOff = 0, treeVox = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 cc = best + new int2(dx, dz);
                int gy = ShellGround(src, cc);
                for (int cy = (gy >> 7) - 1; cy <= ((gy + Trees.MAX_HEIGHT) >> 7); cy++)
                {
                    var coord = new int3(cc.x, cy, cc.y);
                    src.SetTrees(false);
                    var off = Gen(src, coord, out int nOff, out double msOff);
                    src.SetTrees(true);
                    var onv = Gen(src, coord, out int nOn, out double msOn);
                    dOn += nOn; dOff += nOff; tOn += msOn; tOff += msOff;
                    if (nOn > 0) chunksOn++; if (nOff > 0) chunksOff++;
                    for (int i = 0; i < onv.Length; i++) if (TreeEngine.Contains(onv[i])) treeVox++;
                }
            }
            log.AppendLine($"{BiomeName(b),-8} LOD 0 patch at chunk column {best} ({bestN} roots in the centre column): dense bricks OFF {dOff} -> ON {dOn} "
                         + $"({(dOff > 0 ? 100.0 * (dOn - dOff) / dOff : 0):+0;-0} %), +{(dOn - dOff) * 512 / 1048576.0:F2} MB bodies per 9 columns; "
                         + $"non-empty chunks {chunksOff} -> {chunksOn}; tree voxels {treeVox}; gen time OFF {tOff:F0} ms -> ON {tOn:F0} ms ({(tOff > 0 ? 100.0 * (tOn - tOff) / tOff : 0):+0;-0} %)");
            // shells 1-5: the shell chunk holding that column's ground, ON vs OFF
            using var sc = new TerrainGenChunkSource.ShellChunk();
            for (int k = 1; k <= Trees.MAX_SHELL; k++)
            {
                int gy = ShellGround(src, best);
                var fine = new int3(best.x << 7, gy, best.y << 7);
                long[] dn = new long[2]; double[] ms = new double[2];
                for (int arm = 0; arm < 2; arm++)
                {
                    src.SetTrees(arm == 1);
                    for (int ly = -1; ly <= 1; ly++)
                    {
                        int3 coord = (fine >> (7 + k)) + new int3(0, ly, 0);
                        var sw = Stopwatch.StartNew();
                        src.GenerateShellChunk(k, coord, sc);
                        ms[arm] += sw.Elapsed.TotalMilliseconds;
                        dn[arm] += sc.DenseBricks;
                    }
                }
                log.AppendLine($"           shell {k} ({0.1 * (1 << k):F1} m voxels), 3 layers: dense OFF {dn[0]} -> ON {dn[1]} ({(dn[0] > 0 ? 100.0 * (dn[1] - dn[0]) / dn[0] : 0):+0;-0} %), "
                             + $"gen OFF {ms[0]:F0} -> ON {ms[1]:F0} ms");
            }
            src.SetTrees(true);
        }
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "cost.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
    }

    // ==================================================================
    // Shot poses (explicit, for the look launch): the densest tree spot per biome
    // ==================================================================
    [Test, Explicit("writes Logs/TerrainGen/trees180/poses.txt for -wsfixed")]
    public void Poses_DensestSpots()
    {
        using var c = new Ctx(42u);
        var spec = c.src.Spec; var view = c.src.Map.View; var o = c.src.O;
        using var s = RunScan(c, SparseWindows(spec));
        var items = new List<string>();
        foreach (byte b in new byte[] { GenBiome.Forest, GenBiome.Jungle, GenBiome.Savanna, GenBiome.Snow, GenBiome.Desert })
        {
            // the 32 m square holding the most roots of this biome
            var per = new Dictionary<int2, int>();
            for (int i = 0; i < s.trees.Length; i++)
                if (s.trees[i].biome == b) { var k = s.trees[i].root.xz >> 8; per[k] = per.TryGetValue(k, out int n) ? n + 1 : 1; }
            if (per.Count == 0) continue;
            int2 best = default; int bn = -1;
            foreach (var kv in per) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; }
            float2 t = (float2)(best * 256 + 128);
            float gT = ShapeField.Sample(in spec, in view, in o, t.x, t.y, 1).height;
            // eye 45 m to +z (the survey heads -z: the sun behind the camera), 16 m over the higher ground
            float2 e = t + new float2(0, 450);
            float gE = ShapeField.Sample(in spec, in view, in o, e.x, e.y, 1).height;
            float eyeY = math.max(gT, gE) + 160f;
            string F(float v) => (v * 0.1f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            items.Add($"{BiomeName(b)}|{F(e.x)},{F(eyeY)},{F(e.y)}|{F(t.x)},{F(gT + 60f)},{F(t.y)}");
        }
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "poses.txt"), string.Join(";", items) + "\n");
        UnityEngine.Debug.Log(string.Join(";", items));
    }

    /// The ground Y at the centre of chunk column cc (shell 0's column pass).
    private static int ShellGround(TerrainGenChunkSource src, int2 cc)
    {
        using var sc = new TerrainGenChunkSource.ShellChunk();
        src.GenerateShellChunk(0, new int3(cc.x, src.Spec.seaLevelY >> 7, cc.y), sc);
        int g = int.MaxValue;
        for (int z = 0; z < 128; z += 16) for (int x = 0; x < 128; x += 16) g = math.min(g, sc.GroundY(x, z));
        return g;
    }
}
