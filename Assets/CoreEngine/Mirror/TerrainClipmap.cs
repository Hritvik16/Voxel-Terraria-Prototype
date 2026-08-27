// ==========================================
// Assets/CoreEngine/Mirror/TerrainClipmap.cs
//
// PHASE 4 REVISION. Two changes, one of them a layout change to a GPU buffer.
//
// ---------------------------------------------------------------------------
// 1. GPU CLIPMAP IS NOW CHUNK-MAJOR (was brick-linear). THIS IS THE ONE
//    STRUCTURAL CHANGE IN THIS PHASE AND IT NEEDS REVIEWING AS SUCH.
// ---------------------------------------------------------------------------
// §3.7 requires that a boundary crossing rewrite "only the newly-entered slab
// of brick entries ... not the whole grid", capped by
// MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME (§0.2). With the old brick-linear index
//     flat = x + dimX*(y + dimY*z)
// that is not achievable, and the arithmetic is worth writing down because it
// is the entire justification for touching a frozen-ish buffer layout:
//
//   A chunk owns bricks spanning 16 in x, 16 in y, 16 in z. Under a linear
//   index with x fastest, its only CONTIGUOUS runs are 16 entries (64 bytes)
//   long, one per (y,z) pair -- 256 disjoint runs per chunk. An X-slab
//   crossing dirties 64 chunks => ~16,000 scattered SetData ranges per frame.
//   Call overhead alone puts that in the tens of ms, and the spec's own
//   ~8 MB/frame figure assumed the slab was contiguous, which it never was.
//
// Chunk-major makes each chunk exactly one contiguous 16 KB run:
//     flat = chunkSlot * 4096 + localBrickIndex
//     chunkSlot      = wrappedChunk.x + dimsChunks.x*(wrappedChunk.y + dimsChunks.y*wrappedChunk.z)
//     localBrickIndex= CoordMath.LocalBrickIndex(brickCoord & 15)   (C.1, unchanged)
// so one chunk = ONE SetData, and a 64-chunk slab = 64 calls / 1.0 MB, under
// the §0.2 cap in a single frame. It is also strictly better for traversal
// locality: a ray inside a chunk now walks one 16 KB region instead of
// striding across a 33 MB buffer.
//
// CALLERS THAT MUST MATCH THIS LAYOUT (all updated in this drop):
//   - Raymarch.compute            ReadClipmap
//   - RaymarchStripped.compute    ReadClipmap
//   - RaymarchMemoryProbe.compute ReadClipmap
//   - ClipmapValidator            (byte-compares CPU vs GPU -- would fail loudly)
// The AIR-MIP pyramid is deliberately NOT affected: it is built from
// _clipmapLocal, which stays brick-linear on the CPU. AirMip.cs,
// AirMip.Packed.cs, AirMipValidator and all their tests are untouched.
//
// ---------------------------------------------------------------------------
// 2. UPLOADS ARE INCREMENTAL AND BUDGETED
// ---------------------------------------------------------------------------
// Phase 3 re-uploaded the ENTIRE brick pool (BrickDataBuffer.SetData(RawData)
// = 384 MB) and the ENTIRE clipmap on every dirty flush -- ~920 MB per flush
// including cascades, measured at 446 ms + 3,033 ms in PHASE_3_COMPLETION.md
// §4. Under streaming that fires ~5x/second. Now:
//   - clipmap: one contiguous 16 KB write per dirty chunk
//   - brick bodies: only the pool slots the dirty chunks actually reference,
//     sorted and coalesced into runs (freshly generated chunks allocate
//     consecutive slots, so this is typically a handful of runs)
//   - air-mip: packed pyramid only (~0.16 MB at WINDOW_CHUNKS_Y=2). The legacy
//     four-buffer form is now uploaded ONLY when UseLegacyAirMipUpload is set,
//     because its level-1 buffer alone is 4.2 MB -- over the §0.2 cap by
//     itself, for a path whose certified default is off.
//
// §3.7's "missing invariant" (8.4) is honoured: chunks within
// UPLOAD_EXEMPT_RADIUS_CHUNKS of the camera, or freshly edited, upload THIS
// FRAME unconditionally, exempt from the byte cap. Only prefetched chunks
// ahead of the player are subject to the spread.
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
    private readonly int3 _chunkMask;
    private readonly uint[] _clipmapLocal;      // brick-linear, CPU only, feeds AirMip
    private readonly uint[] _chunkStaging = new uint[EngineConfig.BRICKS_PER_CHUNK];

    private readonly HashSet<int3> _dirtyChunks = new HashSet<int3>();
    private readonly List<int3> _dirtyOrdered = new List<int3>();
    private readonly List<int> _dirtyBrickSlots = new List<int>();

    private int3 _windowOrigin;

    public static bool UseLockBufferForAirMip = false;
    /// Legacy 4-buffer air-mip upload. Off: the packed path is the certified
    /// default (Amendment 8.9 §6) and the legacy buffers are A/B scaffolding.
    public static bool UseLegacyAirMipUpload = false;

    public const int NUM_AIR_MIP_LEVELS = 4;
    private AirMipData _mips;
    private GraphicsBuffer[] _airMipBuffers;
    public AirMipData Mips => _mips;
    public GraphicsBuffer AirMipBuffer(int oneBasedLevel) => _airMipBuffers[oneBasedLevel - 1];
    public int AirMipLevelCount => _mips != null ? _mips.NumLevels : 0;

    private AirMip.PackedMips _packed;
    public GraphicsBuffer AirMipPackedBuffer { get; private set; }
    public AirMip.PackedMips Packed => _packed;

    public int3 WindowDimsBricks => _windowDimsBricks;
    public int3 WindowDimsChunks => _windowDimsChunks;
    public int3 WindowOrigin => _windowOrigin;
    /// Window origin expressed in BRICKS -- what the shader's bounds guard needs.
    public int3 WindowOriginBricks => _windowOrigin * EngineConfig.CHUNK_EDGE_BRICKS;

    public struct UploadStats
    {
        public int chunksUploaded;
        public int chunksDeferred;
        public int brickRuns;
        public int bytesUploaded;
    }

    public TerrainClipmap(int3 windowChunks, int brickPoolCapacity)
    {
        _windowDimsChunks = windowChunks;
        _windowDimsBricks = windowChunks * EngineConfig.CHUNK_EDGE_BRICKS;
        _brickMask = _windowDimsBricks - new int3(1, 1, 1);
        _chunkMask = windowChunks - new int3(1, 1, 1);

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

    public void SetWindowOrigin(int3 originChunks) => _windowOrigin = originChunks;
    public void MarkDirty(int3 chunkCoord) => _dirtyChunks.Add(chunkCoord);

    // =========================================================================
    // Indexing
    // =========================================================================

    /// Ring slot for a chunk. Origin-independent for the same reason
    /// ChunkStore.GetFlatIndex is: under a power-of-two mask, subtracting the
    /// origin only rotates the ring. The origin matters for BOUNDS, not index.
    private int ChunkSlot(int3 chunkCoord)
    {
        int3 w = chunkCoord & _chunkMask;
        return w.x + _windowDimsChunks.x * (w.y + _windowDimsChunks.y * w.z);
    }

    /// Brick-linear index into the CPU-side mirror that feeds AirMip. NOT the
    /// GPU layout -- keep the two straight, they are deliberately different.
    private int BrickLinearIndex(int3 worldBrick)
    {
        int3 w = worldBrick & _brickMask;
        return w.x + (w.y * _windowDimsBricks.x) + (w.z * _windowDimsBricks.x * _windowDimsBricks.y);
    }

    // =========================================================================
    // Upload
    // =========================================================================

    /// Phase 2/3 compatibility overload: unbudgeted, uploads everything dirty.
    /// Used by the Phase 2/3 bootstrappers, which populate a static world once
    /// and are not subject to a per-frame budget.
    public void UploadDirty(ChunkStore store, BrickDataPool cpuPool)
        => UploadDirty(store, cpuPool, int.MaxValue, int3.zero, int.MaxValue);

    public UploadStats UploadDirty(ChunkStore store, BrickDataPool cpuPool,
        int byteBudget, int3 cameraChunk, int exemptRadiusChunks)
    {
        var stats = new UploadStats();
        if (_dirtyChunks.Count == 0) return stats;

        // Exempt chunks first (§3.7's 8.4 invariant), then the rest nearest-first
        // so the spread admits what the player is closest to seeing.
        _dirtyOrdered.Clear();
        _dirtyOrdered.AddRange(_dirtyChunks);
        _dirtyOrdered.Sort((a, b) => ChebyshevXZ(a, cameraChunk).CompareTo(ChebyshevXZ(b, cameraChunk)));

        _dirtyBrickSlots.Clear();
        int bytes = 0;

        foreach (int3 chunkCoord in _dirtyOrdered)
        {
            bool exempt = ChebyshevXZ(chunkCoord, cameraChunk) <= exemptRadiusChunks;
            if (!exempt && bytes + EngineConfig.CLIPMAP_BYTES_PER_CHUNK > byteBudget)
            { stats.chunksDeferred++; continue; }

            Chunk chunk = store.GetChunk(chunkCoord);
            if (chunk == null) { _dirtyChunks.Remove(chunkCoord); continue; }

            int3 baseBrickCoord = chunkCoord * EngineConfig.CHUNK_EDGE_BRICKS;

            for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int3 localBrick = new int3(x, y, z);
                int local = CoordMath.LocalBrickIndex(localBrick);

                uint handle;
                if (chunk.isUniform) handle = chunk.uniformMaterial;
                else handle = chunk.bricks[local].data;

                _chunkStaging[local] = handle;
                _clipmapLocal[BrickLinearIndex(baseBrickCoord + localBrick)] = handle;

                if ((handle & 0x80000000u) != 0)
                    _dirtyBrickSlots.Add((int)(handle & 0x3FFFFFFFu));
            }

            // ONE contiguous write per chunk -- the whole point of chunk-major.
            ClipmapBuffer.SetData(_chunkStaging, 0, ChunkSlot(chunkCoord) * EngineConfig.BRICKS_PER_CHUNK,
                                  EngineConfig.BRICKS_PER_CHUNK);

            AirMip.RebuildRegion(_clipmapLocal, _mips, baseBrickCoord,
                                 baseBrickCoord + new int3(15, 15, 15));

            _dirtyChunks.Remove(chunkCoord);
            stats.chunksUploaded++;
            bytes += EngineConfig.CLIPMAP_BYTES_PER_CHUNK;
        }

        bytes += UploadDirtyBrickBodies(cpuPool, ref stats);

        if (stats.chunksUploaded > 0)
        {
            if (UseLegacyAirMipUpload)
                for (int k = 0; k < _mips.NumLevels; k++) UploadAirMipLevel(k);

            RebuildAndUploadPacked();
            bytes += _packed != null ? _packed.Words.Length * 4 : 0;
        }

        stats.bytesUploaded = bytes;
        return stats;
    }

    /// Uploads only the dense bodies the dirty chunks reference. Sorts the
    /// slot indices and coalesces consecutive ones into single ranges: a
    /// freshly generated chunk allocates its slots consecutively (BrickDataPool
    /// hands out a descending free-stack, so sequential Allocs are sequential
    /// indices), which collapses hundreds of slots into a few runs.
    private int UploadDirtyBrickBodies(BrickDataPool cpuPool, ref UploadStats stats)
    {
        if (_dirtyBrickSlots.Count == 0) return 0;

        _dirtyBrickSlots.Sort();

        // Reinterpret the byte pool as uints so source and destination strides
        // agree with the buffer's 4-byte element size. 512 bytes = 128 uints.
        NativeArray<uint> asUints = cpuPool.RawData.Reinterpret<uint>(sizeof(byte));

        int bytes = 0;
        int i = 0;
        while (i < _dirtyBrickSlots.Count)
        {
            int runStart = _dirtyBrickSlots[i];
            int runEnd = runStart;
            i++;
            while (i < _dirtyBrickSlots.Count &&
                   (_dirtyBrickSlots[i] == runEnd || _dirtyBrickSlots[i] == runEnd + 1))
            {
                runEnd = _dirtyBrickSlots[i];
                i++;
            }

            int firstUint = runStart * 128;
            int countUints = (runEnd - runStart + 1) * 128;
            BrickDataBuffer.SetData(asUints, firstUint, firstUint, countUints);

            stats.brickRuns++;
            bytes += countUints * 4;
        }

        _dirtyBrickSlots.Clear();
        return bytes;
    }

    private static int ChebyshevXZ(int3 a, int3 b)
        => math.max(math.abs(a.x - b.x), math.abs(a.z - b.z));

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

    /// Exposed for ClipmapValidator, which must reproduce the GPU index exactly
    /// rather than keeping its own copy of the formula -- the failure mode its
    /// own header already records ("each had their own copy of the window size,
    /// and only the first two happened to agree").
    public int GpuIndexOf(int3 chunkCoord, int3 localBrick)
        => ChunkSlot(chunkCoord) * EngineConfig.BRICKS_PER_CHUNK + CoordMath.LocalBrickIndex(localBrick);

    public void Dispose()
    {
        if (Active == this) Active = null;
        ClipmapBuffer?.Release();
        BrickDataBuffer?.Release();
        AirMipPackedBuffer?.Release();
        AirMipPackedBuffer = null;
        if (_airMipBuffers != null)
        {
            for (int k = 0; k < _airMipBuffers.Length; k++) _airMipBuffers[k]?.Release();
            _airMipBuffers = null;
        }
    }
}