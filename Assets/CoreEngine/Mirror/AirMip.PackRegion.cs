// ==========================================
// Assets/CoreEngine/Mirror/AirMip.PackRegion.cs
//
// Incremental counterpart to AirMip.Pack. NEW FILE -- adds to the existing
// AirMip partial class rather than editing AirMip.Packed.cs, so the packer that
// AirMipPackedTests already covers stays untouched.
//
// NAMESPACE NOTE: this is VoxelEngine.MEMORY, not VoxelEngine.Mirror, despite
// living in the Mirror/ folder. AirMip, AirMipData and AirMip.PackedMips are all
// declared in VoxelEngine.Memory (AirMip.cs:42, AirMip.Packed.cs:24,
// AirMip.FromStore.cs:27) — the folder name and the namespace disagree here, and
// getting it wrong silently creates a SECOND, unrelated AirMip class rather than
// extending this one.
//
// WHY THIS EXISTS
// TerrainClipmap.UploadDirty called AirMip.Pack() on every frame that uploaded
// anything, which under streaming is most frames. Pack is a FULL rebuild: it
// allocates a fresh uint[] and walks every cell of every level -- ~2.4M cells at
// the Phase 4 mirror size (256x32x256 at level 1, plus the smaller levels).
// A dominant share of the frame budget went to re-deriving bits that had not
// changed, plus a ~300 KB allocation per frame feeding the GC.
//
// RebuildRegion already updates AirMipData incrementally for one chunk's brick
// box. This does the same for the packed mirror, walking the same shrinking cell
// box across levels so the two stay in lockstep.
//
// POLARITY MUST MATCH Pack EXACTLY: "AirMip stores 0 = air, nonzero = occupied.
// Packed keeps the same polarity: bit set == occupied". Pack builds into a
// zeroed array and only ever ORs bits in, so it never needs to clear. An
// INCREMENTAL update does: a cell going occupied -> air must CLEAR its bit.
// That is the dangerous direction -- a stale SET bit means "solid" where there
// is air, which costs traversal steps but is safe, while a stale CLEAR bit lets
// the traversal leap through solid terrain, i.e. holes. Both directions are
// written explicitly below rather than relying on OR alone.
using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    public static partial class AirMip
    {
        /// Re-packs only the cells covering `regionMinBrick`..`regionMaxBrick`
        /// (inclusive, in LEVEL-0 BRICK coordinates -- the same convention
        /// RebuildRegion takes). Call immediately after RebuildRegion for the
        /// same region so the packed mirror matches the pyramid it came from.
        ///
        /// `packed` is mutated in place; no allocation.
        public static void PackRegion(PackedMips packed, AirMipData mips,
                                      int3 regionMinBrick, int3 regionMaxBrick)
        {
            if (packed == null || packed.Words == null || mips == null) return;

            // Level 1 cells overlapping the region: divide brick bounds by 2 --
            // identical to RebuildRegion's opening step.
            int3 mn = regionMinBrick >> 1;
            int3 mx = regionMaxBrick >> 1;

            int levels = math.min(mips.NumLevels, packed.NumLevels);
            for (int k = 0; k < levels; k++)
            {
                int3 dims = mips.LevelDims[k];
                uint[] level = mips.Levels[k];
                int wordOffset = packed.WordOffsets[k];

                for (int cz = mn.z; cz <= mx.z; cz++)
                for (int cy = mn.y; cy <= mx.y; cy++)
                for (int cx = mn.x; cx <= mx.x; cx++)
                {
                    // FlatIndex wraps toroidally, matching how the rest of the
                    // clipmap addresses the window, so out-of-range cell coords
                    // fold back rather than throwing.
                    int flat = FlatIndex(new int3(cx, cy, cz), dims);
                    int word = wordOffset + (flat >> 5);
                    uint bit = 1u << (flat & 31);

                    if (level[flat] != 0u) packed.Words[word] |= bit;   // occupied
                    else                   packed.Words[word] &= ~bit;  // air -- MUST clear
                }

                // Each higher level halves the cell box, mirroring
                // RebuildRegion's own progression.
                mn >>= 1;
                mx >>= 1;
            }
        }
    }
}