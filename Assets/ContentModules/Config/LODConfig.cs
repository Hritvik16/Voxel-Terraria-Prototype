// Assets/ContentModules/Config/LODConfig.cs
//
// Amendment 8.9, §5.1: "build it parameterized (tier count, tier voxel sizes,
// and tier boundaries all as config values, not hardcoded), so the open
// questions can be resolved by changing numbers, not by rewriting the system."
//
// This file is that parameterization. Every number below traces to a specific
// decision; see the comment on each field. Nothing here is derived from memory
// of the spec — the formula source is §6.4 C.5 (ARCHITECTURE_v8.6.md) and the
// resolved values are from Amendment 8.9 §2/§3 plus the chat decision that
// superseded the still-open items in §3.
//
// STATUS OF EACH NUMBER:
//   TIER_VOXEL_SIZE_M   - DECIDED (chat, this session). Supersedes the spec's
//                          literal §6.4 table (0.1/0.4/1.6m).
//   TIER_OUTER_RANGE_M  - tiers 0,1 DECIDED (chat, this session, from Amendment
//                          8.9 §3's own 540p-height table). Tier 2's outer bound
//                          is an ASSUMPTION: Amendment 8.9 §3 option 1
//                          (extend outermost tier to window corner distance),
//                          which the amendment recommends but never formally
//                          adopts. Flagged in chat; change here if wrong.
//   PIXEL_SUBTEND_SCREEN_HEIGHT - DECIDED: 540 (960x540 internal resolution).
using System;

public static class LODConfig
{
    public const int TIER_COUNT = 3;

    // Voxel edge length in meters, per tier. Tier 0 matches the existing
    // full-resolution terrain (CoordMath's 0.1m atomic unit) exactly - tier 0
    // is NOT a separate downsampled buffer, it's the existing TerrainClipmap.
    public static readonly float[] TIER_VOXEL_SIZE_M = { 0.1f, 0.2f, 0.4f };

    // Distance (meters, from camera) at which each tier's coverage ENDS and
    // the next coarser tier takes over. Tier N covers
    // (TIER_OUTER_RANGE_M[N-1], TIER_OUTER_RANGE_M[N]] (tier 0 starts at 0).
    //
    // Tiers 0->1 and 1->2: pixel-subtend formula (§6.4 C.5) at screen height
    // 540, taken directly from Amendment 8.9 §3's own table (already includes
    // the spec's ~1.24x headroom rounding, same style as the original 128m
    // figure at 1080p).
    //
    // Tier 2's outer bound: window corner distance. WINDOW_CHUNKS_XZ=32 chunks
    // x 12.8m/chunk = 409.6m window width -> half-diagonal = 409.6/2 * sqrt(2)
    // = 289.6m, rounded to 290m. This is Amendment 8.9 §3 "option 1" (extend
    // outermost tier past the strict pixel-math boundary to the window's own
    // shape) - the amendment's recommendation, not yet independently verified
    // against the window math by a second source. Re-derive if WINDOW_CHUNKS_XZ
    // changes.
    public static readonly float[] TIER_OUTER_RANGE_M = { 64f, 128f, 290f };

    // Screen height used for the pixel-subtend formula (§6.4 C.5):
    //   anglePerPixel = vFOV_radians / screenHeightPixels
    //   D(V) = V / anglePerPixel
    // Decided this session: internal render resolution is 960x540, so the
    // formula is evaluated at height 540, not 1080. If TIER_OUTER_RANGE_M
    // above is ever regenerated from the formula directly, use this constant,
    // not a hardcoded 540/1080.
    public const int PIXEL_SUBTEND_SCREEN_HEIGHT = 540;

    // Downsample factor from tier 0 to tier N, i.e. how many tier-0 voxels
    // per axis are merged into one tier-N voxel. Derived, not independently
    // chosen - must stay consistent with TIER_VOXEL_SIZE_M or the majority-vote
    // downsampler and the traversal step size disagree about what a tier
    // actually is.
    public static int DownsampleFactor(int tier)
    {
        if (tier == 0) return 1;
        float ratio = TIER_VOXEL_SIZE_M[tier] / TIER_VOXEL_SIZE_M[0];
        int factor = (int)Math.Round(ratio, MidpointRounding.AwayFromZero);
        return factor;
    }

    static LODConfig()
    {
        // Fail fast in the editor/tests if someone edits one array and not
        // the other, or introduces a non-power-of-two ratio (CoordMath's
        // whole shift/mask scheme depends on power-of-two factors - see
        // ARCHITECTURE_v8.6.md §2.3).
        if (TIER_VOXEL_SIZE_M.Length != TIER_COUNT)
            throw new InvalidOperationException(
                $"LODConfig.TIER_VOXEL_SIZE_M has {TIER_VOXEL_SIZE_M.Length} entries, expected TIER_COUNT={TIER_COUNT}.");
        if (TIER_OUTER_RANGE_M.Length != TIER_COUNT)
            throw new InvalidOperationException(
                $"LODConfig.TIER_OUTER_RANGE_M has {TIER_OUTER_RANGE_M.Length} entries, expected TIER_COUNT={TIER_COUNT}.");

        for (int t = 0; t < TIER_COUNT; t++)
        {
            int factor = DownsampleFactor(t);
            if (factor <= 0 || (factor & (factor - 1)) != 0)
                throw new InvalidOperationException(
                    $"LODConfig tier {t}: downsample factor {factor} (voxel size {TIER_VOXEL_SIZE_M[t]}m vs tier-0 {TIER_VOXEL_SIZE_M[0]}m) " +
                    "is not a power of two. CoordMath's shift/mask scheme requires power-of-two ratios between tiers.");

            if (t > 0 && TIER_OUTER_RANGE_M[t] <= TIER_OUTER_RANGE_M[t - 1])
                throw new InvalidOperationException(
                    $"LODConfig.TIER_OUTER_RANGE_M is not strictly increasing at tier {t} " +
                    $"({TIER_OUTER_RANGE_M[t]}m <= {TIER_OUTER_RANGE_M[t - 1]}m).");
        }
    }
}