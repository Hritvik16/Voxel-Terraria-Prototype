// Assets/CoreEngine/Tests/FluidBenchPlanTests.cs
//
// Coverage for the dense-vs-tiled A/B's planning math. The benchmark itself
// cannot be unit tested -- it is a wall clock in a standalone player -- but
// everything that decides WHAT it measures is pure, and that is the part where
// a silent mistake would produce confident, wrong numbers rather than an
// obvious failure.
//
// The specific failure this file exists to prevent: a config that parses to
// something other than its label. A row in the CSV saying "tiled_s64_v8000"
// whose scenario was actually one pocket of 8000 would be indistinguishable
// from a real result, and would be read as evidence that scatter is free.

using NUnit.Framework;
using System;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;

public class FluidBenchPlanTests
{
    // =====================================================================
    // Parsing
    // =====================================================================

    [Test]
    public void Parse_ReadsPathVolumeAndPockets()
    {
        var c = FluidBenchPlan.Parse("tiled_s64_v8000");
        Assert.IsTrue(c.Tiled, "tiled_ must select the sparse path");
        Assert.AreEqual(8000, c.TargetVoxels);
        Assert.AreEqual(64, c.Pockets);
        Assert.IsFalse(c.IsDriftcheck);
        Assert.AreEqual("tiled_s64_v8000", c.Label, "the label must survive verbatim for CSV traceability");
    }

    [Test]
    public void Parse_DenseAndTiled_AreDistinct()
    {
        Assert.IsFalse(FluidBenchPlan.Parse("dense_v500").Tiled);
        Assert.IsTrue(FluidBenchPlan.Parse("tiled_v500").Tiled);
    }

    [Test]
    public void Parse_DefaultsToOnePocket_WhenNoScatterFieldIsGiven()
    {
        Assert.AreEqual(1, FluidBenchPlan.Parse("dense_v2000").Pockets,
            "step 1's configs name no pocket count and must mean one contiguous body");
    }

    [Test]
    public void Parse_RecognisesTheDriftcheckSuffix_WithoutChangingWhatIsMeasured()
    {
        var plain = FluidBenchPlan.Parse("tiled_v8000");
        var drift = FluidBenchPlan.Parse("tiled_v8000_REPEAT_driftcheck");

        Assert.IsTrue(drift.IsDriftcheck);
        Assert.IsFalse(plain.IsDriftcheck);
        // The whole point of a driftcheck is that it is the SAME measurement.
        Assert.AreEqual(plain.Tiled, drift.Tiled);
        Assert.AreEqual(plain.TargetVoxels, drift.TargetVoxels);
        Assert.AreEqual(plain.Pockets, drift.Pockets);
    }

    [Test]
    public void Parse_RejectsUnknownInput_RatherThanDefaulting()
    {
        // Each of these would otherwise produce a plausible-looking CSV row
        // for a scenario nobody asked for.
        Assert.Throws<ArgumentException>(() => FluidBenchPlan.Parse(""));
        Assert.Throws<ArgumentException>(() => FluidBenchPlan.Parse("sparse_v8000"), "unknown path");
        Assert.Throws<ArgumentException>(() => FluidBenchPlan.Parse("tiled"), "no volume");
        Assert.Throws<ArgumentException>(() => FluidBenchPlan.Parse("tiled_s8"), "no volume");
        Assert.Throws<ArgumentException>(() => FluidBenchPlan.Parse("tiled_v8000_q3"), "unknown field");
        Assert.Throws<ArgumentException>(() => FluidBenchPlan.Parse("tiled_vmany"), "non-numeric volume");
        Assert.Throws<ArgumentException>(() => FluidBenchPlan.Parse("tiled_s0_v8000"), "zero pockets");
    }

    // =====================================================================
    // Layout
    // =====================================================================

    [Test]
    public void Layout_SplitsTheVolumeAcrossTheRequestedPocketCount()
    {
        var cfg = FluidBenchPlan.Parse("tiled_s8_v8000");
        var pockets = FluidBenchPlan.Layout(cfg, new int3(1000, 0, 1000), 40);

        Assert.AreEqual(8, pockets.Length);
        foreach (var p in pockets)
            Assert.AreEqual(1000, p.Voxels, "8 pockets of 8000 voxels is 1000 each (10^3)");
    }

    [Test]
    public void Layout_PlacedTotalTracksTheTarget_WithinCubeRounding()
    {
        // Not exact, deliberately -- see Layout's comment. What must hold is
        // that it is CLOSE, so the ladder's rungs stay meaningfully apart.
        foreach (string label in new[] { "tiled_v500", "tiled_v2000", "tiled_v8000", "tiled_v32000" })
        {
            var cfg = FluidBenchPlan.Parse(label);
            int placed = FluidBenchPlan.PlacedVoxels(
                FluidBenchPlan.Layout(cfg, new int3(1000, 0, 1000), 40));
            double ratio = (double)placed / cfg.TargetVoxels;
            Assert.That(ratio, Is.InRange(0.75, 1.35),
                $"{label} placed {placed} for a target of {cfg.TargetVoxels}");
        }
    }

    [Test]
    public void Layout_SeparatesPocketsByMoreThanATile_OrScatterMeasuresNothing()
    {
        // THE LOAD-BEARING ONE. Tiles are 32 voxels on an edge; two pockets
        // closer than that share a tile, and a "64 pocket" scatter would then
        // light up far fewer than 64 tiles while still being labelled s64.
        Assert.Greater(FluidBenchPlan.PocketSpacingVoxels, ChunkFluidMask.TILE_EDGE,
            "pocket spacing must exceed the tile edge");

        var cfg = FluidBenchPlan.Parse("tiled_s64_v8000");
        var pockets = FluidBenchPlan.Layout(cfg, new int3(1000, 0, 1000), 40);

        for (int i = 0; i < pockets.Length; i++)
            for (int j = i + 1; j < pockets.Length; j++)
            {
                int3 a = pockets[i].Lo / ChunkFluidMask.TILE_EDGE;
                int3 b = pockets[j].Lo / ChunkFluidMask.TILE_EDGE;
                Assert.IsFalse(a.Equals(b), $"pockets {i} and {j} land in the same tile {a}");
            }
    }

    [Test]
    public void Layout_PlacesFluidAboveTheSurface_SoItIsStillMovingWhenSampled()
    {
        const int surface = 40;
        var pockets = FluidBenchPlan.Layout(FluidBenchPlan.Parse("tiled_v8000"),
                                            new int3(1000, 0, 1000), surface);
        foreach (var p in pockets)
            Assert.Greater(p.Lo.y, surface,
                "a settled pool measures the cost of doing nothing, not of simulating");
    }

    [Test]
    public void Layout_CentresTheGridOnThePlayerVoxel()
    {
        // §7.4's radius is measured from the player voxel. An off-centre
        // scatter would clip on one side and quietly reduce the active set.
        var cfg = FluidBenchPlan.Parse("tiled_s64_v8000");
        int3 centre = new int3(1000, 0, 1000);
        var pockets = FluidBenchPlan.Layout(cfg, centre, 40);

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var p in pockets)
        {
            minX = math.min(minX, p.Lo.x); maxX = math.max(maxX, p.Lo.x + p.Size.x);
            minZ = math.min(minZ, p.Lo.z); maxZ = math.max(maxZ, p.Lo.z + p.Size.z);
        }
        Assert.That((minX + maxX) / 2, Is.EqualTo(centre.x).Within(2));
        Assert.That((minZ + maxZ) / 2, Is.EqualTo(centre.z).Within(2));
    }

    // =====================================================================
    // Dense region sizing
    // =====================================================================

    [Test]
    public void SizeDenseRegion_IsAlwaysAPowerOfTwoPerAxis()
    {
        // Non-power-of-two does not fail loudly, it ALIASES -- §6.2's phantom
        // terrain is the precedent, and FluidGpuSimulation's constructor calls
        // RequirePow2 on every axis.
        foreach (string label in new[] { "dense_v500", "dense_v2000", "dense_v8000",
                                         "dense_v32000", "dense_s8_v8000", "dense_s64_v8000" })
        {
            var cfg = FluidBenchPlan.Parse(label);
            var pockets = FluidBenchPlan.Layout(cfg, new int3(1000, 0, 1000), 40);
            int3 dims = FluidBenchPlan.SizeDenseRegion(pockets, 40, out _);

            foreach (int d in new[] { dims.x, dims.y, dims.z })
            {
                Assert.Greater(d, 0, label);
                Assert.AreEqual(0, d & (d - 1), $"{label}: {d} is not a power of two");
            }
        }
    }

    [Test]
    public void SizeDenseRegion_ContainsEveryPocketAndTheSurfaceTheyLandOn()
    {
        const int surface = 40;
        foreach (string label in new[] { "dense_v500", "dense_v8000", "dense_v32000", "dense_s8_v8000" })
        {
            var cfg = FluidBenchPlan.Parse(label);
            var pockets = FluidBenchPlan.Layout(cfg, new int3(1000, 0, 1000), surface);
            int3 dims = FluidBenchPlan.SizeDenseRegion(pockets, surface, out int3 origin);
            int3 hiBound = origin + dims;

            foreach (var p in pockets)
            {
                Assert.IsTrue(math.all(p.Lo >= origin), $"{label}: pocket {p.Lo} below region origin {origin}");
                Assert.IsTrue(math.all(p.Lo + p.Size <= hiBound), $"{label}: pocket exceeds region top {hiBound}");
            }
            // The landing surface must be inside too, WITH clearance below it,
            // or dense drops voxels out of region as they settle and simulates
            // less than tiled does.
            //
            // ASSERTING `origin.y <= surface` IS NOT ENOUGH, and a mutation
            // sweep is what showed it: DropHeightVoxels and SpreadMarginVoxels
            // are both 24, so `lo -= SpreadMargin` lands lo.y exactly on the
            // surface all by itself. Deleting the reach-down entirely left
            // every test green. Requiring real clearance makes the reach-down
            // load-bearing again at these constants.
            Assert.LessOrEqual(origin.y, surface - FluidBenchPlan.SpreadMarginVoxels,
                $"{label}: region floor {origin.y} leaves no clearance under the surface {surface}");
        }
    }

    [Test]
    public void SizeDenseRegion_ReachesDownToASurfaceFarBelowThePockets()
    {
        // THE GENERAL CASE THE REACH-DOWN EXISTS FOR, and the one the other
        // tests could not see. Fluid does not always land at
        // pocketY - DropHeight: a scattered pocket sits at the CENTRE's
        // surface height but falls onto whatever terrain is under it, which
        // on a slope or near the coast is far lower. If the region floor
        // tracks the pockets instead of the landing surface, dense loses
        // those voxels mid-fall and quietly simulates less than tiled does --
        // which would read as "tiling is slower", not as a bug.
        var cfg = FluidBenchPlan.Parse("dense_v8000");
        var pockets = FluidBenchPlan.Layout(cfg, new int3(1000, 0, 1000), 200);

        const int landingSurface = 40;   // 160 voxels below where the fluid starts
        FluidBenchPlan.SizeDenseRegion(pockets, landingSurface, out int3 origin);

        Assert.LessOrEqual(origin.y, landingSurface - FluidBenchPlan.SpreadMarginVoxels,
            $"region floor {origin.y} does not reach the landing surface {landingSurface}");
    }

    [Test]
    public void SizeDenseRegion_GrowsWithTheScenario_SoDenseIsSteelmanned()
    {
        // The dense box is re-sized per config rather than pinned at the
        // shipped 64^3. If it did not grow, the ladder would be comparing
        // tiled against a fixed cost and the "per voxel" question would be
        // unanswerable.
        int3 small = FluidBenchPlan.SizeDenseRegion(
            FluidBenchPlan.Layout(FluidBenchPlan.Parse("dense_v500"), new int3(1000, 0, 1000), 40), 40, out _);
        int3 large = FluidBenchPlan.SizeDenseRegion(
            FluidBenchPlan.Layout(FluidBenchPlan.Parse("dense_s64_v8000"), new int3(1000, 0, 1000), 40), 40, out _);

        long smallCells = (long)small.x * small.y * small.z;
        long largeCells = (long)large.x * large.y * large.z;
        Assert.Greater(largeCells, smallCells,
            "a 64-pocket scatter must size a bigger dense box than a single small pool");
    }

    [Test]
    public void NextPow2_IsExactOnPowersAndRoundsUpOtherwise()
    {
        Assert.AreEqual(1, FluidBenchPlan.NextPow2(1));
        Assert.AreEqual(64, FluidBenchPlan.NextPow2(64), "an exact power must not double");
        Assert.AreEqual(128, FluidBenchPlan.NextPow2(65));
        Assert.AreEqual(32, FluidBenchPlan.NextPow2(17));
        Assert.AreEqual(1, FluidBenchPlan.NextPow2(0));
    }
}
