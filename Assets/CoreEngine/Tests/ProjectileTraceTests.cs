// Assets/CoreEngine/Tests/ProjectileTraceTests.cs
//
// Correctness proof for §13 Phase 6 file 4 (§8.4 Projectiles), and for the
// shared VoxelRayWalker underneath it.
//
// §8.4's whole tunneling argument is one sentence: "A 2.5m/frame arrow can't
// tunnel a 0.1m wall; the DDA visits every cell." Both halves are tested here
// -- the arrow, and the claim about the DDA that makes it true. The second is
// the more useful of the two: a hit test can pass by luck of alignment, but a
// walker that provably visits every cell in an unbroken chain cannot skip a
// wall at any speed.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

public class ProjectileTraceTests
{
    private sealed class FakeWorld : IWorldQuery
    {
        public readonly Dictionary<int3, byte> Cells = new Dictionary<int3, byte>();
        public byte GetVoxel(int3 v) => Cells.TryGetValue(v, out byte m) ? m : Materials.Air;

        public void Box(int3 lo, int3 hi, byte m)
        {
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
                Cells[new int3(x, y, z)] = m;
        }
    }

    private sealed class AllResident : IVoxelResidency { public bool IsResident(int3 c) => true; }

    private sealed class ResidentExcept : IVoxelResidency
    {
        public readonly HashSet<int3> Missing = new HashSet<int3>();
        public bool IsResident(int3 c) => !Missing.Contains(c);
    }

    // =====================================================================
    // VoxelRayWalker -- "the DDA visits every cell"
    // =====================================================================

    [Test]
    public void TheWalker_VisitsAnUnbrokenChainOfCells_OneAxisStepAtATime()
    {
        // THIS is what makes tunneling impossible at any speed. If consecutive
        // visited cells were ever more than one axis-step apart, something
        // between them was skipped, and a wall could sit in the gap.
        var w = VoxelRayWalker.Create(new float3(0.03f, 0.07f, 0.11f),
                                      new float3(7.31f, 3.47f, -2.19f));

        int3 prev = default;
        bool first = true;
        int visited = 0;

        while (w.MoveNext())
        {
            if (!first)
            {
                int3 d = w.Voxel - prev;
                int manhattan = math.abs(d.x) + math.abs(d.y) + math.abs(d.z);
                Assert.AreEqual(1, manhattan,
                    $"cells must be adjacent: {prev} -> {w.Voxel} is a jump of {manhattan}");
            }
            prev = w.Voxel;
            first = false;
            visited++;
        }

        Assert.Greater(visited, 70, "a ~8.4 m ray should visit many dozens of 0.1 m cells");
    }

    [Test]
    public void TheWalker_CoversTheWholeSegment_WithNoGapsOrOverlaps()
    {
        var w = VoxelRayWalker.Create(new float3(1.234f, -0.5f, 2.0f),
                                      new float3(5.678f, 2.25f, -1.5f));
        float length = w.Length;

        float covered = 0f;
        float lastExit = 0f;
        while (w.MoveNext())
        {
            Assert.AreEqual(lastExit, w.TEnter, 1e-4f,
                "each cell must start exactly where the previous one ended -- a gap is a " +
                "skipped cell and an overlap is double-counted distance");
            Assert.GreaterOrEqual(w.TExit, w.TEnter, "a cell cannot be exited before it is entered");
            covered += w.TExit - w.TEnter;
            lastExit = w.TExit;
        }

        Assert.AreEqual(length, covered, 1e-3f, "the visited cells account for the whole segment");
        Assert.AreEqual(length, lastExit, 1e-3f, "and the last cell ends at the segment end");
    }

    [Test]
    public void TheWalker_StartsInTheCellContainingTheOrigin()
    {
        float3 p0 = new float3(3.14f, 1.59f, -2.65f);
        var w = VoxelRayWalker.Create(p0, p0 + new float3(1f, 0f, 0f));
        Assert.IsTrue(w.MoveNext());
        Assert.AreEqual(CoordMath.WorldToVoxel(p0), w.Voxel);
        Assert.AreEqual(0f, w.TEnter, 1e-6f);
    }

    [Test]
    public void TheWalker_HandlesAxisAlignedRays_WithoutDividingByZero()
    {
        // A ray exactly along +X has zero Y and Z components; those axes must
        // never produce a crossing rather than an infinity or a NaN.
        var w = VoxelRayWalker.Create(new float3(0f, 0.05f, 0.05f), new float3(2f, 0.05f, 0.05f));
        int visited = 0;
        int3 prev = default;
        bool first = true;
        while (w.MoveNext())
        {
            Assert.IsFalse(float.IsNaN(w.TEnter) || float.IsNaN(w.TExit), "no NaN distances");
            if (!first) Assert.AreEqual(1, math.abs((w.Voxel - prev).x), "steps along X only");
            prev = w.Voxel; first = false;
            visited++;
        }
        Assert.AreEqual(20, visited, 1, "2.0 m along X is ~20 cells");
    }

    [Test]
    public void TheWalker_OnAZeroLengthSegment_YieldsNothing()
    {
        var w = VoxelRayWalker.Create(new float3(1f, 1f, 1f), new float3(1f, 1f, 1f));
        Assert.IsFalse(w.MoveNext(), "a zero-length segment has no cells to visit");
    }

    [Test]
    public void TheWalker_Terminates_OnAVeryLongSegment()
    {
        // The guard must bound the walk without truncating a legitimate ray.
        var w = VoxelRayWalker.Create(float3.zero, new float3(200f, 0f, 0f));
        int visited = 0;
        while (w.MoveNext()) visited++;
        Assert.AreEqual(2000, visited, 5, "200 m along X is ~2000 cells, and it terminates");
    }

    // =====================================================================
    // §8.4: "A 2.5m/frame arrow can't tunnel a 0.1m wall"
    // =====================================================================

    [Test]
    public void A25mPerFrameArrow_CannotTunnelA01mWall_AtAnyStartingPhase()
    {
        // 0.1 m = ONE voxel, the thinnest wall the world can express, against a
        // frame step 25x its thickness. Swept across starting phases because a
        // single alignment proves nothing about the others.
        var failures = new List<string>();

        for (int trial = 0; trial < 25; trial++)
        {
            var w = new FakeWorld();
            w.Box(new int3(100, -50, -50), new int3(100, 50, 50), Materials.Stone);  // x 10.0..10.1
            var pt = new ProjectileTrace(w, new AllResident());

            float startX = 8.0f + trial * 0.037f;
            float3 from = new float3(startX, 0.05f, 0.05f);
            float3 to = from + new float3(ProjectileTrace.ReferenceArrowSpeedMpf, 0f, 0f);

            ProjectileHit h = pt.Trace(from, to);
            if (!h.Hit) { failures.Add($"trial {trial}: passed through from x={startX:F3}"); continue; }
            if (h.Voxel.x != 100)
                failures.Add($"trial {trial}: stopped at voxel x={h.Voxel.x}, expected 100");
            if (h.PointM.x > 10.0f + 1e-3f)
                failures.Add($"trial {trial}: rest point {h.PointM.x:F4} is inside/past the wall");
        }

        CollectionAssert.IsEmpty(failures, string.Join(" | ", failures));
    }

    [Test]
    public void AnEndpointOnlyCheck_MissesTheSameWall_SoTheTestIsNotVacuous()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(100, 50, 50), Materials.Stone);

        float3 from = new float3(8.0f, 0.05f, 0.05f);
        float3 to = from + new float3(2.5f, 0f, 0f);          // lands at 10.5, past the wall

        Assert.AreEqual(Materials.Air, w.GetVoxel(CoordMath.WorldToVoxel(from)), "start is clear");
        Assert.AreEqual(Materials.Air, w.GetVoxel(CoordMath.WorldToVoxel(to)),
            "and so is the END POINT -- sampling only the endpoints sees nothing at all, " +
            "which is exactly the tunneling §8.4's cell-by-cell walk rules out");

        Assert.IsTrue(new ProjectileTrace(w, new AllResident()).Trace(from, to).Hit,
            "but the swept trace does see it");
    }

    [Test]
    public void EvenAVeryFastProjectile_StillStopsAtTheFirstWall()
    {
        // 200 m in one segment, wall at 10 m. Speed is irrelevant to a walk
        // that visits every cell -- that is the point of §8.4's design.
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(100, 50, 50), Materials.Stone);
        var pt = new ProjectileTrace(w, new AllResident());

        ProjectileHit h = pt.Trace(new float3(0f, 0.05f, 0.05f), new float3(200f, 0.05f, 0.05f));

        Assert.IsTrue(h.Hit);
        Assert.AreEqual(10.0f, h.DistanceM, 0.02f, "it stops at the FIRST wall, not the last");
        Assert.AreEqual(100, h.Voxel.x);
    }

    [Test]
    public void TheHitReports_Distance_Voxel_MaterialAndRestPoint()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(105, 50, 50), Materials.Sandstone);
        var pt = new ProjectileTrace(w, new AllResident());

        ProjectileHit h = pt.Trace(new float3(5f, 0.05f, 0.05f), new float3(15f, 0.05f, 0.05f));

        Assert.IsTrue(h.Hit);
        Assert.AreEqual(5.0f, h.DistanceM, 0.02f, "5 m from the start to the 10.0 m face");
        Assert.AreEqual(new int3(100, 0, 0), h.Voxel);
        Assert.AreEqual(Materials.Sandstone, h.Material, "and says what it hit");
        Assert.Less(h.PointM.x, 10.0f, "the rest point is OUTSIDE the wall, not on its face");
        Assert.Greater(h.PointM.x, 9.9f, "but flush against it, not short");
    }

    [Test]
    public void AMissReportsTheFullLength_SoACallerCanAdvanceEitherWay()
    {
        var w = new FakeWorld();
        var pt = new ProjectileTrace(w, new AllResident());
        float3 from = new float3(0f, 0.05f, 0.05f), to = new float3(3f, 0.05f, 0.05f);

        ProjectileHit h = pt.Trace(from, to);
        Assert.IsFalse(h.Hit);
        Assert.AreEqual(3f, h.DistanceM, 1e-3f);
        Assert.AreEqual(to.x, h.PointM.x, 1e-3f, "and rests at the requested destination");
        Assert.AreEqual(Materials.Air, h.Material);
    }

    [Test]
    public void ADiagonalShot_StillFindsAWall()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(101, 50, 50), Materials.Stone);
        var pt = new ProjectileTrace(w, new AllResident());

        ProjectileHit h = pt.Trace(new float3(5f, 0.05f, 0.05f), new float3(15f, 4f, 3f));
        Assert.IsTrue(h.Hit, "a diagonal ray must not slip between cells");
        Assert.AreEqual(100, h.Voxel.x);
    }

    [Test]
    public void TraceMotion_IsTheSameAsTracingPositionPlusVelocityTimesDt()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(101, 50, 50), Materials.Stone);
        var pt = new ProjectileTrace(w, new AllResident());

        float3 p = new float3(5f, 0.05f, 0.05f);
        float3 v = new float3(150f, 0f, 0f);
        ProjectileHit a = pt.TraceMotion(p, v, 1f / 60f);
        ProjectileHit b = pt.Trace(p, p + v * (1f / 60f));

        Assert.AreEqual(b.Hit, a.Hit);
        Assert.AreEqual(b.DistanceM, a.DistanceM, 1e-4f);
    }

    // =====================================================================
    // Fluid and residency -- same rules as §8.2, from the shared definitions
    // =====================================================================

    [Test]
    public void FluidDoesNotStopAProjectile_ButIsReported()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(102, 50, 50), Materials.Water);   // 0.3 m
        var pt = new ProjectileTrace(w, new AllResident());

        ProjectileHit h = pt.Trace(new float3(9f, 0.05f, 0.05f), new float3(12f, 0.05f, 0.05f));

        Assert.IsFalse(h.Hit, "water is not solid; an arrow passes through it");
        Assert.AreEqual(0.3f, h.FluidTraversedDistanceM, 0.02f,
            "but the crossing is measured, so a splash can be raised");
        Assert.AreEqual(Materials.Water, h.PrimaryFluidMaterial);
    }

    [Test]
    public void FluidCrossedBeforeAWall_IsStillReportedWithTheHit()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(104, 50, 50), Materials.Water);
        w.Box(new int3(120, -50, -50), new int3(121, 50, 50), Materials.Stone);
        var pt = new ProjectileTrace(w, new AllResident());

        ProjectileHit h = pt.Trace(new float3(9f, 0.05f, 0.05f), new float3(14f, 0.05f, 0.05f));

        Assert.IsTrue(h.Hit, "the wall behind the water still stops it");
        Assert.AreEqual(120, h.Voxel.x);
        Assert.Greater(h.FluidTraversedDistanceM, 0.4f, "and the water crossed on the way counts");
    }

    [Test]
    public void SandStopsAProjectile_BecauseItIsAFallingSolidNotAFluid()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(105, 50, 50), Materials.Sand);
        var pt = new ProjectileTrace(w, new AllResident());

        ProjectileHit h = pt.Trace(new float3(9f, 0.05f, 0.05f), new float3(12f, 0.05f, 0.05f));
        Assert.IsTrue(h.Hit);
        Assert.AreEqual(Materials.Sand, h.Material);
        Assert.AreEqual(0f, h.FluidTraversedDistanceM, 1e-5f, "and is not counted as fluid");
    }

    [Test]
    public void AnUnloadedChunk_StopsTheProjectile_AndSaysThatIsWhy()
    {
        // §9.4 once more: a projectile must not fly off into world that has not
        // streamed in and report a clean miss.
        var w = new FakeWorld();
        var r = new ResidentExcept();
        r.Missing.Add(new int3(1, 0, 0));                 // voxels x >= 128 => 12.8 m

        ProjectileHit h = new ProjectileTrace(w, r).Trace(new float3(11f, 0.05f, 0.05f),
                                                          new float3(16f, 0.05f, 0.05f));

        Assert.IsTrue(h.Hit, "an unloaded chunk stops the trace");
        Assert.IsTrue(h.WasNonResident,
            "and reports that it was a residency edge, so a streaming stall cannot be " +
            "mistaken for an impact");
    }

    [Test]
    public void ZeroLengthTrace_IsAMissRatherThanAnError()
    {
        var w = new FakeWorld();
        var pt = new ProjectileTrace(w, new AllResident());
        float3 p = new float3(1f, 1f, 1f);

        ProjectileHit h = pt.Trace(p, p);
        Assert.IsFalse(h.Hit);
        Assert.AreEqual(0f, h.DistanceM, 1e-6f);
    }

    // =====================================================================
    // The shared walker must give SweptCCD and ProjectileTrace the SAME answer
    // =====================================================================

    [Test]
    public void AProjectileAndASweepAgree_AboutWhereAWallIs()
    {
        // The reason VoxelRayWalker was factored out rather than copied: if
        // these two ever disagreed, a projectile would pass through a wall the
        // player's sweep stops at, and it would look like a projectile bug
        // rather than a duplicated-DDA bug.
        var w = new FakeWorld();
        w.Box(new int3(100, -50, -50), new int3(101, 50, 50), Materials.Stone);

        float3 from = new float3(8.0f, 0.05f, 0.05f);
        float3 to = new float3(11.0f, 0.05f, 0.05f);

        ProjectileHit p = new ProjectileTrace(w, new AllResident()).Trace(from, to);
        CCDResult c = new SweptCCD(w, new AllResident()).Sweep(from, to, 0.001f, 0.002f);

        Assert.IsTrue(p.Hit && c.HitSolid, "both must see the wall");
        Assert.AreEqual(c.SolidHitDistanceM, p.DistanceM, 0.02f,
            "and agree about how far away it is");
    }
}
