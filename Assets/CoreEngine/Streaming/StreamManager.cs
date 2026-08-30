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
using Unity.Collections;
using Unity.Jobs;
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

        /// Coords that have a .delta file on disk RIGHT NOW.
        ///
        /// Exists so the streaming dispatch can ask "does this chunk have a
        /// delta?" without a filesystem stat. That question has to be answered
        /// per dispatched chunk, on the main thread, and a stat there is exactly
        /// the kind of main-thread I/O the async load path exists to avoid.
        ///
        /// MAIN THREAD ONLY (§0.1.5). Seeded by one directory enumeration in
        /// Start, then maintained at the only two sites where a delta file is
        /// created or destroyed -- both inside SaveDelta, both already main
        /// thread. §4.1 makes file ABSENCE meaningful, so this set mirrors
        /// presence exactly rather than approximating it.
        ///
        /// This assumes the engine is the sole writer of _deltaDir. A file
        /// dropped in from outside MID-RUN would be missed; files present at
        /// startup are picked up by the enumeration.
        private readonly HashSet<int3> _deltaOnDisk = new HashSet<int3>();
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

        // DEDICATED WORKER THREADS, not Task.Run.
        //
        // Measured on the run that forced this: PrimeWindow generated 722 chunks
        // in 20,854ms of wall time -- 28.9ms/chunk when the per-chunk CPU cost is
        // ~30ms, i.e. effective parallelism of 1.07 from Parallel.For over
        // 8-item waves. Steady state was the same shape: supply ~34 chunks/s
        // against the ~152/s that 60 m/s demands, which is why residency
        // collapsed to 174 during the outbound leg and terrain visibly loaded
        // slower than the camera moved.
        //
        // Cause: Unity's Mono ThreadPool starts nearly empty and injects worker
        // threads slowly. Phase 3's one long Parallel.For (1.8s over 484 chunks)
        // ramped the pool up; 92 short bursts never did, and Task.Run rides the
        // same starved pool. Dedicated threads make the parallelism a property
        // of the code instead of a property of the pool's mood. SetMinThreads in
        // the ctor additionally primes the pool for PrimeWindow's Parallel.For.
        private System.Collections.Concurrent.BlockingCollection<WorkItem> _jobs;
        private Thread[] _workerThreads;

        private struct WorkItem
        {
            public int3 coord;
            public ushort generation;
            public ScratchContext scratch;
        }

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
        public double LastCascadeMs { get; private set; }
        public TerrainClipmap.UploadStats LastUploadStats { get; private set; }
        public int ClipmapDirtyRemaining => LastUploadStats.dirtyRemaining;

        // PrimeWindow breakdown. Startup measured 30,489ms last run and the
        // report could only say "startup". Generation and the main-thread
        // transfer need separating before either is optimised.
        public double PrimeGenerateMs { get; private set; }
        public double PrimeTransferMs { get; private set; }
        public int PrimeWaves { get; private set; }
        public int PrimeChunks { get; private set; }
        public readonly List<string> RejectLog = new List<string>();

        /// How many chunks SHOULD be resident right now, given the load radius
        /// and the generated Y range. The first run had no such number in the
        /// report, so "380 chunks resident" read as normal when it was 52% of
        /// what had been asked for. Any census without an expected value is not
        /// a census.
        public int ExpectedResidentChunks
        {
            get
            {
                int side = _loadRadiusChunks * 2 + 1;
                return side * side * (MAX_GENERATED_CHUNK_Y + 1);
            }
        }

        public int LoadRadiusChunks => _loadRadiusChunks;
        public int EvictRadiusChunks => _evictRadiusChunks;
        public int GenerationErrors { get; private set; }

        /// Coords inside the load square with no resident chunk. Bounded so a
        /// badly broken run cannot produce a megabyte of log.
        public List<int3> MissingChunks(int max = 32)
        {
            var missing = new List<int3>();
            int r = _loadRadiusChunks;
            for (int cy = 0; cy <= MAX_GENERATED_CHUNK_Y && missing.Count < max; cy++)
            for (int dz = -r; dz <= r && missing.Count < max; dz++)
            for (int dx = -r; dx <= r && missing.Count < max; dx++)
            {
                var c = new int3(_lastCameraChunk.x + dx, cy, _lastCameraChunk.z + dz);
                if (!_store.IsResident(c)) missing.Add(c);
            }
            return missing;
        }

        private readonly List<int3> _pending = new List<int3>();
        private readonly HashSet<int3> _pendingSet = new HashSet<int3>();
        private uint _frame;
        private int3 _lastCameraChunk = new int3(int.MinValue, 0, 0);
        private float3 _lastCameraPos;
        private float3 _velocity;

        // Highest chunk-Y that generation can produce content for.
        //
        // Two distinct jobs, both load-bearing:
        //   1. Drives the shader's content-ceiling early exit
        //      (RaymarchFeature.ContentCeilingVoxelY), replacing the hardcoded
        //      128 that Raymarch.compute flagged as an assumption.
        //   2. Bounds the streaming workload independently of the window's
        //      height -- see RebuildPendingSet.
        // Both go stale together the moment generation grows vertically, which
        // is why they read one constant instead of two.
        public const int MAX_GENERATED_CHUNK_Y = 0;

        // =====================================================================
        // Radius derivation. THE EVICT RADIUS IS WHAT MUST FIT, NOT THE LOAD
        // RADIUS -- eviction always sits HYSTERESIS_RING_CHUNKS further out.
        // =====================================================================

        /// Largest Chebyshev radius (in chunks, around the camera chunk) that is
        /// guaranteed to lie fully inside the window.
        ///
        /// The window is NOT symmetric about the camera. SlideWindow sets
        /// origin = camChunk - dims/2, so a 32-chunk window spans
        /// camChunk-16 .. camChunk+15. The positive side is the binding one, so
        /// the answer is (dims/2 - 1) = 15, not dims/2 = 16.
        ///
        /// Getting this off by one does not crash. It leaves chunks exactly one
        /// step outside the window still classified as non-evictable, so they
        /// keep a ring slot that an incoming chunk then aliases onto -- phantom
        /// terrain on the +X/+Z edge only, intermittent, and far harder to find
        /// than a startup exception. Hence the assert in the constructor.
        public static int MaxEvictRadiusChunks(int3 windowDims)
            => (math.min(windowDims.x, windowDims.z) / 2) - 1;

        /// The load radius that leaves exactly enough room for the hysteresis
        /// ring. Callers should derive from this rather than picking a number
        /// that can silently disagree with WINDOW_CHUNKS_XZ later.
        public static int MaxLoadRadiusChunks(int3 windowDims)
            => MaxEvictRadiusChunks(windowDims) - EngineConfig.HYSTERESIS_RING_CHUNKS;

        /// One chunk being generated by SCHEDULED JOBS rather than by a raw
        /// worker thread. Main-thread state: created in DispatchLoads, harvested
        /// in HarvestJobLoads, never touched from a worker.
        private sealed class JobLoad
        {
            public int3 coord;
            public ushort generation;
            public ScratchContext scratch;
            public GeneratedChunk gen;
            public ChunkGeneratorFull.GenJobResources res;
            public JobHandle handle;
        }

        private readonly List<JobLoad> _jobLoads = new List<JobLoad>();

        private sealed class ScratchContext
        {
            public BrickDataPool pool;
            public ChunkHandleAllocator allocator;
            public byte[] scratchBody = new byte[EngineConfig.BRICK_BODY_BYTES];

            // Reusable downsample buffers, one set per worker. Sized once; every
            // chunk this worker handles reuses them instead of allocating a
            // fresh 2 MB tier-0 extraction plus per-step arrays per tier.
            public LODDownsampler.DownsampleScratch downsample = new LODDownsampler.DownsampleScratch();
        }

        private struct LoadCompletion
        {
            public int3 coord;
            public ushort generation;
            public Chunk chunk;              // built in scratch storage
            public byte[][] cascadeData;     // [tier-1] = downsampled voxels, computed ON THE WORKER
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

            int maxEvict = MaxEvictRadiusChunks(_windowDims);
            if (_evictRadiusChunks > maxEvict)
                throw new InvalidOperationException(
                    $"[StreamManager] evict radius {_evictRadiusChunks} chunks (load {loadRadiusChunks} + " +
                    $"hysteresis {EngineConfig.HYSTERESIS_RING_CHUNKS}) exceeds the maximum {maxEvict} for a " +
                    $"{_windowDims.x}x{_windowDims.z} window. Use MaxLoadRadiusChunks() to derive the load " +
                    "radius instead of hard-coding it. A chunk outside the window still occupies a ring slot " +
                    "that a NEW chunk will alias onto, so the window must strictly contain the eviction radius.");

            // §0.1 invariant 8: the limit lives in EngineConfig, not here.
            // 0 = derive from CPU topology.
            //
            // DERIVED FROM PERFORMANCE CORES, NOT ProcessorCount. This is a
            // 4P+4E machine and ProcessorCount reports 8, so the old
            // ProcessorCount-1 = 7 oversubscribed the four cores that can
            // actually run generation quickly -- and those are the same cores
            // Unity's main and render threads need. The four efficiency cores
            // it was also counting cannot help the main thread at all.
            //
            // CpuTopology reports Available=false rather than inventing a number
            // when the sysctl keys are missing (Intel Mac, non-macOS), in which
            // case this falls back to the historical derivation unchanged.
            // MEASURED: deriving from performance cores (= 4 here) is NOT better.
            // It selects a point on the same trade-off curve rather than moving
            // it, because a thread COUNT does not pin threads to cores -- macOS
            // schedules them wherever it likes either way:
            //
            //     workers  Gate C p50   Gate C p99   deficit p50
            //        7       21.76        860           0
            //        4       95.49        647          15
            //        2       51.61        326         202
            //
            // p99 improves at 4 but p50 is 4x worse and the world stops keeping
            // up. Default therefore stays on the historical derivation; the
            // topology is queried and logged because it is genuinely useful
            // diagnostic context, not because it should pick this number.
            _maxConcurrentLoads = EngineConfig.CHUNK_GEN_WORKER_THREADS > 0
                ? EngineConfig.CHUNK_GEN_WORKER_THREADS
                : math.max(2, Environment.ProcessorCount - 1);

            Debug.Log($"[StreamManager] chunk-gen workers: {_maxConcurrentLoads}  " +
                      $"(cpu topology: {VoxelEngine.Diagnostics.CpuTopology.Describe()})");

            // Prime the ThreadPool so PrimeWindow's Parallel.For gets real
            // threads immediately instead of after seconds of slow injection.
            ThreadPool.GetMinThreads(out _, out int minIo);
            ThreadPool.SetMinThreads(Environment.ProcessorCount * 2, minIo);

            _jobs = new System.Collections.Concurrent.BlockingCollection<WorkItem>();
            _workerThreads = new Thread[_maxConcurrentLoads];
            for (int i = 0; i < _maxConcurrentLoads; i++)
            {
                _workerThreads[i] = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = $"ChunkGen{i}",
                };
                _workerThreads[i].Start();
            }
            // TWO scratch contexts per worker, not one. Dispatch runs once per
            // frame; with a single context per worker, a worker that finishes
            // mid-frame sits idle until the next Update hands it a scratch.
            // Double-buffering keeps a queued job ready per worker: measured
            // flight-time supply was ~44 chunks/s against 7 workers that manage
            // 88/s when pumped continuously, i.e. roughly half the pipeline was
            // idle time between dispatches.
            for (int i = 0; i < _maxConcurrentLoads * 2; i++)
            {
                _scratchPool.Add(new ScratchContext
                {
                    // 4096 = worst case, an entirely dense chunk (§11.4).
                    pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK),
                    allocator = new ChunkHandleAllocator(2),
                });
            }

            System.IO.Directory.CreateDirectory(_deltaDir);
            ScanDeltaDirectory();
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

            bool cameraMoved = !camChunk.Equals(_lastCameraChunk);
            if (allowSlide && cameraMoved)
            {
                _lastCameraChunk = camChunk;
                SlideWindow(camChunk);
                RebuildPendingSet(camChunk);
                EvictOutOfRange(camChunk);
            }
            else if (_pending.Count == 0 && _inFlight == 0 && _completions.Count == 0)
            {
                // Re-sweep whenever the queue drains, not only when the camera
                // crosses a boundary. Admission has several legitimate skip
                // paths (chunk already resident, record mid-Loading, worker
                // slots exhausted), and any of them can leave a coord unloaded
                // with nothing scheduled to notice. Without this sweep the world
                // stays permanently incomplete and looks exactly like a
                // generation bug.
                //
                // Cheap: 27x27 dictionary probes over the load square, and only
                // on frames where nothing is in flight.
                RebuildPendingSet(camChunk);
            }

            DispatchLoads(camChunk);
            DrainCompletions();
            ApplyPoolPressureValve(camChunk);

            // EDIT PROPAGATION. SetVoxel sets chunk.dirty and nothing else --
            // through every run so far, no code path carried an in-place edit
            // to the mirrors' dirty sets, so an edit only became visible after
            // its chunk was evicted and reloaded. Gate D passed regardless
            // because its checks are CPU-side, which is exactly the kind of
            // blind spot the visual captures exist to catch. Edited chunks DO
            // need the main-thread cascade path (content changed, so the
            // worker-computed downsample is stale), which is what the budgeted
            // UploadDirty remains for.
            foreach (var chunk in _store.ResidentChunks())
            {
                if (!chunk.dirty) continue;
                chunk.dirty = false;
                _clipmap.MarkDirty(chunk.coord);
                _cascades.MarkDirty(chunk.coord);
            }

            LastDrainMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var stats = _clipmap.UploadDirty(_store, _pool, EngineConfig.MAX_CLIPMAP_UPLOAD_BYTES_PER_FRAME,
                                             camChunk, EngineConfig.UPLOAD_EXEMPT_RADIUS_CHUNKS);
            double clipDone = sw.Elapsed.TotalMilliseconds;
            _cascades.UploadDirty(_store, _pool);
            LastUploadMs = sw.Elapsed.TotalMilliseconds;
            LastCascadeMs = LastUploadMs - clipDone;
            LastUploadBytes = stats.bytesUploaded;
            LastUploadStats = stats;
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

            // CONTENT LAYERS ONLY, not the full window height.
            //
            // WINDOW_CHUNKS_Y is 16 (see EngineConfig's header for why it could
            // not be 2), but WorldGenConstants clamps terrain to y in [1,120],
            // entirely inside cy=0. Iterating the whole window here would queue
            // 31x31x16 = 15,376 chunks at startup instead of 961, and make every
            // slab crossing 8.0 MB of clipmap instead of 0.5 MB -- 15 of every 16
            // chunks being a guaranteed-empty air layer that costs a generate, a
            // table entry, an upload and an eviction each.
            //
            // So the window's HEIGHT is address space; this loop is workload.
            // Keeping them separate is what makes Y=16 cost memory only.
            //
            // Raise MAX_GENERATED_CHUNK_Y the moment generation produces
            // anything outside cy=0. It is a correctness dependency, not a
            // preference: chunks outside this range are never admitted, so they
            // would render as air no matter what the generator produced. The
            // assert below fails loudly rather than leaving that silent.
            if (MAX_GENERATED_CHUNK_Y >= _windowDims.y)
                throw new InvalidOperationException(
                    $"[StreamManager] MAX_GENERATED_CHUNK_Y={MAX_GENERATED_CHUNK_Y} does not fit in a " +
                    $"{_windowDims.y}-chunk-tall window. Raise EngineConfig.WINDOW_CHUNKS_Y.");

            for (int cy = 0; cy <= MAX_GENERATED_CHUNK_Y; cy++)
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
            bool scheduledAny = false;
            while (_pending.Count > 0 && _inFlight < _maxConcurrentLoads * 2)
            {
                int3 coord = _pending[0];
                _pending.RemoveAt(0);
                _pendingSet.Remove(coord);

                if (!_store.IsInWindow(coord) || _store.IsResident(coord)) continue;

                var rec = GetOrCreateRecord(coord);
                if (rec.state != ChunkState.Unloaded) continue;

                if (!_scratchPool.TryTake(out ScratchContext scratch))
                {
                    // PUT IT BACK. This line used to be a bare `break`, which
                    // silently discarded a chunk that had ALREADY been dequeued
                    // two statements earlier -- and since its record stays
                    // Unloaded and RebuildPendingSet only runs on a camera-chunk
                    // change, it was never retried.
                    //
                    // Measured cost of that bug: the first acceptance run
                    // reported "380 chunks resident" where load radius 13 asks
                    // for 27x27 = 729. Every frame in which all worker slots
                    // were busy lost one chunk, which is what produced the holes
                    // and the ragged window edge.
                    _pending.Insert(0, coord);
                    _pendingSet.Add(coord);
                    break;
                }

                // Unloaded -> Loading. Condition: "free residency slot exists".
                // The slot is free iff nothing resident currently occupies this
                // coord's ring slot -- checked via IsResident above plus the
                // aliasing guard in ChunkStore.InsertChunk.
                ChunkLifecycle.Transition(rec, ChunkState.Loading, conditionMet: true, context: "DispatchLoads");

                Interlocked.Increment(ref _inFlight);

                // ---- HYBRID DISPATCH ----
                //
                // A chunk with a delta on disk keeps the raw-worker path
                // untouched: replaying edits means reading and decoding a FILE,
                // which a job cannot do, and porting that would touch the
                // frozen delta serialization (0.3). A chunk with no delta has
                // no file to read, so its whole pipeline -- column sample,
                // brick fill, tier-0 gather, downsample chain -- is pure
                // compute over blittable state and can be scheduled.
                //
                // Both paths produce an identical LoadCompletion and go through
                // the same DrainCompletions admission, so the 4.4 state machine
                // sees exactly one shape of completion regardless of route.
                if (_deltaOnDisk.Contains(coord))
                {
                    _jobs.Add(new WorkItem { coord = coord, generation = rec.generation, scratch = scratch });
                }
                else
                {
                    ScheduleJobLoad(coord, rec.generation, scratch);
                    scheduledAny = true;
                }
            }

            // Kick the batch once, not per chunk: Schedule() only queues, and
            // without this the jobs would not start until something else forced
            // a flush -- typically the Complete() in the NEXT frame's harvest,
            // which would serialise the very work this exists to overlap.
            if (scheduledAny) JobHandle.ScheduleBatchedJobs();
        }

        /// Schedules the whole per-chunk pipeline and returns immediately.
        /// MAIN THREAD -- Unity permits scheduling from nowhere else, which is
        /// the reason the delta path cannot simply be moved here too.
        private void ScheduleJobLoad(int3 coord, ushort generation, ScratchContext scratch)
        {
            var load = new JobLoad
            {
                coord = coord,
                generation = generation,
                scratch = scratch,
                gen = GeneratedChunk.Create(Allocator.Persistent),
            };

            JobHandle h = ChunkGeneratorFull.ScheduleChunkNative(
                in _samplerState, _meta, coord, ref load.gen, out load.res);

            // The downsample chains onto generation by JobHandle instead of
            // waiting for it -- the whole point of Stage 5. It reads the native
            // GeneratedChunk, which is why Tier0ExtractJob had to exist.
            load.handle = LODDownsampler.ScheduleAllTiersFromNative(
                in load.gen, scratch.downsample, h);

            _jobLoads.Add(load);
        }

        /// Turns finished job loads into ordinary LoadCompletions.
        ///
        /// Polls IsCompleted rather than blocking: a chunk that is not done yet
        /// simply stays in the list for a later frame, exactly as an unfinished
        /// worker load stays out of the completion queue.
        private void HarvestJobLoads()
        {
            for (int i = _jobLoads.Count - 1; i >= 0; i--)
            {
                JobLoad load = _jobLoads[i];
                if (!load.handle.IsCompleted) continue;

                _jobLoads.RemoveAt(i);
                load.handle.Complete();   // required even when IsCompleted, to release the handle

                var completion = new LoadCompletion
                {
                    coord = load.coord,
                    generation = load.generation,
                    scratch = load.scratch,
                };

                try
                {
                    load.gen.denseCount = load.res.denseOut[0];

                    // Materialise into scratch storage, exactly where the worker
                    // path leaves its chunk, so TransferToSharedPool downstream
                    // is unchanged.
                    var chunk = new Chunk();
                    if (!GeneratedChunkConverter.TryToChunk(
                            in load.gen, load.coord, chunk, load.scratch.allocator, load.scratch.pool))
                        throw new InvalidOperationException(
                            $"scratch pool could not fit generated chunk {load.coord} " +
                            $"({load.gen.denseCount} dense bricks)");

                    completion.cascadeData = new byte[LODConfig.TIER_COUNT - 1][];
                    for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                    {
                        var src = load.scratch.downsample.TierOut[tier - 1];
                        var owned = new byte[src.Length];
                        src.CopyTo(owned);
                        completion.cascadeData[tier - 1] = owned;
                    }

                    completion.chunk = chunk;
                }
                catch (Exception e)
                {
                    completion.error = e;
                }
                finally
                {
                    load.res.Dispose();
                    load.gen.Dispose();
                    _completions.Enqueue(completion);
                    Interlocked.Decrement(ref _inFlight);
                }
            }
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (WorkItem item in _jobs.GetConsumingEnumerable(_cancel.Token))
                    GenerateOnWorker(item.coord, item.generation, item.scratch);
            }
            catch (OperationCanceledException) { /* shutdown */ }
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

                // Downsample HERE, on the worker, while it exclusively owns the
                // chunk and its scratch pool (§3.2 holds: no shared state read).
                // Measured cost being moved off the main thread: ~11ms per
                // chunk-tier in the Editor -- the entirety of the 22ms/frame
                // p50 the cascade pass was charging every frame.
                //
                // The downsample now runs into this worker's REUSABLE buffers,
                // and only the finished result is copied into the completion.
                // That copy is deliberate and is the single allocation kept
                // here: the completion crosses to the main thread through the
                // queue, so ownership genuinely transfers at exactly this point
                // and the scratch buffer is free to be overwritten by the next
                // chunk. Handing the scratch array itself to the queue would
                // alias -- the next chunk on this worker would rewrite voxels
                // the main thread has not consumed yet.
                completion.cascadeData = new byte[LODConfig.TIER_COUNT - 1][];
                // Gather tier-0 ONCE for all tiers. It is a 128^3 (2 MB) walk and
                // it was being redone per tier, so every chunk paid it twice --
                // pure duplicated worker CPU, on the threads whose contention
                // with the main thread is what starves frames in the first place.
                bool tier0Ready = LODDownsampler.PrepareTier0(chunk, scratch.pool, scratch.downsample);
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                {
                    var scratchResult =
                        LODDownsampler.DownsampleTierFromScratch(chunk, tier, scratch.downsample, tier0Ready);
                    var owned = new byte[scratchResult.Length];
                    scratchResult.CopyTo(owned);
                    completion.cascadeData[tier - 1] = owned;
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
            // Every drain site -- per-frame, unbounded startup, WaitForIdle --
            // routes through here, so harvesting at the top means none of them
            // needed changing and none can accidentally skip the job path.
            HarvestJobLoads();

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
                        GenerationErrors++;
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
                    if (c.cascadeData != null)
                        for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                            _cascades.TierPool(tier).SubmitPrecomputed(c.coord, c.cascadeData[tier - 1]);
                    else
                        _cascades.MarkDirty(c.coord); // fallback: main-thread path

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

                // Bulk copy, not a 512-iteration indexer loop. The indexer is
                // bounds-checked in the Editor, so the old loop cost ~150,000
                // checked accesses per chunk on the MAIN thread -- a large part
                // of why the first run measured 40ms/chunk against Phase 3's
                // 3.8ms.
                NativeArray<byte>.Copy(src, srcIdx * body, dst, dstIdx * body, body);

                // Return the scratch slot immediately. ResetScratch used to
                // Dispose and reallocate a 2 MB BrickDataPool per completed
                // chunk instead; freeing the slots the chunk actually used
                // restores the scratch pool exactly and allocates nothing.
                scratch.pool.Free(srcIdx);

                chunk.bricks[i].data = 0x80000000u | (uint)dstIdx;
            }

            // Hand the scratch handle array back so the real allocator owns it.
            var real = _allocator.Alloc();
            Array.Copy(chunk.bricks, real, EngineConfig.BRICKS_PER_CHUNK);
            scratch.allocator.Free(chunk.bricks);
            chunk.bricks = real;
        }

        /// Scratch slots are returned in TransferToSharedPool as each dense
        /// brick is copied out, so by the time we get here the pool should
        /// already be empty. This is now a no-op that exists as a seam: if a
        /// future generator path allocates a slot it does not hand back via
        /// chunk.bricks, the scratch pool would slowly fill and eventually throw
        /// from Alloc. ScratchExhaustionWarnings counts that, loudly.
        private static void ResetScratch(ScratchContext scratch) { }

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

            // Mark the mirrors dirty AFTER the store drops the chunk, so their
            // GetChunk returns null and they take the CLEAR path.
            //
            // Without this the GPU keeps describing terrain that no longer
            // exists, pointing at pool slots already reissued to other chunks
            // -- the holes and garbage geometry seen on the first run. The
            // ordering matters: mark before the evict and they would happily
            // re-upload the chunk they were meant to erase.
            _clipmap.MarkDirty(coord);
            _cascades.MarkDirty(coord);

            ChunksEvictedTotal++;
            return saved;
        }

        /// One enumeration of _deltaDir at startup. O(files on disk), paid once,
        /// replacing what would otherwise be one stat per chunk dispatch for the
        /// lifetime of the run.
        private void ScanDeltaDirectory()
        {
            _deltaOnDisk.Clear();
            foreach (string full in System.IO.Directory.EnumerateFiles(_deltaDir, "*.delta"))
            {
                if (DeltaCodec.TryParseFileName(System.IO.Path.GetFileName(full), out int3 c))
                    _deltaOnDisk.Add(c);
            }
        }

        /// Whether this chunk has edits on disk that a load must replay (§4.2).
        /// MAIN THREAD.
        public bool HasDeltaOnDisk(int3 coord) => _deltaOnDisk.Contains(coord);

        /// Test/diagnostic hook: does the in-memory set actually agree with the
        /// filesystem? The set is only sound while the engine is the directory's
        /// sole writer, and that claim deserves to be checkable rather than
        /// merely asserted in a comment.
        public bool DeltaSetMatchesDisk()
        {
            var onDisk = new HashSet<int3>();
            foreach (string full in System.IO.Directory.EnumerateFiles(_deltaDir, "*.delta"))
                if (DeltaCodec.TryParseFileName(System.IO.Path.GetFileName(full), out int3 c))
                    onDisk.Add(c);
            return onDisk.SetEquals(_deltaOnDisk);
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
                    _deltaOnDisk.Remove(coord);
                    if (_table.TryGetValue(coord, out var r0)) r0.deltaByteLength = 0;
                    return true;
                }

                DeltaCodec.WriteAtomic(path, bytes);
                _deltaOnDisk.Add(coord);
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

        /// Bulk-generates the whole load square in parallel, on the main thread's
        /// timeline, for STARTUP ONLY.
        ///
        /// WHY THIS EXISTS INSTEAD OF JUST CALLING WaitForIdle:
        /// WaitForIdle drives the normal streaming pipeline, which caps itself at
        /// _maxConcurrentLoads in-flight Tasks and hands results back one drain
        /// at a time. Measured on the acceptance run that produced this method:
        /// 399 chunks in 16,239ms, i.e. ~40ms per chunk of WALL time. Phase 3
        /// generated 484 chunks in 1,840ms with Parallel.For over the SAME
        /// generator -- ~3.8ms per chunk. Per-chunk CPU cost is therefore ~30ms
        /// and Phase 3 was getting ~8x parallelism where the Task pipeline was
        /// getting roughly 1x.
        ///
        /// Rather than debug the Task scheduling under a startup burst, priming
        /// uses the mechanism that was already measured fast, restricted to the
        /// one case that needs bulk throughput. The steady-state streaming path
        /// is untouched: it is latency-bound (a handful of chunks per frame) and
        /// the Task pipeline is the right shape for it.
        ///
        /// WAVES, not one big Parallel.For: a worker cannot generate a second
        /// chunk until the main thread has transferred the first out of its
        /// scratch pool (that is what frees the scratch slots, and it is
        /// single-writer main-thread work per §3.2). So each wave generates one
        /// chunk per worker in parallel, then the main thread drains all of them.
        /// Bounded memory: workers x 2 MB of scratch, nothing else.
        /// Fills the initial load square by pumping the SAME dedicated-thread
        /// pipeline the steady state uses, as fast as the main thread can drain
        /// it -- no frame pacing, no Parallel.For.
        ///
        /// History, since this method has now been rewritten twice on evidence:
        /// the Task.Run pipeline measured ~40ms/chunk wall; Parallel.For waves
        /// measured 28.9-33ms/chunk and SetMinThreads did NOT improve it,
        /// which falsified the ThreadPool-starvation theory for Parallel.For;
        /// the dedicated threads measured 11.3ms/chunk during Gate C's refill
        /// (6.2s for ~550 chunks) -- the best observed by 2.5x. So startup now
        /// uses the dedicated threads too. Whether 11.3ms/chunk is itself
        /// thread-limited or work-limited is answered by the rig's new
        /// generation micro-benchmark, not guessed at here.
        public void PrimeWindow(Vector3 cameraWorldPos)
        {
            var primeSw = System.Diagnostics.Stopwatch.StartNew();

            float3 camPos = new float3(cameraWorldPos.x, cameraWorldPos.y, cameraWorldPos.z);
            _lastCameraPos = camPos;
            int3 camChunk = CoordMath.VoxelToChunk(CoordMath.WorldToVoxel(camPos));
            camChunk.y = 0;
            _lastCameraChunk = camChunk;
            SlideWindow(camChunk);
            RebuildPendingSet(camChunk);

            int before = ChunksAdmittedTotal;
            while ((_pending.Count > 0 || _inFlight > 0 || _completions.Count > 0)
                   && primeSw.Elapsed.TotalSeconds < 90)
            {
                DispatchLoads(camChunk);
                DrainCompletionsUnbounded();
                if (_inFlight > 0 && _completions.Count == 0) Thread.Sleep(1);
            }

            PrimeChunks = ChunksAdmittedTotal - before;
            PrimeGenerateMs = primeSw.Elapsed.TotalMilliseconds;
            PrimeTransferMs = 0; // folded into the pipeline drain; see micro-benchmark
            PrimeWaves = 0;
            Debug.Log($"[StreamManager] PrimeWindow: {PrimeChunks} chunks in {PrimeGenerateMs:F0}ms via the " +
                      $"worker pipeline ({(PrimeChunks > 0 ? PrimeGenerateMs / PrimeChunks : 0):F1}ms/chunk wall)");
        }

        /// Drain without the per-frame admission cap -- startup and refill-wait
        /// only, where there is no frame budget to protect.
        private void DrainCompletionsUnbounded()
        {
            int guard = 0;
            while (_completions.Count > 0 && guard++ < 100000) DrainCompletions();
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

        /// Chunk coords inside the load square that are NOT resident right now.
        /// This is the number the player experiences as "terrain loading slower
        /// than I move": zero means the visible world is complete, and its decay
        /// after a teleport is the refill rate. The rig samples it every frame.
        public int LoadDeficit()
        {
            int deficit = 0;
            int r = _loadRadiusChunks;
            for (int cy = 0; cy <= MAX_GENERATED_CHUNK_Y; cy++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                if (!_store.IsResident(new int3(_lastCameraChunk.x + dx, cy, _lastCameraChunk.z + dz)))
                    deficit++;
            return deficit;
        }

        public void Dispose()
        {
            // In-flight scheduled loads own Persistent NativeArrays. They must
            // be completed before their inputs are freed -- tearing down under a
            // running job is a crash, not a leak warning.
            for (int i = 0; i < _jobLoads.Count; i++)
            {
                _jobLoads[i].handle.Complete();
                _jobLoads[i].res.Dispose();
                _jobLoads[i].gen.Dispose();
            }
            _jobLoads.Clear();

            _samplerState.Dispose();   // owns NativeArrays since the Burst port
            if (_scratchPool != null)
                foreach (var sc in _scratchPool) sc.downsample?.Dispose();
            _cancel.Cancel();
            _jobs?.CompleteAdding();
            if (_workerThreads != null)
                foreach (var t in _workerThreads) t?.Join(100);
            while (_scratchPool.TryTake(out var s)) s.pool?.Dispose();
            _cancel.Dispose();
        }
    }
}