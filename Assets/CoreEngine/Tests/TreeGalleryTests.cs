// Assets/CoreEngine/Tests/TreeGalleryTests.cs
//
// Task 200 (EXAMPLES ONLY; owner 2026-11-13 trees talk): the prototype growing
// methods of TerrainGen.TreeGallery.
//   TodayTrees_Pinned          today's Trees.Build output (seed 42, a block of tree
//                              cells) hashes to the value recorded before task 200:
//                              the world's trees did not change.
//   Methods_Species_SheetsCost every species x method x 3 seeds on flat ground: each
//                              stands on its trunk, has leaves, no floating voxel;
//                              CPU preview sheets Screenshots/200/proto_<species>.png,
//                              counts + Build() ms (Editor Mono micro-count) in
//                              Logs/TerrainGen/trees200/cost.txt.
//   Gallery_Layout (Explicit)  picks the flat open sites on seed 42 for the in-engine
//                              stills: writes the -treegallery layout + the -wsfixed
//                              poses (Logs/TerrainGen/trees200/).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class TreeGalleryTests
{
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "trees200");
    private static readonly string ShotDir = Path.Combine("Screenshots", "200");

    // ------------------------------------------------------------------
    // Today's trees are untouched
    // ------------------------------------------------------------------
    /// FNV-1a over every TreePrim field of Trees.Build for the first 64 x 64 tree-cell block holding >= 50 trees on seed 42,
    /// default look. Recorded 2026-11-14 with Trees.cs as tasks 180 / 192 left it (task 200 does not edit it).
    private const ulong TODAY_HASH = 0x38EF6100C34F1928UL;   // block (512, 256), recorded 2026-11-14 (Trees.cs unchanged by task 200)
    private const int TODAY_TREES = 1663;

    [Test, Category("Slow")]
    public unsafe void TodayTrees_Pinned()
    {
        using var src = new TerrainGenChunkSource(42);
        src.SetTrees(true);
        src.SetTrees2(false);   // task 214: today's trees (-trees2 0), as recorded
        var m = src.Map;
        var plane = TreeTable.DensityPlane(m.Record, src.Spec.mapCells);
        fixed (byte* pl = plane)
        {
            var view = m.View; view.trees = pl;
            var spec = src.Spec; var o = src.O;
            var buf = stackalloc TreePrim[Trees.MAX_PRIMS];
            ulong h = 14695981039346656037UL; int trees = 0, prims = 0, b0x = -1, b0z = -1, nz = 0;
            void Mix(uint v) { for (int k = 0; k < 4; k++) { h = (h ^ (v & 255)) * 1099511628211UL; v >>= 8; } }
            for (int i = 0; i < plane.Length; i++) if (plane[i] != 0) nz++;
            // the first 64 x 64-cell block (origins on a 128-cell lattice, row by row) holding >= 50 trees
            int cellsN = spec.worldSizeVoxels >> Trees.CELL_SHIFT;
            for (int bz = 128; bz + 64 < cellsN && b0x < 0; bz += 128)
            for (int bx = 128; bx + 64 < cellsN && b0x < 0; bx += 128)
            {
                h = 14695981039346656037UL; trees = 0; prims = 0;
                for (int cz = bz; cz < bz + 64; cz++)
                for (int cx = bx; cx < bx + 64; cx++)
                {
                    int n = 0;
                    if (!Trees.Build(in spec, in view, in o, cx, cz, buf, ref n, out Tree t)) continue;
                    trees++; prims += n;
                    Mix((uint)cx); Mix((uint)cz); Mix((uint)t.root.x); Mix((uint)t.root.y); Mix((uint)t.root.z);
                    for (int i = 0; i < n; i++)
                    {
                        var p = buf[i];
                        Mix((uint)p.lo.x); Mix((uint)p.lo.y); Mix((uint)p.lo.z); Mix((uint)p.hi.x); Mix((uint)p.hi.y); Mix((uint)p.hi.z);
                        Mix(F(p.a.x)); Mix(F(p.a.y)); Mix(F(p.a.z)); Mix(F(p.b.x)); Mix(F(p.b.y)); Mix(F(p.b.z));
                        Mix(F(p.r0)); Mix(F(p.r1)); Mix(p.noise); Mix(p.kind); Mix(p.mat); Mix(p.leaf);
                    }
                }
                if (trees >= 50) { b0x = bx; b0z = bz; }
            }
            // today's cost per tree, the same volume measure as the prototypes (cost.txt): 30 forest broadleaves of that block
            var costLine = new StringBuilder();
            {
                long tr = 0, lf = 0, bk = 0, by = 0; int cnt = 0, hsum = 0;
                for (int cz = b0z; cz < b0z + 64 && cnt < 30; cz++)
                for (int cx = b0x; cx < b0x + 64 && cnt < 30; cx++)
                {
                    int n = 0;
                    if (!Trees.Build(in spec, in view, in o, cx, cz, buf, ref n, out Tree t) || t.biome != GenBiome.Forest || t.shape != Trees.FORM_BROAD) continue;
                    TreeGallery.FromPrims(buf, n, t.root, out var st);
                    tr += st.trunk; lf += st.leaf; bk += st.bricks; by += st.bytes; hsum += t.hi.y - t.root.y; cnt++;
                }
                if (cnt > 0)
                    costLine.Append($"today forest broadleaf (mean of {cnt}): H {hsum / (10f * cnt):F1} m, trunk {tr / cnt} leaf {lf / cnt} vox, {bk / cnt} bricks, {by / (1048576f * cnt):F2} MB volume\n");
            }
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "today_pin.txt"), $"seed 42 tree cells [{b0x},{b0x + 64}) x [{b0z},{b0z + 64}): {trees} trees, {prims} prims, FNV {h:X16} (plane: {nz} nonzero cells)\n" + costLine);
            UnityEngine.Debug.Log($"[TreeGallery] today pin: block ({b0x}, {b0z}) {trees} trees, {prims} prims, FNV {h:X16}, plane nonzero {nz}");
            Assert.Greater(trees, 50, "the pinned block must hold trees");
            if (TODAY_TREES >= 0)
            {
                Assert.AreEqual(TODAY_TREES, trees, "today's tree count changed");
                Assert.AreEqual(TODAY_HASH, h, "today's Trees.Build output changed");
            }
        }
    }

    private static uint F(float f) => (uint)BitConverter.SingleToInt32Bits(f);

    // ------------------------------------------------------------------
    // Every species x method x 3 seeds: rules, sheets, cost
    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    public void Methods_Species_SheetsCost()
    {
        Directory.CreateDirectory(LogDir); Directory.CreateDirectory(ShotDir);
        var sb = new StringBuilder();
        sb.AppendLine("# task 200: prototype trees on flat ground (Editor Mono; Build = grow + voxelise, one thread)");
        sb.AppendLine("species  method    seed  H_m   W_m   trunk_vox  leaf_vox  leaf/trunk  nodes  segs  clumps  aerial/props  crumbs  bricks  MB    grow_ms  vox_ms");
        var fails = new List<string>();
        var perSpecies = new Dictionary<int, List<(string label, TreeGallery.Vol v, TreeGallery.Stats st)>>();
        for (int sp = 0; sp < TreeGallery.SP_COUNT; sp++)
        {
            var rows = new List<(string, TreeGallery.Vol, TreeGallery.Stats)>();
            for (int me = 0; me < TreeGallery.M_COUNT; me++)
            for (uint seed = 1; seed <= 3; seed++)
            {
                var v = TreeGallery.Build(sp, me, seed, TreeGallery.Flat, out var st, out var k);
                rows.Add(($"{TreeGallery.MethodName[me]} {seed}", v, st));
                sb.AppendLine($"{TreeGallery.SpeciesName[sp],-8} {TreeGallery.MethodName[me],-9} {seed,4}  {st.top / 10f,4:F1}  {math.max(st.size.x, st.size.z) / 10f,4:F1}  " +
                              $"{st.trunk,9}  {st.leaf,8}  {(float)st.leaf / math.max(1, st.trunk),10:F2}  {st.nodes,5}  {st.segs,4}  {st.clumps,6}  {st.aerial,6}/{st.props,-5}  {st.crumbs,6}  {st.bricks,6}  {st.bytes / 1048576f,4:F1}  {st.msGrow,7:F0}  {st.msVox,6:F0}");
                string id = $"{TreeGallery.SpeciesName[sp]}/{TreeGallery.MethodName[me]}/{seed}";
                if (st.leaf < 1000) fails.Add($"{id}: only {st.leaf} leaf voxels");
                if (!TreeGallery.IsTreeWood(v.Get(new int3(0, 1, 0)))) fails.Add($"{id}: no trunk at the root");
            }
            perSpecies[sp] = rows;
            WriteSheet(Path.Combine(ShotDir, $"proto_{TreeGallery.SpeciesName[sp]}.png"), rows, 3, 2);
        }
        File.WriteAllText(Path.Combine(LogDir, "cost.txt"), sb.ToString());
        UnityEngine.Debug.Log(sb.ToString());
        Assert.IsEmpty(fails, string.Join("\n", fails));
    }

    // ------------------------------------------------------------------
    // CPU preview: side view looking -z (the in-engine camera's heading), `px` voxels per pixel
    // ------------------------------------------------------------------
    internal static void WriteSheet(string path, List<(string label, TreeGallery.Vol v, TreeGallery.Stats st)> trees, int cols, int px)
    {
        int3 ext = 0;
        foreach (var t in trees) ext = math.max(ext, t.v.nb * 8);
        int pw = ext.x / px + 8, ph = ext.y / px + 8;
        int rows = (trees.Count + cols - 1) / cols;
        int W = pw * cols, H = ph * rows;
        var img = new UnityEngine.Color32[W * H];
        for (int i = 0; i < img.Length; i++) { int y = i / W; float f = (float)(y % ph) / ph; img[i] = new UnityEngine.Color32((byte)(150 + 50 * f), (byte)(185 + 35 * f), (byte)(225 + 20 * f), 255); }
        var depth = new int[W * H];
        for (int k = 0; k < trees.Count; k++)
        {
            var (label, v, st) = trees[k];
            int ox = (k % cols) * pw + pw / 2, oy = (rows - 1 - k / cols) * ph + 4;   // root column at the panel's centre, ground 4 px up
            for (int i = 0; i < pw; i++) for (int j = 0; j < 4; j++) { int x = (k % cols) * pw + i, y = oy + j - 4; if (y >= 0) img[y * W + x] = new UnityEngine.Color32(96, 84, 66, 255); }
            for (int bi = 0; bi < v.index.Length; bi++)
            {
                int sl = v.index[bi]; if (sl < 0) continue;
                int bx = bi % v.nb.x, bz = (bi / v.nb.x) % v.nb.z, by = bi / (v.nb.x * v.nb.z);
                var b = v.bricks[sl];
                for (int j = 0; j < 512; j++)
                {
                    byte m = b[j]; if (m == 0) continue;
                    int3 l = v.lo + new int3((bx << 3) | (j & 7), (by << 3) | (j >> 6), (bz << 3) | ((j >> 3) & 7));
                    if (l.y < 0) continue;
                    int sx = ox + (int)math.floor(l.x / (float)px), sy = oy + l.y / px;
                    if (sx < (k % cols) * pw || sx >= (k % cols + 1) * pw || sy >= H) continue;
                    int di = sy * W + sx, dz = l.z + 100000;
                    if (depth[di] >= dz) continue;
                    depth[di] = dz;
                    bool top = v.Get(l + new int3(0, 1, 0)) == 0, front = v.Get(l + new int3(0, 0, 1)) == 0;
                    float shade = top ? 1.08f : front ? 0.86f : 0.62f;
                    shade *= 0.8f + 0.2f * math.saturate((l.z + 200) / 400f);
                    var c = MaterialPalette.Of(VoxelEngine.Rendering.FarField.GenToEngine(m));
                    img[di] = new UnityEngine.Color32((byte)math.clamp(c.R * 255 * shade, 0, 255), (byte)math.clamp(c.G * 255 * shade, 0, 255), (byte)math.clamp(c.B * 255 * shade, 0, 255), 255);
                }
            }
            // a 1.8 m person marker beside the trunk
            int mx = ox + 30 / px;
            for (int y = 0; y < 18 / px; y++) for (int x = 0; x < math.max(1, 4 / px); x++) img[(oy + y) * W + mx + x] = new UnityEngine.Color32(200, 40, 30, 255);
        }
        var tex = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGBA32, false);
        tex.SetPixels32(img); tex.Apply();
        File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(tex));
        UnityEngine.Object.DestroyImmediate(tex);
        var sb = new StringBuilder();
        sb.AppendLine($"{Path.GetFileName(path)}: side view looking -z, {px * 0.1f:F1} m per pixel, {cols} per row; red post = 1.8 m person");
        for (int k = 0; k < trees.Count; k++) sb.AppendLine($"  [{k / cols},{k % cols}] {trees[k].label}");
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), sb.ToString());
    }
    // ------------------------------------------------------------------
    // The engine hook: a gallery tree changes AIR only, to exactly GalleryAt's material
    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    public void Gallery_Chunk_AirOnly()
    {
        using var src = new TerrainGenChunkSource(42);
        src.SetTrees(true);
        src.SetTrees2(false);   // task 214: today's trees (-trees2 0), as recorded
        var spec = src.Spec; var o = src.O; var view = src.Map.View;
        // a dry, gentle land column near the world's centre (a spiral of 16 m steps)
        int3 site = -1;
        for (int k = 0; k < 4000 && site.x < 0; k++)
        {
            int x = spec.worldSizeVoxels / 2 + (k % 63 - 31) * 160, z = spec.worldSizeVoxels / 2 + ((k / 63) - 31) * 160;
            var c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
            if (Bad(in spec, in c)) continue;
            var c2 = ShapeField.Sample(in spec, in view, in o, x + 120.5f, z + 0.5f, 1);
            var c3 = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 120.5f, 1);
            if (math.abs(c2.height - c.height) > 15 || math.abs(c3.height - c.height) > 15) continue;
            site = new int3(x, c.height, z);
        }
        Assert.GreaterOrEqual(site.x, 0, "no dry gentle column found");
        Directory.CreateDirectory(LogDir);
        string layout = Path.Combine(LogDir, "chunktest_layout.txt");
        File.WriteAllText(layout, $"tree {TreeGallery.SP_OAK} {TreeGallery.M_SPACE} 1 {site.x} {site.z}\nmarker {site.x + 60} {site.z}\n");
        // the chunks round the tree, OFF first
        int3 c0 = (site - new int3(130, 50, 130)) >> 7, c1 = (site + new int3(130, 260, 130)) >> 7;
        var off = new Dictionary<int3, byte[]>();
        for (int cz = c0.z; cz <= c1.z; cz++) for (int cy = c0.y; cy <= c1.y; cy++) for (int cx = c0.x; cx <= c1.x; cx++)
            off[new int3(cx, cy, cz)] = Gen(src, new int3(cx, cy, cz));
        src.BuildGallery(layout, new byte[spec.mapCells * spec.mapCells]);
        int changed = 0, expected = 0, bad = 0;
        byte air = VoxelEngine.Rendering.FarField.GenToEngine(GenMat.Air);
        foreach (var kv in off)
        {
            var on = Gen(src, kv.Key);
            int3 org = kv.Key * 128;
            for (int y = 0; y < 128; y++) for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++)
            {
                int i = TerrainGenChunkSource.Index(x, y, z);
                byte g = TerrainGenChunkSource.GalleryAt(org + new int3(x, y, z));
                byte a = kv.Value[i], b = on[i];
                if (g != 0 && a == air) { expected++; if (b != VoxelEngine.Rendering.FarField.GenToEngine(g)) bad++; }
                else if (a != b) bad++;
                if (a != b) changed++;
            }
        }
        string line = $"seed 42 oak/spacecol/1 at {site}: {off.Count} chunks, {changed} voxels changed, {expected} expected (gallery voxel over air), {bad} wrong";
        File.WriteAllText(Path.Combine(LogDir, "chunktest.txt"), line + "\n");
        UnityEngine.Debug.Log("[TreeGallery] " + line);
        Assert.AreEqual(0, bad, line);
        Assert.AreEqual(expected, changed, line);
        Assert.Greater(changed, 20000, line);
    }

    private static bool Bad(in TerrainSpec spec, in ColumnSample c)
        => c.height <= spec.seaLevelY + 20 || (c.water != ColumnSample.NO_WATER_Y && c.water > c.height - 20) || c.stampMat != 0 || c.open1Hi > c.open1Lo;

    private static byte[] Gen(TerrainGenChunkSource src, int3 coord)
    {
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
        try
        {
            var chunk = new Chunk();
            src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
            var v = new byte[128 * 128 * 128];
            if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return v; }
            for (int y = 0; y < 128; y++) for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++)
            {
                int bi = ((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3);
                uint d = chunk.bricks[bi].data;
                v[TerrainGenChunkSource.Index(x, y, z)] = (d & 0x80000000u) == 0 ? (byte)d
                    : pool.RawData[(int)(d & 0x3FFFFFFFu) * 512 + (((z & 7) << 6) | ((y & 7) << 3) | (x & 7))];
            }
            return v;
        }
        finally { pool.Dispose(); }
    }

    // ------------------------------------------------------------------
    // The in-engine stills' layout: flat open sites on seed 42 (writes files; Explicit)
    // ------------------------------------------------------------------
    private struct Block { public string name; public int sp, me, S; public bool today; }

    [Test, Explicit]
    public unsafe void Gallery_Layout()
    {
        using var src = new TerrainGenChunkSource(42);
        src.SetTrees2(false);   // task 214: today's trees (-trees2 0), as recorded
        var spec = src.Spec; var o = src.O; var view = src.Map.View;
        var plane = TreeTable.DensityPlane(src.Map.Record, spec.mapCells);
        const int G = 160;   // 16 m grid
        int n = spec.worldSizeVoxels / G;
        var hg = new int[n * n]; var bad = new bool[n * n]; var biome = new byte[n * n]; var topm = new byte[n * n];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
        {
            var c = ShapeField.Sample(in spec, in view, in o, i * G + 80.5f, j * G + 80.5f, 1);
            hg[j * n + i] = c.height; bad[j * n + i] = Bad(in spec, in c); biome[j * n + i] = c.surfBiome;
            topm[j * n + i] = GroundRules.Top(in c, 0f);
        }
        // run 2 (task 200): no town in or near a block (towns sit on flat pads -- run 1 put the oak on one)
        var town = new byte[spec.mapCells * spec.mapCells];
        for (int k = 0; k < town.Length; k++) town[k] = 1;
        src.Map.EnsureTowns();
        if (src.Map.TownPlane != null) Towns.ClearCells(town, spec.mapCells, spec.mapCellVoxels, src.Map.TownPlane);
        bool Town(int i, int j) => town[Ores.CellIndex(i * G + 80, j * G + 80, spec.mapCells, spec.mapCellVoxels)] == 0;
        bool Snowy(byte m) => m == GenMat.Snow || m == GenMat.PackedIce;
        // a block: trees in x [-5, 5] cells x z [-2, 2]; the camera corridor z [+2, +6] (95 m); clear behind to -10
        var cand = new List<(float score, int i, int j)>();
        for (int j = 12; j < n - 12; j++) for (int i = 8; i < n - 8; i++)
        {
            int g0 = hg[j * n + i];
            byte b0 = biome[j * n + i];
            if (b0 == GenBiome.Desert || b0 == GenBiome.Badlands) continue;
            int lo = int.MaxValue, hi = int.MinValue, chi = int.MinValue, clo = int.MaxValue; bool ok = true; int dens = 0;
            for (int dz = -2; dz <= 6 && ok; dz++) for (int dx = -5; dx <= 5 && ok; dx++)
            {
                int k = (j + dz) * n + i + dx;
                if (bad[k]) { ok = false; break; }
                if (dz <= 2) { lo = math.min(lo, hg[k]); hi = math.max(hi, hg[k]); }
                else { chi = math.max(chi, hg[k]); clo = math.min(clo, hg[k]); }
            }
            if (!ok || hi - lo > 80 || chi > g0 + 40 || clo < g0 - 120) continue;
            bool snow = false, nearTown = false;
            for (int dz = -2; dz <= 2; dz++) for (int dx = -5; dx <= 5; dx++) snow |= Snowy(topm[(j + dz) * n + i + dx]);
            for (int dz = -6; dz <= 7; dz++) for (int dx = -7; dx <= 7; dx++)
                if (i + dx >= 0 && i + dx < n && j + dz >= 0 && j + dz < n && Town(i + dx, j + dz)) nearTown = true;
            if (nearTown) continue;
            if (snow && b0 != GenBiome.Snow) continue;
            for (int dz = -10; dz <= 6; dz++) for (int dx = -6; dx <= 6; dx++)
                dens += plane[Ores.CellIndex((i + dx) * G + 80, (j + dz) * G + 80, spec.mapCells, spec.mapCellVoxels)];
            cand.Add((hi - lo + 0.3f * (chi - clo) + dens * 0.02f, i, j));   // biome at (i, j): biome[]
        }
        cand.Sort((a, b) => a.score.CompareTo(b.score));
        var blocks = new List<Block>
        {
            new Block { name = "oak_spacecol", sp = TreeGallery.SP_OAK, me = TreeGallery.M_SPACE, S = 440 },
            new Block { name = "oak_lsystem", sp = TreeGallery.SP_OAK, me = TreeGallery.M_LSYS, S = 440 },
            new Block { name = "oak_template", sp = TreeGallery.SP_OAK, me = TreeGallery.M_TEMPLATE, S = 440 },
            new Block { name = "banyan_spacecol", sp = TreeGallery.SP_BANYAN, me = TreeGallery.M_SPACE, S = 440 },
            new Block { name = "banyan_lsystem", sp = TreeGallery.SP_BANYAN, me = TreeGallery.M_LSYS, S = 440 },
            new Block { name = "banyan_template", sp = TreeGallery.SP_BANYAN, me = TreeGallery.M_TEMPLATE, S = 440 },
            new Block { name = "redwood_spacecol", sp = TreeGallery.SP_REDWOOD, me = TreeGallery.M_SPACE, S = 300 },
            new Block { name = "birch_spacecol", sp = TreeGallery.SP_BIRCH, me = TreeGallery.M_SPACE, S = 240 },
            new Block { name = "spruce_spacecol", sp = TreeGallery.SP_SPRUCE, me = TreeGallery.M_SPACE, S = 240 },
            new Block { name = "acacia_spacecol", sp = TreeGallery.SP_ACACIA, me = TreeGallery.M_SPACE, S = 300 },
            new Block { name = "today", today = true, S = 300 },
        };
        // Run 1's sites whose stills were good stay (Screenshots/200/engine); the others are re-sited:
        // oak_spacecol (a town pad), banyan_spacecol / _lsystem (snow), redwood (a town in frame), acacia (snow).
        var keep = new Dictionary<string, int2>
        {
            ["oak_lsystem"] = new int2(306, 311), ["oak_template"] = new int2(282, 337), ["banyan_template"] = new int2(285, 197),
            ["birch_spacecol"] = new int2(237, 141), ["spruce_spacecol"] = new int2(220, 130), ["today"] = new int2(301, 349),
        };
        byte Pref(Block bl) => bl.sp == TreeGallery.SP_BANYAN ? GenBiome.Jungle : bl.sp == TreeGallery.SP_ACACIA ? GenBiome.Savanna : GenBiome.Forest;
        var picked = new List<int2>();
        foreach (var bl in blocks) picked.Add(keep.TryGetValue(bl.name, out var kp) ? kp : new int2(-1, -1));
        for (int pass = 0; pass < 2; pass++)   // pass 0: the species' own biome; pass 1: any (non-snowy) biome
        for (int b = 0; b < blocks.Count; b++)
        {
            if (picked[b].x >= 0) continue;
            foreach (var c in cand)
            {
                if (pass == 0 && biome[c.j * n + c.i] != Pref(blocks[b])) continue;
                if (biome[c.j * n + c.i] == GenBiome.Snow && blocks[b].sp != TreeGallery.SP_SPRUCE) continue;   // only the spruce on snow
                bool far = true;
                foreach (var p in picked) if (p.x >= 0 && math.lengthsq(new float2(p.x - c.i, p.y - c.j)) < 20 * 20) { far = false; break; }   // 320 m apart
                if (far) { picked[b] = new int2(c.i, c.j); break; }
            }
        }
        string missing = string.Join(", ", blocks.FindAll(bl => picked[blocks.IndexOf(bl)].x < 0).ConvertAll(bl => bl.name));
        picked.RemoveAll(p => p.x < 0);
        Assert.AreEqual(blocks.Count, picked.Count, $"only {picked.Count} flat open sites (of {cand.Count} candidates); none for {missing}");
        // today's trees: three mature forest broadleaves (192's default look) from anywhere on the map
        var todayCells = new List<int2>();
        fixed (byte* pl = plane)
        {
            var tv = view; tv.trees = pl;
            var buf = stackalloc TreePrim[Trees.MAX_PRIMS];
            int cells = spec.worldSizeVoxels >> Trees.CELL_SHIFT;
            for (int cz = 40; cz < cells - 40 && todayCells.Count < 3; cz += 7)
            for (int cx = 40; cx < cells - 40 && todayCells.Count < 3; cx += 11)
            {
                int nn = 0;
                if (!Trees.Build(in spec, in tv, in o, cx, cz, buf, ref nn, out Tree t)) continue;
                if (t.biome != GenBiome.Forest || t.shape != Trees.FORM_BROAD || t.hi.y - t.root.y < 125 || t.hi.y - t.root.y > 165) continue;
                bool far = true; foreach (var p in todayCells) if (math.abs(p.x - cx) + math.abs(p.y - cz) < 200) far = false;
                if (far) todayCells.Add(new int2(cx, cz));
            }
        }
        Assert.AreEqual(3, todayCells.Count, "three of today's forest trees");
        var lay = new StringBuilder("# task 200 tree gallery, seed 42 map v4 (TreeGalleryTests.Gallery_Layout); voxels\n");
        var poses = new List<string>(); var note = new StringBuilder();
        string M(float v) => (v / 10f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        string P(string name, int3 e, int3 t) => $"{name}|{M(e.x)},{M(e.y)},{M(e.z)}|{M(t.x)},{M(t.y)},{M(t.z)}";
        for (int b = 0; b < blocks.Count; b++)
        {
            var bl = blocks[b];
            int bx = picked[b].x * G + 80, bz = picked[b].y * G + 80;
            int g = ShapeField.Sample(in spec, in view, in o, bx + 0.5f, bz + 0.5f, 1).height;
            lay.AppendLine($"# block {bl.name} centre ({bx}, {bz}) ground {g} biome {biome[picked[b].y * n + picked[b].x]}");
            lay.AppendLine($"clear {bx - bl.S - 500} {bz - 1600} {bx + bl.S + 500} {bz + 1100}");
            for (int k = -1; k <= 1; k++)
            {
                if (bl.today) lay.AppendLine($"today {todayCells[k + 1].x} {todayCells[k + 1].y} {bx + k * bl.S} {bz}");
                else lay.AppendLine($"tree {bl.sp} {bl.me} {k + 2} {bx + k * bl.S} {bz}");
            }
            lay.AppendLine($"marker {bx + bl.S / 2} {bz + 80}");
            // method framing (oak/banyan blocks, today), species framing (spacecol blocks, today), close-up (oak spacecol, today)
            if (bl.sp == TreeGallery.SP_OAK || bl.sp == TreeGallery.SP_BANYAN || bl.today)
                    poses.Add(P("m_" + bl.name, new int3(bx, g + 60, bz + 750), new int3(bx, g + 130, bz)));
            if (bl.me == TreeGallery.M_SPACE || bl.today)
                poses.Add(P("s_" + bl.name, new int3(bx, g + 80, bz + 950), new int3(bx, g + 280, bz)));
            if ((bl.sp == TreeGallery.SP_OAK && bl.me == TreeGallery.M_SPACE) || bl.today)
            {
                int gr = g;
                poses.Add(P("c_" + bl.name, new int3(bx + 120, gr + 90, bz + 170), new int3(bx, gr + 110, bz)));
            }
        }
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "gallery_42.txt"), lay.ToString());
        File.WriteAllText(Path.Combine(LogDir, "poses_42.txt"), string.Join(";", poses));
        UnityEngine.Debug.Log($"[TreeGallery] layout: {cand.Count} candidates, grid {sw.ElapsedMilliseconds} ms\n{lay}\n{string.Join("\n", poses)}");
    }
}
