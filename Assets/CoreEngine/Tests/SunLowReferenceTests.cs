// Assets/CoreEngine/Tests/SunLowReferenceTests.cs
//
// TASK 232 (performance, owner Q-AN 2026-11-16): the CPU twin of Raymarch.compute's LOW-RES SUN (SunLowReference).
// Views are simulated top-down: pixel (px, py) shows the top face at X = x0 + s px + j, Z = z0 + s py + j, where j is
// the frame's jitter (0 / 1 alternating: a FAR view, s = 2 voxels per pixel, shows a DIFFERENT set of faces every
// frame, so no record keys match and every value is a prior or an inline ray), or the camera pans one voxel a frame
// (a NEAR view, s = 1). Proven: a slot floor never takes the ledge top's light (different plane); a steep bare slope
// has no false shadow (228's triangle field); a thin pole's shadow stays exact near; every wrong pixel, near or far,
// lies within 1 voxel of an exact shadow edge.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class SunLowReferenceTests
{
    private static readonly int3 WindowBricks = new int3(64, 64, 64);   // 512 voxels per axis, as ShadowRayTests
    private static readonly float3 Cam = new float3(1f, 6f, 1f);
    private static readonly int3 Up = new int3(0, 1, 0);

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;
    private Dictionary<uint, float> _exact;
    private float3 _sun;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(20000);
        _allocator = new ChunkHandleAllocator(256);
        _store = new ChunkStore(_pool, _allocator);
        int3 wc = WindowBricks / 16;
        for (int cz = 0; cz < wc.z; cz++)
        for (int cy = 0; cy < wc.y; cy++)
        for (int cx = 0; cx < wc.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
        _exact = new Dictionary<uint, float>();
        _top.Clear();
    }

    [TearDown]
    public void Teardown() => _pool.Dispose();

    private void Box(int3 min, int3 size)
    {
        for (int z = 0; z < size.z; z++)
        for (int y = 0; y < size.y; y++)
        for (int x = 0; x < size.x; x++)
            _store.SetVoxel(min + new int3(x, y, z), Materials.Stone);
    }

    /// The column top (highest solid y below 200), or -1.
    private readonly Dictionary<int2, int> _top = new Dictionary<int2, int>();
    private int Top(int x, int z)
    {
        if (_top.TryGetValue(new int2(x, z), out int t)) return t;
        t = -1;
        for (int y = 199; y >= 0 && t < 0; y--) if (_store.GetVoxel(new int3(x, y, z)) != 0) t = y;
        _top[new int2(x, z)] = t;
        return t;
    }

    private float Exact(int3 v, int3 n)
    {
        uint k = SkyVisReference.OriginKey(v + n);
        if (!_exact.TryGetValue(k, out float e))
        {
            e = ShadowRayReference.Trace(_store, v, n, _sun, Cam, 0f, 60f).sunVis;
            _exact[k] = e;
        }
        return e;
    }

    private struct RunStats { public int wrong, wrongFarFromEdge, wrongAboveFloor, inline, priors, pixels; public string firstBad, firstWrong; }

    /// Runs `frames` frames of the low path over a w x w top-down view at (x0, z0), s voxels per pixel. far: jitter
    /// j = f & 1 on both axes; else the camera pans +1 voxel in x per frame. Every frame compares each pixel with the
    /// exact ray; a wrong pixel is "near an edge" when a neighbouring top face (x / z +-1) has a different exact value.
    private RunStats Run(int x0, int z0, int w, int s, bool far, int bs, int frames)
    {
        var a = new SunLowReference.Record[w * w];
        var b = new SunLowReference.Record[w * w];
        var vis = new float[w * w];
        var st = new RunStats();
        int jPrev = 0;
        for (int f = 0; f < frames; f++)
        {
            int jx = far ? (f & 1) : f, jz = far ? (f & 1) : 0, jxPrev = far ? jPrev : f - 1, jzPrev = far ? jPrev : 0;
            SunLowReference.FaceAt face = (int px, int py, out int3 v, out int3 n) =>
            {
                int x = x0 + s * px + jx, z = z0 + s * py + jz;
                n = Up; v = new int3(x, Top(x, z), z);
                return v.y >= 0;
            };
            System.Func<int3, int3, float2> prev = (v, n) =>
                new float2((v.x - x0 - jxPrev) / (float)s + 0.5f, (v.z - z0 - jzPrev) / (float)s + 0.5f);
            int inl = 0, pri = 0;
            SunLowReference.Frame(a, f > 0, b, vis, w, w, bs, (uint)f, face, prev, v => 1f + 2f * s,
                                  (v, n, sh) => Exact(v, n), ref inl, ref pri);
            st.inline += inl; st.priors += pri;
            for (int py = 0; py < w; py++)
            for (int px = 0; px < w; px++)
            {
                if (!face(px, py, out int3 v, out int3 n)) continue;
                st.pixels++;
                float e = Exact(v, n);
                if (math.abs(vis[py * w + px] - e) <= SunLowReference.AGREE) continue;
                st.wrong++;
                if (v.y > 19) st.wrongAboveFloor++;
                if (st.firstWrong == null) st.firstWrong = $"frame {f} px ({px},{py}) voxel {v}: low {vis[py * w + px]} exact {e}";
                bool nearEdge = false;
                int3[] d = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
                foreach (var dd in d)
                {
                    int3 q = v + dd;
                    q.y = Top(q.x, q.z);
                    if (q.y >= 0 && q.y == v.y && math.abs(Exact(q, Up) - e) > SunLowReference.AGREE) nearEdge = true;
                }
                if (!nearEdge)
                {
                    st.wrongFarFromEdge++;
                    if (st.firstBad == null) st.firstBad = $"frame {f} px ({px},{py}) voxel {v}: low {vis[py * w + px]} exact {e}";
                }
            }
            (a, b) = (b, a);
            jPrev = far ? (f & 1) : 0;
        }
        return st;
    }

    [Test]
    public void LowSun_RecordPackAndShaderPins()
    {
        float[] vs = { 0f, 0.37f, 1f };
        foreach (float x in vs)
        foreach (uint fl in new[] { 0u, SunLowReference.FLAG_PRIOR, SunLowReference.FLAG_SHELL, SunLowReference.FLAG_PRIOR | SunLowReference.FLAG_SHELL })
        {
            var r = SunLowReference.Make(x, new int3(-1234, 300, 98765), new int3(0, 0, -1), fl);
            Assert.IsTrue(r.Has);
            Assert.AreEqual(x, r.Vis, "the value survives the flag bits");
            Assert.AreEqual(fl, r.Flags);
            r.Face(out int3 v, out int3 n);
            Assert.AreEqual(new int3(-1234, 300, 98765), v);
            Assert.AreEqual(new int3(0, 0, -1), n);
        }
        Assert.IsFalse(SunLowReference.None.Has);
        string src = System.IO.File.ReadAllText("Assets/CoreEngine/Rendering/Raymarch.compute");
        StringAssert.Contains("struct SunRec { uint v; uint a; uint b; };", src);
        StringAssert.Contains("#define SUN_REC_PRIOR 0x80000000u", src);
        StringAssert.Contains("#define SUN_REC_SHELL 0x40000000u", src);
        StringAssert.Contains("#define SUN_AGREE (1.0 / 16.0)", src);
        StringAssert.Contains("#define SUN_PRIOR_MIN 2u", src);
        Assert.AreEqual(2u, SunLowReference.PRIOR_MIN);
        StringAssert.Contains("#pragma kernel CSSunTrace VE_FAR_SHADE VE_SUN_TRACE", src);
        StringAssert.Contains("else if (nComp >= SUN_PRIOR_MIN && pMax - pMin <= SUN_AGREE) { vis = pFirst; flags = SUN_REC_PRIOR; kind = 2u; }", src);
        StringAssert.Contains("if (slKind == 0u) { slVis = SunTraceFace(voxel, normal, slShell); slFlags = 0u; }", src);
        StringAssert.Contains("if (r.a == rep.a && r.b == rep.b && (r.v & SUN_REC_SHELL) == shellF) ShadowHistW[idx] = traced;", src);
        StringAssert.Contains("if (gsAnyShadowNeeded != 0u && !SUN_LOW_ON)", src);
        string feat = System.IO.File.ReadAllText("Assets/CoreEngine/Rendering/RaymarchFeature.cs");
        StringAssert.Contains("private const int SHADOW_REC_STRIDE = 12;", feat);
        StringAssert.Contains("public static int SunRes = ValidSunRes(ReadIntArg(\"-sunres\", 1));", feat);
        StringAssert.Contains("public static int ValidSunRes(int v) => v == 2 || v == 4 ? v : 1;", feat);
    }

    /// The prior's probe order and agreement rule: the first compatible probe's value, only when >= 2 agree.
    [Test]
    public void LowSun_FetchNeedsTwoAgreeingSamePlaneProbes()
    {
        int w = 4;
        var h = new SunLowReference.Record[w * w];
        for (int i = 0; i < h.Length; i++) h[i] = SunLowReference.None;
        var v = new int3(100, 19, 100);
        float2 ppf = new float2(1.7f, 1.7f);   // probes (1,1), (2,1), (1,2), (2,2)
        h[1 * w + 1] = SunLowReference.Make(1f, new int3(98, 19, 98), Up, 0u);
        Assert.AreEqual(0u, SunLowReference.Fetch(h, w, w, ppf, v, Up, 5f, out _, out _), "one probe is not enough");
        h[1 * w + 2] = SunLowReference.Make(0f, new int3(102, 19, 98), Up, 0u);
        Assert.AreEqual(0u, SunLowReference.Fetch(h, w, w, ppf, v, Up, 5f, out _, out _), "two that disagree: an edge, trace inline");
        h[1 * w + 2] = SunLowReference.Make(0.97f, new int3(102, 19, 98), Up, 0u);
        Assert.AreEqual(2u, SunLowReference.Fetch(h, w, w, ppf, v, Up, 5f, out float pv, out uint pf));
        Assert.AreEqual(1f, pv, "the first probe's value"); Assert.AreEqual(SunLowReference.FLAG_PRIOR, pf);
        h[1 * w + 2] = SunLowReference.Make(1f, new int3(102, 29, 98), Up, 0u);
        Assert.AreEqual(0u, SunLowReference.Fetch(h, w, w, ppf, v, Up, 5f, out _, out _), "another plane never counts");
        h[2 * w + 2] = SunLowReference.Make(0.25f, v, Up, SunLowReference.FLAG_PRIOR);
        Assert.AreEqual(1u, SunLowReference.Fetch(h, w, w, ppf, v, Up, 5f, out float kv, out uint kf), "own key wins");
        Assert.AreEqual(0.25f, kv); Assert.AreEqual(SunLowReference.FLAG_PRIOR, kf, "a carried prior stays a prior until traced");
    }

    /// A ledge top beside a 3-voxel slot, 10 deep, sun low from +x: the slot floor is in shadow, the ledge lit. In a
    /// FAR view (every frame new faces) the floor must never take the ledge's light: its pixels stay exact every frame.
    [Test]
    public void LowSun_LedgeTopNeverLightsSlotFloor([Values(1, 2)] int bs)
    {
        Box(new int3(160, 0, 160), new int3(192, 10, 192));
        Box(new int3(160, 10, 160), new int3(90, 10, 192));    // ledge x < 250
        Box(new int3(253, 10, 160), new int3(99, 10, 192));    // ledge x >= 253; slot x in [250, 253), floor top y 9
        _sun = math.normalize(new float3(1f, 0.5f, 0f));
        Assert.AreEqual(0f, Exact(new int3(251, 9, 200), Up), "setup: slot floor in shadow");
        Assert.AreEqual(1f, Exact(new int3(240, 19, 200), Up), "setup: ledge lit");
        var st = Run(230, 190, 24, 2, true, bs, 40);
        TestContext.WriteLine($"bs {bs}: pixels {st.pixels} wrong {st.wrong} inline {st.inline} priors {st.priors}");
        Assert.Greater(st.priors, st.pixels / 4, "the prior path is exercised");
        Assert.AreEqual(0, st.wrong, st.firstWrong);
    }

    /// 228's defect case: a steep bare slope (2 voxels up per voxel across). Lit from the open side, from steep above,
    /// and self-shadowed from the other side. Each step top is its own plane, so no value crosses steps: the slope is
    /// exact every frame, no false (or missing) shadow anywhere on it. (The flat floor at its foot may be off by one
    /// voxel at the slope's own shadow edge there, as any far edge: within 1 voxel.)
    [Test]
    public void LowSun_SteepSlopeHasNoFalseShadow([Values(0, 1, 2)] int sunCase)
    {
        Box(new int3(160, 0, 160), new int3(90, 20, 192));
        for (int x = 250; x < 300; x++) Box(new int3(x, 0, 160), new int3(1, 20 + 2 * (x - 250), 192));
        _sun = sunCase == 0 ? math.normalize(new float3(-1f, 1.2f, 0.3f))
             : sunCase == 1 ? math.normalize(new float3(1f, 2.6f, 0.2f))
             : math.normalize(new float3(1f, 0.4f, 0.2f));
        var st = Run(240, 190, 24, 2, true, 2, 40);
        TestContext.WriteLine($"sun {sunCase}: pixels {st.pixels} wrong {st.wrong} (on the slope {st.wrongAboveFloor}) inline {st.inline} priors {st.priors}");
        Assert.AreEqual(0, st.wrongAboveFloor, st.firstWrong);
        Assert.AreEqual(0, st.wrongFarFromEdge, st.firstBad);
    }

    /// ADVERSARIAL: a 1 x 1 voxel pole, 40 tall: a one-voxel shadow line on flat ground. NEAR view (a pixel per voxel,
    /// the camera panning a voxel a frame): every face was seen last frame, so the line is exact every frame.
    [Test]
    public void LowSun_ThinPoleShadowStaysExactNear([Values(1, 2)] int bs)
    {
        Box(new int3(160, 0, 160), new int3(192, 20, 192));
        Box(new int3(260, 20, 220), new int3(1, 40, 1));
        _sun = math.normalize(new float3(1f, 1f, 0f));
        Assert.AreEqual(0f, Exact(new int3(240, 19, 220), Up), "setup: the line");
        Assert.AreEqual(1f, Exact(new int3(240, 19, 221), Up), "setup: one voxel beside it");
        var st = Run(220, 208, 24, 1, false, bs, 30);
        TestContext.WriteLine($"bs {bs}: pixels {st.pixels} wrong {st.wrong} inline {st.inline} priors {st.priors}");
        Assert.AreEqual(0, st.wrong, st.firstWrong);
    }

    /// The shadow edge, FAR (new faces every frame): a tall wall's straight edge, and the thin pole's line (narrower than
    /// a pixel there). Wrong pixels may occur only within 1 voxel of an exact edge. The pole's line can be missed in
    /// frames where it was not in last frame's samples (a sub-pixel shadow; counted and printed).
    [Test]
    public void LowSun_FarShadowEdgeWithinOneVoxel([Values(0, 1)] int shape, [Values(1, 2)] int bs)
    {
        Box(new int3(160, 0, 160), new int3(192, 20, 192));
        if (shape == 0) Box(new int3(260, 20, 150), new int3(4, 60, 200));
        else Box(new int3(260, 20, 220), new int3(1, 40, 1));
        _sun = shape == 0 ? math.normalize(new float3(1f, 1f, 0.35f)) : math.normalize(new float3(1f, 1f, 0f));
        var st = Run(shape == 0 ? 200 : 220, 196, 24, 2, true, bs, 40);
        TestContext.WriteLine($"shape {shape} bs {bs}: pixels {st.pixels} wrong {st.wrong} ({100.0 * st.wrong / st.pixels:F2} %) inline {st.inline} ({100.0 * st.inline / st.pixels:F1} %) priors {st.priors}");
        Assert.AreEqual(0, st.wrongFarFromEdge, st.firstBad);
        if (shape == 0) Assert.LessOrEqual(st.wrong, st.pixels / 100, "a straight edge: nearly every edge pixel exact");
    }
}
