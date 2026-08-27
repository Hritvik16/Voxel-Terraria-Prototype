// ==========================================
// Assets/CoreEngine/Streaming/StreamManager.cs
//
// Phase 4, file 1 of §13 Phase 4's ordered list: "single-writer, NativeQueue
// plumbing, the §4.4 lifetime state machine with the Saving-eviction lock."
//
// SINGLE-WRITER (§3.2, §13's failure signature "you reach for a lock -> you
// violated the single-writer rule"): worker threads NEVER touch ChunkStore,
// BrickDataPool, ChunkHandleAllocator, or the clipmaps. They generate into a
// per-worker SCRATCH pool they own exclusively, and push a completion record
// into a concurrent queue. The main thread drains that queue and does all
// mutation of shared state. There is no lock anywhere in this file.
//
// This replaces Phase 3's Parallel.For + `allocLock`, which measured only
// 1.99x on 8 cores (PHASE_3_COMPLETION.md §4) precisely because every one of
// ~151k pool allocations serialised on that lock. Scratch pools remove the
// contention rather than reducing it.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    public class StreamManager : IDisposable
    {
        // ---- Owned collaborators ----
        private readonly ChunkStore _store;
        private readonly BrickDataPool _pool;
        private readonly ChunkHandleAllocator _allocator;
        private readonly TerrainClipmap _clipmap;
        private readonly LODCascadeManager _cascades;
        private readonly WorldMetaData _meta;
        private readonly string _deltaDir;
        private readonly ColumnSampler.State _samplerState;

        // §3.2 Sparse Chunk Table: "every chunk ever generated/edited".
        private readonly Dictionary<int3, ChunkRecord> _table = new Dictionary<int3, ChunkRecord>();

        // Worker -> main completion queue. ConcurrentQueue rather than
        // NativeQueue: the payload is a managed Chunk plus a managed scratch
        // context, neither of which is blittable, so NativeQueue cannot hold
        // it. The §3.2 property that matters is "workers push, main thread
        // drains once per frame, no locks" -- that is satisfied either way.
        private readonly ConcurrentQueue<LoadCompletion> _completions = new ConcurrentQueue<LoadCompletion>();

        // Scratch contexts, one per worker slot, recycled. Each owns a private
        // BrickDataPool so generation never touches the shared one.
        private readonly ConcurrentBag<ScratchContext> _scratchPool = new ConcurrentBag<ScratchContext>();
        private readonly int _maxConcurrentLoads;
        private int _inFlight;

        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

        // ---- Tunables read from EngineConfig (§0.1 inv. 8) ----
        private readonly int3 _windowDims;
        private readonly int _loadRadiusChunks;
        private readonly int _evictRadiusChunks;

        // ---- Telemetry the acceptance rig reads ----
        public int ChunksAdmittedTotal { get; private set; }
        public int ChunksEvictedTotal { get; private set; }
        public int ChunksSavedTotal { get; private set; }
        public int DeltasLoadedTotal { get; private set; }
        public int DeltasRejectedTotal { get; private set; }
        public int LruEvictionsTotal { get; private set; }
        public int PendingLoads => _pending.Count;
        public int InFlightLoads => _inFlight;
        public double LastDrainMs { get; private set; }
        public double LastUploadMs { get; private set; }
        public int LastUploadBytes { get; private set; }
        public readonly List<string> RejectLog = new List<string>();

        private readonly List<int3> _pending = new List<int3>();
        private readonly HashSet<int3> _pendingSet = new HashSet<int3>();
        private uint _frame;
        private int3 _lastCameraChunk = new int3(int.MinValue, 0, 0);
        private float3 _lastCameraPos;
        private float3 _velocity;

        // Highest chunk-Y that generation can produce content for. Drives the
        // shader's content-ceiling early exit; asserted below so the hardcoded
        // assumption in Raymarch.compute cannot silently go stale.
        public const int MAX_GENERATED_CHUNK_Y = 0;

        private sealed class ScratchContext
        {
            public BrickDataPool pool;
            public ChunkHandleAllocator allocator;
            public byte[] scratchBody = new byte[EngineConfig.BRICK_BODY_BYTES];
        }

        private struct LoadCompletion
        {
            public int3 coord;
            public ushort generation;
            public Chunk chunk;              // built in scratch storage
            public ScratchContext scratch;
            public bool deltaApplied;
            public DeltaRejectReason rejectReason;
            public bool hadDeltaFile;
            public Exception error;
        }

        public StreamManager(
            ChunkStore store, BrickDataPool pool, ChunkHandleAllocator allocator,
            TerrainClipmap clipmap, LODCascadeManager cascades,
            WorldMetaData meta, string deltaDirectory,
            int loadRadiusChunks)
        {
            _store = store; _pool = pool; _allocator = allocator;
            _clipmap = clipmap; _cascades = cascades;
            _meta = meta; _deltaDir = deltaDirectory;
            _samplerState = ColumnSampler.CreateState(meta);

            _windowDims = store.WindowDims;

            // Eviction must happen strictly OUTSIDE the load radius or a chunk
            // oscillates between admitted and evicted on every camera jitter.
            _loadRadiusChunks = loadRadiusChunks;
            _evictRadiusChunks = loadRadiusChunks + EngineConfig.HYSTERESIS_RING_CHUNKS;

            int halfWindow = math.min(_windowDims.x, _windowDims.z) / 2;
            if (_evictRadiusChunks > halfWindow)
                throw new InvalidOperationException(
                    $"[StreamManager] evict radius {_evictRadiusChunks} chunks exceeds half the window " +
                    $"({halfWindow}). A chunk outside the window still occupies a ring slot that a NEW " +
                    "chunk will alias onto, so the window must strictly contain the eviction radius.");

            _maxConcurrentLoads = math.max(2, Environment.ProcessorCount - 1);
            for (int i = 0; i < _maxConcurrentLoads; i++)
            {
                _scratchPool.Add(new ScratchContext
                {
                    // 4096 = worst case, an entirely dense chunk (§11.4).
                    pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK),
                    allocator = new ChunkHandleAllocator(2),
                });
            }

            System.IO.Directory.CreateDirectory(_deltaDir);
        }

        // =====================================================================
        // Per-frame entry point. MAIN THREAD ONLY.
        // =====================================================================
        public void Update(Vector3 cameraWorldPos, bool allowSlide = true)
        {
            _frame++;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            float3 camPos = new float3(cameraWorldPos.x, cameraWorldPos.y, cameraWorldPos.z);
            _velocity = math.lerp(_velocity, (camPos - _lastCameraPos) * 60f, 0.25f);
            _lastCameraPos = camPos;

            int3 camChunk = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(camPos));
            camChunk.y = 0; // Y pinned this phase -- see EngineConfig header

            if (allowSlide && !camChunk.Equals(_lastCameraChunk))
            {
                _lastCameraChunk = camChunk;
                SlideWindow(camChunk);
                RebuildPendingSet(camChunk);
                EvictOutOfRange(camChunk);
            }

            DispatchLoads(camChunk);
            DrainCompletions();
            ApplyPoolPressureValve(camChunk);

            LastDrainMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var stats = _clipmap.UploadDirty(_store, _pool, EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME,
                                             camChunk, EngineConfig.UPLOAD_EXEMPT_RADIUS_CHUNKS);
            _cascades.UploadDirty(_store, _pool);
            LastUploadMs = sw.Elapsed.TotalMilliseconds;
            LastUploadBytes = stats.bytesUploaded;
        }

        private void SlideWindow(int3 camChunk)
        {
            // Origin centres the box on the camera. Y is pinned to 0 this
            // phase: nothing generates outside cy=0, so sliding in Y would add
            // an untrusted axis for zero content (EngineConfig header).
            int3 origin = new int3(
                camChunk.x - _windowDims.x / 2,
                0,
                camChunk.z - _windowDims.z / 2);
            _store.SetWindowOrigin(origin);
            _clipmap.SetWindowOrigin(origin);
        }

        // =====================================================================
        // Admission
        // =====================================================================

        private void RebuildPendingSet(int3 camChunk)
        {
            _pending.Clear();
            _pendingSet.Clear();

            int r = _loadRadiusChunks;
            for (int cy = 0; cy < _windowDims.y; cy++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                var coord = new int3(camChunk.x + dx, cy, camChunk.z + dz);
                if (!_store.IsInWindow(coord)) continue;
                if (_store.IsResident(coord)) continue;

                var rec = GetOrCreateRecord(coord);
                if (rec.state != ChunkState.Unloaded) continue; // already Loading/Saving

                _pending.Add(coord);
                _pendingSet.Add(coord);
            }

            // §4.3 prefetch ordering: "incoming edge chunks load by angular
            // proximity to the velocity vector." Nearest-first is the primary
            // key (pop-in inside 128m is the failure §13 names); the velocity
            // term breaks ties toward where the player is heading.
            float3 vel = math.lengthsq(_velocity) > 0.01f ? math.normalize(_velocity) : new float3(0, 0, 1);
            _pending.Sort((a, b) => Score(a).CompareTo(Score(b)));

            float Score(int3 c)
            {
                float3 d = new float3(c.x - camChunk.x, 0, c.z - camChunk.z);
                float dist = math.length(d);
                float align = dist > 0.01f ? math.dot(math.normalize(d), vel) : 1f;
                return dist - align * 1.5f; // up to 1.5 chunks of priority for heading
            }
        }

        private void DispatchLoads(int3 camChunk)
        {
            while (_pending.Count > 0 && _inFlight < _maxConcurrentLoads)
            {
                int3 coord = _pending[0];
                _pending.RemoveAt(0);
                _pendingSet.Remove(coord);

                if (!_store.IsInWindow(coord) || _store.IsResident(coord)) continue;

                var rec = GetOrCreateRecord(coord);
                if (rec.state != ChunkState.Unloaded) continue;

                if (!_scratchPool.TryTake(out ScratchContext scratch)) break;

                // Unloaded -> Loading. Condition: "free residency slot exists".
                // The slot is free iff nothing resident currently occupies this
                // coord's ring slot -- checked via IsResident above plus the
                // aliasing guard in ChunkStore.InsertChunk.
                ChunkLifecycle.Transition(rec, ChunkState.Loading, conditionMet: true, context: "DispatchLoads");

                Interlocked.Increment(ref _inFlight);
                ushort gen = rec.generation;
                Task.Run(() => GenerateOnWorker(coord, gen, scratch), _cancel.Token);
            }
        }

        // ---- WORKER THREAD. Touches only `scratch` and pure functions. ----
        private void GenerateOnWorker(int3 coord, ushort generation, ScratchContext scratch)
        {
            var completion = new LoadCompletion { coord = coord, generation = generation, scratch = scratch };
            try
            {
                var chunk = new Chunk();
                ChunkGeneratorFull.GenerateChunkFull(in _samplerState, _meta, coord, chunk,
                    scratch.allocator, scratch.pool, allocLock: null);

                // §4.2: a delta, if present, replays the player's edits onto the
                // freshly-regenerated baseline. Decode runs HERE, on the worker,
                // against scratch storage -- it is pure CPU work and keeping it
                // off the main thread is the whole point of async loading.
                string path = DeltaCodec.PathFor(_deltaDir, coord);
                if (DeltaCodec.TryReadFile(path, out byte[] bytes, out var readReason))
                {
                    completion.hadDeltaFile = true;
                    if (DeltaCodec.TryDecodeOnto(bytes, coord, _meta.seed, chunk, scratch.pool, out var reason))
                        completion.deltaApplied = true;
                    else
                        completion.rejectReason = reason;
                }
                else if (readReason == DeltaRejectReason.FileUnreadable)
                {
                    completion.hadDeltaFile = true;
                    completion.rejectReason = readReason;
                }

                completion.chunk = chunk;
            }
            catch (Exception e)
            {
                completion.error = e;
            }
            finally
            {
                _completions.Enqueue(completion);
                Interlocked.Decrement(ref _inFlight);
            }
        }

        // ---- MAIN THREAD. All shared-state mutation happens here. ----
        private void DrainCompletions()
        {
            int admitted = 0;
            while (admitted < EngineConfig.MAX_CHUNK_LOADS_PER_FRAME && _completions.TryDequeue(out var c))
            {
                try
                {
                    if (!_table.TryGetValue(c.coord, out ChunkRecord rec)) continue;

                    // Stale-completion guard. A chunk that was evicted while its
                    // load was in flight has a bumped generation (ChunkLifecycle
                    // increments on ->Unloaded). Dropping the result here is what
                    // prevents a fast leave/re-enter from being overwritten by
                    // its own previous load -- an ordering bug that only
                    // reproduces at speed.
                    if (rec.generation != c.generation || rec.state != ChunkState.Loading) continue;

                    if (c.error != null)
                    {
                        Debug.LogError($"[StreamManager] Generation failed for {c.coord}: {c.error}");
                        // §4.4 forbids Loading->Unloaded. Fail EXPLICITLY into
                        // Resident as a uniform-air chunk rather than abandoning
                        // the load; the chunk can then evict normally.
                        var airChunk = new Chunk { coord = c.coord, isUniform = true, uniformMaterial = Materials.Air };
                        ChunkLifecycle.Transition(rec, ChunkState.Resident, true, "generation failed -> air");
                        _store.InsertChunk(airChunk);
                        rec.residentSlot = 0;
                        continue;
                    }

                    if (c.hadDeltaFile)
                    {
                        if (c.deltaApplied) { DeltasLoadedTotal++; rec.deltaByteLength = 1; }
                        else
                        {
                            DeltasRejectedTotal++;
                            rec.deltaByteLength = 0;
                            string msg = $"chunk {c.coord}: delta DISCARDED ({c.rejectReason}) -> regenerated pristine baseline (§4.2)";
                            if (RejectLog.Count < 64) RejectLog.Add(msg);
                            Debug.LogWarning("[StreamManager] " + msg);
                        }
                    }

                    // Transfer scratch -> shared pool. This is the only place
                    // shared pool allocation happens for generated chunks.
                    TransferToSharedPool(c.chunk, c.scratch);

                    // Loading -> Resident. Condition: "CRC passes (loaded) or
                    // generation done". Generation completed without throwing,
                    // and any delta either verified or was discarded -- both
                    // satisfy the condition.
                    ChunkLifecycle.Transition(rec, ChunkState.Resident, true, "DrainCompletions");

                    _store.InsertChunk(c.chunk);
                    rec.residentSlot = 0;
                    rec.lastTouchedFrame = _frame;

                    _clipmap.MarkDirty(c.coord);
                    _cascades.MarkDirty(c.coord);

                    ChunksAdmittedTotal++;
                    admitted++;
                }
                finally
                {
                    ResetScratch(c.scratch);
                    _scratchPool.Add(c.scratch);
                }
            }
        }

        private void TransferToSharedPool(Chunk chunk, ScratchContext scratch)
        {
            if (chunk.isUniform || chunk.bricks == null) return;

            var src = scratch.pool.RawData;
            var dst = _pool.RawData;
            int body = EngineConfig.BRICK_BODY_BYTES;

            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                uint data = chunk.bricks[i].data;
                if ((data & 0x80000000u) == 0) continue;

                int srcIdx = (int)(data & 0x3FFFFFFFu);
                int dstIdx = _pool.Alloc();
                int s = srcIdx * body, d = dstIdx * body;
                for (int v = 0; v < body; v++) dst[d + v] = src[s + v];

                chunk.bricks[i].data = 0x80000000u | (uint)dstIdx;
            }

            // Hand the scratch handle array back so the real allocator owns it.
            var real = _allocator.Alloc();
            Array.Copy(chunk.bricks, real, EngineConfig.BRICKS_PER_CHUNK);
            scratch.allocator.Free(chunk.bricks);
            chunk.bricks = real;
        }

        private static void ResetScratch(ScratchContext scratch)
        {
            if (scratch == null) return;
            // Rebuild the free list wholesale rather than tracking which slots
            // were used: a scratch pool is only 4096 slots and this is O(n)
            // with no bookkeeping to get wrong.
            scratch.pool.Dispose();
            scratch.pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        }

        // =====================================================================
        // Eviction (§4.5)
        // =====================================================================

        private void EvictOutOfRange(int3 camChunk)
        {
            _evictScratch.Clear();
            foreach (var chunk in _store.ResidentChunks())
            {
                int dx = math.abs(chunk.coord.x - camChunk.x);
                int dz = math.abs(chunk.coord.z - camChunk.z);
                if (math.max(dx, dz) > _evictRadiusChunks || !_store.IsInWindow(chunk.coord))
                    _evictScratch.Add(chunk.coord);
            }

            int saves = 0;
            foreach (var coord in _evictScratch)
            {
                if (saves >= EngineConfig.MAX_CHUNK_SAVES_PER_FRAME && NeedsSave(coord)) continue;
                if (EvictChunk(coord)) saves++;
            }
        }
        private readonly List<int3> _evictScratch = new List<int3>();

        private bool NeedsSave(int3 coord)
        {
            var chunk = _store.GetChunk(coord);
            return chunk != null && chunk.deltaDirty;
        }

        /// The single eviction path. Returns true if a delta was written.
        private bool EvictChunk(int3 coord)
        {
            if (!_table.TryGetValue(coord, out ChunkRecord rec)) return false;
            if (rec.state != ChunkState.Resident) return false; // Saving/Loading are not evictable

            var chunk = _store.GetChunk(coord);
            if (chunk == null) return false;

            bool saved = false;
            if (chunk.deltaDirty)
            {
                // Resident -> Saving, condition deltaDirty == true.
                ChunkLifecycle.Transition(rec, ChunkState.Saving, true, "EvictChunk (dirty)");
                bool renamed = SaveDelta(coord, chunk);
                // Saving -> Unloaded, condition "atomic rename succeeded".
                ChunkLifecycle.Transition(rec, ChunkState.Unloaded, renamed, "EvictChunk save complete");
                saved = true;
                ChunksSavedTotal++;
            }
            else
            {
                // Resident -> Unloaded, condition deltaDirty == false.
                ChunkLifecycle.Transition(rec, ChunkState.Unloaded, true, "EvictChunk (clean)");
            }

            _store.EvictChunk(coord);
            rec.residentSlot = ChunkRecord.NONE;
            ChunksEvictedTotal++;
            return saved;
        }

        private bool SaveDelta(int3 coord, Chunk live)
        {
            // §4.2: "each populated chunk's bricks are compared against a fresh
            // baseline regeneration." The baseline is built into a scratch pool
            // so the diff never perturbs the live one.
            ScratchContext scratch = null;
            try
            {
                if (!_scratchPool.TryTake(out scratch))
                    scratch = new ScratchContext
                    {
                        pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK),
                        allocator = new ChunkHandleAllocator(2),
                    };

                var baseline = new Chunk();
                ChunkGeneratorFull.GenerateChunkFull(in _samplerState, _meta, coord, baseline,
                    scratch.allocator, scratch.pool, null);

                byte[] bytes = DeltaCodec.Encode(coord, _meta.seed, live, _pool, baseline, scratch.pool);
                string path = DeltaCodec.PathFor(_deltaDir, coord);

                if (bytes == null)
                {
                    // Coalesced back to baseline. §4.1 makes ABSENCE meaningful,
                    // so the stale file must go, not linger as a lie.
                    DeltaCodec.DeleteIfPresent(path);
                    if (_table.TryGetValue(coord, out var r0)) r0.deltaByteLength = 0;
                    return true;
                }

                DeltaCodec.WriteAtomic(path, bytes);
                if (_table.TryGetValue(coord, out var r)) { r.deltaByteLength = (uint)bytes.Length; }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[StreamManager] Delta save failed for {coord}: {e}");
                return false;
            }
            finally
            {
                if (scratch != null) { ResetScratch(scratch); _scratchPool.Add(scratch); }
            }
        }

        // =====================================================================
        // §3.6 pool-pressure valve
        // =====================================================================

        private void ApplyPoolPressureValve(int3 camChunk)
        {
            int guard = 0;
            while (_store.IsUnderPoolPressure && guard++ < 32)
            {
                // "LRU-evicts the COLDEST resident chunk". Coldest is scored by
                // last-touched frame, tie-broken by distance, so a chunk the
                // player is standing in is never the victim -- §3.6's guarantee
                // that "the triggering edit always succeeds."
                int3 victim = default;
                bool found = false;
                uint bestFrame = uint.MaxValue;
                int bestDist = -1;

                foreach (var chunk in _store.ResidentChunks())
                {
                    int dist = math.max(math.abs(chunk.coord.x - camChunk.x), math.abs(chunk.coord.z - camChunk.z));
                    if (dist <= 2) continue; // never evict the player's immediate neighbourhood
                    uint touched = _table.TryGetValue(chunk.coord, out var r) ? r.lastTouchedFrame : 0;
                    if (!found || touched < bestFrame || (touched == bestFrame && dist > bestDist))
                    { victim = chunk.coord; bestFrame = touched; bestDist = dist; found = true; }
                }

                if (!found) break;
                EvictChunk(victim);
                LruEvictionsTotal++;
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private ChunkRecord GetOrCreateRecord(int3 coord)
        {
            if (!_table.TryGetValue(coord, out ChunkRecord rec))
            {
                rec = new ChunkRecord { coord = coord };
                _table[coord] = rec;
            }
            return rec;
        }

        public ChunkRecord GetRecord(int3 coord) => _table.TryGetValue(coord, out var r) ? r : null;
        public int TableSize => _table.Count;

        /// Forces every dirty resident chunk to disk. Used by the acceptance
        /// rig's force-quit simulation and on clean shutdown.
        public int FlushAllDirty()
        {
            int n = 0;
            foreach (var chunk in _store.ResidentChunks())
            {
                if (!chunk.deltaDirty) continue;
                if (SaveDelta(chunk.coord, chunk)) { chunk.deltaDirty = false; n++; }
            }
            return n;
        }

        /// Blocks until every in-flight load has landed. Startup only -- never
        /// call this per-frame; it exists so the rig can begin from a known
        /// fully-populated window instead of racing the prefetcher.
        public void WaitForIdle(int maxDrains = 10000)
        {
            int guard = 0;
            while ((_inFlight > 0 || _completions.Count > 0 || _pending.Count > 0) && guard++ < maxDrains)
            {
                DispatchLoads(_lastCameraChunk);
                DrainCompletions();
                if (_inFlight > 0) Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            _cancel.Cancel();
            while (_scratchPool.TryTake(out var s)) s.pool?.Dispose();
            _cancel.Dispose();
        }
    }
}