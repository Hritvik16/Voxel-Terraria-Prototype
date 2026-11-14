// Assets/CoreEngine/Tests/MaterialRuleChunkTests.cs
//
// Task 180b: the material rules (TerrainGen.GroundRules, `-matrules 1`) in real
// chunks, seed 42. All Slow.
//   Chunks_...   sites found by the rules (a cliff per biome, a river bank, a bank
//                in a biome border zone, a beach, an oasis, a scree apron, flat
//                ground): ON twice = the same bytes; ON vs OFF differ only in
//                which SOLID (air and fluids identical: the rules never move the
//                ground); a "dug" pit (the top 8 m of a column set to air, as an
//                edit would) and a regeneration: the generator never sees it, so
//                the pit's walls and floor are the ORIGINAL surface's bands
//                (checked top-down against GroundRules). Logs/TerrainGen/180b/chunks.txt
//   Shells_BaredVoxels_WearTheSkin (task 182) shells 3 and 5 round every site: a
//                coarse voxel bared on top or on a side wears the skin, never the
//                soil / subsoil / stone under it (Logs/TerrainGen/180b/shell_bared182.txt).
//   Cost_...     dense bricks + generation ms, ON vs OFF interleaved, LOD 0
//                (3 x 3 chunk columns round each site) and shells 1-3:
//                Screenshots/180b/cost180b.txt. Editor Burst, this test's own
//                Generate calls: a direction, not a timing arm.
//   CutAway_...  a vertical slice 32 m wide x 30 m deep through each site, ON |
//                OFF side by side, coloured by the engine palette: what a dug pit's
//                wall shows (Screenshots/180b/pit_<site>.png).
//   Poses        (Explicit) the sites as -wsfixed poses for the look launch.
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using TerrainGen;
using Unity.Mathematics;
using VoxelEngine;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class MaterialRuleChunkTests
{
    private static TerrainGenChunkSource _src;
    private static readonly string LogDir = Path.Combine("Logs", "TerrainGen", "180b");
    private static readonly string ShotDir = Path.Combine("Screenshots", "180b");
    private static readonly string[] BIOME = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };

    /// Trees OFF (task 195a, trees are the chunk source's default now): a tree's density
    /// reads the column's cover slot, which the rules set (GroundRules, "trees read the
    /// Cover slot"), so rules ON vs OFF legitimately moves trees -- i.e. AIR. These proofs
    /// are about the rules' terrain; towns stay at the default (they do not read the rules).
    [OneTimeSetUp]
    public void BuildOnce() { if (_src == null) { _src = new TerrainGenChunkSource(42u); _src.SetMatRules(true); _src.SetTrees(false); } }

    [OneTimeTearDown]
    public void TearDown() { _src?.Dispose(); _src = null; }

    private struct Site { public string name; public int x, z, h; public float2 face; }

    private static List<Site> _sites;

    /// The rules' own sites on a 16 m grid over the island (first found per kind).
    private static List<Site> Sites()
    {
        if (_sites != null) return _sites;
        var on = _src.Spec; on.matRulesOff = 0;
        var view = _src.Map.View; var o = _src.O;
        var found = new Dictionary<string, Site>();
        string[] want = { "cliff_forest", "cliff_desert", "cliff_snow", "bank", "border_bank", "beach", "oasis_or_swamp", "scree", "flat" };
        int G = 160, half = (int)(on.islandRadius * 1.25f), c0 = on.worldSizeVoxels / 2;
        // a permutation of the grid, so the first find is spread over the island
        int N = (2 * half) / G;
        for (long t = 0; t < (long)N * N && found.Count < want.Length; t++)
        {
            long idx = t * 7919L % ((long)N * N);
            int x = c0 - half + (int)(idx % N) * G, z = c0 - half + (int)(idx / N) * G;
            var c = ShapeField.Sample(in on, in view, in o, x + 0.5f, z + 0.5f, 1);
            if (c.height <= on.seaLevelY || c.stampMat != 0 || c.stamp3D || c.lipLo != 0) continue;
            int slot = GroundRules.Slot(c.mrSlot);
            ShapeField.SampleMap(in view, x + 0.5f, z + 0.5f, out float macro, out float rough);
            var ri = GroundRules.Inputs(in on, in view, in o, new float2(x + 0.5f, z + 0.5f), macro, rough, false, 1, in c);
            bool dry = !(c.water > c.height);
            // the camera stands 60 m to +z: that ground must be lower than the site + 8 m (not inside a hill)
            var e = ShapeField.Sample(in on, in view, in o, x + 0.5f, z + 600.5f, 1);
            bool open = e.height < c.height + 80;
            string k = null;
            if (slot == GroundRules.Bare && ri.slopeDeg > 55f && ri.sunny > 0.45f && open)
            {
                if (c.surfBiome == GenBiome.Forest) k = "cliff_forest";
                else if (c.surfBiome == GenBiome.Desert) k = "cliff_desert";
                else if (c.surfBiome == GenBiome.Snow) k = "cliff_snow";
            }
            else if (slot == GroundRules.Hollow && dry && open)
            {
                if (DetailField.RockA(in c) != DetailField.RockB(in c)) k = "border_bank";
                else if (c.surfBiome == GenBiome.Desert || c.surfBiome == GenBiome.Jungle) k = "oasis_or_swamp";
                else k = "bank";
            }
            else if (slot == GroundRules.Shore && open) k = "beach";
            else if (slot == GroundRules.Scree && open && ri.upSlopeDeg > 50f) k = "scree";
            else if (slot == GroundRules.Flat && ri.slopeDeg < 8f && c.surfBiome == GenBiome.Forest) k = "flat";
            if (k != null && !found.ContainsKey(k)) found[k] = new Site { name = k, x = x, z = z, h = c.height };
        }
        _sites = new List<Site>();
        foreach (var k in want) if (found.TryGetValue(k, out var s)) _sites.Add(s);
        return _sites;
    }

    // ------------------------------------------------------------------
    private static byte Read(Chunk c, BrickDataPool pool, int3 local)
    {
        if (c.isUniform) return c.uniformMaterial;
        int bi = ((local.z >> 3) << 8) | ((local.y >> 3) << 4) | (local.x >> 3);
        uint d = c.bricks[bi].data;
        if ((d & 0x80000000u) == 0) return (byte)d;
        return pool.RawData[(int)(d & 0x3FFFFFFFu) * 512 + (((local.z & 7) << 6) | ((local.y & 7) << 3) | (local.x & 7))];
    }

    private static byte[] Gen(int3 coord, out int dense, out double ms, bool read = true)
    {
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK + 8);
        try
        {
            var chunk = new Chunk();
            var sw = Stopwatch.StartNew();
            _src.Generate(coord, chunk, new ChunkHandleAllocator(2), pool, null);
            ms = sw.Elapsed.TotalMilliseconds;
            dense = 0;
            var v = new byte[128 * 128 * 128];
            if (chunk.isUniform) { for (int i = 0; i < v.Length; i++) v[i] = chunk.uniformMaterial; return v; }
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if ((chunk.bricks[i].data & 0x80000000u) != 0) dense++;
            if (!read) return null;
            for (int y = 0; y < 128; y++)
            for (int z = 0; z < 128; z++)
            for (int x = 0; x < 128; x++)
                v[TerrainGenChunkSource.Index(x, y, z)] = Read(chunk, pool, new int3(x, y, z));
            return v;
        }
        finally { pool.Dispose(); }
    }

    private static HashSet<byte> NonSolidEngine()
    {
        var s = new HashSet<byte>();
        for (byte g = 0; g <= GenMat.RiverLip; g++) s.Add(VoxelEngine.Rendering.FarField.GenToEngine(g));
        return s;
    }

    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    public void Chunks_Deterministic_GroundUnchanged_DigNeverChangesBeneath()
    {
        var sites = Sites();
        Assert.GreaterOrEqual(sites.Count, 6, "the rules' sites exist on seed 42");
        var non = NonSolidEngine();
        var log = new StringBuilder("Task 180b chunks, seed 42 (-matrules 1 vs 0)\n");
        var on = _src.Spec; var view = _src.Map.View; var o = _src.O;
        int chunks = 0, pits = 0;
        foreach (var s in sites)
        {
            int3 coord = new int3(s.x >> 7, s.h >> 7, s.z >> 7);
            for (int dy = -1; dy <= 0; dy++)
            {
                var cc = coord + new int3(0, dy, 0);
                _src.SetMatRules(true);
                var a = Gen(cc, out int dA, out _);
                var b = Gen(cc, out _, out _);
                FastAssert.BytesEqual(a, b, $"{s.name} {cc}: ON twice");
                _src.SetMatRules(false);
                var off = Gen(cc, out int dOff, out _);
                _src.SetMatRules(true);
                int changed = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    bool na = non.Contains(a[i]), no = non.Contains(off[i]);
                    if ((na || no) && off[i] != a[i]) Assert.AreEqual(off[i], a[i], $"{s.name} {cc} voxel {i}: air / fluid must not change");   // 184b: guarded, same check
                    else if (a[i] != off[i]) changed++;
                }
                // the dug pit: 8 m of the site column set to air in a copy (an edit, applied on
                // top); regenerating ignores it, so what the pit exposes is the original column.
                var dug = (byte[])a.Clone();
                int lx = s.x & 127, lz = s.z & 127, top = -1;
                for (int ly = 127; ly >= 0; ly--) if (!non.Contains(a[TerrainGenChunkSource.Index(lx, ly, lz)])) { top = ly; break; }
                if (top >= 0)
                {
                    for (int ly = top; ly > top - 80 && ly >= 0; ly--) dug[TerrainGenChunkSource.Index(lx, ly, lz)] = Materials.Air;
                    var re = Gen(cc, out _, out _);
                    FastAssert.BytesEqual(a, re, $"{s.name} {cc}: regeneration never reads the dug voxels");
                    // the pit's column, top-down, against the rules (where no cave / ore / lip)
                    var c = ShapeField.Sample(in on, in view, in o, s.x + 0.5f, s.z + 0.5f, 1);
                    float st = ShapeField.MaxStep(in on, in view, in o, s.x + 0.5f, s.z + 0.5f, 1, c.height);
                    var cd = DetailField.PrepareColumn(in on, in o, s.x, s.z);
                    var seq = new StringBuilder();
                    byte prev = 255;
                    for (int ly = top; ly >= 0; ly--)
                    {
                        int y = (cc.y << 7) + ly, d = c.height - y;
                        if (d < 0) continue;
                        byte m = a[TerrainGenChunkSource.Index(lx, ly, lz)];
                        int band = GroundRules.Band(in c, st, d);
                        byte g = band <= 2 ? GroundRules.NearMaterial(in c, st, band) : GroundRules.RockAt(in on, in c, y, cd.deepOffset, cd.cavernOffset, cd.underOffset);
                        byte want = VoxelEngine.Rendering.FarField.GenToEngine(g);
                        if (!non.Contains(m) && !TerrainGen.Ores.IsOre(DetailField.Material(in on, in o, in c, in cd, st, s.x, y, s.z)))
                            Assert.AreEqual(want, m, $"{s.name} pit depth {d}: band {band}");
                        if (g != prev) { seq.Append($" | {d * 0.1f:F1} m {g}"); prev = g; }
                    }
                    if (dy == 0) { log.AppendLine($"  {s.name,-15} ({s.x},{s.h},{s.z}) pit top-down (depth where each GenMat starts):{seq}"); pits++; }
                }
                log.AppendLine($"  {s.name,-15} chunk {cc}: ON twice equal, air/fluid equal ON vs OFF, {changed} solid voxels repainted; dense OFF {dOff} ON {dA}");
                chunks++;
            }
        }
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "chunks.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
        Assert.Greater(pits, 4);
    }

    // ------------------------------------------------------------------
    /// The rules repaint SOLID voxels only: shells 1-3 round every site, and LOD 0's
    /// 3 x 3 x 3 chunks round the snow cliff (where the ON | OFF shot differed), keep
    /// every air / fluid voxel of the OFF arm.
    [Test, Category("Slow")]
    public void Shells_And_SnowCliff_AirAndFluidUnchanged()
    {
        var sites = Sites();
        var non = NonSolidEngine();
        var log = new StringBuilder("Task 180b: air / fluid ON vs OFF (seed 42)\n");
        using var a = new TerrainGenChunkSource.ShellChunk();
        using var b = new TerrainGenChunkSource.ShellChunk();
        byte At(TerrainGenChunkSource.ShellChunk c, int x, int y, int z)
        {
            int bi = ((z >> 3) << 8) | ((y >> 3) << 4) | (x >> 3);
            return c.BrickUniform[bi] ? c.BrickMat[bi] : c.Mats[TerrainGenChunkSource.Index(x, y, z)];
        }
        try
        {
            foreach (var s in sites)
            for (int k = 1; k <= 3; k++)
            for (int ly = -1; ly <= 1; ly++)
            {
                int3 coord = (new int3(s.x, s.h, s.z) >> (7 + k)) + new int3(0, ly, 0);
                _src.SetMatRules(false); _src.GenerateShellChunk(k, coord, b);
                _src.SetMatRules(true); _src.GenerateShellChunk(k, coord, a);
                int bad = 0, first = -1;
                if (!(a.IsUniformAir && b.IsUniformAir))
                    for (int y = 0; y < 128; y++)
                    for (int z = 0; z < 128; z++)
                    for (int x = 0; x < 128; x++)
                    {
                        byte va = a.IsUniformAir ? Materials.Air : At(a, x, y, z), vb = b.IsUniformAir ? Materials.Air : At(b, x, y, z);
                        if ((non.Contains(va) || non.Contains(vb)) && va != vb) { if (bad++ == 0) first = TerrainGenChunkSource.Index(x, y, z); }
                    }
                log.AppendLine($"  {s.name,-15} shell {k} {coord}: {bad} air/fluid voxels differ");
                Assert.AreEqual(0, bad, $"{s.name} shell {k} {coord}: air / fluid differs ON vs OFF (first at index {first})");
            }
            var snow = sites.Find(t => t.name == "cliff_snow");
            if (snow.name != null)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var cc = new int3((snow.x >> 7) + dx, (snow.h >> 7) + dy, (snow.z >> 7) + dz);
                    _src.SetMatRules(false); var off = Gen(cc, out _, out _);
                    _src.SetMatRules(true); var on = Gen(cc, out _, out _);
                    int bad = 0;
                    for (int i = 0; i < on.Length; i++) if ((non.Contains(on[i]) || non.Contains(off[i])) && on[i] != off[i]) bad++;
                    log.AppendLine($"  cliff_snow LOD 0 {cc}: {bad} air/fluid voxels differ");
                    Assert.AreEqual(0, bad, $"cliff_snow LOD 0 {cc}: air / fluid differs ON vs OFF");
                }
        }
        finally
        {
            _src.SetMatRules(true);
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, "air_on_off.txt"), log.ToString());
        }
    }

    // ------------------------------------------------------------------
    /// Task 182, defect 2 of 180b: a coarse shell voxel bared to the open air -- on
    /// top (its sample within one voxel of its column's ground) or on a side (a 4-
    /// neighbour column's ground lies below its sample) -- wears its column's SKIN
    /// (Band 0's material), never the soil, subsoil or stone under it; columns whose
    /// skin is skipped (bare, sheer) show their stone, as at LOD 0. Shells 3 and 5
    /// (0.8 m, 3.2 m: 180b's mosaic was at 3.2 m and up) round every site, the
    /// generator alone (ShellFill off, as OreRuleTests). "Before" counts the bared
    /// voxels whose OWN depth's band is deeper than skin (what 180b drew).
    [Test, Category("Slow")]
    public void Shells_BaredVoxels_WearTheSkin()
    {
        var sites = Sites();
        var non = NonSolidEngine();
        var log = new StringBuilder("Task 182: bared shell voxels (seed 42, -matrules 1, ShellFill off)\n");
        int bared = 0, skin = 0, wrong = 0, before = 0;
        _src.SetMatRules(true); _src.ShellFill = false;
        using var sc = new TerrainGenChunkSource.ShellChunk();
        try
        {
            foreach (var s in sites)
            foreach (int k in new[] { 3, 5 })
            for (int ly0 = -1; ly0 <= 1; ly0++)
            {
                int3 coord = (new int3(s.x, s.h, s.z) >> (7 + k)) + new int3(0, ly0, 0);
                _src.GenerateShellChunk(k, coord, sc);
                if (sc.IsUniformAir) continue;
                int3 origin = coord << (7 + k);
                int half = (1 << (k - 1)) - 1, T = 1 << k;
                int sB = 0, sS = 0, sW = 0, sBefore = 0;
                for (int lz = 1; lz < 127; lz++)
                for (int lx = 1; lx < 127; lx++)
                {
                    int h = sc.GroundY(lx, lz);
                    int lo = math.min(math.min(sc.GroundY(lx - 1, lz), sc.GroundY(lx + 1, lz)), math.min(sc.GroundY(lx, lz - 1), sc.GroundY(lx, lz + 1)));
                    int yTop = h - origin.y - half;   // the top sample's local y (before >> k)
                    if (yTop < 0) continue;
                    ColumnSample c = default; float st = 0f; bool have = false;
                    for (int ly = math.min(126, yTop >> k); ly >= 0; ly--)
                    {
                        int y = origin.y + (ly << k) + half, d = h - y;
                        if (d < 0) continue;
                        bool open = d < T || y > lo;   // bared on top or to a lower neighbour column
                        if (!open) break;              // deeper voxels are bared by nothing
                        byte got = sc.Get(lx, ly, lz);
                        if (non.Contains(got)) continue;   // an opening / fluid
                        if (!have)
                        {
                            // the voxel pass's own column (Burst) -- a Mono resample can round 1 voxel apart
                            c = sc.Column(lx, lz); st = sc.ColumnStep(lx, lz); have = true;
                            Assert.AreEqual(math.max(T, h - lo), (int)c.mrExpose, $"{s.name} shell {k} column ({lx},{lz}): mrExpose = max(2^k, h - lowest neighbour)");
                        }
                        if (c.stampMat != 0 || c.stamp3D || c.lipLo != 0) break;   // stamps paint their own
                        if (GroundRules.Band(in c, st, 0) == 3) break;          // bare / sheer: the stone, as LOD 0
                        sB++;
                        byte want = VoxelEngine.Rendering.FarField.GenToEngine(GroundRules.NearMaterial(in c, st, 0));
                        byte rb = DetailField.RockA(in c);
                        var deeper = new[] { GroundRules.Soil(c.surfBiome), GroundRules.Subsoil(c.surfBiome), GroundRules.Stone(rb), GroundRules.DeepStone(rb) };
                        bool isDeeper = false;
                        foreach (var g in deeper) if (VoxelEngine.Rendering.FarField.GenToEngine(g) == got && got != want) isDeeper = true;
                        if (got == want) sS++;
                        else if (isDeeper) { if (sW++ == 0) log.AppendLine($"    first wrong: {s.name} shell {k} {coord} ({lx},{ly},{lz}) depth {d} got {got} want {want} slot {c.mrSlot} st {st} sub {c.mrSub} expose {c.mrExpose}"); }
                        var c0 = c; c0.mrExpose = 0;
                        if (d >= GroundRules.SKIN && GroundRules.Band(in c0, st, d) > 0) sBefore++;
                    }
                }
                log.AppendLine($"  {s.name,-15} shell {k} {coord}: {sB} bared voxels, {sS} skin, {sW} soil/subsoil/stone; 180b's depth band would bare {sBefore} under the skin");
                bared += sB; skin += sS; wrong += sW; before += sBefore;
            }
        }
        finally
        {
            _src.ShellFill = true;
            Directory.CreateDirectory(LogDir);
            log.AppendLine($"TOTAL: {bared} bared voxels, {skin} skin ({100.0 * skin / math.max(1, bared):F2} %), {wrong} soil/subsoil/stone; before (180b) {before} would show a deeper band");
            File.WriteAllText(Path.Combine(LogDir, "shell_bared182.txt"), log.ToString());
            UnityEngine.Debug.Log(log.ToString());
        }
        Assert.Greater(bared, 5000, "the sites' shells have bared slopes");
        Assert.Greater(before, 500, "the adversarial case exists: 180b's bands bared soil / stone here");
        Assert.AreEqual(0, wrong, "a bared shell voxel shows a band under the skin");
        Assert.Greater(skin, bared * 0.95, "bared shell voxels wear the skin");
    }

    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    [Category("Nightly")]   // task 184b: test-suite tier
    public void Cost_OnVsOff()
    {
        var sites = Sites();
        var log = new StringBuilder();
        log.AppendLine("Task 180b cost, seed 42: dense bricks (512-byte bodies, CPU + GPU) and Generate ms, -matrules 1 vs 0, interleaved per chunk.");
        log.AppendLine("LOD 0: 3 x 3 chunk columns round each site, the ground layer -1 .. +1. Shells 1-3: the site's shell chunk, 3 layers. Editor Burst, this test's own calls.");
        long tdOn = 0, tdOff = 0; double ttOn = 0, ttOff = 0;
        foreach (var s in sites)
        {
            long dOn = 0, dOff = 0; double tOn = 0, tOff = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var cc = new int3((s.x >> 7) + dx, (s.h >> 7) + dy, (s.z >> 7) + dz);
                _src.SetMatRules(false); Gen(cc, out int nOff, out double mOff, false);
                _src.SetMatRules(true); Gen(cc, out int nOn, out double mOn, false);
                dOn += nOn; dOff += nOff; tOn += mOn; tOff += mOff;
            }
            tdOn += dOn; tdOff += dOff; ttOn += tOn; ttOff += tOff;
            log.AppendLine($"{s.name,-15} LOD 0, 27 chunks: dense OFF {dOff} -> ON {dOn} ({Pct(dOff, dOn)}), gen OFF {tOff / 27:F1} -> ON {tOn / 27:F1} ms/chunk ({Pct(tOff, tOn)})");
            using var sc = new TerrainGenChunkSource.ShellChunk();
            for (int k = 1; k <= 3; k++)
            {
                long[] dn = new long[2]; double[] ms = new double[2];
                for (int ly = -1; ly <= 1; ly++)
                for (int arm = 0; arm < 2; arm++)
                {
                    _src.SetMatRules(arm == 1);
                    int3 coord = (new int3(s.x, s.h, s.z) >> (7 + k)) + new int3(0, ly, 0);
                    var sw = Stopwatch.StartNew();
                    _src.GenerateShellChunk(k, coord, sc);
                    ms[arm] += sw.Elapsed.TotalMilliseconds;
                    dn[arm] += sc.DenseBricks;
                }
                log.AppendLine($"                shell {k}: dense OFF {dn[0]} -> ON {dn[1]} ({Pct(dn[0], dn[1])}), gen OFF {ms[0] / 3:F1} -> ON {ms[1] / 3:F1} ms/chunk");
            }
        }
        _src.SetMatRules(true);
        log.AppendLine($"ALL sites, LOD 0: dense OFF {tdOff} -> ON {tdOn} ({Pct(tdOff, tdOn)}, {(tdOn - tdOff) * 512 / 1048576.0:+0.00;-0.00} MB of bodies over {sites.Count * 27} chunks); "
                     + $"gen OFF {ttOff / (sites.Count * 27):F1} -> ON {ttOn / (sites.Count * 27):F1} ms/chunk ({Pct(ttOff, ttOn)})");
        Directory.CreateDirectory(ShotDir); Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(ShotDir, "cost180b.txt"), log.ToString());
        File.WriteAllText(Path.Combine(LogDir, "cost.txt"), log.ToString());
        UnityEngine.Debug.Log(log.ToString());
        Assert.Greater(tdOn, 0);
    }

    private static string Pct(double a, double b) => a > 0 ? $"{100.0 * (b - a) / a:+0;-0} %" : "n/a";

    // ------------------------------------------------------------------
    [Test, Category("Slow")]
    public void CutAway_Pits()
    {
        var sites = Sites();
        const int W = 320, H = 300, GAP = 8, SCALE = 2, ABOVE = 40;   // 32 m wide, 4 m above the site's ground to 26 m under
        var view = _src.Map.View; var o = _src.O;
        var sky = new UnityEngine.Color32(170, 200, 235, 255);
        var water = new UnityEngine.Color32(40, 90, 160, 255);
        foreach (var s in sites)
        {
            int PW = (2 * W + GAP) * SCALE, PH = H * SCALE;
            var px = new UnityEngine.Color32[PW * PH];
            for (int i = 0; i < px.Length; i++) px[i] = new UnityEngine.Color32(255, 255, 255, 255);
            for (int arm = 0; arm < 2; arm++)
            {
                var spec = _src.Spec; spec.matRulesOff = arm == 0 ? (byte)0 : (byte)1;   // left ON, right OFF
                int ox = arm * (W + GAP);
                for (int i = 0; i < W; i++)
                {
                    int x = s.x - W / 2 + i, z = s.z;
                    var c = ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                    float st = ShapeField.MaxStep(in spec, in view, in o, x + 0.5f, z + 0.5f, 1, c.height);
                    var cd = DetailField.PrepareColumn(in spec, in o, x, z);
                    for (int j = 0; j < H; j++)
                    {
                        int y = s.h + ABOVE - j;
                        byte m = DetailField.Material(in spec, in o, in c, in cd, st, x, y, z);
                        UnityEngine.Color32 col;
                        if (m == GenMat.Air) col = sky;
                        else if (GenMat.IsFluid(m) || m == GenMat.RiverLip) col = water;
                        else
                        {
                            var pc = MaterialPalette.Of(VoxelEngine.Rendering.FarField.GenToEngine(m));
                            col = new UnityEngine.Color32((byte)math.clamp(pc.R * 255, 0, 255), (byte)math.clamp(pc.G * 255, 0, 255), (byte)math.clamp(pc.B * 255, 0, 255), 255);
                        }
                        for (int sy = 0; sy < SCALE; sy++)
                        for (int sx = 0; sx < SCALE; sx++)
                            px[(PH - 1 - (j * SCALE + sy)) * PW + (ox + i) * SCALE + sx] = col;
                    }
                }
            }
            var tex = new UnityEngine.Texture2D(PW, PH, UnityEngine.TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, $"pit_{s.name}.png"), UnityEngine.ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.DestroyImmediate(tex);
        }
        Assert.Greater(sites.Count, 4);
    }

    // ------------------------------------------------------------------
    [Test, Explicit("writes Logs/TerrainGen/180b/poses.txt for -wsfixed")]
    public void Poses()
    {
        var on = _src.Spec; var view = _src.Map.View; var o = _src.O;
        var items = new List<string>();
        string F(float v) => (v * 0.1f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var s in Sites())
        {
            // the camera 60 m to +z (the survey looks -z: the sun behind it), 12 m over the higher ground
            float2 t = new float2(s.x + 0.5f, s.z + 0.5f), e = t + new float2(0f, 600f);
            float gE = ShapeField.Sample(in on, in view, in o, e.x, e.y, 1).height;
            float eyeY = math.max(s.h, gE) + 120f;
            items.Add($"{s.name}|{F(e.x)},{F(eyeY)},{F(e.y)}|{F(t.x)},{F(s.h + 10f)},{F(t.y)}");
        }
        Directory.CreateDirectory(LogDir);
        File.WriteAllText(Path.Combine(LogDir, "poses.txt"), string.Join(";", items) + "\n");
        UnityEngine.Debug.Log(string.Join(";", items));
    }
}
