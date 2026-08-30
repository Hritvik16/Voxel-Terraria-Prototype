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
        public int dirtyRemaining;

        // PER-PHASE TIMING. The previous run reported a single upload_ms of
        // 44.9ms median against a 1.0ms budget and gave no way to attribute it.
        // "Where did the 44ms go" is not answerable by adding more Stopwatches
        // around the same call -- it needs the call broken into its parts.
        public double stagingMs;      // building the 4096-entry chunk staging array
        public double clipmapSetMs;   // ClipmapBuffer writes
        public double mipRebuildMs;   // AirMip.RebuildRegion
        public double packRegionMs;   // AirMip.PackRegion
        public double brickSetMs;     // BrickDataBuffer writes (dense bodies)
        public double packUploadMs;   // AirMipPackedBuffer write
        public int setDataCalls;      // count of GPU write calls issued
    }

    /// Use LockBufferForWrite/UnlockBufferAfterWrite instead of SetData for the
    /// per-chunk partial writes.
    ///
    /// WHY THIS IS A TOGGLE AND NOT JUST A CHANGE: SetData with an offset is a
    /// plausible but UNPROVEN suspect for the ~1,000ms/frame that shows up on
    /// the RENDER thread and not in any main-thread Stopwatch. Unity may service
    /// a partial write to an in-flight buffer by renaming the whole allocation --
    /// which for a 384MB BrickDataBuffer would be catastrophic and would land
    /// exactly where the unexplained time is. LockBufferForWrite is the API
    /// intended for partial updates and should avoid that.
    ///
    /// Leaving it switchable so the acceptance rig can A/B it and MEASURE the
    /// difference rather than shipping a fix on a hypothesis. The same pattern
    /// already exists in this file for air-mips (UseLockBufferForAirMip).
    public static bool UseLockBufferForUploads = true;

    /// Skips all GPU writes while leaving every CPU-side structure updated.
    /// Diagnostic only -- lets the rig measure frame time with uploads removed
    /// and nothing else changed, which is the only clean way to separate upload
    /// cost from raymarch cost when the time is hiding on the render thread.
    public static bool SuppressGpuUploads = false;

    public TerrainClipmap(int3 windowChunks, int brickPoolCapacity)
    {
        _windowDimsChunks = windowChunks;
        _windowDimsBricks = windowChunks * EngineConfig.CHUNK_EDGE_BRICKS;
        _brickMask = _windowDimsBricks - new int3(1, 1, 1);
        _chunkMask = windowChunks - new int3(1, 1, 1);

        int totalBricks = _windowDimsBricks.x * _windowDimsBricks.y * _windowDimsBricks.z;

        // LockBufferForWrite usage must be declared at creation. Requesting it
        // unconditionally costs nothing when unused and avoids a second buffer
        // shape to reason about.
        ClipmapBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
            GraphicsBuffer.UsageFlags.LockBufferForWrite, totalBricks, 4);
        BrickDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
            GraphicsBuffer.UsageFlags.LockBufferForWrite, brickPoolCapacity * 128, 4);
        _clipmapLocal = new uint[totalBricks];

        // ZERO THE GPU CLIPMAP. This line's absence was the "memory corruption".
        //
        // Incremental upload only ever writes chunks that get LOADED, and a
        // fresh GraphicsBuffer contains whatever was in GPU memory. The window
        // is 32x32 chunks while the load radius covers 27x27, so every slot in
        // the surrounding ring held garbage -- some of it decoding as DENSE
        // handles pointing at arbitrary brick bodies. Rendered as thin plates of
        // random material near y=120..127 voxels, which at distance appear as
        // slabs floating above the horizon.
        //
        // Phase 3 never hit this because its UploadDirty did a full
        // SetData(_clipmapLocal) on every flush, which incidentally zeroed
        // everything. Making the upload incremental removed that side effect and
        // nothing replaced it. _clipmapLocal is already all-zero (uniform air),
        // so one upload at construction establishes the invariant that every
        // slot the shader can address holds a value the CPU agrees with.
        ClipmapBuffer.SetData(_clipmapLocal);

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

    // --- Dirty-set introspection. Not decoration: without it a clipmap
    // mismatch is undiagnosable, because "GPU is stale" has two causes that
    // need OPPOSITE fixes:
    //   still dirty  -> upload LAG. The write is queued; the budget or the
    //                   frame ordering delayed it. Benign, or a budget tune.
    //   not dirty    -> LOST UPDATE. Something changed the CPU chunk without
    //                   marking it, or the mark was consumed without the write
    //                   landing. A real bug, and the one that corrupts frames.
    // The first acceptance run reported 15 mismatches and could not tell these
    // apart, which is why it produced no actionable finding.
    public bool IsDirty(int3 chunkCoord) => _dirtyChunks.Contains(chunkCoord);
    public int DirtyCount => _dirtyChunks.Count;

    /// Forces every pending chunk out regardless of the per-frame byte budget.
    /// Test/diagnostic use only -- it deliberately violates §0.2's anti-stutter
    /// cap, which is exactly why it must never be called from a timing path.
    public UploadStats FlushAllDirty(ChunkStore store, BrickDataPool cpuPool)
        => UploadDirty(store, cpuPool, int.MaxValue, int3.zero, int.MaxValue);

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

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double phaseStart = 0;

        // Exempt chunks first (§3.7's 8.4 invariant), then the rest nearest-first
        // so the spread admits what the player is closest to seeing.
        _dirtyOrdered.Clear();
        _dirtyOrdered.AddRange(_dirtyChunks);
        _dirtyOrdered.Sort((a, b) => ChebyshevXZ(a, cameraChunk).CompareTo(ChebyshevXZ(b, cameraChunk)));

        _dirtyBrickSlots.Clear();
        int bytes = 0;

        // §0.2's cap has to bound EVERY byte this frame puts on the bus. It did
        // not: only the clipmap-ENTRY term was tested in the loop below, while
        // the two larger payloads -- the dense 512B brick bodies flushed by
        // UploadDirtyBrickBodies, and this packed air-mip SetData -- are added
        // to `bytes` AFTER the loop has already made every admission decision.
        // Both bypassed the budget completely, which is why Gate C measured a
        // ~3.28MB peak against the 3.145MB cap while the entry term alone was
        // bounded to 256KB by MAX_CLIPMAP_CHUNKS_PER_FRAME and could never have
        // breached it on its own. Charged up front so a chunk is admitted only
        // when its FULL cost fits.
        int packedReserve = (_packed != null && !SuppressGpuUploads)
            ? _packed.Words.Length * 4
            : 0;

        // The BYTE cap alone was not a throttle. 3 MB / 16 KB per chunk = 192
        // chunks per frame, and each chunk costs a 4096-iteration staging loop,
        // an AirMip.RebuildRegion, a PackRegion and a SetData -- CPU work that
        // dwarfs the bus traffic the byte cap was written to bound. §0.2's cap
        // protects the bus; this one protects the frame.
        int chunkCap = byteBudget == int.MaxValue
            ? int.MaxValue
            : EngineConfig.MAX_CLIPMAP_CHUNKS_PER_FRAME;

        foreach (int3 chunkCoord in _dirtyOrdered)
        {
            Chunk chunk = store.GetChunk(chunkCoord);
            bool exempt = ChebyshevXZ(chunkCoord, cameraChunk) <= exemptRadiusChunks;

            // Deferred chunks stay in _dirtyChunks and upload on a later frame
            // -- that IS §3.7's multi-frame spread, not a dropped write, and the
            // rig's "no LOST UPDATES" check covers the difference.
            //
            // Exempt chunks are still unconditional (§3.7's 8.4 invariant: what
            // the player stands in or just edited uploads THIS frame). They are
            // therefore still able to carry the total past the cap on their own;
            // bounding them would mean breaking that invariant, which is not a
            // trade this fix is allowed to make.
            if (!exempt)
            {
                int committed = bytes
                              + _dirtyBrickSlots.Count * EngineConfig.BRICK_BODY_BYTES
                              + packedReserve;
                int chunkCost = EngineConfig.CLIPMAP_BYTES_PER_CHUNK
                              + ProjectedBodyBytes(chunk);
                if (committed + chunkCost > byteBudget
                    || stats.chunksUploaded >= chunkCap)
                { stats.chunksDeferred++; continue; }
            }

            phaseStart = sw.Elapsed.TotalMilliseconds;
            int3 baseBrickCoord = chunkCoord * EngineConfig.CHUNK_EDGE_BRICKS;

            // A dirty chunk that is NO LONGER RESIDENT has been evicted, and it
            // must be CLEARED, not skipped.
            //
            // This was the terrain-holes bug. Skipping left the GPU holding that
            // chunk's 4096 handles, still pointing at pool slots that
            // ChunkStore.EvictChunk had already returned to the free-list and
            // that a different chunk had since been given. Worse, the air-mip
            // still described the region, so rays either leapt through solid
            // terrain (holes) or hit freed bodies (garbage geometry).
            //
            // It is reachable, not theoretical: the window spans
            // camChunk-16..camChunk+15 while eviction fires beyond radius 15, so
            // evicted slots sit INSIDE the window bounds where the shader's
            // guard lets the read through.
            //
            // Clearing to uniform air is correct rather than merely safe: an
            // unloaded chunk reads as air on the CPU too (see ChunkStore's
            // GetVoxel note), so the two mirrors agree.
            bool evicted = chunk == null;

            for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int3 localBrick = new int3(x, y, z);
                int local = CoordMath.LocalBrickIndex(localBrick);

                uint handle;
                if (evicted) handle = 0u;                        // uniform air
                else if (chunk.isUniform) handle = chunk.uniformMaterial;
                else handle = chunk.bricks[local].data;

                _chunkStaging[local] = handle;
                _clipmapLocal[BrickLinearIndex(baseBrickCoord + localBrick)] = handle;

                if ((handle & 0x80000000u) != 0)
                    _dirtyBrickSlots.Add((int)(handle & 0x3FFFFFFFu));
            }

            stats.stagingMs += sw.Elapsed.TotalMilliseconds - phaseStart;
            phaseStart = sw.Elapsed.TotalMilliseconds;

            // ONE contiguous write per chunk -- the whole point of chunk-major.
            WriteClipmapChunk(ChunkSlot(chunkCoord) * EngineConfig.BRICKS_PER_CHUNK, ref stats);

            stats.clipmapSetMs += sw.Elapsed.TotalMilliseconds - phaseStart;
            phaseStart = sw.Elapsed.TotalMilliseconds;

            int3 regionMax = baseBrickCoord + new int3(15, 15, 15);
            AirMip.RebuildRegion(_clipmapLocal, _mips, baseBrickCoord, regionMax);

            stats.mipRebuildMs += sw.Elapsed.TotalMilliseconds - phaseStart;
            phaseStart = sw.Elapsed.TotalMilliseconds;
            // Keep the packed mirror in lockstep with the pyramid, incrementally.
            // Was a full AirMip.Pack() once per frame: ~2.4M cells rescanned and
            // a fresh ~300 KB array allocated, every frame that uploaded
            // anything. See AirMip.PackRegion.cs.
            AirMip.PackRegion(_packed, _mips, baseBrickCoord, regionMax);

            stats.packRegionMs += sw.Elapsed.TotalMilliseconds - phaseStart;
            phaseStart = sw.Elapsed.TotalMilliseconds;

            _dirtyChunks.Remove(chunkCoord);
            stats.chunksUploaded++;
            bytes += EngineConfig.CLIPMAP_BYTES_PER_CHUNK;
        }

        phaseStart = sw.Elapsed.TotalMilliseconds;
        bytes += UploadDirtyBrickBodies(cpuPool, ref stats);
        stats.brickSetMs = sw.Elapsed.TotalMilliseconds - phaseStart;
        phaseStart = sw.Elapsed.TotalMilliseconds;

        if (stats.chunksUploaded > 0)
        {
            if (UseLegacyAirMipUpload)
                for (int k = 0; k < _mips.NumLevels; k++) UploadAirMipLevel(k);

            // Upload only. The words were already updated in place by
            // PackRegion above; the whole packed pyramid is ~300 KB at the
            // Phase 4 mirror size, so one SetData is cheaper than tracking which
            // word ranges are dirty.
            if (_packed != null && !SuppressGpuUploads)
            {
                AirMipPackedBuffer.SetData(_packed.Words);
                stats.setDataCalls++;
                bytes += _packed.Words.Length * 4;
            }
        }

        stats.packUploadMs = sw.Elapsed.TotalMilliseconds - phaseStart;
        stats.bytesUploaded = bytes;
        stats.dirtyRemaining = _dirtyChunks.Count;
        return stats;
    }

    private void WriteClipmapChunk(int dstElement, ref UploadStats stats)
    {
        if (SuppressGpuUploads) return;
        stats.setDataCalls++;

        if (UseLockBufferForUploads)
        {
            var na = ClipmapBuffer.LockBufferForWrite<uint>(dstElement, EngineConfig.BRICKS_PER_CHUNK);
            na.CopyFrom(_chunkStaging);
            ClipmapBuffer.UnlockBufferAfterWrite<uint>(EngineConfig.BRICKS_PER_CHUNK);
        }
        else
        {
            ClipmapBuffer.SetData(_chunkStaging, 0, dstElement, EngineConfig.BRICKS_PER_CHUNK);
        }
    }

    private void WriteBrickRun(NativeArray<uint> src, int firstUint, int countUints, ref UploadStats stats)
    {
        if (SuppressGpuUploads) return;
        stats.setDataCalls++;

        if (UseLockBufferForUploads)
        {
            var na = BrickDataBuffer.LockBufferForWrite<uint>(firstUint, countUints);
            NativeArray<uint>.Copy(src, firstUint, na, 0, countUints);
            BrickDataBuffer.UnlockBufferAfterWrite<uint>(countUints);
        }
        else
        {
            BrickDataBuffer.SetData(src, firstUint, firstUint, countUints);
        }
    }

    /// Uploads only the dense bodies the dirty chunks reference. Sorts the
    /// slot indices and coalesces consecutive ones into single ranges: a
    /// freshly generated chunk allocates its slots consecutively (BrickDataPool
    /// hands out a descending free-stack, so sequential Allocs are sequential
    /// indices), which collapses hundreds of slots into a few runs.
    /// Bytes of dense brick BODY that uploading this chunk would add, counted
    /// read-only so a deferred chunk mutates nothing. An evicted chunk uploads
    /// as uniform air and a uniform chunk owns no bodies, so both cost zero and
    /// skip the scan entirely. The count is an upper bound on what
    /// UploadDirtyBrickBodies will actually write (its run-coalescing collapses
    /// duplicates), which is the conservative direction for a cap.
    private static int ProjectedBodyBytes(Chunk chunk)
    {
        if (chunk == null || chunk.isUniform) return 0;

        int dense = 0;
        BrickHandle[] bricks = chunk.bricks;
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            if ((bricks[i].data & 0x80000000u) != 0) dense++;

        return dense * EngineConfig.BRICK_BODY_BYTES;
    }

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
            WriteBrickRun(asUints, firstUint, countUints, ref stats);

            stats.brickRuns++;
            bytes += countUints * 4;
        }

        _dirtyBrickSlots.Clear();
        return bytes;
    }

    private static int ChebyshevXZ(int3 a, int3 b)
        => math.max(math.abs(a.x - b.x), math.abs(a.z - b.z));

    /// Full rebuild + upload. Construction only -- the per-frame path uses
    /// AirMip.PackRegion instead.
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