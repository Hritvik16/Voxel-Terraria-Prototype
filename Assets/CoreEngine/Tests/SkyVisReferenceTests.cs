// ==========================================
// SkyVisReferenceTests.cs -- task 147 (SOFT_SKY_PLAN.md step S1, owner Q94 plan A)
//
// The soft-sky CPU oracle (SkyVisReference): the cosine-weighted sky fraction,
// its direction sequence, and the 1/n-then-1/16 running mean. Every shape is
// BUILT through the edit-op API (EditOps.Record of Box ops, as a player's build
// would be), never taken from a generator (the 2026-10-24 generality rule):
//   (1) a sealed room: exactly 0 for every face, frame and seed
//   (2) open ground: exactly 1; a vertical face with nothing in front exactly
//       0.5, a down face exactly 0 (the below-the-horizon rule)
//   (3) a 0.1 m shell room: exactly 0 (a thin roof blocks fully), and a 0.1 m
//       wall blocks EXACTLY what a 2 m one with the same near face does
//   (4) a 3 m ledge: the floor and the wall under it lie in (0.1, 0.6) and the
//       running mean matches the quadrature after 64 frames (RMS over pixels
//       <= 0.05, unbiased; single pixels can miss by ~0.1, see AssertMatchesQuadrature)
//   (5) a 2 m hole in a 10 m room's roof (Q3 (a), physical): the floor below
//       it sees sky, the corners < 0.05
//   (7) the history: a key change restarts the count, 1/n then 1/16, 0 and 1 stay exact
//   + a 1-voxel pillar, task 138's built site (tunnel darkens inward), and the
//     direction sequence's integer contract (pinned values for S2's shader).
// Test (6) (a tunnel crossing the box edge) is step S3's.
// REPORTS=1 also writes Screenshots/147/softsky_quadrature147.txt.
// ==========================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;

public class SkyVisReferenceTests
{
    private static readonly int3 WindowBricks = new int3(64, 64, 64);   // 51.2 m cube, voxels 0..511
    private const float ClampM = 40f;
    private const int Frames = 64;

    private static readonly int3 Up = new int3(0, 1, 0), Down = new int3(0, -1, 0);
    private static readonly int3 PX = new int3(1, 0, 0), NX = new int3(-1, 0, 0), PZ = new int3(0, 0, 1);

    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditOps _ops;

    [SetUp]
    public void Setup() => NewWorld();

    [TearDown]
    public void Teardown() => _pool?.Dispose();

    private void NewWorld()
    {
        _pool?.Dispose();
        _pool = new BrickDataPool(80000);
        var alloc = new ChunkHandleAllocator(256);
        _store = new ChunkStore(_pool, alloc);
        int3 wc = WindowBricks / 16;
        for (int cz = 0; cz < wc.z; cz++)
        for (int cy = 0; cy < wc.y; cy++)
        for (int cx = 0; cx < wc.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
        var edits = new EditService();
        edits.AttachWorld(_store, _store, _store, null);
        _ops = new EditOps(EditOpLog.InMemory(147u), edits, _store);
    }

    /// The inclusive box lo..hi as Box edit ops: cubes of the largest odd edge that
    /// fits the thinnest side, the last one on each axis pulled back to end at hi.
    private void Fill(int3 lo, int3 hi, byte m)
    {
        int3 ext = hi - lo + 1;
        int e = math.cmin(ext);
        if ((e & 1) == 0) e--;
        int s = (e - 1) / 2;
        foreach (int x in Starts(lo.x, hi.x, e))
        foreach (int y in Starts(lo.y, hi.y, e))
        foreach (int z in Starts(lo.z, hi.z, e))
            _ops.Record(new EditOp(EditOpKind.Box, new int3(x, y, z) + s, (ushort)s, m));
    }

    private static IEnumerable<int> Starts(int lo, int hi, int e)
    {
        int a = lo;
        for (; a + e - 1 < hi; a += e) yield return a;
        yield return hi - e + 1;
    }

    private static float3 Cam(int3 hit, int3 n) => (new float3(hit + n) + 0.5f) * 0.1f;

    private float Q(int3 hit, int3 n, bool lift = false) =>
        SkyVisReference.Quadrature(_store, hit, n, Cam(hit, n), ClampM, lift);

    private float Est(int3 hit, int3 n, uint seed, int frames = Frames) =>
        SkyVisReference.Estimate(_store, hit, n, seed, frames, Cam(hit, n), ClampM);

    private static uint Seed(int i) => SkyVisReference.PixelSeed(new uint2((uint)(97 + 131 * i), (uint)(41 + 57 * i)));

    /// Every sample of `frames` frames for `seeds` pixel seeds equals `want`, and so does the history.
    private void AssertEverySample(int3 hit, int3 n, float want, int seeds, int frames, string what)
    {
        for (int i = 0; i < seeds; i++)
        {
            uint seed = Seed(i);
            for (uint k = 0; k < frames; k++)
            {
                float s = SkyVisReference.Sample(_store, hit, n, SkyVisReference.Direction(n, seed, k), Cam(hit, n), ClampM);
                if (s != want) Assert.Fail($"{what}: seed {i} frame {k} sample {s}, want exactly {want}");
            }
            Assert.AreEqual(want, Est(hit, n, seed, frames), $"{what}: seed {i} history");
        }
    }

    /// The running mean after 64 frames matches the quadrature: RMS error over 32 pixel seeds <= 0.05
    /// and the mean over the seeds within 0.025 (no bias). NOT "every pixel within 0.05": under the
    /// plan's fixed 1/16 mean a pixel keeps ~16-31 effective samples, so single pixels miss by up to
    /// ~0.1 at a long sky edge whatever the sequence (task 147 HANDOFF; the plan's sqrt(0.25/16) is 0.125, not 0.06).
    private void AssertMatchesQuadrature(int3 hit, int3 n, float q, string what)
    {
        const int seeds = 32;
        float se2 = 0f, sum = 0f;
        for (int i = 0; i < seeds; i++)
        {
            float e = Est(hit, n, Seed(i));
            se2 += (e - q) * (e - q); sum += e;
        }
        float rms = math.sqrt(se2 / seeds), mean = sum / seeds;
        TestContext.WriteLine($"{what}: quadrature {q:F3}, after {Frames} frames mean {mean:F3} rms err {rms:F3} ({seeds} seeds)");
        Assert.LessOrEqual(rms, 0.05f, $"{what}: rms error over {seeds} pixel seeds");
        Assert.AreEqual(q, mean, 0.025f, $"{what}: mean over {seeds} pixel seeds (bias)");
    }

    // ---------------------------------------------------------------------
    // The direction sequence: S2's contract
    // ---------------------------------------------------------------------

    [Test]
    public void Sequence_IntegerPartIsPinned()
    {
        // Values computed independently (integer arithmetic, Python) -- the shader must reproduce them.
        Assert.AreEqual(0x688990C0u, SkyVisReference.Hash(1u));
        Assert.AreEqual(0u, SkyVisReference.Hash(0u));
        (uint seed, uint idx, uint u, uint v)[] pins =
        {
            (0u, 1u, 6332372u, 4780166u), (0u, 15u, 2710904u, 4593638u),
            (12345u, 7u, 7140944u, 4434567u), (0xDEADBEEFu, 1000u, 6515649u, 7773531u),
        };
        foreach (var p in pins)
        {
            float2 uv = SkyVisReference.SamplePoint(p.seed, p.idx);
            Assert.AreEqual((p.u + 0.5f) / 8388608f, uv.x, $"u seed {p.seed} index {p.idx}");
            Assert.AreEqual((p.v + 0.5f) / 8388608f, uv.y, $"v seed {p.seed} index {p.idx}");
        }
    }

    [Test]
    public void Sequence_UnitDirectionsInsideTheFaceHemisphere_Deterministic()
    {
        int3[] ns = { Up, Down, PX, NX, PZ, new int3(0, 0, -1) };
        foreach (var n in ns)
            for (int i = 0; i < 8; i++)
                for (uint k = 0; k < 256; k++)
                {
                    float2 uv = SkyVisReference.SamplePoint(Seed(i), k);
                    Assert.IsTrue(uv.x > 0f && uv.x < 1f && uv.y > 0f && uv.y < 1f, $"uv {uv} strictly inside (0,1)");
                    float3 d = SkyVisReference.Direction(n, Seed(i), k);
                    Assert.AreEqual(1f, math.length(d), 1e-5f, $"n {n} unit");
                    Assert.Greater(math.dot(d, (float3)n), 0f, $"n {n} seed {i} k {k}: inside the hemisphere");
                    Assert.AreEqual(d, SkyVisReference.Direction(n, Seed(i), k), "deterministic");
                }
    }

    // ---------------------------------------------------------------------
    // (1) sealed room, (3) thin roof
    // ---------------------------------------------------------------------

    private void AssertSealedRoomZero(int lo, int hi, string what)
    {
        // interior lo+1 .. hi-1 on every axis; one point per face kind
        int c = (lo + hi) / 2;
        (int3 hit, int3 n, string name)[] pts =
        {
            (new int3(c, lo, c), Up, "floor centre"), (new int3(lo + 1, lo, lo + 1), Up, "floor corner"),
            (new int3(lo, c, c), PX, "wall"), (new int3(hi, hi - 1, lo + 1), NX, "wall top corner"),
            (new int3(c, hi, c), Down, "ceiling"),
        };
        foreach (var p in pts)
        {
            Assert.AreNotEqual(0, _store.GetVoxel(p.hit), $"{what} {p.name}: hit voxel solid");
            Assert.AreEqual(0, _store.GetVoxel(p.hit + p.n), $"{what} {p.name}: origin voxel air");
            Assert.AreEqual(0f, Q(p.hit, p.n), $"{what} {p.name}: quadrature");
            AssertEverySample(p.hit, p.n, 0f, 8, 256, $"{what} {p.name}");
        }
    }

    [Test]
    public void SealedRoom_ExactlyZero_EveryFaceFrameAndSeed()
    {
        Fill(new int3(200), new int3(320), Materials.Stone);
        Fill(new int3(220), new int3(300), 0);                   // 8.1 m room, 2 m of rock all round
        AssertSealedRoomZero(219, 301, "2 m rock room");
    }

    [Test]
    public void ThinShellRoom_0p1mRoofAndWalls_ExactlyZero()
    {
        Fill(new int3(200), new int3(320), Materials.Stone);
        Fill(new int3(201), new int3(319), 0);                   // 11.9 m room inside ONE voxel of stone
        Assert.AreEqual(0, _store.GetVoxel(new int3(201, 260, 260)), "the shell is one voxel thick");
        AssertSealedRoomZero(200, 320, "0.1 m shell room");
    }

    // ---------------------------------------------------------------------
    // (2) open ground, the horizon rule, a 1-voxel pillar
    // ---------------------------------------------------------------------

    [Test]
    public void OpenGround_ExactlyOne_VerticalFaceHalf_DownFaceZero_Pillar()
    {
        Fill(new int3(156, 80, 156), new int3(356, 99, 356), Materials.Stone);   // 2 m slab, top face y 99
        Assert.AreEqual(1f, Q(new int3(200, 99, 200), Up), "open ground: quadrature");
        AssertEverySample(new int3(200, 99, 200), Up, 1f, 8, 256, "open ground");

        Fill(new int3(256, 100, 256), new int3(256, 129, 256), Materials.Stone); // 1-voxel pillar, 3 m
        Fill(new int3(160, 110, 160), new int3(200, 119, 200), Materials.Stone); // a floating 1 m slab behind the pillar, below its top: its underside
        Assert.AreEqual(1f, Q(new int3(256, 129, 256), Up), "pillar top");
        AssertEverySample(new int3(256, 129, 256), Up, 1f, 4, 64, "pillar top");

        Assert.AreEqual(0.5f, Q(new int3(256, 110, 256), PX), "pillar side: upper half of a vertical hemisphere, exactly");
        Assert.AreEqual(0f, Q(new int3(180, 110, 180), Down), "down face: below the horizon is ground");
        AssertEverySample(new int3(180, 110, 180), Down, 0f, 4, 64, "down face");

        // the ground right beside the pillar: the pillar takes a real bite
        int3 foot = new int3(257, 99, 256);
        float q = Q(foot, Up);
        Assert.Greater(q, 0.5f, "beside a 0.1 m pillar: mostly open");
        Assert.Less(q, 1f, "beside a 0.1 m pillar: the pillar blocks some sky");
        AssertMatchesQuadrature(foot, Up, q, "pillar foot");
    }

    [Test]
    public void ThinWall_0p1m_BlocksExactlyAsA2mWall()
    {
        Fill(new int3(156, 80, 156), new int3(356, 99, 356), Materials.Stone);
        Fill(new int3(270, 100, 0), new int3(289, 139, 511), Materials.Stone);   // 2 m thick, 4 m tall, the window's full z
        (int3 hit, int3 n)[] pts =
        {
            (new int3(260, 99, 256), Up), (new int3(266, 99, 230), Up), (new int3(240, 99, 256), Up), (new int3(270, 110, 256), NX),
        };
        var thick = new float[pts.Length];
        var thickEst = new float[pts.Length];
        for (int i = 0; i < pts.Length; i++) { thick[i] = Q(pts[i].hit, pts[i].n); thickEst[i] = Est(pts[i].hit, pts[i].n, Seed(i)); }

        Fill(new int3(271, 100, 0), new int3(289, 139, 511), 0);                 // carve: 0.1 m left at x 270
        Assert.AreEqual(0, _store.GetVoxel(new int3(271, 120, 256)));
        for (int i = 0; i < pts.Length; i++)
        {
            Assert.AreEqual(thick[i], Q(pts[i].hit, pts[i].n), $"point {i}: thin = thick quadrature, exactly");
            Assert.AreEqual(thickEst[i], Est(pts[i].hit, pts[i].n, Seed(i)), $"point {i}: thin = thick running mean, exactly");
        }
        Assert.Less(thick[0], 1f, "the wall blocks sky at 1 m");
        Assert.Less(thick[0], thick[2], "less blocked further away");
        Assert.AreEqual(0.5f, thick[3], "the wall's own face sees the upper half");
    }

    // ---------------------------------------------------------------------
    // (4) a 3 m ledge
    // ---------------------------------------------------------------------

    private void BuildLedge()
    {
        Fill(new int3(156, 80, 156), new int3(356, 99, 356), Materials.Stone);
        Fill(new int3(160, 100, 0), new int3(199, 179, 511), Materials.Stone);   // back wall, 8 m, face at x 199
        Fill(new int3(200, 130, 0), new int3(229, 139, 511), Materials.Stone);   // ledge 3 m out, underside 3 m up
    }

    [Test]
    public void Ledge3m_FloorAndWallGraded_RunningMeanMatchesQuadrature()
    {
        BuildLedge();
        (int3 hit, int3 n, string name)[] pts =
        {
            (new int3(215, 99, 256), Up, "floor 1.5 m under the ledge"),
            (new int3(199, 110, 256), PX, "wall under the ledge"),
        };
        foreach (var p in pts)
        {
            float q = Q(p.hit, p.n);
            Assert.Greater(q, 0.1f, p.name);
            Assert.Less(q, 0.6f, p.name);
            AssertMatchesQuadrature(p.hit, p.n, q, p.name);
        }
        Assert.AreEqual(0f, Q(new int3(215, 130, 256), Down), "the ledge underside (a down face) is exactly 0");
        Assert.Less(Q(new int3(205, 99, 256), Up), Q(new int3(225, 99, 256), Up), "darker deeper under the lip");
    }

    // ---------------------------------------------------------------------
    // (5) a 2 m hole in a 10 m room's roof (owner Q3 (a): physical)
    // ---------------------------------------------------------------------

    private void BuildHoleRoom()
    {
        Fill(new int3(196, 80, 196), new int3(315, 144, 315), Materials.Stone);
        Fill(new int3(206, 100, 206), new int3(305, 139, 305), 0);               // room 10 x 4 x 10 m, roof 0.5 m
        Fill(new int3(246, 140, 246), new int3(265, 144, 265), 0);               // the 2 m hole
    }

    [Test]
    public void HoleInRoof_FloorBelowSeesSky_CornersUnder0p05()
    {
        BuildHoleRoom();
        int3 below = new int3(255, 99, 255);
        float qb = Q(below, Up);
        Assert.Greater(qb, 0f, "under the hole: sky");
        AssertMatchesQuadrature(below, Up, qb, "under the hole");
        int3[] corners = { new int3(206, 99, 206), new int3(305, 99, 305), new int3(206, 99, 305), new int3(305, 99, 206) };
        foreach (var c in corners)
        {
            float qc = Q(c, Up);
            Assert.Less(qc, 0.05f, $"corner {c}");
            Assert.Less(qc, qb, $"corner {c} darker than under the hole");
        }
    }

    // ---------------------------------------------------------------------
    // (7) the history
    // ---------------------------------------------------------------------

    [Test]
    public void History_KeyChangeRestarts_OneOverNThenFixed_ExactEnds()
    {
        var h = SkyVisReference.History.Empty;
        Assert.AreEqual(SkyVisReference.NO_KEY, h.key);
        Assert.AreNotEqual(SkyVisReference.NO_KEY, SkyVisReference.OriginKey(new int3(-1, -1, -1)), "no key equals none");
        Assert.AreEqual(0x7FFu | (0x1FFu << 11) | (0x7FFu << 20), SkyVisReference.OriginKey(new int3(-1, -1, -1)));
        Assert.AreEqual(5u | (7u << 11) | (9u << 20), SkyVisReference.OriginKey(new int3(5, 7, 9)));

        // 1/n: the first SETTLE_SAMPLES are the arithmetic mean
        float[] pattern = { 1, 0, 0, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1, 0, 1, 1 };
        float sum = 0f;
        for (int i = 0; i < pattern.Length; i++)
        {
            h.Accumulate(7u, pattern[i]);
            sum += pattern[i];
            Assert.AreEqual(i + 1, h.count);
            Assert.AreEqual(sum / (i + 1), h.vis, 1e-6f, $"after {i + 1}: the plain mean");
        }
        // then a fixed 1/16: from 0 (16 zeros), m ones give 1 - (15/16)^m
        h = SkyVisReference.History.Empty;
        for (int i = 0; i < 16; i++) h.Accumulate(9u, 0f);
        for (int m = 1; m <= 80; m++)
        {
            h.Accumulate(9u, 1f);
            Assert.AreEqual(SkyVisReference.SETTLE_SAMPLES, h.count);
            Assert.AreEqual(1f - math.pow(15f / 16f, m), h.vis, 1e-5f, $"{m} frames after the step (the dug-skylight lag)");
        }
        // a key change restarts: count 1, vis = the sample
        h.Accumulate(10u, 0f);
        Assert.AreEqual(10u, h.key); Assert.AreEqual(1, h.count); Assert.AreEqual(0f, h.vis);

        // 0 and 1 stay EXACT through any number of frames
        var one = SkyVisReference.History.Empty; var zero = SkyVisReference.History.Empty;
        for (int i = 0; i < 1000; i++) { one.Accumulate(3u, 1f); zero.Accumulate(3u, 0f); }
        Assert.AreEqual(1f, one.vis); Assert.AreEqual(0f, zero.vis);
    }

    // ---------------------------------------------------------------------
    // Task 138's built site (the edit ops of PlaygroundCapture.SkyTiltBuild, mouth +x) + the report
    // ---------------------------------------------------------------------

    private static readonly int3 C138 = new int3(256, 200, 256);   // ground top face y 199 (gv = 200)

    private void BuildSite138()
    {
        int3 C = C138, a = PX;
        Fill(new int3(96, 180, 96), new int3(480, 199, 416), Materials.Stone);  // ground 2 m
        Fill(new int3(176, 100, 176), new int3(336, 199, 336), Materials.Stone); // the rock under the block
        // SkyTiltBuild's own ops, verbatim (C, a)
        _ops.Record(new EditOp(EditOpKind.Box, C + new int3(0, 45, 0), 75, Materials.Stone));          // block g-3 .. g+12 m
        for (int k = 0; k < 3; k++)                                                                     // tunnel 4.1 m, 12.25 m deep
            _ops.Record(new EditOp(EditOpKind.Box, C + a * (75 - 20 - 41 * k) + new int3(0, 25, 0), 20, 0));
        _ops.Record(new EditOp(EditOpKind.Box, C + new int3(0, -60, 0), 25, 0));                         // sealed room g-8.5 .. g-3.5 m
    }

    private static readonly float[] TunnelDepthsM = { 0.5f, 1f, 2f, 3f, 6f, 9f, 12f };
    private static int3 TunnelFloor(float dM) => new int3(331 - (int)math.round(dM * 10f), 204, 256);

    [Test]
    public void Site138_TunnelDarkensInward_SealedRoomZero_OpenFaceHalf()
    {
        BuildSite138();
        Assert.AreEqual(0, _store.GetVoxel(new int3(331, 225, 256)), "the mouth is open");
        Assert.AreNotEqual(0, _store.GetVoxel(new int3(331, 225, 300)), "the block face beside it");

        Assert.AreEqual(0f, Q(new int3(256, 114, 256), Up), "sealed room floor");
        Assert.AreEqual(0f, Q(new int3(230, 140, 256), PX), "sealed room wall");
        Assert.AreEqual(0.5f, Q(new int3(331, 225, 300), PX), "the open block face");
        Assert.AreEqual(1f, Q(new int3(256, 320, 256), Up), "the block top");

        float prev = float.MaxValue;
        foreach (float d in TunnelDepthsM)
        {
            float q = Q(TunnelFloor(d), Up);
            Assert.LessOrEqual(q, prev, $"tunnel floor {d} m in: no brighter than nearer the mouth");
            prev = q;
        }
        Assert.Less(Q(TunnelFloor(12f), Up), Q(TunnelFloor(0.5f), Up), "the deep end is darker than the mouth");
    }

    [Test]
    [Category("Slow")]
    public void Report_QuadratureTable()
    {
        if (Environment.GetEnvironmentVariable("REPORTS") != "1") Assert.Ignore("report only: set REPORTS=1");
        var sb = new StringBuilder();
        sb.AppendLine("Task 147 (SOFT_SKY_PLAN S1): soft-sky CPU oracle at task 138's built site + the test shapes.");
        sb.AppendLine("quad = 1,024-direction cosine quadrature (the truth). mc = the running mean (1/n then 1/16) after 64 frames,");
        sb.AppendLine("mean / rms error vs quad / min..max over 32 pixel seeds. lift = quadrature if below-horizon directions were traced at y = 1e-6");
        sb.AppendLine("(TraceShadowRay's sun clamp) instead of counting as ground -- NOT the defined quantity, shown for S2.");
        sb.AppendLine("today = the vertical probe's 0/1 (Raymarch.compute, _SkyProbeTilt 0). Clamp 40 m, 0.1 m voxels, edit-op built.");
        sb.AppendLine();
        sb.AppendLine($"{"point",-46} {"today",5} {"quad",6} {"mc",6} {"rmserr",6} {"min..max",12} {"lift",6}");

        void Row(string name, int3 hit, int3 n)
        {
            float q = Q(hit, n), lift = Q(hit, n, lift: true);
            float today = ShadowRayReference.TraceSkyProbe(_store, hit, n, new float3(0, 0, 1), 0, Cam(hit, n), ClampM).occluded ? 0f : 1f;
            const int seeds = 32;
            float s = 0f, s2 = 0f, lo = 1f, hi = 0f;
            for (int i = 0; i < seeds; i++)
            {
                float e = Est(hit, n, Seed(i));
                s += e; s2 += (e - q) * (e - q); lo = math.min(lo, e); hi = math.max(hi, e);
            }
            float mean = s / seeds, sd = math.sqrt(s2 / seeds);
            sb.AppendLine($"{name,-46} {today,5:F0} {q,6:F3} {mean,6:F3} {sd,6:F3} {lo,5:F3}..{hi,5:F3} {lift,6:F3}");
        }

        BuildSite138();
        sb.AppendLine("-- task 138's site: 15.1 m block, 4.1 m x 12.3 m tunnel (mouth faces +x), sealed 5.1 m room under it");
        Row("sealed room: floor centre", new int3(256, 114, 256), Up);
        Row("sealed room: wall", new int3(230, 140, 256), PX);
        Row("sealed room: ceiling (down face)", new int3(256, 166, 256), Down);
        foreach (float d in TunnelDepthsM) Row($"tunnel floor {d:0.#} m in", TunnelFloor(d), Up);
        Row("tunnel side wall 3 m in", new int3(301, 225, 235), PZ);
        Row("tunnel ceiling 3 m in (down face)", new int3(301, 246, 256), Down);
        Row("tunnel end wall 12.3 m in", new int3(208, 225, 256), PX);
        Row("mouth lip underside (down face)", new int3(328, 246, 256), Down);
        Row("block face beside the mouth (open)", new int3(331, 205, 300), PX);
        Row("ground 1 m in front of the mouth", new int3(342, 199, 256), Up);
        Row("ground 3 m in front of the mouth", new int3(362, 199, 256), Up);
        Row("ground 10 m in front of the mouth", new int3(432, 199, 256), Up);
        Row("block top", new int3(256, 320, 256), Up);

        NewWorld(); BuildLedge();
        sb.AppendLine("-- the 3 m ledge (test 4): 8 m wall, ledge 3 m out, underside 3 m up, 51 m long");
        Row("floor 0.5 m in from the lip", new int3(225, 99, 256), Up);
        Row("floor 1.5 m under the ledge", new int3(215, 99, 256), Up);
        Row("floor at the wall foot (2.9 m in)", new int3(201, 99, 256), Up);
        Row("wall under the ledge, 1 m up", new int3(199, 110, 256), PX);
        Row("ledge underside (down face)", new int3(215, 130, 256), Down);
        Row("ledge front face", new int3(229, 135, 256), PX);
        Row("ground 3 m out from the lip", new int3(260, 99, 256), Up);

        NewWorld(); BuildHoleRoom();
        sb.AppendLine("-- the 10 m room, 4 m tall, 0.5 m roof, 2 m hole in its centre (test 5)");
        Row("floor below the hole", new int3(255, 99, 255), Up);
        Row("floor 2.5 m off the hole axis", new int3(280, 99, 255), Up);
        Row("floor corner", new int3(206, 99, 206), Up);
        Row("wall middle", new int3(205, 120, 255), PX);

        string dir = Path.Combine(Directory.GetCurrentDirectory(), "Screenshots", "147");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "softsky_quadrature147.txt"), sb.ToString());
        TestContext.WriteLine(sb.ToString());
    }
}
