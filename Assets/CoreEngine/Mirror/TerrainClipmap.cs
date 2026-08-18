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

    // ---- AIR-MIP (Amendment 8.7, Step 3) -----------------------------------
    // The pyramid is a pure function of _clipmapLocal (the CPU-side L0 handle
    // array). It is built once, then RebuildRegion'd per dirty chunk in the same
    // UploadDirty call that writes the clipmap - so the GPU mip and GPU clipmap
    // always reach the GPU together and consistently. One GraphicsBuffer per
    // level. Nothing READS these on the GPU until Step 4 wires the shader; Step 3
    // only builds + uploads + validates them, so Beauty must be pixel-identical.
    // Diagnostic toggle (this session): swap the air-mip buffers' upload
    // mechanism between SetData and LockBufferForWrite, mirroring the
    // §3.7 clipmap benchmark PHASE_1_COMPLETION never got to run cleanly
    // (empty-scene, isolated dispatch). The air-mip buffers are read
    // dependently, 4x per outer iteration, on every ray - if either buffer
    // ends up CPU-visible instead of device-local, this is where it would
    // show. Default false (SetData) - current behavior unchanged until an
    // explicit A/B flips it.
    public static bool UseLockBufferForAirMip = false;

    public const int NUM_AIR_MIP_LEVELS = 4;
    private AirMipData _mips;                       // CPU-side pyramid over _clipmapLocal
    private GraphicsBuffer[] _airMipBuffers;        // one per level, L1.._mips.NumLevels
    public AirMipData Mips => _mips;
    public GraphicsBuffer AirMipBuffer(int oneBasedLevel) => _airMipBuffers[oneBasedLevel - 1];
    public int AirMipLevelCount => _mips != null ? _mips.NumLevels : 0;

    // Single source of truth for window sizing in bricks. Everything that needs
    // to compute a clipmap flat index (RaymarchFeature, Raymarch.compute via the
    // uniform this feeds, ClipmapValidator) must read this rather than hardcode
    // its own copy - that duplication is exactly what caused the 8.6 window-dims
    // mismatch (EngineConfig said 32x16x32 chunks, the shader/validator assumed
    // 16x8x16 chunks baked in at construction time).
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

        // --- Air-mip: allocate an all-air pyramid (zero-init) and its GPU
        // buffers. _clipmapLocal is currently all-zero (air), so an empty build
        // is the correct starting state; UploadDirty's RebuildRegion fills in the
        // non-air cells as chunks are written. ---
        _mips = AirMip.Build(_clipmapLocal, _windowDimsBricks, NUM_AIR_MIP_LEVELS);
        _airMipBuffers = new GraphicsBuffer[_mips.NumLevels];
        for (int k = 0; k < _mips.NumLevels; k++)
        {
            int count = _mips.Levels[k].Length;
            _airMipBuffers[k] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4);
            UploadAirMipLevel(k); // upload the all-air starting state
        }

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

            // --- Air-mip maintenance: now that this chunk's L0 handles are
            // written into _clipmapLocal, recompute the mip cells overlapping
            // this chunk's brick region, bottom-up. Same code path edits will use
            // (§8.7 maintenance rule). Uses the toroidally-masked FlatIndex, so it
            // is correct even when the chunk sits at the window wrap edge. ---
            int3 regionMin = baseBrickCoord;                 // inclusive brick bounds
            int3 regionMax = baseBrickCoord + new int3(15, 15, 15);
            AirMip.RebuildRegion(_clipmapLocal, _mips, regionMin, regionMax);
        }

        ClipmapBuffer.SetData(_clipmapLocal);

        // Upload the (now-updated) mip levels. At Phase 2 this is a full re-upload
        // per level; the level buffers are small relative to the clipmap and this
        // fires only on dirty frames. Sub-range upload is a later optimization if
        // ever needed (the dirty set is small).
        for (int k = 0; k < _mips.NumLevels; k++)
            UploadAirMipLevel(k);

        _dirtyChunks.Clear();
    }

    // Single upload chokepoint for both call sites above, so the A/B toggle
    // can never drift between constructor-time and UploadDirty-time behavior.
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
        if (_airMipBuffers != null)
        {
            for (int k = 0; k < _airMipBuffers.Length; k++)
                _airMipBuffers[k]?.Release();
            _airMipBuffers = null;
        }
    }
}