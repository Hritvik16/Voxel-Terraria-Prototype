// Assets/CoreEngine/Tests/ShellHandOverTests.cs
//
// Engine step 11b (task 80; Amendment 8.14 rows 5 and 15, owner Q2 (a)): the
// shell hand-over, on ShellRayReference (the CPU twin of Raymarch.compute's
// ShellMarch). A synthetic world: the High layout's real bounds, reaches and
// holes; every shell stores a flat ground (top at y = 16384 voxels, a chunk
// face in every shell) as UNIFORM chunks of material 10 + k, air above, so a
// hit names the shell that drew it.
//   * a ray at each shell's mid-range and at both sides of each boundary reads
//     the right shell (inside the finer shell's hole: the finer one, rule
//     "hole -> finer");
//   * a chunk absent (or refused) in shell k is read from k+1, k+2, ...
//     (the first that stores it: a coarser shell never stores chunks in its
//     hole); none -> the far band (END_ABSENT);
//   * past the last shell -> the far band (END_PAST_LAST);
//   * a march that starts inside a coarse solid cell is a TOP-face hit (the
//     seam rule: never the side face it last crossed);
//   * task 85 (step 12): inside tier 0's band a chunk tier 0 holds is leapt,
//     one it does not hold is read from shell 1 (hand-over by distance).
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Mirror;

public class ShellHandOverTests
{
    const int GROUND = 16384;                       // 128 << 7: a chunk face in shells 0-7
    static readonly float3 Cam = new float3(40000.5f, GROUND + 100.5f, 40000.5f);   // 10 m above the ground

    sealed class FlatView : ShellRayReference.IView
    {
        public readonly ShellLayout L;
        public readonly HashSet<(int, int3)> Absent = new HashSet<(int, int3)>();
        public FlatView(ShellLayout l) { L = l; }
        public int ShellCount => L.ShellCount;
        public float OuterVoxels(int k) => L[k].OuterM * 10f;
        public int3 CamChunk(int k) => (int3)math.floor(Cam) >> (7 + k);
        public bool Box(int k, out int3 lo, out int3 hi, out int3 cc, out int hole)
        {
            cc = CamChunk(k); hole = L[k].Hole;
            lo = cc - L[k].Reach; hi = cc + L[k].Reach;
            return true;
        }
        public uint Dir(int k, int3 c)
        {
            if (Absent.Contains((k, c))) return ShellStore.ABSENT;
            bool solid = (c.y + 1) * (128 << k) <= GROUND;
            return ShellStore.UNIFORM | (solid ? (uint)(10 + k) : 0u);
        }
        public uint Page(int k, int page, int lb) => 0u;          // every chunk is UNIFORM here
        public byte Voxel(int k, int body, int vIdx) => 0;
        public bool Tier0Missing(int3 voxel) => Tier0Gap;
        public bool Tier0Gap;
        public bool Tier0Holds(int3 voxel) => Tier0Held;
        public bool Tier0Held;
        public bool InHole(int k, int3 voxel)
        {
            if (L[k].Hole < 0) return false;
            int3 d = math.abs((voxel >> (7 + k)) - CamChunk(k));
            return math.cmax(d) <= L[k].Hole;
        }
    }

    static FlatView View() => new FlatView(ShellLayout.From(QualityPreset.High));

    /// A ray from Cam down to the ground at camera distance dM metres, along azimuth az (radians).
    static float3 DirTo(float dM, float az)
    {
        float h = Cam.y - GROUND, d = dM * 10f;
        float x = math.sqrt(math.max(1f, d * d - h * h));
        return math.normalize(new float3(math.cos(az) * x, -h, math.sin(az) * x));
    }

    static ShellRayReference.Result Shoot(FlatView v, float dM, float az = 0.3f) =>
        ShellRayReference.March(v, Cam, DirTo(dM, az), 0f, false, Cam, 4096);

    /// The rule as the test states it: the distance shell, or the finer one while
    /// the point is in the distance shell's hole.
    static int Expected(FlatView v, float3 p, float tVox)
    {
        int k = ShellRayReference.ShellByDistance(v, tVox);
        int3 vox = (int3)math.floor(p);
        while (k > 1 && v.InHole(k, vox)) k--;
        return k;
    }

    [Test]
    public void EachShellDrawsItsOwnRange()
    {
        var v = View();
        Assert.AreEqual(8, v.ShellCount, "High has shells 0-7");
        var lines = new List<string>();
        for (int k = 1; k < v.ShellCount; k++)
        {
            float inner = v.L[k].InnerM, outer = v.L[k].OuterM;
            foreach (float az in new[] { 0.3f, 1.1f, 2.6f, 4.0f })
            {
                // Mid-range: the distance shell itself.
                float mid = 0.5f * (inner + outer);
                var r = Shoot(v, mid, az);
                Assert.AreEqual(ShellRayReference.END_HIT, r.End, $"shell {k} mid {mid} m az {az}: end {r.End}");
                int wantMid = Expected(v, Cam + DirTo(mid, az) * r.T, r.T);
                Assert.AreEqual(wantMid, r.Shell, $"mid-range of shell {k} ({mid} m, az {az}) drew shell {r.Shell}, the rule says {wantMid}");
                Assert.IsTrue(wantMid == k || wantMid == k - 1, $"mid-range of shell {k}: rule gave {wantMid}");
                if (az == 0.3f) Assert.AreEqual(k, r.Shell, "near an axis, mid-range is outside the finer shell's hole");
                Assert.AreEqual(10 + r.Shell, r.Mat);
                Assert.AreEqual(new int3(0, 1, 0), r.Normal, "a ground hit is a top face");
                // Both sides of the outer boundary.
                foreach (float dd in new[] { -0.5f, 0.5f })
                {
                    float dM = outer + dd;
                    var rb = Shoot(v, dM, az);
                    if (k == v.ShellCount - 1 && dd > 0f)
                    {
                        Assert.AreEqual(ShellRayReference.END_PAST_LAST, rb.End, $"past the last shell ({dM} m): far band");
                        Assert.GreaterOrEqual(rb.T, v.OuterVoxels(k));
                        continue;
                    }
                    Assert.AreEqual(ShellRayReference.END_HIT, rb.End, $"boundary {k} {dM} m az {az}");
                    int want = Expected(v, Cam + DirTo(dM, az) * rb.T, rb.T);
                    Assert.AreEqual(want, rb.Shell, $"boundary of shell {k} at {dM} m, az {az}: drew {rb.Shell}, the rule says {want}");
                    Assert.AreEqual(10 + rb.Shell, rb.Mat);
                    if (dd < 0f) Assert.AreEqual(k, rb.Shell, $"just inside shell {k}'s outer bound");
                }
            }
            lines.Add($"shell {k}: {inner:F0}-{outer:F0} m ok");
        }
        UnityEngine.Debug.Log("[ShellHandOver] " + string.Join("; ", lines));
    }

    [Test]
    public void AbsentChunkIsReadFromTheNextShell_ThenTheFarBand()
    {
        var v = View();
        for (int k = 1; k < v.ShellCount; k++)
        {
            float dM = 0.5f * (v.L[k].InnerM + v.L[k].OuterM);
            var r0 = Shoot(v, dM);
            Assert.AreEqual(k, r0.Shell);
            int3 hitVox = r0.Voxel;   // the first solid voxel, just below the face
            // Refuse (row 15) the hit chunk in shell k: the coarser shell answers.
            var marked = new List<(int, int3)>();
            for (int j = k; j < v.ShellCount; j++)
            {
                var key = (j, hitVox >> (7 + j));
                v.Absent.Add(key); marked.Add(key);
                var r = Shoot(v, dM);
                // The next coarser shell that stores the chunk (a coarser shell
                // never stores chunks in its hole); none -> the far band.
                int want = -1;
                for (int c = j + 1; c < v.ShellCount && want < 0; c++) if (!v.InHole(c, hitVox)) want = c;
                if (want > 0)
                {
                    Assert.AreEqual(ShellRayReference.END_HIT, r.End, $"shell {k}: absent in {k}..{j}");
                    Assert.AreEqual(want, r.Shell, $"absent in shells {k}..{j}: drew {r.Shell}");
                    Assert.AreEqual(10 + r.Shell, r.Mat);
                    Assert.AreEqual(r0.T, r.T, 1e-2f, "the same ground point");
                }
                else
                    Assert.AreEqual(ShellRayReference.END_ABSENT, r.End, $"absent in {k}..{j}, in the hole of every coarser shell: far band (end {r.End}, shell {r.Shell})");
            }
            foreach (var key in marked) v.Absent.Remove(key);
        }
    }

    [Test]
    public void TierZerosSpace_IsLeapt_UnlessTierZeroIsMissingThere()
    {
        var v = View();
        // From the camera (its chunk is in every hole): leapt, the hit is shell 3's.
        float dM = 0.5f * (v.L[3].InnerM + v.L[3].OuterM);
        var r = Shoot(v, dM);
        Assert.AreEqual(ShellRayReference.END_HIT, r.End);
        Assert.AreEqual(3, r.Shell);
        // Tier 0 missing there (a non-resident column): the far band, from the start.
        v.Tier0Gap = true;
        var g = Shoot(v, dM);
        Assert.AreEqual(ShellRayReference.END_ABSENT, g.End);
        Assert.AreEqual(0f, g.T);
    }

    [Test]
    public void TierZerosBand_HeldIsLeapt_NotHeldIsShellOne()
    {
        var v = View();
        float r0 = v.OuterVoxels(0);
        // Ground at 60 m (< tier 0's 68.75 m hand-over), outside shell 1's hole.
        float dM = 60f;
        var hit = ShellRayReference.March(v, Cam, DirTo(dM, 0.3f), 0f, false, Cam, 4096, r0);
        Assert.IsTrue(v.InHole(1, hit.Voxel) == false, "the 60 m ground point is outside shell 1's hole");
        // Tier 0 does not hold it (non-resident, outside its window or layers): shell 1 draws it.
        Assert.AreEqual(ShellRayReference.END_HIT, hit.End);
        Assert.AreEqual(1, hit.Shell);
        Assert.AreEqual(dM * 10f, hit.T, 20f);
        // Tier 0 holds it: it marched there (and saw air), so nothing inside its band is
        // drawn by a shell; the first shell sample is at or past r0 (a top-face hit).
        v.Tier0Held = true;
        var held = ShellRayReference.March(v, Cam, DirTo(dM, 0.3f), 0f, false, Cam, 4096, r0);
        Assert.AreEqual(ShellRayReference.END_HIT, held.End);
        Assert.GreaterOrEqual(held.T, r0, "no shell pixel inside tier 0's band where tier 0 holds the chunk");
        // tTier0 = 0 (shadow rays, old callers): the rule is off.
        var off = ShellRayReference.March(v, Cam, DirTo(dM, 0.3f), 0f, false, Cam, 4096);
        Assert.AreEqual(hit.T, off.T, 1e-3f);
    }

    [Test]
    public void PastTheLastShell_IsTheFarBand()
    {
        var v = View();
        // Level, just above the ground: inside every box, never hits.
        var r = ShellRayReference.March(v, Cam, math.normalize(new float3(1f, 0.0001f, 0.37f)), 0f, false, Cam, 4096);
        Assert.AreEqual(ShellRayReference.END_PAST_LAST, r.End, $"end {r.End} at t {r.T}");
        Assert.GreaterOrEqual(r.T, v.OuterVoxels(v.ShellCount - 1));
        UnityEngine.Debug.Log($"[ShellHandOver] level ray: far band from t {r.T * 0.1f:F0} m after {r.Iters} iterations");
        Assert.Less(r.Iters, 384, "the shader's default cap (RaymarchFeature.ShellIterCap) must cover an empty level ray");
    }

    [Test]
    public void StartInsideACoarseSolidCell_IsATopFaceHit()
    {
        var v = View();
        // The hand-over from tier 0 at ~210 m (shell 2 by distance): the ray
        // starts a few voxels below the coarse top (tier 0 saw air there).
        float3 d = math.normalize(new float3(1f, -0.05f, 0.2f));
        float t0 = 2100f;
        Assert.Less((Cam + d * t0).y, GROUND, "the start is inside the ground");
        var r = ShellRayReference.March(v, Cam, d, t0, false, Cam, 64);
        Assert.AreEqual(ShellRayReference.END_HIT, r.End);
        Assert.AreEqual(2, r.Shell);
        Assert.IsTrue(r.InsideStart, "entered mid-cell");
        Assert.AreEqual(new int3(0, 1, 0), r.Normal, "a downward ray that starts inside rock is lit as a top face, not the side face");
        Assert.AreEqual(1, r.Iters);
        // A shadow ray that starts in a solid coarse cell leaves it (no self-shadowing).
        float3 so = new float3(Cam.x + 2000f, GROUND - 0.5f, Cam.z);
        var s = ShellRayReference.March(v, so, math.normalize(new float3(0.3f, 0.8f, 0.1f)), 0f, true, Cam, 256);
        Assert.AreNotEqual(ShellRayReference.END_HIT, s.End, $"a sun ray up from inside the ground's top cell is not shadowed (end {s.End}, shell {s.Shell})");
    }
}
