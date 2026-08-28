// ==========================================
// Assets/CoreEngine/Mirror/LODDownsampler.cs
//
// Amendment 8.9 / ARCHITECTURE_v8.6.md §6.4: "LOD1-2 populated by majority-vote
// downsampling into flat cascade pools at chunk load."
//
// This file is the majority-vote step only: given tier-0 (full resolution)
// material data, produce the material grid for one coarser tier. It does NOT
// yet write into GPU cascade pool buffers - that's the next pass (mirrors
// TerrainClipmap's ClipmapBuffer/BrickDataBuffer pattern). Keeping this
// separate means the one piece with real correctness risk (the vote and its
// tie-break rule) has its own file and its own tests, independent of buffer
// plumbing.
//
// TIE-BREAK RULE (implementation detail, not specified verbatim by the spec -
// "majority-vote" alone doesn't define what happens on a tie, so this is
// documented here rather than left implicit):
//   - Air (material 0) wins only on a STRICT majority (5 of 8 samples).
//   - Otherwise, the most frequent non-air material wins.
//   - Ties among non-air materials, or an exact 4-4 air/solid split, resolve
//     to the LOWEST non-zero material id among the tied values. This is a
//     deliberate solid-preserving bias: at LOD distance, a thin solid feature
//     silently vanishing (because air won a coin-flip tie) is a worse visual
//     bug than a thin gap silently filling in. Revisit if a benchmark/visual
//     pass says otherwise - this is a design choice, not a measured fact.
using System;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Mirror
{
    // PHASE 4 NOTE: DownsampleChunkToTier now has a store-free (Chunk, pool)
    // form so worker threads can downsample freshly generated chunks they
    // exclusively own (§3.2: workers never read the store). The store-based
    // form delegates to it -- one implementation, two entry points. This file
    // is otherwise byte-identical to its Phase 3 state; it is shipped COMPLETE
    // because two successive anchored patches to it were mis-merged and the
    // resulting hybrid produced duplicate-definition errors.
    public static class LODDownsampler
    {
        /// <summary>
        /// Majority-vote merge of exactly 8 tier-(N-1) materials (a 2x2x2 block)
        /// into one tier-N material. See file header for the tie-break rule.
        ///
        /// PERFORMANCE (rewritten after a real measurement - see chat): the
        /// original version used a 256-entry stackalloc<int> table, zero-
        /// initialized and fully rescanned, for a vote among at most 8
        /// samples that can never have more than 8 distinct values. At the
        /// call volume this actually runs at (~270 MILLION calls across a
        /// full-world cascade upload, confirmed by reading the generator:
        /// ChunkGenerator always sets chunk.isUniform=false, so the
        /// chunk-level fast path added earlier never fires - every chunk
        /// goes through the full downsample chain), that 256-entry constant
        /// factor was the dominant remaining cost. This version tracks at
        /// most 8 (material, count) pairs directly - verified bit-identical
        /// to the old implementation across every documented test case in
        /// this file plus a 200,000-sample random fuzz before shipping (see
        /// chat), not assumed equivalent from reading the code alone.
        /// </summary>
        public static byte MajorityVote(byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h)
        {
            Span<byte> samples = stackalloc byte[8] { a, b, c, d, e, f, g, h };

            int airCount = 0;
            foreach (byte s in samples)
                if (s == 0) airCount++;

            if (airCount >= 5) return 0;

            Span<byte> distinctMaterials = stackalloc byte[8];
            Span<int> distinctCounts = stackalloc int[8];
            int distinctCount = 0;

            foreach (byte s in samples)
            {
                if (s == 0) continue;

                int idx = -1;
                for (int i = 0; i < distinctCount; i++)
                {
                    if (distinctMaterials[i] == s) { idx = i; break; }
                }

                if (idx >= 0)
                {
                    distinctCounts[idx]++;
                }
                else
                {
                    distinctMaterials[distinctCount] = s;
                    distinctCounts[distinctCount] = 1;
                    distinctCount++;
                }
            }

            int bestCount = 0;
            for (int i = 0; i < distinctCount; i++)
                if (distinctCounts[i] > bestCount) bestCount = distinctCounts[i];

            // Ties resolve to the LOWEST material id among everything tied for
            // bestCount - matches the original's ascending-scan-with-strict->
            // semantics exactly (verified in the fuzz above), just derived by
            // an explicit min-among-ties pass instead of scan order.
            byte bestMaterial = 0;
            for (int i = 0; i < distinctCount; i++)
            {
                if (distinctCounts[i] == bestCount)
                {
                    if (bestMaterial == 0 || distinctMaterials[i] < bestMaterial)
                        bestMaterial = distinctMaterials[i];
                }
            }

            return bestMaterial;
        }

        /// <summary>
        /// Downsamples one tier level from tier-0 (or tier N-1) material data
        /// for a single chunk-sized region. sourceMaterials is a flat array of
        /// sourceEdgeVoxels^3 bytes (one material per voxel, x-fastest, matching
        /// CoordMath.LocalVoxelIndex/LocalBrickIndex ordering), representing one
        /// chunk's worth of physical space at the SOURCE tier's voxel size.
        /// Returns a flat array of (sourceEdgeVoxels/2)^3 bytes at the next
        /// coarser tier's voxel size, same physical footprint, same ordering.
        /// </summary>
        public static byte[] DownsampleOnce(byte[] sourceMaterials, int sourceEdgeVoxels)
        {
            if (sourceEdgeVoxels <= 0 || (sourceEdgeVoxels & 1) != 0)
                throw new ArgumentException($"sourceEdgeVoxels must be even and positive, got {sourceEdgeVoxels}.", nameof(sourceEdgeVoxels));

            long expectedLen = (long)sourceEdgeVoxels * sourceEdgeVoxels * sourceEdgeVoxels;
            if (sourceMaterials.Length != expectedLen)
                throw new ArgumentException(
                    $"sourceMaterials.Length={sourceMaterials.Length} does not match sourceEdgeVoxels={sourceEdgeVoxels} cubed ({expectedLen}).",
                    nameof(sourceMaterials));

            int destEdge = sourceEdgeVoxels / 2;
            byte[] dest = new byte[destEdge * destEdge * destEdge];
            DownsampleOnceInto(sourceMaterials, sourceEdgeVoxels, dest);
            return dest;
        }

        /// Fill-into-caller-buffer form of DownsampleOnce. Identical math, no
        /// allocation. Every destination cell is written unconditionally, so a
        /// reused buffer needs no clearing here.
        public static void DownsampleOnceInto(byte[] sourceMaterials, int sourceEdgeVoxels, byte[] dest)
        {
            int destEdge = sourceEdgeVoxels / 2;
            int srcStride = sourceEdgeVoxels;
            int srcSlice = sourceEdgeVoxels * sourceEdgeVoxels;

            for (int dz = 0; dz < destEdge; dz++)
            for (int dy = 0; dy < destEdge; dy++)
            for (int dx = 0; dx < destEdge; dx++)
            {
                int sx = dx * 2;
                int sy = dy * 2;
                int sz = dz * 2;

                byte v0 = SampleFlat(sourceMaterials, sx, sy, sz, srcStride, srcSlice);
                byte v1 = SampleFlat(sourceMaterials, sx + 1, sy, sz, srcStride, srcSlice);
                byte v2 = SampleFlat(sourceMaterials, sx, sy + 1, sz, srcStride, srcSlice);
                byte v3 = SampleFlat(sourceMaterials, sx + 1, sy + 1, sz, srcStride, srcSlice);
                byte v4 = SampleFlat(sourceMaterials, sx, sy, sz + 1, srcStride, srcSlice);
                byte v5 = SampleFlat(sourceMaterials, sx + 1, sy, sz + 1, srcStride, srcSlice);
                byte v6 = SampleFlat(sourceMaterials, sx, sy + 1, sz + 1, srcStride, srcSlice);
                byte v7 = SampleFlat(sourceMaterials, sx + 1, sy + 1, sz + 1, srcStride, srcSlice);

                byte voted = MajorityVote(v0, v1, v2, v3, v4, v5, v6, v7);

                int destIndex = dx + destEdge * (dy + destEdge * dz);
                dest[destIndex] = voted;
            }
        }

        /// <summary>
        /// Downsamples tier-0 material data for one chunk, straight from
        /// ChunkStore, down to an arbitrary tier's voxel size. Chains
        /// DownsampleOnce as many times as LODConfig.DownsampleFactor requires
        /// (log2 of the factor), matching the "one traversal algorithm
        /// parameterized by voxel size" intent of §6.4 - the downsampler is
        /// likewise generic over tier, not one hand-written pass per tier.
        /// </summary>
        public static byte[] DownsampleChunkToTier(ChunkStore store, BrickDataPool pool, int3 chunkCoord, int targetTier)
        {
            // The store's only job here is the lookup; everything downstream is
            // (chunk, pool). Delegating keeps ONE downsample implementation for
            // both callers, so the two cannot drift.
            return DownsampleChunkToTier(store.GetChunk(chunkCoord), pool, targetTier);
        }

        /// Per-worker reusable buffers for the whole downsample chain, sized
        /// once and reused for every chunk that worker handles.
        ///
        /// WHY: the allocating path below builds a fresh 128^3 tier-0 extraction
        /// (2 MB) plus one array per halving step, PER CHUNK PER TIER. At the
        /// ~1620 chunks a single traversal admits that is multiple GB of
        /// garbage, all of it concentrated in the re-admission bursts on return
        /// legs -- which is precisely where the rig measured 5-8 gen-0
        /// collections inside ONE frame and frames of 1.0-2.6 SECONDS
        /// (2026-08-28_151418, gc+N column), while every main-thread phase timer
        /// stayed under a millisecond. §0.1 invariant 3 -- "no hidden
        /// allocations on the hot path" -- is what this restores.
        public sealed class DownsampleScratch
        {
            public readonly byte[] Tier0;
            public readonly byte[][] Steps;   // Steps[i] holds edge 128 >> (i+1)

            public DownsampleScratch()
            {
                const int chunkEdgeVoxels = 128;
                Tier0 = new byte[chunkEdgeVoxels * chunkEdgeVoxels * chunkEdgeVoxels];

                int maxFactor = 1;
                for (int t = 1; t < LODConfig.TIER_COUNT; t++)
                    maxFactor = Math.Max(maxFactor, LODConfig.DownsampleFactor(t));

                int maxSteps = IntegerLog2(maxFactor);
                Steps = new byte[maxSteps][];
                int edge = chunkEdgeVoxels;
                for (int i = 0; i < maxSteps; i++)
                {
                    edge /= 2;
                    Steps[i] = new byte[edge * edge * edge];
                }
            }
        }

        /// Allocation-free form.
        ///
        /// THE RETURNED ARRAY IS SCRATCH, NOT A GIFT. It is one of the caller's
        /// own reusable buffers and is valid only until this worker's next call.
        /// Anything that outlives the call -- notably a LoadCompletion queued for
        /// the main thread -- must COPY it. That copy is the one place ownership
        /// genuinely transfers, and the one place an allocation is warranted.
        public static byte[] DownsampleChunkToTier(Chunk chunk, BrickDataPool pool, int targetTier,
                                                   DownsampleScratch scratch)
        {
            if (scratch == null) return DownsampleChunkToTier(chunk, pool, targetTier);
            if (targetTier <= 0 || targetTier >= LODConfig.TIER_COUNT)
                throw new ArgumentOutOfRangeException(nameof(targetTier),
                    $"targetTier must be in [1, {LODConfig.TIER_COUNT - 1}], got {targetTier}.");

            const int chunkEdgeVoxels = 128;
            int factor = LODConfig.DownsampleFactor(targetTier);
            int steps = IntegerLog2(factor);
            byte[] result = scratch.Steps[steps - 1];

            // Same two fast paths as the allocating form, but a reused buffer
            // must be written in full -- it still holds the previous chunk.
            if (chunk == null)
            {
                Array.Clear(result, 0, result.Length);
                return result;
            }
            if (chunk.isUniform)
            {
                if (chunk.uniformMaterial == 0) Array.Clear(result, 0, result.Length);
                else Array.Fill(result, chunk.uniformMaterial);
                return result;
            }

            ExtractChunkTier0MaterialsInto(chunk, pool, chunkEdgeVoxels, scratch.Tier0);

            byte[] current = scratch.Tier0;
            int currentEdge = chunkEdgeVoxels;
            for (int i = 0; i < steps; i++)
            {
                DownsampleOnceInto(current, currentEdge, scratch.Steps[i]);
                current = scratch.Steps[i];
                currentEdge /= 2;
            }
            return current;
        }

        /// Store-free form for worker threads (§3.2: workers never read the
        /// store). The chunk and pool must be exclusively owned by the caller:
        /// a freshly generated chunk in its scratch pool qualifies; a RESIDENT
        /// chunk does not unless called from the main thread.
        public static byte[] DownsampleChunkToTier(Chunk chunk, BrickDataPool pool, int targetTier)
        {
            if (targetTier <= 0 || targetTier >= LODConfig.TIER_COUNT)
                throw new ArgumentOutOfRangeException(nameof(targetTier),
                    $"targetTier must be in [1, {LODConfig.TIER_COUNT - 1}], got {targetTier}.");

            const int chunkEdgeVoxels = 128; // CHUNK_EDGE_BRICKS(16) * BRICK_EDGE(8), per EngineConfig
            int factor = LODConfig.DownsampleFactor(targetTier);
            int outputEdge = chunkEdgeVoxels / factor;

            // FAST PATH, added after a real timing measurement (20.7s for
            // Cascades.UploadDirty vs 0.47s for Clipmap.UploadDirty across the
            // same 484 chunks - see chat). The bulk-brick extraction fix
            // eliminated the slow READ, but DownsampleOnce's majority-vote
            // loop was still running in full even for chunks where the
            // answer is trivial: majority-vote of an array that's already
            // one uniform value, at every step of the chain, is just that
            // same value. This is mathematically identical to running the
            // full pipeline (the existing UniformSolidChunk/UniformAirChunk
            // tests already prove this input/output shape, so this fast path
            // makes no behavior change, only skips redundant computation).
            if (chunk == null)
                return new byte[outputEdge * outputEdge * outputEdge]; // air - array already zero-filled
            if (chunk.isUniform)
            {
                byte[] uniformResult = new byte[outputEdge * outputEdge * outputEdge];
                if (chunk.uniformMaterial != 0)
                    Array.Fill(uniformResult, chunk.uniformMaterial);
                return uniformResult;
            }

            byte[] tier0 = ExtractChunkTier0Materials(chunk, pool, chunkEdgeVoxels);

            int steps = IntegerLog2(factor);
            byte[] current = tier0;
            int currentEdge = chunkEdgeVoxels;
            for (int i = 0; i < steps; i++)
            {
                current = DownsampleOnce(current, currentEdge);
                currentEdge /= 2;
            }

            return current;
        }

        /// <summary>
        /// Reads one chunk's full tier-0 material data directly from Chunk/
        /// BrickHandle/BrickDataPool - NOT via ChunkStore.GetVoxel() per voxel.
        ///
        /// PERFORMANCE NOTE (this replaced a per-voxel version after a real
        /// "beach ball for minutes" report at 484 generated chunks): the
        /// original version called store.GetVoxel() once per voxel - 128^3 =
        /// ~2.1M calls per chunk, each redoing a chunk lookup + uniform check
        /// + brick decode from scratch. At 484 chunks x 2 tiers that's ~2
        /// billion calls, all synchronous on the main thread in Start().
        /// This version walks the chunk's 4096 bricks directly (both index
        /// formulas below are confirmed simple row-major - x + 8y + 64z for
        /// voxels-in-brick, x + 16y + 256z for bricks-in-chunk, per
        /// CoordMath.LocalVoxelIndex/LocalBrickIndex - so a sequential bulk
        /// copy is correct, not just fast) and bulk-fills or bulk-copies each
        /// brick's 512 bytes in one shot. Trades encapsulation (this now
        /// knows about Chunk/BrickHandle/BrickDataPool internals directly,
        /// where the old version deliberately didn't, to stay robust against
        /// internal layout changes) for the ~2-3 orders of magnitude speedup
        /// that made the old approach viable at all at this chunk count.
        /// </summary>
        private static byte[] ExtractChunkTier0Materials(Chunk chunk, BrickDataPool pool, int chunkEdgeVoxels)
        {
            byte[] result = new byte[chunkEdgeVoxels * chunkEdgeVoxels * chunkEdgeVoxels];
            ExtractChunkTier0MaterialsInto(chunk, pool, chunkEdgeVoxels, result);
            return result;
        }

        /// Fill-into-caller-buffer form. CLEARS FIRST: the allocating form above
        /// leaned on `new byte[]` being zero-filled for air regions (see the
        /// air/uniform branches and FillBrickRegion's m != 0 skip), and a REUSED
        /// buffer still holds the previous chunk's voxels.
        private static void ExtractChunkTier0MaterialsInto(Chunk chunk, BrickDataPool pool,
                                                           int chunkEdgeVoxels, byte[] result)
        {
            Array.Clear(result, 0, chunkEdgeVoxels * chunkEdgeVoxels * chunkEdgeVoxels);

            if (chunk == null)
            {
                // Unloaded chunk = air throughout, matching ChunkStore.GetVoxel's
                // own "chunk == null -> 0" rule. result[] was cleared above -
                // nothing further to do.
                return;
            }

            if (chunk.isUniform)
            {
                if (chunk.uniformMaterial != 0)
                    Array.Fill(result, chunk.uniformMaterial);
                return;
            }

            const int bricksPerChunkEdge = 16; // Chunk = 16x16x16 tier-0 bricks (§2.3)
            const int brickEdgeVoxels = 8;
            int stride = chunkEdgeVoxels;
            int slice = chunkEdgeVoxels * chunkEdgeVoxels;

            for (int bz = 0; bz < bricksPerChunkEdge; bz++)
            for (int by = 0; by < bricksPerChunkEdge; by++)
            for (int bx = 0; bx < bricksPerChunkEdge; bx++)
            {
                int brickFlatIndex = CoordMath.LocalBrickIndex(new int3(bx, by, bz));
                uint handleData = chunk.bricks[brickFlatIndex].data;
                bool isDense = (handleData & 0x80000000) != 0;

                int originX = bx * brickEdgeVoxels;
                int originY = by * brickEdgeVoxels;
                int originZ = bz * brickEdgeVoxels;

                if (!isDense)
                {
                    byte m = (byte)(handleData & 0xFF);
                    if (m != 0) // air (0): destination region already zero, skip work
                        FillBrickRegion(result, stride, slice, originX, originY, originZ, brickEdgeVoxels, m);
                }
                else
                {
                    int poolIndex = (int)(handleData & 0x3FFFFFFF);
                    CopyBrickRegion(result, stride, slice, originX, originY, originZ, brickEdgeVoxels, pool.RawData, poolIndex * 512);
                }
            }
        }

        private static void FillBrickRegion(byte[] dest, int stride, int slice, int ox, int oy, int oz, int edge, byte value)
        {
            for (int z = 0; z < edge; z++)
            for (int y = 0; y < edge; y++)
            {
                int rowStart = ox + stride * (oy + y) + slice * (oz + z);
                for (int x = 0; x < edge; x++)
                    dest[rowStart + x] = value;
            }
        }

        private static void CopyBrickRegion(byte[] dest, int stride, int slice, int ox, int oy, int oz, int edge, NativeArray<byte> src, int srcOffset)
        {
            int srcIdx = 0;
            for (int z = 0; z < edge; z++)
            for (int y = 0; y < edge; y++)
            {
                int rowStart = ox + stride * (oy + y) + slice * (oz + z);
                for (int x = 0; x < edge; x++)
                    dest[rowStart + x] = src[srcOffset + srcIdx++];
            }
        }

        private static byte SampleFlat(byte[] data, int x, int y, int z, int stride, int slice)
        {
            return data[x + stride * y + slice * z];
        }

        // Manual integer log2 - Unity's Mono/.NET runtime target for this project
        // doesn't have System.Math.Log2 (added in .NET Core 3.0+). Factor is
        // always a small power of two here (LODConfig's constructor already
        // guarantees that), so a shift loop is exact and cheap - no float
        // rounding risk either.
        private static int IntegerLog2(int powerOfTwo)
        {
            if (powerOfTwo <= 0 || (powerOfTwo & (powerOfTwo - 1)) != 0)
                throw new ArgumentException($"IntegerLog2 requires a positive power of two, got {powerOfTwo}.", nameof(powerOfTwo));

            int steps = 0;
            while ((1 << steps) < powerOfTwo) steps++;
            return steps;
        }
    }
}

// ==========================================