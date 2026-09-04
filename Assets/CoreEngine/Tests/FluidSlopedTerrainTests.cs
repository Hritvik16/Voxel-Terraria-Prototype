// Assets/CoreEngine/Tests/FluidSlopedTerrainTests.cs
//
// The CA's first contact with NON-FLAT ground.
//
// Phases 5a and 5b both only ever tested flat, hand-built basins. The
// Playground dogfood scene put fluid on generated terrain for the first time
// and a lava voxel appeared to be floating in mid-air. Playground is explicitly
// not a diagnostic scene, so this reproduces the shape of that situation here,
// in the oracle, where it can be stepped through.
//
// THE INVARIANT UNDER TEST: at rest, no mobile voxel may have Air directly
// beneath it. Straight down is the FIRST tier of §7.4's Intent hierarchy, so a
// drop with air under it has a legal move and has no business being at rest.
using NUnit.Framework;
using Unity.Mathematics;

public class FluidSlopedTerrainTests
{
    private static FluidReferenceCPU NewSandbox() => new FluidReferenceCPU(4, 4, 4);   // 32^3

    /// A staircase: each step one voxel higher than the last. Deliberately the
    /// simplest non-flat floor -- if something breaks on generated terrain, it
    /// should break here first and be far easier to read.
    private static void BuildStaircase(FluidReferenceCPU sim)
    {
        for (int z = 0; z < 32; z++)
        for (int x = 0; x < 32; x++)
        {
            int h = 1 + (x / 4);                      // 8 steps across X
            for (int y = 0; y <= h; y++)
                sim.EditVoxel(x, y, z, Materials.Stone);
        }
    }

    /// Ticks to rest, asserting conservation every tick. Returns the tick it
    /// rested on, or -1.
    private static int TickToRest(FluidReferenceCPU sim, int maxTicks, int quiet)
    {
        int q = 0;
        for (int t = 0; t < maxTicks; t++)
        {
            sim.Tick();
            var c = sim.CheckConservation();
            Assert.IsTrue(c.Ok, c.Message);
            q = sim.ChangedCellsThisTick == 0 ? q + 1 : 0;
            if (q >= quiet) return sim.TickCount - q + 1;
        }
        return -1;
    }

    [Test]
    public void Water_OnStaircase_NothingFloatsAtRest()
    {
        var sim = NewSandbox();
        BuildStaircase(sim);
        for (int i = 0; i < 40; i++) sim.EditVoxel(6, 20 + (i % 8), 16 + (i / 8), Materials.Water);

        int rest = TickToRest(sim, 600, 12);
        Assert.Greater(rest, 0, "water on a staircase never came to rest");

        int floating = sim.CountFloatingMobile();
        string where = sim.TryFindFloatingMobile(out int3 f, out byte m)
            ? $" first at {f} (material {m})" : "";
        Assert.AreEqual(0, floating,
            $"{floating} mobile voxel(s) at rest with Air beneath them{where} — " +
            "straight down is Intent tier 1, so these had a legal move and stopped anyway");
    }

    [Test]
    public void Sand_OnStaircase_NothingFloatsAtRest()
    {
        var sim = NewSandbox();
        BuildStaircase(sim);
        for (int i = 0; i < 30; i++) sim.EditVoxel(10, 20 + i % 6, 14 + i / 6, Materials.Sand);

        int rest = TickToRest(sim, 600, 12);
        Assert.Greater(rest, 0, "sand on a staircase never came to rest");
        string where = sim.TryFindFloatingMobile(out int3 f, out byte m)
            ? $" first at {f} (material {m})" : "";
        Assert.AreEqual(0, sim.CountFloatingMobile(), $"sand left floating{where}");
    }

    [Test]
    public void Lava_OnStaircase_NothingFloatsAtRest()
    {
        // Lava is the material the Playground capture showed floating, and its
        // tick interval of 6 makes it the most likely to be caught mid-flight
        // by a rest check -- so the quiet window here is well above 6.
        var sim = NewSandbox();
        BuildStaircase(sim);
        for (int i = 0; i < 20; i++) sim.EditVoxel(8, 20 + i % 5, 15 + i / 5, Materials.Lava);

        int rest = TickToRest(sim, 3000, 40);
        Assert.Greater(rest, 0, "lava on a staircase never came to rest within 3000 ticks");
        string where = sim.TryFindFloatingMobile(out int3 f, out byte m)
            ? $" first at {f} (material {m})" : "";
        Assert.AreEqual(0, sim.CountFloatingMobile(), $"lava left floating{where}");
    }

    [Test]
    public void Water_OnOverhang_NothingFloatsAtRest()
    {
        // An overhang: a roof with open air beneath it. Generated terrain has
        // these (carved features, §5.5) and no 5a/5b scenario ever had one.
        var sim = NewSandbox();
        sim.FillBox(new int3(0, 0, 0), new int3(31, 0, 31), Materials.Stone);
        sim.FillBox(new int3(8, 12, 8), new int3(23, 12, 23), Materials.Stone);   // the roof
        for (int i = 0; i < 40; i++) sim.EditVoxel(4, 16 + (i % 10), 16 + (i / 10), Materials.Water);

        int rest = TickToRest(sim, 800, 12);
        Assert.Greater(rest, 0, "water near an overhang never came to rest");
        string where = sim.TryFindFloatingMobile(out int3 f, out byte m)
            ? $" first at {f} (material {m})" : "";
        Assert.AreEqual(0, sim.CountFloatingMobile(), $"water left floating{where}");
    }
}
