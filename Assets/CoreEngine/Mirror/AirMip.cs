// Assets/CoreEngine/Mirror/AirMip.cs
//
// Amendment 8.7 — Step 1: CPU air-skip pyramid builder (no GPU, no shader).
//
// WHAT THIS IS
//   A read-elimination pyramid over the L0 clipmap. Each mip level is a flat,
//   directly-indexed, toroidal grid of `uint` cells. A cell holds 0 if EVERY L0
//   brick it covers is uniform-air, else 1. Traversal (Step 2+) uses it to cross
//   large air regions without reading L0 per brick. This file BUILDS the pyramid
//   only — it does not traverse anything.
//
//   §3.7's flat-no-pointer-tree constraint is preserved: every level is a flat
//   power-of-two grid, indexed by one bitwise calc, never a tree walk.
//
// THE ONE PREDICATE THAT MUST MATCH THE SHADER (Step 3/4)
//   "uniform-air" == (h & 0x80000000)==0 && (h & 0xFF)==0
//   i.e. top bit clear (uniform, not dense) AND material byte 0 (air).
//   This lives in exactly ONE place — IsUniformAir — and both this builder and
//   the Step-3 GPU-side interpretation read it, so "what counts as air" can
//   never drift between the CPU build and the GPU read. Do not inline this test
//   anywhere; call the helper.
//
// CONSISTENCY RULE (what makes mip traversal bit-consistent with L0 traversal)
//   Each level is a pure function of the L0 clipmap contents. A cell is air iff
//   ALL covered L0 handles are uniform-air, INCLUDING never-uploaded window
//   entries, which are zero-initialized and therefore read as uniform-air (0).
//   That matches how the clipmap itself zero-inits, so the pyramid agrees with
//   what an L0 walk over those same entries would have concluded.
//
// LEVELS (derived, this config WINDOW_BRICKS = 512 x 256 x 512)
//   L1 = 2^3 bricks/cell, dims WINDOW_BRICKS >> 1 = 256 x 128 x 256
//   L2 = 4^3 bricks/cell, dims WINDOW_BRICKS >> 2 = 128 x  64 x 128
//   L3 = 8^3 bricks/cell, dims WINDOW_BRICKS >> 3 =  64 x  32 x  64
//   L4 = 16^3 bricks/cell,dims WINDOW_BRICKS >> 4 =  32 x  16 x  32
//   Level k covers (2^k)^3 L0 bricks; its dims are WINDOW_BRICKS >> k.
//   If any axis of a level would drop below 1, the level count is clamped
//   (not applicable at this config; smallest dim is 16). See BuildLevelDims.

using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    // Holds the built pyramid. Four separate uint[] levels behind this holder so
    // Step-1 tests can assert individual cells by (level, cell) without offset
    // arithmetic. Converts trivially to any GPU layout at Step 3 behind this API.
    public sealed class AirMipData
    {
        public readonly int NumLevels;          // number of built levels (<= requested)
        public readonly int3 L0Dims;            // L0 clipmap dims in bricks
        public readonly int3[] LevelDims;       // LevelDims[k] = dims of level (k+1)
        public readonly uint[][] Levels;        // Levels[k]    = flat cells of level (k+1)

        public AirMipData(int3 l0Dims, int3[] levelDims, uint[][] levels)
        {
            L0Dims = l0Dims;
            LevelDims = levelDims;
            Levels = levels;
            NumLevels = levels.Length;
        }

        // Convenience: 1-based level accessor matching the plan's L1..L4 naming.
        // level == 1 -> Levels[0], etc. L0 is the clipmap itself, not stored here.
        public uint[] Level(int oneBasedLevel) => Levels[oneBasedLevel - 1];
        public int3 DimsOfLevel(int oneBasedLevel) => LevelDims[oneBasedLevel - 1];
    }

    public static partial class AirMip
    {
        // The single shared air predicate. MUST match the shader's Step 3/4 form.
        // Uniform brick (top bit 0) whose material byte is 0 (air).
        public static bool IsUniformAir(uint handle)
        {
            return (handle & 0x80000000u) == 0u && (handle & 0xFFu) == 0u;
        }

        // Flatten a cell coordinate within a level of the given dims, toroidally.
        // Dims are power-of-two by construction, so wrap is a mask. Identical
        // index form to the clipmap's own (x + y*W + z*W*H).
        public static int FlatIndex(int3 cell, int3 dims)
        {
            int3 wrapped = cell & (dims - new int3(1, 1, 1));
            return wrapped.x + wrapped.y * dims.x + wrapped.z * dims.x * dims.y;
        }

        // Derive per-level dims from L0 dims for `requestedLevels`, clamping the
        // count if any axis would drop below 1 (so tiny windows stay valid).
        // Returns the dims array (length == number of VALID levels).
        public static int3[] BuildLevelDims(int3 l0Dims, int requestedLevels)
        {
            var dims = new System.Collections.Generic.List<int3>(requestedLevels);
            for (int k = 1; k <= requestedLevels; k++)
            {
                int3 d = l0Dims >> k; // per-component arithmetic shift
                if (d.x < 1 || d.y < 1 || d.z < 1) break; // clamp: stop adding levels
                dims.Add(d);
            }
            return dims.ToArray();
        }

        // FULL BUILD. Builds all valid levels from the L0 clipmap handles.
        //   l0Handles : the flat L0 clipmap (length l0Dims.x*y*z), same layout
        //               and toroidal indexing as TerrainClipmap's _clipmapLocal.
        //   l0Dims    : L0 dims in bricks (e.g. 512 x 256 x 512).
        //   requestedLevels : how many mip levels to attempt (default 4).
        //
        // L1 is built from L0 (an 8-brick AND-reduction of IsUniformAir).
        // Lk (k>=2) is built from L(k-1): a cell is air iff all 8 child cells are
        // air. Building each level from the previous is O(total cells) and keeps
        // the reduction to a fixed 2x2x2 gather regardless of level.
        public static AirMipData Build(uint[] l0Handles, int3 l0Dims, int requestedLevels = 4)
        {
            int3[] levelDims = BuildLevelDims(l0Dims, requestedLevels);
            int n = levelDims.Length;
            var levels = new uint[n][];

            // --- Level 1 from L0 ---
            {
                int3 d1 = levelDims[0];
                var l1 = new uint[d1.x * d1.y * d1.z];
                for (int cz = 0; cz < d1.z; cz++)
                for (int cy = 0; cy < d1.y; cy++)
                for (int cx = 0; cx < d1.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    l1[FlatIndex(cell, d1)] = ReduceFromL0(l0Handles, l0Dims, cell) ? 0u : 1u;
                }
                levels[0] = l1;
            }

            // --- Level k from level (k-1) ---
            for (int k = 1; k < n; k++)
            {
                int3 childDims = levelDims[k - 1];
                uint[] child = levels[k - 1];
                int3 d = levelDims[k];
                var lvl = new uint[d.x * d.y * d.z];
                for (int cz = 0; cz < d.z; cz++)
                for (int cy = 0; cy < d.y; cy++)
                for (int cx = 0; cx < d.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    lvl[FlatIndex(cell, d)] = ReduceFromChild(child, childDims, cell) ? 0u : 1u;
                }
                levels[k] = lvl;
            }

            return new AirMipData(l0Dims, levelDims, levels);
        }

        // REGION REBUILD (maintenance path, Step 3 will call this on UploadDirty).
        // Given that some L0 handles inside a chunk-sized brick region changed,
        // recompute only the cells at every level that cover that region, bottom-
        // up. `regionMinBrick` / `regionMaxBrick` are INCLUSIVE brick-coordinate
        // bounds of the dirty region in L0 space (e.g. a chunk = 16^3 bricks).
        //
        // At Phase 2 the window is static so this is exercised mainly by tests;
        // it is the same maintenance the GPU upload will use when edits land.
        public static void RebuildRegion(
            uint[] l0Handles, AirMipData mips,
            int3 regionMinBrick, int3 regionMaxBrick)
        {
            int3 l0Dims = mips.L0Dims;

            // Level 1 cells overlapping the region: divide brick bounds by 2.
            int3 prevMin = regionMinBrick >> 1;
            int3 prevMax = regionMaxBrick >> 1;

            // --- Level 1 from L0 over the affected cell box ---
            {
                int3 d1 = mips.LevelDims[0];
                uint[] l1 = mips.Levels[0];
                for (int cz = prevMin.z; cz <= prevMax.z; cz++)
                for (int cy = prevMin.y; cy <= prevMax.y; cy++)
                for (int cx = prevMin.x; cx <= prevMax.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    l1[FlatIndex(cell, d1)] = ReduceFromL0(l0Handles, l0Dims, cell) ? 0u : 1u;
                }
            }

            // --- Each higher level from the previous, over shrinking cell box ---
            for (int k = 1; k < mips.NumLevels; k++)
            {
                int3 childDims = mips.LevelDims[k - 1];
                uint[] child = mips.Levels[k - 1];
                int3 d = mips.LevelDims[k];
                uint[] lvl = mips.Levels[k];

                int3 cMin = prevMin >> 1;
                int3 cMax = prevMax >> 1;
                for (int cz = cMin.z; cz <= cMax.z; cz++)
                for (int cy = cMin.y; cy <= cMax.y; cy++)
                for (int cx = cMin.x; cx <= cMax.x; cx++)
                {
                    int3 cell = new int3(cx, cy, cz);
                    lvl[FlatIndex(cell, d)] = ReduceFromChild(child, childDims, cell) ? 0u : 1u;
                }

                prevMin = cMin;
                prevMax = cMax;
            }
        }

        // ---- private reductions -------------------------------------------------

        // L1 cell (cx,cy,cz) covers L0 bricks [2*cx .. 2*cx+1] on each axis.
        // Returns true iff all 8 covered L0 handles are uniform-air. Never-mapped
        // entries can't occur here (we index within l0Dims, and out-of-window
        // handling is the caller's toroidal concern), but the flat index is masked
        // for safety by FlatIndex-style wrap on l0Dims.
        private static bool ReduceFromL0(uint[] l0, int3 l0Dims, int3 cell)
        {
            int3 baseBrick = cell * 2;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int3 b = baseBrick + new int3(dx, dy, dz);
                int idx = FlatIndex(b, l0Dims);
                if (!IsUniformAir(l0[idx])) return false;
            }
            return true;
        }

        // Lk cell covers child cells [2*c .. 2*c+1] on each axis. Air (0) iff all
        // 8 child cells are air (0).
        private static bool ReduceFromChild(uint[] child, int3 childDims, int3 cell)
        {
            int3 baseChild = cell * 2;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int3 c = baseChild + new int3(dx, dy, dz);
                int idx = FlatIndex(c, childDims);
                if (child[idx] != 0u) return false; // any non-air child -> not air
            }
            return true;
        }
    }
}