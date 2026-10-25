// ==========================================
// SkyProbeTiltTests.cs -- task 138 (owner Q89 (a), 2026-10-24)
//
// The sky-visibility probe (Raymarch.compute's AMBIENT SKY VISIBILITY block)
// with `-skytilt 1`: side faces probe 45 deg up and out of the face, down faces
// horizontally toward the viewer, up faces straight up. Pinned on the CPU twin
// (ShadowRayReference.SkyProbeDir / TraceSkyProbe) with hand-built shapes that
// are not taken from any generator: an undercut under a ledge (task 136's black
// shape), an arch underside, a sealed cave (must stay dark from every face and
// every view), and an open cliff face (must stay open).
// ==========================================

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class SkyProbeTiltTests
{
    private static readonly int3 WindowBricks = new int3(64, 64, 64);
    private const float ClampM = 30f;

    private BrickDataPool _pool;
    private ChunkHandleAllocator _allocator;
    private ChunkStore _store;

    [SetUp]
    public void Setup()
    {
        _pool = new BrickDataPool(80000);
        _allocator = new ChunkHandleAllocator(256);
        _store = new ChunkStore(_pool, _allocator);
        int3 windowChunks = WindowBricks / 16;
        for (int cz = 0; cz < windowChunks.z; cz++)
        for (int cy = 0; cy < windowChunks.y; cy++)
        for (int cx = 0; cx < windowChunks.x; cx++)
            _store.InsertChunk(new Chunk { coord = new int3(cx, cy, cz), isUniform = true, uniformMaterial = 0 });
    }

    [TearDown]
    public void Teardown() => _pool.Dispose();

    private void Box(int3 min, int3 maxExcl, byte m)
    {
        for (int z = min.z; z < maxExcl.z; z++)
        for (int y = min.y; y < maxExcl.y; y++)
        for (int x = min.x; x < maxExcl.x; x++)
            _store.SetVoxel(new int3(x, y, z), m);
    }

    private static float3 Cam(int3 v) => (new float3(v.x, v.y, v.z) + 0.5f) * 0.1f;

    private bool Open(int3 hit, int3 n, float3 rayDir, int tilt) =>
        !ShadowRayReference.TraceSkyProbe(_store, hit, n, rayDir, tilt, Cam(hit + n), ClampM).occluded;

    // ---------------------------------------------------------------------
    // Directions
    // ---------------------------------------------------------------------

    [Test]
    public void Direction_OffIsStraightUpForEveryFace()
    {
        var up = new float3(0f, 1f, 0f);
        int3[] ns = { new int3(0, 1, 0), new int3(0, -1, 0), new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
        foreach (var n in ns)
            Assert.AreEqual(up, ShadowRayReference.SkyProbeDir(n, math.normalize(new float3(0.3f, 0.5f, 0.2f)), 0), $"n {n}");
    }

    [Test]
    public void Direction_TiltUpSideDown()
    {
        float3 view = math.normalize(new float3(0.6f, 0.5f, -0.8f));
        Assert.AreEqual(new float3(0f, 1f, 0f), ShadowRayReference.SkyProbeDir(new int3(0, 1, 0), view, 1), "up face: straight up");

        int3[] sides = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
        foreach (var n in sides)
        {
            float3 d = ShadowRayReference.SkyProbeDir(n, view, 1);
            Assert.AreEqual(1f, math.length(d), 1e-5f, $"side {n}: unit");
            Assert.AreEqual(0.70710678f, d.y, 1e-5f, $"side {n}: 45 deg up");
            Assert.AreEqual(0.70710678f, math.dot(d, (float3)n), 1e-5f, $"side {n}: 45 deg out of the face");
        }

        float3 down = ShadowRayReference.SkyProbeDir(new int3(0, -1, 0), view, 1);
        Assert.AreEqual(0f, down.y, "down face: horizontal");
        Assert.AreEqual(1f, math.length(down), 1e-5f, "down face: unit");
        float2 toViewer = math.normalize(-view.xz);
        Assert.AreEqual(1f, math.dot(down.xz, toViewer), 1e-5f, "down face: toward the viewer");

        Assert.AreEqual(new float3(1f, 0f, 0f),
            ShadowRayReference.SkyProbeDir(new int3(0, -1, 0), new float3(1e-6f, 1f, 1e-6f), 1),
            "view straight up: the deterministic +x fallback");
    }

    // ---------------------------------------------------------------------
    // Shapes
    // ---------------------------------------------------------------------

    /// Task 136's black shape: a wall face with a ledge 20 m straight above it
    /// that overhangs it by 4 m. The vertical probe hits the ledge; the tilted
    /// one passes under its lip and sees sky.
    [Test]
    public void UndercutUnderALedge_VerticalBlocked_TiltedOpen()
    {
        Box(new int3(160, 0, 0), new int3(320, 32, 512), Materials.Stone);        // ground
        Box(new int3(160, 0, 296), new int3(320, 320, 336), Materials.Stone);     // the wall, its face at z = 296 (n -z)
        Box(new int3(160, 248, 256), new int3(320, 272, 296), Materials.Stone);   // the ledge: 4 m out, 20 m up
        int3 hit = new int3(240, 48, 296), n = new int3(0, 0, -1);
        float3 view = math.normalize(new float3(0f, -0.1f, 1f));                 // looking at the wall

        Assert.IsFalse(Open(hit, n, view, 0), "vertical probe: the ledge above blocks it (task 136)");
        Assert.IsTrue(Open(hit, n, view, 1), "tilted probe: out under the ledge's lip to the sky");
    }

    /// An arch underside (down face). Vertical: its own voxel blocks it, always.
    /// Tilted: horizontally toward the viewer, out from under the span.
    [Test]
    public void ArchUnderside_TiltedOpenTowardTheViewer()
    {
        Box(new int3(0, 0, 0), new int3(512, 16, 512), Materials.Stone);           // ground
        Box(new int3(152, 16, 240), new int3(176, 200, 264), Materials.Stone);    // pillar -x
        Box(new int3(328, 16, 240), new int3(352, 200, 264), Materials.Stone);    // pillar +x
        Box(new int3(152, 200, 240), new int3(352, 224, 264), Materials.Stone);   // the span, along x
        int3 hit = new int3(256, 200, 248), n = new int3(0, -1, 0);

        float3 viewAcross = math.normalize(new float3(0f, 0.4f, 1f));             // from the -z side, looking up at it
        Assert.IsFalse(Open(hit, n, viewAcross, 0), "vertical probe from a down face hits the face's own voxel");
        Assert.IsTrue(Open(hit, n, viewAcross, 1), "tilted: toward the viewer, out from under the 2.4 m wide span");

        // Viewed along the span, the probe runs under it into a pillar: dark. Pinned, not a goal --
        // the probe is binary and has one direction; this is the view dependence of the down-face rule.
        float3 viewAlong = math.normalize(new float3(1f, 0.4f, 0f));
        Assert.IsFalse(Open(hit, n, viewAlong, 1), "along the span: the -x pillar blocks it");
    }

    /// A sealed cave stays dark from every face and every view direction.
    [Test]
    public void SealedCave_EveryFaceEveryViewStaysDark()
    {
        Box(new int3(160, 160, 160), new int3(352, 352, 352), Materials.Stone);
        Box(new int3(208, 208, 208), new int3(304, 304, 304), 0);                 // the hollow, 9.6 m
        var faces = new (int3 hit, int3 n)[]
        {
            (new int3(256, 207, 256), new int3(0, 1, 0)),    // floor
            (new int3(256, 304, 256), new int3(0, -1, 0)),   // ceiling
            (new int3(207, 256, 256), new int3(1, 0, 0)),
            (new int3(304, 256, 256), new int3(-1, 0, 0)),
            (new int3(256, 256, 207), new int3(0, 0, 1)),
            (new int3(256, 256, 304), new int3(0, 0, -1)),
            (new int3(208, 207, 208), new int3(0, 1, 0)),    // a floor corner
            (new int3(303, 304, 208), new int3(0, -1, 0)),   // a ceiling corner
        };
        float3[] views =
        {
            math.normalize(new float3(1f, 0.3f, 0.2f)), math.normalize(new float3(-0.4f, 0.8f, 0.4f)),
            math.normalize(new float3(0.2f, -0.6f, -1f)), new float3(1e-6f, 1f, 1e-6f),
        };
        foreach (var (hit, n) in faces)
        foreach (var v in views)
        {
            Assert.IsFalse(Open(hit, n, v, 1), $"tilted: face {hit} n {n} view {v}");
            Assert.IsFalse(Open(hit, n, v, 0), $"vertical: face {hit} n {n} view {v}");
        }
    }

    /// An open cliff face with nothing above or in front of it: open both ways.
    /// The top of the cliff (an up face) keeps the vertical probe.
    [Test]
    public void OpenCliffFace_OpenBothWays()
    {
        Box(new int3(0, 0, 0), new int3(512, 32, 512), Materials.Stone);
        Box(new int3(160, 0, 296), new int3(352, 240, 512), Materials.Stone);     // cliff, face at z = 296
        float3 view = math.normalize(new float3(0.2f, -0.2f, 1f));
        foreach (int y in new[] { 40, 120, 232 })
        {
            int3 hit = new int3(256, y, 296);
            Assert.IsTrue(Open(hit, new int3(0, 0, -1), view, 0), $"vertical, y {y}");
            Assert.IsTrue(Open(hit, new int3(0, 0, -1), view, 1), $"tilted, y {y}");
        }
        int3 top = new int3(256, 239, 400);
        Assert.IsTrue(Open(top, new int3(0, 1, 0), view, 1), "cliff top, tilted (= vertical)");
        Assert.AreEqual(Open(top, new int3(0, 1, 0), view, 0), Open(top, new int3(0, 1, 0), view, 1));
    }

    /// A wall at the bottom of a canyon narrower than it is deep reads covered
    /// with the tilt (the opposite wall is above 45 deg) and open without it.
    /// This is the tilt's trade, pinned so a change to it is a decision.
    [Test]
    public void NarrowCanyonFoot_TiltSeesTheOppositeWall()
    {
        Box(new int3(0, 0, 0), new int3(512, 32, 512), Materials.Stone);
        Box(new int3(160, 0, 296), new int3(352, 240, 336), Materials.Stone);     // wall A, face at z = 296 (n -z)
        Box(new int3(160, 0, 216), new int3(352, 240, 256), Materials.Stone);     // wall B, 4 m across, 24 m tall
        int3 hit = new int3(256, 48, 296), n = new int3(0, 0, -1);
        float3 view = math.normalize(new float3(1f, 0f, 0.2f));
        Assert.IsTrue(Open(hit, n, view, 0), "vertical: the canyon is open above");
        Assert.IsFalse(Open(hit, n, view, 1), "tilted: the opposite wall rises above 45 deg");
    }
    /// Mode 2 tilts side faces only: a down face keeps the vertical probe (blocked by
    /// its own voxel); the undercut wall still opens.
    [Test]
    public void Mode2_SideFacesOnly()
    {
        float3 view = math.normalize(new float3(0.6f, 0.5f, -0.8f));
        Assert.AreEqual(new float3(0f, 1f, 0f), ShadowRayReference.SkyProbeDir(new int3(0, -1, 0), view, 2), "down face: vertical");
        Assert.AreEqual(ShadowRayReference.SkyProbeDir(new int3(1, 0, 0), view, 1),
                        ShadowRayReference.SkyProbeDir(new int3(1, 0, 0), view, 2), "side face: as mode 1");

        Box(new int3(160, 0, 296), new int3(320, 320, 336), Materials.Stone);
        Box(new int3(160, 248, 256), new int3(320, 272, 296), Materials.Stone);
        Assert.IsTrue(Open(new int3(240, 48, 296), new int3(0, 0, -1), view, 2), "undercut wall: open");
        Assert.IsFalse(Open(new int3(240, 248, 276), new int3(0, -1, 0), view, 2), "ledge underside: vertical, blocked");
    }

    /// A straight tunnel 12 m into a hill, seen from outside: with mode 1 its ceiling reads
    /// open all the way in (the horizontal ray runs back out of the mouth toward the viewer)
    /// while its walls and floor read covered. Pinned as the down-face rule's known trade
    /// (task 138's look check), not as a goal.
    [Test]
    public void StraightTunnel_Mode1CeilingOpenWallsCovered()
    {
        Box(new int3(0, 0, 0), new int3(512, 32, 512), Materials.Stone);
        Box(new int3(96, 0, 256), new int3(416, 320, 512), Materials.Stone);      // the hill, face at z = 256
        Box(new int3(232, 32, 256), new int3(280, 80, 376), 0);                   // tunnel 4.8 x 4.8 m, 12 m deep
        float3 view = math.normalize(new float3(0f, 0.15f, 1f));                 // from outside, looking in
        int3 ceiling = new int3(256, 80, 360), wall = new int3(231, 48, 360), floorV = new int3(256, 31, 360);
        Assert.IsTrue(Open(ceiling, new int3(0, -1, 0), view, 1), "mode 1: deep ceiling opens toward the mouth");
        Assert.IsFalse(Open(ceiling, new int3(0, -1, 0), view, 2), "mode 2: deep ceiling covered");
        Assert.IsFalse(Open(wall, new int3(1, 0, 0), view, 1), "wall: 45 deg up meets the ceiling");
        Assert.IsFalse(Open(floorV, new int3(0, 1, 0), view, 1), "floor: straight up meets the ceiling");
        float3 viewOut = math.normalize(new float3(0f, 0.15f, -1f));             // from inside, looking out
        Assert.IsFalse(Open(ceiling, new int3(0, -1, 0), viewOut, 1), "mode 1, looking out: the ray runs deeper in");
    }
}
