// Assets/CoreEngine/Mirror/TerrainClipmap.cs
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public class TerrainClipmap : IDisposable
{
    public static TerrainClipmap Active { get; private set; }

    public GraphicsBuffer ClipmapBuffer { get; private set; }
    public GraphicsBuffer BrickDataBuffer { get; private set; }

    private readonly int3 _windowDimsChunks;
    private readonly int3 _windowDimsBricks;
    private readonly int3 _brickMask;
    private readonly uint[] _clipmapLocal;
    private readonly HashSet<int3> _dirtyChunks = new HashSet<int3>();

    public static bool UseLockBufferForAirMip = false;

    public const int NUM_AIR_MIP_LEVELS = 4;
    private AirMipData _mips;
    private GraphicsBuffer[] _airMipBuffers;        // legacy: one 4-byte-per-cell buffer per level
    public AirMipData Mips => _mips;
    public GraphicsBuffer AirMipBuffer(int oneBasedLevel) => _airMipBuffers[oneBasedLevel - 1];
    public int AirMipLevelCount => _mips != null ? _mips.NumLevels : 0;

    // --- PACKED + MERGED AIR-MIP (optimization pass) ---
    // One buffer, all levels, 1 bit per cell. ~1.2 MB total vs ~38 MB for the
    // legacy four-buffer form, which puts the whole pyramid inside the M1's
    // 8 MB system-level cache. Kept ALONGSIDE the legacy buffers so the two
    // can be A/B'd in the same build - the extra 1.2 MB is negligible.
    private AirMip.PackedMips _packed;
    public GraphicsBuffer AirMipPackedBuffer { get; private set; }
    public AirMip.PackedMips Packed => _packed;

    public int3 WindowDimsBricks => _windowDimsBricks;
    public int3 WindowDimsChunks => _windowDimsChunks;

    public TerrainClipmap(int3 windowChunks, int brickPoolCapacity)
    {
        _windowDimsChunks = windowChunks;
        _windowDimsBricks = windowChunks * 16;
        _brickMask = _windowDimsBricks - new int3(1, 1, 1);

        int totalBricks = _windowDimsBricks.x * _windowDimsBricks.y * _windowDimsBricks.z;

        ClipmapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalBricks, 4);
        BrickDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, brickPoolCapacity * 128, 4);
        _clipmapLocal = new uint[totalBricks];

        _mips = AirMip.Build(_clipmapLocal, _windowDimsBricks, NUM_AIR_MIP_LEVELS);
        _airMipBuffers = new GraphicsBuffer[_mips.NumLevels];
        for (int k = 0; k < _mips.NumLevels; k++)
        {
            int count = _mips.Levels[k].Length;
            _airMipBuffers[k] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
            UploadAirMipLevel(k);
        }

        RebuildAndUploadPacked();

        Active = this;
    }

    public void MarkDirty(int3 chunkCoord)
    {
        _dirtyChunks.Add(chunkCoord);
    }

    public void UploadDirty(ChunkStore store, BrickDataPool cpuPool)
    {
        if (_dirtyChunks.Count == 0) return;

        BrickDataBuffer.SetData(cpuPool.RawData);

        foreach (int3 chunkCoord in _dirtyChunks)
        {
            Chunk chunk = store.GetChunk(chunkCoord);
            if (chunk == null) continue;

            int3 baseBrickCoord = chunkCoord * 16;

            for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int3 localBrick = new int3(x, y, z);
                int3 worldBrick = baseBrickCoord + localBrick;

                int3 wrapped = worldBrick & _brickMask;
                int flatIndex = wrapped.x + (wrapped.y * _windowDimsBricks.x) + (wrapped.z * _windowDimsBricks.x * _windowDimsBricks.y);

                if (chunk.isUniform)
                {
                    _clipmapLocal[flatIndex] = chunk.uniformMaterial;
                }
                else
                {
                    int brickFlatIdx = CoordMath.LocalBrickIndex(localBrick);
                    _clipmapLocal[flatIndex] = chunk.bricks[brickFlatIdx].data;
                }
            }

            int3 regionMin = baseBrickCoord;
            int3 regionMax = baseBrickCoord + new int3(15, 15, 15);
            AirMip.RebuildRegion(_clipmapLocal, _mips, regionMin, regionMax);
        }

        ClipmapBuffer.SetData(_clipmapLocal);

        for (int k = 0; k < _mips.NumLevels; k++)
            UploadAirMipLevel(k);

        // Full re-pack on every dirty upload. At Phase 2 the world is static so
        // this fires rarely; incremental packing (only the words covering dirty
        // cells) is a straightforward later optimization if edit-heavy frames
        // ever make it show up in a profile.
        RebuildAndUploadPacked();

        _dirtyChunks.Clear();
    }

    private void RebuildAndUploadPacked()
    {
        _packed = AirMip.Pack(_mips);

        if (AirMipPackedBuffer == null || AirMipPackedBuffer.count != _packed.Words.Length)
        {
            AirMipPackedBuffer?.Release();
            AirMipPackedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _packed.Words.Length, 4);
        }
        AirMipPackedBuffer.SetData(_packed.Words);
    }

    private void UploadAirMipLevel(int k)
    {
        uint[] level = _mips.Levels[k];
        if (UseLockBufferForAirMip)
        {
            var native = _airMipBuffers[k].LockBufferForWrite<uint>(0, level.Length);
            native.CopyFrom(level);
            _airMipBuffers[k].UnlockBufferAfterWrite<uint>(level.Length);
        }
        else
        {
            _airMipBuffers[k].SetData(level);
        }
    }

    public void Dispose()
    {
        if (Active == this) Active = null;
        ClipmapBuffer?.Release();
        BrickDataBuffer?.Release();
        AirMipPackedBuffer?.Release();
        AirMipPackedBuffer = null;
        if (_airMipBuffers != null)
        {
            for (int k = 0; k < _airMipBuffers.Length; k++)
                _airMipBuffers[k]?.Release();
            _airMipBuffers = null;
        }
    }
}