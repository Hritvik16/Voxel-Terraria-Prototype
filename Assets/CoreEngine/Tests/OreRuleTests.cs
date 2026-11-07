// Assets/CoreEngine/Tests/OreRuleTests.cs
//
// Task 181 (placement, resource deposits, `-ores 1`; default OFF): proofs for
// TerrainGen.OreRules through the engine's generators.
//   Twins_ContentResolves            the latent twin table: generator = content,
//                                    every cavern / underworld host has a twin,
//                                    stage 0 = the host (colour, tier, drop, name),
//                                    the unlock stage = the ore.
//   Sites_OnVsOff_...                (Slow) seed 42, ADVERSARIAL sites: the tallest
//                                    cliff, the deepest canyon wall, a badlands wall,
//                                    a harshness-4 cavern and underworld chunk. ON
//                                    twice = same bytes; ON vs OFF changes only rock
//                                    (air and fluid untouched); FarQuery points =
//                                    chunk; shell 0 = Generate, shells 1-2 = the
//                                    0.1 m voxel at the sample wherever either is
//                                    ore; brick skip on = off; twins sit in their
//                                    host; veins show on the wall faces. Cost ON vs
//                                    OFF (dense bricks, ms), face / cut-away images.
//   FourSeeds_StartHidden_AndTable   (Slow) 42 / 7 / 2026 / 31337: no rule ore with
//                                    open sky beside it round either play start
//                                    (and at the zone's steepest spot); deposits
//                                    per ore x biome / harshness / depth, ON vs OFF.
// Writes Logs/TerrainGen/ores181/*.txt and Screenshots/181/*.png.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class OreRuleTests
{
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "ores181");
    private static readonly string ShotDir = Path.Combine("Screenshots", "181");
    private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };

    private static readonly HashSet<byte> RuleOreEngine = new HashSet<byte>();
    private static readonly HashSet<byte> TwinEngine = new HashSet<byte>();
    static OreRuleTests()
    {
        for (byte g = Ores.Copper; g <= Ores.Emberstone; g++) RuleOreEngine.Add(Materials.FromTg2(g));
        for (int i = 0; i < LatentOres.TwinCount; i++) { RuleOreEngine.Add(Materials.FromTg2(LatentOres.TwinCode(i))); TwinEngine.Add(Materials.FromTg2(LatentOres.TwinCode(i))); }
    }

    [TearDown]
    public void ResetStage() => LatentOres.SetStage(0);

    // ------------------------------------------------------------------
    [Test]
    public void Twins_ContentResolves()
    {
        Assert.AreEqual(OreRulePlane.TWINS.Length / 3, LatentOres.TwinCount);
        for (int i = 0; i < LatentOres.TwinCount; i++)
        {
            byte t = OreRulePlane.TWINS[i * 3], ore = OreRulePlane.TWINS[i * 3 + 1], host = OreRulePlane.TWINS[i * 3 + 2];
            Assert.AreEqual(t, LatentOres.TwinCode(i)); Assert.AreEqual(ore, LatentOres.OreCode(i)); Assert.AreEqual(host, LatentOres.HostCode(i));
            Assert.AreEqual(t, OreRules.Twin(ore, host)); Assert.AreEqual(ore, OreRules.TwinOre(t)); Assert.AreEqual(host, OreRulePlane.TwinHost(t));
            Assert.IsTrue(OreRules.IsLatentOre(ore));
            Assert.AreEqual(Materials.FromTg2(t), VoxelEngine.Rendering.FarField.GenToEngine(t), "twin must map to its own engine id");
            Assert.IsTrue(GenMat.IsSolid(t), "twin must be solid");
        }
        // every host the generator can put a latent ore in has a twin
        for (byte b = 0; b < GenBiome.Count; b++)
        {
            Assert.AreNotEqual(0, OreRules.Twin(Ores.Cobalt, GroundRules.CavernRock(b)), $"no cobalt twin for biome {b}'s cavern rock (rules)");
            Assert.AreNotEqual(0, OreRules.Twin(Ores.Cobalt, DetailField.BiomeRock(b, 2)), $"no cobalt twin for biome {b}'s cavern rock (layers)");
        }
        foreach (byte u in new[] { GenMat.Ash, GenMat.Obsidian, GenMat.MagmaCrust })
            Assert.AreNotEqual(0, OreRules.Twin(Ores.Emberstone, u));
        // no twin code collides with any other material
        var used = new HashSet<byte>();
        foreach (var f in typeof(GenMat).GetFields())
            if (f.FieldType == typeof(byte)) used.Add((byte)f.GetValue(null));
        for (int i = 0; i < LatentOres.TwinCount; i++) Assert.IsFalse(used.Contains(LatentOres.TwinCode(i)), $"twin {LatentOres.TwinCode(i)} collides with a GenMat");

        for (int stage = 0; stage <= 3; stage++)
        {
            LatentOres.SetStage((byte)stage);
            for (int i = 0; i < LatentOres.TwinCount; i++)
            {
                byte te = Materials.FromTg2(LatentOres.TwinCode(i)), oe = Materials.FromTg2(LatentOres.OreCode(i)), he = Materials.FromTg2(LatentOres.HostCode(i));
                bool open = stage >= LatentOres.UnlockStage(oe);
                byte want = open ? oe : he;
                Assert.AreEqual(want, LatentOres.Resolve(te));
                Assert.AreEqual(Tools.Hardness(want), Tools.Hardness(te)); Assert.AreEqual(Tools.RequiredTier(want), Tools.RequiredTier(te));
                for (int tier = 0; tier <= 6; tier++) Assert.AreEqual(Tools.Cost(want, tier), Tools.Cost(te, tier));
                Assert.AreEqual(Items.ItemFor(want), Items.ItemFor(te)); Assert.AreEqual(Items.Name(want), Items.Name(te));
                var a = MaterialPalette.Of(te); var b = MaterialPalette.Of(want);
                Assert.IsTrue(a.R == b.R && a.G == b.G && a.B == b.B, $"stage {stage} twin {te} colour != {want}");
                if (stage == 0)
                {
                    var raw = MaterialPalette.Of(he);   // the generated table: twin row = host row
                    Assert.IsTrue(a.R == raw.R && a.G == raw.G && a.B == raw.B);
                }
            }
        }
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

    private sealed class Empty : IDisposable
    {
        private readonly BrickDataPool _pool = new BrickDataPool(16);
        public readonly ChunkStore store;
        public Empty() { store = new ChunkStore(_pool, new ChunkHandleAllocator(1)); }
        public void Dispose() => _pool.Dispose();
    }

    private static bool Solid(byte m) => m != Materials.Air && !MaterialRules.IsFluidMaterial(m);

    /// A chunk stack (one chunk column, layers y0..y1) and its per-column top solid y.
    private sealed class Stack
    {
        public int2 cc; public int y0, y1;
        public Dictionary<int, byte[]> layers = new Dictionary<int, byte[]>();
        public byte At(int x, int y, int z)   // stack-local x, z in [0, 128), world y
        {
            if (!layers.TryGetValue(y >> 7, out var a)) return 255;
            return a[TerrainGenChunkSource.Index(x, y & 127, z)];
        }
        /// Top solid y per column; int.MaxValue = unknown (solid at the stack's top).
        public int[] Tops()
        {
            var t = new int[128 * 128];
            int yTop = (y1 << 7) + 127, yBot = y0 << 7;
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
            {
                int y = yTop;
                if (Solid(At(x, y, z))) { t[z * 128 + x] = int.MaxValue; continue; }
                while (y >= yBot && !Solid(At(x, y, z))) y--;
                t[z * 128 + x] = y;
            }
            return t;
        }
    }

    /// Rule-ore voxels of the stack with an open-sky (above its column's top) air or
    /// fluid neighbour inside the stack: what a player could see without digging.
    private static int Exposed(Stack s, out int total, out string first, uint[] zonePlane = null, TerrainSpec spec = default)
    {
        var tops = s.Tops();
        total = 0; first = null; int n = 0;
        int3[] d = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
        for (int ly = s.y0; ly <= s.y1; ly++)
        {
            var a = s.layers[ly];
            for (int i = 0; i < a.Length; i++)
            {
                if (!RuleOreEngine.Contains(a[i])) continue;
                total++;
                int x = i & 127, z = (i >> 7) & 127, y = (ly << 7) + (i >> 14);
                if (zonePlane != null &&   // the start test: only the zone's own cells
                    (zonePlane[Ores.CellIndex((s.cc.x << 7) + x, (s.cc.y << 7) + z, spec.mapCells, spec.mapCellVoxels)] & OreRules.START) == 0) continue;
                foreach (var dd in d)
                {
                    int nx = x + dd.x, ny = y + dd.y, nz = z + dd.z;
                    if (nx < 0 || nz < 0 || nx > 127 || nz > 127) continue;
                    byte m = s.At(nx, ny, nz);
                    if (m == 255 || Solid(m)) continue;
                    int top = tops[nz * 128 + nx];
                    if (top == int.MaxValue || ny <= top) continue;   // under something: a cave, not the open air
                    if (n == 0) first = $"({(s.cc.x << 7) + x}, {y}, {(s.cc.y << 7) + z}) mat {a[i]} beside {m} at y {ny} over top {top}";
                    n++;
                    break;
                }
            }
        }
        return n;
    }

    private static Stack BuildStack(TerrainGenChunkSource src, int2 cc, int yLo, int yHi, bool on)
    {
        src.SetOres(on);
        var s = new Stack { cc = cc, y0 = yLo >> 7, y1 = yHi >> 7 };
        for (int ly = s.y0; ly <= s.y1; ly++) s.layers[ly] = Gen(src, new int3(cc.x, ly, cc.y), out _, out _);
        return s;
    }

    /// Height range of chunk column cc (8-voxel grid + the chunk's edges).
    private static int2 HeightRange(TerrainGenChunkSource src, int2 cc)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        int lo = int.MaxValue, hi = int.MinValue;
        for (int z = 0; z <= 128; z += 8)
        for (int x = 0; x <= 128; x += 8)
        {
            var c = ShapeField.Sample(in spec, in view, in o, (cc.x << 7) + math.min(x, 127) + 0.5f, (cc.y << 7) + math.min(z, 127) + 0.5f, 1);
            lo = math.min(lo, c.height); hi = math.max(hi, math.max(c.height, c.water));
        }
        return new int2(lo, hi);
    }

    // ------------------------------------------------------------------
    // Sites (seed 42): adversarial walls and harsh deep chunks
    // ------------------------------------------------------------------
    private struct Site { public string name; public int2 col; public int yLo, yHi; public bool wall; }

    private static float Relief(WorldMap m, int cx, int cz)
    {
        int n = m.Spec.mapCells; float h = m.Height[cz * n + cx], r = 0f;
        if (cx > 0) r = math.max(r, math.abs(m.Height[cz * n + cx - 1] - h));
        if (cx < n - 1) r = math.max(r, math.abs(m.Height[cz * n + cx + 1] - h));
        if (cz > 0) r = math.max(r, math.abs(m.Height[(cz - 1) * n + cx] - h));
        if (cz < n - 1) r = math.max(r, math.abs(m.Height[(cz + 1) * n + cx] - h));
        return r;
    }

    /// The steepest LOD-0 column (4-voxel grid) of map cell (cx, cz).
    private static int2 SteepestColumn(TerrainGenChunkSource src, int cx, int cz, out int step, out int h)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        int cv = spec.mapCellVoxels; step = -1; h = 0; int2 best = default;
        for (int z = 0; z < cv; z += 4)
        for (int x = 0; x < cv; x += 4)
        {
            int gx = cx * cv + x, gz = cz * cv + z;
            int c0 = ShapeField.Sample(in spec, in view, in o, gx + 0.5f, gz + 0.5f, 1).height;
            int m = 0;
            foreach (var d in new[] { new int2(4, 0), new int2(-4, 0), new int2(0, 4), new int2(0, -4) })
                m = math.max(m, math.abs(ShapeField.Sample(in spec, in view, in o, gx + d.x + 0.5f, gz + d.y + 0.5f, 1).height - c0));
            if (m > step) { step = m; best = new int2(gx, gz); h = c0; }
        }
        return best;
    }

    private static List<Site> FindSites(TerrainGenChunkSource src, StringBuilder log)
    {
        var map = src.Map; var rec = map.Record; int n = src.Spec.mapCells;
        uint[] plane = OreRulePlane.CellPlane(map, src.Spec);
        var sites = new List<Site>();
        void Wall(string name, Func<RecordCell, bool> pred)
        {
            float best = -1f; int bx = 0, bz = 0;
            for (int cz = 1; cz < n - 1; cz += 1)
            for (int cx = 1; cx < n - 1; cx += 1)
            {
                var rc = rec.AtCell(cx, cz);
                uint pc = plane[cz * n + cx];
                if (rc.IsSea || (pc & (OreRules.START | OreRules.ROUTE)) != 0 || !pred(rc)) continue;
                float r = Relief(map, cx, cz);
                if (r > best) { best = r; bx = cx; bz = cz; }
            }
            Assert.Greater(best, 0f, $"no {name} cell");
            int2 col = SteepestColumn(src, bx, bz, out int step, out int h);
            int2 cc = col >> 7;
            var hr = HeightRange(src, cc);
            int yLo = math.max(hr.x - 64, h - 256), yHi = math.min(hr.y + 8, h + 256);
            var rc2 = rec.AtCell(bx, bz);
            sites.Add(new Site { name = name, col = col, yLo = yLo, yHi = yHi, wall = true });
            log.AppendLine($"  site {name}: cell ({bx}, {bz}) relief {best:F0} vox, biome {rc2.Biome}, arch {rc2.Archetype}, band {rc2.MoveBand}, " +
                           $"harsh {plane[bz * n + bx] & 7}; steepest column {col} step {step} / 4 vox at h {h}; chunks y {yLo >> 7}..{yHi >> 7}");
        }
        Wall("tall_cliff", rc => rc.MoveBand == Danger.Sheer);
        Wall("deep_canyon", rc => rc.Archetype == Regions.Canyonlands);
        Wall("badlands_wall", rc => rc.Biome == GenBiome.Badlands && rc.MoveBand >= Danger.Climb);
        // harshness 4: one cavern chunk (cobalt) and one underworld chunk (emberstone)
        for (int cz = n / 4; cz < n; cz += 3)
        {
            bool done = false;
            for (int cx = n / 4; cx < n && !done; cx += 3)
            {
                if ((plane[cz * n + cx] & OreRules.HARSH_MASK) != 4u) continue;
                int2 col = new int2(cx * src.Spec.mapCellVoxels + 40, cz * src.Spec.mapCellVoxels + 40);
                var spec = src.Spec;
                sites.Add(new Site { name = "harsh_cavern", col = col, yLo = spec.CavernTopY - 300, yHi = spec.CavernTopY - 300 });
                sites.Add(new Site { name = "harsh_underworld", col = col, yLo = spec.UnderworldTopY - 900, yHi = spec.UnderworldTopY - 900 });
                var rc = rec.AtCell(cx, cz);
                log.AppendLine($"  site harsh_cavern / harsh_underworld: cell ({cx}, {cz}) biome {rc.Biome} arch {rc.Archetype}, column {col}");
                done = true;
            }
            if (done) break;
        }
        return sites;
    }

    [Test, Category("Slow"), Timeout(1200000)]
    public void Sites_OnVsOff_Deterministic_PointShellsSkipAgree_Images()
    {
        Directory.CreateDirectory(LogDir); Directory.CreateDirectory(ShotDir);
        var log = new StringBuilder();
        log.AppendLine("Task 181 sites, seed 42 (ON = -ores 1, OFF = step 3's placement)");
        using var src = new TerrainGenChunkSource(42u);
        var sites = FindSites(src, log);
        Assert.GreaterOrEqual(sites.Count, 5, "missing a site kind");
        int wallExposedSites = 0;
        long denseOn = 0, denseOff = 0; double msOn = 0, msOff = 0; int chunks = 0;
        var oreCount = new Dictionary<string, int>();
        var poses = new List<string>();
        foreach (var s in sites)
        {
            int2 cc = s.col >> 7;
            var on = new Stack { cc = cc, y0 = s.yLo >> 7, y1 = s.yHi >> 7 };
            var off = new Stack { cc = cc, y0 = on.y0, y1 = on.y1 };
            int changed = 0, ruleOre = 0, twins = 0, veinish = 0;
            for (int ly = on.y0; ly <= on.y1; ly++)
            {
                int3 coord = new int3(cc.x, ly, cc.y);
                src.SetOres(true);
                byte[] a = Gen(src, coord, out int dA, out double mA);
                byte[] a2 = Gen(src, coord, out _, out _);
                src.BrickSkip = false; byte[] a3 = Gen(src, coord, out _, out _); src.BrickSkip = true;
                src.SetOres(false);
                byte[] b = Gen(src, coord, out int dB, out double mB);
                denseOn += dA; denseOff += dB; msOn += mA; msOff += mB; chunks++;
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] != a2[i]) Assert.Fail($"{s.name} chunk {coord} voxel {i}: ON not deterministic ({a[i]} vs {a2[i]})");
                    if (a[i] != a3[i]) Assert.Fail($"{s.name} chunk {coord} voxel {i}: brick skip on {a[i]} != off {a3[i]}");
                    if (a[i] == b[i]) { if (RuleOreEngine.Contains(a[i])) ruleOre++; continue; }
                    changed++;
                    Assert.IsTrue(Solid(b[i]) && Solid(a[i]), $"{s.name} chunk {coord} voxel {i}: ON {a[i]} vs OFF {b[i]} -- air / fluid changed");
                    bool offOre = RuleOreEngine.Contains(b[i]);
                    Assert.IsTrue(RuleOreEngine.Contains(a[i]) || offOre, $"{s.name} chunk {coord} voxel {i}: ON {a[i]} vs OFF {b[i]} -- not an ore change");
                    if (RuleOreEngine.Contains(a[i])) ruleOre++;
                    if (TwinEngine.Contains(a[i]))
                    {
                        twins++;
                        byte host = Materials.FromTg2(OreRulePlane.TwinHost((byte)(a[i] - Materials.TG2_BASE)));
                        if (!offOre) Assert.AreEqual(host, b[i], $"{s.name} twin {a[i]} not in its host (OFF has {b[i]})");
                    }
                }
                foreach (byte m in a)
                    if (RuleOreEngine.Contains(m))
                    {
                        string k = $"{s.name}:{Items.Name(LatentOres.IsTwin(m) ? Materials.FromTg2((byte)OreRules.TwinOre((byte)(m - Materials.TG2_BASE))) : m)}{(LatentOres.IsTwin(m) ? " (latent)" : "")}";
                        oreCount[k] = oreCount.TryGetValue(k, out int c0) ? c0 + 1 : 1;
                    }
                on.layers[ly] = a; off.layers[ly] = b;

                // the point path: ore voxels (every 4th) + random points
                var rng = new Unity.Mathematics.Random(181u + (uint)ly);
                var pts = new List<int3>(); var want = new List<byte>();
                int3 org = coord << 7;
                for (int i = 0; i < a.Length; i++)
                    if (RuleOreEngine.Contains(a[i]) && (i & 3) == 0) { pts.Add(org + new int3(i & 127, i >> 14, (i >> 7) & 127)); want.Add(a[i]); }
                for (int k = 0; k < 3000; k++) { int3 l = rng.NextInt3(new int3(0), new int3(128)); pts.Add(org + l); want.Add(a[TerrainGenChunkSource.Index(l.x, l.y, l.z)]); }
                src.SetOres(true);
                using (var empty = new Empty())
                using (var fq = new FarQuery(empty.store, empty.store, src, EditOpLog.InMemory(42u), null, 42u))
                {
                    var p = new NativeArray<int3>(pts.ToArray(), Allocator.TempJob);
                    var res = new NativeArray<byte>(pts.Count, Allocator.TempJob);
                    try
                    {
                        fq.GetVoxels(p, res);
                        for (int i = 0; i < pts.Count; i++) Assert.AreEqual(want[i], res[i], $"{s.name} point {pts[i]}: FarQuery {res[i]} != chunk {want[i]}");
                    }
                    finally { p.Dispose(); res.Dispose(); }
                }
                // shell 0 = Generate
                using (var sc = new TerrainGenChunkSource.ShellChunk())
                {
                    src.GenerateShellChunk(0, coord, sc);
                    for (int i = 0; i < a.Length; i++)
                    {
                        byte g = sc.Get(i & 127, i >> 14, (i >> 7) & 127);
                        if (g != a[i]) Assert.Fail($"{s.name} shell 0 chunk {coord} voxel {i}: {g} != Generate {a[i]}");
                    }
                }
            }
            // shells 1-2: wherever the 0.1 m voxel or the shell voxel is rule ore, they agree
            src.SetOres(true); src.ShellFill = false;
            int cmp = 0, shellOre = 0;
            try
            {
                using var sc = new TerrainGenChunkSource.ShellChunk();
                for (int k = 1; k <= 2; k++)
                for (int ly = on.y0; ly <= on.y1; ly++)
                {
                    int3 f0 = new int3(cc.x << 7, ly << 7, cc.y << 7);
                    int3 sc3 = f0 >> (7 + k);
                    src.GenerateShellChunk(k, sc3, sc);
                    int3 origin = sc3 << (7 + k);
                    int half = (1 << (k - 1)) - 1;
                    for (int z = 0; z < 128; z++)
                    for (int x = 0; x < 128; x++)
                    for (int y = 0; y < 128; y++)
                    {
                        int3 f = origin + new int3(x << k, y << k, z << k) + half;
                        if ((f.x >> 7) != cc.x || (f.z >> 7) != cc.y || (f.y >> 7) != ly) continue;
                        // the shell's top sample of a column wears the material AT the top (task 74 Q1 (a))
                        int gh = sc.GroundY(x, z);
                        int3 fw = y == ((gh - origin.y - half) >> k) ? new int3(f.x, gh, f.z) : f;
                        byte want1 = on.At(f.x - (cc.x << 7), fw.y, f.z - (cc.y << 7));
                        if (want1 == 255) continue;   // the top lies outside the stack
                        byte got = sc.Get(x, y, z);
                        if (!RuleOreEngine.Contains(want1) && !RuleOreEngine.Contains(got)) continue;
                        cmp++; if (RuleOreEngine.Contains(got)) shellOre++;
                        if (want1 != got) Assert.Fail($"{s.name} shell {k} sample {f}: {got} != 0.1 m voxel {want1}; " + Diag(src, f, k));
                    }
                }
            }
            finally { src.ShellFill = true; }

            int exposedOn = Exposed(on, out int totOn, out string firstOn);
            int exposedOff = Exposed(off, out int totOff, out _);
            if (s.wall && exposedOn > 0) wallExposedSites++;
            log.AppendLine($"  {s.name}: {on.y1 - on.y0 + 1} chunk(s); changed {changed:N0} voxels, rule ore ON {totOn:N0} (twins {twins:N0}) vs OFF {totOff:N0}; " +
                           $"ore with open sky beside it ON {exposedOn:N0} vs OFF {exposedOff:N0}{(firstOn != null ? " e.g. " + firstOn : "")}; " +
                           $"shells 1-2 ore samples compared {cmp:N0} (shell ore {shellOre:N0}), all equal; FarQuery equal; skip on = off");
            if (s.wall) poses.Add(FaceImage(src, s, on, off));
            else CutAwayImage(s, on, off);
        }
        log.AppendLine("  ore voxels per site:");
        foreach (var kv in oreCount) log.AppendLine($"    {kv.Key}: {kv.Value:N0}");
        log.AppendLine($"  ROUGH COST (these {chunks} chunks, Editor, Burst): dense bricks ON {denseOn:N0} vs OFF {denseOff:N0} " +
                       $"({(denseOff > 0 ? 100.0 * (denseOn - denseOff) / denseOff : 0):+0.0;-0.0} %), gen ms ON {msOn:F0} vs OFF {msOff:F0}");
        File.WriteAllText(Path.Combine(LogDir, "sites.txt"), log.ToString());
        File.WriteAllText(Path.Combine(LogDir, "poses.txt"), string.Join(";", poses));   // -wsfixed (metres)
        UnityEngine.Debug.Log(log.ToString());
        Assert.Greater(wallExposedSites, 0, "no wall site shows a vein on its face");
    }

    private static string Diag(TerrainGenChunkSource src, int3 f, int k)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var c0 = ShapeField.Sample(in spec, in view, in o, f.x + 0.5f, f.z + 0.5f, 1);
        var c1 = ShapeField.Sample(in spec, in view, in o, f.x + 0.5f, f.z + 0.5f, 1, true, 1 << k);
        int st = 0;
        foreach (var d in new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) })
            st = math.max(st, math.abs(ShapeField.Sample(in spec, in view, in o, f.x + d.x + 0.5f, f.z + d.y + 0.5f, 1, false).height - c0.height));
        var cd = DetailField.PrepareColumn(in spec, in o, f.x, f.z);
        int idx = Ores.CellIndex(f.x, f.z, spec.mapCells, spec.mapCellVoxels);
        cd.oreCell = src.OreCellPlane[idx];
        cd.oreMul = OreTable.DensityPlane(src.Map.Record, spec.mapCells)[idx];
        var cr = DetailField.CullBrick(in spec, in o, f.x & ~7, f.y & ~7, f.z & ~7);
        byte m0 = DetailField.Material(in spec, in o, in c0, in cd, st, f.x, f.y, f.z, in cr);
        byte m1 = DetailField.Material(in spec, in o, in c1, in cd, st, f.x, f.y, f.z, in cr);
        return $"h {c0.height}/{c1.height} biome {c0.biome}/{c1.biome} surf {c0.surfBiome}/{c1.surfBiome} cover {c0.cover}/{c1.cover} step {st} " +
               $"cell 0x{cd.oreCell:X} mul {cd.oreMul} depth {c0.height - f.y}; managed Material fine-col {m0} shell-col {m1}";
    }

    // ------------------------------------------------------------------
    // Images
    // ------------------------------------------------------------------
    private static Color32 Col(byte m, float shade)
    {
        var c = MaterialPalette.Of(m);
        return new Color32((byte)math.clamp(c.R * shade * 330f, 0, 255), (byte)math.clamp(c.G * shade * 330f, 0, 255), (byte)math.clamp(c.B * shade * 330f, 0, 255), 255);
    }

    private static void WritePng(string path, Color32[] px, int w, int h, int scale)
    {
        var big = new Color32[w * scale * h * scale];
        for (int y = 0; y < h * scale; y++)
        for (int x = 0; x < w * scale; x++) big[y * w * scale + x] = px[(y / scale) * w + x / scale];
        var t = new Texture2D(w * scale, h * scale, TextureFormat.RGBA32, false);
        t.SetPixels32(big); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
    }

    /// Elevation of the wall: looking from the low side of the steepest column, each
    /// pixel (u, y) is the first solid voxel along the view axis, darker farther in.
    /// Panels: OFF | ON as seen (stage 0) | ON with latent ores shown (stage 2).
    private static string FaceImage(TerrainGenChunkSource src, Site s, Stack on, Stack off)
    {
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        int hx1 = ShapeField.Sample(in spec, in view, in o, s.col.x + 4.5f, s.col.y + 0.5f, 1).height, hx0 = ShapeField.Sample(in spec, in view, in o, s.col.x - 3.5f, s.col.y + 0.5f, 1).height;
        int hz1 = ShapeField.Sample(in spec, in view, in o, s.col.x + 0.5f, s.col.y + 4.5f, 1).height, hz0 = ShapeField.Sample(in spec, in view, in o, s.col.x + 0.5f, s.col.y - 3.5f, 1).height;
        bool alongX = math.abs(hx1 - hx0) >= math.abs(hz1 - hz0);   // the view axis
        int dir = alongX ? (hx1 < hx0 ? -1 : 1) : (hz1 < hz0 ? -1 : 1);   // from the low side toward the face
        int yLo = on.y0 << 7, H = (on.y1 - on.y0 + 1) << 7, W = 128;
        var px = new Color32[W * 3 * H];
        for (int panel = 0; panel < 3; panel++)
        {
            var st = panel == 0 ? off : on;
            LatentOres.SetStage(panel == 2 ? (byte)2 : (byte)0);
            for (int yy = 0; yy < H; yy++)
            for (int u = 0; u < W; u++)
            {
                Color32 c = new Color32(150, 190, 225, 255);
                for (int t = 0; t < 128; t++)
                {
                    int a = dir > 0 ? t : 127 - t;
                    int x = alongX ? a : u, z = alongX ? u : a;
                    byte m = st.At(x, yLo + yy, z);
                    if (m == 255 || !Solid(m)) { if (m != 255 && m != Materials.Air) { c = Col(m, 0.8f); break; } continue; }
                    c = Col(m, 1f - 0.6f * t / 128f); break;
                }
                px[yy * W * 3 + panel * W + u] = c;
            }
        }
        LatentOres.SetStage(0);
        WritePng(Path.Combine(ShotDir, $"face_{s.name}.png"), px, W * 3, H, 3);
        // a player pose: 30 m out on the low side, looking at the face's middle
        int2 ax = alongX ? new int2(dir, 0) : new int2(0, dir);
        int2 eye = s.col - ax * 300;
        int ge = ShapeField.Sample(in spec, in view, in o, eye.x + 0.5f, eye.y + 0.5f, 1).height;
        int hc = ShapeField.Sample(in spec, in view, in o, s.col.x + 0.5f, s.col.y + 0.5f, 1).height;
        int atY = math.max((hc + ge) / 2, ge + 20), eyeY = math.max(ge + 40, atY + 20);
        string M(float v) => (v * 0.1f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        return $"{s.name}|{M(eye.x + 0.5f)},{M(eyeY)},{M(eye.y + 0.5f)}|{M(s.col.x + 0.5f)},{M(atY)},{M(s.col.y + 0.5f)}";
    }

    /// A vertical slice through the chunk at the z with the most rule ore.
    private static void CutAwayImage(Site s, Stack on, Stack off)
    {
        int H = (on.y1 - on.y0 + 1) << 7, W = 128, bestZ = 64, best = -1;
        for (int z = 0; z < 128; z++)
        {
            int n = 0;
            for (int y = 0; y < H; y++) for (int x = 0; x < 128; x++) if (RuleOreEngine.Contains(on.At(x, (on.y0 << 7) + y, z))) n++;
            if (n > best) { best = n; bestZ = z; }
        }
        var px = new Color32[W * 3 * H];
        for (int panel = 0; panel < 3; panel++)
        {
            var st = panel == 0 ? off : on;
            LatentOres.SetStage(panel == 2 ? (byte)2 : (byte)0);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte m = st.At(x, (on.y0 << 7) + y, bestZ);
                px[y * W * 3 + panel * W + x] = m == Materials.Air ? new Color32(20, 20, 24, 255) : Col(m, 1f);
            }
        }
        LatentOres.SetStage(0);
        WritePng(Path.Combine(ShotDir, $"cut_{s.name}.png"), px, W * 3, H, 3);
    }

    // ------------------------------------------------------------------
    // Four seeds: the start stays hidden; the counts table
    // ------------------------------------------------------------------
    [Test, Category("Slow"), Timeout(1800000)]
    public void FourSeeds_StartHidden_AndTable()
    {
        Directory.CreateDirectory(LogDir);
        var log = new StringBuilder();
        log.AppendLine("Task 181 -- four seeds: the start zone (no rule ore with open sky beside it) and the deposit table");
        string[] oreName = { "copper", "iron", "gold", "cobalt", "emberstone" };
        string[] biomeName = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
        int failures = 0;
        foreach (uint seed in Seeds)
        {
            using var src = new TerrainGenChunkSource(seed);
            src.SetOres(true);
            var spec = src.Spec; var view = src.Map.View; var o = src.O; int n = spec.mapCells;
            uint[] plane = OreRulePlane.CellPlane(src.Map, spec);
            byte[] mul = OreTable.DensityPlane(src.Map.Record, n);
            log.AppendLine($"seed {seed}:");

            // ---- the start zone ----
            int zoneCells = 0;
            for (int i = 0; i < plane.Length; i++) if ((plane[i] & OreRules.START) != 0) zoneCells++;
            var blocks = new List<(string what, int2 centre, int r)>();
            for (int mild = 1; mild >= 0; mild--)
                if (PlayStart.Find(src.Map, out var pick, mild == 1)) blocks.Add(($"start{(mild == 1 ? " (mild)" : " (any)")}", pick.column >> 7, 3));
            // the zone's steepest spot (adversarial): steepest map cell by relief, then its steepest column
            float bestR = -1f; int bcx = 0, bcz = 0;
            for (int cz = 1; cz < n - 1; cz++)
            for (int cx = 1; cx < n - 1; cx++)
            {
                if ((plane[cz * n + cx] & OreRules.START) == 0) continue;
                float r = Relief(src.Map, cx, cz);
                if (r > bestR) { bestR = r; bcx = cx; bcz = cz; }
            }
            if (bestR >= 0f) blocks.Add(("zone's steepest cell", SteepestColumn(src, bcx, bcz, out _, out _) >> 7, 1));
            foreach (var bl in blocks)
            {
                int exposed = 0, total = 0, stacks = 0; string first = null;
                for (int dz = -bl.r; dz <= bl.r; dz++)
                for (int dx = -bl.r; dx <= bl.r; dx++)
                {
                    int2 cc = bl.centre + new int2(dx, dz);
                    var hr = HeightRange(src, cc);
                    var st = BuildStack(src, cc, hr.x - 160, hr.y + 16, true);
                    exposed += Exposed(st, out int t, out string f, plane, spec); total += t; stacks++;
                    if (first == null) first = f;
                }
                log.AppendLine($"  {bl.what} round chunk column {bl.centre}: {stacks} chunk columns, rule ore voxels on zone cells {total:N0}, with open sky beside them {exposed}{(first != null ? " e.g. " + first : "")}");
                if (exposed > 0) failures++;
            }
            log.AppendLine($"  start zone: {zoneCells} map cells ({zoneCells * 0.0064:F1} km^2)");

            // ---- the deposit table: passing 32^3 cells over every 8th cell column ----
            var on = new long[5, GenBiome.Count]; var offT = new long[5, GenBiome.Count];
            var harsh = new long[5, 5]; var depthBins = new long[5, 8];
            long cols = 0;
            int cellCols = spec.worldSizeVoxels >> Ores.CELL_SHIFT;
            for (int cz = 4; cz < cellCols; cz += 8)
            for (int cx = 4; cx < cellCols; cx += 8)
            {
                int x = (cx << 5) + 16, z = (cz << 5) + 16;
                var c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                var cd = DetailField.PrepareColumn(in spec, in o, x, z);
                int idx = Ores.CellIndex(x, z, n, spec.mapCellVoxels);
                byte m = mul[idx]; uint cell = plane[idx];
                cols++;
                for (int cy = spec.worldBottomY >> 5; (cy << 5) + 16 < c.height; cy++)
                {
                    int y = (cy << 5) + 16;
                    if (c.height - y <= Ores.MIN_DEPTH) continue;
                    int layer = Ores.Layer(in spec, y, cd.deepOffset, cd.cavernOffset, cd.underOffset);
                    int hsh = (int)(Ores.Hash(spec.seed, cx, cy, cz) & 0xFFFFu);
                    if (hsh < OreRules.CellChance(layer, c.biome, m, cell))
                    {
                        on[layer, c.biome]++; harsh[layer, (int)(cell & 7u)]++;
                        depthBins[layer, math.clamp((spec.seaLevelY - y) / 1000 + 1, 0, 7)]++;
                    }
                    if (hsh < Ores.CellChance(layer, c.biome, m)) offT[layer, c.biome]++;
                }
            }
            log.AppendLine($"  deposits (32^3 cells that hold one) over {cols:N0} sampled cell columns (1 in 64 of the world), ON / OFF:");
            var sb = new StringBuilder("    ore         ");
            foreach (var b in biomeName) sb.Append($"{b,16}");
            sb.Append("            total");
            log.AppendLine(sb.ToString());
            for (int l = 0; l < 5; l++)
            {
                sb.Clear(); sb.Append($"    {oreName[l],-12}"); long a = 0, b2 = 0;
                for (int b = 0; b < GenBiome.Count; b++) { sb.Append($"{on[l, b] + " / " + offT[l, b],16}"); a += on[l, b]; b2 += offT[l, b]; }
                sb.Append($"{a + " / " + b2,17}");
                log.AppendLine(sb.ToString());
            }
            log.AppendLine("    ON by harshness 0-4:  " + string.Join("; ", Rows(harsh, 5, oreName)));
            log.AppendLine("    ON by depth (band 0 = above sea, k = (k-1)..k x 100 m below sea): " + string.Join("; ", Rows(depthBins, 8, oreName)));
        }
        File.WriteAllText(Path.Combine(LogDir, "table.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
        Assert.AreEqual(0, failures, "rule ore with open sky beside it near a start: see " + Path.Combine(LogDir, "table.txt"));
    }

    private static IEnumerable<string> Rows(long[,] t, int cols, string[] names)
    {
        for (int l = 0; l < 5; l++)
        {
            var parts = new List<string>();
            for (int k = 0; k < cols; k++) parts.Add(t[l, k].ToString());
            yield return $"{names[l]} [{string.Join(" ", parts)}]";
        }
    }
}
