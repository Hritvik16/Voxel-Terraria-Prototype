// Assets/CoreEngine/Tests/ShellMarchCacheTests.cs
//
// Task 93b4: ShellMarch's one-entry READ CACHES (Raymarch.compute SHELL_CACHE_ON,
// ShellRayReference.March's useCache) change no result. Resolve's answer is kept
// per (distance shell kd, kd's chunk) unless that chunk is in kd's hole, and the
// brick handle per (shell, brick). A synthetic world that exercises every path
// the caches touch: the High layout's real bounds, reaches and holes; per shell
// a rolling height field with cave cells, so chunks are UNIFORM air / solid or
// PAGED with uniform and DENSE bricks; some chunks of shells 1-3 absent (read
// from a coarser shell); a tier-0 window with held and missing columns. Every
// primary ray (sky, horizon, ground, steep) and the shadow ray from each shell
// hit must give the same Result with the caches on and off, field for field,
// and the caches must save reads.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Mirror;

public class ShellMarchCacheTests
{
    const int GROUND = 16384;
    static readonly float3 Cam = new float3(40000.5f, GROUND + 140.5f, 40000.5f);

    sealed class TerrainView : ShellRayReference.IView
    {
        public readonly ShellLayout L;
        readonly Dictionary<int, (int, int3)> _pages = new Dictionary<int, (int, int3)>();
        readonly Dictionary<(int, int3), int> _pageOf = new Dictionary<(int, int3), int>();
        readonly Dictionary<int, (int, int3)> _bodies = new Dictionary<int, (int, int3)>();
        readonly Dictionary<(int, int3), int> _bodyOf = new Dictionary<(int, int3), int>();
        public TerrainView(ShellLayout l) { L = l; }
        public int ShellCount => L.ShellCount;
        public float OuterVoxels(int k) => L[k].OuterM * 10f;
        int3 CamChunk(int k) => (int3)math.floor(Cam) >> (7 + k);
        public bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole)
        {
            cc = CamChunk(k); hole = L[k].Hole;
            lo = cc - L[k].Reach; hi = cc + L[k].Reach;
            return true;
        }

        static uint Hash(int3 c, int k)
        {
            uint h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663) ^ (uint)(c.z * 83492791) ^ (uint)(k * 2654435761u);
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return h;
        }

        /// Ground top in voxels at column (x, z): +-30 m rolls and 8 m ripples.
        static float Height(float x, float z) =>
            GROUND + 300f * math.sin(x / 2000f) * math.cos(z / 1700f) + 80f * math.sin(x / 310f + z / 170f);

        const float BLOB_TOP = GROUND + 2600f;   // sparse floating cells 20-250 m up (none above)

        /// Shell k's cell (2^k voxels) at cell coords c: solid below the ground at its
        /// centre, except one cell in nine (caves, so dense bricks have air inside);
        /// 20-250 m above it, in one chunk in two, sparse single solid cells (one in
        /// 1,024: about two bricks in five dense), so the shells' hole corners up in the
        /// air hold paged chunks that differ chunk to chunk (a stale cached entry shows).
        byte Cell(int k, int3 c)
        {
            float s = 1 << k;
            float3 p = ((float3)c + 0.5f) * s;
            if (p.y >= Height(p.x, p.z))
            {
                if (p.y < GROUND + 200f || p.y > BLOB_TOP - 100f) return 0;
                if ((Hash(c >> 7, k + 70) & 1u) != 0u) return 0;   // this chunk's sky is empty
                return Hash(c, k + 50) % 1024u == 0u ? (byte)(10 + k) : (byte)0;
            }
            return Hash(c, k) % 9u == 0u ? (byte)0 : (byte)(10 + k);
        }

        /// 0 air, 2 mixed over a span of 2^span cells of shell k starting at cell c0.
        int SpanKind(int k, int3 c0, int cells)
        {
            float s = 1 << k;
            float x0 = c0.x * s, z0 = c0.z * s, w = cells * s;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i <= 4; i++)
            for (int j = 0; j <= 4; j++)
            {
                float h = Height(x0 + w * i / 4f, z0 + w * j / 4f);
                lo = math.min(lo, h); hi = math.max(hi, h);
            }
            float y0 = c0.y * s;
            return y0 >= math.max(hi + 40f * s, BLOB_TOP) ? 0 : 2;   // caves and floating cells: every chunk below is paged
        }

        public uint Dir(int k, int3 c)
        {
            // Some chunks of shells 1-3 absent (refused / not streamed): read from a coarser shell.
            if (k <= 3 && Hash(c, k + 100) % 11u == 0u) return ShellStore.ABSENT;
            int kind = SpanKind(k, c << 7, 128);
            if (kind == 0) return ShellStore.UNIFORM;
            if (!_pageOf.TryGetValue((k, c), out int page))
            {
                page = _pages.Count; _pages[page] = (k, c); _pageOf[(k, c)] = page;
            }
            return ShellStore.PAGE | (uint)page;
        }

        readonly Dictionary<(int, int3), uint> _handle = new Dictionary<(int, int3), uint>();
        public uint Page(int k, int page, int lb)
        {
            var (pk, c) = _pages[page];
            Assert.AreEqual(k, pk, "page read with the wrong shell");
            int3 bq = (c << 4) + new int3(lb & 15, (lb >> 4) & 15, lb >> 8);
            if (_handle.TryGetValue((k, bq), out uint known)) return known;
            uint hdl = PageUncached(k, c, lb);
            _handle[(k, bq)] = hdl;
            return hdl;
        }

        uint PageUncached(int k, int3 c, int lb)
        {
            int3 b = new int3(lb & 15, (lb >> 4) & 15, lb >> 8);
            int3 brick = (c << 4) + b;
            int3 cell0 = brick << 3;
            int first = -1;
            for (int z = 0; z < 8; z++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                int m = Cell(k, cell0 + new int3(x, y, z));
                if (first < 0) first = m; else if (first != m) first = -2;
            }
            if (first >= 0) return (uint)first;   // uniform brick (air or one solid)
            if (!_bodyOf.TryGetValue((k, brick), out int body))
            {
                body = _bodies.Count; _bodies[body] = (k, brick); _bodyOf[(k, brick)] = body;
            }
            return ShellStore.DENSE | (uint)body;
        }

        public byte Voxel(int k, int body, int vIdx)
        {
            var (bk, brick) = _bodies[body];
            Assert.AreEqual(k, bk, "body read with the wrong shell");
            return Cell(k, (brick << 3) + new int3(vIdx & 7, (vIdx >> 3) & 7, vIdx >> 6));
        }

        // A tier-0 window of 9 x 9 chunks round the camera, ground layers only; one column in 13 not resident (never the camera's).
        bool InWindow(int3 voxel, out bool resident)
        {
            int3 c = voxel >> 7, cc = (int3)math.floor(Cam) >> 7;
            resident = Hash(new int3(c.x, 0, c.z), 7) % 13u != 0u || (c.x == cc.x && c.z == cc.z);
            return math.abs(c.x - cc.x) <= 4 && math.abs(c.z - cc.z) <= 4 && voxel.y >= GROUND - 512 && voxel.y < GROUND + 512;
        }
        public bool Tier0Missing(int3 voxel) => InWindow(voxel, out bool res) && !res;
        public bool Tier0Holds(int3 voxel) => InWindow(voxel, out bool res) && res;
    }

    static void AssertSame(ShellRayReference.Result a, ShellRayReference.Result b, string what)
    {
        Assert.AreEqual(a.End, b.End, what + ": end");
        Assert.AreEqual(a.Shell, b.Shell, what + ": shell");
        Assert.AreEqual(a.T, b.T, what + ": t (exact)");
        Assert.AreEqual(a.Voxel, b.Voxel, what + ": voxel");
        Assert.AreEqual(a.Normal, b.Normal, what + ": normal");
        Assert.AreEqual(a.Mat, b.Mat, what + ": mat");
        Assert.AreEqual(a.Iters, b.Iters, what + ": iterations");
        Assert.AreEqual(a.InsideStart, b.InsideStart, what + ": inside");
    }

    /// The ray set (task 93b5's ShellAirMaskTests reuses it): 48 x 40 directions
    /// (sky, horizon, ground, steep) plus rays through each shell's hole-corner tips.
    internal static List<float3> RaySet(ShellLayout L, float3 cam, out int tipRays)
    {
        var dirs = new List<float3>();
        for (int i = 0; i < 48; i++)
        for (int j = 0; j < 40; j++)
        {
            float az = (i + 0.37f) * (2f * math.PI / 48f);
            float el = math.radians(-60f + j * 2.75f);   // -60 .. +47 deg: ground, grazing, horizon, sky
            dirs.Add(math.normalize(new float3(math.cos(az) * math.cos(el), math.sin(el), math.sin(az) * math.cos(el))));
        }
        // The tips of each shell's hole corners: there the distance (> the finer
        // shell's outer bound) asks for shell k but the chunk is in k's hole, so the
        // finer shells decide chunk by chunk -- the one answer the resolve cache must
        // not keep for the whole chunk. Rays through a grid of points in each upper
        // corner chunk that lie beyond outer_(k-1).
        tipRays = 0;
        for (int k = 2; k < L.ShellCount; k++)
        {
            int hole = L[k].Hole;
            if (hole < 1) continue;
            int3 cc = (int3)math.floor(cam) >> (7 + k);
            float size = 128 << k, inner = L[k - 1].OuterM * 10f;
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float3 c0 = (float3)(cc + new int3(sx * hole, hole, sz * hole)) * size;
                for (int a = 0; a < 8; a++) for (int b = 0; b < 8; b++) for (int q = 0; q < 8; q++)
                {
                    float3 pt = c0 + (new float3(a, b, q) + 0.5f) / 8f * size;
                    if (math.length(pt - cam) <= inner) continue;
                    dirs.Add(math.normalize(pt - cam)); tipRays++;
                }
            }
        }
        return dirs;
    }

    [Test]
    public void CachesChangeNoPrimaryOrShadowResult()
    {
        var v = new TerrainView(ShellLayout.From(QualityPreset.High));
        var sun = math.normalize(new float3(0.35f, 0.8f, -0.2f));
        const float tTier0 = 640f;   // tier 0 marched 64 m and saw air
        int rays = 0, hits = 0, shadows = 0, shadowHits = 0, absent = 0, past = 0, cap = 0;
        long readsOn = 0, readsOff = 0;
        var shells = new HashSet<int>();
        var dirs = RaySet(v.L, Cam, out int tipRays);
        Assert.Greater(tipRays, 50, "the hole corners reach past the finer shell's bound");
        foreach (var dir in dirs)
        {
            float az = math.atan2(dir.z, dir.x), el = math.asin(dir.y);
            var on = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, 384, tTier0, useCache: true);
            var off = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, 384, tTier0, useCache: false);
            AssertSame(on, off, $"primary az {math.degrees(az):F1} el {math.degrees(el):F1}");
            rays++;
            readsOn += on.DirReads + on.PageReads; readsOff += off.DirReads + off.PageReads;
            if (on.End == ShellRayReference.END_ABSENT) absent++;
            if (on.End == ShellRayReference.END_PAST_LAST) past++;
            if (on.End == ShellRayReference.END_CAP) cap++;
            if (on.End != ShellRayReference.END_HIT) continue;
            hits++; shells.Add(on.Shell);
            // The shell-hit sun shadow, as CSMainBody starts it.
            float3 so = Cam + dir * on.T + (float3)on.Normal * 0.01f;
            var sOn = ShellRayReference.March(v, so, sun, 0f, true, Cam, 192, 0f, useCache: true);
            var sOff = ShellRayReference.March(v, so, sun, 0f, true, Cam, 192, 0f, useCache: false);
            AssertSame(sOn, sOff, $"shadow of az {math.degrees(az):F1} el {math.degrees(el):F1}");
            shadows++;
            if (sOn.End == ShellRayReference.END_HIT) shadowHits++;
            readsOn += sOn.DirReads + sOn.PageReads; readsOff += sOff.DirReads + sOff.PageReads;
        }
        UnityEngine.Debug.Log($"[ShellMarchCache] {rays} primary rays: {hits} hits in shells {string.Join(",", shells)}, absent {absent}, " +
                              $"past {past}, cap {cap}; {shadows} shadow rays, {shadowHits} shadowed; reads with caches {readsOn:N0} vs {readsOff:N0} " +
                              $"({100.0 * readsOn / math.max(1, readsOff):F0}%)");
        Assert.Greater(hits, 200, "rays hit the ground");
        Assert.GreaterOrEqual(shells.Count, 4, "hits in several shells");
        Assert.Greater(absent + past, 100, "sky rays leave the shells");
        Assert.Greater(shadowHits, 10, "some shadow rays are blocked");
        Assert.Less(readsOn, readsOff * 0.8, "the caches save reads");
    }

    /// Task 134 (R6): the play kernel asks ShellByDistance only when the distance leaves the
    /// band its last answer holds for (bandCache). Every field equal, read counters too, for the
    /// primary rays, the shell-hit sun rays and task 133's near sun rays (a tier-0 sun ray that
    /// left the window: start near the camera, t > 0, distance from the camera).
    [Test]
    public void BandCacheChangesNoResult()
    {
        var v = new TerrainView(ShellLayout.From(QualityPreset.High));
        var sun = math.normalize(new float3(0.35f, 0.8f, -0.2f));
        var lowSun = math.normalize(new float3(-0.9f, 0.18f, 0.3f));
        const float tTier0 = 640f;
        int rays = 0, hits = 0, near = 0;
        var shells = new HashSet<int>();
        foreach (var dir in RaySet(v.L, Cam, out _))
        {
            float az = math.atan2(dir.z, dir.x), el = math.asin(dir.y);
            var on = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, 384, tTier0, bandCache: true);
            var off = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, 384, tTier0);
            AssertSame(on, off, $"primary az {math.degrees(az):F1} el {math.degrees(el):F1}");
            Assert.AreEqual(off.DirReads, on.DirReads, "dir reads"); Assert.AreEqual(off.PageReads, on.PageReads, "page reads");
            rays++;
            if (on.End == ShellRayReference.END_HIT)
            {
                hits++; shells.Add(on.Shell);
                float3 so = Cam + dir * on.T + (float3)on.Normal * 0.01f;
                foreach (var s in new[] { sun, lowSun })
                    AssertSame(ShellRayReference.March(v, so, s, 0f, true, Cam, 192, 0f, bandCache: true),
                               ShellRayReference.March(v, so, s, 0f, true, Cam, 192, 0f), $"shadow of az {math.degrees(az):F1}");
            }
            // Near sun rays: from a point within 100 m of the camera, leaving at t = 60-1,200 voxels.
            float3 o = Cam + dir * (37f + (rays % 9) * 100f);
            float tExit = 600f + (rays % 7) * 100f;
            foreach (var s in new[] { sun, lowSun })
            {
                AssertSame(ShellRayReference.March(v, o, s, tExit, true, Cam, 192, 0f, bandCache: true),
                           ShellRayReference.March(v, o, s, tExit, true, Cam, 192, 0f), $"near sun ray {rays}");
                near++;
            }
        }
        UnityEngine.Debug.Log($"[ShellBandCache] {rays} primary rays ({hits} hits in shells {string.Join(",", shells)}), {near} near sun rays: identical");
        Assert.Greater(hits, 200, "rays hit the ground");
        Assert.GreaterOrEqual(shells.Count, 4, "hits in several shells");
    }
}
