// ==========================================
// Assets/CoreEngine/Streaming/CoalesceScheduler.cs
//
// Phase 4, file 4 of §13 Phase 4's ordered list: "the background job that
// periodically calls Phase-1's Memory/Coalescer.TryCoalesce on resident
// chunks (the check itself already exists and is tested; THIS PHASE ONLY ADDS
// THE SCHEDULING)."
//
// So this file adds no coalescing logic. It decides WHEN and HOW MANY, and it
// handles the two bookkeeping duties Coalescer deliberately left to its
// caller (its own comment: "The calling streaming/eviction system is
// responsible for returning chunk.bricks back to the ChunkHandleAllocator to
// avoid tight coupling").
//
// §4.5: "Coalescing only ever FREES memory and only touches already-resident
// chunks -- it can never fail or race (single-writer, main-thread-scheduled)."
// Main-thread-scheduled is load-bearing: this runs inside the same drain the
// StreamManager owns, so it cannot observe a chunk mid-eviction.
//
// AMORTISATION: a full pass over a populated chunk reads up to 4,096 handles
// and, for each dense one, up to 512 body bytes -- ~2MB in the worst case.
// Doing that for every resident chunk every frame would cost far more than
// the memory it reclaims, so the scan is a ROUND-ROBIN cursor: a fixed small
// budget of chunks per frame, cycling through residency. Reclaim is not
// urgent (§3.10's Volatile path handles the one case that IS urgent, and that
// is Phase 5 fluid work, not this).
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

namespace VoxelEngine.Streaming
{
    public class CoalesceScheduler
    {
        private readonly ChunkStore _store;
        private readonly BrickDataPool _pool;
        private readonly TerrainClipmap _clipmap;
        private readonly LODCascadeManager _cascades;

        private readonly List<int3> _cursor = new List<int3>();
        private int _cursorIndex;

        // Chunks examined per frame. Deliberately small: this is a reclaim
        // path, not a correctness path, and §0.1 invariant 9 ("the dumbest
        // implementation that works") argues against anything cleverer until a
        // profile says otherwise.
        public int ChunksPerFrame = 2;

        // Raised while the pool is under pressure -- reclaiming faster is
        // exactly what defers an LRU eviction (§3.6), and a coalesce is
        // strictly cheaper than evicting and re-streaming a chunk.
        public int ChunksPerFrameUnderPressure = 16;

        public int BricksCoalescedTotal { get; private set; }
        public int ChunksCollapsedTotal { get; private set; }
        public int ChunksScannedTotal { get; private set; }

        public CoalesceScheduler(ChunkStore store, BrickDataPool pool,
            TerrainClipmap clipmap, LODCascadeManager cascades)
        {
            _store = store; _pool = pool; _clipmap = clipmap; _cascades = cascades;
        }

        /// How many free-stack entries to re-sort per defrag pass, and how often.
        /// 4096 = one fully-dense chunk's worth of bricks, so a pass restores
        /// locality for roughly the next chunk-sized burst of allocations.
        /// Every 60th Update keeps the cost off the per-frame path -- an
        /// Array.Sort of 4096 ints is tens of microseconds, once a second.
        private const int DefragEntries = 4096;
        private const int DefragEveryNUpdates = 60;
        private int _defragTick;

        public void Update()
        {
            // FREE-LIST DEFRAGMENTATION. Eviction returns slots in whatever
            // order chunks die, so a later chunk's dense bodies land on
            // scattered indices and TerrainClipmap's run-coalescer can only
            // merge short runs -- measured runs/slots = 0.224, ~4.5 bricks per
            // SetData, ~1084 driver calls in the worst frame. Re-sorting the top
            // of the free stack makes the next allocations consecutive again.
            // Lives here because §4.5 already defines this as the low-priority
            // background scan; it is not on the streaming hot path.
            if (++_defragTick >= DefragEveryNUpdates)
            {
                _defragTick = 0;
                _pool.SortFreeTop(DefragEntries);
            }

            int budget = _store.IsUnderPoolPressure ? ChunksPerFrameUnderPressure : ChunksPerFrame;

            for (int n = 0; n < budget; n++)
            {
                if (_cursorIndex >= _cursor.Count) Refill();
                if (_cursor.Count == 0) return;

                int3 coord = _cursor[_cursorIndex++];
                var chunk = _store.GetChunk(coord);
                if (chunk == null || chunk.isUniform) continue; // evicted or already collapsed

                ChunksScannedTotal++;
                CoalesceOne(chunk);
            }
        }

        /// Runs a full pass over every resident chunk immediately. Used by the
        /// acceptance rig, which needs a deterministic "coalescing has caught
        /// up" point before asserting that a refilled tunnel returned to
        /// uniform -- a round-robin cursor gives no such point.
        public void RunFullPass()
        {
            Refill();
            foreach (var coord in _cursor)
            {
                var chunk = _store.GetChunk(coord);
                if (chunk == null || chunk.isUniform) continue;
                ChunksScannedTotal++;
                CoalesceOne(chunk);
            }
            _cursorIndex = _cursor.Count;
        }

        private void CoalesceOne(Chunk chunk)
        {
            int denseBefore = CountDense(chunk);

            // Phase-1 code, unchanged and already tested. It frees dense bodies
            // it collapses and sets isUniform when the whole chunk agrees.
            bool collapsed = Coalescer.TryCoalesce(chunk, _pool);

            int denseAfter = chunk.isUniform ? 0 : CountDense(chunk);
            int freed = denseBefore - denseAfter;

            if (freed > 0)
            {
                // Coalescer frees bodies directly into BrickDataPool but has no
                // knowledge of ChunkStore's dense-brick accounting, which the
                // §3.6 valve reads. Without this the valve's count drifts
                // upward forever and eventually LRU-evicts on phantom pressure.
                _store.NotifyDenseBricksFreed(freed);
                BricksCoalescedTotal += freed;
            }

            if (collapsed && chunk.bricks != null)
            {
                // The other duty Coalescer left to its caller: the 16KB handle
                // array of a now-uniform chunk is pure waste until returned.
                _store.ReleaseHandleArray(chunk);
                ChunksCollapsedTotal++;
            }

            if (freed > 0 || collapsed)
            {
                // The tier-0 clipmap MUST re-upload: its handles point at pool
                // slots the coalesce just freed, which may already be reissued
                // -- the silent CPU/GPU drift §3.7's validator exists to catch.
                _clipmap.MarkDirty(chunk.coord);

                // The CASCADES deliberately do NOT re-mark. Coalescing is
                // content-preserving by definition (§4.5: it detects that all
                // voxels are already identical and changes only the
                // representation), and the cascade tiers are derived from voxel
                // CONTENT -- so the existing downsampled data is still exactly
                // right. Re-marking here was queueing an ~11ms main-thread
                // downsample per coalesced chunk to recompute bytes that could
                // not have changed.
                //
                // chunk.dirty is likewise NOT set: that flag now feeds
                // StreamManager's edit-propagation sweep, which marks BOTH
                // mirrors -- using it here would re-queue the cascade work this
                // branch exists to avoid.
            }
        }

        private void Refill()
        {
            _cursor.Clear();
            foreach (var chunk in _store.ResidentChunks())
                if (!chunk.isUniform) _cursor.Add(chunk.coord);
            _cursorIndex = 0;
        }

        private static int CountDense(Chunk chunk)
        {
            if (chunk.isUniform || chunk.bricks == null) return 0;
            int n = 0;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
                if ((chunk.bricks[i].data & 0x80000000u) != 0) n++;
            return n;
        }
    }
}