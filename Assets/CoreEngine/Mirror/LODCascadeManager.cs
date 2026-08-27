// Assets/CoreEngine/Mirror/LODCascadeManager.cs
//
// Owns one CascadeTierPool per non-zero tier (tiers 1..LODConfig.TIER_COUNT-1
// - tier 0 is the existing TerrainClipmap, untouched). Mirrors the
// TerrainClipmap.Active static-instance pattern so RaymarchFeature can reach
// it the same way it reaches TerrainClipmap.Active.
using System;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Mirror
{
    public class LODCascadeManager : IDisposable
    {
        public static LODCascadeManager Active { get; private set; }

        private readonly CascadeTierPool[] _tierPools; // index 0 unused (tier 0 has no cascade pool)

        public CascadeTierPool TierPool(int tier)
        {
            if (tier <= 0 || tier >= LODConfig.TIER_COUNT)
                throw new ArgumentOutOfRangeException(nameof(tier), $"No cascade pool for tier {tier}.");
            return _tierPools[tier];
        }

        // PLACEHOLDER, NOT MEASURED. ARCHITECTURE_v8.6.md §11.3 lists cascade
        // pool memory as "[Phase 2 gate]" - an open measurement, not a derived
        // budget. This follows §11.3's own stated philosophy ("sized
        // aggressively low first, raised only if measurement allows") applied
        // to a case that section doesn't actually cover a number for yet.
        // 1/16 of the tier-0 brick pool cap per tier is a guess sized on the
        // intuition that coarse terrain should be far more uniform (fewer
        // dense bricks) than fine terrain, not on any measurement. Treat
        // BrickDataPool's "pool exhausted" exception, if it ever fires, as
        // the signal to raise this - not as a bug to silence.
        // Bumped from /16 to /4 after moving Phase2Bootstrapper's generation
        // from 8x8 to 22x22 chunks (~7.6x more chunks) - the old /16 value
        // was sized against the smaller world and was very likely to exhaust
        // (rough estimate: non-uniform coarse bricks concentrate near the
        // terrain surface, not throughout the volume, so per-chunk count
        // doesn't scale with full chunk volume - but 7.6x more chunks alone
        // likely pushes past the old ~46875 cap even so). /4 is STILL a
        // guess, not a measured number - same "not measured" status as
        // before, just less likely to immediately throw. If
        // BrickDataPool's exhaustion exception fires again, that remains
        // the correct signal to raise this further, not a bug to silence.
        public static int DefaultTierPoolCapacity(int brickPoolCapTier0) => Math.Max(1024, brickPoolCapTier0 / 4);

        public LODCascadeManager(int3 windowDimsChunks, Func<int, int> tierPoolCapacity)
        {
            _tierPools = new CascadeTierPool[LODConfig.TIER_COUNT];
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
            {
                int capacity = tierPoolCapacity(tier);
                _tierPools[tier] = new CascadeTierPool(tier, windowDimsChunks, capacity);
            }
            Active = this;
        }

        public void MarkDirty(int3 chunkCoord)
        {
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier].MarkDirty(chunkCoord);
        }

        public void UploadDirty(ChunkStore store, BrickDataPool pool)
        {
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier].UploadDirty(store, pool);
        }

        public void Dispose()
        {
            if (Active == this) Active = null;
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier]?.Dispose();
        }
    }
}