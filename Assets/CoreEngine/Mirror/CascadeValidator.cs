// ==========================================
// Assets/CoreEngine/Mirror/CascadeValidator.cs
//
// The cascade-tier analogue of ClipmapValidator. It exists because of a
// specific, observed gap:
//
//   ClipmapValidator checks TIER 0 ONLY (TerrainClipmap). It reported GREEN
//   through every Phase 4 run while the Gate D "after persistence" screenshot
//   showed floating slabs and a hole in the water AT THE HORIZON -- i.e. at
//   distance, which is exactly where LODConfig hands rendering over to cascade
//   tiers 1 and 2. Nothing in the codebase validated those two buffers against
//   anything, so "tier 0 is byte-exact" was being read as "the mirror is
//   correct" when it only ever meant "the near field is correct."
//
// GROUND TRUTH IS RECOMPUTED, NOT REMEMBERED. For each resident chunk this
// re-runs LODDownsampler.DownsampleChunkToTier from CURRENT ChunkStore state
// and re-derives what the coarse entries ought to be, then diffs that against
// what is actually sitting in the tier's GPU buffers. It deliberately does not
// consult CascadeTierPool's own _clipmapLocal shadow: that shadow is written by
// the same code path under test, so agreeing with it would prove nothing. The
// only inputs trusted here are ChunkStore, the tier-0 BrickDataPool, and the
// GPU.
//
// WHY DENSE HANDLES ARE NOT COMPARED BY VALUE. A dense coarse entry is
// 0x80000000 | poolIndex, and that pool index is an allocation artifact --
// re-running the downsample cannot predict which slot the pool happened to
// hand out, and it would differ harmlessly on every run. So the handle is
// compared SEMANTICALLY: uniform-vs-dense classification, the material byte
// for uniform cells, and for dense cells the 512 body bytes are followed
// through the GPU's own slot number into the tier's BrickDataBuffer and
// compared there. A wrong slot therefore still fails -- it fails on the bytes,
// which is the thing that actually reaches the screen.
//
// COST: re-downsamples every chunk it checks (~5ms each) and forces GPU syncs.
// Debug-only per §10.4. Never call inside a timing measurement, and keep
// maxChunks small.
using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public static class CascadeValidator
{
    private const int BODY_CHECK_CHUNK_LIMIT = 4;
    private const int BODY_CHECK_SLOT_LIMIT = 8192;   // ~4 MB readback ceiling
    private const int MAX_SAMPLES = 12;

    public enum MismatchKind
    {
        CpuUniformGpuDense,       // fresh downsample says uniform; GPU holds a dense handle
        CpuDenseGpuUniform,       // fresh downsample says dense; GPU collapsed it to uniform
        BothUniformDifferentMat,  // both uniform, different material byte
        DenseBodyBytesDiffer,     // both dense, but the GPU's body bytes are wrong
        StaleEvictedEntry,        // GPU still describes terrain for a non-resident chunk
        Other,
    }

    public struct Sample
    {
        public int tier;
        public int3 chunkCoord;
        public int3 coarseBrick;
        public uint cpu, gpu;
        public MismatchKind kind;
        public bool chunkWasDirty;

        public override string ToString() =>
            $"tier {tier} chunk {chunkCoord} coarse {coarseBrick}: " +
            $"CPU 0x{cpu:X8} GPU 0x{gpu:X8} [{kind}] stillDirty={chunkWasDirty}";
    }

    public struct Result
    {
        public bool pass;
        public int tier;
        public int chunksChecked;
        public int chunksWithMismatch;
        public int entryMismatches;
        public int bodyByteMismatches;

        /// Mismatches in chunks STILL QUEUED for a cascade upload -- lag, not
        /// corruption. Expected to be nonzero while the window is moving.
        public int mismatchesInDirtyChunks;

        /// Mismatches in chunks with NOTHING queued. Every one is a lost update
        /// and a real bug. This is the number that matters.
        public int mismatchesInCleanChunks;

        public Dictionary<MismatchKind, int> byKind;
        public List<Sample> samples;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"tier {tier}: ").Append(pass ? "GREEN" : "RED").Append(". ");
            sb.Append($"{chunksChecked} chunks checked, {chunksWithMismatch} with mismatches, ");
            sb.Append($"{entryMismatches} entry + {bodyByteMismatches} body mismatches.");
            if (!pass)
            {
                sb.AppendLine();
                sb.AppendLine($"      upload LAG (chunk still queued): {mismatchesInDirtyChunks}");
                sb.AppendLine($"      LOST UPDATES (not queued):       {mismatchesInCleanChunks}   <-- real bugs");
                if (byKind != null)
                    foreach (var kv in byKind)
                        sb.AppendLine($"        {kv.Key}: {kv.Value}");
                if (samples != null)
                    foreach (var smp in samples) sb.AppendLine("        " + smp);
            }
            return sb.ToString();
        }
    }

    /// Validates every non-zero tier held by the manager. Returns one Result per
    /// tier, in tier order.
    public static List<Result> ValidateAllTiers(LODCascadeManager cascades, ChunkStore store,
        BrickDataPool cpuPool, int maxChunks = 12, bool validateBodies = true)
    {
        var results = new List<Result>();
        if (cascades == null) return results;

        for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
            results.Add(ValidateTier(cascades.TierPool(tier), store, cpuPool, maxChunks, validateBodies));

        return results;
    }

    /// Sweeps EVERY chunk slot in the tier window, not just resident ones, and
    /// asserts that slots whose chunk is NOT resident read as uniform air.
    ///
    /// This exists because ValidateTier below has a structural blind spot: it
    /// walks store.ResidentChunks(), so a coarse entry left describing terrain
    /// for a chunk that has since been EVICTED is invisible to it. That is
    /// exactly the shape of the observed Gate D artifact -- a slab hanging over
    /// open ocean at distance, i.e. where the window has been sliding and
    /// evicting. CascadeTierPool.ClearChunkEntries is supposed to write uniform
    /// air over an evicted chunk's entries; nothing had ever checked that it
    /// does.
    ///
    /// Bounded by construction: reads one chunk's entries at a time and stops
    /// after maxSlots, so the cost is a partial readback per slot, not the whole
    /// 33 MB tier buffer.
    public static Result ValidateEvictedSlots(CascadeTierPool pool, ChunkStore store,
        int maxSlots = 4096)
    {
        var result = new Result
        {
            pass = true,
            tier = pool.Tier,
            byKind = new Dictionary<MismatchKind, int>(),
            samples = new List<Sample>(),
        };

        int cbpe = pool.CoarseBricksPerChunkEdge;
        int entriesPerChunk = pool.EntriesPerChunk;
        var gpuEntries = new uint[entriesPerChunk];

        int3 windowChunks = pool.WindowDimsCoarseBricks / cbpe;
        int3 origin = store.WindowOrigin;
        int slotsChecked = 0;

        for (int cz = 0; cz < windowChunks.z && slotsChecked < maxSlots; cz++)
        for (int cy = 0; cy < windowChunks.y && slotsChecked < maxSlots; cy++)
        for (int cx = 0; cx < windowChunks.x && slotsChecked < maxSlots; cx++)
        {
            int3 chunkCoord = origin + new int3(cx, cy, cz);
            if (store.IsResident(chunkCoord)) continue;   // covered by ValidateTier
            slotsChecked++;
            result.chunksChecked++;

            bool dirty = pool.IsDirty(chunkCoord);
            bool had = false;

            pool.ClipmapBuffer.GetData(gpuEntries, 0, pool.GpuIndexOf(chunkCoord, 0, 0, 0), entriesPerChunk);

            for (int i = 0; i < entriesPerChunk; i++)
            {
                // A non-resident chunk reads as air on the CPU, so its coarse
                // entries must be exactly 0 (uniform air) -- the same agreement
                // TerrainClipmap's evicted-chunk clear maintains for tier 0.
                if (gpuEntries[i] == 0u) continue;

                result.entryMismatches++;
                had = true;
                if (dirty) result.mismatchesInDirtyChunks++;
                else result.mismatchesInCleanChunks++;

                result.byKind.TryGetValue(MismatchKind.StaleEvictedEntry, out int n);
                result.byKind[MismatchKind.StaleEvictedEntry] = n + 1;

                if (result.samples.Count < MAX_SAMPLES)
                {
                    int bx = i % cbpe, by = (i / cbpe) % cbpe, bz = i / (cbpe * cbpe);
                    result.samples.Add(new Sample
                    {
                        tier = pool.Tier, chunkCoord = chunkCoord, coarseBrick = new int3(bx, by, bz),
                        cpu = 0u, gpu = gpuEntries[i],
                        kind = MismatchKind.StaleEvictedEntry, chunkWasDirty = dirty,
                    });
                }
            }

            if (had) result.chunksWithMismatch++;
        }

        result.pass = result.entryMismatches == 0;
        if (result.pass) Debug.Log("[CascadeValidator/evicted] " + result.Describe());
        else Debug.LogError("[CascadeValidator/evicted] " + result.Describe());
        return result;
    }

    public static Result ValidateTier(CascadeTierPool pool, ChunkStore store, BrickDataPool cpuPool,
        int maxChunks = 12, bool validateBodies = true)
    {
        var result = new Result
        {
            pass = true,
            tier = pool.Tier,
            byKind = new Dictionary<MismatchKind, int>(),
            samples = new List<Sample>(),
        };

        int factor = LODConfig.DownsampleFactor(pool.Tier);
        int dsEdge = EngineConfig.CHUNK_EDGE_VOXELS / factor;   // downsampled voxels per chunk edge
        int cbpe = pool.CoarseBricksPerChunkEdge;
        int entriesPerChunk = pool.EntriesPerChunk;

        var gpuEntries = new uint[entriesPerChunk];
        var brickScratch = new byte[512];

        // Dense cells found this pass, kept for the bounded body check below.
        var denseChecks = new List<(int3 chunkCoord, int3 coarseBrick, int slot, byte[] expected, bool dirty)>();
        int bodyChunksQueued = 0;

        foreach (Chunk chunk in store.ResidentChunks())
        {
            if (result.chunksChecked >= maxChunks) break;
            result.chunksChecked++;

            bool dirty = pool.IsDirty(chunk.coord);
            bool chunkHadMismatch = false;

            // Ground truth, recomputed from live CPU state.
            byte[] fresh = LODDownsampler.DownsampleChunkToTier(chunk, cpuPool, pool.Tier);

            pool.ClipmapBuffer.GetData(gpuEntries, 0, pool.GpuIndexOf(chunk.coord, 0, 0, 0), entriesPerChunk);

            bool wantBodies = validateBodies && bodyChunksQueued < BODY_CHECK_CHUNK_LIMIT;
            bool chunkContributedDense = false;

            for (int bz = 0; bz < cbpe; bz++)
            for (int by = 0; by < cbpe; by++)
            for (int bx = 0; bx < cbpe; bx++)
            {
                ExtractBrick(fresh, dsEdge, bx * 8, by * 8, bz * 8, 8, brickScratch);
                bool expectUniform = IsUniform(brickScratch);
                byte expectMat = brickScratch[0];

                int local = bx + cbpe * (by + cbpe * bz);   // matches CascadeTierPool.LocalCoarseIndex
                uint gpu = gpuEntries[local];
                bool gpuDense = (gpu & 0x80000000u) != 0;

                MismatchKind kind;
                if (expectUniform && gpuDense) kind = MismatchKind.CpuUniformGpuDense;
                else if (!expectUniform && !gpuDense) kind = MismatchKind.CpuDenseGpuUniform;
                else if (expectUniform && (gpu & 0xFFu) != expectMat) kind = MismatchKind.BothUniformDifferentMat;
                else
                {
                    // Both dense and both agree structurally: the bytes decide.
                    if (!expectUniform && wantBodies)
                    {
                        denseChecks.Add((chunk.coord, new int3(bx, by, bz),
                                         (int)(gpu & 0x3FFFFFFFu), (byte[])brickScratch.Clone(), dirty));
                        chunkContributedDense = true;
                    }
                    continue;
                }

                result.entryMismatches++;
                chunkHadMismatch = true;
                if (dirty) result.mismatchesInDirtyChunks++;
                else result.mismatchesInCleanChunks++;

                result.byKind.TryGetValue(kind, out int n);
                result.byKind[kind] = n + 1;

                if (result.samples.Count < MAX_SAMPLES)
                    result.samples.Add(new Sample
                    {
                        tier = pool.Tier, chunkCoord = chunk.coord, coarseBrick = new int3(bx, by, bz),
                        cpu = expectUniform ? expectMat : 0x80000000u, gpu = gpu,
                        kind = kind, chunkWasDirty = dirty,
                    });
            }

            if (chunkContributedDense) bodyChunksQueued++;
            if (chunkHadMismatch) result.chunksWithMismatch++;
        }

        if (denseChecks.Count > 0) ValidateBodies(pool, denseChecks, ref result);

        result.pass = result.entryMismatches == 0 && result.bodyByteMismatches == 0;
        if (result.pass) Debug.Log("[CascadeValidator] " + result.Describe());
        else Debug.LogError("[CascadeValidator] " + result.Describe());
        return result;
    }

    /// Follows each dense cell's GPU slot into the tier's BrickDataBuffer and
    /// byte-compares against the freshly downsampled brick. One contiguous
    /// range readback, same bounding strategy as ClipmapValidator.
    private static void ValidateBodies(CascadeTierPool pool,
        List<(int3 chunkCoord, int3 coarseBrick, int slot, byte[] expected, bool dirty)> denseChecks,
        ref Result result)
    {
        int minSlot = int.MaxValue, maxSlot = -1;
        foreach (var d in denseChecks)
        {
            if (d.slot < minSlot) minSlot = d.slot;
            if (d.slot > maxSlot) maxSlot = d.slot;
        }
        if (maxSlot < 0) return;

        int slotCount = maxSlot - minSlot + 1;
        if (slotCount > BODY_CHECK_SLOT_LIMIT) return;   // don't degenerate into a full readback

        var gpuRange = new uint[slotCount * 128];
        pool.BrickDataBuffer.GetData(gpuRange, 0, minSlot * 128, slotCount * 128);

        foreach (var d in denseChecks)
        {
            int gpuByteBase = (d.slot - minSlot) * 512;
            for (int v = 0; v < 512; v++)
            {
                int gb = gpuByteBase + v;
                byte g = (byte)((gpuRange[gb >> 2] >> ((gb & 3) * 8)) & 0xFF);
                if (g == d.expected[v]) continue;

                result.bodyByteMismatches++;
                if (d.dirty) result.mismatchesInDirtyChunks++;
                else result.mismatchesInCleanChunks++;

                result.byKind.TryGetValue(MismatchKind.DenseBodyBytesDiffer, out int n);
                result.byKind[MismatchKind.DenseBodyBytesDiffer] = n + 1;

                if (result.samples.Count < MAX_SAMPLES)
                    result.samples.Add(new Sample
                    {
                        tier = pool.Tier, chunkCoord = d.chunkCoord, coarseBrick = d.coarseBrick,
                        cpu = d.expected[v], gpu = g,
                        kind = MismatchKind.DenseBodyBytesDiffer, chunkWasDirty = d.dirty,
                    });
                break;   // one report per brick is enough
            }
        }
    }

    // Byte-for-byte the same extraction CascadeTierPool.WriteChunkFromDownsampled
    // performs. Duplicated rather than shared on purpose: this is the oracle, and
    // an oracle that calls into the code under test cannot detect that code being
    // wrong. If these two ever disagree, that disagreement IS the finding.
    private static void ExtractBrick(byte[] source, int sourceEdge,
        int originX, int originY, int originZ, int brickEdge, byte[] result)
    {
        int stride = sourceEdge;
        int slice = sourceEdge * sourceEdge;
        int idx = 0;
        for (int z = 0; z < brickEdge; z++)
        for (int y = 0; y < brickEdge; y++)
        for (int x = 0; x < brickEdge; x++)
            result[idx++] = source[(originX + x) + stride * (originY + y) + slice * (originZ + z)];
    }

    private static bool IsUniform(byte[] brickVoxels)
    {
        byte first = brickVoxels[0];
        for (int i = 1; i < brickVoxels.Length; i++)
            if (brickVoxels[i] != first) return false;
        return true;
    }
}
