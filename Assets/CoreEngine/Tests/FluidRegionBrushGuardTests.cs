// Assets/CoreEngine/Tests/FluidRegionBrushGuardTests.cs
//
// Pins the guard for the frozen-fluid-outside-the-arena bug.
//
// THE BUG. The Playground's RMB brush wrote a mobile material (water/sand/lava)
// wherever the crosshair pointed, with no check that the cell was inside the
// CA's region. FluidGpuSimulation.RequestWake correctly and silently drops
// out-of-region wakes -- right for the engine, wrong for that caller -- so the
// voxel was committed to ChunkStore, uploaded to the mirror, drawn, and then
// never simulated. It hung in mid-air forever. The reported screenshot showed a
// vertical column of SAND suspended in the sky, which is the unambiguous tell:
// a falling solid that does not fall is not being ticked at all.
//
// The vent path (Playground.OpenVentAtTarget) had always guarded this. The
// paint path did not. Two callers, one limitation, one of them enforcing it.
//
// These tests are pure integer geometry -- no compute device, no GPU, no
// ChunkStore. They pin the predicate the paint path now asks BEFORE writing.
// The end-to-end proof (RMB outside the arena visibly refuses) is an
// interactive Playground run; see the report for the manual repro.
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Simulation;

public class FluidRegionBrushGuardTests
{
    // The exact arena from the reported bug: Playground builds a 64^3 arena
    // centred on the basin it finds, with
    //   origin = (centre.x - 32, max(0, centre.y - 32), centre.z - 32).
    // The HUD prints the CENTRE, which is what the screenshot showed.
    private static readonly int3 Centre = new int3(13012, 31, 12804);
    private static readonly int3 Origin = new int3(13012 - 32, 0, 12804 - 32); // (12980, 0, 12772)
    private static readonly int3 Dims = new int3(64, 64, 64);

    // ---------------------------------------------------------------
    // The reported repro
    // ---------------------------------------------------------------

    [Test]
    public void TheReportedPlacement_IsOutsideTheArena_OnTwoAxes()
    {
        // "placing water at int3(13080, 49, 12759)" straight off the HUD.
        int3 reported = new int3(13080, 49, 12759);

        Assert.IsFalse(FluidGpuSimulation.InRegion(reported, Origin, Dims),
            "the reported placement must be outside the arena -- if this ever " +
            "passes, the repro coordinates no longer describe the bug");

        // Specifically: past the far X face, short of the near Z face, but with
        // a legal Y. Two axes wrong is why it read as 'floating in the sky'.
        Assert.Greater(reported.x, Origin.x + Dims.x - 1, "X is past the far face");
        Assert.Less(reported.z, Origin.z, "Z is short of the near face");
        Assert.IsTrue(reported.y >= Origin.y && reported.y < Origin.y + Dims.y, "Y is legal");
    }

    [Test]
    public void TheReportedPlacement_IsRefusedByTheBrushGuard()
    {
        // radius 1 -- what the fluid brush actually paints.
        Assert.IsFalse(
            FluidGpuSimulation.SphereFitsInRegion(new int3(13080, 49, 12759), 1, Origin, Dims),
            "a fluid brush at the reported cell must be refused; placing it is " +
            "what left frozen blobs hanging in the air");
    }

    [Test]
    public void APlacementAtTheArenaCentre_IsAccepted()
    {
        Assert.IsTrue(FluidGpuSimulation.SphereFitsInRegion(Centre, 1, Origin, Dims),
            "the guard must not refuse a legitimate placement -- a guard that " +
            "refuses everything would 'fix' the screenshot and break the scene");
        Assert.IsTrue(FluidGpuSimulation.SphereFitsInRegion(Centre, 2, Origin, Dims),
            "the stone brush radius must also fit at the centre");
    }

    // ---------------------------------------------------------------
    // All-or-nothing at the edge
    // ---------------------------------------------------------------

    [Test]
    public void ABlobStraddlingTheEdge_IsRefusedEntirely_NotClipped()
    {
        // Centre exactly ON the last legal cell: the +1 offsets fall outside.
        int3 onFarFace = Origin + new int3(Dims.x - 1, 10, 10);
        Assert.IsTrue(FluidGpuSimulation.InRegion(onFarFace, Origin, Dims),
            "the centre cell itself is inside");
        Assert.IsFalse(FluidGpuSimulation.SphereFitsInRegion(onFarFace, 1, Origin, Dims),
            "but the blob straddles the face, so the whole placement is refused; " +
            "clipping it would leave a frozen rim outside -- the same " +
            "silent-partial shape as the §9.4 residency-edge bug");

        // Same on the near face, where the -1 offsets fall outside.
        int3 onNearFace = Origin + new int3(0, 10, 10);
        Assert.IsTrue(FluidGpuSimulation.InRegion(onNearFace, Origin, Dims));
        Assert.IsFalse(FluidGpuSimulation.SphereFitsInRegion(onNearFace, 1, Origin, Dims));
    }

    [Test]
    public void OneCellInFromEveryFace_IsTheFirstAcceptedCell_ForRadiusOne()
    {
        Assert.IsTrue(
            FluidGpuSimulation.SphereFitsInRegion(Origin + new int3(1, 1, 1), 1, Origin, Dims),
            "one cell in from the near corner fits at radius 1");
        Assert.IsTrue(
            FluidGpuSimulation.SphereFitsInRegion(Origin + Dims - new int3(2, 2, 2), 1, Origin, Dims),
            "one cell in from the far corner fits at radius 1");
    }

    [Test]
    public void ALargerBrush_NeedsMoreClearance()
    {
        int3 oneIn = Origin + new int3(1, 1, 1);
        Assert.IsTrue(FluidGpuSimulation.SphereFitsInRegion(oneIn, 1, Origin, Dims));
        Assert.IsFalse(FluidGpuSimulation.SphereFitsInRegion(oneIn, 2, Origin, Dims),
            "radius 2 reaches two cells out, so one cell of clearance is not enough");
    }

    // ---------------------------------------------------------------
    // The shared offset definition
    // ---------------------------------------------------------------

    [Test]
    public void SphereCovers_IsTheR2Rule_AndExcludesCorners()
    {
        // Radius 1: centre + the 6 face neighbours, and nothing diagonal.
        int covered = 0;
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            if (FluidGpuSimulation.SphereCovers(x, y, z, 1)) covered++;
        Assert.AreEqual(7, covered, "radius 1 covers the centre plus 6 faces");

        Assert.IsFalse(FluidGpuSimulation.SphereCovers(1, 1, 1, 1), "corners are excluded");
        Assert.IsFalse(FluidGpuSimulation.SphereCovers(1, 1, 0, 1), "edges are excluded");
        Assert.IsTrue(FluidGpuSimulation.SphereCovers(0, 0, 0, 1), "the centre is covered");
    }

    [Test]
    public void TheGuardChecksEveryCellTheBrushWouldWrite()
    {
        // Independent re-derivation: a placement fits iff no covered offset
        // lands outside. Swept across a band that crosses the near X face, so
        // both answers actually occur.
        for (int dx = -3; dx <= 3; dx++)
        {
            int3 c = Origin + new int3(dx, 10, 10);

            bool expected = true;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (!FluidGpuSimulation.SphereCovers(x, y, z, 1)) continue;
                if (!FluidGpuSimulation.InRegion(c + new int3(x, y, z), Origin, Dims)) expected = false;
            }

            Assert.AreEqual(expected, FluidGpuSimulation.SphereFitsInRegion(c, 1, Origin, Dims),
                $"guard disagreed with a cell-by-cell check at dx={dx}");
        }
    }

    // ---------------------------------------------------------------
    // The pure InRegion form must agree with the instance form's rule
    // ---------------------------------------------------------------

    [Test]
    public void InRegion_IsHalfOpen_OriginInclusive_FarFaceExclusive()
    {
        Assert.IsTrue(FluidGpuSimulation.InRegion(Origin, Origin, Dims),
            "the origin cell is inside");
        Assert.IsTrue(FluidGpuSimulation.InRegion(Origin + Dims - new int3(1, 1, 1), Origin, Dims),
            "origin + dims - 1 is the last inside cell");
        Assert.IsFalse(FluidGpuSimulation.InRegion(Origin + Dims, Origin, Dims),
            "origin + dims is outside");
        Assert.IsFalse(FluidGpuSimulation.InRegion(Origin - new int3(1, 0, 0), Origin, Dims),
            "one short of the origin is outside");
    }
}
