// Assets/CoreEngine/Tests/ShellHoleLeapTests.cs
//
// Task 137: the shells' HOLE LEAP (Raymarch.compute ShellHoleBox / SHELL_HOLELEAP_ON, the CPU twin
// ShellRayReference.March(holeLeap) / HoleBox). Inside shell 1's hole and outside tier 0's layers
// every iteration is the same tier-0-chunk leap; with the leap on, those iterations run alone (no
// distance / resolve / tier-0 tests). It must change nothing: every field equal, iterations too
// (so the step cap ends a ray at the same place), only the directory reads drop.
// The world: ShellMarchCacheTests' TerrainView (the High layout's bounds, reaches and holes, paged
// and uniform chunks, absent chunks, a tier-0 window with missing columns), with tier 0's layers
// cut to [ground - 25.6 m, ground + 20.3 m) (not chunk-aligned) so shell 1's hole reaches above the
// band, and an ADVERSARIAL PILLAR (owner's generality rule): a 300 m stone column 60-70 m from the
// camera, just outside shell 1's hole, in every shell's chunks (as an edit op redraws them), so near
// sun rays leaving the band ceiling cross the hole and must still be blocked by it.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class ShellHoleLeapTests
{
    const int GROUND = 16384;
    static readonly float3 Cam = new float3(40000.5f, GROUND + 140.5f, 40000.5f);
    const int FLOOR_Y = GROUND - 256, CEIL_Y = GROUND + 203;
    // The pillar: x [40600, 40700), z [39950, 40050), y [GROUND - 64, GROUND + 3000).
    static readonly int3 PillarLo = new int3(40600, GROUND - 64, 39950), PillarHi = new int3(40700, GROUND + 3000, 40050);

    sealed class HoleView : ShellRayReference.IMaskView
    {
        readonly ShellMarchCacheTests.TerrainView _t;
        public readonly bool Pillar;
        public HoleView(ShellMarchCacheTests.TerrainView t, bool pillar) { _t = t; Pillar = pillar; }
        public ShellLayout L => _t.L;
        public int ShellCount => _t.ShellCount;
        public float OuterVoxels(int k) => _t.OuterVoxels(k);
        public bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole) => _t.Box(k, out lo, out hi, out cc, out hole);
        public uint Dir(int k, int3 c)
        {
            if (Pillar)
            {
                int3 lo = c << (7 + k), hi = lo + (128 << k);
                if (math.all(lo < PillarHi) && math.all(hi > PillarLo)) return ShellStore.UNIFORM | Materials.Stone;
            }
            return _t.Dir(k, c);
        }
        public uint Page(int k, int page, int lb) => _t.Page(k, page, lb);
        public byte Voxel(int k, int body, int vIdx) => _t.Voxel(k, body, vIdx);
        bool InLayers(int3 v) => v.y >= FLOOR_Y && v.y < CEIL_Y;
        public bool Tier0Missing(int3 v) => InLayers(v) && _t.Tier0Missing(v);
        public bool Tier0Holds(int3 v) => InLayers(v) && _t.Tier0Holds(v);
        public ulong GroupMask(int k, int page) => throw new System.InvalidOperationException("no mask in this test");
        public int Tier0FloorY => FLOOR_Y;
        public int Tier0CeilY => CEIL_Y;
    }

    static void AssertSame(ShellRayReference.Result a, ShellRayReference.Result b, string what)
    {
        Assert.AreEqual(b.End, a.End, what + ": end");
        Assert.AreEqual(b.Shell, a.Shell, what + ": shell");
        Assert.AreEqual(b.T, a.T, what + ": t (exact)");
        Assert.AreEqual(b.Voxel, a.Voxel, what + ": voxel");
        Assert.AreEqual(b.Normal, a.Normal, what + ": normal");
        Assert.AreEqual(b.Mat, a.Mat, what + ": mat");
        Assert.AreEqual(b.Iters, a.Iters, what + ": iterations");
        Assert.AreEqual(b.InsideStart, a.InsideStart, what + ": inside");
    }

    static readonly float3[] Suns =
    {
        math.normalize(new float3(0.1f, 0.98f, 0.05f)),     // noon, nearly overhead
        math.normalize(new float3(0.6f, 0.75f, 0.02f)),     // toward the pillar, 50 deg
        math.normalize(new float3(0.9f, 0.36f, -0.05f)),    // toward the pillar, 20 deg
        math.normalize(new float3(-0.5f, 0.6f, 0.62f)),
    };

    /// Primary rays (both band caches), shell-hit sun rays and task 133's near sun rays (start in tier 0's
    /// band near the camera, t = the band-ceiling exit), with and without the pillar, at the real step caps
    /// and at small caps (3-12) that end rays inside the hole box: identical with the leap on and off.
    [Test]
    public void HoleLeapChangesNoResult()
    {
        var terrain = new ShellMarchCacheTests.TerrainView(ShellLayout.From(QualityPreset.High));
        Assert.GreaterOrEqual(terrain.L[1].Hole, 0, "shell 1 has a hole");
        int3 cc1 = (int3)math.floor(Cam) >> 8;
        Assert.Greater((PillarLo.x >> 8) - cc1.x, terrain.L[1].Hole, "the pillar stands outside shell 1's hole");
        Assert.Greater((cc1.y + terrain.L[1].Hole + 1) << 8, CEIL_Y, "shell 1's hole reaches above tier 0's layers");
        const float tTier0 = 640f;
        int rays = 0, near = 0, nearBlocked = 0, pillarBlocks = 0, capped = 0;
        long holeLeaps = 0, readsOn = 0, readsOff = 0;
        foreach (bool pillar in new[] { false, true })
        {
            var v = new HoleView(terrain, pillar);
            int i = 0;
            foreach (var dir in ShellMarchCacheTests.RaySet(v.L, Cam, out _))
            {
                if (i++ % 3 != 0) continue;   // every third ray of the set (the fast suite's time)
                foreach (bool band in new[] { false, true })
                {
                    var on = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, 384, tTier0, bandCache: band, holeLeap: true);
                    var off = ShellRayReference.March(v, Cam, dir, 0f, false, Cam, 384, tTier0, bandCache: band);
                    AssertSame(on, off, $"primary {i} pillar {pillar}");
                    holeLeaps += on.HoleLeaps; readsOn += on.DirReads; readsOff += off.DirReads;
                    rays++;
                    if (band || on.End != ShellRayReference.END_HIT) continue;
                    float3 so = Cam + dir * on.T + (float3)on.Normal * 0.01f;
                    foreach (var s in Suns)
                        AssertSame(ShellRayReference.March(v, so, s, 0f, true, Cam, 192, 0f, holeLeap: true),
                                   ShellRayReference.March(v, so, s, 0f, true, Cam, 192, 0f), $"shell-hit sun ray {i}");
                }
                // Near sun rays: ground points within ~90 m of the camera (in tier 0's layers), leaving at the band ceiling.
                float a = i * 2.39996f, rr = 30f + (i % 30) * 30f;
                var o = new float3(Cam.x + rr * math.cos(a), GROUND + 1.5f + (i % 5) * 20f, Cam.z + rr * math.sin(a));
                foreach (var s in Suns)
                {
                    float tExit = (CEIL_Y - o.y) / s.y;
                    foreach (int cap in new[] { 96, 3 + (i % 10) })
                    {
                        bool band = (i / 3 + cap) % 2 == 0;   // the play kernel's band cache on half of them
                        {
                            var on = ShellRayReference.March(v, o, s, tExit, true, Cam, cap, 0f, bandCache: band, holeLeap: true);
                            var off = ShellRayReference.March(v, o, s, tExit, true, Cam, cap, 0f, bandCache: band);
                            AssertSame(on, off, $"near sun ray {i} cap {cap} pillar {pillar}");
                            holeLeaps += on.HoleLeaps; readsOn += on.DirReads; readsOff += off.DirReads;
                            near++;
                            if (on.End == ShellRayReference.END_CAP && on.HoleLeaps > 0) capped++;
                            if (cap != 96 || on.End != ShellRayReference.END_HIT) continue;
                            nearBlocked++;
                            if (pillar && math.all(on.Voxel >= PillarLo - (128 << on.Shell)) && math.all(on.Voxel < PillarHi + (128 << on.Shell))) pillarBlocks++;
                        }
                    }
                }
            }
        }
        UnityEngine.Debug.Log($"[ShellHoleLeap] {rays} primary rays, {near} near sun rays ({nearBlocked} blocked, {pillarBlocks} by the pillar, " +
                              $"{capped} capped after a hole leap): identical; hole leaps {holeLeaps:N0}; dir reads {readsOn:N0} vs {readsOff:N0}");
        Assert.Greater(holeLeaps, 1000, "the hole box is used");
        Assert.Greater(pillarBlocks, 50, "near sun rays cross the hole and the pillar blocks them");
        Assert.Greater(capped, 20, "small caps end rays inside the hole box");
        Assert.Less(readsOn, readsOff, "the leap skips directory reads");
    }
}
