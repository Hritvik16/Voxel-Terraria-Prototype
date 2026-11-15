// Assets/CoreEngine/Tests/FluidPocketTests.cs
//
// Task 183 (placement, fluid pockets at rest; default ON since 201, `-fluidpockets 0` = off):
// proofs for TerrainGen.FluidPockets through the engine's generators.
//   Rules_LiningsDoNotReact            every pocket fluid's lining is solid, static
//                                      and has no reaction with that fluid; the
//                                      waterKind codes map to the fluids; every
//                                      pocket fluid maps to an engine fluid.
//   FourSeeds_Plane_Table_KeepOuts     (Slow) 42 / 7 / 2026 / 31337: the plane is
//                                      deterministic; every surface site's rim and
//                                      footprint keep out of water, stamps, routes,
//                                      features, the sea and the start zone; the
//                                      sealed shape holds (fluid at L, rim >= L + 1);
//                                      the counts table (surface: fluid x biome /
//                                      landform; deep: fluid x layer, ON vs OFF).
//   Sites_AtRest_PathsAgree_Adversarial (Slow) seed 42: the largest surface site of
//                                      each fluid, the smallest deep lava / acid
//                                      pocket, and two FORCED sites (on the tallest
//                                      cliff edge, in the deepest canyon floor):
//                                      ON twice = same bytes; brick skip on = off;
//                                      ON vs OFF changes only the site's columns;
//                                      CPU fluid oracle: nothing moves or reacts in
//                                      N ticks (the settle check); FarQuery = chunk;
//                                      shell 0 = Generate; shells 1-2 = the 0.1 m
//                                      voxel wherever either is pocket fluid or
//                                      lining; the far band's column (texel 8-64)
//                                      holds the same fluid at the same level.
//                                      Cut-away images ON vs OFF, player poses.
//   Bank_Rules                         (task 196) the bank / bowl integer rules: BANK_Q8 is
//                                      tan(Cover.STEEP_DEG) rounded down; the rim lands on L + 1;
//                                      the change tapers by <= 1 voxel per ring to 0 at BankBand;
//                                      Ring finds the smallest nesting ellipse; BowlDepth's wall.
// Task 196 adds to the two Slow tests: the rim is flush (L + 1) on every site whose
// excess was not capped; on seed 42's cases (+ a FORCED site on a ~20 deg slope) no
// 4-neighbour step outside the footprint grows by more than 2 voxels ON vs OFF, and
// the ON vs OFF change stays inside the bank's band.
// Writes Logs/TerrainGen/pockets183/*.txt and Screenshots/183/*.png.
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

public unsafe class FluidPocketTests
{
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "pockets183");
    private static readonly string ShotDir = Path.Combine("Screenshots", "183");
    private static readonly uint[] Seeds = { 42u, 7u, 2026u, 31337u };
    private static readonly byte[] PocketFluids = { GenMat.Water, GenMat.Lava, GenMat.Oil, GenMat.Acid, GenMat.Sludge, GenMat.Honey, GenMat.Quicksand };
    private static readonly string[] BiomeName = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
    private static readonly string[] ArchName = { "alpine", "highlands", "canyonlands", "volcanic", "plains", "breaks", "dunes", "coastal cliffs", "plateau", "wetland" };

    private static string FluidName(byte g)
    {
        switch (g)
        {
            case GenMat.Water: return "water"; case GenMat.Lava: return "lava"; case GenMat.Oil: return "oil";
            case GenMat.Acid: return "acid"; case GenMat.Sludge: return "sludge"; case GenMat.Honey: return "honey";
            case GenMat.Quicksand: return "quicksand"; default: return "g" + g;
        }
    }

    // ------------------------------------------------------------------
    [Test]
    public void Rules_LiningsDoNotReact()
    {
        Assert.AreEqual(GenMat.Water, FluidPockets.FluidOf(0));
        Assert.AreEqual(GenMat.Lava, FluidPockets.FluidOf(1));
        foreach (byte f in PocketFluids)
        {
            if (f != GenMat.Water) Assert.AreEqual(f, FluidPockets.FluidOf(f));
            byte fe = VoxelEngine.Rendering.FarField.GenToEngine(f);
            Assert.IsTrue(MaterialRules.IsFluidMaterial(fe), $"{FluidName(f)} -> engine {fe} is not a fluid");
            foreach (byte lin in new[] { FluidPockets.Lining(f), FluidPockets.DeepLining(f) })
            {
                if (lin == 0) { Assert.AreEqual(GenMat.Water, f, "only water's deep pocket goes unlined"); continue; }
                byte le = VoxelEngine.Rendering.FarField.GenToEngine(lin);
                Assert.IsTrue(GenMat.IsSolid(lin), $"{FluidName(f)} lining {lin} not solid");
                Assert.IsFalse(MaterialRules.IsFluidMaterial(le) || MaterialRules.IsMobile(le), $"{FluidName(f)} lining {lin} is mobile");
                Assert.IsFalse(MaterialRules.TryGetReaction(fe, le, out _, out _), $"{FluidName(f)} reacts with its lining {lin}");
            }
        }
        // the deep acid pocket's old wall (task 180b's lining, OchreClay) DID react: the reason for DeepLining
        Assert.IsTrue(MaterialRules.TryGetReaction(Materials.Acid, VoxelEngine.Rendering.FarField.GenToEngine(GenMat.OchreClay), out _, out _));
    }

    [Test]
    public void Bank_Rules()
    {
        // the slope is the cover's steep rule, never over it
        int q = (int)math.floor(math.tan(math.radians(Cover.STEEP_DEG)) * 256f);
        Assert.AreEqual(q, FluidPockets.BANK_Q8, "BANK_Q8 is not tan(Cover.STEEP_DEG) * 256 rounded down");
        const int L = 5000;
        foreach (int up in new[] { 0, 1, 7, 25, 60, 255 })
        foreach (int dn in new[] { 0, 3, 40 })
        {
            int band = FluidPockets.BankBand(up, dn);
            for (int dev = -dn - 5; dev <= up + 5; dev++)
            {
                int h = L + 1 + dev, prev = FluidPockets.BankHeight(h, L, up, dn, 1);
                if (dev <= up && dev >= -dn) Assert.AreEqual(L + 1, prev, $"rim up {up} dn {dn} dev {dev}");
                for (int t = 2; t <= band + 2; t++)
                {
                    int cur = FluidPockets.BankHeight(h, L, up, dn, t);
                    Assert.LessOrEqual(math.abs(cur - prev), 1, $"ring step up {up} dn {dn} dev {dev} t {t}");
                    Assert.LessOrEqual(math.abs(cur - h), math.abs(prev - h), "the change must shrink outward");
                    if (t >= band) Assert.AreEqual(h, cur, $"band end up {up} dn {dn} dev {dev} t {t}");
                    prev = cur;
                }
            }
        }
        foreach (var (rx, rz) in new[] { (24, 18), (40, 28), (52, 31), (14, 14) })
        {
            int band = 40;
            for (int dz = -rz - band; dz <= rz + band; dz += 3)
            for (int dx = -rx - band; dx <= rx + band; dx += 3)
            {
                if (FluidPockets.InFoot(dx, dz, rx, rz)) continue;
                int t = FluidPockets.Ring(dx, dz, rx, rz, band);
                if (t < band) Assert.IsTrue(FluidPockets.InFoot(dx, dz, rx + t, rz + t) && (t == 1 || !FluidPockets.InFoot(dx, dz, rx + t - 1, rz + t - 1)), $"ring ({dx}, {dz})");
                else Assert.IsFalse(FluidPockets.InFoot(dx, dz, rx + band - 1, rz + band - 1));
            }
            int d = FluidPockets.BowlDepth(127, rx, rz), rmin = math.min(rx, rz);
            Assert.LessOrEqual(2f * (d - 1) / rmin, FluidPockets.BANK_Q8 / 256f + 1e-6f, "bowl wall steeper than the bank");
            Assert.AreEqual(3, FluidPockets.BowlDepth(3, rx, rz), "a shallow bowl keeps its depth");
        }
    }

    /// Task 203: the natural outline's rules on 600 synthetic sites (every radius range,
    /// every low direction): inside the planner's box and ellipse (+ 1), the centre in it,
    /// one 4-connected pond, rings nested and inside box (rx + t), the bed shelving at most
    /// 1 voxel per column, irregular (not the ellipse), reaching toward the low direction;
    /// the outline bit off = the ellipse exactly; Pack / Decode round trip. Writes a few
    /// outlines as text to Logs/TerrainGen/outline203/shapes.txt.
    [Test]
    public void Outline_Rules()
    {
        var rng = new Unity.Mathematics.Random(203u);
        var sb = new StringBuilder();
        int towardLow = 0, sites = 0, maxStep = 0; long pairs = 0, steep = 0; double areaSum = 0, xorSum = 0;
        for (int it = 0; it < 600; it++)
        {
            int rx = rng.NextInt(14, 53), rz = math.max(rx * 3 / 4 - 4, (int)(rx * rng.NextFloat(0.6f, 1f)));
            if (rng.NextBool()) { int t0 = rx; rx = rz; rz = t0; }
            int d = rng.NextInt(1, 21), lowDir = it & 15;
            var s = new FluidPockets.Site { cx = rng.NextInt(200, 80000), cz = rng.NextInt(200, 80000), rx = rx, rz = rz, depth = d, level = 4000,
                                            lowDir = lowDir, outline = true, fluid = GenMat.Water, up = rng.NextInt(0, 40), dn = rng.NextInt(0, 20) };
            var l = FluidPockets.LobesOf(in s);
            for (int i = 0; i < l.n; i++)
                Assert.IsTrue(math.abs(l.ox[i]) + l.a[i] <= rx && math.abs(l.oz[i]) + l.b[i] <= rz && l.a[i] >= 2 && l.b[i] >= 2, $"lobe {i} outside the box, site {it}");
            Assert.IsTrue(FluidPockets.InSite(in s, 0, 0), $"centre not in the pond, site {it}");
            int W = 2 * rx + 3, H = 2 * rz + 3, area = 0, ell = 0, xor = 0;
            var foot = new bool[W * H]; var dep = new int[W * H];
            for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int dx = i - rx - 1, dz = j - rz - 1, k = j * W + i;
                foot[k] = FluidPockets.InSite(in s, dx, dz);
                bool e = FluidPockets.InFoot(dx, dz, rx, rz);
                if (foot[k]) { area++; Assert.IsTrue(FluidPockets.InFoot(dx, dz, rx + 1, rz + 1), $"pond outside the ellipse + 1 at ({dx}, {dz}), site {it}");
                               dep[k] = FluidPockets.SiteDepth(in s, dx, dz); Assert.IsTrue(dep[k] >= 1 && dep[k] <= d, "depth range"); }
                if (e) ell++;
                if (foot[k] != e) xor++;
            }
            // one 4-connected pond
            var seen = new bool[W * H]; var q = new Queue<int>(); int c0 = (rz + 1) * W + rx + 1, reached = 0;
            seen[c0] = true; q.Enqueue(c0);
            while (q.Count > 0)
            {
                int k = q.Dequeue(); reached++;
                int i = k % W, j = k / W;
                foreach (var (ni, nj) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
                {
                    if (ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                    int nk = nj * W + ni;
                    if (!foot[nk] || seen[nk]) continue;
                    seen[nk] = true; q.Enqueue(nk);
                    int st = math.abs(dep[nk] - dep[k]); maxStep = math.max(maxStep, st); pairs++; if (st > 1) steep++;
                }
            }
            Assert.AreEqual(area, reached, $"the pond is not one piece, site {it}");
            // rings nest and stay in the box grown by t
            int band = FluidPockets.BankBand(s.up, s.dn);
            for (int dz = -rz - band; dz <= rz + band; dz += 2)
            for (int dx = -rx - band; dx <= rx + band; dx += 2)
            {
                if (FluidPockets.InLobes(in l, dx, dz, 0)) continue;
                int t = FluidPockets.LobeRing(in l, dx, dz, band);
                if (t < band)
                {
                    Assert.IsTrue(FluidPockets.InLobes(in l, dx, dz, t) && (t == 1 || !FluidPockets.InLobes(in l, dx, dz, t - 1)), $"ring ({dx}, {dz}) site {it}");
                    Assert.IsTrue(math.abs(dx) <= rx + t && math.abs(dz) <= rz + t, $"ring {t} outside the box at ({dx}, {dz}), site {it}");
                }
                else if (band > 1) Assert.IsFalse(FluidPockets.InLobes(in l, dx, dz, band - 1));
            }
            // reaches farther toward the low direction than away from it (normalized)
            float Reach(int k)
            {
                float best = 0f;
                for (float r = 0f; r <= 1.05f; r += 0.01f)
                    if (FluidPockets.InSite(in s, (int)math.round(r * rx * FluidPockets.CosQ8(k) / 256f), (int)math.round(r * rz * FluidPockets.SinQ8(k) / 256f))) best = r;
                return best;
            }
            if (Reach(lowDir) > Reach(lowDir + 8)) towardLow++;
            sites++; areaSum += (double)area / ell; xorSum += (double)xor / ell;
            Assert.Greater(xor, ell / 20, $"site {it}: the outline is (almost) the ellipse");
            // the outline bit off is the ellipse exactly
            var off = s; off.outline = false;
            for (int dz = -rz - 1; dz <= rz + 1; dz += 3)
            for (int dx = -rx - 1; dx <= rx + 1; dx += 3)
            {
                Assert.AreEqual(FluidPockets.InFoot(dx, dz, rx, rz), FluidPockets.InSite(in off, dx, dz));
                if (FluidPockets.InFoot(dx, dz, rx, rz)) Assert.AreEqual(FluidPockets.DepthAt(dx, dz, rx, rz, d), FluidPockets.SiteDepth(in off, dx, dz));
            }
            // Pack / Decode
            var w = new int[4]; var plane = new int[4];
            FluidPockets.Pack(in s, 9 << 24, w);
            var back = FluidPockets.Decode(w, 0);
            Assert.IsTrue(back.lowDir == lowDir && back.outline && back.rx == rx && back.rz == rz && back.depth == d && back.cx == s.cx && back.up == s.up && ((w[0] >> 24) & 15) == 9, "pack / decode");
            if (it < 6)
            {
                sb.AppendLine($"site {it}: r {rx} x {rz}, low dir {lowDir} ({lowDir * 22.5f:F1} deg from +x toward +z), lobes {l.n}, area {100.0 * area / ell:F0} % of the ellipse; '~' fluid (digit = depth / 2), '.' the old ellipse only, '#' both");
                for (int j = H - 1; j >= 0; j -= 2)
                {
                    var line = new StringBuilder();
                    for (int i = 0; i < W; i++)
                    {
                        int k = j * W + i; bool e = FluidPockets.InFoot(i - rx - 1, j - rz - 1, rx, rz);
                        line.Append(foot[k] ? (char)('0' + math.min(9, dep[k] / 2)) : e ? '.' : ' ');
                    }
                    sb.AppendLine(line.ToString());
                }
            }
        }
        sb.Insert(0, $"Task 203 outline rules: {sites} sites, pond area {100 * areaSum / sites:F0} % of the ellipse (mean), differs from the ellipse on {100 * xorSum / sites:F0} % of its area, " +
                     $"reaches farther toward the low direction on {towardLow} / {sites}, max bed step {maxStep} voxels, steps > 1: {steep} of {pairs} neighbour pairs\n");
        string dir = Path.Combine("Logs", "TerrainGen", "outline203"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "shapes.txt"), sb.ToString());
        Assert.Greater(towardLow, sites * 9 / 10, "the pond does not reach toward the low ground");
        Assert.LessOrEqual(maxStep, 2, $"bed steeper than 2 voxels per column: {steep} of {pairs} pairs, max {maxStep}");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    private static byte Read(Chunk c, BrickDataPool pool, int3 local)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((local.z >> 3) << 8) | ((local.y >> 3) << 4) | (local.x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        return pool.RawData[(int)(d & 0x3FFFFFFFu) * 512 + (((local.z & 7) << 6) | ((local.y & 7) << 3) | (local.x & 7))];
    }

    private static byte[] Gen(TerrainGenChunkSource src, int3 coord)
    {
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
        try
        {
            var chunk = new Chunk();
            src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
            var v = new byte[128 * 128 * 128];
            if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return v; }
            for (int y = 0; y < 128; y++)
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
                v[TerrainGenChunkSource.Index(x, y, z)] = Read(chunk, pool, new int3(x, y, z));
            return v;
        }
        finally { pool.Dispose(); }
    }

    /// Chunks generated on demand, one state of the source.
    private sealed class Region
    {
        private readonly TerrainGenChunkSource _src;
        private readonly bool _on;
        private readonly Dictionary<long, byte[]> _c = new Dictionary<long, byte[]>();
        public Region(TerrainGenChunkSource src, bool on) { _src = src; _on = on; }
        private static long Key(int3 c) => ((long)(c.x & 0xFFFFF) << 40) | ((long)(c.y & 0xFFFFF) << 20) | (long)(c.z & 0xFFFFF);
        public byte[] Chunk(int3 cc)
        {
            if (!_c.TryGetValue(Key(cc), out var a))
            {
                bool was = _src.Spec.Pockets;
                if (was != _on) _src.SetFluidPockets(_on);
                a = Gen(_src, cc); _c[Key(cc)] = a;
                if (was != _on) _src.SetFluidPockets(was);
            }
            return a;
        }
        public byte At(int x, int y, int z) => Chunk(new int3(x >> 7, y >> 7, z >> 7))[TerrainGenChunkSource.Index(x & 127, y & 127, z & 127)];
        public IEnumerable<KeyValuePair<long, byte[]>> All => _c;
    }

    private static bool Solid(byte m) => m != Materials.Air && !MaterialRules.IsFluidMaterial(m);

    /// The settle check: the box [org, org + size) copied into the CPU fluid oracle
    /// (every fluid voxel awake), `ticks` ticks. Returns "" when nothing moved or
    /// reacted and every fluid's count is unchanged, else what happened.
    private static string Settle(Func<int, int, int, byte> at, int3 org, int bx, int by, int bz, int ticks, out int fluidCells)
    {
        var sim = new FluidReferenceCPU(bx, by, bz);
        int sx = bx * 8, sy = by * 8, sz = bz * 8;
        for (int z = 0; z < sz; z++)
        for (int y = 0; y < sy; y++)
        for (int x = 0; x < sx; x++)
        {
            byte m = at(org.x + x, org.y + y, org.z + z);
            if (m != Materials.Air) sim.EditVoxel(x, y, z, m);
        }
        sim.ResetLedgerToCurrentState();
        var before = new Dictionary<byte, int>();
        foreach (byte f in PocketFluids) { byte fe = VoxelEngine.Rendering.FarField.GenToEngine(f); before[fe] = sim.CountMaterial(fe); }
        fluidCells = sim.CountMobileBytes();
        long changed = 0, reactions = 0; int firstTick = -1;
        for (int t = 0; t < ticks; t++)
        {
            sim.Tick();
            if (sim.ChangedCellsThisTick != 0 && firstTick < 0) firstTick = t;
            changed += sim.ChangedCellsThisTick; reactions += sim.ReactionsThisTick;
        }
        var sb = new StringBuilder();
        if (changed != 0 || reactions != 0) sb.Append($"changed {changed} cells, {reactions} reactions (first at tick {firstTick}); ");
        foreach (var kv in before)
        {
            int now = sim.CountMaterial(kv.Key);
            if (now != kv.Value) sb.Append($"material {kv.Key}: {kv.Value} -> {now}; ");
        }
        return sb.ToString();
    }

    private static int3 ChunkLo(int3 p) => p >> 7;

    // ------------------------------------------------------------------
    // Four seeds: plane determinism, keep-outs, the table
    // ------------------------------------------------------------------
    [Test, Category("Slow"), Timeout(3600000)]
    public void FourSeeds_Plane_Table_KeepOuts()
    {
        Directory.CreateDirectory(LogDir);
        var log = new StringBuilder();
        log.AppendLine("Task 183 -- fluid pockets, four seeds: plane determinism, keep-outs, sealed shape, counts (ON = -fluidpockets 1)");
        int failures = 0;
        var allSurface = new Dictionary<string, int>();
        foreach (uint seed in Seeds)
        {
            var sw = Stopwatch.StartNew();
            using var src = new TerrainGenChunkSource(seed);
            double mapMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            src.SetFluidPockets(true);
            double planeMs = sw.Elapsed.TotalMilliseconds;
            var map = src.Map; var spec = src.Spec; var o = src.O;
            int[] plane = map.PocketPlane;
            Assert.IsNotNull(plane);
            int[] again = FluidPockets.BuildPlane(map, out var st);
            FastAssert.ArrayCollectionEqual(plane, again, $"seed {seed}: the plane is not deterministic");   // 184b: same check, no boxing
            var view = map.View;                       // with the plane
            var baseView = view; baseView.pocket = null;   // the base terrain
            var zone = FluidPockets.StartZone(map);
            int n = FluidPockets.PlaneCells(in spec), mc = spec.mapCells, cv = spec.mapCellVoxels;
            log.AppendLine($"seed {seed}: map {mapMs / 1000:F1} s, plane {planeMs:F0} ms: {st.sites} surface sites of {st.candidates} cells " +
                           $"({st.ruled} matched a rule; rejected: cells {st.rejectedCells}, samples {st.rejectedSamples}, flat / sea {st.rejectedFlat}); " +
                           $"task 196: banked {st.banked}, capped {st.bankCapped}, max cut {st.maxUp} / fill {st.maxDn} voxels, bowls made shallower {st.depthCut}");

            var byBiome = new Dictionary<string, int>(); var byArch = new Dictionary<string, int>();
            int sites = 0, checkedRim = 0, checkedFoot = 0, berms = 0, maxBerm = 0, rimHigh = 0, maxRimWall = 0, cappedSites = 0;
            string firstFail = null; int fails = 0;
            for (int i = 0; i < n * n; i++)
            {
                if (plane[i * FluidPockets.STRIDE + 3] == 0) continue;
                var s = FluidPockets.Decode(plane, i);
                sites++;
                var rc = map.Record.AtCell(s.cx / cv, s.cz / cv);
                string kb = $"{FluidName(s.fluid),-9} {BiomeName[rc.Biome]}";
                string ka = $"{FluidName(s.fluid),-9} {(rc.Archetype < ArchName.Length ? ArchName[rc.Archetype] : "?")}";
                byBiome[kb] = byBiome.TryGetValue(kb, out int v0) ? v0 + 1 : 1;
                byArch[ka] = byArch.TryGetValue(ka, out int v1) ? v1 + 1 : 1;
                allSurface[FluidName(s.fluid)] = allSurface.TryGetValue(FluidName(s.fluid), out int v2) ? v2 + 1 : 1;
                bool siteCapped = false; int siteRimHigh = 0;
                // every rim column (exhaustive) and the footprint on a 4-voxel grid
                for (int dz = -s.rz - 1; dz <= s.rz + 1; dz++)
                for (int dx = -s.rx - 1; dx <= s.rx + 1; dx++)
                {
                    bool foot = FluidPockets.InSite(in s, dx, dz);
                    bool rim = false;
                    if (!foot)
                        for (int oz = -1; oz <= 1; oz++) for (int ox = -1; ox <= 1; ox++)
                            if ((ox | oz) != 0 && FluidPockets.InSite(in s, dx + ox, dz + oz)) rim = true;
                    if (!foot && !rim) continue;
                    if (foot && ((dx & 3) != 0 || (dz & 3) != 0) && !(dx == 0 && dz == 0)) continue;
                    int x = s.cx + dx, z = s.cz + dz;
                    var b = ShapeField.Sample(in spec, in baseView, in o, x + 0.5f, z + 0.5f, 1, false);
                    var c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1, false);
                    var q = map.Record.AtCell(x / cv, z / cv);
                    string why = null;
                    if (b.water != ColumnSample.NO_WATER_Y) why = $"base water {b.water}";
                    else if (b.lipLo != 0 || b.stampMat != 0 || b.stamp3D || b.carvesTop || b.open1Hi > b.open1Lo || b.open2Hi > b.open2Lo) why = "a stamp / river lip";
                    else if (q.IsSea || q.IsRoute || q.IsFeature || zone[(z / cv) * mc + x / cv]) why = "sea / route / feature / start-zone cell";
                    else if (foot && !(c.water == s.level && c.height < s.level && c.height >= s.level - s.depth && FluidPockets.FluidOf(c.waterKind) == s.fluid)) why = $"footprint h {c.height} water {c.water}";
                    else if (rim && !(c.height >= s.level + 1 && c.water <= c.height)) why = $"rim h {c.height} water {c.water}";
                    else if (c.riverFloor == 0 || c.riverFloor > s.level - s.depth - FluidPockets.CAP_MARGIN) why = $"cave cap {c.riverFloor}";
                    if (rim)
                    {
                        checkedRim++; if (c.height > b.height) { berms++; maxBerm = math.max(maxBerm, c.height - b.height); }
                        // task 196: flush at L + 1 unless the site's excess had to be capped
                        if (b.height - (s.level + 1) > s.up || (s.level + 1) - b.height > s.dn) siteCapped = true;
                        if (c.height > s.level + 1) { siteRimHigh++; maxRimWall = math.max(maxRimWall, c.height - s.level); }
                    }
                    else checkedFoot++;
                    if (why != null) { fails++; if (firstFail == null) firstFail = $"site {FluidName(s.fluid)} at ({s.cx}, {s.cz}) column ({x}, {z}): {why}"; }
                }
                rimHigh += siteRimHigh; if (siteCapped) cappedSites++;
                if (siteRimHigh > 0 && !siteCapped) { fails++; if (firstFail == null) firstFail = $"site {FluidName(s.fluid)} at ({s.cx}, {s.cz}): {siteRimHigh} rim columns over L + 1, not capped"; }
            }
            failures += fails;
            log.AppendLine($"  {sites} sites; rim columns checked {checkedRim:N0} (berm raised {berms:N0}, max {maxBerm} voxels), footprint columns {checkedFoot:N0}; violations {fails}{(firstFail != null ? " e.g. " + firstFail : "")}");
            log.AppendLine($"  task 196: rim columns over L + 1: {rimHigh:N0} (in {cappedSites} capped sites; highest such rim {maxRimWall} voxels over the fluid; 0 = every rim flush at L + 1)");
            log.AppendLine("  surface sites, fluid x biome:");
            foreach (var kv in Sorted(byBiome)) log.AppendLine($"    {kv.Key,-24} {kv.Value,5}");
            log.AppendLine("  surface sites, fluid x landform:");
            foreach (var kv in Sorted(byArch)) log.AppendLine($"    {kv.Key,-24} {kv.Value,5}");

            // deep pockets: rule fluid (ON) vs the old fluid (OFF), per layer
            var deep = new Dictionary<string, int[]>();
            int cyMax = (spec.seaLevelY >> DetailField.POCKET_CELL_SHIFT) + 1;
            for (int cz = 0; cz < n; cz++)
            for (int cx = 0; cx < n; cx++)
            {
                byte ctx = (byte)((plane[(cz * n + cx) * FluidPockets.STRIDE] >> 24) & 15);
                for (int cy = 0; cy <= cyMax; cy++)
                {
                    if (!DetailField.TryGetPocket(in spec, in o, cx, cy, cz, out var pk)) continue;
                    int3 v = (int3)math.floor(pk.centre - new float3(0, pk.radii.y * 0.5f, 0));   // under the level
                    byte off = DetailField.Pocket(in spec, in o, v.x, v.y, v.z, out _);
                    byte on = ctx != 0 ? FluidPockets.DeepPocket(in spec, in o, ctx, v.x, v.y, v.z, out _) : off;
                    string layer = pk.centre.y < spec.CavernTopY
                        ? ((spec.CavernTopY - pk.centre.y) / (float)(spec.CavernTopY - spec.UnderworldTopY) > 0.55f ? "cavern (deep half)" : "cavern")
                        : "stone / deep stone";
                    foreach (var (f, k) in new[] { (on, 0), (off, 1) })
                    {
                        string key = $"{FluidName(f),-9} {layer}";
                        if (!deep.TryGetValue(key, out var a)) deep[key] = a = new int[2];
                        a[k]++;
                    }
                }
            }
            log.AppendLine("  deep pockets (sealed, >= 100 m below sea), fluid x layer: ON / OFF");
            foreach (var kv in Sorted(deep)) log.AppendLine($"    {kv.Key,-30} {kv.Value[0],5} / {kv.Value[1],5}");
        }
        log.AppendLine("all seeds, surface sites per fluid: " + string.Join(", ", Sorted(allSurface).ConvertAll(kv => $"{kv.Key} {kv.Value}")));
        log.AppendLine("steam: none generated (only where lava meets water, and there the reaction makes it).");
        File.WriteAllText(Path.Combine(LogDir, "table.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
        Assert.AreEqual(0, failures, "keep-out / sealed-shape violations: see " + Path.Combine(LogDir, "table.txt"));
        Assert.IsTrue(allSurface.ContainsKey("water") && allSurface.ContainsKey("lava") && allSurface.ContainsKey("oil"), "a required surface fluid never appears");
    }

    private static List<KeyValuePair<string, T>> Sorted<T>(Dictionary<string, T> d)
    {
        var l = new List<KeyValuePair<string, T>>(d);
        l.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return l;
    }

    // ------------------------------------------------------------------
    // Seed 42: at rest, the paths agree, the adversarial forced sites, images
    // ------------------------------------------------------------------
    private struct Case
    {
        public string name; public int planeIndex; public bool forced, deep;
        public int3 lo, hi;   // the box the pocket and its walls live in (inclusive)
        public FluidPockets.Site site;
        public DetailField.PocketInfo pk; public byte deepFluid;
        public int3 org; public int bx, by, bz;   // the settle box
    }

    [Test, Category("Slow"), Timeout(3600000)]
    public void Sites_AtRest_PathsAgree_Adversarial()
    {
        Directory.CreateDirectory(LogDir); Directory.CreateDirectory(ShotDir);
        var log = new StringBuilder();
        log.AppendLine("Task 183 sites, seed 42 (ON = -fluidpockets 1)");
        using var src = new TerrainGenChunkSource(42u);
        src.SetFluidPockets(true);
        var map = src.Map; var spec = src.Spec; var o = src.O;
        int n = FluidPockets.PlaneCells(in spec);
        int[] plane = map.PocketPlane;
        var cases = new List<Case>();

        // the largest surface site of each fluid (water: a valley one when there is one)
        foreach (byte f in PocketFluids)
        {
            int best = -1, bestR = -1;
            for (int i = 0; i < n * n; i++)
            {
                if (plane[i * FluidPockets.STRIDE + 3] == 0) continue;
                var s = FluidPockets.Decode(plane, i);
                if (s.fluid != f) continue;
                int r = s.rx + s.rz;
                if (r > bestR) { bestR = r; best = i; }
            }
            if (best < 0) { log.AppendLine($"  no surface {FluidName(f)} site on seed 42"); continue; }
            var st = FluidPockets.Decode(plane, best);
            cases.Add(SurfaceCase($"surface_{FluidName(f)}", best, st, false));
        }

        // the smallest deep lava and acid pockets that fit a 128 x 64 x 128 settle box
        foreach (byte f in new[] { GenMat.Lava, GenMat.Acid })
        {
            Case bestC = default; float bestV = float.MaxValue;
            int cyMax = (spec.seaLevelY >> DetailField.POCKET_CELL_SHIFT) + 1;
            for (int cz = 0; cz < n; cz++)
            for (int cx = 0; cx < n; cx++)
            {
                byte ctx = (byte)((plane[(cz * n + cx) * FluidPockets.STRIDE] >> 24) & 15);
                if (ctx == 0) continue;
                for (int cy = 0; cy <= cyMax; cy++)
                {
                    if (!DetailField.TryGetPocket(in spec, in o, cx, cy, cz, out var pk)) continue;
                    if (FluidPockets.DeepFluid(in spec, in o, ctx, cx, cy, cz, pk.centre) != f) continue;
                    if (f == GenMat.Acid)   // the control: a pocket the OLD rule made acid too (unlined then)
                    {
                        int3 v = (int3)math.floor(pk.centre - new float3(0, pk.radii.y * 0.5f, 0));
                        if (DetailField.Pocket(in spec, in o, v.x, v.y, v.z, out _) != GenMat.Acid) continue;
                    }
                    if (pk.radii.x > 60f || pk.radii.z > 60f || pk.level - (pk.centre.y - pk.radii.y) > 56f) continue;
                    float vol = pk.radii.x * pk.radii.y * pk.radii.z;
                    if (vol >= bestV) continue;
                    bestV = vol;
                    int3 c0 = (int3)math.floor(pk.centre);
                    int fy0 = (int)math.floor(pk.centre.y - pk.radii.y) - 3;
                    bestC = new Case
                    {
                        name = $"deep_{FluidName(f)}", deep = true, pk = pk, deepFluid = f,
                        lo = new int3(c0.x - (int)pk.radii.x - 4, fy0, c0.z - (int)pk.radii.z - 4),
                        hi = new int3(c0.x + (int)pk.radii.x + 4, (int)math.ceil(pk.level) + 6, c0.z + (int)pk.radii.z + 4),
                        org = new int3(c0.x - 64, fy0, c0.z - 64), bx = 16, by = 8, bz = 16,
                    };
                }
            }
            if (bestV < float.MaxValue) cases.Add(bestC); else log.AppendLine($"  no deep {FluidName(f)} pocket fits the settle box");
        }

        // ADVERSARIAL: a forced site on the tallest cliff edge and in the deepest canyon floor
        // Task 196: + a FORCED site on a ~20 deg slope, its level the planner's (1 under the lowest rim
        // column): the uphill bank is cut, the downhill rim must still hold.
        FindAdversarial(map, out int2 cliff, out int2 canyon, out int2 slope);
        var baseView = map.View; baseView.pocket = null;
        foreach (var (name, col, fluid) in new[] { ("forced_cliff_water", cliff, GenMat.Water), ("forced_canyon_lava", canyon, GenMat.Lava), ("forced_slope_lava", slope, GenMat.Lava) })
        {
            int pi = (col.y >> FluidPockets.CELL_SHIFT) * n + (col.x >> FluidPockets.CELL_SHIFT);
            int lx = col.x & (FluidPockets.CELL - 1), lz = col.y & (FluidPockets.CELL - 1);
            int m = name == "forced_slope_lava" ? FluidPockets.SITE_MARGIN : 64;   // the slope site: a planner's margin, room for its bank
            int cx = (col.x & ~(FluidPockets.CELL - 1)) + math.clamp(lx, m, FluidPockets.CELL - m);
            int cz = (col.y & ~(FluidPockets.CELL - 1)) + math.clamp(lz, m, FluidPockets.CELL - m);
            int L = ShapeField.Sample(in spec, in baseView, in o, cx + 0.5f, cz + 0.5f, 1, false).height;   // the centre's own ground: the cliff side drops away
            const int rx = 40, rz = 28;
            if (name == "forced_slope_lava")
            {
                FluidPockets.MeasureBank(in spec, in baseView, in o, cx, cz, L, rx, rz, 255, out _, out int dn0, out _);
                L = L + 1 - dn0 - 1;   // the lowest rim column - 1
            }
            int room = math.min(math.min(cx & (FluidPockets.CELL - 1), FluidPockets.CELL - 1 - (cx & (FluidPockets.CELL - 1))) - rx,
                                math.min(cz & (FluidPockets.CELL - 1), FluidPockets.CELL - 1 - (cz & (FluidPockets.CELL - 1))) - rz);
            // task 203: the forced site takes the default outline (lobes toward its lowest rim direction)
            var s = new FluidPockets.Site { cx = cx, cz = cz, level = L, rx = rx, rz = rz, depth = FluidPockets.BowlDepth(16, rx, rz), fluid = fluid,
                                            outline = FluidPockets.OutlineArg(), lowDir = FluidPockets.RimLowDir(in spec, in baseView, in o, cx, cz, rx, rz) };
            FluidPockets.MeasureBank(in spec, in baseView, in o, in s, room, out s.up, out s.dn, out _);
            cases.Add(SurfaceCase(name, pi, s, true));
        }

        int failures = 0;
        var poses = new List<string>();
        foreach (var cs in cases)
        {
            int[] saved = null;
            if (cs.forced)
            {
                int b = cs.planeIndex * FluidPockets.STRIDE;
                var pc = src.PocketPlaneCopy;
                saved = new[] { pc[b], pc[b + 1], pc[b + 2], pc[b + 3] };
                var s = cs.site;
                int[] w = new int[4];
                FluidPockets.Pack(in s, saved[0], w);
                src.OverridePocketSite(cs.planeIndex, w[0], w[1], w[2], w[3]);
                for (int k = 0; k < 4; k++) plane[b + k] = w[k];
            }
            try { failures += RunCase(src, cs, log, poses); }
            finally
            {
                if (saved != null)
                {
                    src.OverridePocketSite(cs.planeIndex, saved[0], saved[1], saved[2], saved[3]);
                    for (int k = 0; k < 4; k++) plane[cs.planeIndex * FluidPockets.STRIDE + k] = saved[k];
                }
            }
        }
        File.WriteAllText(Path.Combine(LogDir, "sites.txt"), log.ToString());
        File.WriteAllText(Path.Combine(LogDir, "poses.txt"), string.Join(";", poses));
        UnityEngine.Debug.Log(log.ToString());
        Assert.AreEqual(0, failures, "see " + Path.Combine(LogDir, "sites.txt"));
        Assert.GreaterOrEqual(cases.Count, 6, "too few cases");
    }

    private static Case SurfaceCase(string name, int planeIndex, FluidPockets.Site s, bool forced)
    {
        int yLo = s.level - s.depth - 3 - s.dn, e = math.max(1, FluidPockets.BankBand(s.up, s.dn)) + 2;   // task 196: + the bank
        return new Case
        {
            name = name, planeIndex = planeIndex, forced = forced, site = s,
            lo = new int3(s.cx - s.rx - e, yLo, s.cz - s.rz - e),
            hi = new int3(s.cx + s.rx + e, s.level + 4 + s.up, s.cz + s.rz + e),
            org = new int3(s.cx - 64, s.level - s.depth - 3, s.cz - 64), bx = 16, by = 4, bz = 16,
        };
    }

    /// The tallest cliff (largest height step between neighbouring map cells, land)
    /// and the deepest canyon (land cell farthest below the mean of the cells 6 out).
    private static void FindAdversarial(WorldMap m, out int2 cliff, out int2 canyon, out int2 slope)
    {
        int n = m.Spec.mapCells, cv = m.Spec.mapCellVoxels;
        float bestStep = -1f, bestDepth = -1f, bestSlope = float.MaxValue; cliff = canyon = slope = default;
        float want = math.tan(math.radians(20f));   // task 196: a ~20 deg slope (between the walk and steep rules)
        for (int z = 8; z < n - 8; z++)
        for (int x = 8; x < n - 8; x++)
        {
            float h = m.Height[z * n + x];
            if (h < 50f) continue;
            bool wet = false;
            for (int dz = -3; dz <= 3 && !wet; dz++) for (int dx = -3; dx <= 3; dx++) if (m.Water[(z + dz) * n + x + dx] > Hydrology.NO_WATER * 0.5f) { wet = true; break; }
            if (wet) continue;
            float st = math.max(math.abs(m.Height[z * n + x + 1] - h), math.abs(m.Height[(z + 1) * n + x] - h));
            if (st > bestStep) { bestStep = st; cliff = new int2(x * cv + cv / 2, z * cv + cv / 2); }
            float mean = (m.Height[z * n + x + 6] + m.Height[z * n + x - 6] + m.Height[(z + 6) * n + x] + m.Height[(z - 6) * n + x]) * 0.25f;
            if (mean - h > bestDepth) { bestDepth = mean - h; canyon = new int2(x * cv + cv / 2, z * cv + cv / 2); }
            // the same gradient over 3 cells each way (a slope, not a step), closest to 20 deg
            float gx = (m.Height[z * n + x + 3] - m.Height[z * n + x - 3]) / (6f * cv), gz = (m.Height[(z + 3) * n + x] - m.Height[(z - 3) * n + x]) / (6f * cv);
            float gx1 = (m.Height[z * n + x + 1] - m.Height[z * n + x - 1]) / (2f * cv), gz1 = (m.Height[(z + 1) * n + x] - m.Height[(z - 1) * n + x]) / (2f * cv);
            float g = math.sqrt(gx * gx + gz * gz), g1 = math.sqrt(gx1 * gx1 + gz1 * gz1);
            float miss = math.abs(g - want) + math.abs(g1 - want);
            if (miss < bestSlope) { bestSlope = miss; slope = new int2(x * cv + cv / 2, z * cv + cv / 2); }
        }
    }

    private static int RunCase(TerrainGenChunkSource src, Case cs, StringBuilder log, List<string> poses)
    {
        var spec = src.Spec; var o = src.O;
        int fails = 0; var sb = new StringBuilder(); var sb2 = new StringBuilder();
        void Fail(string m) { fails++; if (sb.Length < 600) sb.Append(m).Append("; "); }

        // ON twice, ON with the brick skip off, OFF -- over the box's chunks
        src.SetFluidPockets(true);
        var on = new Region(src, true);
        int3 c0 = ChunkLo(math.min(cs.lo, cs.org)), c1 = ChunkLo(math.max(cs.hi, cs.org + new int3(cs.bx, cs.by, cs.bz) * 8 - 1));
        var coords = new List<int3>();
        for (int cy = c0.y; cy <= c1.y; cy++) for (int cz = c0.z; cz <= c1.z; cz++) for (int cx = c0.x; cx <= c1.x; cx++) coords.Add(new int3(cx, cy, cz));
        var sw = Stopwatch.StartNew();
        foreach (var cc in coords) on.Chunk(cc);
        double msOn = sw.Elapsed.TotalMilliseconds;
        var off = new Region(src, false);
        int changed = 0, outside = 0, fluidOn = 0, treeMoved = 0;
        foreach (var cc in coords)
        {
            byte[] a = on.Chunk(cc), a2 = Gen(src, cc);
            src.BrickSkip = false; byte[] a3 = Gen(src, cc); src.BrickSkip = true;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != a2[i]) { Fail($"chunk {cc} voxel {i}: ON not deterministic"); break; }
                if (a[i] != a3[i]) { Fail($"chunk {cc} voxel {i}: brick skip on {a[i]} != off {a3[i]}"); break; }
            }
        }
        sw.Restart();
        foreach (var cc in coords) off.Chunk(cc);
        double msOff = sw.Elapsed.TotalMilliseconds;
        foreach (var cc in coords)
        {
            byte[] a = on.Chunk(cc), b = off.Chunk(cc);
            int3 org = cc << 7;
            for (int i = 0; i < a.Length; i++)
            {
                if (MaterialRules.IsFluidMaterial(a[i]) && a[i] != Materials.Water) fluidOn++;
                if (a[i] == b[i]) continue;
                changed++;
                int x = org.x + (i & 127), z = org.z + ((i >> 7) & 127), y = org.y + (i >> 14);
                bool inside = cs.deep
                    ? (x >> DetailField.POCKET_CELL_SHIFT) == ((int)cs.pk.centre.x >> DetailField.POCKET_CELL_SHIFT) && (y >> DetailField.POCKET_CELL_SHIFT) == ((int)cs.pk.centre.y >> DetailField.POCKET_CELL_SHIFT) && (z >> DetailField.POCKET_CELL_SHIFT) == ((int)cs.pk.centre.z >> DetailField.POCKET_CELL_SHIFT)
                    : math.abs(x - cs.site.cx) <= cs.site.rx + Ext(cs.site) && math.abs(z - cs.site.cz) <= cs.site.rz + Ext(cs.site);
                if (!inside) outside++;
            }
        }
        // a surface site OFF->ON may also retint deep pockets elsewhere in these chunks: allowed only for deep cases
        if (outside > 0 && !cs.deep)
        {
            // count only changes outside every deep pocket's shell. Task 196: and not a tree's
            // voxel -- a tree on the bank stands on the lowered ground (Trees roots on the
            // column's final height), so its crown moves with it past the band.
            var treeMat = new bool[256];
            for (int g = 0; g < 256; g++)
                if (GenMat.IsTreeTrunk((byte)g) || GenMat.IsTreeLeaf((byte)g)) treeMat[VoxelEngine.Rendering.FarField.GenToEngine((byte)g)] = true;
            int realOutside = 0;
            foreach (var cc in coords)
            {
                byte[] a = on.Chunk(cc), b = off.Chunk(cc); int3 org = cc << 7;
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] == b[i]) continue;
                    int x = org.x + (i & 127), z = org.z + ((i >> 7) & 127), y = org.y + (i >> 14);
                    if (math.abs(x - cs.site.cx) <= cs.site.rx + Ext(cs.site) && math.abs(z - cs.site.cz) <= cs.site.rz + Ext(cs.site)) continue;
                    if (DetailField.Pocket(in spec, in o, x, y, z, out bool inShell) != 255 || inShell) continue;
                    if (treeMat[a[i]] || treeMat[b[i]]) { treeMoved++; continue; }
                    realOutside++;
                }
            }
            outside = realOutside;
        }
        if (outside > 0) Fail($"{outside} voxels changed outside the pocket's columns");
        if (treeMoved > 0) sb2.Append($"tree voxels moved past the band (a tree on the bank) {treeMoved:N0}; ");

        // the settle check (CPU fluid oracle)
        sw.Restart();
        string settle = Settle((x, y, z) => on.At(x, y, z), cs.org, cs.bx, cs.by, cs.bz, 150, out int fluidCells);
        double msSettle = sw.Elapsed.TotalMilliseconds;
        if (settle.Length > 0) Fail("NOT at rest: " + settle);
        if (fluidCells == 0) Fail("no fluid in the settle box");
        // NEGATIVE CONTROLS: the check must see a breach. Surface: a hole cut in the
        // rim (5 voxels at the +x edge, L - 3 .. L + 1). Deep: the OFF bytes (the old,
        // unlined pocket in the same place).
        string control;
        if (!cs.deep)
        {
            int hx = cs.site.cx, hz = cs.site.cz, L0 = cs.site.level;   // task 203: walk out from the centre to the first rim column
            while (FluidPockets.InSite(in cs.site, hx - cs.site.cx, 0)) hx++;
            string holed = Settle((x, y, z) => x == hx && z == hz && y >= L0 - 3 && y <= L0 + 1 ? Materials.Air : on.At(x, y, z),
                                  cs.org, cs.bx, cs.by, cs.bz, 150, out _);
            control = "rim hole -> " + (holed.Length > 0 ? holed : "NOTHING (the check is blind)");
            if (holed.Length == 0) Fail("negative control: a hole in the rim went unnoticed");
        }
        else
        {
            string old = Settle((x, y, z) => off.At(x, y, z), cs.org, cs.bx, cs.by, cs.bz, 150, out int offCells);
            byte offFluid = off.At((int)cs.pk.centre.x, (int)(cs.pk.centre.y - cs.pk.radii.y * 0.5f), (int)cs.pk.centre.z);
            control = $"OFF (old fluid {offFluid}, unlined, {offCells:N0} mobile cells) -> " + (old.Length > 0 ? old : "at rest");
        }

        // FarQuery = chunk (pocket voxels + random points in the box)
        var pts = new List<int3>(); var want = new List<byte>();
        var rng = new Unity.Mathematics.Random(183u + (uint)cs.name.Length);
        for (int k = 0; k < 4000; k++)
        {
            int3 p = rng.NextInt3(cs.lo, cs.hi + 1);
            pts.Add(p); want.Add(on.At(p.x, p.y, p.z));
        }
        using (var empty = new Empty())
        using (var fq = new FarQuery(empty.store, empty.store, src, EditOpLog.InMemory(42u), null, 42u))
        {
            var p = new NativeArray<int3>(pts.ToArray(), Allocator.TempJob);
            var res = new NativeArray<byte>(pts.Count, Allocator.TempJob);
            try
            {
                fq.GetVoxels(p, res);
                int bad = 0; for (int i = 0; i < pts.Count; i++) if (want[i] != res[i]) { if (bad++ == 0) Fail($"FarQuery {pts[i]} {res[i]} != chunk {want[i]}"); }
            }
            finally { p.Dispose(); res.Dispose(); }
        }

        // shell 0 = Generate; shells 1-2 agree wherever either is pocket fluid or the voxel
        // is a LINING POSITION. Task 186: the lining is told by position, not by material --
        // the material rules (the default) paint the same materials beside a fluid (sludge's
        // shore is mud, quicksand's packed sand, a wet bank mud), and that paint is not the
        // pocket. The lining range is the column's own (FluidPockets.Apply works on the
        // integer column and its fine neighbours), so it is the same in shells and chunks.
        byte lining = cs.deep ? FluidPockets.DeepLining(cs.deepFluid) : FluidPockets.Lining(cs.site.fluid);
        byte fluidE = VoxelEngine.Rendering.FarField.GenToEngine(cs.deep ? cs.deepFluid : cs.site.fluid);
        var specL = src.Spec; var viewL = src.Map.View;
        var linCol = new Dictionary<long, TerrainGen.ColumnSample>();
        bool LiningAt(int3 v)
        {
            if (lining == 0) return false;
            long key = ((long)v.x << 32) ^ (uint)v.z;
            if (!linCol.TryGetValue(key, out var lc))
                linCol[key] = lc = ShapeField.Sample(in specL, in viewL, in o, v.x + 0.5f, v.z + 0.5f, 1, false);
            if (!cs.deep) return lc.pkLining != 0 && v.y >= lc.pkLineLo && v.y <= lc.pkLineHi;
            return lc.pkCtx != 0 && FluidPockets.DeepPocket(in specL, in o, lc.pkCtx, v.x, v.y, v.z, out _) == lining;
        }
        int shellCmp = 0;
        using (var sc = new TerrainGenChunkSource.ShellChunk())
        {
            int3 mid = ChunkLo((cs.lo + cs.hi) >> 1);
            src.GenerateShellChunk(0, mid, sc);
            byte[] a = on.Chunk(mid);
            for (int i = 0; i < a.Length; i++)
                if (sc.Get(i & 127, i >> 14, (i >> 7) & 127) != a[i]) { Fail($"shell 0 chunk {mid} voxel {i} != Generate"); break; }
            src.ShellFill = false;
            try
            {
                for (int k = 1; k <= 2; k++)
                {
                    var done = new HashSet<long>();
                    foreach (var cc in coords)
                    {
                        int3 sc3 = (cc << 7) >> (7 + k);
                        long key = ((long)sc3.x << 40) ^ ((long)sc3.y << 20) ^ sc3.z;
                        if (!done.Add(key)) continue;
                        src.GenerateShellChunk(k, sc3, sc);
                        int3 origin = sc3 << (7 + k);
                        int half = (1 << (k - 1)) - 1;
                        for (int z = 0; z < 128; z++)
                        for (int x = 0; x < 128; x++)
                        for (int y = 0; y < 128; y++)
                        {
                            int3 f = origin + new int3(x << k, y << k, z << k) + half;
                            if (math.any(f < cs.lo) || math.any(f > cs.hi)) continue;
                            int gh = sc.GroundY(x, z);
                            int3 fw = y == ((gh - origin.y - half) >> k) ? new int3(f.x, gh, f.z) : f;
                            byte want1 = on.At(fw.x, fw.y, fw.z);
                            byte got = sc.Get(x, y, z);
                            if (want1 != fluidE && got != fluidE && !LiningAt(fw)) continue;
                            shellCmp++;
                            if (want1 != got) { Fail($"shell {k} sample {f}: {got} != 0.1 m voxel {want1}"); goto nextK; }
                        }
                    }
                    nextK:;
                }
            }
            finally { src.ShellFill = true; }
        }

        // the far band's column: texels 8-64 whose sample lies in the footprint hold the same fluid at L
        int farCmp = 0;
        if (!cs.deep)
        {
            var view = src.Map.View;   // the map's plane (a forced site is written there too)
            {
                var s = cs.site;
                foreach (int t in new[] { 8, 16, 32, 64 })
                for (int dz = -s.rz; dz <= s.rz; dz += 3)
                for (int dx = -s.rx; dx <= s.rx; dx += 3)
                {
                    if (!FluidPockets.InSite(in s, dx, dz)) continue;
                    var c = ShapeField.Sample(in spec, in view, in o, s.cx + dx + 0.5f, s.cz + dz + 0.5f, t);
                    ShapeField.FarColumn(in spec, ref c);
                    farCmp++;
                    byte top = on.At(s.cx + dx, s.level, s.cz + dz), above = on.At(s.cx + dx, s.level + 1, s.cz + dz);
                    if (c.water != s.level || c.height >= s.level || FluidPockets.FluidOf(c.waterKind) != s.fluid || top != fluidE || (above != Materials.Air && !TreeVoxel(above)))   // task 203: a bank tree's crown may overhang the pond
                    { Fail($"far texel {t} at ({s.cx + dx}, {s.cz + dz}): water {c.water} h {c.height} kind {c.waterKind}; LOD 0 {top} / {above}"); goto farDone; }
                }
                farDone:;
            }
        }

        LogCase(log, cs, fails, sb, changed, fluidOn, shellCmp, farCmp, msOn, msOff, msSettle, coords.Count);
        log.AppendLine("    negative control: " + control);
        if (sb2.Length > 0) log.AppendLine("    " + sb2);
        if (!cs.deep) { int bf = NaturalBasin(src, cs, log); fails += bf; }
        CutAway(cs, on, off);
        if (!cs.deep) TopView(src, cs);   // task 203
        if (!cs.deep && !cs.forced) poses.Add(Pose(src, cs));
        return fails;
    }

    /// Task 203: an engine material that is a tree's trunk or leaves (trees overwrite air only, so a crown may hang over a pond).
    private static bool TreeVoxel(byte e)
    {
        for (int g = 0; g < 256; g++)
            if ((GenMat.IsTreeTrunk((byte)g) || GenMat.IsTreeLeaf((byte)g)) && VoxelEngine.Rendering.FarField.GenToEngine((byte)g) == e) return true;
        return false;
    }

    private static int Ext(FluidPockets.Site s) => math.max(1, FluidPockets.BankBand(s.up, s.dn));

    /// Task 196: the shape over the whole bank, ON vs OFF heights per column. The rim is
    /// flush (L + 1) unless the site's excess was capped; outside the footprint no
    /// 4-neighbour step grows by more than 2 voxels (the bank's slope + an integer ring
    /// change), except on a capped site's rim (the residual it must keep to hold the fluid).
    private static int NaturalBasin(TerrainGenChunkSource src, Case cs, StringBuilder log)
    {
        var s = cs.site; var spec = src.Spec; var o = src.O;
        var view = src.Map.View; var baseView = view; baseView.pocket = null;
        int e = Ext(s) + 1, W = 2 * (s.rx + e) + 1, H = 2 * (s.rz + e) + 1;
        var hOn = new int[W * H]; var hOff = new int[W * H]; var kind = new byte[W * H];   // 0 bank / outside, 1 rim, 2 footprint
        bool capped = false; int rimHigh = 0, maxWall = int.MinValue, maxCut = 0, maxFill = 0, moved = 0;
        for (int j = 0; j < H; j++)
        for (int i = 0; i < W; i++)
        {
            int dx = i - s.rx - e, dz = j - s.rz - e, k = j * W + i;
            float x = s.cx + dx + 0.5f, z = s.cz + dz + 0.5f;
            hOn[k] = ShapeField.Sample(in spec, in view, in o, x, z, 1, false).height;
            hOff[k] = ShapeField.Sample(in spec, in baseView, in o, x, z, 1, false).height;
            if (FluidPockets.InSite(in s, dx, dz)) { kind[k] = 2; continue; }
            for (int oz = -1; oz <= 1; oz++) for (int ox = -1; ox <= 1; ox++)
                if ((ox | oz) != 0 && FluidPockets.InSite(in s, dx + ox, dz + oz)) kind[k] = 1;
            if (kind[k] == 1)
            {
                if (hOff[k] - (s.level + 1) > s.up || (s.level + 1) - hOff[k] > s.dn) capped = true;
                if (hOn[k] > s.level + 1) rimHigh++;
                maxWall = math.max(maxWall, hOn[k] - s.level);
            }
            else if (hOn[k] != hOff[k]) { moved++; maxCut = math.max(maxCut, hOff[k] - hOn[k]); maxFill = math.max(maxFill, hOn[k] - hOff[k]); }
        }
        int bad = 0, worst = 0; string first = null;
        for (int j = 0; j < H; j++)
        for (int i = 0; i < W; i++)
        foreach (var (ni, nj) in new[] { (i + 1, j), (i, j + 1) })
        {
            if (ni >= W || nj >= H) continue;
            int a = j * W + i, b = nj * W + ni;
            if (kind[a] == 2 || kind[b] == 2) continue;
            if (capped && (kind[a] == 1 || kind[b] == 1)) continue;
            int grow = math.abs(hOn[a] - hOn[b]) - math.abs(hOff[a] - hOff[b]);
            worst = math.max(worst, grow);
            if (grow > 2) { bad++; if (first == null) first = $"({s.cx + i - s.rx - e}, {s.cz + j - s.rz - e}) step ON {math.abs(hOn[a] - hOn[b])} vs OFF {math.abs(hOff[a] - hOff[b])}"; }
        }
        int fails = 0;
        if (rimHigh > 0 && !capped) fails++;
        if (bad > 0) fails++;
        log.AppendLine($"    task 196 basin: bank UP {s.up} / DN {s.dn} voxels, band {Ext(s)} rings, bowl depth {s.depth}; rim wall over the fluid max {maxWall} voxel(s), " +
                       $"rim columns over L + 1: {rimHigh}{(capped ? " (CAPPED site)" : "")}; bank columns moved {moved:N0} (max cut {maxCut}, max fill {maxFill}); " +
                       $"largest step growth ON vs OFF outside the footprint {worst} voxel(s){(bad > 0 ? $", {bad} over 2: FAIL e.g. {first}" : "")}" +
                       $"{(rimHigh > 0 && !capped ? "; FAIL: rim over L + 1 on an uncapped site" : "")}");
        return fails;
    }

    private static void LogCase(StringBuilder log, Case cs, int fails, StringBuilder sb, int changed, int fluidOn, int shellCmp, int farCmp,
                                  double msOn, double msOff, double msSettle, int chunks)
    {
        string what = cs.deep
            ? $"deep {FluidName(cs.deepFluid)} pocket at ({cs.pk.centre.x:F0}, {cs.pk.centre.y:F0}, {cs.pk.centre.z:F0}) radii ({cs.pk.radii.x:F0}, {cs.pk.radii.y:F0}, {cs.pk.radii.z:F0}) level {cs.pk.level:F0}"
            : $"{(cs.forced ? "FORCED " : "")}{FluidName(cs.site.fluid)} basin at ({cs.site.cx}, {cs.site.cz}) r {cs.site.rx} x {cs.site.rz}, depth {cs.site.depth}, level {cs.site.level}";
        log.AppendLine($"  {cs.name}: {what}");
        log.AppendLine($"    {(fails == 0 ? "PASS" : "FAIL " + sb)}; changed ON vs OFF {changed:N0} voxels, non-water fluid voxels ON {fluidOn:N0}; " +
                       $"settle 150 ticks {msSettle / 1000:F1} s: nothing moved or reacted; shells 1-2 samples compared {shellCmp:N0}; far texels {farCmp:N0}; " +
                       $"{chunks} chunks ON {msOn:F0} ms / OFF {msOff:F0} ms (Editor, rough cost only)");
    }

    private sealed class Empty : IDisposable
    {
        private readonly BrickDataPool _pool = new BrickDataPool(16);
        public readonly ChunkStore store;
        public Empty() { store = new ChunkStore(_pool, new ChunkHandleAllocator(1)); }
        public void Dispose() => _pool.Dispose();
    }

    // ------------------------------------------------------------------
    // Images
    // ------------------------------------------------------------------
    private static Color32 Col(byte m, float shade)
    {
        var c = MaterialPalette.Of(m);
        return new Color32((byte)math.clamp(c.R * shade * 330f, 0, 255), (byte)math.clamp(c.G * shade * 330f, 0, 255), (byte)math.clamp(c.B * shade * 330f, 0, 255), 255);
    }

    /// Task 203: the site from above on the CPU (no renderer): each column's top material
    /// (TopMaterial), hill-shaded from the north-west, fluid darker with depth; the old
    /// ellipse's edge drawn in white dots. North (+z) is up. Screenshots/203/cpu_top_<case>.png.
    private static void TopView(TerrainGenChunkSource src, Case cs)
    {
        var s = cs.site; var spec = src.Spec; var o = src.O; var view = src.Map.View;
        int e = Ext(s) + 4, W = 2 * (s.rx + e) + 1, H = 2 * (s.rz + e) + 1;
        var hh = new int[W * H]; var mat = new byte[W * H]; var dep = new int[W * H];
        for (int j = 0; j < H; j++)
        for (int i = 0; i < W; i++)
        {
            int x = s.cx + i - s.rx - e, z = s.cz + j - s.rz - e, k = j * W + i;
            var c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1, false);
            hh[k] = c.height; dep[k] = c.water > c.height ? c.water - c.height : 0;
            mat[k] = VoxelEngine.Rendering.FarField.GenToEngine(DetailField.TopMaterial(in spec, in c, 1f, in o, x, z));
        }
        var px = new Color32[W * H];
        for (int j = 0; j < H; j++)
        for (int i = 0; i < W; i++)
        {
            int k = j * W + i;
            float shade;
            if (dep[k] > 0) shade = math.clamp(1.05f - dep[k] * 0.03f, 0.55f, 1.05f);
            else
            {
                int hw = i > 0 ? hh[k - 1] : hh[k], hn = j < H - 1 ? hh[k + W] : hh[k];
                shade = math.clamp(0.85f + 0.12f * ((hw - hh[k]) + (hn - hh[k])) * -1f, 0.45f, 1.15f);
            }
            px[k] = Col(mat[k], shade);
            int dx = i - s.rx - e, dz = j - s.rz - e;
            bool inE = FluidPockets.InFoot(dx, dz, s.rx, s.rz);
            if (inE && (!FluidPockets.InFoot(dx + 1, dz, s.rx, s.rz) || !FluidPockets.InFoot(dx - 1, dz, s.rx, s.rz) ||
                        !FluidPockets.InFoot(dx, dz + 1, s.rx, s.rz) || !FluidPockets.InFoot(dx, dz - 1, s.rx, s.rz)) && ((dx + dz) & 1) == 0)
                px[k] = new Color32(255, 255, 255, 255);
        }
        string dir = Path.Combine("Screenshots", "203"); Directory.CreateDirectory(dir);
        WritePng(Path.Combine(dir, $"cpu_top_{cs.name}{(s.outline ? "" : "_ellipse")}.png"), px, W, H, 4);
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

    /// A vertical slice along x through the pocket's centre: OFF (left) | ON (right).
    private static void CutAway(Case cs, Region on, Region off)
    {
        int cx = cs.deep ? (int)cs.pk.centre.x : cs.site.cx, cz = cs.deep ? (int)cs.pk.centre.z : cs.site.cz;
        int cy = cs.deep ? (int)cs.pk.level : cs.site.level;
        int hw = cs.deep ? 100 : math.min(160, cs.site.rx + Ext(cs.site) + 20), hh = cs.deep ? 70 : math.max(40, cs.site.up + 12);
        int W = 2 * hw, H = 2 * hh;
        var px = new Color32[W * 2 * H];
        for (int panel = 0; panel < 2; panel++)
        {
            var r = panel == 0 ? off : on;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int wx = cx - hw + x, wy = cy - hh + y;
                byte m = r.At(wx, wy, cz);
                px[y * W * 2 + panel * W + x] = m == Materials.Air ? new Color32(150, 190, 225, 255) : Col(m, 1f);
            }
            for (int y = 0; y < H; y++) px[y * W * 2 + W] = new Color32(0, 0, 0, 255);   // the divider
        }
        WritePng(Path.Combine(ShotDir, $"cut_{cs.name}.png"), px, W * 2, H, cs.deep ? 3 : 4);
    }

    /// A player pose ~10 m west and 6 m south of the basin's rim, 6 m over the ground, looking at its middle.
    /// Task 201 (report, Explicit): the largest seed-42 surface water and lava sites in
    /// FOREST, with a look-review pose framed from the north like 196's water retake
    /// (camera ~25 m past the far rim, 16 m over the fluid or 8 m over the ground).
    [Test, Explicit, Category("Slow")]
    public void ForestSites_Poses201()
    {
        using var src = new TerrainGenChunkSource(42u);
        src.SetFluidPockets(true);
        var map = src.Map; var spec = src.Spec; var view = map.View; var o = src.O;
        int n = FluidPockets.PlaneCells(in spec), cv = spec.mapCellVoxels;
        int[] plane = map.PocketPlane;
        string M(float v) => (v * 0.1f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        var log = new StringBuilder("Task 201 forest pocket sites, seed 42 (largest first per fluid)\n");
        var poses = new List<string>();
        foreach (byte f in new[] { GenMat.Water, GenMat.Lava })
        {
            var list = new List<FluidPockets.Site>();
            for (int i = 0; i < n * n; i++)
            {
                if (plane[i * FluidPockets.STRIDE + 3] == 0) continue;
                var st = FluidPockets.Decode(plane, i);
                if (st.fluid != f || map.Record.AtCell(st.cx / cv, st.cz / cv).Biome != 0) continue;
                list.Add(st);
            }
            list.Sort((a, b) => (b.rx + b.rz).CompareTo(a.rx + a.rz));
            foreach (var st in list)
            {
                float ex = st.cx + 0.5f, ez = st.cz + st.rz + 250;
                int g = ShapeField.Sample(in spec, in view, in o, ex, ez, 1).height;
                float ey = math.max(st.level + 160, g + 80);
                string pose = $"forest_{FluidName(f)}|{M(ex)},{M(ey)},{M(ez)}|{M(st.cx + 0.5f)},{M(st.level)},{M(st.cz + 0.5f)}";
                log.AppendLine($"  {FluidName(f)} at ({st.cx}, {st.cz}) r {st.rx} x {st.rz}, depth {st.depth}, level {st.level}, cam ground {g}: {pose}");
                if (poses.Count < 2 && !poses.Exists(q => q.StartsWith($"forest_{FluidName(f)}|"))) poses.Add(pose);
            }
        }
        string dir = Path.Combine("Logs", "TerrainGen", "pockets201");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "forest_sites.txt"), log.ToString());
        File.WriteAllText(Path.Combine(dir, "forest_poses.txt"), string.Join(";", poses));
        UnityEngine.Debug.Log(log.ToString());
    }

    private static string Pose(TerrainGenChunkSource src, Case cs)
    {
        var s = cs.site; var spec = src.Spec; var view = src.Map.View; var o = src.O;
        float ex = s.cx - (s.rx + 100), ez = s.cz - 60;
        int g = ShapeField.Sample(in spec, in view, in o, ex, ez, 1).height;
        float ey = math.max(s.level, g) + 60;
        string M(float v) => (v * 0.1f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        return $"{cs.name}|{M(ex)},{M(ey)},{M(ez)}|{M(s.cx + 0.5f)},{M(s.level)},{M(s.cz + 0.5f)}";
    }
}
