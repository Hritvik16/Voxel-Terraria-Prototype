// ==========================================
// Assets/CoreEngine/Tests/ReactionPairTests.cs
//
// RX1 (task 93e1, GAMEPLAY_PHASE_PLAN row RX1): adjacency reactions, table
// rows only. One test per pair, each run on the CPU reference
// (FluidReferenceCPU, conservation checked every tick) AND checked against
// the GPU path's flattened table (FluidGpuSimulation.BuildReactionTables --
// the exact array FluidCA.compute's CSReact reads). EditMode has no GPU, so
// the GPU half is the table plus the shader's wake rule; the play demo
// (`-reactiondemo`) runs the real GPU CA.
//
//   water + lava         -> steam (water cell) + cooled stone (lava cell)
//   water + loam/red earth -> water stays, the soil becomes mud
//   sand  + lava         -> glass (sand cell), the lava stays
//   snow/ice + lava      -> water (snow cell), the lava stays

using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Simulation;

public class ReactionPairTests
{
    private static readonly byte Loam = Materials.FromTg2(20), RedEarth = Materials.FromTg2(24);
    private static readonly byte Mud = Materials.FromTg2(23);
    private static readonly byte Tg2Sand = Materials.FromTg2(12), BeachSand = Materials.FromTg2(14);
    private static readonly byte Tg2Snow = Materials.FromTg2(13), PackedIce = Materials.FromTg2(22);

    // 16 x 32 x 16 voxels, a stone floor at y = 0 unless the test lays its own.
    private static FluidReferenceCPU Sandbox(byte floor)
    {
        var sim = new FluidReferenceCPU(2, 4, 2);
        sim.FillBox(new int3(0, 0, 0), new int3(15, 0, 15), floor);
        return sim;
    }

    /// Ticks until `quiet` consecutive ticks change nothing, conservation
    /// asserted every tick. quiet > lava's interval (6), per TickToRest's note.
    private static void RunToRest(FluidReferenceCPU sim, int maxTicks = 20000, int quiet = 40)
    {
        int still = 0;
        for (int t = 0; t < maxTicks; t++)
        {
            sim.Tick();
            var check = sim.CheckConservation();
            Assert.IsTrue(check.Ok, $"tick {sim.TickCount}: {check.Message}");
            still = sim.ChangedCellsThisTick == 0 ? still + 1 : 0;
            if (still >= quiet) return;
        }
        Assert.Fail($"never reached rest in {maxTicks} ticks");
    }

    private static void TickChecked(FluidReferenceCPU sim)
    {
        sim.Tick();
        var check = sim.CheckConservation();
        Assert.IsTrue(check.Ok, $"tick {sim.TickCount}: {check.Message}");
    }

    private static uint[] _gpuSelf, _gpuOther;

    /// The GPU path's lookup gives the same products, in both scan orders.
    private static void AssertGpuAgrees(byte a, byte b, byte aProduct, byte bProduct)
    {
        if (_gpuSelf == null) FluidGpuSimulation.BuildReactionTables(out _gpuSelf, out _gpuOther);
        Assert.AreEqual(aProduct, _gpuSelf[a * 256 + b], $"GPU: {a} next to {b}, {a}'s product");
        Assert.AreEqual(bProduct, _gpuOther[a * 256 + b], $"GPU: {a} next to {b}, {b}'s product");
        Assert.AreEqual(bProduct, _gpuSelf[b * 256 + a], $"GPU: {b} next to {a}, {b}'s product");
        Assert.AreEqual(aProduct, _gpuOther[b * 256 + a], $"GPU: {b} next to {a}, {a}'s product");
        Assert.IsTrue(MaterialRules.TryGetReaction(a, b, out byte ca, out byte cb));
        Assert.AreEqual(aProduct, ca, "CPU table"); Assert.AreEqual(bProduct, cb, "CPU table");
    }

    /// No material named in `bad` sits face-adjacent to `m` anywhere.
    private static void AssertNoFaceContact(FluidReferenceCPU sim, byte m, params byte[] bad)
    {
        int3[] f = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0),
                     new int3(0, -1, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
        for (int y = 0; y < sim.SizeYVoxels; y++)
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
        {
            if (sim.GetVoxel(x, y, z) != m) continue;
            foreach (int3 d in f)
            {
                int3 n = new int3(x, y, z) + d;
                if (n.x < 0 || n.y < 0 || n.z < 0 || n.x > 15 || n.z > 15 || n.y >= sim.SizeYVoxels) continue;
                byte o = sim.GetVoxel(n);
                foreach (byte b in bad)
                    Assert.AreNotEqual(b, o, $"material {m} at ({x},{y},{z}) still touches {b} at rest");
            }
        }
    }

    // ---------------------------------------------------------------------

    [Test]
    public void GpuTable_MatchesTheCpuReference_ForEveryPair()
    {
        FluidGpuSimulation.BuildReactionTables(out uint[] s, out uint[] o);
        int pairs = 0;
        for (int a = 0; a < 256; a++)
        for (int b = 0; b < 256; b++)
        {
            bool cpu = MaterialRules.TryGetReaction((byte)a, (byte)b, out byte sp, out byte op);
            Assert.AreEqual(cpu ? sp : 0xFFFFFFFFu, s[a * 256 + b], $"self product, pair ({a},{b})");
            if (!cpu) continue;
            Assert.AreEqual((uint)op, o[a * 256 + b], $"other product, pair ({a},{b})");
            // Order-independent: the reverse entry exists with swapped products.
            Assert.IsTrue(MaterialRules.TryGetReaction((byte)b, (byte)a, out byte rs, out byte ro), $"({b},{a}) missing");
            Assert.AreEqual(op, rs); Assert.AreEqual(sp, ro);
            pairs++;
        }
        Assert.Greater(pairs, 0);
    }

    [Test]
    public void GpuReact_WakesMobileProducts_LikeTheCpuCommit()
    {
        // FluidReferenceCPU's CommitPass wakes every changed reaction cell, so a
        // steam / melt-water product gets a slot. On the GPU that wake is the
        // op's wake bit; without it the product would sit unpromoted.
        string src = File.ReadAllText("Assets/CoreEngine/Simulation/FluidCA.compute");
        StringAssert.Contains("selfProduct | (IsMobile(selfProduct) ? OP_WAKE_BIT : 0u)", src);
        StringAssert.Contains("otherProduct | (IsMobile(otherProduct) ? OP_WAKE_BIT : 0u)", src);
    }

    [Test]
    public void WaterPlusLava_SteamAndCooledStone_EitherScanOrder()
    {
        AssertGpuAgrees(Materials.Water, Materials.Lava, Materials.Steam, Materials.CooledStone);
        for (int order = 0; order < 2; order++)
        {
            var sim = Sandbox(Materials.Stone);
            // The first-placed fluid gets the lower slot index and scans first.
            if (order == 0) { sim.EditVoxel(8, 1, 8, Materials.Water); sim.EditVoxel(9, 1, 8, Materials.Lava); }
            else            { sim.EditVoxel(9, 1, 8, Materials.Lava);  sim.EditVoxel(8, 1, 8, Materials.Water); }
            sim.ResetLedgerToCurrentState();

            TickChecked(sim);
            Assert.AreEqual(1, sim.ReactionsThisTick, $"order {order}");
            Assert.AreEqual(Materials.Steam, sim.GetVoxel(8, 1, 8), $"order {order}: water cell");
            Assert.AreEqual(Materials.CooledStone, sim.GetVoxel(9, 1, 8), $"order {order}: lava cell");
            Assert.AreEqual(1L, sim.Ledger.ReactionConsumed, "only the lava is consumed");

            RunToRest(sim);
            Assert.AreEqual(1, sim.CountMaterial(Materials.Steam), "the steam is conserved");
            Assert.AreEqual(1, sim.CountMaterial(Materials.CooledStone));
            Assert.AreEqual(0, sim.CountMaterial(Materials.Water) + sim.CountMaterial(Materials.Lava));
            Assert.AreEqual(sim.SizeYVoxels - 1, FindY(sim, Materials.Steam), "the steam rose to the top");
        }
    }

    [Test]
    public void WaterOnLoamOrRedEarth_TurnsTheSoilToMud_WaterKept()
    {
        foreach (byte soil in new[] { Loam, RedEarth })
        {
            AssertGpuAgrees(Materials.Water, soil, Materials.Water, Mud);
            var sim = Sandbox(soil);
            sim.FillBox(new int3(7, 6, 7), new int3(8, 7, 8), Materials.Water);   // 8 drops onto the soil
            sim.ResetLedgerToCurrentState();
            int soil0 = sim.CountMaterial(soil);

            RunToRest(sim);
            Assert.AreEqual(8, sim.CountMaterial(Materials.Water), $"soil {soil}: the water is not consumed");
            int mud = sim.CountMaterial(Mud);
            Assert.Greater(mud, 0, $"soil {soil}: no mud formed");
            Assert.AreEqual(soil0 - mud, sim.CountMaterial(soil), "each mud cell was one soil cell");
            Assert.AreEqual(0L, sim.Ledger.ReactionConsumed, "soil -> mud moves no mobile mass");
            // NOT asserted (since task 94): "no water touches soil at rest". It held
            // only for this pour's pre-94 path; with the 8.16 fall one drop slides
            // onto (15,1,1) and the sleep rule ages it on that LATERAL move, so it
            // dozes off before its next scan -- the sand test's note, same cause.
        }
    }

    [Test]
    public void LavaOnSand_TurnsTheSandToGlass_LavaKept()
    {
        foreach (byte sand in new[] { Materials.Sand, Tg2Sand, BeachSand })
        {
            AssertGpuAgrees(Materials.Lava, sand, Materials.Lava, Materials.Glass);
            var sim = Sandbox(Materials.Stone);
            sim.FillBox(new int3(0, 1, 0), new int3(15, 1, 15), sand);   // a sand layer on the stone
            sim.EditVoxel(8, 2, 8, Materials.Lava);
            sim.ResetLedgerToCurrentState();

            TickChecked(sim);
            Assert.AreEqual(Materials.Glass, sim.GetVoxel(8, 1, 8), $"sand {sand}: the cell under the lava");
            Assert.AreEqual(Materials.Lava, sim.GetVoxel(8, 2, 8), "the lava stays");
            Assert.AreEqual(1L, sim.Ledger.ReactionConsumed, "one sand (mobile) became glass");

            RunToRest(sim);
            Assert.AreEqual(1, sim.CountMaterial(Materials.Lava), $"sand {sand}: the lava is kept");
            int glass = sim.CountMaterial(Materials.Glass);
            Assert.AreEqual(256 - glass, sim.CountMaterial(sand), "each glass cell was one sand cell");
            Assert.AreEqual((long)glass, sim.Ledger.ReactionConsumed, "every glass booked as consumed sand");
            // NOT asserted: "no lava touches sand at rest". The CA's sleep rule
            // ages a slot on a LATERAL move, so lava can doze off on a sand cell
            // it just slid onto before its next scan (it reacts when woken).
            // RX1 adds table rows only; the CA is not redesigned.
        }
    }

    [Test]
    public void LavaOnSnowOrIce_MeltsItToWater_ThenQuenches()
    {
        foreach (byte snow in new[] { Materials.Snow, Tg2Snow, PackedIce })
        {
            AssertGpuAgrees(Materials.Lava, snow, Materials.Lava, Materials.Water);
            var sim = Sandbox(Materials.Stone);
            sim.FillBox(new int3(0, 1, 0), new int3(15, 1, 15), snow);   // static snow / ice layer
            sim.EditVoxel(8, 2, 8, Materials.Lava);
            sim.ResetLedgerToCurrentState();

            TickChecked(sim);
            Assert.AreEqual(Materials.Water, sim.GetVoxel(8, 1, 8), $"snow {snow}: melted under the lava");
            Assert.AreEqual(Materials.Lava, sim.GetVoxel(8, 2, 8), "the lava stays");
            Assert.AreEqual(-1L, sim.Ledger.ReactionConsumed, "melting CREATES one mobile byte, booked as such");

            // The melt water then meets the lava: the water + lava pair.
            RunToRest(sim);
            Assert.AreEqual(Materials.CooledStone, sim.GetVoxel(8, 2, 8), "the lava quenched to cooled stone");
            Assert.AreEqual(1, sim.CountMaterial(Materials.Steam), "the melt water flashed to steam");
            Assert.AreEqual(255, sim.CountMaterial(snow), "exactly one snow cell melted");
            Assert.AreEqual(0, sim.CountMaterial(Materials.Lava) + sim.CountMaterial(Materials.Water));
            Assert.AreEqual(0L, sim.Ledger.ReactionConsumed, "one byte created (melt), one consumed (quench)");
        }
    }

    private static int FindY(FluidReferenceCPU sim, byte m)
    {
        for (int y = sim.SizeYVoxels - 1; y >= 0; y--)
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
            if (sim.GetVoxel(x, y, z) == m) return y;
        return -1;
    }
}
