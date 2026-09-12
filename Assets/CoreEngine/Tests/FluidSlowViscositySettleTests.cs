// Assets/CoreEngine/Tests/FluidSlowViscositySettleTests.cs
//
// Closes the gap PHASE_5A_COMPLETION.md §8.1 flagged and deliberately left open:
//
//   "ChangedCellsThisTick reads 'at rest' 5 of every 6 ticks for lava
//    (interval 6). Affects no current test (all rest-based assertions use Water
//    or Sand, interval 1)... WHOEVER WRITES A LAVA OR HONEY SETTLE-TIME TEST:
//    do not use a plain 'N consecutive ticks with 0 changed cells' window.
//    Either require the quiet window to exceed the slowest live material's
//    TickInterval, or assert on ActiveSlotCount reaching 0 instead, which has
//    no such blind spot because a viscosity-gated slot is still an allocated
//    slot."
//
// These are the first rest-based assertions in the suite on slow-viscosity
// materials, and they use BOTH recommended techniques so the two are pinned
// against each other: if they ever disagree about when a basin settled, one of
// them is wrong and the failure names which.
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

public class FluidSlowViscositySettleTests
{
    private static FluidReferenceCPU Basin()
    {
        var sim = new FluidReferenceCPU(4, 4, 4);   // 32^3
        sim.FillBox(new int3(0, 0, 0), new int3(31, 0, 31), Materials.Stone);
        for (int y = 1; y < 32; y++)
        {
            sim.FillBox(new int3(0, y, 0), new int3(31, y, 0), Materials.Stone);
            sim.FillBox(new int3(0, y, 31), new int3(31, y, 31), Materials.Stone);
            sim.FillBox(new int3(0, y, 0), new int3(0, y, 31), Materials.Stone);
            sim.FillBox(new int3(31, y, 0), new int3(31, y, 31), Materials.Stone);
        }
        sim.ResetLedgerToCurrentState();
        return sim;
    }

    /// Rest by the ACTIVE-SLOT definition: no allocated slots at all. Immune to
    /// the viscosity blind spot, because a gated slot is still allocated.
    private static int TickToNoSlots(FluidReferenceCPU sim, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            sim.Tick();
            var c = sim.CheckConservation();
            Assert.IsTrue(c.Ok, c.Message);
            if (sim.ActiveSlotCount == 0) return sim.TickCount;
        }
        return -1;
    }

    [Test]
    public void Lava_QuietWindowShorterThanItsInterval_WouldFalselyReportRest()
    {
        // Demonstrates the blind spot itself, so the reason for the rule is a
        // test rather than a paragraph. Lava's interval is 6; a 4-tick quiet
        // window is reached while the lava is still visibly falling.
        Assert.AreEqual(6u, MaterialRules.TickInterval(Materials.Lava));

        var sim = Basin();
        for (int i = 0; i < 6; i++) sim.EditVoxel(16, 24 + i, 16, Materials.Lava);

        int quiet = 0, falseRestTick = -1;
        for (int t = 0; t < 40 && falseRestTick < 0; t++)
        {
            sim.Tick();
            quiet = sim.ChangedCellsThisTick == 0 ? quiet + 1 : 0;
            if (quiet >= 4) falseRestTick = sim.TickCount;
        }

        Assert.Greater(falseRestTick, 0,
            "a 4-tick quiet window should be reachable while lava is mid-fall");
        Assert.Greater(sim.ActiveSlotCount, 0,
            "the blind spot requires slots to still be alive at the false rest — " +
            "if this fails the scenario no longer demonstrates it");
        int lowest = 32;
        for (int y = 1; y < 32; y++)
            if (sim.GetVoxel(16, y, 16) == Materials.Lava) { lowest = y; break; }
        Assert.Greater(lowest, 1,
            $"lava reported 'rest' at tick {falseRestTick} while still at y={lowest}, " +
            "not yet on the floor — this is the §8.1 blind spot, pinned deliberately");
    }

    [Test]
    public void Lava_SettlesToTheFloor_WhenRestIsMeasuredByActiveSlots()
    {
        var sim = Basin();
        const int drops = 6;
        for (int i = 0; i < drops; i++) sim.EditVoxel(16, 24 + i, 16, Materials.Lava);

        // 6 drops x ~23 cells of fall x interval 6 = well under 3000.
        int rest = TickToNoSlots(sim, 3000);
        Assert.Greater(rest, 0, "lava never reached zero active slots within 3000 ticks");

        Assert.AreEqual(drops, sim.CountMaterial(Materials.Lava), "lava must be conserved");
        Assert.AreEqual(0, sim.CountFloatingMobile(),
            "no lava may rest with Air beneath it");
        // NOT asserted on the same tick slots hit zero: the last slot can commit
        // a move and then be freed by the post-commit sweep within one tick, so
        // slots==0 and changed>0 legitimately coincide exactly once. Assert on
        // the tick AFTER instead -- with no slots left, nothing can move.
        sim.Tick();
        Assert.AreEqual(0, sim.ChangedCellsThisTick,
            "with zero active slots, the following tick must change nothing");
        Assert.AreEqual(0, sim.ActiveSlotCount, "no slot may reappear unprompted");
        TestContext.WriteLine($"[slowviscosity] lava: {drops} drops settled by tick {rest}");
    }

    [Test]
    public void Honey_SettlesToTheFloor_AtIntervalThirty()
    {
        // Honey has never been simulated to rest anywhere in the suite before
        // this. At interval 30 it is the harshest test of the viscosity gate not
        // ageing a slot toward sleep while it waits its turn.
        Assert.AreEqual(30u, MaterialRules.TickInterval(Materials.Honey));

        var sim = Basin();
        const int drops = 3;
        for (int i = 0; i < drops; i++) sim.EditVoxel(16, 20 + i, 16, Materials.Honey);

        int rest = TickToNoSlots(sim, 6000);
        Assert.Greater(rest, 0, "honey never reached zero active slots within 6000 ticks");
        Assert.AreEqual(drops, sim.CountMaterial(Materials.Honey), "honey must be conserved");
        Assert.AreEqual(0, sim.CountFloatingMobile(), "no honey may rest with Air beneath it");
        TestContext.WriteLine($"[slowviscosity] honey: {drops} drops settled by tick {rest}");
    }

    [Test]
    public void SlowAndFastFluids_Together_BothSettle()
    {
        // Mixed viscosities in one basin: the fast fluid reaching rest must not
        // let a rest check conclude the basin is done while the slow one is
        // still falling.
        var sim = Basin();
        for (int i = 0; i < 8; i++) sim.EditVoxel(10, 20 + i, 16, Materials.Water);
        for (int i = 0; i < 4; i++) sim.EditVoxel(22, 20 + i, 16, Materials.Lava);

        int rest = TickToNoSlots(sim, 4000);
        Assert.Greater(rest, 0, "mixed basin never reached zero active slots");
        Assert.AreEqual(8, sim.CountMaterial(Materials.Water));
        Assert.AreEqual(4, sim.CountMaterial(Materials.Lava));
        Assert.AreEqual(0, sim.CountFloatingMobile(), "nothing may be left floating");
        TestContext.WriteLine($"[slowviscosity] mixed water+lava settled by tick {rest}");
    }
}
