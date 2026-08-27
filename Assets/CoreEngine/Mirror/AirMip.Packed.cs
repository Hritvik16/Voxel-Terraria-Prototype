// Assets/CoreEngine/Mirror/AirMip.Packed.cs
//
// Bit-packed representation of the air-mip pyramid, plus the merged-buffer
// layout the shader reads.
//
// WHY: the pyramid currently stores ONE UINT (4 bytes) per cell to hold a
// single 0/1 bit. At the real window size that is ~38 MB across four levels -
// far larger than the M1's 8 MB system-level cache, so every mip probe is a
// potential cache miss against main memory. Packed at 1 bit per cell the
// whole pyramid is ~1.2 MB and fits in cache with room to spare.
//
// It also merges all four levels into ONE flat word array with per-level
// offsets. That kills the shader's 4-way `if (level==1) ... if (level==2)`
// branch over four separate StructuredBuffers, which the compiler could not
// index dynamically and could not hoist out of the traversal loop.
//
// This file is ADDITIVE - AirMip.cs's proven build logic is untouched. Packing
// is a pure transform of its already-tested output, so correctness reduces to
// "does the pack/unpack round-trip exactly", which AirMipPackedTests.cs fuzzes.

using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    public static partial class AirMip
    {
        // All levels concatenated into one word array. Level k's cells start at
        // WordOffsets[k-1] words in. Cell i of level k lives at bit (i & 31) of
        // word (WordOffsets[k-1] + (i >> 5)).
        public sealed class PackedMips
        {
            public uint[] Words;
            public int[] WordOffsets;   // per level, 0-based index = level-1
            public int3[] LevelDims;    // per level, 0-based index = level-1
            public int NumLevels;

            public int WordCount => Words.Length;
            public int ByteSize => Words.Length * 4;
        }

        public static PackedMips Pack(AirMipData mips)
        {
            int n = mips.NumLevels;
            var offsets = new int[n];
            int totalWords = 0;

            for (int k = 0; k < n; k++)
            {
                offsets[k] = totalWords;
                int cells = mips.Levels[k].Length;
                totalWords += (cells + 31) / 32; // round up to whole words
            }

            var words = new uint[totalWords];

            for (int k = 0; k < n; k++)
            {
                uint[] level = mips.Levels[k];
                int off = offsets[k];
                for (int i = 0; i < level.Length; i++)
                {
                    // AirMip stores 0 = air, nonzero = occupied. Packed keeps
                    // the same polarity: bit set == occupied == "not air".
                    if (level[i] != 0u)
                        words[off + (i >> 5)] |= 1u << (i & 31);
                }
            }

            return new PackedMips
            {
                Words = words,
                WordOffsets = offsets,
                LevelDims = mips.LevelDims,
                NumLevels = n
            };
        }

        // Read one cell back out of the packed form. Used by tests and by the
        // GPU validator; the shader has its own inlined equivalent.
        public static bool IsCellOccupiedPacked(PackedMips p, int oneBasedLevel, int flatIndex)
        {
            int off = p.WordOffsets[oneBasedLevel - 1];
            return ((p.Words[off + (flatIndex >> 5)] >> (flatIndex & 31)) & 1u) != 0u;
        }

        // Convenience: pack straight from a store, matching BuildFromStore.
        public static PackedMips PackFromStore(ChunkStore store, int3 windowDimsBricks, int requestedLevels = 4)
        {
            AirMipData mips = BuildFromStore(store, windowDimsBricks, requestedLevels);
            return Pack(mips);
        }
    }
}