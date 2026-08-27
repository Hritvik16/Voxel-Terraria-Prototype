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
            const int coarseBrickEdge = TIER0_BRICK_EDGE_VOXELS;

            _dirtyBrickSlots.Clear();

            foreach (int3 chunkCoord in _dirtyChunks)
            {
                if (store.GetChunk(chunkCoord) == null) continue;

                byte[] downsampled = LODDownsampler.DownsampleChunkToTier(store, pool, chunkCoord, Tier);
                int slotBase = ChunkSlot(chunkCoord) * _entriesPerChunk;

                for (int bz = 0; bz < _coarseBricksPerChunkEdge; bz++)
                for (int by = 0; by < _coarseBricksPerChunkEdge; by++)
                for (int bx = 0; bx < _coarseBricksPerChunkEdge; bx++)
                {
                    byte[] brickVoxels = ExtractBrick(downsampled, downsampledEdge,
                        bx * coarseBrickEdge, by * coarseBrickEdge, bz * coarseBrickEdge, coarseBrickEdge);
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

            UploadDirtyBrickBodies();
            _dirtyChunks.Clear();
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
            }
            _dirtyBrickSlots.Clear();
        }

        private static byte[] ExtractBrick(byte[] source, int sourceEdge, int originX, int originY, int originZ, int brickEdge)
        {
            byte[] result = new byte[brickEdge * brickEdge * brickEdge];
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