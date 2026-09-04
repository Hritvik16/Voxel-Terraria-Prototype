// Assets/CoreEngine/Tests/FluidTieBreakVarianceTests.cs
//
// Answers ONE question, and it is the Phase 5b gating question:
//
//   Is the CPU-vs-GPU difference in FINAL FLUID DISTRIBUTION a real translation
//   bug, or is it §7.8's sanctioned tie-break variance?
//
// The Phase 5b rig compares the two implementations' per-layer occupancy and
// requires an exact match. That assertion is only meaningful if exact layout is
// a PROPERTY OF THE RULES. If merely reordering equally-legal choices moves the
// oracle's own answer, then exact layout was never a property of the rules --
// only of one arbitrary ordering -- and demanding the GPU reproduce it is
// demanding something §7.8 explicitly says not to.
//
// This asks the oracle directly, on the CPU, where everything is deterministic
// and no GPU is involved. FluidReferenceCPU.TieBreakSalt changes only the ORDER
// in which equally-legal destinations are tried; it never changes which
// destinations are legal, so conservation must hold identically across salts.
using System.Text;
using UnityEngine;
using NUnit.Framework;
using Unity.Mathematics;

public class FluidTieBreakVarianceTests
{
    private static FluidReferenceCPU Basin(int salt, out int3 innerMin)
    {
        var sim = new FluidReferenceCPU(4, 4, 4);   // 32^3
        sim.TieBreakSalt = salt;
        int lo = 12, hi = 19;
        sim.FillBox(new int3(lo - 1, 0, lo - 1), new int3(hi + 1, 0, hi + 1), Materials.Stone);
        for (int y = 1; y < 32; y++)
        {
            sim.FillBox(new int3(lo - 1, y, lo - 1), new int3(hi + 1, y, lo - 1), Materials.Stone);
            sim.FillBox(new int3(lo - 1, y, hi + 1), new int3(hi + 1, y, hi + 1), Materials.Stone);
            sim.FillBox(new int3(lo - 1, y, lo - 1), new int3(lo - 1, y, hi + 1), Materials.Stone);
            sim.FillBox(new int3(hi + 1, y, lo - 1), new int3(hi + 1, y, hi + 1), Materials.Stone);
        }
        innerMin = new int3(lo, 1, lo);
        return sim;
    }

    private static int[] RunAndProfile(int salt, out int total)
    {
        var sim = Basin(salt, out int3 innerMin);
        // 40 drops into an 8x8 basin: MORE than one layer, so the surface is
        // partly filled and drops genuinely have equally-legal lateral choices.
        for (int i = 0; i < 40; i++)
            sim.EditVoxel(innerMin.x + 1, 2 + i, innerMin.z + 1, Materials.Water);

        int quiet = 0;
        for (int t = 0; t < 1200; t++)
        {
            sim.Tick();
            var c = sim.CheckConservation();
            Assert.IsTrue(c.Ok, c.Message);
            quiet = sim.ChangedCellsThisTick == 0 ? quiet + 1 : 0;
            if (quiet >= 12) break;
        }

        var profile = new int[sim.SizeYVoxels];
        for (int y = 0; y < sim.SizeYVoxels; y++)
        for (int z = 0; z < sim.SizeZVoxels; z++)
        for (int x = 0; x < sim.SizeXVoxels; x++)
            if (sim.GetVoxel(x, y, z) == Materials.Water) profile[y]++;
        total = sim.CountMaterial(Materials.Water);
        return profile;
    }

    [Test]
    public void SameSalt_IsBitDeterministic()
    {
        // §7.8's actual guarantee: the CPU reference is deterministic given a
        // fixed tick order. Permuting the salt must not break THAT.
        int[] a = RunAndProfile(0, out int ta);
        int[] b = RunAndProfile(0, out int tb);
        Assert.AreEqual(ta, tb);
        CollectionAssert.AreEqual(a, b, "the oracle is not deterministic at a fixed salt");
    }

    [Test]
    public void PermutingTieBreakOrder_ConservesExactly()
    {
        // Reordering equally-legal choices cannot change how much water exists.
        // If this ever fails, the salt is changing which destinations are LEGAL,
        // and the experiment below would be meaningless.
        int baseline = -1;
        for (int salt = 0; salt < 6; salt++)
        {
            RunAndProfile(salt, out int total);
            if (baseline < 0) baseline = total;
            Assert.AreEqual(baseline, total,
                $"salt {salt} changed the conserved count — the salt is altering legality, not just order");
        }
    }

    [Test]
    public void PermutingTieBreakOrder_ChangesFinalDistribution()
    {
        // THE EXPERIMENT. If this passes, exact per-layer layout is NOT a
        // property of the rules, and requiring the GPU to reproduce the CPU's
        // exact layout is requiring something §7.8 says not to require.
        //
        // If it FAILED -- i.e. every salt produced an identical distribution --
        // that would mean layout IS determined by the rules alone, and the
        // CPU/GPU gap would be a genuine translation bug.
        int[] reference = RunAndProfile(0, out _);
        var report = new StringBuilder();
        int differing = 0, worstDelta = 0;

        for (int salt = 1; salt < 6; salt++)
        {
            int[] p = RunAndProfile(salt, out _);
            int delta = 0;
            for (int y = 0; y < p.Length; y++) delta = Mathf.Max(delta, Mathf.Abs(p[y] - reference[y]));
            if (delta > 0) differing++;
            worstDelta = Mathf.Max(worstDelta, delta);
            report.Append($"salt {salt}: worst per-layer delta vs salt 0 = {delta}   ");
        }

        TestContext.WriteLine($"[tiebreak] {report}");
        TestContext.WriteLine($"[tiebreak] {differing} of 5 permutations produced a DIFFERENT " +
                              $"final distribution; worst per-layer delta {worstDelta}");

        Assert.Greater(differing, 0,
            "every tie-break permutation produced an identical layout — that would mean exact " +
            "layout IS determined by the rules, and the CPU/GPU distribution gap is a real bug");
    }

    // ---- The control at the RIG's actual scale -------------------------------
    // The 8x8 basin above understates the effect: tie-break variance scales with
    // how many equally-legal choices exist, and a 62x62 floor has vastly more
    // than an 8x8 one. Phase5bBasin's geometry is reproduced exactly here so the
    // number is comparable to the rig's reported delta rather than merely
    // suggestive.
    private static FluidReferenceCPU RigShapedBasin(int salt)
    {
        var sim = new FluidReferenceCPU(8, 4, 8);   // 64 x 32 x 64, as Phase5bBasin
        sim.TieBreakSalt = salt;
        const int SX = 64, SY = 32, SZ = 64;
        sim.FillBox(new int3(0, 0, 0), new int3(SX - 1, 0, SZ - 1), Materials.Stone);
        for (int y = 1; y < SY; y++)
        {
            sim.FillBox(new int3(0, y, 0), new int3(SX - 1, y, 0), Materials.Stone);
            sim.FillBox(new int3(0, y, SZ - 1), new int3(SX - 1, y, SZ - 1), Materials.Stone);
            sim.FillBox(new int3(0, y, 0), new int3(0, y, SZ - 1), Materials.Stone);
            sim.FillBox(new int3(SX - 1, y, 0), new int3(SX - 1, y, SZ - 1), Materials.Stone);
        }
        sim.PlayerVoxel = new int3(SX / 2, SY / 2, SZ / 2);
        sim.ResetLedgerToCurrentState();
        return sim;
    }

    private static int[] RunRigShaped(int salt, out int total, out int ticks)
    {
        var sim = RigShapedBasin(salt);
        int poured = 0;
        int quiet = 0;
        int t = 0;
        for (; t < 4000; t++)
        {
            if (poured < 150 && sim.GetVoxel(21, 30, 32) == Materials.Air)
            { sim.EditVoxel(21, 30, 32, Materials.Water); poured++; }
            sim.Tick();
            var c = sim.CheckConservation();
            Assert.IsTrue(c.Ok, c.Message);
            quiet = sim.ChangedCellsThisTick == 0 ? quiet + 1 : 0;
            if (poured >= 150 && quiet >= 12) break;
        }
        ticks = t;
        var profile = new int[sim.SizeYVoxels];
        for (int y = 0; y < sim.SizeYVoxels; y++)
        for (int z = 0; z < sim.SizeZVoxels; z++)
        for (int x = 0; x < sim.SizeXVoxels; x++)
            if (sim.GetVoxel(x, y, z) == Materials.Water) profile[y]++;
        total = sim.CountMaterial(Materials.Water);
        return profile;
    }

    [Test]
    public void AtRigScale_TieBreakOrderMovesTheDistributionAsMuchAsTheGpuDoes()
    {
        // THE DECISIVE CONTROL. If reordering the ORACLE'S OWN tie-breaks moves
        // its final layer occupancy by about as much as the GPU differs from it,
        // then the GPU is not doing anything the rules forbid -- it is landing
        // on one of many equally-valid rest states, which is exactly what §7.8
        // says to expect and exactly what it says not to test for.
        int[] reference = RunRigShaped(0, out int baseTotal, out _);
        int worst = 0;
        var report = new StringBuilder();

        for (int salt = 1; salt <= 5; salt++)
        {
            int[] p = RunRigShaped(salt, out int total, out _);
            Assert.AreEqual(baseTotal, total, $"salt {salt} changed the conserved count");
            int delta = 0;
            for (int y = 0; y < p.Length; y++) delta = Mathf.Max(delta, Mathf.Abs(p[y] - reference[y]));
            worst = Mathf.Max(worst, delta);
            report.Append($"salt {salt}: delta {delta}   ");
        }

        TestContext.WriteLine($"[rigscale] conserved {baseTotal} water at every salt");
        TestContext.WriteLine($"[rigscale] {report}");
        TestContext.WriteLine($"[rigscale] WORST per-layer delta from tie-break reordering alone: {worst}");
        Assert.AreEqual(baseTotal, 150, "the pour should have delivered 150 drops");
    }
}
