// ==========================================
// SkyFillReferenceTests.cs -- task 193 (`-skyfill 1|2`, OFF by default; SOFT_SKY_PLAN S2 + ground bounce)
//
// The CPU twin of Raymarch.compute's SKY FILL (SkyFillReference). Every shape is BUILT with Box edit
// ops (the generality rule), never taken from a generator. Adversarial cases, both modes:
//   - a sealed 2 m-rock room and a 0.1 m shell room: EXACTLY 0 for every face, frame (no leak)
//   - a 1-voxel crack through a 1.1 m roof: light only where the crack is in view; 2 m aside exactly 0
//   - open ground and an open pillar side over sunlit ground: EXACTLY 1 (today's value, no noise)
//   - a 3 m ledge: its underside (a down face) is 0 in mode 1 (as today) and graded in mode 2;
//     the running mean matches the quadrature
//   - a deep narrow trench: walls darken with depth, never exactly 0
//   - a lattice canopy (a crown with gaps): the ground and a trunk under it are graded, not 0
// plus the GPU contracts: the fold, the half schedule, the 12-bit record, the entry-face rule.
// ==========================================

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;

public class SkyFillReferenceTests
{
    private static readonly int3 WindowBricks = new int3(64, 64, 64);   // 51.2 m cube, voxels 0..511
    private const float ClampM = 40f;
    private const int Frames = 64;
    private const int SKY = SkyFillReference.SKY, BOTH = SkyFillReference.SKY_AND_BOUNCE;

    private static readonly int3 Up = new int3(0, 1, 0), Down = new int3(0, -1, 0);
    private static readonly int3 PX = new int3(1, 0, 0), NX = new int3(-1, 0, 0);
    private static readonly float3 Sun = math.normalize(new float3(0.3f, 0.9f, 0.2f));   // high, off-axis

    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditOps _ops;

    [SetUp]
    public void Setup()
    {
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
        _ops = new EditOps(EditOpLog.InMemory(193u), edits, _store);
    }

    [TearDown]
    public void Teardown() => _pool?.Dispose();

    /// The inclusive box lo..hi as Box edit ops (SkyVisReferenceTests.Fill).
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

    private float QS(int3 hit, int3 n) => SkyFillReference.SkyQuadrature(_store, hit, n, Cam(hit, n), ClampM);
    private float QB(int3 hit, int3 n) => SkyFillReference.GroundQuadrature(_store, hit, n, Sun, Cam(hit, n), ClampM);

    /// Hemisphere weights (FarShadeSky / CubeBounceLight shapes; sky luma 0.5, sun luma 1, bounce 0.25).
    private static (float ws, float wb) W(int3 n) => SkyFillReference.Weights(n, 0.5f, 1f, 0.25f, Sun);

    private float Est(int mode, int3 hit, int3 n, int frames = Frames, uint first = 0)
    {
        var w = W(n);
        return SkyFillReference.Estimate(mode, _store, hit, n, frames, Sun, Cam(hit, n), ClampM, w.ws, w.wb, first);
    }

    /// Every sample of every frame equals `want`, and so does the history, in `mode`.
    private void AssertEverySample(int mode, int3 hit, int3 n, float want, int frames, string what)
    {
        uint seed = SkyFillReference.Seed(hit, n);
        for (uint k = 0; k < frames; k++)
        {
            if (mode == SKY && n.y < 0) break;
            int half = SkyFillReference.Half(mode, n, k);
            float3 d = SkyFillReference.Direction(mode, n, seed, k, half);
            float s = SkyFillReference.Sample(half, _store, hit, n, d, Sun, Cam(hit, n), ClampM);
            if (s != want) Assert.Fail($"{what} mode {mode}: frame {k} half {half} sample {s}, want exactly {want}");
        }
        Assert.AreEqual(want, Est(mode, hit, n, frames), $"{what} mode {mode}: history");
    }

    /// The running mean after 64 frames matches `truth` over `cells` neighbouring cells of one uniform
    /// surface (each cell its own seed): RMS <= tol, mean within tol / 2 (no bias). 0.07: a 1/16 running mean
    /// keeps ~16-31 effective samples, so at p ~ 0.5 a single cell scatters ~0.06 even with R2's strata.
    private void AssertMatches(int mode, int3 hit0, int3 step, int3 n, float truth, string what, int cells = 32, float tol = 0.07f) =>
        AssertMatches(mode, hit0, step, n, _ => truth, what, cells, tol);

    private void AssertMatches(int mode, int3 hit0, int3 step, int3 n, System.Func<int3, float> truthOf, string what, int cells, float tol)
    {
        float se2 = 0f, sum = 0f, tsum = 0f;
        for (int i = 0; i < cells; i++)
        {
            int3 h = hit0 + step * i;
            float truth = truthOf(h), e = Est(mode, h, n);
            se2 += (e - truth) * (e - truth); sum += e; tsum += truth;
        }
        float rms = math.sqrt(se2 / cells), mean = sum / cells, tmean = tsum / cells;
        TestContext.WriteLine($"{what} mode {mode}: truth mean {tmean:F3}, after {Frames} frames mean {mean:F3} rms {rms:F3} ({cells} cells)");
        Assert.LessOrEqual(rms, tol, $"{what}: rms over {cells} cells");
        Assert.AreEqual(tmean, mean, tol * 0.5f, $"{what}: mean over {cells} cells (bias)");
    }

    private float Mix(int3 n, float s, float b) { var w = W(n); return SkyFillReference.Open(BOTH, n, s, 1, b, 1, w.ws, w.wb); }

    // ---------------------------------------------------------------------
    // GPU contracts
    // ---------------------------------------------------------------------

    [Test]
    public void Contract_FoldHalvesRecordAndEntryFace()
    {
        // The half schedule: up = sky always, down = ground in mode 2, side alternates in mode 2 only.
        for (uint f = 0; f < 8; f++)
        {
            Assert.AreEqual(0, SkyFillReference.Half(BOTH, Up, f));
            Assert.AreEqual(1, SkyFillReference.Half(BOTH, Down, f));
            Assert.AreEqual((int)(f & 1), SkyFillReference.Half(BOTH, PX, f));
            Assert.AreEqual(0, SkyFillReference.Half(SKY, PX, f));
        }
        // The fold: inside the face's hemisphere, on the requested side of the horizon, unit length.
        int3[] ns = { Up, Down, PX, NX, new int3(0, 0, 1), new int3(0, 0, -1) };
        foreach (var n in ns)
            for (uint k = 0; k < 256; k++)
                for (int half = 0; half < 2; half++)
                {
                    if (n.y > 0 && half == 1 || n.y < 0 && half == 0) continue;
                    float3 d = SkyFillReference.Direction(BOTH, n, 12345u + (uint)n.x * 7u, k, half);
                    Assert.AreEqual(1f, math.length(d), 1e-5f);
                    Assert.Greater(math.dot(d, (float3)n), 0f, $"n {n}: inside the hemisphere");
                    if (n.y == 0) Assert.IsTrue(half == 0 ? d.y >= 0f : d.y <= 0f, $"n {n} half {half}: folded, d.y {d.y}");
                }
        // The record: 0 and 1 stay exact through the fold, the pack round-trips, the count caps at 15.
        var r = SkyFillReference.Record.Empty;
        for (int i = 0; i < 40; i++) r.Accumulate(77u, 0, 1f);
        for (int i = 0; i < 40; i++) r.Accumulate(77u, 1, 0f);
        Assert.AreEqual(1f, r.s); Assert.AreEqual(0f, r.b);
        Assert.AreEqual(15u, r.cs); Assert.AreEqual(15u, r.cb);
        var u = SkyFillReference.Record.Unpack(r.Pack());
        Assert.AreEqual(r.s, u.s); Assert.AreEqual(r.b, u.b); Assert.AreEqual(r.cs, u.cs); Assert.AreEqual(r.cb, u.cb); Assert.AreEqual(77u, u.key);
        Assert.AreEqual(0u, r.Pack().y >> 31, "a key never equals the 0xFFFFFFFF none");
        r.Accumulate(78u, 1, 1f);
        Assert.AreEqual(0u, r.cs, "a new key restarts both halves"); Assert.AreEqual(1u, r.cb); Assert.AreEqual(1f, r.b);
        // 1/n for the first samples: 1, 0 -> 0.5 (12-bit: 2048 / 4095).
        var h = SkyFillReference.Record.Empty;
        h.Accumulate(5u, 0, 1f); h.Accumulate(5u, 0, 0f);
        Assert.AreEqual(math.round(0.5f * 4095f) / 4095f, h.s);
        // A half with no sample takes the other's value.
        Assert.AreEqual(0.25f, SkyFillReference.Open(BOTH, PX, 0.25f, 3, 0.9f, 0, 1f, 1f));

        // The entry face: the shader's geometric rule equals the DDA's exact entry face on a rough surface.
        Fill(new int3(100, 80, 100), new int3(400, 99, 400), Materials.Stone);
        var rng = new Unity.Mathematics.Random(193u);
        for (int i = 0; i < 300; i++)
        {
            int3 c = new int3(rng.NextInt(150, 350), 100 + rng.NextInt(0, 6), rng.NextInt(150, 350));
            Fill(c, c + new int3(rng.NextInt(0, 4), 0, rng.NextInt(0, 4)), Materials.Stone);   // bumps
        }
        int checkedHits = 0, mismatches = 0;
        for (int i = 0; i < 2000; i++)
        {
            int3 o = new int3(rng.NextInt(180, 320), 130, rng.NextInt(180, 320));
            float3 d = math.normalize(new float3(rng.NextFloat(-1f, 1f), -rng.NextFloat(0.05f, 1f), rng.NextFloat(-1f, 1f)));
            var g = SkyFillReference.FirstSolid(_store, o - Up, Up, d, (new float3(o) + 0.5f) * 0.1f, ClampM);
            if (!g.found) continue;
            checkedHits++;
            Assert.AreEqual(0, _store.GetVoxel(g.voxel + g.entry), $"ray {i}: the bounce origin is air");
            if (math.any(g.entry != SkyFillReference.EntryNormal(o, d, g.voxel, g.t))) mismatches++;   // only a ray through an edge can tie
        }
        TestContext.WriteLine($"entry face: {checkedHits} hits, {mismatches} geometric/DDA disagreements");
        Assert.Greater(checkedHits, 1500);
        Assert.LessOrEqual(mismatches, checkedHits / 500, "the geometric entry face is the DDA's (edge ties aside)");
    }

    // ---------------------------------------------------------------------
    // Caves stay dark: exactly 0, no leak through rock
    // ---------------------------------------------------------------------

    private void AssertSealedZero(int lo, int hi, string what)
    {
        int c = (lo + hi) / 2;
        (int3 hit, int3 n, string name)[] pts =
        {
            (new int3(c, lo, c), Up, "floor"), (new int3(lo + 1, lo, lo + 1), Up, "floor corner"),
            (new int3(lo, c, c), PX, "wall"), (new int3(hi, hi - 1, lo + 1), NX, "wall top corner"),
            (new int3(c, hi, c), Down, "ceiling"),
        };
        foreach (var p in pts)
        {
            Assert.AreNotEqual(0, _store.GetVoxel(p.hit), $"{what} {p.name}: hit solid");
            Assert.AreEqual(0, _store.GetVoxel(p.hit + p.n), $"{what} {p.name}: origin air");
            if (p.n.y >= 0) Assert.AreEqual(0f, QS(p.hit, p.n), $"{what} {p.name}: S quadrature");
            if (p.n.y <= 0) Assert.AreEqual(0f, QB(p.hit, p.n), $"{what} {p.name}: B quadrature");
            AssertEverySample(SKY, p.hit, p.n, 0f, 64, $"{what} {p.name}");
            AssertEverySample(BOTH, p.hit, p.n, 0f, 64, $"{what} {p.name}");
        }
    }

    [Test]
    public void SealedRooms_RockAndThinShell_ExactlyZero_BothModes()
    {
        Fill(new int3(200), new int3(320), Materials.Stone);
        Fill(new int3(220), new int3(300), 0);
        AssertSealedZero(219, 301, "2 m rock room");

        Fill(new int3(200), new int3(320), Materials.Stone);
        Fill(new int3(201), new int3(319), 0);                    // one voxel of stone all round
        AssertSealedZero(200, 320, "0.1 m shell room");
    }

    [Test]
    public void Crack1Voxel_InA1mRoof_LightsOnlyWhereTheCrackIsInView()
    {
        Fill(new int3(200, 150, 200), new int3(320, 200, 320), Materials.Stone);
        Fill(new int3(210, 160, 210), new int3(310, 189, 310), 0);    // 3 m room; roof y 190..200 (1.1 m)
        Fill(new int3(260, 190, 210), new int3(260, 200, 310), 0);    // a 0.1 m crack through the roof, along z
        // The slit is ~1 % of the cosine lobe: the 32 x 32 quadrature grid steps over it, so the sliver is
        // counted with the shader's own sequence over 2,048 frames (low discrepancy: it does hit the slit).
        int3 u = new int3(260, 159, 260);
        Assert.AreEqual(1f, SkyFillReference.SkySample(_store, u, Up, new float3(0f, 1f, 0f), Cam(u, Up), ClampM), "straight up through the crack");
        uint seed = SkyFillReference.Seed(u, Up);
        int open = 0;
        for (uint k = 0; k < 2048; k++)
            open += (int)SkyFillReference.SkySample(_store, u, Up, SkyFillReference.Direction(SKY, Up, seed, k, 0), Cam(u, Up), ClampM);
        float under = open / 2048f;
        float aside = QS(new int3(240, 159, 260), Up);
        TestContext.WriteLine($"floor under the crack S {under:F4} (2,048 frames), 2 m aside {aside:F4} (quadrature)");
        Assert.Greater(under, 0f, "the crack is a real opening: the floor under it sees a sliver of sky");
        Assert.Less(under, 0.05f, "but only a sliver");
        Assert.AreEqual(0f, aside, "2 m aside the crack is out of view: exactly 0");
        AssertEverySample(SKY, new int3(240, 159, 260), Up, 0f, 64, "2 m aside");
        AssertEverySample(BOTH, new int3(240, 159, 260), Up, 0f, 64, "2 m aside");
        AssertEverySample(BOTH, new int3(240, 190, 260), Down, 0f, 64, "ceiling 2 m aside (its ground is the dark floor)");
    }

    // ---------------------------------------------------------------------
    // Open places keep today's value exactly
    // ---------------------------------------------------------------------

    [Test]
    public void OpenGroundAndPillarSide_ExactlyOne_BothModes()
    {
        Fill(new int3(100, 80, 100), new int3(400, 99, 400), Materials.Stone);      // 2 m slab, top y 99
        Fill(new int3(256, 100, 256), new int3(256, 129, 256), Materials.Stone);    // 1-voxel pillar, 3 m
        AssertEverySample(SKY, new int3(200, 99, 200), Up, 1f, 64, "open ground");
        AssertEverySample(BOTH, new int3(200, 99, 200), Up, 1f, 64, "open ground");
        // The pillar's side on the sun's side (+x, the sun leans +x): its ground half lands on sunlit
        // slab out to the clamp, its sky half is open.
        Assert.AreEqual(1f, QS(new int3(256, 125, 256), PX), "pillar side: S, open = 1 (not 0.5)");
        float qb = QB(new int3(256, 125, 256), PX);
        TestContext.WriteLine($"pillar side +x: B {qb:F4} (its own shadow falls on -x)");
        Assert.AreEqual(1f, qb, "pillar side: B (the ground it sees is in sun)");
        AssertEverySample(BOTH, new int3(256, 125, 256), PX, 1f, 64, "pillar side");
        // The folded side quadrature is SkyVisReference's (upper half only) x 2.
        Fill(new int3(160, 100, 160), new int3(200, 119, 200), Materials.Stone);    // a block 6 m away
        float sv = SkyVisReference.Quadrature(_store, new int3(256, 105, 256), NX, Cam(new int3(256, 105, 256), NX), ClampM);
        Assert.AreEqual(2f * sv, QS(new int3(256, 105, 256), NX), 0.01f, "S(side) = 2 x skyVis(side)");
    }

    // ---------------------------------------------------------------------
    // Overhang: the underside is today's black in mode 1, graded in mode 2
    // ---------------------------------------------------------------------

    [Test]
    public void Ledge3m_UndersideAndWall_Graded_RunningMeanMatchesQuadrature()
    {
        Fill(new int3(100, 80, 0), new int3(400, 99, 511), Materials.Stone);
        Fill(new int3(160, 100, 0), new int3(199, 179, 511), Materials.Stone);   // back wall, face x 199
        Fill(new int3(200, 130, 0), new int3(229, 139, 511), Materials.Stone);   // ledge 3 m out, underside y 130
        int3 under = new int3(215, 130, 200);
        float b = QB(under, Down);
        TestContext.WriteLine($"ledge underside: B {b:F3}");
        Assert.AreEqual(0f, Est(SKY, under, Down), "mode 1: a down face keeps today's 0");
        Assert.Greater(b, 0.1f, "mode 2: the underside sees sunlit ground beyond the lip");
        Assert.Less(b, 0.9f, "but not only sunlit ground (the ledge shades the floor under it)");
        AssertMatches(BOTH, under, new int3(0, 0, 1), Down, b, "ledge underside");

        int3 wall = new int3(199, 110, 200);
        float s = QS(wall, PX), bw = QB(wall, PX);
        TestContext.WriteLine($"wall under the ledge: S {s:F3} B {bw:F3}");
        Assert.Greater(s, 0.2f); Assert.Less(s, 0.9f);
        AssertMatches(SKY, wall, new int3(0, 0, 1), PX, s, "wall under the ledge");
        AssertMatches(BOTH, wall, new int3(0, 0, 1), PX, Mix(PX, s, bw), "wall under the ledge");
        Assert.Less(QS(new int3(205, 99, 200), Up), QS(new int3(225, 99, 200), Up), "the floor darkens deeper under the lip");
    }

    // ---------------------------------------------------------------------
    // A deep narrow trench: darker with depth, never black
    // ---------------------------------------------------------------------

    [Test]
    public void DeepTrench_1mWide6mDeep_DarkensWithDepth_NeverZero()
    {
        Fill(new int3(100, 80, 0), new int3(400, 199, 511), Materials.Stone);    // top y 199
        Fill(new int3(250, 140, 0), new int3(259, 199, 511), 0);                 // a 1 m slot, 6 m deep, floor y 139
        float prev = 2f;
        foreach (int y in new[] { 190, 170, 150, 141 })
        {
            float s = QS(new int3(249, y, 256), PX);
            TestContext.WriteLine($"trench wall {(199 - y) * 0.1f:F1} m down: S {s:F4} B {QB(new int3(249, y, 256), PX):F4}");
            Assert.Greater(s, 0f, $"y {y}: a wall of an open trench always sees some sky");
            Assert.Less(s, prev, $"y {y}: darker deeper");
            prev = s;
        }
        float floor = QS(new int3(255, 139, 256), Up);
        TestContext.WriteLine($"trench floor: S {floor:F4}");
        Assert.Greater(floor, 0f);
        Assert.Less(floor, 0.1f, "a 1 m x 6 m slot is a dark place: physical, not tuned");
        AssertMatches(SKY, new int3(249, 170, 200), new int3(0, 0, 1), PX, QS(new int3(249, 170, 200), PX), "trench wall 3 m down");
    }

    // ---------------------------------------------------------------------
    // Under a crown: a lattice canopy with gaps
    // ---------------------------------------------------------------------

    [Test]
    public void LatticeCanopy_GroundTrunkAndUnderside_Graded()
    {
        Fill(new int3(100, 80, 100), new int3(400, 99, 400), Materials.Stone);    // ground, top y 99
        Fill(new int3(220, 140, 220), new int3(292, 145, 292), Materials.Stone);  // a 7 m x 7 m crown slab, 4 m up
        for (int x = 222; x < 292; x += 8)                                         // 0.4 m gaps on a 0.8 m grid
        for (int z = 222; z < 292; z += 8)
            Fill(new int3(x, 140, z), new int3(x + 3, 145, z + 3), 0);
        Fill(new int3(256, 100, 256), new int3(257, 139, 257), Materials.Stone);  // a 0.2 m trunk
        int3 g = new int3(250, 99, 250);
        float sg = QS(g, Up);
        float st = QS(new int3(257, 120, 256), PX), bt = QB(new int3(257, 120, 256), PX);
        float bu = QB(new int3(236, 140, 236), Down);   // a solid crown voxel (gaps are x, z = 222 + 8k .. +3)
        TestContext.WriteLine($"under the canopy: ground S {sg:F3}; trunk S {st:F3} B {bt:F3}; crown underside B {bu:F3}");
        Assert.Greater(sg, 0.05f, "ground under the crown: sky through the gaps and from the side");
        Assert.Less(sg, 0.9f);
        Assert.Greater(st, 0.2f, "the trunk sees the open sky beyond the crown's edge");
        Assert.Greater(bu, 0.05f, "the crown's underside sees sun on the ground around (mode 2)");
        AssertMatches(SKY, g, new int3(1, 0, 0), Up, h => QS(h, Up), "ground under the canopy", 8, 0.08f);
    }
}
