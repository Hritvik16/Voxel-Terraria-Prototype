// Assets/CoreEngine/Tests/StrokeTests.cs
//
// Gameplay BLD1 (task 93a1, GAMEPLAY_PHASE_PLAN.md §3 Building, BLD1's
// acceptance): the drag line. LineCubes is exact on the 6 axis directions and
// on diagonals (checked against an integer reference, round(i * d / n) with
// ties away from the start), floors negative coordinates, is 26-connected and
// never repeats a cube; a place stroke costs exactly the cells it places,
// stops at the first cube the slot cannot pay for, and is one EditService call;
// a dig stroke credits what it removed. No undo (none exists yet).

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class StrokeTests
{
    private static readonly byte Limestone = Materials.FromTg2(TerrainGen.GenMat.Limestone);
    private static readonly byte Tg2Grass = Materials.FromTg2(TerrainGen.GenMat.Grass);
    private static readonly byte Loam = Materials.FromTg2(TerrainGen.GenMat.Loam);
    private const int Ground = 20;                 // y < 20 limestone, y == 20 grass, above air

    // =====================================================================
    // LineCubes
    // =====================================================================

    /// The reference: cube i = p + sign(d) * floor((2 i |d| + n) / 2n), per axis.
    private static List<int3> Reference(int3 a, int3 b, int edge)
    {
        int s = math.tzcnt(edge);
        int3 p = a >> s, q = b >> s;
        int3 d = math.abs(q - p), st = math.sign(q - p);
        int n = math.cmax(d);
        var r = new List<int3>();
        for (int i = 0; i <= n; i++)
        {
            int3 off = n == 0 ? int3.zero : (2 * i * d + n) / (2 * n);
            r.Add((p + st * off) << s);
        }
        return r;
    }

    private static void AssertLine(int3 a, int3 b, int edge)
    {
        var got = new List<int3>();
        SurvivalBuild.LineCubes(a, b, edge, got);
        var want = Reference(a, b, edge);
        Assert.AreEqual(want.Count, got.Count, $"{a} -> {b} edge {edge}: cube count");
        for (int i = 0; i < want.Count; i++) Assert.AreEqual(want[i], got[i], $"{a} -> {b} edge {edge}: cube {i}");
        Assert.AreEqual(SurvivalBuild.SnapToGrid(a, edge), got[0], "starts at the press cube");
        Assert.AreEqual(SurvivalBuild.SnapToGrid(b, edge), got[got.Count - 1], "ends at the release cube");
        var seen = new HashSet<int3>();
        for (int i = 0; i < got.Count; i++)
        {
            Assert.IsTrue(seen.Add(got[i]), "no cube twice");
            Assert.AreEqual(int3.zero, got[i] & (edge - 1), "on the brush grid");
            if (i > 0)
                Assert.AreEqual(edge, math.cmax(math.abs(got[i] - got[i - 1])), "26-connected: one brush step at a time");
        }
    }

    [Test]
    public void LineCubes_SixAxisDirections_AreExact()
    {
        int3 o = new int3(37, 21, 50);
        foreach (int edge in SurvivalBuild.CubeEdges)
        for (int axis = 0; axis < 3; axis++)
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            int3 d = int3.zero;
            d[axis] = sgn * 10 * edge;
            var got = new List<int3>();
            SurvivalBuild.LineCubes(o, o + d, edge, got);
            Assert.AreEqual(11, got.Count, $"axis {axis} sign {sgn} edge {edge}: 10 steps + the start");
            int3 step = int3.zero;
            step[axis] = sgn * edge;
            for (int i = 0; i < got.Count; i++)
                Assert.AreEqual(SurvivalBuild.SnapToGrid(o, edge) + step * i, got[i], $"axis {axis} sign {sgn} edge {edge}: cube {i}");
            AssertLine(o, o + d, edge);
        }
    }

    [Test]
    public void LineCubes_Diagonals_MatchTheIntegerReference()
    {
        int3 o = new int3(100, 40, 100);
        int3[] ds =
        {
            new int3(20, 20, 20), new int3(-20, 20, -20), new int3(20, 0, 20), new int3(0, -20, 20),   // pure diagonals
            new int3(28, 12, 0), new int3(-24, 8, -36), new int3(7, 3, -11), new int3(33, -5, 17),    // general slopes
            new int3(1, 0, 0), new int3(0, 0, 0),                                                       // one cube, a click
        };
        foreach (int edge in SurvivalBuild.CubeEdges)
        foreach (int3 d in ds)
            AssertLine(o, o + d * edge, edge);
        // Pure diagonals step every axis every cube.
        var g = new List<int3>();
        SurvivalBuild.LineCubes(o, o + new int3(-20, 20, -20) * 2, 2, g);
        Assert.AreEqual(21, g.Count);
        for (int i = 1; i < g.Count; i++) Assert.AreEqual(new int3(-2, 2, -2), g[i] - g[i - 1]);
    }

    [Test]
    public void LineCubes_NegativeCoordinates_Floor_AndLongStrokesAreCut()
    {
        AssertLine(new int3(-5, -1, -9), new int3(13, -30, 2), 4);
        var g = new List<int3>();
        SurvivalBuild.LineCubes(new int3(-1, -1, -1), new int3(-1, -1, -1), 4, g);
        Assert.AreEqual(1, g.Count);
        Assert.AreEqual(new int3(-4, -4, -4), g[0], "arithmetic shift floors, like SnapToGrid");

        SurvivalBuild.LineCubes(int3.zero, new int3(1000, 0, 0), 1, g);
        Assert.AreEqual(SurvivalBuild.MaxStrokeCubes, g.Count, "a longer drag is cut at the far end");
        Assert.AreEqual(int3.zero, g[0]);
        Assert.Throws<ArgumentException>(() => SurvivalBuild.LineCubes(int3.zero, int3.zero, 3, g));
    }

    // =====================================================================
    // Strokes on a synthetic ChunkStore
    // =====================================================================

    private BrickDataPool _pool;
    private ChunkStore _store;
    private EditService _edits;

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

    [Test]
    public void PlaceStroke_InTheAir_CostsExactlyTheCellsPlaced()
    {
        var inv = new Inventory();
        inv.Add(Limestone, 5000);
        var cubes = new List<int3>();
        SurvivalBuild.LineCubes(new int3(2, 33, 9), new int3(41, 33, 30), 4, cubes);   // a diagonal in the air
        int n = cubes.Count;
        var preview = SurvivalBuild.Cost(_store, cubes, 4, false, inv.CountAt(0));
        Assert.AreEqual(n * 64, preview.Cells);
        Assert.AreEqual(n, preview.Affordable);

        long written = _edits.VoxelsWritten;
        var r = SurvivalBuild.PlaceStroke(_edits, _store, inv, 0, cubes, 4);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.Placed, r.Outcome);
        Assert.AreEqual(n, r.CubesPaid);
        Assert.IsFalse(r.RanOut);
        Assert.AreEqual(n * 64, r.Placed, "cost = cells placed");
        Assert.AreEqual(5000 - r.Placed, inv.Count(Limestone), "inventory -= cells placed");
        int inWorld = 0;
        foreach (var c in cubes) inWorld += CountIn(c, 4, Limestone);
        Assert.AreEqual(r.Placed, inWorld, "ChunkStore += cells placed");
        Assert.AreEqual(written + r.Placed, _edits.VoxelsWritten, "one write per placed cell, nothing else");
    }

    [Test]
    public void PlaceStroke_ThroughTheGround_FillsOnlyAir_AndCostsOnlyThat()
    {
        var inv = new Inventory();
        inv.Add(Loam, 9000);
        var cubes = new List<int3>();
        SurvivalBuild.LineCubes(new int3(4, Ground - 6, 20), new int3(60, Ground + 10, 20), 4, cubes);
        var r = SurvivalBuild.PlaceStroke(_edits, _store, inv, 0, cubes, 4);
        int loam = 0, air = 0;
        foreach (var c in cubes) { loam += CountIn(c, 4, Loam); air += CountIn(c, 4, Materials.Air); }
        Assert.AreEqual(r.Placed, loam, "every placed cell is the slot's block");
        Assert.AreEqual(0, air, "every air cell along the stroke is filled");
        Assert.Less(r.Placed, cubes.Count * 64, "the solid cells were left alone and cost nothing");
        Assert.AreEqual(9000 - r.Placed, inv.Count(Loam));
        Assert.AreEqual(Limestone, _store.GetVoxel(new int3(4, Ground - 6, 20)), "solid ground under the stroke unchanged");
    }

    [Test]
    public void PlaceStroke_StopsWhenTheSlotRunsOut_WholeCubesOnly()
    {
        var inv = new Inventory();
        inv.Add(Limestone, 64 * 3 + 10);
        var cubes = new List<int3>();
        SurvivalBuild.LineCubes(new int3(0, 40, 4), new int3(39, 40, 4), 4, cubes);    // 10 cubes in the air
        Assert.AreEqual(10, cubes.Count);
        var preview = SurvivalBuild.Cost(_store, cubes, 4, false, inv.CountAt(0));
        Assert.AreEqual(3, preview.Affordable, "the preview shows where it runs out");

        var r = SurvivalBuild.PlaceStroke(_edits, _store, inv, 0, cubes, 4);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.Placed, r.Outcome);
        Assert.AreEqual(3, r.CubesPaid);
        Assert.IsTrue(r.RanOut);
        Assert.AreEqual(192, r.Placed);
        Assert.AreEqual(640, r.Needed);
        Assert.AreEqual(10, inv.CountAt(0), "the remainder stays in the slot");
        for (int i = 0; i < 10; i++)
            Assert.AreEqual(i < 3 ? 64 : 0, CountIn(cubes[i], 4, Limestone), $"cube {i}: placed in path order, then stop");

        var none = new Inventory();
        none.Add(Limestone, 63);
        var t = SurvivalBuild.PlaceStroke(_edits, _store, none, 0, cubes, 4);
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.TooFew, t.Outcome, "cube 4 needs 64, the slot has 63");
        Assert.AreEqual(0, t.Placed);
        Assert.AreEqual(63, none.CountAt(0));
        Assert.AreEqual(0, CountIn(cubes[3], 4, Limestone));

        var empty = new Inventory();
        Assert.AreEqual(SurvivalBuild.PlaceOutcome.EmptySlot, SurvivalBuild.PlaceStroke(_edits, _store, empty, 0, cubes, 4).Outcome);
    }

    [Test]
    public void DigStroke_RemovesTheLine_AndCreditsWhatItRemoved()
    {
        var inv = new Inventory();
        var scratch = new int[256];
        var cubes = new List<int3>();
        SurvivalBuild.LineCubes(new int3(2, Ground, 2), new int3(50, Ground - 8, 30), 2, cubes);
        int expect = 0;
        foreach (var c in cubes) expect += SurvivalBuild.DiggableCells(_store, c, 2);
        var preview = SurvivalBuild.Cost(_store, cubes, 2, true, int.MaxValue);
        Assert.AreEqual(expect, preview.Cells);

        var r = SurvivalBuild.DigStroke(_edits, inv, cubes, 2, scratch);
        Assert.AreEqual(expect, r.Removed);
        Assert.AreEqual(r.Removed, r.Credited + r.Lost);
        Assert.AreEqual(0, r.Lost);
        Assert.AreEqual(r.Credited, inv.Count(Limestone) + inv.Count(Loam), "dug N voxels -> inventory +N");
        Assert.Greater(inv.Count(Loam), 0, "grass gives its soil (P3)");
        Assert.AreEqual(0, inv.Count(Tg2Grass));
        foreach (var c in cubes) Assert.AreEqual(8, CountIn(c, 2, Materials.Air), $"cube {c} dug out");
    }

    [Test]
    public void SetCubes_FillOnly_LeavesSolidCells_AndCountsPerMaterial()
    {
        var cubes = new List<int3> { new int3(8, Ground - 2, 8), new int3(8, Ground + 2, 8) };   // all rock, all air
        int got = _edits.SetCubes(cubes, 2, 2, Loam, fillOnly: true);
        Assert.AreEqual(8, got, "the air cube only; the rock cube is left alone");
        Assert.AreEqual(8, CountIn(cubes[1], 2, Loam));
        Assert.AreEqual(8, CountIn(cubes[0], 2, Limestone));
        var counts = new int[256];
        int dug = _edits.SetCubes(cubes, 1, 2, Materials.Air, fillOnly: false, counts);
        Assert.AreEqual(8, dug, "count = 1: only the first cube");
        Assert.AreEqual(8, counts[Limestone], "every removed cell is counted by its material");
        Assert.AreEqual(8, CountIn(cubes[1], 2, Loam), "the second cube untouched");
        Assert.Throws<ArgumentException>(() => _edits.SetCubes(cubes, 1, 3, Loam, true));
        Assert.Throws<ArgumentException>(() => _edits.SetCubes(cubes, 1, 2, Loam, true, new int[10]));
    }
}
