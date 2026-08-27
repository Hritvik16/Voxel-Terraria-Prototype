// Assets/CoreEngine/Mirror/CascadeTierPool.cs
//
// §6.4: "LOD1-2 populated by majority-vote downsampling into flat cascade
// pools at chunk load." This is one cascade pool, for ONE non-zero tier.
// Structurally this is TerrainClipmap's own pattern (a flat toroidal clipmap
// of brick handles + a BrickDataPool of dense bodies), just re-parameterized:
// a "brick" here is 8x8x8 voxels of THIS TIER's (coarser) voxel size, not
// tier 0's. BrickDataPool itself needed zero changes to be reused - it's
// already just "N slots x 512 bytes," with no assumption baked in about what
// a byte represents.
//
// MEMORY STATUS: brickPoolCapacity is a caller-supplied placeholder, NOT a
// measured number. ARCHITECTURE_v8.6.md §11.3 lists "LOD Cascade Pools (3
// tiers, not 5)" as "[Phase 2 gate]" - explicitly an open measurement, not a
// derived budget line. Per §11.3's own stated philosophy ("sized aggressively
// low first, raised only if measurement allows"), whoever constructs this
// should start low and raise only with real data. This file does not choose
// that number - see LODCascadeManager for where the current placeholder is
// set, flagged the same way.
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

        private readonly int3 _windowDimsChunks;
        private readonly int _coarseBricksPerChunkEdge;
        private readonly int3 _windowDimsCoarseBricks;
        private readonly int3 _coarseBrickMask;
        private readonly uint[] _clipmapLocal;

        // Which BrickDataPool slot (if any) currently backs each clipmap cell.
        // -1 = cell is uniform, no pool slot in use. Tracked so re-downsampling
        // a dirty chunk frees its OLD dense allocation before writing the new
        // one - without this, every re-dirty of a previously-dense cell leaks
        // a pool slot permanently (the same eviction-spiral shape as the bug
        // documented in ARCHITECTURE_v8.6.md's "What Changed in 8.2" §1, just
        // for cascade pools instead of fluid).
        private readonly int[] _clipmapCellPoolIndex;

        private readonly BrickDataPool _brickPool;
        private readonly HashSet<int3> _dirtyChunks = new HashSet<int3>();

        private const int TIER0_BRICK_EDGE_VOXELS = 8;   // matches CoordMath's brick shape, all tiers
        private const int CHUNK_EDGE_BRICKS_TIER0 = 16;   // Chunk = 16x16x16 tier-0 bricks (§2.3)
        private const int CHUNK_EDGE_VOXELS_TIER0 = CHUNK_EDGE_BRICKS_TIER0 * TIER0_BRICK_EDGE_VOXELS; // 128

        public CascadeTierPool(int tier, int3 windowDimsChunks, int brickPoolCapacity)
        {
            if (tier <= 0 || tier >= LODConfig.TIER_COUNT)
                throw new ArgumentOutOfRangeException(nameof(tier),
                    $"CascadeTierPool is only for non-zero tiers (tier 0 is the existing TerrainClipmap). Got {tier}.");

            Tier = tier;
            _windowDimsChunks = windowDimsChunks;

            int factor = LODConfig.DownsampleFactor(tier);
            // Coarse bricks per chunk edge = (downsampled chunk edge in this
            // tier's voxels) / 8. E.g. tier1 (factor 2): 128/2/8 = 8. tier2
            // (factor 4): 128/4/8 = 4.
            _coarseBricksPerChunkEdge = CHUNK_EDGE_VOXELS_TIER0 / factor / TIER0_BRICK_EDGE_VOXELS;
            if (_coarseBricksPerChunkEdge <= 0)
                throw new ArgumentException(
                    $"Tier {tier} downsample factor {factor} leaves fewer than 1 coarse brick per chunk edge - tier voxel size too large for a 12.8m chunk.");

            _windowDimsCoarseBricks = windowDimsChunks * _coarseBricksPerChunkEdge;
            _coarseBrickMask = _windowDimsCoarseBricks - new int3(1, 1, 1);

            int totalCoarseBricks = _windowDimsCoarseBricks.x * _windowDimsCoarseBricks.y * _windowDimsCoarseBricks.z;
            _clipmapLocal = new uint[totalCoarseBricks]; // default 0 = uniform, material 0 (air) - matches window-not-yet-loaded state
            _clipmapCellPoolIndex = new int[totalCoarseBricks];
            for (int i = 0; i < totalCoarseBricks; i++) _clipmapCellPoolIndex[i] = -1;

            _brickPool = new BrickDataPool(brickPoolCapacity);

            ClipmapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalCoarseBricks, 4);
            BrickDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, brickPoolCapacity * 128, 4);

            ClipmapBuffer.SetData(_clipmapLocal);
        }

        public void MarkDirty(int3 chunkCoord) => _dirtyChunks.Add(chunkCoord);

        public void UploadDirty(ChunkStore store, BrickDataPool pool)
        {
            if (_dirtyChunks.Count == 0) return;

            bool poolChanged = false;
            int downsampledEdge = CHUNK_EDGE_VOXELS_TIER0 / LODConfig.DownsampleFactor(Tier);
            int coarseBrickEdge = TIER0_BRICK_EDGE_VOXELS;

            foreach (int3 chunkCoord in _dirtyChunks)
            {
                byte[] downsampled = LODDownsampler.DownsampleChunkToTier(store, pool, chunkCoord, Tier);
                int3 baseCoarseBrick = chunkCoord * _coarseBricksPerChunkEdge;

                for (int bz = 0; bz < _coarseBricksPerChunkEdge; bz++)
                for (int by = 0; by < _coarseBricksPerChunkEdge; by++)
                for (int bx = 0; bx < _coarseBricksPerChunkEdge; bx++)
                {
                    byte[] brickVoxels = ExtractBrick(downsampled, downsampledEdge, bx * coarseBrickEdge, by * coarseBrickEdge, bz * coarseBrickEdge, coarseBrickEdge);
                    bool uniform = IsUniform(brickVoxels);

                    int3 worldCoarseBrick = baseCoarseBrick + new int3(bx, by, bz);
                    int3 wrapped = worldCoarseBrick & _coarseBrickMask;
                    int flatIndex = wrapped.x + _windowDimsCoarseBricks.x * (wrapped.y + _windowDimsCoarseBricks.y * wrapped.z);

                    int staleIndex = _clipmapCellPoolIndex[flatIndex];
                    if (staleIndex >= 0)
                    {
                        _brickPool.Free(staleIndex);
                        _clipmapCellPoolIndex[flatIndex] = -1;
                        poolChanged = true;
                    }

                    if (uniform)
                    {
                        _clipmapLocal[flatIndex] = brickVoxels[0]; // bit31 = 0 (uniform), material in low byte
                    }
                    else
                    {
                        int poolIndex = _brickPool.Alloc();
                        NativeArray<byte> raw = _brickPool.RawData;
                        int offset = poolIndex * 512;
                        for (int i = 0; i < 512; i++) raw[offset + i] = brickVoxels[i];

                        _clipmapLocal[flatIndex] = 0x80000000u | (uint)poolIndex;
                        _clipmapCellPoolIndex[flatIndex] = poolIndex;
                        poolChanged = true;
                    }
                }
            }

            ClipmapBuffer.SetData(_clipmapLocal);
            // Full re-upload on any change, matching TerrainClipmap.UploadDirty's
            // own documented tradeoff (§ comment there): Phase 2's world is
            // static, so this is rare; incremental (dirty-slot-only) upload is
            // the obvious later optimization if edit-heavy frames ever profile
            // hot here.
            if (poolChanged) BrickDataBuffer.SetData(_brickPool.RawData);

            _dirtyChunks.Clear();
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