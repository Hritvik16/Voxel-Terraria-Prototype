// Assets/CoreEngine/Mirror/LODCascadeManager.cs
//
// Owns one CascadeTierPool per non-zero tier (tiers 1..LODConfig.TIER_COUNT-1
// - tier 0 is the existing TerrainClipmap, untouched). Mirrors the
// TerrainClipmap.Active static-instance pattern so RaymarchFeature can reach
// it the same way it reaches TerrainClipmap.Active.
using System;
using System.Collections.Generic;
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
        /// DEPRECATED SIZING, kept only for the Phase 2/3 bootstrappers.
        /// "/4" was never measured; EngineConfig.CascadeTierPoolCap(tier) now
        /// carries per-tier caps derived from BrickDataPool.PeakUsed. Phase 4
        /// uses that instead.
        public static int DefaultTierPoolCapacity(int brickPoolCapTier0) => Math.Max(1024, brickPoolCapTier0 / 4);

        public LODCascadeManager(int3 windowDimsChunks, Func<int, int> tierPoolCapacity)
        {
            _tierPools = new CascadeTierPool[LODConfig.TIER_COUNT];
            _batchScratch = new List<int3>[LODConfig.TIER_COUNT];
            for (int t = 0; t < LODConfig.TIER_COUNT; t++) _batchScratch[t] = new List<int3>();
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

        /// SHARED-CHAIN CASCADE UPLOAD. Default ON.
        ///
        /// Set false to restore the per-tier path byte-for-byte. That is the
        /// regression baseline every prior cascade proof was written against,
        /// and it is how the mutation check is run: flip this off and the cost
        /// must come back.
        public static bool SharedChainEnabled { get; set; } = true;

        private readonly List<int3> _unionScratch = new List<int3>();
        private readonly List<int3>[] _batchScratch;
        private LODDownsampler.DownsampleScratch _chainScratch;

        /// WHAT CHANGED AND WHAT DID NOT.
        ///
        /// NOT changed: the frame budget. MAX_CASCADE_CHUNKS_PER_FRAME still
        /// caps chunks and MAX_CASCADE_MS_PER_TIER still bounds wall clock.
        /// The cascade was ALREADY budgeted -- measured backlog under a live
        /// fluid load was p50 2, max 16 chunks and did not grow, so deferring
        /// more would only have made distant terrain staler without saving
        /// anything.
        ///
        /// Changed: the WORK. Every dirty chunk is marked dirty on EVERY tier
        /// (MarkDirty loops them), and each tier then re-gathered the chunk's
        /// 128^3 materials and re-ran the halving chain from scratch. At the
        /// shipped tier sizes tier 2 repeated 89% of tier 1's chain. Driving
        /// the tiers chunk-major computes the gather and the chain ONCE and
        /// lets each tier read its own step.
        public void UploadDirty(ChunkStore store, BrickDataPool pool)
        {
            if (!SharedChainEnabled)
            {
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                    _tierPools[tier].UploadDirty(store, pool);
                return;
            }

            // Pass 1 per tier: evicted clears (unbudgeted) + batch selection.
            _unionScratch.Clear();
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
            {
                _tierPools[tier].SelectBatch(store, _batchScratch[tier]);
                _tierPools[tier].ClearBrickSlotScratch();
                foreach (int3 c in _batchScratch[tier])
                    if (!_unionScratch.Contains(c)) _unionScratch.Add(c);
            }
            if (_unionScratch.Count == 0) return;

            _chainScratch ??= new LODDownsampler.DownsampleScratch();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            double budgetMs = EngineConfig.MAX_CASCADE_MS_PER_TIER * (LODConfig.TIER_COUNT - 1);
            int processed = 0;

            foreach (int3 chunkCoord in _unionScratch)
            {
                // Same shape as the per-tier guard: at least one chunk always
                // runs so the queue cannot stall, and beyond that a frame that
                // is already spent stops paying.
                if (processed > 0 && sw.Elapsed.TotalMilliseconds > budgetMs) break;
                processed++;

                double t0 = sw.Elapsed.TotalMilliseconds;
                Chunk chunk = store.GetChunk(chunkCoord);
                int steps = LODDownsampler.BuildChain(chunk, pool, _chainScratch);
                double downMs = sw.Elapsed.TotalMilliseconds - t0;

                double t1 = sw.Elapsed.TotalMilliseconds;
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                {
                    if (!_batchScratch[tier].Contains(chunkCoord)) continue;

                    byte[] result;
                    if (steps == 0)
                    {
                        // Null or uniform chunk. A REUSED BUFFER MUST BE
                        // WRITTEN IN FULL -- it still holds the last chunk.
                        result = LODDownsampler.ChainResultFor(tier, _chainScratch);
                        byte fill = (chunk == null) ? (byte)0 : chunk.uniformMaterial;
                        if (fill == 0) Array.Clear(result, 0, result.Length);
                        else Array.Fill(result, fill);
                    }
                    else result = LODDownsampler.ChainResultFor(tier, _chainScratch);

                    _tierPools[tier].ApplyChunkFromChain(store, chunkCoord, result);
                }
                double writeMs = sw.Elapsed.TotalMilliseconds - t1;

                // Attribute the shared chain to tier 1 so the existing report
                // lines keep summing to the real total rather than counting it
                // once per tier.
                _tierPools[1].AddTimings(downMs, 0);
                _tierPools[1].AddTimings(0, writeMs);
            }

            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier].FlushBrickBodies();
        }

        public void Dispose()
        {
            if (Active == this) Active = null;
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier]?.Dispose();
        }
    }
}