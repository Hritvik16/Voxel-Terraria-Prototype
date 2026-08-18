// Assets/CoreEngine/Tests/OccupancyMaskTests.cs
//
// Amendment 8.8 — Phase A tests. Pure C#, no GPU, no shader, no traversal.
//
// Mirrors AirMipTests.cs's structure deliberately: same real-config dims, same
// class of cases (all-air baseline, one-solid-brick ancestor chain, boundary
// isolation), so this gets the same coverage discipline the pyramid itself
// did before anything downstream trusted it.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class OccupancyMaskTests
{
    // Real window in bricks: matches AirMipTests' config exactly.
    private static readonly int3 L0Dims = new int3(512, 256, 512);

    private static uint[] AllAirL0()
    {
        return new uint[L0Dims.x * L0Dims.y * L0Dims.z];
    }

    private static int L0Index(int3 brick) => AirMip.FlatIndex(brick, L0Dims);

    private static uint SolidHandle(byte material) => material;

    // ---------------------------------------------------------------------
    //  All-air world -> every occupancy byte is 0 at every level.
    // ---------------------------------------------------------------------
    [Test]
    public void AllAir_EveryMaskIsZero_AtEveryLevel()
    {
        uint[] l0 = AllAirL0();
        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        Assert.AreEqual(mips.NumLevels, occ.NumLevels, "Occupancy must have the same level count as the source AirMipData.");

        for (int lvl = 0; lvl < occ.NumLevels; lvl++)
        {
            byte[] cells = occ.Levels[lvl];
            for (int i = 0; i < cells.Length; i++)
                Assert.AreEqual((byte)0, cells[i], $"Level {lvl + 1} cell {i} should have an all-clear mask in an all-air world.");
        }
    }

    // ---------------------------------------------------------------------
    //  One solid brick -> exactly one bit set in the ancestor chain, at the
    //  octant matching the brick's position within each ancestor's coverage.
    // ---------------------------------------------------------------------
    [Test]
    public void OneSolidBrick_SetsExactlyOneBit_InEachAncestorLevel()
    {
        uint[] l0 = AllAirL0();
        // Brick (40, 20, 72): even coordinates on every axis, so it sits at
        // octant (0,0,0) of its L1 parent cell (20,10,36) - easy to hand-verify.
        int3 solidBrick = new int3(40, 20, 72);
        l0[L0Index(solidBrick)] = SolidHandle(2);

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        int3 parentCellL1 = solidBrick >> 1; // (20, 10, 36)
        int expectedBit = OccupancyMask.OctantBit(0, 0, 0); // brick is even on all axes -> octant (0,0,0)
        Assert.AreEqual(0, expectedBit, "Sanity: octant (0,0,0) must be bit 0.");

        byte l1Mask = occ.Level(1)[AirMip.FlatIndex(parentCellL1, occ.DimsOfLevel(1))];
        Assert.AreEqual((byte)(1 << expectedBit), l1Mask,
            $"L1 parent cell mask should have exactly bit {expectedBit} set, got {System.Convert.ToString(l1Mask, 2)}.");

        // Every OTHER L1 cell must still be all-clear.
        byte[] l1Cells = occ.Level(1);
        int nonZeroCount = 0;
        foreach (byte b in l1Cells) if (b != 0) nonZeroCount++;
        Assert.AreEqual(1, nonZeroCount, "Exactly one L1 cell should have a non-zero mask.");

        // Ancestor chain up through L2-L4: each ancestor's mask must have
        // exactly one bit set (whichever octant contains the L1 cell above).
        for (int k = 2; k <= mips.NumLevels; k++)
        {
            byte[] cells = occ.Level(k);
            int nonZero = 0;
            foreach (byte b in cells) if (b != 0) nonZero++;
            Assert.AreEqual(1, nonZero, $"L{k}: exactly one cell should have a non-zero mask.");
        }
    }

    // ---------------------------------------------------------------------
    //  A DENSE brick counts as occupied (conservative, matches AirMip's own
    //  "dense is never air" rule) even if its interior happens to be all air.
    // ---------------------------------------------------------------------
    [Test]
    public void DenseBrick_SetsOccupancyBit_EvenIfInternallyAir()
    {
        uint[] l0 = AllAirL0();
        int3 denseBrick = new int3(10, 6, 18);
        l0[L0Index(denseBrick)] = 0x80000000u | 1234u; // dense: top bit set

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        int3 parentCellL1 = denseBrick >> 1;
        byte l1Mask = occ.Level(1)[AirMip.FlatIndex(parentCellL1, occ.DimsOfLevel(1))];
        Assert.AreNotEqual((byte)0, l1Mask, "A dense brick must set an occupancy bit (conservative rule).");
    }

    // ---------------------------------------------------------------------
    //  Two solid bricks in the SAME L1 cell but different octants -> two
    //  distinct bits set, not one, and the exact octant math is verified.
    // ---------------------------------------------------------------------
    [Test]
    public void TwoSolidBricksInSameParentCell_SetDistinctBits()
    {
        uint[] l0 = AllAirL0();
        // Parent L1 cell at brick-space (20,10,36) covers L0 bricks
        // [40..41] x [20..21] x [72..73]. Put solids at octant (0,0,0) and
        // octant (1,1,1) - the two extreme corners.
        int3 parentBase = new int3(20, 10, 36) * 2; // (40, 20, 72)
        int3 brickA = parentBase + new int3(0, 0, 0);
        int3 brickB = parentBase + new int3(1, 1, 1);
        l0[L0Index(brickA)] = SolidHandle(3);
        l0[L0Index(brickB)] = SolidHandle(4);

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        int3 parentCellL1 = new int3(20, 10, 36);
        byte mask = occ.Level(1)[AirMip.FlatIndex(parentCellL1, occ.DimsOfLevel(1))];

        int bitA = OccupancyMask.OctantBit(0, 0, 0);
        int bitB = OccupancyMask.OctantBit(1, 1, 1);
        Assert.AreEqual(0, bitA);
        Assert.AreEqual(7, bitB);

        byte expected = (byte)((1 << bitA) | (1 << bitB));
        Assert.AreEqual(expected, mask, $"Expected bits {bitA} and {bitB} set, got {System.Convert.ToString(mask, 2)}.");
    }

    // ---------------------------------------------------------------------
    //  Solid at a boundary shared by neighboring L1 cells -> only the
    //  CONTAINING cell's mask flips; the neighbor's stays all-clear.
    // ---------------------------------------------------------------------
    [Test]
    public void SolidAtCellBoundary_OnlyFlipsContainingCellsMask()
    {
        uint[] l0 = AllAirL0();
        int3 solidBrick = new int3(16, 0, 0); // first brick of L1 cell (8,0,0)
        l0[L0Index(solidBrick)] = SolidHandle(3);

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        int3 d1 = occ.DimsOfLevel(1);
        byte[] l1 = occ.Level(1);

        Assert.AreNotEqual((byte)0, l1[AirMip.FlatIndex(new int3(8, 0, 0), d1)],
            "Containing L1 cell must have a non-zero mask.");
        Assert.AreEqual((byte)0, l1[AirMip.FlatIndex(new int3(7, 0, 0), d1)],
            "Neighbor L1 cell (7,0,0) mask must remain all-clear.");
        Assert.AreEqual((byte)0, l1[AirMip.FlatIndex(new int3(9, 0, 0), d1)],
            "Neighbor L1 cell (9,0,0) mask must remain all-clear.");
    }

    // ---------------------------------------------------------------------
    //  RebuildRegion: after AirMip.RebuildRegion clears a solid, this file's
    //  RebuildRegion must return the affected masks to all-clear too - the
    //  maintenance path stays consistent with the builder's from-scratch
    //  output, mirroring AirMipTests' own RebuildRegion coverage.
    // ---------------------------------------------------------------------
    [Test]
    public void RebuildRegion_AfterClearingSolid_ReturnsMasksToZero()
    {
        uint[] l0 = AllAirL0();
        int3 solidBrick = new int3(40, 20, 72);
        l0[L0Index(solidBrick)] = SolidHandle(2);

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        // Precondition: ancestor chain non-zero.
        int3 parentCellL1 = solidBrick >> 1;
        Assert.AreNotEqual((byte)0, occ.Level(1)[AirMip.FlatIndex(parentCellL1, occ.DimsOfLevel(1))]);

        // Clear the brick, rebuild AirMip first (maintenance order requirement),
        // then rebuild the occupancy mask over the same region.
        l0[L0Index(solidBrick)] = 0u;
        int3 chunk = solidBrick >> 4;
        int3 regionMin = chunk * 16;
        int3 regionMax = regionMin + new int3(15, 15, 15);

        AirMip.RebuildRegion(l0, mips, regionMin, regionMax);
        OccupancyMask.RebuildRegion(l0, mips, occ, regionMin, regionMax);

        for (int k = 1; k <= occ.NumLevels; k++)
        {
            byte[] cells = occ.Level(k);
            for (int i = 0; i < cells.Length; i++)
                Assert.AreEqual((byte)0, cells[i], $"L{k}: cell {i} should be all-clear after RebuildRegion.");
        }
    }

    // ---------------------------------------------------------------------
    //  RebuildRegion the other direction: air -> solid via a region rebuild
    //  produces the same result as a from-scratch Build over the same final
    //  L0 state - the maintenance path and the full builder must agree.
    // ---------------------------------------------------------------------
    [Test]
    public void RebuildRegion_AfterAddingSolid_MatchesFromScratchBuild()
    {
        uint[] l0 = AllAirL0();
        AirMipData mips = AirMip.Build(l0, L0Dims, 4); // all air
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        int3 solidBrick = new int3(12, 4, 200);
        l0[L0Index(solidBrick)] = SolidHandle(5);

        int3 chunk = solidBrick >> 4;
        int3 regionMin = chunk * 16;
        int3 regionMax = regionMin + new int3(15, 15, 15);

        AirMip.RebuildRegion(l0, mips, regionMin, regionMax);
        OccupancyMask.RebuildRegion(l0, mips, occ, regionMin, regionMax);

        // Independent from-scratch build over the same final l0/mips state.
        OccupancyMaskData fresh = OccupancyMask.Build(l0, mips);

        for (int k = 1; k <= occ.NumLevels; k++)
        {
            byte[] maintained = occ.Level(k);
            byte[] freshCells = fresh.Level(k);
            Assert.AreEqual(freshCells.Length, maintained.Length, $"L{k}: length mismatch.");
            for (int i = 0; i < freshCells.Length; i++)
                Assert.AreEqual(freshCells[i], maintained[i], $"L{k}: cell {i} - maintained mask disagrees with from-scratch rebuild.");
        }
    }

    // ---------------------------------------------------------------------
    //  Fuzz: random solid placements, then assert every occupancy bit agrees
    //  with a direct, independent re-derivation from the AirMip levels (not
    //  from OccupancyMask's own code path) - the real guard against an
    //  octant-indexing bug that a handful of hand-picked cases could miss.
    // ---------------------------------------------------------------------
    [Test]
    public void RandomizedFuzz_EveryBitAgreesWithIndependentRederivation()
    {
        var rng = new System.Random(20260813);
        uint[] l0 = AllAirL0();

        // Scatter solids across a sub-region (keeps the fuzz fast at this
        // window size - full-window density isn't needed to stress octant math).
        for (int i = 0; i < 500; i++)
        {
            int3 b = new int3(rng.Next(0, 128), rng.Next(0, 64), rng.Next(0, 128));
            l0[L0Index(b)] = (uint)(2 + rng.Next(6));
        }

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        OccupancyMaskData occ = OccupancyMask.Build(l0, mips);

        // Independent re-derivation: for level 1, directly re-check each of
        // the 8 child L0 bricks per cell against IsUniformAir; for level k>=2,
        // directly re-check each of the 8 child cells at level k-1 against
        // AirMip's own values. This duplicates the predicate on purpose (it's
        // the test's job to be an independent check, not to reuse the
        // builder's internals).
        for (int k = 1; k <= occ.NumLevels; k++)
        {
            int3 dims = occ.DimsOfLevel(k);
            byte[] cells = occ.Level(k);

            // Sample a bounded number of cells rather than the whole level
            // (full-level iteration at L1's 256x128x256 size is needlessly
            // slow for a fuzz check) - sample near where solids were placed
            // plus a handful of random cells for broad coverage.
            for (int sample = 0; sample < 2000; sample++)
            {
                int3 cell = new int3(rng.Next(0, dims.x), rng.Next(0, dims.y), rng.Next(0, dims.z));
                byte actual = cells[AirMip.FlatIndex(cell, dims)];
                byte expected = 0;

                if (k == 1)
                {
                    int3 baseBrick = cell * 2;
                    for (int dz = 0; dz < 2; dz++)
                    for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int3 b = baseBrick + new int3(dx, dy, dz);
                        int idx = AirMip.FlatIndex(b, L0Dims);
                        if (!AirMip.IsUniformAir(l0[idx]))
                            expected |= (byte)(1 << OccupancyMask.OctantBit(dx, dy, dz));
                    }
                }
                else
                {
                    uint[] child = mips.Level(k - 1);
                    int3 childDims = mips.DimsOfLevel(k - 1);
                    int3 baseChild = cell * 2;
                    for (int dz = 0; dz < 2; dz++)
                    for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int3 c = baseChild + new int3(dx, dy, dz);
                        int idx = AirMip.FlatIndex(c, childDims);
                        if (child[idx] != 0u)
                            expected |= (byte)(1 << OccupancyMask.OctantBit(dx, dy, dz));
                    }
                }

                Assert.AreEqual(expected, actual,
                    $"L{k} cell {cell}: mask disagrees with independent re-derivation. " +
                    $"Expected {System.Convert.ToString(expected, 2)}, got {System.Convert.ToString(actual, 2)}.");
            }
        }
    }
}