// Assets/CoreEngine/Tests/FluidPocketTests.cs
//
// Task 183 (placement, fluid pockets at rest, `-fluidpockets 1`; default OFF):
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
                           $"({st.ruled} matched a rule; rejected: cells {st.rejectedCells}, samples {st.rejectedSamples}, flat / sea {st.rejectedFlat})");

            var byBiome = new Dictionary<string, int>(); var byArch = new Dictionary<string, int>();
            int sites = 0, checkedRim = 0, checkedFoot = 0, berms = 0, maxBerm = 0;
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
                // every rim column (exhaustive) and the footprint on a 4-voxel grid
                for (int dz = -s.rz - 1; dz <= s.rz + 1; dz++)
                for (int dx = -s.rx - 1; dx <= s.rx + 1; dx++)
                {
                    bool foot = FluidPockets.InFoot(dx, dz, s.rx, s.rz);
                    bool rim = false;
                    if (!foot)
                        for (int oz = -1; oz <= 1; oz++) for (int ox = -1; ox <= 1; ox++)
                            if ((ox | oz) != 0 && FluidPockets.InFoot(dx + ox, dz + oz, s.rx, s.rz)) rim = true;
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
                    if (rim) { checkedRim++; if (c.height > b.height) { berms++; maxBerm = math.max(maxBerm, c.height - b.height); } }
                    else checkedFoot++;
                    if (why != null) { fails++; if (firstFail == null) firstFail = $"site {FluidName(s.fluid)} at ({s.cx}, {s.cz}) column ({x}, {z}): {why}"; }
                }
            }
            failures += fails;
            log.AppendLine($"  {sites} sites; rim columns checked {checkedRim:N0} (berm raised {berms:N0}, max {maxBerm} voxels), footprint columns {checkedFoot:N0}; violations {fails}{(firstFail != null ? " e.g. " + firstFail : "")}");
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
        FindAdversarial(map, out int2 cliff, out int2 canyon);
        var baseView = map.View; baseView.pocket = null;
        foreach (var (name, col, fluid) in new[] { ("forced_cliff_water", cliff, GenMat.Water), ("forced_canyon_lava", canyon, GenMat.Lava) })
        {
            int pi = (col.y >> FluidPockets.CELL_SHIFT) * n + (col.x >> FluidPockets.CELL_SHIFT);
            int lx = col.x & (FluidPockets.CELL - 1), lz = col.y & (FluidPockets.CELL - 1);
            int cx = (col.x & ~(FluidPockets.CELL - 1)) + math.clamp(lx, 64, FluidPockets.CELL - 64);
            int cz = (col.y & ~(FluidPockets.CELL - 1)) + math.clamp(lz, 64, FluidPockets.CELL - 64);
            int L = ShapeField.Sample(in spec, in baseView, in o, cx + 0.5f, cz + 0.5f, 1, false).height;   // the centre's own ground: the cliff side drops away
            var s = new FluidPockets.Site { cx = cx, cz = cz, level = L, rx = 40, rz = 28, depth = 16, fluid = fluid };
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
                int[] w = { (saved[0] & ~0xFFFFFF) | s.cx, s.cz, s.level, (int)((uint)s.fluid | ((uint)s.rx << 8) | ((uint)s.rz << 16) | ((uint)s.depth << 24)) };
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
        int yLo = s.level - s.depth - 3;
        return new Case
        {
            name = name, planeIndex = planeIndex, forced = forced, site = s,
            lo = new int3(s.cx - s.rx - 3, yLo, s.cz - s.rz - 3),
            hi = new int3(s.cx + s.rx + 3, s.level + 4, s.cz + s.rz + 3),
            org = new int3(s.cx - 64, yLo, s.cz - 64), bx = 16, by = 4, bz = 16,
        };
    }

    /// The tallest cliff (largest height step between neighbouring map cells, land)
    /// and the deepest canyon (land cell farthest below the mean of the cells 6 out).
    private static void FindAdversarial(WorldMap m, out int2 cliff, out int2 canyon)
    {
        int n = m.Spec.mapCells, cv = m.Spec.mapCellVoxels;
        float bestStep = -1f, bestDepth = -1f; cliff = canyon = default;
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
        }
    }

    private static int RunCase(TerrainGenChunkSource src, Case cs, StringBuilder log, List<string> poses)
    {
        var spec = src.Spec; var o = src.O;
        int fails = 0; var sb = new StringBuilder();
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
        int changed = 0, outside = 0, fluidOn = 0;
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
                    : math.abs(x - cs.site.cx) <= cs.site.rx + 1 && math.abs(z - cs.site.cz) <= cs.site.rz + 1;
                if (!inside) outside++;
            }
        }
        // a surface site OFF->ON may also retint deep pockets elsewhere in these chunks: allowed only for deep cases
        if (outside > 0 && !cs.deep)
        {
            // count only changes outside every deep pocket's shell
            int realOutside = 0;
            foreach (var cc in coords)
            {
                byte[] a = on.Chunk(cc), b = off.Chunk(cc); int3 org = cc << 7;
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] == b[i]) continue;
                    int x = org.x + (i & 127), z = org.z + ((i >> 7) & 127), y = org.y + (i >> 14);
                    if (math.abs(x - cs.site.cx) <= cs.site.rx + 1 && math.abs(z - cs.site.cz) <= cs.site.rz + 1) continue;
                    if (DetailField.Pocket(in spec, in o, x, y, z, out bool inShell) != 255 || inShell) continue;
                    realOutside++;
                }
            }
            outside = realOutside;
        }
        if (outside > 0) Fail($"{outside} voxels changed outside the pocket's columns");

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
            int hx = cs.site.cx + cs.site.rx, hz = cs.site.cz, L0 = cs.site.level;
            while (FluidPockets.InFoot(hx - cs.site.cx, 0, cs.site.rx, cs.site.rz)) hx++;
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

        // shell 0 = Generate; shells 1-2 agree wherever either is pocket fluid or lining
        byte lining = cs.deep ? FluidPockets.DeepLining(cs.deepFluid) : FluidPockets.Lining(cs.site.fluid);
        byte fluidE = VoxelEngine.Rendering.FarField.GenToEngine(cs.deep ? cs.deepFluid : cs.site.fluid);
        byte liningE = lining != 0 ? VoxelEngine.Rendering.FarField.GenToEngine(lining) : (byte)255;
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
                            if (want1 != fluidE && want1 != liningE && got != fluidE && got != liningE) continue;
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
                    if (!FluidPockets.InFoot(dx, dz, s.rx, s.rz)) continue;
                    var c = ShapeField.Sample(in spec, in view, in o, s.cx + dx + 0.5f, s.cz + dz + 0.5f, t);
                    ShapeField.FarColumn(in spec, ref c);
                    farCmp++;
                    byte top = on.At(s.cx + dx, s.level, s.cz + dz), above = on.At(s.cx + dx, s.level + 1, s.cz + dz);
                    if (c.water != s.level || c.height >= s.level || FluidPockets.FluidOf(c.waterKind) != s.fluid || top != fluidE || above != Materials.Air)
                    { Fail($"far texel {t} at ({s.cx + dx}, {s.cz + dz}): water {c.water} h {c.height} kind {c.waterKind}; LOD 0 {top} / {above}"); goto farDone; }
                }
                farDone:;
            }
        }

        LogCase(log, cs, fails, sb, changed, fluidOn, shellCmp, farCmp, msOn, msOff, msSettle, coords.Count);
        log.AppendLine("    negative control: " + control);
        CutAway(cs, on, off);
        if (!cs.deep && !cs.forced) poses.Add(Pose(src, cs));
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
        int hw = cs.deep ? 100 : math.min(120, cs.site.rx + 40), hh = cs.deep ? 70 : 40;
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
