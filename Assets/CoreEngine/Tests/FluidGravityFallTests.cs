// Assets/CoreEngine/Tests/FluidGravityFallTests.cs
//
// Task 94 (AMENDMENT_8_16_FLUID_FALL.md): gravity fall, the same for every fluid.
// A fluid with air below falls FluidFall's table each tick (ungated by
// viscosity), accelerating at g up to a whole-voxel terminal speed, and stops
// above the first cell that was not air at tick start. Checked on the CPU oracle
// (FluidReferenceCPU); FluidCA.compute reads the same integer table and runs the
// same branch, and the FluidTiled rig checks the GPU's conservation.
using NUnit.Framework;
using Unity.Mathematics;

public class FluidGravityFallTests
{
    private static readonly byte[] AllFluids =
    {
        Materials.Water, Materials.Lava, Materials.Honey, Materials.Oil,
        Materials.Acid, Materials.Sludge, Materials.Quicksand,
    };

    private const double Dt = 1.0 / 4.5;   // task 91's fixed rate
    private const int Top = 500;

    /// 8 x 512 x 8 voxels of air: a 51 m drop shaft.
    private static FluidReferenceCPU Shaft()
    {
        var sim = new FluidReferenceCPU(1, 64, 1);
        sim.FallTickSeconds = Dt;
        return sim;
    }

    /// Lowest y holding `m` in column (x, z), or -1.
    private static int Front(FluidReferenceCPU sim, int x, int z, byte m)
    {
        for (int y = 0; y < sim.SizeYVoxels; y++)
            if (sim.GetVoxel(x, y, z) == m) return y;
        return -1;
    }

    /// Lowest y holding `m` anywhere, or -1.
    private static int Lowest(FluidReferenceCPU sim, byte m)
    {
        for (int y = 0; y < sim.SizeYVoxels; y++)
        for (int z = 0; z < sim.SizeZVoxels; z++)
        for (int x = 0; x < sim.SizeXVoxels; x++)
            if (sim.GetVoxel(x, y, z) == m) return y;
        return -1;
    }

    private static void TickChecked(FluidReferenceCPU sim)
    {
        sim.Tick();
        var c = sim.CheckConservation();
        Assert.IsTrue(c.Ok, c.Message);
        Assert.AreEqual(0, sim.CountDuplicateSlotOwnership(), "duplicate slot ownership");
    }

    [Test]
    public void Table_IsTheIntegratedFall_WithinOneVoxel_AndNeverSlowerThanOneVoxelPerTick()
    {
        int[] t = FluidFall.BuildTable(Dt);
        Assert.AreEqual(27, FluidFall.TerminalVoxelsPerTick(Dt), "12 m/s at 4.5 Hz, rounded to whole voxels");
        for (int k = 1; k < FluidFall.TableSize; k++)
        {
            double exact = FluidFall.DistanceM(k * Dt, Dt) / FluidFall.VoxelM;
            Assert.LessOrEqual(t[k], exact + 1e-6, $"k {k}: table ahead of the exact fall");
            Assert.Greater(t[k], exact - 1.0, $"k {k}: table more than a voxel behind");
            Assert.LessOrEqual(t[k] - t[k - 1], 27, $"k {k}: step over the cap");
        }
        // Once at the cap every tick is exactly the cap (so k saturating at 255 is exact).
        for (int k = 8; k < FluidFall.TableSize; k++) Assert.AreEqual(27, t[k] - t[k - 1], $"k {k}");

        // A fast rate: g dt^2 / 2 is under a voxel, the old one voxel per tick is the floor.
        int[] fast = FluidFall.BuildTable(1.0 / 60.0);
        for (int k = 1; k < FluidFall.TableSize; k++) Assert.GreaterOrEqual(fast[k] - fast[k - 1], 1, $"60 Hz k {k}");
    }

    [Test]
    public void SingleDrop_FallDistance_MatchesTheIntegratedSpeed_ForEveryFluid_AndIsTheSameForAll()
    {
        // 0.5 / 1 / 2 s at 4.5 Hz = 2.25 / 4.5 / 9 ticks: checked at every tick
        // through 2 s, reported at the nearest whole tick to each.
        int[] reportTicks = { 2, 5, 9 };
        int[] reference = null;
        foreach (byte m in AllFluids)
        {
            var sim = Shaft();
            sim.EditVoxel(4, Top, 4, m);
            sim.ResetLedgerToCurrentState();
            var fronts = new int[10];
            fronts[0] = Top;
            for (int k = 1; k <= 9; k++)
            {
                TickChecked(sim);
                fronts[k] = Front(sim, 4, 4, m);
                double exact = FluidFall.DistanceM(k * Dt, Dt) / FluidFall.VoxelM;
                double fell = Top - fronts[k];
                Assert.That(math.abs(fell - exact), Is.LessThan(1.0),
                    $"{m} after {k} ticks ({k * Dt:F2} s): fell {fell} voxels, integrated {exact:F2}");
            }
            foreach (int k in reportTicks)
                TestContext.WriteLine($"material {m}: +{k * Dt:F2} s fell {(Top - fronts[k]) * 0.1:F2} m " +
                                      $"(integrated {FluidFall.DistanceM(k * Dt, Dt):F2} m), speed {FluidFall.SpeedMps(k * Dt, Dt):F2} m/s");
            if (reference == null) reference = fronts;
            else Assert.AreEqual(reference, fronts, $"material {m} falls differently from water");
        }
    }

    [Test]
    public void Fall_NeverPassesThroughAOneVoxelSlab_AtAnyOffset()
    {
        // The slab height sweeps a whole terminal step, so some fall's step ends
        // exactly ON the slab, one voxel past it and so on. Glass: reacts with
        // nothing (acid-proof, task 93e1).
        foreach (byte m in AllFluids)
        for (int slab = 300; slab < 300 + 28; slab += (m == Materials.Water ? 1 : 9))
        {
            var sim = Shaft();
            sim.FillBox(new int3(0, slab, 0), new int3(7, slab, 7), Materials.Glass);
            sim.EditVoxel(4, Top, 4, m);
            sim.ResetLedgerToCurrentState();
            for (int i = 0; i < 20; i++)
            {
                TickChecked(sim);
                Assert.Greater(Lowest(sim, m), slab, $"{m} passed the slab at y {slab} on tick {i + 1}");
            }
            // Landed ON it (it may have spread sideways since).
            Assert.AreEqual(slab + 1, Lowest(sim, m), $"{m} did not land on the slab at {slab}");
            Assert.AreEqual(1, sim.CountMaterial(m));
        }
    }

    [Test]
    public void Cap_Holds_EveryTickMovesAtMostTheTerminalStep()
    {
        var sim = Shaft();
        sim.EditVoxel(4, Top, 4, Materials.Honey);
        sim.ResetLedgerToCurrentState();
        int prev = Top, cap = FluidFall.TerminalVoxelsPerTick(Dt);
        for (int k = 1; k <= 18; k++)   // 4 s
        {
            TickChecked(sim);
            int y = Front(sim, 4, 4, Materials.Honey);
            int step = prev - y;
            Assert.LessOrEqual(step, cap, $"tick {k}");
            if (k >= 8) Assert.AreEqual(cap, step, $"tick {k}: at terminal speed");
            prev = y;
        }
        TestContext.WriteLine($"terminal {FluidFall.TerminalMpsFor(Dt):F2} m/s = {cap} voxels/tick; 4 s fall {(Top - prev) * 0.1:F1} m");
    }

    [Test]
    public void Off_IsThePre94Rule_OneVoxelPerEvaluatedTick()
    {
        foreach (byte m in new[] { Materials.Water, Materials.Lava, Materials.Honey })
        {
            var sim = Shaft();
            sim.GravityFall = false;
            sim.EditVoxel(4, Top, 4, m);
            sim.ResetLedgerToCurrentState();
            for (int k = 0; k < 60; k++) TickChecked(sim);
            int expect = 60 / (int)MaterialRules.TickInterval(m);
            Assert.AreEqual(Top - expect, Front(sim, 4, 4, m), $"{m}");
        }
    }

    [Test]
    public void Viscosity_StillGatesSideways_LavaSpreadsOnceEverySixTicks()
    {
        // 8.16 keeps the tick interval for everything but free fall: two lava
        // cells stacked on a floor; the top one can only go down-diagonal or
        // sideways, and does so on its 6th tick, not its 1st.
        var sim = Shaft();
        sim.FillBox(new int3(0, 100, 0), new int3(7, 100, 7), Materials.Glass);
        sim.EditVoxel(4, 101, 4, Materials.Lava);
        sim.EditVoxel(4, 102, 4, Materials.Lava);
        sim.ResetLedgerToCurrentState();
        int first = -1;
        for (int t = 1; t <= 12 && first < 0; t++)
        {
            TickChecked(sim);
            if (sim.MovesThisTick > 0) first = t;
        }
        Assert.AreEqual(6, first, "lava's sideways move waits for its viscosity interval");
    }

    [Test]
    public void HoneyOverSteam_SwapsEveryTick_NotEveryThirtieth()
    {
        var sim = Shaft();
        sim.FillBox(new int3(4, 0, 4), new int3(4, 0, 4), Materials.Glass);
        sim.FillBox(new int3(4, 1, 4), new int3(4, 10, 4), Materials.Steam);
        sim.FillBox(new int3(3, 1, 3), new int3(5, 12, 3), Materials.Glass);
        sim.FillBox(new int3(3, 1, 5), new int3(5, 12, 5), Materials.Glass);
        sim.FillBox(new int3(3, 1, 4), new int3(3, 12, 4), Materials.Glass);
        sim.FillBox(new int3(5, 1, 4), new int3(5, 12, 4), Materials.Glass);
        sim.EditVoxel(4, 11, 4, Materials.Honey);
        sim.ResetLedgerToCurrentState();
        for (int k = 0; k < 12; k++) TickChecked(sim);
        Assert.Less(Front(sim, 4, 4, Materials.Honey), 6, "honey sank through steam at the fall rate");
    }

    [Test]
    public void Columns_OfEveryFluid_InABasin_ConserveMassExactly()
    {
        // A 32^3 basin, a 3x20x3 column of each fluid (and water + lava side by
        // side, which react) released together: fast falls, landings, collisions
        // with laterally spreading cells and reactions all in one run.
        var sim = new FluidReferenceCPU(4, 4, 4);
        sim.FallTickSeconds = Dt;
        sim.FillBox(new int3(0, 0, 0), new int3(31, 0, 31), Materials.Glass);
        for (int y = 1; y < 32; y++)
        {
            sim.FillBox(new int3(0, y, 0), new int3(31, y, 0), Materials.Glass);
            sim.FillBox(new int3(0, y, 31), new int3(31, y, 31), Materials.Glass);
            sim.FillBox(new int3(0, y, 0), new int3(0, y, 31), Materials.Glass);
            sim.FillBox(new int3(31, y, 0), new int3(31, y, 31), Materials.Glass);
        }
        int i = 0;
        foreach (byte m in AllFluids)
        {
            int x = 3 + (i % 4) * 7, z = 3 + (i / 4) * 12;
            for (int y = 10; y < 30; y++) sim.FillBox(new int3(x, y, z), new int3(x + 2, y, z + 2), m);
            i++;
        }
        sim.ResetLedgerToCurrentState();
        int fast = 0;
        for (int t = 0; t < 300; t++) { TickChecked(sim); fast += sim.FastFallsThisTick; }
        Assert.Greater(fast, 0, "no fast falls happened");
        Assert.AreEqual(0, sim.OwnershipGuardFiredTotal);
        TestContext.WriteLine($"fast falls {fast}, active slots at end {sim.ActiveSlotCount}");
    }
}
