// Assets/CoreEngine/Tests/ShellFillTests.cs
//
// Task 83 (engine step 11d; owner Q44 (a); Amendment 8.14 row 13's fill note):
// the shell cave fill (TerrainGenChunkSource.ShellFill.cs) on seed 42.
//   Visible      (i) a top-down and 4 side-view raycasts of every test chunk:
//                fill + flatten (mouths off) vs the generator: 0 rays differ.
//                With mouths on, every ray that differs hit an opened mouth voxel.
//   Mouths       (ii) tunnel mouths found in shell 3 (cliff sites): each one that
//                shows in shells 3-6 before the fill still shows after it (0 lost);
//                the mouth rule's gains are reported.
//   Bricks       (iii) dense bricks drop (per shell, reported; total asserted).
//   Prefilter    the mouth prefilter changes no byte at the mouth chunks.
//   Far ops      a crater crossing a chunk face below the surface stays open.
//   Shells 1-2   untouched by the fill.
//   Sea / lake   (task 83b: enclosed fluids are filled too, the default) at a sea
//                and a lake site no fluid voxel a ray from outside can reach changes,
//                and the outside rays see the same.
// "Pass" = air, a fluid or the river lip (GenMat 0-8), as the fill defines it.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class ShellFillTests
{
    private static TerrainGenChunkSource _src;
    private static readonly bool[] Pass = new bool[256];

    [OneTimeSetUp]
    public void BuildOnce()
    {
        if (_src == null) _src = new TerrainGenChunkSource(42u);
        Pass[Materials.Air] = true;
        for (int g = 0; g <= TerrainGen.GenMat.RiverLip; g++) Pass[VoxelEngine.Rendering.FarField.GenToEngine((byte)g)] = true;
    }

    [OneTimeTearDown]
    public void TearDown() { _src?.Dispose(); _src = null; }

    [TearDown]
    public void Defaults()
    {
        if (_src == null) return;
        _src.ShellFill = true; _src.ShellFillMouths = true; _src.ShellFillFlatten = true; _src.ShellFillMouthPrefilter = true;
        _src.ShellFillFluids = true;
    }

    // ------------------------------------------------------------------
    // Chunks as plain arrays
    // ------------------------------------------------------------------
    private sealed class C
    {
        public int K; public int3 Coord; public byte[] M; public int[] LyOpen, Ground; public int Dense; public double Ms;
    }

    /// variant: 0 = the generator (no fill), 1 = fill + flatten without mouths, 2 = everything,
    /// 3 = everything without the prefilter. Every fill variant fills enclosed fluids (83b's default).
    private static readonly Dictionary<(int, int3, int), C> _cache = new Dictionary<(int, int3, int), C>();

    private static C Gen(int k, int3 coord, int variant, EditOpSnapshot ops = null)
    {
        if (ops == null && _cache.TryGetValue((k, coord, variant), out var hit)) return hit;
        _src.ShellFill = variant != 0; _src.ShellFillMouths = variant >= 2; _src.ShellFillMouthPrefilter = variant != 3;
        _src.ShellFillFluids = true;
        var c = new C { K = k, Coord = coord, M = new byte[128 * 128 * 128], LyOpen = new int[128 * 128], Ground = new int[128 * 128] };
        using (var sc = new TerrainGenChunkSource.ShellChunk())
        {
            var t = System.Diagnostics.Stopwatch.StartNew();
            if (ops != null) _src.GenerateShellChunk(k, coord, sc, ops); else _src.GenerateShellChunk(k, coord, sc);
            c.Ms = t.Elapsed.TotalMilliseconds;
            if (!sc.IsUniformAir) sc.Mats.CopyTo(c.M);
            c.Dense = sc.DenseBricks;
            int V = 1 << k, half = (1 << (k - 1)) - 1, oy = coord.y << (7 + k);
            for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++)
            {
                c.Ground[(z << 7) | x] = sc.GroundY(x, z);
                c.LyOpen[(z << 7) | x] = ((sc.GroundY(x, z) - V - oy - half) >> k) + 1;
            }
        }
        _src.ShellFill = true; _src.ShellFillMouths = true; _src.ShellFillMouthPrefilter = true;
        if (ops == null) _cache[(k, coord, variant)] = c;
        return c;
    }

    private static int Idx(int x, int y, int z) => (y << 14) | (z << 7) | x;
    private static bool HeightOpen(C c, int x, int y, int z) => y >= c.LyOpen[(z << 7) | x];

    /// Pass voxel below the surface margin with a 6-neighbour (in the chunk) that is a
    /// pass voxel above it: a hole in the ground's face.
    private static bool IsMouth(C c, int x, int y, int z)
    {
        if (!Pass[c.M[Idx(x, y, z)]] || HeightOpen(c, x, y, z)) return false;
        for (int d = 0; d < 6; d++)
        {
            int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0), nz = z + (d == 4 ? -1 : d == 5 ? 1 : 0);
            if ((uint)nx >= 128 || (uint)ny >= 128 || (uint)nz >= 128) continue;
            if (Pass[c.M[Idx(nx, ny, nz)]] && HeightOpen(c, nx, ny, nz)) return true;
        }
        return false;
    }

    /// One record per ray: first non-air voxel and first non-pass voxel (index, material).
    /// Rays start only in a voxel open by height (a ray from outside the ground).
    private static List<long> Rays(C c)
    {
        var r = new List<long>();
        for (int view = 0; view < 5; view++)
        for (int a = 0; a < 128; a++)
        for (int b = 0; b < 128; b++)
        {
            long nonAir = -1, hit = -1; bool started = false;
            for (int t = 0; t < 128; t++)
            {
                int x, y, z;
                switch (view)
                {
                    case 0: x = a; z = b; y = 127 - t; break;   // top-down
                    case 1: y = a; z = b; x = t; break;         // +x
                    case 2: y = a; z = b; x = 127 - t; break;   // -x
                    case 3: y = a; x = b; z = t; break;         // +z
                    default: y = a; x = b; z = 127 - t; break;  // -z
                }
                if (t == 0) { if (!HeightOpen(c, x, y, z)) break; started = true; }
                int i = Idx(x, y, z); byte m = c.M[i];
                if (nonAir < 0 && m != Materials.Air) nonAir = ((long)i << 8) | m;
                if (!Pass[m]) { hit = ((long)i << 8) | m; break; }
            }
            r.Add(started ? (nonAir << 32) ^ hit : long.MinValue);
        }
        return r;
    }

    private static int HitIndex(C c, int ray)
    {
        int view = ray / (128 * 128), a = (ray >> 7) & 127, b = ray & 127;
        for (int t = 0; t < 128; t++)
        {
            int x, y, z;
            switch (view)
            {
                case 0: x = a; z = b; y = 127 - t; break;
                case 1: y = a; z = b; x = t; break;
                case 2: y = a; z = b; x = 127 - t; break;
                case 3: y = a; x = b; z = t; break;
                default: y = a; x = b; z = 127 - t; break;
            }
            if (!Pass[c.M[Idx(x, y, z)]]) return Idx(x, y, z);
        }
        return -1;
    }

    // ------------------------------------------------------------------
    // Sites: land columns on seed 42 with a cliff (>= 25 m drop over 20 m).
    // ------------------------------------------------------------------
    private static List<int3> CliffSites(int want)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        var list = new List<int3>();
        int W = spec.worldSizeVoxels;
        for (int gz = W / 10; gz < W * 9 / 10 && list.Count < want; gz += 1700)
        for (int gx = W / 10; gx < W * 9 / 10 && list.Count < want; gx += 1700)
        {
            var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, gx + 0.5f, gz + 0.5f, 1);
            if (c.height < spec.seaLevelY + 100 || c.water > c.height) continue;
            int drop = 0;
            for (int d = 0; d < 4; d++)
            {
                int dx = d == 0 ? 200 : d == 1 ? -200 : 0, dz = d == 2 ? 200 : d == 3 ? -200 : 0;
                drop = math.max(drop, c.height - TerrainGen.ShapeField.Sample(in spec, in view, in o, gx + dx + 0.5f, gz + dz + 0.5f, 1).height);
            }
            if (drop >= 250) list.Add(new int3(gx, c.height, gz));
        }
        return list;
    }

    /// Seed 42's cave entrances: the generator's 3D stamps that open the ground
    /// (Stamps.cs: cave mouth = an inclined tube 9-14 m across; fissure = a slot
    /// 1.2-2.5 m wide; fossil = the skull cave, a 9 m mouth). Its tunnels never
    /// come within 6 m of the column top (DetailField.CARVE_MIN_DEPTH), so they
    /// open only where the ground drops that much between neighbouring columns.
    private struct Entrance { public byte Kind; public int3 Site; public int Reach; public TerrainGen.Stamps.Stamp St; }

    private static List<Entrance> Entrances(int perKind)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        var list = new List<Entrance>(); var n = new int[16];
        int cells = spec.worldSizeVoxels >> TerrainGen.Stamps.CELL_SHIFT;
        for (int gz = 0; gz < cells; gz++)
        for (int gx = 0; gx < cells; gx++)
        {
            if (!TerrainGen.Stamps.CellStamp(in o, gx, gz, out var st)) continue;
            if (st.kind != TerrainGen.Stamps.CaveMouth && st.kind != TerrainGen.Stamps.Fissure && st.kind != TerrainGen.Stamps.Fossil) continue;
            if (n[st.kind] >= perKind) continue;
            var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, st.pos.x, st.pos.y, 1);
            if (!c.stamp3D) continue;   // under water, a lake, or a singleton's area: not built
            TerrainGen.Stamps.SampleSiteHeight(in spec, in view, st.pos, out int g);
            int reach = st.kind == TerrainGen.Stamps.Fissure ? 600 : st.kind == TerrainGen.Stamps.Fossil ? 400 : 500;
            list.Add(new Entrance { Kind = st.kind, Site = new int3((int)st.pos.x, g, (int)st.pos.y), Reach = reach, St = st });
            n[st.kind]++;
        }
        return list;
    }

    private static bool InCarve(Entrance e, C c, int x, int y, int z)
    {
        int k = c.K, V = 1 << k;
        int3 f0 = (c.Coord << (7 + k)) + (new int3(x, y, z) << k);
        int top = c.Ground[(z << 7) | x];
        for (int i = 0; i < 9; i++)
        {
            int3 p = i == 8 ? f0 + (V >> 1) : f0 + new int3((i & 1) * (V - 1), ((i >> 1) & 1) * (V - 1), (i >> 2) * (V - 1));
            if (TerrainGen.Stamps.SurfaceCarve(in e.St, e.Site.y, p.x, p.y, p.z, top)) return true;
        }
        return false;
    }

    private static string KindName(byte k) => k == TerrainGen.Stamps.CaveMouth ? "cave mouth" : k == TerrainGen.Stamps.Fissure ? "fissure" : "skull cave";

    /// Does shell k show the entrance as a hole: a pass voxel below the surface
    /// margin, touching open air, whose fine box (centre or a corner) lies in
    /// the stamp's carve (Stamps.SurfaceCarve, the column top = the coarse
    /// column's ground)?
    private static bool Shows(Entrance e, int k, int variant) => Holes(e, k, variant, true) > 0;

    /// Hole voxels of the entrance (Shows' test), counted; `any` stops at the first.
    private static int Holes(Entrance e, int k, int variant, bool any = false)
    {
        int n = 0;
        int3 lo = e.Site - new int3(e.Reach, 600, e.Reach), hi = e.Site + new int3(e.Reach, 200, e.Reach);
        int3 c0 = lo >> (7 + k), c1 = hi >> (7 + k);
        for (int cz = c0.z; cz <= c1.z; cz++) for (int cy = math.max(0, c0.y); cy <= c1.y; cy++) for (int cx = c0.x; cx <= c1.x; cx++)
        {
            int3 coord = new int3(cx, cy, cz);
            var c = Gen(k, coord, variant);
            int3 l0 = math.max((lo - (coord << (7 + k))) >> k, 0), l1 = math.min((hi - (coord << (7 + k))) >> k, 127);
            for (int y = l0.y; y <= l1.y; y++) for (int z = l0.z; z <= l1.z; z++) for (int x = l0.x; x <= l1.x; x++)
                if (IsMouth(c, x, y, z) && InCarve(e, c, x, y, z)) { n++; if (any) return n; }
        }
        return n;
    }

    // ==================================================================

    [Test, Category("Slow")]
    public void Fill_KeepsWhatRaysSee_KeepsMouths_DropsDenseBricks()
    {
        var lines = new List<string>();
        var sites = CliffSites(10);
        Assert.GreaterOrEqual(sites.Count, 6, "too few cliff sites on seed 42");
        lines.Add($"{sites.Count} cliff sites");
        try
        {
            // (i) + (iii): surface chunk and the one below, shells 3-7.
            var denseA = new long[8]; var denseC = new long[8]; var chunks = new int[8];
            int raysChecked = 0, raysWide = 0, raysWideBad = 0, widenedVox = 0;
            foreach (var s in sites)
            for (int k = TerrainGenChunkSource.FIRST_FILL_SHELL; k <= TerrainGenChunkSource.MAX_SHELL; k++)
            for (int dy = -1; dy <= 0; dy++)
            {
                int3 coord = (s >> (7 + k)) + new int3(0, dy, 0);
                if (coord.y < 0) continue;
                C a = Gen(k, coord, 0), b = Gen(k, coord, 1), c = Gen(k, coord, 2);
                denseA[k] += a.Dense; denseC[k] += c.Dense; chunks[k]++;
                var ra = Rays(a); var rb = Rays(b); var rc = Rays(c);
                int bad = 0; string first = null;
                for (int i = 0; i < ra.Count; i++)
                {
                    if (ra[i] == long.MinValue) continue;
                    raysChecked++;
                    if (ra[i] != rb[i]) { bad++; if (first == null) first = $"ray {i} (view {i >> 14})"; }
                    if (rb[i] != rc[i])
                    {
                        raysWide++;
                        int hi = HitIndex(b, i);
                        bool mouth = hi >= 0 && c.M[hi] == Materials.Air && b.M[hi] != Materials.Air;
                        if (!mouth) raysWideBad++;
                    }
                }
                for (int i = 0; i < c.M.Length; i++) if (c.M[i] == Materials.Air && b.M[i] != Materials.Air) widenedVox++;
                Assert.AreEqual(0, bad, $"shell {k} chunk {coord}: {bad} rays see something else after the fill; first {first}");
            }
            lines.Add($"(i) {raysChecked} rays from outside (top + 4 sides) over {chunks[3] + chunks[4] + chunks[5] + chunks[6] + chunks[7]} chunks: " +
                      $"fill + flatten vs generator 0 differ; mouths on: {raysWide} rays differ, {raysWideBad} not at an opened mouth voxel ({widenedVox} voxels opened)");
            Assert.AreEqual(0, raysWideBad, "a ray changed by the mouth rule did not hit an opened mouth voxel");
            long tA = 0, tC = 0;
            for (int k = 3; k <= 7; k++)
            {
                lines.Add($"(iii) shell {k}: {chunks[k]} chunks, dense bricks {denseA[k]} -> {denseC[k]} ({(denseA[k] == 0 ? 0 : 100.0 * denseC[k] / denseA[k]):F1}%)");
                tA += denseA[k]; tC += denseC[k];
            }
            Assert.Less(tC, tA, "the fill dropped no dense brick");

            // (ii) cave entrances (the generator's opening stamps), shells 3-6.
            var ents = Entrances(4);
            var kinds = new Dictionary<byte, int>();
            foreach (var e in ents) kinds[e.Kind] = kinds.TryGetValue(e.Kind, out int q) ? q + 1 : 1;
            string ks = ""; foreach (var kv in kinds) ks += $" {kv.Value} {KindName(kv.Key)}";
            lines.Add($"(ii) {ents.Count} cave entrances on seed 42:{ks}");
            Assert.Greater(ents.Count, 0, "no cave entrance stamp found");
            int lost = 0;
            for (int k = 3; k <= 7; k++)
            {
                int before = 0, after = 0, gained = 0, hb = 0, ha = 0; string per = "";
                foreach (var e in ents)
                {
                    int nb = Holes(e, k, 0), na = Holes(e, k, 2);
                    bool sb = nb > 0, sa = na > 0;
                    hb += nb; ha += na;
                    if (sb) before++;
                    if (sa) after++;
                    if (sa && !sb) gained++;
                    if (sb && !sa) lost++;
                    per += sa ? (sb ? "Y" : "+") : (sb ? "!" : ".");
                }
                lines.Add($"(ii) shell {k} ({0.1f * (1 << k):F1} m voxels): entrances shown {before} -> {after} of {ents.Count} (+{gained} by the mouth rule)  [{per}]; hole voxels {hb} -> {ha}");
            }
            lines.Add("     per entrance: Y shown before and after, + only after (mouth rule), . neither, ! lost");
            Assert.AreEqual(0, lost, "a cave mouth that showed before the fill is gone after it");
        }
        finally
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "fill83_tests.txt"), lines);
            Debug.Log("[83]\n" + string.Join("\n", lines));
            _cache.Clear();
        }
    }

    [Test, Category("Slow")]
    public void MouthPrefilter_ChangesNothing_AtEntranceAndCliffChunks()
    {
        var chunks = new List<(int, int3)>();
        foreach (var e in Entrances(3))
            for (int k = 3; k <= 6; k++) chunks.Add((k, e.Site >> (7 + k)));
        foreach (var s in CliffSites(4))
            for (int k = 3; k <= 6; k++) chunks.Add((k, s >> (7 + k)));
        var ms = new double[4, 8]; var n = new int[8];
        int opened = 0; string bad = null;
        foreach (var (k, coord) in chunks)
        {
            if (_cache.ContainsKey((k, coord, 2))) continue;
            C a = Gen(k, coord, 0), on = Gen(k, coord, 2), off = Gen(k, coord, 3), b = Gen(k, coord, 1);
            ms[0, k] += a.Ms; ms[1, k] += b.Ms; ms[2, k] += on.Ms; ms[3, k] += off.Ms; n[k]++;
            int diff = 0;
            for (int i = 0; i < on.M.Length; i++) { if (on.M[i] != off.M[i]) diff++; if (on.M[i] != b.M[i]) opened++; }
            if (diff > 0 && bad == null) bad = $"shell {k} chunk {coord}: the prefilter changed {diff} voxels";
        }
        var lines = new List<string> { $"prefilter: {chunks.Count} chunks (entrance + cliff sites); voxels opened by the mouth rule {opened}" };
        for (int k = 3; k <= 6; k++)
            if (n[k] > 0)
                lines.Add($"  shell {k}: {n[k]} chunks, ms/chunk (Editor, Burst): generator {ms[0, k] / n[k]:F0}, fill no mouths {ms[1, k] / n[k]:F0}, " +
                          $"fill {ms[2, k] / n[k]:F0}, fill without the prefilter {ms[3, k] / n[k]:F0}");
        Debug.Log("[83] " + string.Join("\n", lines));
        System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells", "fill83_prefilter.txt"), lines);
        _cache.Clear();
        Assert.IsNull(bad, bad);
    }

    [Test]
    public void FarCrater_AcrossAChunkFace_StaysOpen()
    {
        var sites = CliffSites(1);
        Assert.AreEqual(1, sites.Count);
        int3 s = sites[0];
        const int K = 3;
        int3 cs = s >> (7 + K);
        int below = s.y - (cs.y << (7 + K));   // fine voxels from the ground to the chunk's floor
        int r = below + 300;                   // reaches 30 m into the chunk below
        var log = EditOpLog.InMemory(42u);
        log.Append(new EditOp(EditOpKind.Sphere, s, (ushort)r, Materials.Air));
        int half = (1 << (K - 1)) - 1, covered = 0, closed = 0;
        for (int dy = -1; dy <= 0; dy++)
        {
            int3 coord = cs + new int3(0, dy, 0);
            var c = Gen(K, coord, 2, log.Snapshot);
            int3 o = (coord << (7 + K)) + half;
            for (int y = 0; y < 128; y++) for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++)
            {
                if (!EditOpApply.Covers(log.Snapshot.Ops[0], o + (new int3(x, y, z) << K))) continue;
                covered++;
                if (c.M[Idx(x, y, z)] != Materials.Air) closed++;
            }
        }
        Debug.Log($"[83] far crater r {r * 0.1f:F0} m at {s}: {covered} covered samples in two shell-3 chunks, {closed} not air after the fill");
        Assert.Greater(covered, 0);
        Assert.AreEqual(0, closed, "the fill closed part of a far crater");
    }

    /// A sea column (>= 15 m deep) and a lake column (>= 3 m of still water above
    /// sea level) on seed 42: (site, water surface y).
    private static List<(string, int3)> WaterSites()
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        var list = new List<(string, int3)>(); bool sea = false, lake = false;
        int W = spec.worldSizeVoxels;
        for (int gz = W / 10; gz < W * 9 / 10 && !(sea && lake); gz += 500)
        for (int gx = W / 10; gx < W * 9 / 10 && !(sea && lake); gx += 500)
        {
            var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, gx + 0.5f, gz + 0.5f, 1);
            if (!sea && c.height < spec.seaLevelY - 150 && c.water == TerrainGen.ColumnSample.NO_WATER_Y)
            { sea = true; list.Add(("sea", new int3(gx, spec.seaLevelY, gz))); }
            else if (!lake && c.water != TerrainGen.ColumnSample.NO_WATER_Y && c.waterKind == 0 &&
                     c.water > spec.seaLevelY + 50 && c.water - c.height >= 30)
            { lake = true; list.Add(("lake", new int3(gx, c.water, gz))); }
        }
        return list;
    }

    [Test, Category("Slow")]
    public void SeaAndLake_SurfaceFluid_Unchanged()
    {
        var sites = WaterSites();
        Assert.AreEqual(2, sites.Count, "no sea or no lake site on seed 42");
        var lines = new List<string>();
        try
        {
            foreach (var (name, w) in sites)
            for (int k = TerrainGenChunkSource.FIRST_FILL_SHELL; k <= TerrainGenChunkSource.MAX_SHELL; k++)
            {
            int reachK = 0;
            for (int dy = -1; dy <= 0; dy++)
            {
                int3 coord = (w >> (7 + k)) + new int3(0, dy, 0);
                if (coord.y < 0) continue;
                C a = Gen(k, coord, 0), b = Gen(k, coord, 1), c = Gen(k, coord, 2);
                // Pass voxels of the generator reachable inside the chunk from one open by height.
                var seen = new bool[a.M.Length]; var stack = new Stack<int>();
                for (int y = 0; y < 128; y++) for (int z = 0; z < 128; z++) for (int x = 0; x < 128; x++)
                {
                    int i = Idx(x, y, z);
                    if (Pass[a.M[i]] && HeightOpen(a, x, y, z)) { seen[i] = true; stack.Push(i); }
                }
                while (stack.Count > 0)
                {
                    int i = stack.Pop(), x = i & 127, z = (i >> 7) & 127, y = i >> 14;
                    for (int d = 0; d < 6; d++)
                    {
                        int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0), nz = z + (d == 4 ? -1 : d == 5 ? 1 : 0);
                        if ((uint)nx >= 128 || (uint)ny >= 128 || (uint)nz >= 128) continue;
                        int j = Idx(nx, ny, nz);
                        if (seen[j] || !Pass[a.M[j]]) continue;
                        seen[j] = true; stack.Push(j);
                    }
                }
                int reach = 0, reachChanged = 0, enclosed = 0, enclosedFilled = 0;
                for (int i = 0; i < a.M.Length; i++)
                {
                    byte m = a.M[i];
                    if (!Pass[m] || m == Materials.Air) continue;
                    if (seen[i]) { reach++; if (c.M[i] != m) reachChanged++; }
                    else { enclosed++; if (c.M[i] != m) enclosedFilled++; }
                }
                var ra = Rays(a); var rb = Rays(b);
                int rays = 0, bad = 0;
                for (int i = 0; i < ra.Count; i++) { if (ra[i] == long.MinValue) continue; rays++; if (ra[i] != rb[i]) bad++; }
                lines.Add($"{name} shell {k} chunk {coord}: fluid voxels reachable {reach} (changed {reachChanged}), " +
                          $"enclosed {enclosed} (filled {enclosedFilled}); outside rays {rays}, differ {bad}");
                Assert.AreEqual(0, reachChanged, $"{name} shell {k} chunk {coord}: {reachChanged} reachable fluid voxels changed");
                Assert.AreEqual(0, bad, $"{name} shell {k} chunk {coord}: {bad} outside rays see something else");
                reachK += reach;
            }
            // The sea's surface can sit on a chunk floor (Y 5120): either chunk holds it.
            Assert.Greater(reachK, 0, $"{name} shell {k}: no surface fluid in the two test chunks");
            }
        }
        finally
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "fill83b_water.txt"), lines);
            Debug.Log("[83b]\n" + string.Join("\n", lines));
            _cache.Clear();
        }
    }

    [Test]
    public void Shells1and2_AreNotFilled()
    {
        var sites = CliffSites(1);
        for (int k = 1; k <= 2; k++)
        {
            int3 coord = (sites[0] >> (7 + k)) + new int3(0, -1, 0);
            C a = Gen(k, coord, 0), c = Gen(k, coord, 2);
            int diff = 0;
            for (int i = 0; i < a.M.Length; i++) if (a.M[i] != c.M[i]) diff++;
            Assert.AreEqual(0, diff, $"shell {k}: the fill changed {diff} voxels");
        }
        _cache.Clear();
    }

    [Test]
    public void FillShell_AfterGeneration_IsGenerateShellChunkWithTheFill()
    {
        var sites = CliffSites(1);
        int3 coord = (sites[0] >> 10) + new int3(0, -1, 0);
        using (var a = new TerrainGenChunkSource.ShellChunk())
        using (var b = new TerrainGenChunkSource.ShellChunk())
        {
            _src.GenerateShellChunk(3, coord, a);
            _src.ShellFill = false;
            _src.GenerateShellChunk(3, coord, b);
            _src.FillShell(b);
            _src.ShellFill = true;
            int diff = 0;
            for (int i = 0; i < a.Mats.Length; i++) if (a.Mats[i] != b.Mats[i]) diff++;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
                if (a.BrickUniform[i] != b.BrickUniform[i] || (a.BrickUniform[i] && a.BrickMat[i] != b.BrickMat[i])) diff++;
            Assert.AreEqual(0, diff);
            // Brick records match the voxels.
            int badBricks = 0;
            for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
            {
                int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                byte f = a.Get(bx << 3, by << 3, bz << 3); bool uni = true;
                for (int v = 0; v < 512 && uni; v++)
                    if (a.Get((bx << 3) + (v & 7), (by << 3) + ((v >> 3) & 7), (bz << 3) + (v >> 6)) != f) uni = false;
                if (uni != a.BrickUniform[bi] || (uni && a.BrickMat[bi] != f)) badBricks++;
            }
            Assert.AreEqual(0, badBricks, "brick uniform flags / materials disagree with the voxels");
        }
    }

    // ------------------------------------------------------------------
    // Report (REPORTS=1): what every High shell NEEDS around seed 42's spawn
    // (77's fill.txt camera), every wanted chunk generated, caps ignored:
    // dense bricks and pages (non-uniform chunks) before -> after the fill.
    // Sizes ShellLayout's page caps (row 1: measured peak x 1.25).
    // ------------------------------------------------------------------
    private static float3 Spawn()
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 2000; i++)
        {
            float a = i * 2.39996f, r = 40f * i;
            float2 p = new float2(c + r * math.cos(a), c + r * math.sin(a));
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 200 || col.water > col.height) continue;
            return new float3(p.x * 0.1f, (col.height + 1) * 0.1f + 2f, p.y * 0.1f);
        }
        return new float3(c * 0.1f, (spec.seaLevelY + 800) * 0.1f, c * 0.1f);
    }

    [Test, Category("Slow"), Timeout(3600000)]
    public void ShellNeed_AtSpawn_Report()
    {
        if (System.Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var L = ShellLayout.From(QualityPreset.High);
        float3 cam = Spawn();
        var lines = new List<string> { $"camera {cam} m (77's spawn), preset High, every wanted chunk generated (caps ignored)",
            "  A = the generator; C = task 83's fill (fluids kept); D = C + enclosed FLUIDS filled too (task 83b, the default)" };
        var s = new VoxelEngine.Mirror.ShellStreamer(L, _src, 1, synchronous: true) { SyncChunksPerUpdate = 0 };
        var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Logs", "Shells");
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            s.Update(cam, 1 << 30);
            using (var sc = new TerrainGenChunkSource.ShellChunk())
            using (var scC = new TerrainGenChunkSource.ShellChunk())
            using (var scD = new TerrainGenChunkSource.ShellChunk())
            for (int k = 1; k < L.ShellCount; k++)
            {
                s.Box(k, out int3 lo, out int3 hi, out _);
                long dA = 0, dC = 0, dD = 0, cFluid = 0, cAir = 0, cSkin = 0;
                int wanted = 0, air = 0, pA = 0, pC = 0, pD = 0, pageOnlyC = 0, filled = 0;
                var perCol = new Dictionary<int2, int>();
                var t = System.Diagnostics.Stopwatch.StartNew();
                double fillMs = 0;
                for (int z = lo.z; z <= hi.z; z++) for (int y = lo.y; y <= hi.y; y++) for (int x = lo.x; x <= hi.x; x++)
                {
                    var c = new int3(x, y, z);
                    if (!s.Wanted(k, c)) continue;
                    wanted++;
                    _src.ShellFill = false;
                    _src.GenerateShellChunk(k, c, sc);
                    _src.ShellFill = true;
                    if (sc.IsUniformAir) { air++; continue; }
                    VoxelEngine.Mirror.ShellStore.UploadBytes(sc, out int a0, out bool uA, out _);
                    dA += a0; if (!uA) pA++;
                    scC.CopyFrom(sc); scD.CopyFrom(sc);
                    if (TerrainGenChunkSource.ShellFills(k))
                    {
                        var tf = System.Diagnostics.Stopwatch.StartNew();
                        _src.ShellFillFluids = false; _src.FillShell(scC); filled++;
                        fillMs += tf.Elapsed.TotalMilliseconds;
                        _src.ShellFillFluids = true; _src.FillShell(scD);
                    }
                    VoxelEngine.Mirror.ShellStore.UploadBytes(scC, out int c0, out bool uC, out _);
                    VoxelEngine.Mirror.ShellStore.UploadBytes(scD, out int e0, out bool uD, out _);
                    dC += c0; dD += e0; if (!uD) pD++;
                    if (!uC)
                    {
                        pC++; if (c0 == 0) pageOnlyC++;
                        var key = new int2(x, z); perCol[key] = perCol.TryGetValue(key, out int q) ? q + 1 : 1;
                        for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
                        {
                            if (scC.BrickUniform[bi]) continue;
                            int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8; bool fl = false, ai = false;
                            for (int v = 0; v < 512; v++)
                            {
                                byte m = scC.Get((bx << 3) + (v & 7), (by << 3) + ((v >> 3) & 7), (bz << 3) + (v >> 6));
                                if (m == Materials.Air) ai = true; else if (Pass[m]) fl = true;
                            }
                            if (fl) cFluid++; else if (ai) cAir++; else cSkin++;
                        }
                    }
                }
                int maxCol = 0; foreach (var v in perCol.Values) maxCol = math.max(maxCol, v);
                var sh = L[k];
                lines.Add($"shell {k} ({sh.VoxelM:F1} m): {wanted} wanted chunks ({air} uniform air), {sh.Columns} columns, {t.Elapsed.TotalSeconds:F0} s" +
                          (filled > 0 ? $", fill {fillMs / filled:F1} ms/chunk (Editor)" : ""));
                lines.Add($"    dense bricks A {dA} -> C {dC} (D {dD}); est. {sh.EstBricks}, cap {sh.PoolCap}: C {(dC > sh.PoolCap ? "OVER" : "fits")}, D {(dD > sh.PoolCap ? "OVER" : "fits")}; C bodies {dC * 512 / 1e6:F1} MB");
                lines.Add($"    C's dense bricks: {cFluid} hold a fluid, {cAir} air (no fluid), {cSkin} all solid (a skin beside open air)");
                lines.Add($"    pages A {pA} -> C {pC} (D {pD}); cap {sh.PageCap}: C {(pC > sh.PageCap ? "OVER" : "fits")}, D {(pD > sh.PageCap ? "OVER" : "fits")}; " +
                          $"C {pC / (double)sh.Columns:F2}/column, max {maxCol} in one column, {pageOnlyC} chunks with no dense brick");
                System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "need83b.txt"), lines);
            }
        }
        finally
        {
            _src.ShellFill = true; _src.ShellFillFluids = true;
            s.Dispose();
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "need83b.txt"), lines);
            Debug.Log("[83] need\n" + string.Join("\n", lines));
        }
    }
}
