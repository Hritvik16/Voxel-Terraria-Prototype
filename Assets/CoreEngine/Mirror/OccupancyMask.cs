// Assets/CoreEngine/Mirror/OccupancyMask.cs
//
// Amendment 8.8 — Phase A: CPU-only occupancy bitmask builder.
//
// WHAT THIS IS
//   A per-cell 8-bit occupancy summary layered ON TOP of the already-proven
//   AirMipData (Amendment 8.7). For a level-k cell, bit i (0..7) is set iff
//   that cell's child octant i is NOT air. This is strictly a derived view:
//   AirMip.cs is not modified, and this file introduces no new "what counts
//   as air" predicate — it only re-reads AirMip's already-tested output
//   (IsUniformAir for L0->L1, and the existing level-k values for k>=2).
//
// WHY THIS EXISTS (Amendment 8.8 motivation, full detail in
// AMENDMENT_8_8_OCCUPANCY_BITMASK.md)
//   Reseed (Amendment 8.7 attempt #3) fixed the exit-axis loop's O(exitCount)
//   cost but left the traversal's OUTER iteration count data-dependent — every
//   outer while-loop pass re-probes the mip hierarchy top-down. Two rays one
//   pixel apart can need very different outer-iteration counts, and a SIMD
//   warp waits for its slowest lane. This bitmask is step 1 toward a bounded-
//   depth descent (Phase B) that gives every ray the same WORST-CASE
//   iteration count (mip depth, <=5), rather than a count that varies with
//   how much air a specific ray happens to cross.
//
// OCTANT ORDERING
//   Bit i corresponds EXACTLY to AirMip's own ReduceFromL0/ReduceFromChild
//   loop order: i = dx + dy*2 + dz*4, where dx,dy,dz in {0,1} iterate in the
//   same (dz outer, dy middle, dx inner) nesting AirMip.cs already uses. This
//   is the ONE convention every consumer (this builder, Phase B's tracer,
//   Phase C's shader) must share - defined once here, never reinvented.
//
// THIS FILE DOES NOT TRAVERSE ANYTHING. It builds a data structure and
// nothing else, exactly like AirMip.cs's own Step 1.

using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    // Holds the built occupancy masks, one byte-packed-into-uint array per
    // level, parallel in shape to AirMipData's Levels array (same LevelDims,
    // same cell count per level, same toroidal FlatIndex convention - reuses
    // AirMip.FlatIndex directly rather than redefining indexing).
    public sealed class OccupancyMaskData
    {
        public readonly int NumLevels;
        public readonly int3[] LevelDims;   // identical to the source AirMipData's LevelDims
        public readonly byte[][] Levels;    // Levels[k] = occupancy byte per cell of level (k+1)

        public OccupancyMaskData(int3[] levelDims, byte[][] levels)
        {
            LevelDims = levelDims;
            Levels = levels;
            NumLevels = levels.Length;
        }

        // 1-based level accessor, matching AirMipData's own convention.
        public byte[] Level(int oneBasedLevel) => Levels[oneBasedLevel - 1];
        public int3 DimsOfLevel(int oneBasedLevel) => LevelDims[oneBasedLevel - 1];
    }

    public static class OccupancyMask
    {
        // Bit index for octant (dx,dy,dz), each in {0,1}. THE single shared
        // convention - Phase B's CPU tracer and Phase C's shader must both
        // derive octant selection using this exact mapping, never invent a
        // second one.
        public static int OctantBit(int dx, int dy, int dz) => dx + dy * 2 + dz * 4;

        // Build the full pyramid of occupancy masks from an already-built,
        // already-tested AirMipData plus the L0 handles it was built from
        // (needed for level 1, whose children are L0 bricks, not AirMip
        // cells - AirMipData does not itself store an L0 array).
        //
        //   l0Handles : the SAME flat L0 clipmap array passed to AirMip.Build
        //               / AirMip.BuildFromStore for this mips instance. Not
        //               re-validated here - correctness of "what is L0" is
        //               AirMipTests' job, not this file's.
        //   mips      : an already-built AirMipData (Amendment 8.7, proven by
        //               AirMipTests / AirMipValidator). This builder performs
        //               NO air/solid classification of its own - it only asks
        //               "is this already-computed cell air (0) or not" and
        //               packs the answer into a bitmask.
        public static OccupancyMaskData Build(uint[] l0Handles, AirMipData mips)
        {
            int n = mips.NumLevels;
            var levels = new byte[n][];

            // --- Level 1: children are L0 bricks. Reuses AirMip.IsUniformAir
            // (the one shared air predicate, §3.10/Amendment 8.7) rather than
            // re-deriving what "air" means. ---
            {
                int3 d1 = mips.DimsOfLevel(1);
                var l1 = new byte[d1.x * d1.y * d1.z];
                for (int cz = 0; cz < d1.z; cz++)
                for (int cy = 0; cy < d1.y; cy++)
                for (int cx = 0; cx < d1.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    l1[AirMip.FlatIndex(cell, d1)] = MaskFromL0(l0Handles, mips.L0Dims, cell);
                }
                levels[0] = l1;
            }

            // --- Level k (k>=2): children are level (k-1) cells. ---
            for (int k = 1; k < n; k++)
            {
                int3 childDims = mips.DimsOfLevel(k);
                uint[] child = mips.Level(k); // already-proven level-k AirMip values
                int3 d = mips.DimsOfLevel(k + 1);
                var lvl = new byte[d.x * d.y * d.z];
                for (int cz = 0; cz < d.z; cz++)
                for (int cy = 0; cy < d.y; cy++)
                for (int cx = 0; cx < d.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    lvl[AirMip.FlatIndex(cell, d)] = MaskFromChild(child, childDims, cell);
                }
                levels[k] = lvl;
            }

            return new OccupancyMaskData(mips.LevelDims, levels);
        }

        // REGION REBUILD (maintenance path, mirrors AirMip.RebuildRegion's
        // shape). Given an already-updated `mips` (i.e. AirMip.RebuildRegion
        // was already called for this region), recompute only the occupancy
        // cells covering the same region, bottom-up. Same caller obligation
        // as AirMip's own maintenance rule: call this AFTER AirMip.RebuildRegion,
        // never before, so `mips` reflects the new terrain first.
        public static void RebuildRegion(
            uint[] l0Handles, AirMipData mips, OccupancyMaskData occupancy,
            int3 regionMinBrick, int3 regionMaxBrick)
        {
            int3 prevMin = regionMinBrick >> 1;
            int3 prevMax = regionMaxBrick >> 1;

            // --- Level 1 over the affected cell box ---
            {
                int3 d1 = mips.DimsOfLevel(1);
                byte[] l1 = occupancy.Levels[0];
                for (int cz = prevMin.z; cz <= prevMax.z; cz++)
                for (int cy = prevMin.y; cy <= prevMax.y; cy++)
                for (int cx = prevMin.x; cx <= prevMax.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    l1[AirMip.FlatIndex(cell, d1)] = MaskFromL0(l0Handles, mips.L0Dims, cell);
                }
            }

            // --- Each higher level from the (already up to date) level below ---
            for (int k = 1; k < mips.NumLevels; k++)
            {
                int3 childDims = mips.DimsOfLevel(k);
                uint[] child = mips.Level(k);
                int3 d = mips.DimsOfLevel(k + 1);
                byte[] lvl = occupancy.Levels[k];

                int3 cMin = prevMin >> 1;
                int3 cMax = prevMax >> 1;
                for (int cz = cMin.z; cz <= cMax.z; cz++)
                for (int cy = cMin.y; cy <= cMax.y; cy++)
                for (int cx = cMin.x; cx <= cMax.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    lvl[AirMip.FlatIndex(cell, d)] = MaskFromChild(child, childDims, cell);
                }
                prevMin = cMin;
                prevMax = cMax;
            }
        }

        // ---- private mask construction ---------------------------------------

        // Level-1 cell's mask: bit i set iff L0 brick (baseBrick + octant i)
        // is NOT uniform-air. Same 8-brick coverage window AirMip.ReduceFromL0
        // uses, same (dz,dy,dx) nesting, so bit i and (dx,dy,dz) agree with
        // AirMip's own internal reduction by construction.
        private static byte MaskFromL0(uint[] l0, int3 l0Dims, int3 cell)
        {
            int3 baseBrick = cell * 2;
            byte mask = 0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int3 b = baseBrick + new int3(dx, dy, dz);
                int idx = AirMip.FlatIndex(b, l0Dims);
                if (!AirMip.IsUniformAir(l0[idx]))
                    mask |= (byte)(1 << OctantBit(dx, dy, dz));
            }
            return mask;
        }

        // Level-k (k>=2) cell's mask: bit i set iff child cell (baseChild +
        // octant i) at level (k-1) is non-air (AirMip value != 0). Same
        // coverage window as AirMip.ReduceFromChild.
        private static byte MaskFromChild(uint[] child, int3 childDims, int3 cell)
        {
            int3 baseChild = cell * 2;
            byte mask = 0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int3 c = baseChild + new int3(dx, dy, dz);
                int idx = AirMip.FlatIndex(c, childDims);
                if (child[idx] != 0u)
                    mask |= (byte)(1 << OctantBit(dx, dy, dz));
            }
            return mask;
        }
    }
}