// Assets/CoreEngine/Tests/ShellAirMaskTests.cs
//
// Task 93b5 (owner Q58 (a)): the shell AIR-BRICK MASK (ShellStore header; 64 bits
// per page, bit g = brick group g of 4x4x4 bricks holds a brick that is not
// uniform air) and ShellMarch's use of it (Raymarch.compute SHELL_MASK_ON,
// ShellRayReference.March's useMask: an empty group is crossed in ONE leap, exact
// because every shell-march leap's state depends on its exit t alone, task 93b5 (b)).
//   (1) The uploaded mask (ShellStore.ShadowMasks) equals a brute-force scan of
//       the uploaded page for real tg2 shell chunks: generated (no fill), op-redrawn
//       (Store replace: true) and filled; and a clear bit's group is air voxel for voxel.
//   (2) March with the mask on and off (both with the exit-plane leap, planeLeap,
//       which the mask implies) gives every Result field but the counters
//       equal on the ShellMarchCacheTests ray set (hole corners) + their shadows,
//       >= 3,000 rays, in a synthetic world whose pages are cheap to scan.
//   (3) A mutation (bits cleared on non-air groups) FAILS (2).
//   (4) ShellRayReference.PlaneLeap: one leap over a brick group ends exactly where the
//       brick leaps through it end (t, voxel, normal), on random and tie-prone rays.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class ShellAirMaskTests
{
    // ==================================================================
    // (1) The mask of real chunks
    // ==================================================================

    private static TerrainGenChunkSource _src;

    [OneTimeSetUp] public void BuildOnce() { if (_src == null) _src = new TerrainGenChunkSource(42u); }
    [OneTimeTearDown] public void TearDown() { if (_src != null) { _src.ShellFill = true; _src.Dispose(); } _src = null; }

    /// A dry land column near the island centre with its ground height (EditOpRedrawTests.Land).
    private static bool Land(float2 near, float spreadVoxels, out int3 site)
    {
        var spec = _src.Spec; var view = _src.Map.View; var o = _src.O;
        for (int i = 0; i < 400; i++)
        {
            float2 p = near + new float2((i % 20) - 10, (i / 20) - 10) * spreadVoxels;
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x, p.y, 1);
            if (col.height < spec.seaLevelY + 300 || col.water > col.height) continue;
            site = new int3((int)p.x, col.height, (int)p.y);
            return true;
        }
        site = default;
        return false;
    }

    /// Mask errors of the chunk the store holds at sc.Coord (0 = right): the uploaded
    /// mask vs a scan of the uploaded page, and every clear group air in sc.Mats.
    private static int CheckStored(ShellStore st, TerrainGenChunkSource.ShellChunk sc, ref int empty, ref int full, out string ex)
    {
        ex = null;
        uint e = st.DirectoryEntry(st.Slot(sc.Coord));
        Assert.AreNotEqual(0u, e & ShellStore.PAGE, $"shell {sc.Shell} chunk {sc.Coord} is not paged: pick another");
        int page = (int)(e & ~ShellStore.PAGE);
        ulong got = st.ShadowMasks[page * ShellStore.MASK_UINTS] | ((ulong)st.ShadowMasks[page * ShellStore.MASK_UINTS + 1] << 32);
        ulong want = 0ul;
        for (int bz = 0; bz < 16; bz++)
        for (int by = 0; by < 16; by++)
        for (int bx = 0; bx < 16; bx++)
            if (st.ShadowPages[page * EngineConfig.BRICKS_PER_CHUNK + ((bz << 8) | (by << 4) | bx)] != 0u)
                want |= 1ul << (((bz >> 2) << 4) | ((by >> 2) << 2) | (bx >> 2));
        int errors = 0;
        if (got != want) { errors++; ex = $"shell {sc.Shell} chunk {sc.Coord}: mask {got:X16}, page scan {want:X16}"; }
        // Task 116: the page's uploaded COLUMN TOPS equal a scan of the uploaded page too.
        for (int j = 0; j < 16; j++)
        {
            int top = 0;
            for (int bz = (j >> 2) << 2; bz < ((j >> 2) << 2) + 4; bz++)
            for (int bx = (j & 3) << 2; bx < ((j & 3) << 2) + 4; bx++)
            for (int by = 0; by < 16; by++)
                if (st.ShadowPages[page * EngineConfig.BRICKS_PER_CHUNK + ((bz << 8) | (by << 4) | bx)] != 0u) top = math.max(top, by + 1);
            int gotTop = ShellStore.ColumnTop(st.ShadowColTops, j, page * ShellStore.COLTOP_UINTS);
            if (gotTop != top) { errors++; ex ??= $"shell {sc.Shell} chunk {sc.Coord}: column {j} top {gotTop}, page scan {top}"; }
        }
        for (int g = 0; g < 64; g++)
        {
            if (((got >> g) & 1ul) != 0ul) { full++; continue; }
            empty++;
            int x0 = (g & 3) << 5, y0 = ((g >> 2) & 3) << 5, z0 = (g >> 4) << 5;
            for (int z = z0; z < z0 + 32; z++)
            for (int y = y0; y < y0 + 32; y++)
            for (int x = x0; x < x0 + 32; x++)
                if (sc.Mats[TerrainGenChunkSource.Index(x, y, z)] != Materials.Air)
                {
                    errors++;
                    ex ??= $"shell {sc.Shell} chunk {sc.Coord}: group {g} clear but voxel ({x},{y},{z}) is {sc.Mats[TerrainGenChunkSource.Index(x, y, z)]}";
                    goto nextGroup;
                }
            nextGroup:;
        }
        return errors;
    }

    [Test]
    public void Mask_MatchesThePage_Generated_OpRedrawn_Filled()
    {
        Assert.IsTrue(Land(new float2(_src.Spec.worldSizeVoxels * 0.5f), 1500f, out int3 s));
        ShellStore.ShadowUploads = true;
        var a = new TerrainGenChunkSource.ShellChunk();
        var b = new TerrainGenChunkSource.ShellChunk();
        int chunks = 0, empty = 0, full = 0;
        try
        {
            for (int k = 1; k <= 3; k++)
            {
                int3 cs = s >> (7 + k);
                foreach (bool fill in new[] { false, true })
                {
                    _src.ShellFill = fill;
                    _src.GenerateShellChunk(k, cs, a);
                    var log = EditOpLog.InMemory(42u);
                    log.Append(new EditOp(EditOpKind.Sphere, s, 120, Materials.Air));
                    _src.GenerateShellChunk(k, cs, b, log.Snapshot);
                    using (var st = new ShellStore(k, new int3(8, 8, 8), a.DenseBricks + b.DenseBricks + 8, 4))
                    {
                        int big = 64 << 20;
                        // Generated (fill off) or filled (fill on).
                        Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(a, ref big));
                        Assert.IsTrue(st.FlushDirectory(ref big));
                        Assert.AreEqual(0, CheckStored(st, a, ref empty, ref full, out string ex), (fill ? "filled: " : "generated: ") + ex);
                        // Op-redrawn: the gap-free re-store (task 82) writes a new page and its mask.
                        Assert.AreEqual(ShellStore.StoreResult.Stored, st.Store(b, ref big, replace: true));
                        Assert.IsTrue(st.FlushDirectory(ref big));
                        Assert.AreEqual(1, st.Replaced);
                        Assert.AreEqual(0, CheckStored(st, b, ref empty, ref full, out ex), "op-redrawn: " + ex);
                        chunks += 2;
                    }
                }
            }
        }
        finally
        {
            ShellStore.ShadowUploads = false;
            _src.ShellFill = true;
            a.Dispose(); b.Dispose();
        }
        Debug.Log($"[93b5] mask vs page scan: {chunks} stored chunks (shells 1-3, generated / filled, each op-redrawn), groups empty {empty}, non-empty {full}");
        Assert.Greater(empty, 0, "some groups are empty (the mask has something to leap)");
        Assert.Greater(full, 0, "some groups hold terrain");
    }

    // ==================================================================
    // (2), (3) The march with the mask on and off
    // ==================================================================

    internal const int GROUND = 16384;
    internal static readonly float3 Cam = new float3(40000.5f, GROUND + 140.5f, 40000.5f);

    /// ShellMarchCacheTests' world at brick granularity, so a page's 4,096 handles
    /// are cheap and its mask is a scan of them (ShellStore.GroupMask, the store's
    /// own builder): the High layout's bounds, reaches and holes; per brick of shell
    /// k, by the brick's centre column: under the rolling ground solid (one in seven
    /// dense: caves), across it dense, above it air, except 20-250 m up in one chunk
    /// in two, where one brick in 24 holds one floating cell; chunks above that
    /// UNIFORM air; some chunks of shells 1-3 absent; a tier-0 window with held and
    /// missing columns over its layers [GROUND - 512, GROUND + 512).
    internal sealed class BrickView : ShellRayReference.IColumnView
    {
        public readonly ShellLayout L;
        /// (3): clear the bits of the non-empty groups g with (g & 3) == 1.
        public bool Mutate;
        /// Task 116 (ShellColumnLeapTests): every non-zero column top one brick too low.
        public bool MutateTops;
        readonly Dictionary<int, uint[]> _tops = new Dictionary<int, uint[]>();
        readonly Dictionary<int, (int, int3)> _pages = new Dictionary<int, (int, int3)>();
        readonly Dictionary<(int, int3), int> _pageOf = new Dictionary<(int, int3), int>();
        readonly Dictionary<int, (int, int3, int)> _bodies = new Dictionary<int, (int, int3, int)>();
        readonly Dictionary<(int, int3), int> _bodyOf = new Dictionary<(int, int3), int>();
        readonly Dictionary<int, ulong> _mask = new Dictionary<int, ulong>();
        readonly uint[] _handles = new uint[EngineConfig.BRICKS_PER_CHUNK];
        public BrickView(ShellLayout l) { L = l; }
        public int ShellCount => L.ShellCount;
        public float OuterVoxels(int k) => L[k].OuterM * 10f;
        public bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole)
        {
            cc = (int3)math.floor(Cam) >> (7 + k); hole = L[k].Hole;
            lo = cc - L[k].Reach; hi = cc + L[k].Reach;
            return true;
        }

        static uint Hash(int3 c, int k)
        {
            uint h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663) ^ (uint)(c.z * 83492791) ^ (uint)(k * 2654435761u);
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return h;
        }
        static float Height(float x, float z) =>
            GROUND + 300f * math.sin(x / 2000f) * math.cos(z / 1700f) + 80f * math.sin(x / 310f + z / 170f);
        const float BLOB_TOP = GROUND + 2600f;
        const int KIND_CAVE = 0, KIND_SURFACE = 1, KIND_FLOAT = 2;

        uint Dense(int k, int3 b, int kind)
        {
            if (!_bodyOf.TryGetValue((k, b), out int body))
            {
                body = _bodies.Count; _bodies[body] = (k, b, kind); _bodyOf[(k, b)] = body;
            }
            return ShellStore.DENSE | (uint)body;
        }

        uint Handle(int k, int3 b)
        {
            float bs = 8 << k;
            float3 lo = (float3)b * bs;
            float h = Height(lo.x + bs * 0.5f, lo.z + bs * 0.5f);
            if (lo.y + bs <= h) return Hash(b, k) % 7u == 0u ? Dense(k, b, KIND_CAVE) : (uint)(10 + k);
            if (lo.y < h) return Dense(k, b, KIND_SURFACE);
            float cy = lo.y + bs * 0.5f;
            if (cy < GROUND + 200f || cy > BLOB_TOP - 100f) return 0u;
            if ((Hash(b >> 4, k + 70) & 1u) != 0u) return 0u;   // this chunk's sky is empty
            return Hash(b, k + 50) % 24u == 0u ? Dense(k, b, KIND_FLOAT) : 0u;
        }

        public uint Dir(int k, int3 c)
        {
            if (k <= 3 && Hash(c, k + 100) % 11u == 0u) return ShellStore.ABSENT;
            if ((float)(c.y << (7 + k)) >= BLOB_TOP) return ShellStore.UNIFORM;   // every brick above is air
            if (!_pageOf.TryGetValue((k, c), out int page))
            {
                page = _pages.Count; _pages[page] = (k, c); _pageOf[(k, c)] = page;
            }
            return ShellStore.PAGE | (uint)page;
        }

        public uint Page(int k, int page, int lb)
        {
            var (pk, c) = _pages[page];
            Assert.AreEqual(k, pk, "page read with the wrong shell");
            return Handle(k, (c << 4) + new int3(lb & 15, (lb >> 4) & 15, lb >> 8));
        }

        public byte Voxel(int k, int body, int vIdx)
        {
            var (bk, b, kind) = _bodies[body];
            Assert.AreEqual(k, bk, "body read with the wrong shell");
            int3 c = (b << 3) + new int3(vIdx & 7, (vIdx >> 3) & 7, vIdx >> 6);
            byte solid = (byte)(10 + k);
            if (kind == KIND_FLOAT) return Hash(b, k + 60) % 512u == (uint)vIdx ? solid : (byte)0;
            if (Hash(c, k) % 9u == 0u) return 0;
            if (kind == KIND_CAVE) return solid;
            float s = 1 << k;
            float3 p = ((float3)c + 0.5f) * s;
            return p.y < Height(p.x, p.z) ? solid : (byte)0;
        }

        public ulong GroupMask(int k, int page)
        {
            if (!_mask.TryGetValue(page, out ulong m))
            {
                for (int lb = 0; lb < _handles.Length; lb++) _handles[lb] = Page(k, page, lb);
                m = ShellStore.GroupMask(_handles);
                _mask[page] = m;
            }
            if (Mutate) for (int g = 1; g < 64; g += 4) m &= ~(1ul << g);
            return m;
        }

        /// Task 116: the page's column tops (ShellStore.ColumnTops, the store's own builder).
        public int ColumnTop(int k, int page, int j)
        {
            if (!_tops.TryGetValue(page, out uint[] tops))
            {
                for (int lb = 0; lb < _handles.Length; lb++) _handles[lb] = Page(k, page, lb);
                tops = new uint[ShellStore.COLTOP_UINTS];
                ShellStore.ColumnTops(_handles, tops);
                _tops[page] = tops;
            }
            int top = ShellStore.ColumnTop(tops, j);
            return MutateTops && top > 0 ? top - 1 : top;
        }

        bool InWindow(int3 voxel, out bool resident)
        {
            int3 c = voxel >> 7, cc = (int3)math.floor(Cam) >> 7;
            resident = Hash(new int3(c.x, 0, c.z), 7) % 13u != 0u || (c.x == cc.x && c.z == cc.z);
            return math.abs(c.x - cc.x) <= 4 && math.abs(c.z - cc.z) <= 4 && voxel.y >= Tier0FloorY && voxel.y < Tier0CeilY;
        }
        public bool Tier0Missing(int3 voxel) => InWindow(voxel, out bool res) && !res;
        public bool Tier0Holds(int3 voxel) => InWindow(voxel, out bool res) && res;
        public int Tier0FloorY => GROUND - 512;
        public int Tier0CeilY => GROUND + 512;
    }

    internal static bool Same(ShellRayReference.Result a, ShellRayReference.Result b) =>
        a.End == b.End && a.Shell == b.Shell && a.T == b.T && a.Voxel.Equals(b.Voxel) && a.Normal.Equals(b.Normal)
        && a.Mat == b.Mat && a.InsideStart == b.InsideStart;

    internal static string Show(ShellRayReference.Result r) =>
        $"end {r.End} shell {r.Shell} t {r.T:R} voxel {r.Voxel} normal {r.Normal} mat {r.Mat} inside {r.InsideStart} iters {r.Iters}";

    /// March every ray (and the shadow of every hit) with the mask on and off.
    /// Returns how many differ; with `assertSame` the first difference fails the test.
    static int CompareMask(BrickView v, bool assertSame, out string report)
    {
        var sun = math.normalize(new float3(0.35f, 0.8f, -0.2f));
        const float tTier0 = 640f;   // tier 0 marched 64 m and saw air
        const int CAP = 4096, SHADOW_CAP = 2048;   // no ray ends at the cap: every field compares
        int rays = 0, hits = 0, shadows = 0, shadowHits = 0, absent = 0, past = 0, capOff = 0, differ = 0;
        long itersOn = 0, itersOff = 0, groups = 0;
        var shells = new HashSet<int>();
        var dirs = ShellMarchCacheTests.RaySet(v.L, Cam, out int tipRays);
        Assert.Greater(tipRays, 50, "the hole corners reach past the finer shell's bound");
        foreach (var dir in dirs)
        {
            var on = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, CAP, tTier0, useMask: true);
            var off = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, CAP, tTier0, useMask: false, planeLeap: true);
            rays++;
            itersOn += on.Iters; itersOff += off.Iters; groups += on.MaskGroups;
            if (off.End == ShellRayReference.END_CAP) capOff++;
            if (!Same(on, off))
            {
                differ++;
                if (assertSame) Assert.Fail($"primary {dir}: mask on {Show(on)} / off {Show(off)}");
            }
            if (off.End == ShellRayReference.END_ABSENT) absent++;
            if (off.End == ShellRayReference.END_PAST_LAST) past++;
            if (off.End != ShellRayReference.END_HIT) continue;
            hits++; shells.Add(off.Shell);
            float3 so = Cam + dir * off.T + (float3)off.Normal * 0.01f;
            var sOn = ShellRayReference.March(v, so, sun, 0f, true, Cam, SHADOW_CAP, 0f, useMask: true);
            var sOff = ShellRayReference.March(v, so, sun, 0f, true, Cam, SHADOW_CAP, 0f, useMask: false, planeLeap: true);
            shadows++;
            itersOn += sOn.Iters; itersOff += sOff.Iters; groups += sOn.MaskGroups;
            if (sOff.End == ShellRayReference.END_CAP) capOff++;
            if (!Same(sOn, sOff))
            {
                differ++;
                if (assertSame) Assert.Fail($"shadow of {dir}: mask on {Show(sOn)} / off {Show(sOff)}");
            }
            if (sOff.End == ShellRayReference.END_HIT) shadowHits++;
        }
        report = $"{rays} primary rays ({tipRays} through hole corners): {hits} hits in shells {string.Join(",", shells)}, absent {absent}, " +
                 $"past {past}; {shadows} shadow rays, {shadowHits} shadowed; capped (mask off) {capOff}; differ {differ}; " +
                 $"iterations mask on {itersOn:N0} vs off {itersOff:N0} ({100.0 * itersOn / math.max(1, itersOff):F0}%), " +
                 $"empty groups crossed in one leap {groups:N0}";
        if (assertSame)
        {
            Assert.GreaterOrEqual(rays + shadows, 3000, "rays");
            Assert.AreEqual(0, capOff, "no ray ends at the cap (its fields would not compare)");
            Assert.Greater(hits, 200, "rays hit the ground");
            Assert.GreaterOrEqual(shells.Count, 4, "hits in several shells");
            Assert.Greater(absent + past, 100, "sky rays leave the shells");
            Assert.Greater(shadowHits, 10, "some shadow rays are blocked");
            Assert.Greater(groups, 0, "the mask leapt groups");
            Assert.Less(itersOn, itersOff, "the mask saves iterations");
        }
        return differ;
    }

    [Test]
    public void Mask_ChangesNoPrimaryOrShadowResult()
    {
        var v = new BrickView(ShellLayout.From(QualityPreset.High));
        Assert.AreEqual(0, CompareMask(v, true, out string report));
        Debug.Log("[93b5] mask on vs off: " + report);
    }

    [Test]
    public void Mask_Mutation_BitsClearedOnNonAirGroups_IsCaught()
    {
        var v = new BrickView(ShellLayout.From(QualityPreset.High)) { Mutate = true };
        int differ = CompareMask(v, false, out string report);
        Debug.Log("[93b5] MUTATED mask on vs off: " + report);
        Assert.Greater(differ, 0, "a mask that calls a non-air group empty must change some result");
    }

    // ==================================================================
    // (4) Leap is path-independent: one group leap = its brick leaps
    // ==================================================================

    /// From a cell inside a brick group of shell k (group = 4x4x4 bricks of 8 << k voxels),
    /// march brick by brick until the cell leaves the group, and compare with ONE leap over
    /// the group from the same state. Random directions and starts near tg2's coordinates,
    /// plus axis-parallel and diagonal directions (|d| equal on two or three axes: plane ties).
    [Test]
    public void Leap_OneGroupLeap_EndsWhereItsBrickLeapsEnd()
    {
        var rng = new Unity.Mathematics.Random(93005u);
        int rays = 0, ties = 0, multi = 0;
        for (int i = 0; i < 20000; i++)
        {
            float3 dir;
            int kind = i % 4;
            if (kind == 0) dir = rng.NextFloat3Direction();
            else if (kind == 1) dir = math.normalize(new float3(rng.NextBool() ? 1 : -1, rng.NextBool() ? 1 : -1, rng.NextBool() ? 1 : -1));
            else if (kind == 2) { var d = new float3(rng.NextBool() ? 1 : -1, rng.NextBool() ? 1 : -1, 0); dir = math.normalize(rng.NextBool() ? d.xzy : d); }
            else dir = math.normalize(new float3(rng.NextFloat(-1, 1), rng.NextBool() ? 1e-7f : -0.3f, rng.NextFloat(-1, 1)));
            dir = new float3(ShellRayReference.SafeAxis(dir.x), ShellRayReference.SafeAxis(dir.y), ShellRayReference.SafeAxis(dir.z));
            int k = 1 + i % 5;
            int gsz = 1 << (k + 5);
            // A start on a quarter-voxel grid; for the diagonal kinds its axes sit the same
            // distance from group planes, so the ray meets group edges and corners exactly.
            float sx = 40960f + rng.NextInt(-3000, 3000) + rng.NextInt(0, 4) * 0.25f;
            float3 start = kind == 1 || kind == 2
                ? new float3(sx, sx - 40960f + GROUND + gsz * rng.NextInt(-1, 2), sx + gsz * rng.NextInt(-3, 4))
                : new float3(sx, GROUND + rng.NextInt(-400, 800) + rng.NextInt(0, 4) * 0.25f,
                             40960f + rng.NextInt(-3000, 3000) + rng.NextInt(0, 4) * 0.25f);
            int3 step = (int3)math.sign(dir);
            float3 tDelta = math.abs(1f / dir);
            int3 voxel = (int3)math.floor(start);
            float3 tMax = ShellRayReference.ReseedTMax(voxel, start, tDelta, step);
            float t = 0f; int3 normal = int3.zero;
            // Walk a few random brick leaps first so the group is entered mid-path.
            int pre = rng.NextInt(0, 40);
            for (int j = 0; j < pre; j++)
                ShellRayReference.PlaneLeap(ref voxel, ref tMax, ref t, ref normal, tDelta, step, start, (voxel >> (k + 3)) << (k + 3), 1 << (k + 3));
            int gs = k + 5;
            int3 g0 = voxel >> gs;
            int3 bv = voxel, gv = voxel; float3 bm = tMax, gm = tMax; float bt = t, gt = t; int3 bn = normal, gn = normal;
            int leaps = 0;
            while (math.all((bv >> gs) == g0) && leaps < 64)
            {
                ShellRayReference.PlaneLeap(ref bv, ref bm, ref bt, ref bn, tDelta, step, start, (bv >> (k + 3)) << (k + 3), 1 << (k + 3));
                leaps++;
            }
            Assert.Less(leaps, 64, "the brick leaps leave the group");
            ShellRayReference.PlaneLeap(ref gv, ref gm, ref gt, ref gn, tDelta, step, start, (gv >> gs) << gs, 1 << gs);
            rays++;
            if (leaps > 1) multi++;
            if (math.csum(math.select(int3.zero, new int3(1), (bv >> gs) != g0)) > 1) ties++;
            if (bt != gt || !bv.Equals(gv) || !bn.Equals(gn) || !bm.Equals(gm))
                Assert.Fail($"ray {i} k {k} start {start} dir {dir}: {leaps} brick leaps -> t {bt:R} voxel {bv} normal {bn}; " +
                            $"one group leap -> t {gt:R} voxel {gv} normal {gn}");
        }
        Debug.Log($"[93b5] Leap path-independence: {rays} rays, {multi} crossed >1 brick, {ties} left the group across an edge/corner");
        Assert.Greater(multi, rays / 2, "most rays cross several bricks");
        Assert.Greater(ties, 20, "some rays leave the group through an edge or corner (ties)");
    }
}
