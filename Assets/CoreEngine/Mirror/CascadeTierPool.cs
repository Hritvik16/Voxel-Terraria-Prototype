// ==========================================
// Assets/CoreEngine/Mirror/CascadeTierPool.cs
//
// PHASE 4 REVISION. Same two changes as TerrainClipmap, for the same reasons,
// applied to the coarse tiers:
//
//   1. CHUNK-MAJOR coarse clipmap. One chunk contributes
//      coarseBricksPerChunkEdge^3 contiguous entries (512 at tier 1, 64 at
//      tier 2), so a dirty chunk is ONE SetData per tier instead of a scatter.
//      Shader side: ReadClipmapTier in Raymarch.compute matches this.
//
//   2. INCREMENTAL upload. Phase 3 called BrickDataBuffer.SetData(RawData)
//      (96 MB per tier) plus a full coarse-clipmap upload on every flush.
//      PHASE_3_COMPLETION.md §4 measured Cascades.UploadDirty at 3,033 ms and
//      called it "the dominant term". Worth being precise about WHY, because
//      the phase doc's diagnosis was incomplete: cascades moved roughly a
//      THIRD of tier 0's bytes yet took nearly SEVEN TIMES as long, so bus
//      traffic was never the main cost. It was
//      LODDownsampler.DownsampleChunkToTier running over all 484 chunks x 2
//      tiers on every flush -- CPU work, not upload.
//      Streaming fixes that for free (only entering chunks are dirty), and
//      the incremental upload below fixes the remaining third.
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Mirror
{
    public class CascadeTierPool : IDisposable
    {
        public int Tier { get; }

        // Per-call timing, split so the rig can attribute cascade cost instead
        // of reporting one opaque number. Downsampling and GPU writes have
        // different fixes; a merged figure hides which one is binding.
        public double LastDownsampleMs { get; private set; }
        public double LastGpuWriteMs { get; private set; }
        public int LastChunksProcessed { get; private set; }
        public int LastWriteCalls { get; private set; }
        public int DirtyRemaining => _dirtyChunks.Count;
        public GraphicsBuffer ClipmapBuffer { get; private set; }
        public GraphicsBuffer BrickDataBuffer { get; private set; }
        public int3 WindowDimsCoarseBricks => _windowDimsCoarseBricks;
        public int CoarseBricksPerChunkEdge => _coarseBricksPerChunkEdge;
        /// Entries this tier contributes per chunk. The shader needs it to
        /// compute chunkSlot * entriesPerChunk + local.
        public int EntriesPerChunk => _entriesPerChunk;

        private readonly int3 _windowDimsChunks;
        private readonly int3 _chunkMask;
        private readonly int _coarseBricksPerChunkEdge;
        private readonly int _entriesPerChunk;
        private readonly int3 _windowDimsCoarseBricks;
        private readonly uint[] _clipmapLocal;
        private readonly uint[] _chunkStaging;
        private readonly int[] _clipmapCellPoolIndex;

        private readonly BrickDataPool _brickPool;
        private readonly HashSet<int3> _dirtyChunks = new HashSet<int3>();
        private readonly List<int> _dirtyBrickSlots = new List<int>();
        private readonly List<int3> _batch = new List<int3>();
        private readonly List<int3> _evictedScratch = new List<int3>();

        // Reused per-brick scratch. ExtractBrick used to allocate a fresh
        // byte[512] for every coarse brick: 512 at tier 1 + 64 at tier 2 = 576
        // allocations PER CHUNK, ~4.7 MB of garbage per streaming frame at 16
        // admissions, and ~420,000 allocations during the initial window fill.
        // That GC churn, not the downsampling, was the larger share of the
        // first run's stall.
        private readonly byte[] _brickScratch = new byte[512];

        private const int TIER0_BRICK_EDGE_VOXELS = 8;
        private const int CHUNK_EDGE_BRICKS_TIER0 = 16;
        private const int CHUNK_EDGE_VOXELS_TIER0 = CHUNK_EDGE_BRICKS_TIER0 * TIER0_BRICK_EDGE_VOXELS; // 128

        public CascadeTierPool(int tier, int3 windowDimsChunks, int brickPoolCapacity)
        {
            if (tier <= 0 || tier >= LODConfig.TIER_COUNT)
                throw new ArgumentOutOfRangeException(nameof(tier),
                    $"CascadeTierPool is only for non-zero tiers (tier 0 is the existing TerrainClipmap). Got {tier}.");

            Tier = tier;
            _windowDimsChunks = windowDimsChunks;
            _chunkMask = windowDimsChunks - new int3(1, 1, 1);

            int factor = LODConfig.DownsampleFactor(tier);
            _coarseBricksPerChunkEdge = CHUNK_EDGE_VOXELS_TIER0 / factor / TIER0_BRICK_EDGE_VOXELS;
            if (_coarseBricksPerChunkEdge <= 0)
                throw new ArgumentException(
                    $"Tier {tier} downsample factor {factor} leaves fewer than 1 coarse brick per chunk edge.");

            _entriesPerChunk = _coarseBricksPerChunkEdge * _coarseBricksPerChunkEdge * _coarseBricksPerChunkEdge;
            _windowDimsCoarseBricks = windowDimsChunks * _coarseBricksPerChunkEdge;

            int totalCoarseBricks = _windowDimsCoarseBricks.x * _windowDimsCoarseBricks.y * _windowDimsCoarseBricks.z;
            _clipmapLocal = new uint[totalCoarseBricks];
            _chunkStaging = new uint[_entriesPerChunk];
            _clipmapCellPoolIndex = new int[totalCoarseBricks];
            for (int i = 0; i < totalCoarseBricks; i++) _clipmapCellPoolIndex[i] = -1;

            _brickPool = new BrickDataPool(brickPoolCapacity);

            ClipmapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalCoarseBricks, 4);
            BrickDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, brickPoolCapacity * 128, 4);
            ClipmapBuffer.SetData(_clipmapLocal);
        }

        public void MarkDirty(int3 chunkCoord) => _dirtyChunks.Add(chunkCoord);

        /// Flat index of one coarse brick in ClipmapBuffer. The coarse analogue
        /// of TerrainClipmap.GpuIndexOf, exposed for the same reason: a debug
        /// validator cannot address this buffer without reproducing ChunkSlot
        /// and LocalCoarseIndex, and a validator that reimplements the layout it
        /// is checking validates nothing. Passing (coord, 0,0,0) yields the
        /// chunk's slotBase, which is what a per-chunk partial readback needs.
        public int GpuIndexOf(int3 chunkCoord, int bx, int by, int bz)
            => ChunkSlot(chunkCoord) * _entriesPerChunk + LocalCoarseIndex(bx, by, bz);

        /// Whether this chunk is still queued for a cascade upload. Mirrors
        /// TerrainClipmap.IsDirty, and exists for the same diagnostic reason:
        /// it is what separates upload LAG (write queued, budget delayed it)
        /// from a LOST UPDATE (nothing queued, GPU silently stale). Only the
        /// second is a bug.
        public bool IsDirty(int3 chunkCoord) => _dirtyChunks.Contains(chunkCoord);

        /// Accepts a downsampled chunk COMPUTED ON A WORKER THREAD and does only
        /// the cheap remainder here: brick split, pool slot management, GPU
        /// writes -- measured at ~0.3ms against the ~11ms the downsample itself
        /// costs. This is how admission stops paying the downsample on the main
        /// thread at all; the budgeted UploadDirty path remains for evictions
        /// (clear, no downsample) and edits (rare, re-downsampled here).
        private bool _batchlessSubmit;
        public void SubmitPrecomputed(int3 chunkCoord, byte[] downsampled)
        {
            _dirtyChunks.Remove(chunkCoord); // superseded by fresher data
            int downsampledEdge = 128 / LODConfig.DownsampleFactor(Tier);
            _batchlessSubmit = true;
            try { WriteChunkFromDownsampled(chunkCoord, downsampled, downsampledEdge); }
            finally { _batchlessSubmit = false; }
        }

        private int ChunkSlot(int3 chunkCoord)
        {
            int3 w = chunkCoord & _chunkMask;
            return w.x + _windowDimsChunks.x * (w.y + _windowDimsChunks.y * w.z);
        }

        /// Local index of a coarse brick within its chunk, x fastest -- the
        /// coarse analogue of CoordMath.LocalBrickIndex, and the shader
        /// reproduces exactly this.
        private int LocalCoarseIndex(int bx, int by, int bz)
            => bx + _coarseBricksPerChunkEdge * (by + _coarseBricksPerChunkEdge * bz);

        public void UploadDirty(ChunkStore store, BrickDataPool pool)
        {
            if (_dirtyChunks.Count == 0) return;

            int downsampledEdge = CHUNK_EDGE_VOXELS_TIER0 / LODConfig.DownsampleFactor(Tier);
            // const int coarseBrickEdge = TIER0_BRICK_EDGE_VOXELS;

            _dirtyBrickSlots.Clear();

            // BUDGETED. Phase 3 measured DownsampleChunkToTier at ~3ms per
            // chunk-tier (3,033ms / 484 chunks / 2 tiers), so flushing a
            // streaming frame's admissions unthrottled costs tens of ms before
            // any GPU work happens -- and the initial window fill (729 chunks x
            // 2 tiers) cost seconds in a single call.
            //
            // Leftovers stay dirty and are picked up next frame. The visible
            // effect is distant terrain resolving a few frames late, which is
            // exactly the trade §3.7 makes for the tier-0 clipmap already.
            // PASS 1 -- EVICTED CHUNKS, UNBUDGETED.
            //
            // A clear is a memset of _entriesPerChunk plus one SetData. It does
            // NOT pay the ~5ms DownsampleChunkToTier that the budget below
            // exists to bound (see the comment on it) -- so throttling clears
            // behind admissions spends the frame budget on the expensive work
            // and starves the cheap work that maintains a correctness
            // invariant.
            //
            // Measured consequence, CascadeValidator's evicted-slot sweep at
            // Gate D: 63 non-resident chunks still held coarse entries pointing
            // at live pool slots -- 8064 stale entries on tier 1, 1008 on tier
            // 2 -- i.e. dense geometry hanging in the window where a chunk used
            // to be. That is the distant phantom geometry the eviction branch
            // below was already written to prevent; it simply never got a turn.
            //
            // Correctness work that is O(memset) does not belong behind a
            // budget sized for O(downsample) work.
            _evictedScratch.Clear();
            foreach (int3 c in _dirtyChunks)
                if (store.GetChunk(c) == null) _evictedScratch.Add(c);

            foreach (int3 c in _evictedScratch)
            {
                _dirtyChunks.Remove(c);
                ClearChunkEntries(c);
                LastChunksProcessed++;
            }

            // PASS 2 -- resident chunks, budgeted as before.
            _batch.Clear();
            foreach (int3 c in _dirtyChunks)
            {
                if (_batch.Count >= EngineConfig.MAX_CASCADE_CHUNKS_PER_FRAME) break;
                _batch.Add(c);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            LastDownsampleMs = 0; LastGpuWriteMs = 0; LastChunksProcessed = 0; LastWriteCalls = 0;
            double phase = 0;

            foreach (int3 chunkCoord in _batch)
            {
                // Wall-clock guard on top of the chunk cap. At least one chunk
                // always processes (LastChunksProcessed check) so the queue
                // cannot stall; beyond that, a frame that is already spent stops
                // paying.
                if (LastChunksProcessed > 0 &&
                    sw.Elapsed.TotalMilliseconds > EngineConfig.MAX_CASCADE_MS_PER_TIER)
                    break;

                _dirtyChunks.Remove(chunkCoord);
                LastChunksProcessed++;

                // Evicted chunk: CLEAR its coarse entries rather than skipping.
                // Same bug as the tier-0 clipmap -- a skipped evicted chunk
                // leaves the GPU describing terrain that no longer exists, and
                // at cascade range that shows up as distant phantom geometry.
                if (store.GetChunk(chunkCoord) == null)
                {
                    ClearChunkEntries(chunkCoord);
                    continue;
                }

                phase = sw.Elapsed.TotalMilliseconds;
                byte[] downsampled = LODDownsampler.DownsampleChunkToTier(store, pool, chunkCoord, Tier);
                LastDownsampleMs += sw.Elapsed.TotalMilliseconds - phase;

                phase = sw.Elapsed.TotalMilliseconds;
                WriteChunkFromDownsampled(chunkCoord, downsampled, downsampledEdge);
                LastWriteCalls++;
                LastGpuWriteMs += sw.Elapsed.TotalMilliseconds - phase;
            }

            phase = sw.Elapsed.TotalMilliseconds;
            UploadDirtyBrickBodies();
            LastGpuWriteMs += sw.Elapsed.TotalMilliseconds - phase;
        }

        private void WriteChunkFromDownsampled(int3 chunkCoord, byte[] downsampled, int downsampledEdge)
        {
            const int coarseBrickEdge = TIER0_BRICK_EDGE_VOXELS;
            int slotBase = ChunkSlot(chunkCoord) * _entriesPerChunk;

            {
                for (int bz = 0; bz < _coarseBricksPerChunkEdge; bz++)
                for (int by = 0; by < _coarseBricksPerChunkEdge; by++)
                for (int bx = 0; bx < _coarseBricksPerChunkEdge; bx++)
                {
                    byte[] brickVoxels = ExtractBrick(downsampled, downsampledEdge,
                        bx * coarseBrickEdge, by * coarseBrickEdge, bz * coarseBrickEdge,
                        coarseBrickEdge, _brickScratch);
                    bool uniform = IsUniform(brickVoxels);

                    int local = LocalCoarseIndex(bx, by, bz);
                    int flatIndex = slotBase + local;

                    // Free the slot this cell previously held. Missing this
                    // leaks a pool slot on every re-dirty -- the same
                    // eviction-spiral shape §3.10 documents for fluid.
                    int staleIndex = _clipmapCellPoolIndex[flatIndex];
                    if (staleIndex >= 0)
                    {
                        _brickPool.Free(staleIndex);
                        _clipmapCellPoolIndex[flatIndex] = -1;
                    }

                    uint handle;
                    if (uniform)
                    {
                        handle = brickVoxels[0];
                    }
                    else
                    {
                        int poolIndex = _brickPool.Alloc();
                        NativeArray<byte> raw = _brickPool.RawData;
                        int offset = poolIndex * 512;
                        for (int i = 0; i < 512; i++) raw[offset + i] = brickVoxels[i];

                        handle = 0x80000000u | (uint)poolIndex;
                        _clipmapCellPoolIndex[flatIndex] = poolIndex;
                        _dirtyBrickSlots.Add(poolIndex);
                    }

                    _clipmapLocal[flatIndex] = handle;
                    _chunkStaging[local] = handle;
                }

                ClipmapBuffer.SetData(_chunkStaging, 0, slotBase, _entriesPerChunk);
            }
            // brick bodies for this chunk ride the shared dirty-slot list and
            // are flushed by the caller's UploadDirtyBrickBodies pass (UploadDirty)
            // or immediately below (SubmitPrecomputed).
            if (_dirtyBrickSlots.Count > 0 && _batchlessSubmit) UploadDirtyBrickBodies();
            // NOTE: _dirtyChunks is NOT cleared here -- entries are removed as
            // they are processed above, so anything left over is genuinely
            // still pending and must survive to the next frame.
        }

        /// Writes uniform air over a chunk's coarse entries and frees the pool
        /// slots they held. Used when a dirty chunk turns out to be evicted.
        private void ClearChunkEntries(int3 chunkCoord)
        {
            int slotBase = ChunkSlot(chunkCoord) * _entriesPerChunk;
            for (int i = 0; i < _entriesPerChunk; i++)
            {
                int flatIndex = slotBase + i;
                int stale = _clipmapCellPoolIndex[flatIndex];
                if (stale >= 0)
                {
                    _brickPool.Free(stale);
                    _clipmapCellPoolIndex[flatIndex] = -1;
                }
                _clipmapLocal[flatIndex] = 0u;
                _chunkStaging[i] = 0u;
            }
            ClipmapBuffer.SetData(_chunkStaging, 0, slotBase, _entriesPerChunk);
        }

        private void UploadDirtyBrickBodies()
        {
            if (_dirtyBrickSlots.Count == 0) return;
            _dirtyBrickSlots.Sort();

            NativeArray<uint> asUints = _brickPool.RawData.Reinterpret<uint>(sizeof(byte));

            int i = 0;
            while (i < _dirtyBrickSlots.Count)
            {
                int runStart = _dirtyBrickSlots[i];
                int runEnd = runStart;
                i++;
                while (i < _dirtyBrickSlots.Count &&
                       (_dirtyBrickSlots[i] == runEnd || _dirtyBrickSlots[i] == runEnd + 1))
                { runEnd = _dirtyBrickSlots[i]; i++; }

                int firstUint = runStart * 128;
                int countUints = (runEnd - runStart + 1) * 128;
                BrickDataBuffer.SetData(asUints, firstUint, firstUint, countUints);
                LastWriteCalls++;
            }
            _dirtyBrickSlots.Clear();
        }

        private static byte[] ExtractBrick(byte[] source, int sourceEdge, int originX, int originY, int originZ,
                                           int brickEdge, byte[] result)
        {
            int stride = sourceEdge;
            int slice = sourceEdge * sourceEdge;
            int idx = 0;
            for (int z = 0; z < brickEdge; z++)
            for (int y = 0; y < brickEdge; y++)
            for (int x = 0; x < brickEdge; x++)
            {
                int sx = originX + x, sy = originY + y, sz = originZ + z;
                result[idx++] = source[sx + stride * sy + slice * sz];
            }
            return result;
        }

        private static bool IsUniform(byte[] brickVoxels)
        {
            byte first = brickVoxels[0];
            for (int i = 1; i < brickVoxels.Length; i++)
                if (brickVoxels[i] != first) return false;
            return true;
        }

        public void Dispose()
        {
            ClipmapBuffer?.Release();
            BrickDataBuffer?.Release();
            _brickPool?.Dispose();
        }
    }
}