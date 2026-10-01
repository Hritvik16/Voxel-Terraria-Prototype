// Assets/CoreEngine/Tests/FluidRosterTests.cs
//
// DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md §2.7's fluid roster, proven on the
// CPU oracle (FluidReferenceCPU) BEFORE the GPU port, per FluidCA.compute's
// header rule. Every test checks the conservation ledger on every tick (a
// reaction's consumption is the only legal way mobile mass leaves), that the
// run reaches rest, and the fluid's own behaviour:
//   Oil        floats on water (density swap), from below AND from above
//   Layering   oil / water / honey sort by density
//   Acid       dissolves stone voxel-for-voxel and is consumed; obsidian is immune
//   Sludge     emissive AND mobile: tile aggregate light, never a point light
//   Quicksand  the slow viscosity tier: slower than honey, still reaches rest
//   Steam      rises and pools under a ceiling; water sinks through it
using NUnit.Framework;
using Unity.Mathematics;

public class FluidRosterTests
{
    // 16 x 32 x 16 voxels; the sandbox bounds are the walls.
    private static FluidReferenceCPU Basin(bool stoneFloor = true)
    {
        var sim = new FluidReferenceCPU(2, 4, 2);
        if (stoneFloor) sim.FillBox(new int3(0, 0, 0), new int3(15, 0, 15), Materials.Stone);
        sim.ResetLedgerToCurrentState();
        return sim;
    }

    /// Ticks until `quiet` consecutive ticks change nothing, checking the
    /// ledger on every tick. Returns the tick rest began, or -1.
    private static int RunToRest(FluidReferenceCPU sim, int maxTicks, int quiet = 80)
    {
        int still = 0;
        for (int t = 0; t < maxTicks; t++)
        {
            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, $"tick {sim.TickCount}: {check.Message}");
            still = sim.ChangedCellsThisTick == 0 ? still + 1 : 0;
            if (still >= quiet) return sim.TickCount - quiet;
        }
        return -1;
    }

    private static void AssertLayeredByColumn(FluidReferenceCPU sim, byte heavy, byte light)
    {
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
        {
            int maxHeavy = -1, minLight = int.MaxValue;
            for (int y = 0; y < sim.SizeYVoxels; y++)
            {
                byte m = sim.GetVoxel(x, y, z);
                if (m == heavy) maxHeavy = math.max(maxHeavy, y);
                if (m == light) minLight = math.min(minLight, y);
            }
            Assert.IsTrue(maxHeavy < minLight,
                $"column ({x},{z}): material {heavy} at y {maxHeavy} is above material {light} at y {minLight}");
        }
    }

    [Test]
    public void Oil_Under_Water_RisesToTheTop()
    {
        var sim = Basin();
        sim.FillBox(new int3(0, 1, 0), new int3(15, 3, 15), Materials.Oil);
        sim.FillBox(new int3(0, 4, 0), new int3(15, 6, 15), Materials.Water);
        int oil = sim.CountMaterial(Materials.Oil), water = sim.CountMaterial(Materials.Water);
        Assert.Greater(RunToRest(sim, 20000), 0, "never reached rest");
        Assert.AreEqual(oil, sim.CountMaterial(Materials.Oil));
        Assert.AreEqual(water, sim.CountMaterial(Materials.Water));
        AssertLayeredByColumn(sim, Materials.Water, Materials.Oil);
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
        {
            for (int y = 1; y <= 3; y++) Assert.AreEqual(Materials.Water, sim.GetVoxel(x, y, z), $"({x},{y},{z})");
            for (int y = 4; y <= 6; y++) Assert.AreEqual(Materials.Oil, sim.GetVoxel(x, y, z), $"({x},{y},{z})");
        }
        Assert.AreEqual(0, sim.CountFloatingMobile());
    }

    [Test]
    public void Water_PouredOntoOil_SinksBelowIt()
    {
        var sim = Basin();
        sim.FillBox(new int3(0, 1, 0), new int3(15, 2, 15), Materials.Oil);
        sim.FillBox(new int3(6, 20, 6), new int3(9, 21, 9), Materials.Water);   // 32 drops from above
        Assert.Greater(RunToRest(sim, 20000), 0, "never reached rest");
        Assert.AreEqual(32, sim.CountMaterial(Materials.Water));
        Assert.AreEqual(512, sim.CountMaterial(Materials.Oil));
        AssertLayeredByColumn(sim, Materials.Water, Materials.Oil);
        Assert.AreEqual(0, sim.CountFloatingMobile());
    }

    [Test]
    public void OilWaterHoney_SortByDensity()
    {
        var sim = Basin();
        // Lightest at the bottom, densest on top: the worst starting order.
        sim.FillBox(new int3(0, 1, 0), new int3(15, 1, 15), Materials.Oil);
        sim.FillBox(new int3(0, 2, 0), new int3(15, 2, 15), Materials.Water);
        sim.FillBox(new int3(0, 3, 0), new int3(15, 3, 15), Materials.Honey);
        Assert.Greater(RunToRest(sim, 60000), 0, "never reached rest");
        Assert.AreEqual(256, sim.CountMaterial(Materials.Oil));
        Assert.AreEqual(256, sim.CountMaterial(Materials.Water));
        Assert.AreEqual(256, sim.CountMaterial(Materials.Honey));
        AssertLayeredByColumn(sim, Materials.Honey, Materials.Water);
        AssertLayeredByColumn(sim, Materials.Water, Materials.Oil);
    }

    [Test]
    public void Density_Ranks_AreOrderedAndReactionsBeatSwaps()
    {
        Assert.IsTrue(MaterialRules.SinksThrough(Materials.Water, Materials.Oil));
        Assert.IsFalse(MaterialRules.SinksThrough(Materials.Oil, Materials.Water));
        Assert.IsTrue(MaterialRules.SinksThrough(Materials.Honey, Materials.Water));
        Assert.IsTrue(MaterialRules.SinksThrough(Materials.Water, Materials.Steam));
        Assert.IsFalse(MaterialRules.SinksThrough(Materials.Water, Materials.Lava), "water+lava react; they never swap");
        Assert.IsFalse(MaterialRules.SinksThrough(Materials.Sand, Materials.Water), "a falling solid never swaps");
        Assert.IsFalse(MaterialRules.SinksThrough(Materials.Water, Materials.Water));
        Assert.AreEqual(0u, MaterialRules.DensityRank(Materials.Stone), "non-fluids have no rank");
    }

    [Test]
    public void Acid_DissolvesStone_OneForOne_AndIsConsumed()
    {
        var sim = Basin();
        sim.FillBox(new int3(4, 1, 4), new int3(11, 6, 11), Materials.Stone);    // an 8x6x8 block
        sim.ResetLedgerToCurrentState();
        int stone0 = sim.CountMaterial(Materials.Stone);
        sim.FillBox(new int3(6, 12, 6), new int3(9, 12, 9), Materials.Acid);     // 16 acid drops
        Assert.AreEqual(16, sim.CountMaterial(Materials.Acid));
        Assert.Greater(RunToRest(sim, 20000), 0, "never reached rest");
        int acidLeft = sim.CountMaterial(Materials.Acid);
        int dissolved = stone0 - sim.CountMaterial(Materials.Stone);
        Assert.AreEqual(0, acidLeft, "acid resting against stone should have reacted");
        Assert.AreEqual(16 - acidLeft, dissolved, "each acid voxel dissolves exactly one stone voxel");
        // The ledger checked every tick already; the consumed count closes it.
        Assert.IsTrue(sim.CheckConservation().Ok);
    }

    [Test]
    public void Acid_CannotEatObsidian()
    {
        var sim = Basin(stoneFloor: false);
        sim.FillBox(new int3(0, 0, 0), new int3(15, 0, 15), Materials.Obsidian);
        sim.ResetLedgerToCurrentState();
        sim.FillBox(new int3(6, 5, 6), new int3(9, 5, 9), Materials.Acid);
        Assert.Greater(RunToRest(sim, 20000), 0, "never reached rest");
        Assert.AreEqual(16, sim.CountMaterial(Materials.Acid));
        Assert.AreEqual(256, sim.CountMaterial(Materials.Obsidian));
        Assert.AreEqual(0, sim.CountFloatingMobile());
    }

    [Test]
    public void Sludge_GlowsThroughTheTileAggregate_NeverAPointLight()
    {
        Assert.IsTrue(MaterialRules.IsMobile(Materials.Sludge));
        Assert.IsTrue(MaterialRules.IsEmissiveMaterial(Materials.Sludge));
        Assert.IsTrue(MaterialPalette.IsSelfLit(Materials.Sludge));
        var lights = new VoxelEngine.Rendering.LightSourceBuffer();
        for (int i = 0; i < 200; i++)
            Assert.IsFalse(lights.Register(new int3(i % 20, 5, i / 20), Materials.Sludge),
                "a mobile emissive must never register as a point light (CLAUDE.md, 43 ms/frame lava incident)");
        Assert.AreEqual(0, lights.RegisteredCount);
        Assert.AreEqual(200, lights.LavaVoxelCount, "sludge is counted into the tile aggregates");
        Assert.Greater(lights.LavaTileCount, 0);

        var sim = Basin();
        sim.FillBox(new int3(5, 10, 5), new int3(10, 12, 10), Materials.Sludge);
        int n = sim.CountMaterial(Materials.Sludge);
        Assert.Greater(RunToRest(sim, 40000, quiet: 200), 0, "never reached rest");
        Assert.AreEqual(n, sim.CountMaterial(Materials.Sludge));
        Assert.AreEqual(0, sim.CountFloatingMobile());
    }

    [Test]
    public void Quicksand_IsTheSlowTier_AndStillSettles()
    {
        Assert.Greater(MaterialRules.TickInterval(Materials.Quicksand), MaterialRules.TickInterval(Materials.Honey));
        Assert.Greater(MaterialRules.ViscosityDrag(Materials.Quicksand), MaterialRules.ViscosityDrag(Materials.Honey));

        int Spread(byte m, int ticks)
        {
            var s = Basin();
            s.FillBox(new int3(7, 1, 7), new int3(8, 8, 8), m);   // a 2x8x2 column
            for (int t = 0; t < ticks; t++) { s.Tick(); Assert.IsTrue(s.CheckConservation().Ok); }
            int floor = 0;
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) if (s.GetVoxel(x, 1, z) == m) floor++;
            return floor;
        }
        int water = Spread(Materials.Water, 300), sand = Spread(Materials.Quicksand, 300);
        Assert.Greater(water, sand, $"after 300 ticks water covers {water} floor cells, quicksand {sand}: quicksand must be slower");

        var sim = Basin();
        sim.FillBox(new int3(7, 1, 7), new int3(8, 4, 8), Materials.Quicksand);
        Assert.Greater(RunToRest(sim, 200000, quiet: 400), 0, "quicksand never reached rest");
        Assert.AreEqual(16, sim.CountMaterial(Materials.Quicksand));
        Assert.AreEqual(0, sim.CountFloatingMobile());
    }

    [Test]
    public void Steam_RisesAndPoolsUnderTheCeiling()
    {
        var sim = Basin();
        sim.FillBox(new int3(6, 1, 6), new int3(9, 2, 9), Materials.Steam);   // 32 voxels
        Assert.Greater(RunToRest(sim, 20000), 0, "never reached rest");
        Assert.AreEqual(32, sim.CountMaterial(Materials.Steam));
        int top = sim.SizeYVoxels - 1;
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < top; y++)
            Assert.AreNotEqual(Materials.Steam, sim.GetVoxel(x, y, z), $"steam left below the ceiling at ({x},{y},{z})");
        Assert.AreEqual(0, sim.CountFloatingMobile(), "no steam may rest with Air above it");
    }

    [Test]
    public void Water_SinksThroughSteam_SteamEndsOnTop()
    {
        var sim = Basin();
        sim.FillBox(new int3(0, 1, 0), new int3(15, 2, 15), Materials.Steam);  // 512
        sim.FillBox(new int3(0, 3, 0), new int3(15, 3, 15), Materials.Water);  // 256
        Assert.Greater(RunToRest(sim, 40000), 0, "never reached rest");
        Assert.AreEqual(512, sim.CountMaterial(Materials.Steam));
        Assert.AreEqual(256, sim.CountMaterial(Materials.Water));
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
            Assert.AreEqual(Materials.Water, sim.GetVoxel(x, 1, z), $"water should floor the basin at ({x},{z})");
        AssertLayeredByColumn(sim, Materials.Water, Materials.Steam);
        Assert.AreEqual(0, sim.CountFloatingMobile());
    }
}
