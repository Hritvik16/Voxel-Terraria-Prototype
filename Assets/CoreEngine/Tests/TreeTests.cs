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
    // task 192: the default trees are 192's open crowns now; their logs / sheets go to 192
    // (task 180's own sheets stay in Screenshots/180 as the "before")
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "trees192");
    private static readonly string ShotDir = Path.Combine("Screenshots", "192");

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
            src.SetTrees2(false);   // task 214: these pin today's trees (-trees2 0); Trees2Tests covers the default
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

    private static void WriteSheet(string path, List<TreeVox> vox, string title, int rows = SHEET_ROWS)
    {
        int W = PANEL_W * SHEET_COLS, H = PANEL_H * rows;
        var px = new UnityEngine.Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new UnityEngine.Color32(196, 214, 232, 255);
        for (int k = 0; k < vox.Count && k < SHEET_COLS * rows; k++)
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
            var shapes = new int[GenBiome.Count * Trees.FORM_COUNT]; var slots = new int[Cover.SlotCount];
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
                count[t.biome]++; shapes[t.biome * Trees.FORM_COUNT + t.shape]++; slots[Cover.Slot((byte)s.info[i].x)]++;
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
                             + $"  per km2 {(km2 > 0 ? count[b] / km2 : 0),6:F0}  forms broad/conifer/palm/cactus {shapes[b * 4]}/{shapes[b * 4 + 1]}/{shapes[b * 4 + 2]}/{shapes[b * 4 + 3]}");
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
    // Task 192: the look -- leaf FILL, sky through the crown, uniqueness of
    // neighbours, brick values; task 180's trees vs 192's at fill 30 / 45 / 60
    // ==================================================================
    private static readonly (string name, byte fill, bool style180)[] LookArms =
        { ("180", 0, true), ("fill30", 30, false), ("fill45", 45, false), ("fill60", 60, false) };

    /// Leaf fill and sky of one voxelised tree. ENVELOPE (same rule for every style):
    /// the shape inscribed in the box of the tree's leaf voxels -- an upright cone for a
    /// conifer, else an ellipsoid. fill = leaf voxels inside / envelope voxels; sky =
    /// columns under the envelope's widest ellipse holding no tree voxel at all.
    private static bool LeafFill(TreeVox v, bool cone, out double fill, out double sky)
    {
        fill = sky = 0;
        int nx = v.dim.x, ny = v.dim.y, nz = v.dim.z;
        int3 lo = new int3(int.MaxValue), hi = new int3(int.MinValue);
        for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
        {
            if (!GenMat.IsTreeLeaf(v.mats[(y * nz + z) * nx + x])) continue;
            var q = new int3(x, y, z); lo = math.min(lo, q); hi = math.max(hi, q);
        }
        if (lo.x > hi.x) return false;
        float3 c = (float3)(lo + hi) * 0.5f, a = (float3)(hi - lo + 1) * 0.5f;
        long env = 0, inside = 0; int cols = 0, open = 0;
        for (int z = lo.z; z <= hi.z; z++)
        for (int x = lo.x; x <= hi.x; x++)
        {
            float ex = (x - c.x) / a.x, ez = (z - c.z) / a.z, e2 = ex * ex + ez * ez;
            if (e2 > 1f) continue;
            cols++;
            bool any = false;
            for (int y = 0; y < ny && !any; y++) any = v.mats[(y * nz + z) * nx + x] != 0;
            if (!any) open++;
            for (int y = lo.y; y <= hi.y; y++)
            {
                bool inEnv;
                if (cone) { float t = (y - lo.y + 0.5f) / (hi.y - lo.y + 1); inEnv = e2 <= (1f - t) * (1f - t); }
                else { float ey = (y - c.y) / a.y; inEnv = e2 + ey * ey <= 1f; }
                if (!inEnv) continue;
                env++;
                if (GenMat.IsTreeLeaf(v.mats[(y * nz + z) * nx + x])) inside++;
            }
        }
        if (env == 0 || cols == 0) return false;
        fill = (double)inside / env; sky = (double)open / cols;
        return true;
    }

    /// Side silhouette (along z), root-aligned: keys of the occupied (x, y) cells.
    private static HashSet<int> Silhouette(TreeVox v)
    {
        var h = new HashSet<int>();
        int nx = v.dim.x, ny = v.dim.y, nz = v.dim.z;
        for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
            for (int z = 0; z < nz; z++)
                if (v.mats[(y * nz + z) * nx + x] != 0) { h.Add(((v.lo.x + x - v.t.root.x + 256) << 10) | (v.lo.y + y - v.t.root.y + 64)); break; }
        return h;
    }

    /// Tree bricks (world-aligned 8^3 holding a tree voxel) and how many hold <= 2 values (air counts).
    private static void BrickValues(TreeVox v, out int bricks, out int two)
    {
        var vals = new Dictionary<int3, HashSet<byte>>();
        int nx = v.dim.x, ny = v.dim.y, nz = v.dim.z;
        for (int y = 0; y < ny; y++) for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++)
        {
            var w = v.lo + new int3(x, y, z);
            var b = w >> 3;
            byte m = v.mats[(y * nz + z) * nx + x];
            if (!vals.TryGetValue(b, out var set)) { set = new HashSet<byte>(); vals[b] = set; }
            set.Add(m);
        }
        bricks = 0; two = 0;
        foreach (var kv in vals)
        {
            bool tree = false; foreach (byte m in kv.Value) if (m != 0) tree = true;
            if (!tree) continue;
            bricks++;
            var b0 = kv.Key << 3;   // brick voxels outside the tree's box are air
            bool partial = math.any(b0 < v.lo) || math.any(b0 + 7 > v.lo + v.dim - 1);
            int n = kv.Value.Count + (partial && !kv.Value.Contains(0) ? 1 : 0);
            if (n <= 2) two++;
        }
    }

    [Test, Category("Slow")]
    public void Look_FillSkyUniqueness_Sheets()
    {
        var log = new StringBuilder();
        var fails = new List<string>();
        log.AppendLine("task 192: per biome, the 30 trees nearest the biome's densest 25.6 m block (seed 42 / 7); sheets = the 12 nearest.");
        log.AppendLine("fill = leaf voxels / envelope voxels (envelope = cone (conifers) or ellipsoid inscribed in the leaf box); sky = envelope columns with no tree voxel;");
        log.AppendLine("twin = mean over the 12 of the side-silhouette IoU with its most similar neighbour (1 = clones, lower = more unique);");
        log.AppendLine("2val = tree bricks (world 8^3) holding <= 2 values incl. air (task 188 packs those 7 to a body); prims = primitives per tree.");
        log.AppendLine($"{"seed",4} {"biome",-8} {"arm",-7} {"trees",5} {"fill %",7} {"(min-max)",11} {"sky %",6} {"twin",5} {"2val %",6} {"leaf vox",9} {"height m",8} {"prims",5} {"rej",3}");
        foreach (uint seed in new uint[] { 42u, 7u })
        {
            using var c = new Ctx(seed);
            var anchors = new Dictionary<byte, float2>();
            var sheets = new Dictionary<byte, List<TreeVox>>();
            foreach (var arm in LookArms)
            {
                c.src.SetTreeLook(arm.fill, arm.style180);
                using var s = RunScan(c, SparseWindows(c.src.Spec));
                foreach (byte b in new byte[] { GenBiome.Forest, GenBiome.Jungle, GenBiome.Savanna, GenBiome.Snow, GenBiome.Desert })
                {
                    if (!anchors.ContainsKey(b))
                    {   // the densest 25.6 m block of this biome, from the first (180) arm
                        var per = new Dictionary<int2, int>();
                        for (int i = 0; i < s.trees.Length; i++)
                            if (s.trees[i].biome == b) { var k = s.trees[i].root.xz >> 8; per[k] = per.TryGetValue(k, out int nn) ? nn + 1 : 1; }
                        if (per.Count == 0) continue;
                        int2 best = default; int bn = -1;
                        foreach (var kv in per) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; }
                        anchors[b] = (float2)(best * 256 + 128);
                    }
                    float2 an = anchors[b];
                    var idx = new List<int>();
                    for (int i = 0; i < s.trees.Length; i++) if (s.trees[i].biome == b) idx.Add(i);
                    idx.Sort((i, j) => math.lengthsq((float2)s.trees[i].root.xz - an).CompareTo(math.lengthsq((float2)s.trees[j].root.xz - an)));
                    if (idx.Count > 30) idx.RemoveRange(30, idx.Count - 30);
                    var vox = new List<TreeVox>();
                    double fSum = 0, skySum = 0, fMin = 1, fMax = 0; int fN = 0, rej = 0; long leafVox = 0, bricks = 0, two = 0, prims = 0; double hSum = 0;
                    foreach (int i in idx)
                    {
                        var v = Voxelise(c, s, i);
                        vox.Add(v);
                        prims += v.t.primCount; hSum += (v.t.hi.y - v.t.root.y) * 0.1;
                        if (v.Rejected) { rej++; if (!arm.style180) fails.Add($"seed {seed} {BiomeName(b)} {arm.name} tree {v.t.root} form {v.t.shape}: {v.Why}"); }
                        bool cone = arm.style180 ? b == GenBiome.Snow : v.t.shape == Trees.FORM_CONIFER;
                        if (LeafFill(v, cone, out double f, out double sk))
                        { fSum += f; skySum += sk; fN++; fMin = math.min(fMin, f); fMax = math.max(fMax, f); }
                        for (int k = 0; k < v.mats.Length; k++) if (GenMat.IsTreeLeaf(v.mats[k])) leafVox++;
                        BrickValues(v, out int bb, out int tw); bricks += bb; two += tw;
                    }
                    // uniqueness over the 12 neighbours
                    int m12 = math.min(12, vox.Count);
                    var sil = new List<HashSet<int>>();
                    for (int k = 0; k < m12; k++) sil.Add(Silhouette(vox[k]));
                    double twin = 0;
                    for (int k = 0; k < m12; k++)
                    {
                        double best = 0;
                        for (int j = 0; j < m12; j++)
                        {
                            if (j == k) continue;
                            int inter = 0; foreach (int key in sil[k]) if (sil[j].Contains(key)) inter++;
                            int uni = sil[k].Count + sil[j].Count - inter;
                            if (uni > 0) best = math.max(best, (double)inter / uni);
                        }
                        twin += best;
                    }
                    twin = m12 > 1 ? twin / m12 : 0;
                    int nT = idx.Count;
                    log.AppendLine($"{seed,4} {BiomeName(b),-8} {arm.name,-7} {nT,5} {(fN > 0 ? 100 * fSum / fN : 0),7:F1} {(fN > 0 ? $"{100 * fMin:F0}-{100 * fMax:F0}" : "-"),11} "
                                 + $"{(fN > 0 ? 100 * skySum / fN : 0),6:F1} {twin,5:F2} {(bricks > 0 ? 100.0 * two / bricks : 0),6:F1} {(nT > 0 ? leafVox / nT : 0),9} "
                                 + $"{(nT > 0 ? hSum / nT : 0),8:F1} {(nT > 0 ? (double)prims / nT : 0),5:F1} {rej,3}");
                    if (seed == 42u)
                    {
                        WriteSheet(Path.Combine(ShotDir, $"look_{seed}_{BiomeName(b)}_{arm.name}.png"), vox.GetRange(0, m12),
                                   $"seed {seed} {BiomeName(b)} {arm.name}: the 12 trees nearest {anchors[b]}, side view along +z", 2);
                        if (!sheets.TryGetValue(b, out var all)) { all = new List<TreeVox>(); sheets[b] = all; }
                        for (int k = 0; k < 12; k++) if (k < m12) all.Add(vox[k]); else all.Add(null);
                    }
                }
            }
            foreach (var kv in sheets)
            {   // one image per biome: 180 | fill30 | fill45 | fill60, two rows of six each
                var l = kv.Value;
                var keep = new List<TreeVox>();
                foreach (var v in l) keep.Add(v);
                WriteSheetNullable(Path.Combine(ShotDir, $"look_{seed}_{BiomeName(kv.Key)}.png"), keep,
                    $"seed {seed} {BiomeName(kv.Key)}: rows 1-2 task 180, 3-4 fill 30, 5-6 fill 45, 7-8 fill 60 (the same 12 nearest trees' cells)");
            }
        }

        Directory.CreateDirectory(LogDir);
        log.AppendLine($"FAILS {fails.Count}");
        foreach (var f in fails) log.AppendLine("  " + f);
        File.WriteAllText(Path.Combine(LogDir, "look.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
        Assert.IsEmpty(fails, string.Join("\n", fails.GetRange(0, math.min(20, fails.Count))));
    }

    // Task 192 (explicit, for the look launch): a mature broadleaf tree near 180's forest pose; the
    // eye 2.5 m from its trunk (+z side), 1.6 m over the ground, looking up into the crown.
    [Test, Explicit("writes Logs/TerrainGen/trees192/undercrown.txt for -wsfixed")]
    public void Poses_UnderCrown()
    {
        using var c = new Ctx(42u);
        var spec = c.src.Spec; var view = c.src.Map.View; var o = c.src.O;
        float2 aim = new float2(33664f, 49280f);   // 180's forest pose target, voxels
        var win = new List<int4> { new int4(((int)aim.x >> Trees.CELL_SHIFT) - 8, ((int)aim.y >> Trees.CELL_SHIFT) - 8,
                                            ((int)aim.x >> Trees.CELL_SHIFT) + 8, ((int)aim.y >> Trees.CELL_SHIFT) + 8) };
        using var s = RunScan(c, win);
        int pick = -1; float bestD = float.MaxValue;
        for (int i = 0; i < s.trees.Length; i++)
        {
            var t = s.trees[i];
            if (t.biome != GenBiome.Forest || t.shape != Trees.FORM_BROAD || t.hi.y - t.root.y < 100) continue;
            float d = math.length((float2)t.root.xz - aim);
            if (d < bestD) { bestD = d; pick = i; }
        }
        Assert.GreaterOrEqual(pick, 0, "no mature broadleaf near the forest pose");
        var tr = s.trees[pick];
        float2 e = (float2)tr.root.xz + new float2(0.5f, 25.5f);
        float gE = ShapeField.Sample(in spec, in view, in o, e.x, e.y, 1).height;
        string F(float v) => (v * 0.1f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string pose = $"under_crown|{F(e.x)},{F(gE + 16f)},{F(e.y)}|{F(tr.root.x + 0.5f)},{F(tr.root.y + (tr.hi.y - tr.root.y) * 0.8f)},{F(tr.root.z - 15f)}";
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "undercrown.txt"), pose + "\n");
        UnityEngine.Debug.Log($"tree {tr.root} lo {tr.lo} hi {tr.hi}: {pose}");
    }

    /// WriteSheet with empty panels for null entries (rows of 6).
    private static void WriteSheetNullable(string path, List<TreeVox> vox, string title)
    {
        var dense = new List<TreeVox>();
        foreach (var v in vox) dense.Add(v ?? EmptyVox);
        WriteSheet(path, dense, title, (vox.Count + SHEET_COLS - 1) / SHEET_COLS);
    }

    private static readonly TreeVox EmptyVox = new TreeVox
    {
        t = new Tree { root = new int3(0, 0, 0) }, lo = new int3(0, 0, 0), dim = new int3(1, 1, 1),
        ground = new[] { int.MinValue }, mats = new byte[1], trunkGrounded = true,
    };

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
        // task 192: both looks on the same patches -- task 180's trees (-treestyle 0), then 192's default
        foreach (bool style180 in new[] { true, false })
        {
        src.SetTreeLook(0, style180);
        log.AppendLine(style180 ? "== task 180's trees (-treestyle 0)" : $"== task 192's trees (default, leaf fill {Trees.DEFAULT_LEAF_FILL})");
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
        }
        src.SetTreeLook(0, false);
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
