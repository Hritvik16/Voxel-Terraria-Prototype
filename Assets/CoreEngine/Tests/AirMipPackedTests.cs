// Assets/CoreEngine/Tests/AirMipPackedTests.cs
//
// Correctness bar for the bit-packed mip: packing is a pure transform of
// AirMip's already-proven output, so the whole question reduces to "does
// every cell round-trip exactly". These fuzz that directly rather than
// trusting the argument.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class AirMipPackedTests
{
    private static readonly int3 L0Dims = new int3(512, 256, 512);

    private static uint[] AllAirL0() => new uint[L0Dims.x * L0Dims.y * L0Dims.z];
    private static int L0Index(int3 brick) => AirMip.FlatIndex(brick, L0Dims);

    [Test]
    public void Pack_AllAir_EveryBitClear()
    {
        AirMipData mips = AirMip.Build(AllAirL0(), L0Dims, 4);
        AirMip.PackedMips packed = AirMip.Pack(mips);

        foreach (uint w in packed.Words)
            Assert.AreEqual(0u, w, "an all-air world must pack to all-zero words");
    }

    [Test]
    public void Pack_SizeIsThirtyTwoTimesSmaller()
    {
        AirMipData mips = AirMip.Build(AllAirL0(), L0Dims, 4);
        AirMip.PackedMips packed = AirMip.Pack(mips);

        int unpackedBytes = 0;
        for (int k = 0; k < mips.NumLevels; k++) unpackedBytes += mips.Levels[k].Length * 4;

        // 1 bit vs 32 bits per cell, plus word-rounding slack per level.
        Assert.Less(packed.ByteSize * 30, unpackedBytes,
            $"packed ({packed.ByteSize} B) should be ~32x smaller than unpacked ({unpackedBytes} B)");
    }

    [Test]
    public void Pack_OneSolidBrick_RoundTripsExactly()
    {
        uint[] l0 = AllAirL0();
        l0[L0Index(new int3(40, 20, 72))] = 2u; // uniform-solid handle

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        AirMip.PackedMips packed = AirMip.Pack(mips);

        AssertFullRoundTrip(mips, packed);
    }

    [Test]
    public void Pack_DenseBrick_RoundTripsExactly()
    {
        uint[] l0 = AllAirL0();
        l0[L0Index(new int3(10, 6, 18))] = 0x80000000u | 1234u; // dense handle

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        AirMip.PackedMips packed = AirMip.Pack(mips);

        AssertFullRoundTrip(mips, packed);
    }

    [Test]
    public void Pack_RandomField_RoundTripsExactly()
    {
        var rng = new System.Random(20260819);
        uint[] l0 = AllAirL0();

        for (int i = 0; i < 3000; i++)
        {
            int3 b = new int3(rng.Next(0, 256), rng.Next(0, 128), rng.Next(0, 256));
            l0[L0Index(b)] = rng.NextDouble() < 0.5 ? (uint)(2 + rng.Next(6)) : (0x80000000u | (uint)rng.Next(100000));
        }

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        AirMip.PackedMips packed = AirMip.Pack(mips);

        AssertFullRoundTrip(mips, packed);
    }

    [Test]
    public void Pack_WordBoundaryCells_RoundTripExactly()
    {
        // Cells at indices 31/32/63/64 straddle word boundaries - the classic
        // off-by-one location for a packing bug.
        uint[] l0 = AllAirL0();
        AirMipData baseMips = AirMip.Build(l0, L0Dims, 4);
        int3 d1 = baseMips.DimsOfLevel(1);

        foreach (int flat in new[] { 0, 1, 30, 31, 32, 33, 62, 63, 64, 65 })
        {
            int cx = flat % d1.x;
            int cy = (flat / d1.x) % d1.y;
            int cz = flat / (d1.x * d1.y);
            int3 brick = new int3(cx, cy, cz) * 2; // an L0 brick inside that L1 cell
            l0[L0Index(brick)] = 2u;
        }

        AirMipData mips = AirMip.Build(l0, L0Dims, 4);
        AirMip.PackedMips packed = AirMip.Pack(mips);
        AssertFullRoundTrip(mips, packed);
    }

    private static void AssertFullRoundTrip(AirMipData mips, AirMip.PackedMips packed)
    {
        Assert.AreEqual(mips.NumLevels, packed.NumLevels, "level count must match");

        for (int k = 1; k <= mips.NumLevels; k++)
        {
            uint[] level = mips.Level(k);
            for (int i = 0; i < level.Length; i++)
            {
                bool expected = level[i] != 0u;
                bool actual = AirMip.IsCellOccupiedPacked(packed, k, i);
                if (expected != actual)
                    Assert.Fail($"L{k} cell flat={i}: unpacked={level[i]} (occupied={expected}) but packed says occupied={actual}");
            }
        }
    }
}