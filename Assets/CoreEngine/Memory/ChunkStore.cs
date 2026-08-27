// ==========================================
// Assets/CoreEngine/Memory/ChunkStore.cs
//
// PHASE 4 REVISION of the Phase-1 store. §12's freeze commitment applies:
// "when a phase passes, its public interface freezes -- IWorldQuery.GetVoxel,
// IEditService.SetVoxel ... Bug fixes and additive hooks to a passed system's
// internals are normal and expected -- the rule forbids REDESIGN, not
// EDITING."
//
// Accordingly, GetVoxel and SetVoxel below are UNCHANGED in signature and in
// behaviour, byte for byte in their hot paths. Everything Phase 4 needs is
// additive: a window origin, residency queries, and an eviction path.
//
// ---------------------------------------------------------------------------
// WHY THE INDEX MATH DID NOT NEED TO CHANGE (the important part)
// ---------------------------------------------------------------------------
// The obvious expectation is that a sliding window forces GetFlatIndex to
// become origin-relative. It does not, and understanding why matters because
// the SHADER's equivalent read genuinely DOES need the origin.
//
// §3.3 indexes the ring by `(ChunkCoord - windowOrigin) & windowMask` per
// axis. For power-of-two window dims the subtraction is a no-op under the
// mask -- (c - o) & m and c & m differ only by a constant rotation of the ring,
// and both map each world coord to exactly one slot. So the raw `coord & mask`
// this file already used is a valid ring index for ANY origin.
//
// What the origin is actually needed for is deciding whether a coord is
// INSIDE the window at all, because masking alone silently aliases everything
// outside it back in. On the CPU that aliasing is already caught: GetChunk
// verifies `c.coord.Equals(chunkCoord)` before returning, so a coord outside
// the window lands on some other chunk's slot, fails identity, and returns
// null. The GPU has no such identity check -- a clipmap entry is 4 bytes with
// no coord in it -- which is exactly why PHASE_3_COMPLETION.md §6.2's phantom
// terrain existed, and why the shader needs `_WindowOriginBricks` while this
// file does not.
//
// Recording that asymmetry explicitly, because "the CPU didn't need the fix so
// the GPU probably doesn't either" is a very easy and very expensive wrong
// conclusion to reach here.
//
// ---------------------------------------------------------------------------
// WHAT EVICTION HAD TO ADD (§4.5)
// ---------------------------------------------------------------------------
// Nothing in Phase 1-3 ever removed a chunk, so no code path existed to return
// its memory. §4.5: eviction must "free the chunk's inlined BrickHandle[4096]
// array and return its dense bricks to the Brick Data free-list". §13 Phase 4
// names the failure signature: "Memory creep -> a pool free path missed on
// eviction". EvictChunk below is that path, and it is the ONLY place a chunk
// leaves residency.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class ChunkStore : IWorldQuery, IEditService
{
    private readonly Chunk[] _residentWindow;
    private readonly int3 _windowMask;
    private readonly int3 _windowDims;

    private readonly BrickDataPool _brickPool;
    private readonly ChunkHandleAllocator _handleAllocator;

    // Window origin in CHUNK coords: the minimum corner of the resident box.
    // Mutated ONLY by StreamManager (§3.2 single-writer). Starts at zero so
    // Phase 2/3 scenes, which never move the window, behave identically.
    private int3 _windowOrigin;

    // Live count of resident chunks. Maintained incrementally rather than by
    // scanning 16K slots, since the pool-pressure valve (§3.6) and the rig's
    // per-frame reporting both want it every frame.
    private int _residentCount;

    // Dense bricks currently held by resident chunks. Tracked here rather than
    // derived from BrickDataPool because the pool's free-stack depth also
    // counts bricks held by the cascade pools and by scratch pools used for
    // baseline regeneration -- mixing those would make the §3.6 high-water
    // test fire against the wrong number.
    private int _denseBricksHeld;

    public int3 WindowOrigin => _windowOrigin;
    public int3 WindowDims => _windowDims;
    public int ResidentCount => _residentCount;
    public int DenseBricksHeld => _denseBricksHeld;

    /// Inclusive-min, EXCLUSIVE-max chunk bounds of the resident window.
    public int3 WindowMinChunk => _windowOrigin;
    public int3 WindowMaxChunkExclusive => _windowOrigin + _windowDims;

    public ChunkStore(BrickDataPool brickPool, ChunkHandleAllocator handleAllocator)
    {
        _brickPool = brickPool;
        _handleAllocator = handleAllocator;

        // Window dimensions must be powers of two for bitwise masking (§3.3,
        // §4.3). Asserted rather than assumed: a non-power-of-two does not
        // fail loudly, it aliases silently, and silent aliasing in the ring is
        // the same failure shape as §6.2's phantom terrain.
        _windowDims = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        RequirePowerOfTwo(_windowDims.x, nameof(EngineConfig.WINDOW_CHUNKS_XZ));
        RequirePowerOfTwo(_windowDims.y, nameof(EngineConfig.WINDOW_CHUNKS_Y));
        RequirePowerOfTwo(_windowDims.z, nameof(EngineConfig.WINDOW_CHUNKS_XZ));

        _windowMask = _windowDims - new int3(1, 1, 1);
        _windowOrigin = int3.zero;

        _residentWindow = new Chunk[_windowDims.x * _windowDims.y * _windowDims.z];
    }

    private static void RequirePowerOfTwo(int value, string name)
    {
        if (value <= 0 || (value & (value - 1)) != 0)
            throw new InvalidOperationException(
                $"EngineConfig.{name} = {value} is not a power of two. §3.3/§4.3 require power-of-two " +
                "window dimensions: the resident ring is addressed by (coord & (dim-1)) per axis, and a " +
                "non-power-of-two makes that mask alias coords onto wrong slots silently rather than failing.");
    }

    // =========================================================================
    // Ring addressing
    // =========================================================================

    private int GetFlatIndex(int3 chunkCoord)
    {
        // Toroidal ring-buffer masking per axis (§3.3). Origin-independent by
        // construction -- see the file header for why this is correct under a
        // sliding window and why the GPU's equivalent read is not.
        int3 wrapped = chunkCoord & _windowMask;
        return wrapped.x + _windowDims.x * (wrapped.y + _windowDims.y * wrapped.z);
    }

    /// Is this chunk coord inside the current window box? This is the check
    /// masking alone cannot do. StreamManager uses it to decide admission and
    /// eviction; the acceptance rig uses it to distinguish "unloaded" from
    /// "air", which GetVoxel deliberately cannot (see GetVoxel's note).
    public bool IsInWindow(int3 chunkCoord)
    {
        int3 rel = chunkCoord - _windowOrigin;
        return rel.x >= 0 && rel.x < _windowDims.x
            && rel.y >= 0 && rel.y < _windowDims.y
            && rel.z >= 0 && rel.z < _windowDims.z;
    }

    public bool IsResident(int3 chunkCoord) => GetChunk(chunkCoord) != null;

    /// StreamManager only (§3.2). Moving the origin does not touch any slot:
    /// chunks now outside the box keep occupying their ring slots until they
    /// are explicitly evicted, and GetChunk's identity check keeps them
    /// unreachable from any coord that isn't theirs in the meantime. That is
    /// intentional -- it lets StreamManager slide the window first and then
    /// drain evictions across several frames under MAX_CHUNK_SAVES_PER_FRAME,
    /// rather than being forced to do all the freeing in the crossing frame.
    public void SetWindowOrigin(int3 newOrigin) => _windowOrigin = newOrigin;

    // =========================================================================
    // Residency
    // =========================================================================

    public void InsertChunk(Chunk chunk)
    {
        int flat = GetFlatIndex(chunk.coord);

        // A slot occupied by a DIFFERENT chunk means someone is inserting over
        // a live chunk without evicting it -- the memory-creep failure
        // signature §13 Phase 4 names. Caught here rather than leaking.
        Chunk existing = _residentWindow[flat];
        if (existing != null && !existing.coord.Equals(chunk.coord))
            throw new InvalidOperationException(
                $"[ChunkStore] Insert of chunk {chunk.coord} would overwrite live chunk {existing.coord} " +
                $"in ring slot {flat} without eviction. Every chunk leaving residency must go through " +
                "EvictChunk so its handle array and dense bricks are returned (§4.5).");

        if (existing == null) _residentCount++;
        else _denseBricksHeld -= CountDenseBricks(existing); // same coord: re-insert, old accounting drops

        _residentWindow[flat] = chunk;
        _denseBricksHeld += CountDenseBricks(chunk);
    }

    public Chunk GetChunk(int3 chunkCoord)
    {
        int flatIndex = GetFlatIndex(chunkCoord);
        Chunk c = _residentWindow[flatIndex];

        // The identity check is what makes toroidal masking safe on the CPU.
        // Do not remove it as an "optimization": without it, any coord outside
        // the window silently reads a different chunk's data.
        if (c != null && c.coord.Equals(chunkCoord))
        {
            return c;
        }
        return null;
    }

    /// §4.5 eviction: free the inlined BrickHandle[4096] array and return the
    /// chunk's dense bricks to the Brick Data free-list.
    ///
    /// Returns the number of dense bricks reclaimed, so StreamManager can
    /// report it and the rig can assert the pool returns to its pre-traversal
    /// level ("memory flat over 10 minutes -- any creep is a leak", §13).
    ///
    /// Does NOT save the delta. Saving is a separate, earlier step in the
    /// state machine (Resident->Saving->Unloaded), and folding it in here
    /// would let a caller skip it by calling the wrong method.
    public int EvictChunk(int3 chunkCoord)
    {
        Chunk chunk = GetChunk(chunkCoord);
        if (chunk == null) return 0;

        int freed = 0;

        if (!chunk.isUniform && chunk.bricks != null)
        {
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                uint data = chunk.bricks[i].data;
                if ((data & 0x80000000u) != 0)
                {
                    _brickPool.Free((int)(data & 0x3FFFFFFFu));
                    freed++;
                }
            }

            // Handle array goes back to the pooled allocator, which clears it
            // on return (guarding against the handle-ghosting §3.3 warns about
            // when arrays are recycled).
            _handleAllocator.Free(chunk.bricks);
            chunk.bricks = null;
        }

        _residentWindow[GetFlatIndex(chunkCoord)] = null;
        _residentCount--;
        _denseBricksHeld -= freed;
        return freed;
    }

    /// Iterates every resident chunk. Used by the coalesce scheduler, the LRU
    /// scan, and the rig's census. Yields slots in ring order, which is NOT
    /// spatial order -- callers that need distance ordering must sort.
    public IEnumerable<Chunk> ResidentChunks()
    {
        for (int i = 0; i < _residentWindow.Length; i++)
        {
            Chunk c = _residentWindow[i];
            if (c != null) yield return c;
        }
    }

    // =========================================================================
    // Pool pressure (§3.6's LRU valve)
    // =========================================================================

    /// True once dense-brick usage passes the high-water mark, meaning
    /// StreamManager should LRU-evict the coldest resident chunk BEFORE
    /// servicing the next edit. §3.6: "the triggering edit always succeeds --
    /// you push the eviction radius inward, never fail."
    public bool IsUnderPoolPressure => _denseBricksHeld >= EngineConfig.BrickPoolHighWaterBricks;

    public float PoolUtilisation => _brickPool.Capacity > 0
        ? _denseBricksHeld / (float)_brickPool.Capacity
        : 0f;

    private static int CountDenseBricks(Chunk chunk)
    {
        if (chunk.isUniform || chunk.bricks == null) return 0;
        int n = 0;
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            if ((chunk.bricks[i].data & 0x80000000u) != 0) n++;
        return n;
    }

    // =========================================================================
    // FROZEN API (§12) -- GetVoxel / SetVoxel. Behaviour unchanged from Phase 1.
    // =========================================================================

    public byte GetVoxel(int3 worldVoxelCoord)
    {
        int3 chunkCoord = CoordMath.VoxelToChunk(worldVoxelCoord);
        Chunk chunk = GetChunk(chunkCoord);

        // 1. Chunk uniform check
        //
        // A null chunk returns air, and that is DELIBERATELY ambiguous with
        // real air: the raymarcher, the CPU oracle and physics all want "there
        // is nothing solid here", and an unloaded chunk satisfies that. Callers
        // that must distinguish "unloaded" from "air" -- the acceptance rig's
        // false-miss hunt, chiefly, which would otherwise score every
        // out-of-window ray as a defect -- ask IsResident/IsInWindow instead.
        // Changing this return would be a redesign of a frozen API (§12).
        if (chunk == null) return 0;
        if (chunk.isUniform) return chunk.uniformMaterial;

        int3 localBrick = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(worldVoxelCoord));
        int brickFlatIndex = CoordMath.LocalBrickIndex(localBrick);

        uint handleData = chunk.bricks[brickFlatIndex].data;
        bool isDense = (handleData & 0x80000000) != 0;

        // 2. Brick uniform check
        if (!isDense)
        {
            return (byte)(handleData & 0xFF);
        }

        // 3. Dense body read
        int poolIndex = (int)(handleData & 0x3FFFFFFF);
        int3 localVoxel = CoordMath.LocalVoxelIndex3D(worldVoxelCoord);
        int voxelFlatIndex = CoordMath.LocalVoxelIndex(localVoxel);

        return _brickPool.RawData[(poolIndex * 512) + voxelFlatIndex];
    }

    public void SetVoxel(int3 worldVoxelCoord, byte material)
    {
        int3 chunkCoord = CoordMath.VoxelToChunk(worldVoxelCoord);
        Chunk chunk = GetChunk(chunkCoord);

        if (chunk == null) return; // Cannot edit unloaded chunks

        // 1. Chunk uniform check and expansion
        if (chunk.isUniform)
        {
            if (chunk.uniformMaterial == material) return; // No-op fast path

            chunk.isUniform = false;
            chunk.bricks = _handleAllocator.Alloc();

            for (int i = 0; i < 4096; i++)
            {
                chunk.bricks[i].data = chunk.uniformMaterial;
            }
        }

        int3 localBrick = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(worldVoxelCoord));
        int brickFlatIndex = CoordMath.LocalBrickIndex(localBrick);

        uint handleData = chunk.bricks[brickFlatIndex].data;
        bool isDense = (handleData & 0x80000000) != 0;

        // 2. Brick uniform check and expansion
        if (!isDense)
        {
            byte brickMaterial = (byte)(handleData & 0xFF);
            if (brickMaterial == material) return; // No-op fast path

            int poolIndex = _brickPool.Alloc();
            int startOffset = poolIndex * 512;

            NativeArray<byte> rawData = _brickPool.RawData;
            for (int i = 0; i < 512; i++)
            {
                rawData[startOffset + i] = brickMaterial;
            }

            chunk.bricks[brickFlatIndex].data = 0x80000000 | (uint)poolIndex;
            handleData = chunk.bricks[brickFlatIndex].data;

            // Dense-brick accounting for the §3.6 valve. Incremented here
            // because this is the only place SetVoxel can force a brick dense.
            _denseBricksHeld++;
        }

        // 3. Write Voxel
        int poolIdx = (int)(handleData & 0x3FFFFFFF);
        int3 localVoxel = CoordMath.LocalVoxelIndex3D(worldVoxelCoord);
        int voxelFlatIndex = CoordMath.LocalVoxelIndex(localVoxel);

        NativeArray<byte> finalData = _brickPool.RawData;
        finalData[(poolIdx * 512) + voxelFlatIndex] = material;

        // 4. Mark dirty for clipmap upload and delta save
        chunk.dirty = true;
        chunk.deltaDirty = true;
    }

    /// Coalescing (§4.5) frees dense bricks outside SetVoxel, so it reports
    /// back here rather than letting the valve's accounting drift. Called by
    /// CoalesceScheduler after Coalescer.TryCoalesce returns.
    public void NotifyDenseBricksFreed(int count) => _denseBricksHeld -= count;

    /// A chunk that coalesced all the way to uniform hands its handle array
    /// back here. Coalescer deliberately does not do this itself -- its own
    /// comment says "the calling streaming/eviction system is responsible for
    /// returning chunk.bricks back to the ChunkHandleAllocator to avoid tight
    /// coupling", and this is that caller.
    public void ReleaseHandleArray(Chunk chunk)
    {
        if (chunk.bricks == null) return;
        _handleAllocator.Free(chunk.bricks);
        chunk.bricks = null;
    }
}