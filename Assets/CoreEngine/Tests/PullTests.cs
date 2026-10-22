// Assets/CoreEngine/Tests/PullTests.cs
//
// Gameplay BLD2 (task 93b1, owner Q1 2026-10-15, GAMEPLAY_PHASE_PLAN.md BLD2's
// acceptance): the face pull. A block pulled N along any of its 6 faces is a
// line of N+1; a line pulled on a side face is a plane of exactly line x (N+1)
// cubes (along itself it just gets longer); a plane pulled on its face is a
// box; cost = cells placed; a short slot stops at the first cube it cannot
// pay for, layer by layer; pulling back in digs and credits; the aim-to-N
// math, the entry face and the shape's intact check. BLD1's own tests
// (StrokeTests) are unchanged.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class PullTests
{
    private static readonly byte Limestone = Materials.FromTg2(TerrainGen.GenMat.Limestone);
    private static readonly byte Tg2Grass = Materials.FromTg2(TerrainGen.GenMat.Grass);
    private static readonly byte Marble = Materials.FromTg2(TerrainGen.GenMat.Marble);
    private const int Ground = 20;                 // y < 20 limestone, y == 20 grass, above air
    private const int E = 4;                       // 0.4 m cubes, 64 cells each

    private static readonly int3[] Normals =
        { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };

    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditService _edits;
    private readonly List<int3> _face = new List<int3>();
    private readonly List<int3> _cubes = new List<int3>();

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(4000, rangeAware: true);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(8), new int3(4, 4, 4));
        _store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air });
        for (int z = 0; z < 64; z++)
        for (int x = 0; x < 64; x++)
        {
            for (int y = 0; y < Ground; y++) _store.SetVoxel(new int3(x, y, z), Limestone);
            _store.SetVoxel(new int3(x, Ground, z), Tg2Grass);
        }
        _edits = new EditService();
        _edits.AttachWorld(_store, _store, _store, null);
    }

    [TearDown]
    public void TearDown() => _pool?.Dispose();

    private int CountIn(int3 lo, int edge, byte m)
    {
        int n = 0;
        for (int z = 0; z < edge; z++) for (int y = 0; y < edge; y++) for (int x = 0; x < edge; x++)
            if (_store.GetVoxel(lo + new int3(x, y, z)) == m) n++;
        return n;
    }

    private static Inventory Stack(byte item, int n)
    {
        var inv = new Inventory();
        inv.Add(item, n);
        return inv;
    }

    /// FaceLayer + PullCubes + PlacePull, the order Playground.CommitPull uses.
    private SurvivalBuild.StrokeResult Pull(SurvivalBuild.Shape shape, Inventory inv, int3 n, int steps)
    {
        SurvivalBuild.FaceLayer(shape, n, _face);
        SurvivalBuild.PullCubes(_face, shape.Edge, n, steps, _cubes, SurvivalBuild.MaxPullCubes(shape.Edge));
        return SurvivalBuild.PlacePull(_edits, _store, inv, 0, shape, _cubes);
    }

    private SurvivalBuild.Shape Block(int3 lo, Inventory inv)
    {
        var p = SurvivalBuild.Place(_edits, _store, inv, 0, lo, E);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.Placed, p.Outcome);
        var s = new SurvivalBuild.Shape();
        s.Set(new List<int3> { p.Lo }, 1, E);
        return s;
    }

    private static void AssertSameCubes(IEnumerable<int3> want, SurvivalBuild.Shape got, string what)
    {
        var w = new HashSet<int3>(want);
        Assert.AreEqual(w.Count, got.Count, $"{what}: cube count");
        foreach (var c in got.Cubes) Assert.IsTrue(w.Contains(c), $"{what}: unexpected cube {c}");
    }

    // =====================================================================

    [Test]
    public void Block_PulledAlongEachFace_IsALineOfNPlusOne()
    {
        int3 lo = new int3(28, 40, 28);
        foreach (int3 n in Normals)
        {
            TearDown();
            SetUp();
            var inv = Stack(Limestone, 5000);
            var shape = Block(lo, inv);
            int before = inv.CountAt(0);
            long written = _edits.VoxelsWritten;
            var r = Pull(shape, inv, n, 4);
            Assert.AreEqual(SurvivalBuild.PlaceOutcome.Placed, r.Outcome, $"n {n}");
            Assert.AreEqual(4 * 64, r.Placed, $"n {n}: cost = cells placed");
            Assert.AreEqual(before - r.Placed, inv.CountAt(0), $"n {n}: slot -= cells placed");
            Assert.AreEqual(written + r.Placed, _edits.VoxelsWritten, $"n {n}: one write per cell, one call");
            var want = new List<int3>();
            for (int k = 0; k <= 4; k++) want.Add(lo + n * (E * k));
            AssertSameCubes(want, shape, $"n {n}");
            foreach (var c in want) Assert.AreEqual(64, CountIn(c, E, Limestone), $"n {n}: cube {c} full");
            Assert.AreEqual(0, CountIn(lo + n * (E * 5), E, Limestone), $"n {n}: nothing past N");
        }
    }

    [Test]
    public void Line_PulledOnASideFace_IsAPlaneOfLineTimesN_AlongItself_Longer()
    {
        var inv = Stack(Limestone, 10000);
        var line = new List<int3>();
        SurvivalBuild.LineCubes(new int3(8, 32, 28), new int3(36, 32, 28), E, line);   // 8 cubes along +x
        Assert.AreEqual(8, line.Count);
        var s = SurvivalBuild.PlaceStroke(_edits, _store, inv, 0, line, E);
        var shape = new SurvivalBuild.Shape();
        shape.Set(line, s.CubesPaid, E);

        foreach (int3 n in new[] { new int3(0, 1, 0), new int3(0, 0, 1), new int3(0, 0, -1) })
        {
            var sh = new SurvivalBuild.Shape();
            sh.Set(line, line.Count, E);
            SurvivalBuild.FaceLayer(sh, n, _face);
            Assert.AreEqual(8, _face.Count, $"n {n}: a side face is the whole line");
            SurvivalBuild.PullCubes(_face, E, n, 3, _cubes, 4096);
            Assert.AreEqual(24, _cubes.Count, $"n {n}: line x N");
            for (int i = 0; i < 8; i++) Assert.AreEqual(line[i] + n * E, _cubes[i], $"n {n}: layer 1 first, in line order");
        }

        int placedBefore = inv.CountAt(0);
        var r = Pull(shape, inv, new int3(0, 1, 0), 3);
        Assert.AreEqual(8 * 3 * 64, r.Placed);
        Assert.AreEqual(placedBefore - r.Placed, inv.CountAt(0));
        var want = new List<int3>();
        for (int y = 0; y < 4; y++) for (int i = 0; i < 8; i++) want.Add(new int3(8 + i * E, 32 + y * E, 28));
        AssertSameCubes(want, shape, "wall 8 x 4");
        foreach (var c in want) Assert.AreEqual(64, CountIn(c, E, Limestone), $"wall cube {c}");

        // Along itself (+x end face): only the end column (4 cubes) is pulled -> the wall gets longer.
        SurvivalBuild.FaceLayer(shape, new int3(1, 0, 0), _face);
        Assert.AreEqual(4, _face.Count, "the wall's +x end face is one column");
        var e2 = Pull(shape, inv, new int3(1, 0, 0), 2);
        Assert.AreEqual(4 * 2 * 64, e2.Placed);
        Assert.AreEqual(40, shape.Count, "10 x 4");
    }

    [Test]
    public void Plane_PulledOnItsFace_IsABox()
    {
        var inv = Stack(Marble, 10000);
        var shape = Block(new int3(12, 28, 12), inv);
        Pull(shape, inv, new int3(1, 0, 0), 5);                   // line of 6 along x
        Pull(shape, inv, new int3(0, 0, 1), 3);                   // plane 6 x 4 (horizontal, in the air)
        Assert.AreEqual(24, shape.Count);
        int before = inv.CountAt(0);
        var r = Pull(shape, inv, new int3(0, 1, 0), 2);           // thicken upward: box 6 x 3 x 4
        Assert.AreEqual(24 * 2 * 64, r.Placed, "cost = cells placed");
        Assert.AreEqual(before - r.Placed, inv.CountAt(0));
        var want = new List<int3>();
        for (int x = 0; x < 6; x++) for (int y = 0; y < 3; y++) for (int z = 0; z < 4; z++)
            want.Add(new int3(12 + x * E, 28 + y * E, 12 + z * E));
        AssertSameCubes(want, shape, "box 6 x 3 x 4");
        foreach (var c in want) Assert.AreEqual(64, CountIn(c, E, Marble), $"box cube {c}");

        // A box's face is its outer layer only.
        SurvivalBuild.FaceLayer(shape, new int3(0, 1, 0), _face);
        Assert.AreEqual(24, _face.Count, "top layer");
        foreach (var c in _face) Assert.AreEqual(28 + 2 * E, c.y);
    }

    [Test]
    public void Pull_StopsWhenTheSlotRunsOut_LayerOrder_ShapeKeepsOnlyThePaid()
    {
        var inv = Stack(Limestone, 64 + 64 * 10 + 5);
        var shape = Block(new int3(4, 36, 28), inv);
        Pull(shape, inv, new int3(1, 0, 0), 7);                   // line of 8 -> 64 * 7 used
        Assert.AreEqual(64 * 3 + 5, inv.CountAt(0));
        var r = Pull(shape, inv, new int3(0, 1, 0), 3);           // wants 24 cubes, pays 3
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.Placed, r.Outcome);
        Assert.IsTrue(r.RanOut);
        Assert.AreEqual(3, r.CubesPaid);
        Assert.AreEqual(192, r.Placed);
        Assert.AreEqual(24 * 64, r.Needed);
        Assert.AreEqual(5, inv.CountAt(0), "the remainder stays in the slot");
        Assert.AreEqual(11, shape.Count, "only the paid cubes join the shape");
        for (int i = 0; i < 8; i++)
            Assert.AreEqual(i < 3 ? 64 : 0, CountIn(new int3(4 + i * E, 40, 28), E, Limestone), $"layer 1 cube {i}: path order, then stop");

        var t = Pull(shape, inv, new int3(0, 1, 0), 1);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.TooFew, t.Outcome, "5 left, a cube needs 64");
        Assert.AreEqual(5, inv.CountAt(0));
        Assert.AreEqual(11, shape.Count);
    }

    [Test]
    public void Pull_ThroughTheGround_FillsOnlyAir_AndCostsOnlyThat()
    {
        var inv = Stack(Marble, 5000);
        var shape = Block(new int3(20, Ground + 9, 20), inv);    // y 29..32 (snapped to 28..31)
        var r = Pull(shape, inv, new int3(0, -1, 0), 4);          // down into the grass and the rock
        int marble = 0;
        foreach (var c in shape.Cubes) marble += CountIn(c, E, Marble);
        Assert.AreEqual(64 + r.Placed, marble);
        Assert.Less(r.Placed, 4 * 64, "solid cells left alone, not paid for");
        Assert.AreEqual(5000 - 64 - r.Placed, inv.CountAt(0));
        Assert.AreEqual(Limestone, _store.GetVoxel(new int3(20, 13, 20)), "rock under it unchanged");
    }

    [Test]
    public void PullIn_DigsTheFaceLayerAndBehind_AndCredits()
    {
        var inv = Stack(Marble, 10000);
        var shape = Block(new int3(8, 32, 8), inv);
        Pull(shape, inv, new int3(1, 0, 0), 3);                   // 4 along x
        Pull(shape, inv, new int3(0, 0, 1), 2);                   // 4 x 3 plane
        Pull(shape, inv, new int3(0, 1, 0), 2);                   // 4 x 3 x 3 box
        Assert.AreEqual(36, shape.Count);
        int marble = inv.Count(Marble);

        SurvivalBuild.FaceLayer(shape, new int3(0, 0, 1), _face);
        int used = SurvivalBuild.PullCubes(_face, E, new int3(0, 0, 1), -2, _cubes, 4096);
        Assert.AreEqual(-2, used);
        Assert.AreEqual(24, _cubes.Count);
        for (int i = 0; i < 12; i++) Assert.AreEqual(8 + 2 * E, _cubes[i].z, "the face layer itself first");
        var d = SurvivalBuild.DigPull(_edits, inv, shape, _cubes, new int[256]);
        Assert.AreEqual(24 * 64, d.Removed);
        Assert.AreEqual(d.Removed, d.Credited);
        Assert.AreEqual(marble + d.Removed, inv.Count(Marble), "dug N -> inventory +N");
        Assert.AreEqual(12, shape.Count, "the dug cubes leave the shape");
        foreach (var c in shape.Cubes) Assert.AreEqual(8, c.z);
        foreach (var c in _cubes) Assert.AreEqual(64, CountIn(c, E, Materials.Air));
    }

    [Test]
    public void PullCubes_CapsAtWholeLayers()
    {
        var face = new List<int3>();
        for (int i = 0; i < 100; i++) face.Add(new int3(i * E, 0, 0));
        int used = SurvivalBuild.PullCubes(face, E, new int3(0, 1, 0), 50, _cubes, 1000);
        Assert.AreEqual(10, used, "1000 / 100 = 10 whole layers");
        Assert.AreEqual(1000, _cubes.Count);
        Assert.AreEqual(-10, SurvivalBuild.PullCubes(face, E, new int3(0, 1, 0), -50, _cubes, 1000));
        Assert.AreEqual(0, SurvivalBuild.PullCubes(face, E, new int3(0, 1, 0), 0, _cubes, 1000));
        Assert.AreEqual(0, _cubes.Count);
        Assert.AreEqual(SurvivalBuild.MaxPullSteps, SurvivalBuild.PullCubes(face.GetRange(0, 1), E, new int3(0, 1, 0), 1000, _cubes, 1 << 20));
        Assert.AreEqual(SurvivalBuild.MaxPullSteps, _cubes.Count, "steps cap");
        Assert.AreEqual(256, SurvivalBuild.MaxPullCubes(8), "0.8 m: BLD1's longest stroke in cells");
        Assert.AreEqual(2048, SurvivalBuild.MaxPullCubes(4));
        Assert.AreEqual(2048, SurvivalBuild.MaxPullCubes(1));
    }

    [Test]
    public void PullSteps_FollowsTheAimAlongTheNormal()
    {
        float3 face = new float3(1f, 2f, 3f);                    // top face centre of a 0.4 m cube
        int3 up = new int3(0, 1, 0);
        // Eye 4 m away at face height, aiming at a point 1.1 m above the face: 2.75 cubes -> 3.
        float3 eye = new float3(1f, 2f, -1f);
        Assert.AreEqual(3, SurvivalBuild.PullSteps(eye, face + new float3(0, 1.1f, 0) - eye, face, up, 0.4f, 0));
        Assert.AreEqual(2, SurvivalBuild.PullSteps(eye, face + new float3(0, 0.9f, 0) - eye, face, up, 0.4f, 0));
        Assert.AreEqual(0, SurvivalBuild.PullSteps(eye, face - eye, face, up, 0.4f, 7), "aimed at the face: 0");
        Assert.AreEqual(-2, SurvivalBuild.PullSteps(eye, face - new float3(0, 0.8f, 0) - eye, face, up, 0.4f, 0), "below: inward");
        Assert.AreEqual(5, SurvivalBuild.PullSteps(eye, new float3(0, 1, 0), face + new float3(0, 0, 4), up, 0.4f, 5), "parallel: keeps prev");
        Assert.AreEqual(5, SurvivalBuild.PullSteps(eye, new float3(0, 0, 1), eye - new float3(0, 0, 4), up, 0.4f, 5),
                        "line behind the eye: keeps prev");
        Assert.AreEqual(SurvivalBuild.MaxPullSteps, SurvivalBuild.PullSteps(eye, face + new float3(0, 30f, 0) - eye, face, up, 0.4f, 0), "30 m up: clamped");
    }

    [Test]
    public void EntryFace_IsTheFaceTheRayCrosses()
    {
        int3 v = new int3(10, 5, 7);
        Assert.AreEqual(new int3(0, 1, 0), SurvivalBuild.EntryFace(new float3(10.5f, 9f, 7.5f), new float3(0.1f, -1f, 0.05f), v), "from above");
        Assert.AreEqual(new int3(-1, 0, 0), SurvivalBuild.EntryFace(new float3(6f, 5.6f, 7.4f), new float3(1f, 0.02f, 0f), v), "from -x");
        Assert.AreEqual(new int3(0, 0, 1), SurvivalBuild.EntryFace(new float3(10.3f, 5.5f, 12f), new float3(0.05f, 0f, -1f), v), "from +z");
        Assert.AreEqual(new int3(0, -1, 0), SurvivalBuild.EntryFace(new float3(10.5f, 1f, 7.5f), new float3(0f, 1f, 0f), v), "from below");
        // Shallow: a ray skimming down onto the top face from far away still enters through the top.
        Assert.AreEqual(new int3(0, 1, 0), SurvivalBuild.EntryFace(new float3(1f, 7.33f, 7.5f), new float3(10f, -1.4f, 0f), v));
    }

    [Test]
    public void Shape_Intact_Overlaps_AndCap()
    {
        var inv = Stack(Limestone, 5000);
        var shape = Block(new int3(28, 40, 28), inv);
        Pull(shape, inv, new int3(1, 0, 0), 2);
        Assert.IsTrue(shape.Intact(_store));
        Assert.IsTrue(shape.Overlaps(new int3(30, 41, 30), new int3(30, 41, 30)));
        Assert.IsFalse(shape.Overlaps(new int3(0, 0, 0), new int3(5, 5, 5)));
        _edits.SetCubes(new List<int3> { new int3(32, 40, 28) }, 1, E, Materials.Air, false);   // dig the middle cube
        Assert.IsFalse(shape.Intact(_store), "a cube's centre dug -> not intact");

        var big = new SurvivalBuild.Shape();
        var many = new List<int3>();
        for (int i = 0; i <= SurvivalBuild.Shape.MaxCubes; i++) many.Add(new int3(i * E, 0, 0));
        big.Set(many, many.Count, E);
        Assert.IsTrue(big.IsEmpty, "over MaxCubes -> dropped");
    }
}
