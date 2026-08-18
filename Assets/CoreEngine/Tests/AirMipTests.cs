// Assets/CoreEngine/Tests/AirMipTests.cs
//
// Amendment 8.7 — Step 1 tests. Pure C#, no GPU, no shader.
//
// These pin the pyramid builder's correctness the way MemoryModelTests pins the
// memory model: exact expected cell values, asserted directly. The whole point
// of building the pyramid on the CPU first is that every one of these is
// reproducible in a debugger with a plain number, no raymarcher involved.
//
// Uses the REAL config dims (WINDOW_BRICKS = 512 x 256 x 512) so the level-dim
// derivation and the "no clamp at this config" fact are pinned against the
// numbers you actually ship, not a toy size.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class AirMipTests
{
    // Real window in bricks: 32*16 = 512 (X,Z), 16*16 = 256 (Y).
    private static readonly int3 L0Dims = new int3(512, 256, 512);

    private static uint[] AllAirL0()
    {
        // Zero == uniform-air by the shared predicate, and zero-init is exactly
        // how the clipmap starts. So an all-zero array is an all-air world.
        return new uint[L0Dims.x * L0Dims.y * L0Dims.z];
    }

    private static int L0Index(int3 brick)
    {
        return AirMip.FlatIndex(brick, L0Dims);
    }

    // A dense handle (top bit set). Value of the low bits is irrelevant to the
    // air predicate; dense is always "not air" (conservative, per the plan).
    private static uint DenseHandle(uint poolIdx) => 0x80000000u | poolIdx;

    // A uniform-solid handle: top bit clear, material byte nonzero.
    private static uint SolidHandle(byte material) => material;

    // ---------------------------------------------------------------------
    //  Level-dim derivation + clamp behavior at the REAL config.
    // ---------------------------------------------------------------------

    [Test]
    public void LevelDims_AtRealConfig_AreExactAndUnclamped()
    {
        int3[] dims = AirMip.BuildLevelDims(L0Dims, 4);

        Assert.AreEqual(4, dims.Length, "All 4 levels should be valid at this config (no clamp).");
        Assert.AreEqual(new int3(256, 128, 256), dims[0], "L1 dims");
        Assert.AreEqual(new int3(128, 64, 128), dims[1], "L2 dims");
        Assert.AreEqual(new int3(64, 32, 64), dims[2], "L3 dims");
        Assert.AreEqual(new int3(32, 16, 32), dims[3], "L4 dims");
    }

    [Test]
    public void LevelDims_ClampsWhenAnAxisWouldDropBelowOne()
    {
        // A deliberately tiny window to exercise the clamp path that the real
        // config never hits. Y = 8 bricks: >>1=4, >>2=2, >>3=1, >>4=0 -> clamp
        // after level 3.
        int3 tiny = new int3(64, 8, 64);
        int3[] dims = AirMip.BuildLevelDims(tiny, 4);

        Assert.AreEqual(3, dims.Length, "Level count must clamp when Y>>4 would be 0.");
        Assert.AreEqual(new int3(32, 4, 32), dims[0]);
        Assert.AreEqual(new int3(16, 2, 16), dims[1]);
        Assert.AreEqual(new int3(8, 1, 8), dims[2]);
    }

    // ---------------------------------------------------------------------
    //  All-air world -> every cell 0 at every level.
    // ---------------------------------------------------------------------

    [Test]
    public void AllAir_EveryCellIsZero_AtEveryLevel()
    {
        AirMipData mips = AirMip.Build(AllAirL0(), L0Dims, 4);

        for (int lvl = 0; lvl < mips.NumLevels; lvl++)
        {
            uint[] cells = mips.Levels[lvl];
            for (int i = 0; i < cells.Length; i++)
                Assert.AreEqual(0u, cells[i], $"Level {lvl + 1} cell {i} should be air (0).");
        }
    }

    // ---------------------------------------------------------------------
    //  One solid brick -> exactly its ancestor chain (one cell per level) is 1.
    // ---------------------------------------------------------------------

    [Test]
    public void OneSolidBrick_FlipsExactlyItsAncestorChain()
    {
        uint[] l0 = AllAirL0();

        // Put a uniform-solid brick at a brick coordinate whose ancestor cells
        // are easy to reason about. Brick (40, 20, 72).
        int3 solidBrick = new int3(40, 20, 72);
        l0[L0Index(solidBrick)] = SolidHandle(2);

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);

        // The one non-air cell at level k is brick >> k.
        for (int k = 1; k <= mips.NumLevels; k++)
        {
            int3 expectedCell = solidBrick >> k;
            uint[] cells = mips.Level(k);
            int3 dims = mips.DimsOfLevel(k);

            // The expected ancestor cell must be 1.
            Assert.AreEqual(1u, cells[AirMip.FlatIndex(expectedCell, dims)],
                $"L{k}: ancestor cell {expectedCell} of solid brick should be 1.");

            // Every OTHER cell must still be 0.
            int nonAirCount = 0;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != 0u) nonAirCount++;
            Assert.AreEqual(1, nonAirCount,
                $"L{k}: exactly one cell should be non-air, found {nonAirCount}.");
        }
    }

    // ---------------------------------------------------------------------
    //  A DENSE brick that is (hypothetically) all-air internally still counts
    //  as NOT air -> its ancestors are 1 (conservative rule).
    // ---------------------------------------------------------------------

    [Test]
    public void DenseBrick_CountsAsNotAir_EvenIfInternallyAir()
    {
        uint[] l0 = AllAirL0();

        int3 denseBrick = new int3(10, 6, 18);
        l0[L0Index(denseBrick)] = DenseHandle(1234); // dense: top bit set

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);

        for (int k = 1; k <= mips.NumLevels; k++)
        {
            int3 cell = denseBrick >> k;
            Assert.AreEqual(1u, mips.Level(k)[AirMip.FlatIndex(cell, mips.DimsOfLevel(k))],
                $"L{k}: a dense brick must make its ancestor non-air (conservative).");
        }
    }

    // ---------------------------------------------------------------------
    //  Solid at a boundary shared by neighboring L1 cells -> only the CONTAINING
    //  L1 cell flips, not its neighbor (bounds / reduction-window check).
    // ---------------------------------------------------------------------

    [Test]
    public void SolidAtCellBoundary_FlipsOnlyContainingCell()
    {
        uint[] l0 = AllAirL0();

        // Brick (16, 0, 0): L1 cell = (8, 0, 0). Its neighbor L1 cell (7,0,0)
        // covers bricks 14..15, which stay air. Brick 16 is the first brick of
        // cell 8, so only cell 8 should flip.
        int3 solidBrick = new int3(16, 0, 0);
        l0[L0Index(solidBrick)] = SolidHandle(3);

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);

        int3 d1 = mips.DimsOfLevel(1);
        uint[] l1 = mips.Level(1);

        Assert.AreEqual(1u, l1[AirMip.FlatIndex(new int3(8, 0, 0), d1)],
            "Containing L1 cell (8,0,0) must be non-air.");
        Assert.AreEqual(0u, l1[AirMip.FlatIndex(new int3(7, 0, 0), d1)],
            "Neighbor L1 cell (7,0,0) must remain air.");
        Assert.AreEqual(0u, l1[AirMip.FlatIndex(new int3(9, 0, 0), d1)],
            "Neighbor L1 cell (9,0,0) must remain air.");
    }

    // ---------------------------------------------------------------------
    //  RebuildRegion: set a solid, build; then clear it and RebuildRegion over
    //  its chunk -> ancestors return to 0. Guards the maintenance path.
    // ---------------------------------------------------------------------

    [Test]
    public void RebuildRegion_AfterClearingSolid_ReturnsAncestorsToAir()
    {
        uint[] l0 = AllAirL0();

        int3 solidBrick = new int3(40, 20, 72);
        l0[L0Index(solidBrick)] = SolidHandle(2);

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);

        // Sanity: ancestor chain is non-air after the build.
        for (int k = 1; k <= mips.NumLevels; k++)
            Assert.AreEqual(1u,
                mips.Level(k)[AirMip.FlatIndex(solidBrick >> k, mips.DimsOfLevel(k))],
                $"Precondition L{k}: ancestor should be non-air before clear.");

        // Clear the brick back to air and rebuild the chunk-sized region that
        // contains it. The chunk owning brick (40,20,72) spans bricks
        // [chunk*16 .. chunk*16+15] on each axis.
        l0[L0Index(solidBrick)] = 0u; // uniform-air

        int3 chunk = solidBrick >> 4; // brick -> chunk
        int3 regionMin = chunk * 16;
        int3 regionMax = regionMin + new int3(15, 15, 15);

        AirMip.RebuildRegion(l0, mips, regionMin, regionMax);

        // Every level's ancestor cell must be back to air (0), and the whole
        // pyramid must once again be entirely air.
        for (int k = 1; k <= mips.NumLevels; k++)
        {
            Assert.AreEqual(0u,
                mips.Level(k)[AirMip.FlatIndex(solidBrick >> k, mips.DimsOfLevel(k))],
                $"L{k}: ancestor cell should return to air after RebuildRegion.");

            uint[] cells = mips.Level(k);
            for (int i = 0; i < cells.Length; i++)
                Assert.AreEqual(0u, cells[i],
                    $"L{k}: cell {i} should be air after clearing the only solid.");
        }
    }

    // ---------------------------------------------------------------------
    //  RebuildRegion the OTHER direction: air -> solid via a region rebuild
    //  reaches every level (mirror of the clear case; the air->solid edit order
    //  the plan calls "safe order").
    // ---------------------------------------------------------------------

    [Test]
    public void RebuildRegion_AfterAddingSolid_FlipsAncestorChain()
    {
        uint[] l0 = AllAirL0();
        AirMipData mips = AirMip.Build(l0, L0Dims, 4); // all air

        int3 solidBrick = new int3(12, 4, 200);
        l0[L0Index(solidBrick)] = SolidHandle(5);

        int3 chunk = solidBrick >> 4;
        int3 regionMin = chunk * 16;
        int3 regionMax = regionMin + new int3(15, 15, 15);

        AirMip.RebuildRegion(l0, mips, regionMin, regionMax);

        for (int k = 1; k <= mips.NumLevels; k++)
            Assert.AreEqual(1u,
                mips.Level(k)[AirMip.FlatIndex(solidBrick >> k, mips.DimsOfLevel(k))],
                $"L{k}: ancestor cell should be non-air after adding a solid via RebuildRegion.");
    }
}