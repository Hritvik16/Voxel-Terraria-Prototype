// ==========================================
// Assets/CoreEngine/Memory/ChunkFluidMask.cs
//
// "WHICH TILES OF THIS CHUNK CONTAIN FLUID-CAPABLE MATERIAL", as one ulong.
//
// WHY THIS EXISTS. DESIGN_NOTE_7_2 §9: a sparse tile set does not know where
// DORMANT fluid is. The dense region never had to -- CSWakeScan sweeps every
// cell every tick, which is how §7.4's "on approach it wakes" is delivered
// today. Take the cells away and a settled pool whose tile was released becomes
// invisible to the GPU, and nothing ever asks for it back. That is the same
// failure shape as the §7.4 bug fixed on 2026-09-05, reintroduced by the
// structure meant to make the radius affordable.
//
// AirMip (8.7) and OccupancyMask (8.8) cannot answer it: both summarise AIR vs
// NOT-AIR, so stone and water read identically. Checked, not assumed.
//
// THE FIT IS EXACT, AND IT IS WHY T=32 WAS CHOSEN. A chunk is 128 voxels; at a
// 32-voxel tile that is 4x4x4 = 64 tiles = exactly one ulong, one bit each. At
// T=16 it would be 512 tiles and eight words.
//
// THE ERROR DIRECTION IS CHOSEN, NOT INCIDENTAL. A set bit means "there may be
// fluid here"; a clear bit means "there is definitely none". Over-reporting
// costs a tile that is acquired, found empty and freed next tick by §3.10's
// rule. Under-reporting means fluid that never wakes -- the exact defect class
// this whole design exists to avoid. So every uncertain path sets rather than
// clears, and clearing only ever happens after an exact re-scan.
//
// PURE AND ALLOCATION-FREE. Nothing here allocates, and every function takes
// its state as arguments, so all of it is provable against a synthetic chunk
// with no ChunkStore, no streaming and no GPU.

using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    public static class ChunkFluidMask
    {
        /// Tile edge in voxels. MUST divide CHUNK_EDGE_VOXELS, and the quotient
        /// cubed must be <= 64 so the mask fits one ulong.
        public const int TILE_EDGE = 32;
        public const int TILES_PER_EDGE = EngineConfig.CHUNK_EDGE_VOXELS / TILE_EDGE;  // 4
        public const int TILES_PER_CHUNK = TILES_PER_EDGE * TILES_PER_EDGE * TILES_PER_EDGE; // 64
        public const int TILE_SHIFT = 5;    // log2(32)
        public const int BRICKS_PER_TILE_EDGE = TILE_EDGE / EngineConfig.BRICK_EDGE;  // 4

        /// Bit index for a tile within a chunk: x + y*4 + z*16.
        ///
        /// ONE CONVENTION, DEFINED HERE, NEVER REINVENTED -- the same discipline
        /// OccupancyMask's octant ordering documents. Every producer and
        /// consumer must agree or the mask silently describes other tiles.
        public static int TileBit(int3 tileInChunk)
            => tileInChunk.x + tileInChunk.y * TILES_PER_EDGE
                            + tileInChunk.z * (TILES_PER_EDGE * TILES_PER_EDGE);

        /// Which tile of its chunk a world voxel falls in.
        ///
        /// NOT CoordMath.LocalVoxelIndex3D -- that is the voxel's index inside
        /// its BRICK (& 7), not inside its chunk. Using it here silently mapped
        /// every voxel into tile (0,0,0) of an 8-voxel space, so the whole mask
        /// described the wrong tiles. Caught by
        /// AVoxelMapsToTheTileThatGeometricallyContainsIt before this was wired
        /// to anything, which is the entire argument for building it in
        /// isolation first.
        public static int3 TileInChunkOf(int3 worldVoxel)
        {
            int3 local = worldVoxel & (EngineConfig.CHUNK_EDGE_VOXELS - 1);
            return local >> TILE_SHIFT;
        }

        public static ulong BitFor(int3 worldVoxel) => 1UL << TileBit(TileInChunkOf(worldVoxel));

        public static bool IsSet(ulong mask, int3 tileInChunk)
            => (mask & (1UL << TileBit(tileInChunk))) != 0UL;

        public static bool HasFluidAt(ulong mask, int3 worldVoxel)
            => (mask & BitFor(worldVoxel)) != 0UL;

        public static ulong WithVoxel(ulong mask, int3 worldVoxel) => mask | BitFor(worldVoxel);

        // =====================================================================
        // Exact rebuild
        // =====================================================================

        /// Recomputes the whole mask from the chunk's actual contents.
        ///
        /// BRICK-LEVEL, NOT VOXEL-LEVEL, wherever it can be: a uniform brick is
        /// one material test for 512 voxels, and most of a chunk is uniform air
        /// or uniform stone. Only dense bricks are walked byte by byte, and only
        /// until the first mobile voxel -- the answer is a bit, not a count.
        public static ulong Rebuild(Chunk chunk, BrickDataPool pool)
        {
            if (chunk == null) return 0UL;

            if (chunk.isUniform)
                return MaterialRules.IsMobile(chunk.uniformMaterial) ? ulong.MaxValue : 0UL;

            if (chunk.bricks == null) return 0UL;

            ulong mask = 0UL;
            for (int bz = 0; bz < EngineConfig.CHUNK_EDGE_BRICKS; bz++)
            for (int by = 0; by < EngineConfig.CHUNK_EDGE_BRICKS; by++)
            for (int bx = 0; bx < EngineConfig.CHUNK_EDGE_BRICKS; bx++)
            {
                int bit = TileBit(new int3(bx / BRICKS_PER_TILE_EDGE,
                                           by / BRICKS_PER_TILE_EDGE,
                                           bz / BRICKS_PER_TILE_EDGE));
                ulong bitMask = 1UL << bit;
                if ((mask & bitMask) != 0UL) continue;      // this tile already answered

                if (BrickHasMobile(chunk, pool, new int3(bx, by, bz))) mask |= bitMask;
            }
            return mask;
        }

        /// Recomputes ONE tile's bit. Used when a write clears the last fluid in
        /// a tile: setting is exact and free, clearing is not, so a clear costs
        /// this scan rather than being guessed at.
        public static ulong RebuildTile(Chunk chunk, BrickDataPool pool, int3 tileInChunk, ulong mask)
        {
            ulong bitMask = 1UL << TileBit(tileInChunk);
            if (chunk == null) return mask & ~bitMask;

            if (chunk.isUniform)
                return MaterialRules.IsMobile(chunk.uniformMaterial) ? (mask | bitMask) : (mask & ~bitMask);

            if (chunk.bricks == null) return mask & ~bitMask;

            int3 b0 = tileInChunk * BRICKS_PER_TILE_EDGE;
            for (int bz = 0; bz < BRICKS_PER_TILE_EDGE; bz++)
            for (int by = 0; by < BRICKS_PER_TILE_EDGE; by++)
            for (int bx = 0; bx < BRICKS_PER_TILE_EDGE; bx++)
                if (BrickHasMobile(chunk, pool, b0 + new int3(bx, by, bz)))
                    return mask | bitMask;

            return mask & ~bitMask;
        }

        private static bool BrickHasMobile(Chunk chunk, BrickDataPool pool, int3 localBrick)
        {
            uint h = chunk.bricks[CoordMath.LocalBrickIndex(localBrick)].data;

            if ((h & 0x80000000u) == 0u)                   // uniform brick
                return MaterialRules.IsMobile((byte)(h & 0xFFu));

            if (pool == null) return true;                 // cannot tell -> assume fluid (safe direction)

            int poolIndex = (int)(h & 0x3FFFFFFFu);
            int b = poolIndex * EngineConfig.BRICK_BODY_BYTES;
            var raw = pool.RawData;
            for (int i = 0; i < EngineConfig.BRICK_BODY_BYTES; i++)
                if (MaterialRules.IsMobile(raw[b + i])) return true;   // a bit, not a count
            return false;
        }

        // =====================================================================
        // Query
        // =====================================================================

        /// World-voxel bounds of one tile of a chunk, inclusive.
        public static void TileBounds(int3 chunkCoord, int3 tileInChunk, out int3 lo, out int3 hi)
        {
            lo = (chunkCoord * EngineConfig.CHUNK_EDGE_VOXELS) + (tileInChunk << TILE_SHIFT);
            hi = lo + new int3(TILE_EDGE - 1, TILE_EDGE - 1, TILE_EDGE - 1);
        }

        /// The tile's centre voxel -- what a radius test wants.
        public static int3 TileCentre(int3 chunkCoord, int3 tileInChunk)
        {
            TileBounds(chunkCoord, tileInChunk, out int3 lo, out _);
            return lo + new int3(TILE_EDGE >> 1, TILE_EDGE >> 1, TILE_EDGE >> 1);
        }

        /// Absolute tile coordinate, in the same space FluidTileMap indexes by.
        public static int3 AbsoluteTile(int3 chunkCoord, int3 tileInChunk)
            => chunkCoord * TILES_PER_EDGE + tileInChunk;

        public static int PopCount(ulong mask)
        {
            int n = 0;
            while (mask != 0UL) { mask &= mask - 1UL; n++; }
            return n;
        }
    }
}
