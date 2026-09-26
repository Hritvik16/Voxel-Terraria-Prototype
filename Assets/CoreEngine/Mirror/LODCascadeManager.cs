// Assets/CoreEngine/Mirror/LODCascadeManager.cs
//
// Owns one CascadeTierPool per non-zero tier (tiers 1..LODConfig.TIER_COUNT-1
// - tier 0 is the existing TerrainClipmap, untouched). Mirrors the
// TerrainClipmap.Active static-instance pattern so RaymarchFeature can reach
// it the same way it reaches TerrainClipmap.Active.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.Mirror
{
    public class LODCascadeManager : IDisposable
    {
        public static LODCascadeManager Active { get; private set; }

        private readonly CascadeTierPool[] _tierPools; // index 0 unused (tier 0 has no cascade pool)

        public CascadeTierPool TierPool(int tier)
        {
            if (tier <= 0 || tier >= LODConfig.TIER_COUNT)
                throw new ArgumentOutOfRangeException(nameof(tier), $"No cascade pool for tier {tier}.");
            return _tierPools[tier];
        }

        // PLACEHOLDER, NOT MEASURED. ARCHITECTURE_v8.6.md §11.3 lists cascade
        // pool memory as "[Phase 2 gate]" - an open measurement, not a derived
        // budget. This follows §11.3's own stated philosophy ("sized
        // aggressively low first, raised only if measurement allows") applied
        // to a case that section doesn't actually cover a number for yet.
        // 1/16 of the tier-0 brick pool cap per tier is a guess sized on the
        // intuition that coarse terrain should be far more uniform (fewer
        // dense bricks) than fine terrain, not on any measurement. Treat
        // BrickDataPool's "pool exhausted" exception, if it ever fires, as
        // the signal to raise this - not as a bug to silence.
        // Bumped from /16 to /4 after moving Phase2Bootstrapper's generation
        // from 8x8 to 22x22 chunks (~7.6x more chunks) - the old /16 value
        // was sized against the smaller world and was very likely to exhaust
        // (rough estimate: non-uniform coarse bricks concentrate near the
        // terrain surface, not throughout the volume, so per-chunk count
        // doesn't scale with full chunk volume - but 7.6x more chunks alone
        // likely pushes past the old ~46875 cap even so). /4 is STILL a
        // guess, not a measured number - same "not measured" status as
        // before, just less likely to immediately throw. If
        // BrickDataPool's exhaustion exception fires again, that remains
        // the correct signal to raise this further, not a bug to silence.
        /// DEPRECATED SIZING, kept only for the Phase 2/3 bootstrappers.
        /// "/4" was never measured; EngineConfig.CascadeTierPoolCap(tier) now
        /// carries per-tier caps derived from BrickDataPool.PeakUsed. Phase 4
        /// uses that instead.
        public static int DefaultTierPoolCapacity(int brickPoolCapTier0) => Math.Max(1024, brickPoolCapTier0 / 4);

        public LODCascadeManager(int3 windowDimsChunks, Func<int, int> tierPoolCapacity)
        {
            _tierPools = new CascadeTierPool[LODConfig.TIER_COUNT];
            _batchScratch = new List<int3>[LODConfig.TIER_COUNT];
            for (int t = 0; t < LODConfig.TIER_COUNT; t++) _batchScratch[t] = new List<int3>();
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
            {
                int capacity = tierPoolCapacity(tier);
                _tierPools[tier] = new CascadeTierPool(tier, windowDimsChunks, capacity);
            }
            Active = this;
        }

        public void MarkDirty(int3 chunkCoord)
        {
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier].MarkDirty(chunkCoord);
            if (_asyncStarted)
                _editEpoch[chunkCoord] = _editEpoch.TryGetValue(chunkCoord, out int e) ? e + 1 : 1;
        }

        // =====================================================================
        // ASYNC REBUILD (2026-09-26). Edit-driven cascade rebuilds -- the live
        // fluid dirties its chunks every tick -- ran the whole 128^3 gather and
        // halving chain ON THE MAIN THREAD, and a single chunk costs more than
        // the frame budget meant to bound it ("at least one chunk always runs").
        // -framegap measured it: cascade 7.7 ms in the median frame, 16.2 ms in
        // the worst 1%. Admission already moved this work to the chunk
        // workers; edits now do the same:
        //   main    copies the chunk's 4,096 handles and its dense bodies into a
        //           pre-allocated snapshot CONTEXT it owns (a private chunk, a
        //           private pool, downsample scratch, per-tier result buffers);
        //   worker  runs the same BuildChain on that snapshot -- it reads only
        //           the context, never the store or the shared pool;
        //   main    writes the result through the tier's normal write path. An
        //           edit epoch per chunk decides whether the chunk is now clean
        //           or was edited again in flight (then it stays dirty).
        // Ownership of a context moves main -> worker -> main through the two
        // concurrent queues, exactly as StreamManager's scratch contexts do; no
        // lock guards the world. No allocation per job, so no new garbage.
        // -cascadeasync 0 restores the synchronous path.
        // =====================================================================
        /// OFF by default in code, so every existing cascade test keeps its
        /// synchronous "one UploadDirty and the tiers are current" contract; the
        /// player's bootstrapper turns it on (Phase4Bootstrapper, -cascadeasync).
        public static bool AsyncRebuildEnabled = false;
        public static bool AsyncRequestedByArgs() => ReadArg("-cascadeasync", 1) != 0;
        private const int ASYNC_CONTEXTS = 3;

        private sealed class AsyncContext
        {
            public int3 coord;
            public int epoch;
            public readonly Chunk chunk = new Chunk { bricks = new BrickHandle[EngineConfig.BRICKS_PER_CHUNK] };
            public readonly BrickDataPool pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            public readonly LODDownsampler.DownsampleScratch scratch = new LODDownsampler.DownsampleScratch();
            public readonly byte[][] results = new byte[LODConfig.TIER_COUNT][];
        }

        private bool _asyncStarted;
        private readonly Dictionary<int3, int> _editEpoch = new Dictionary<int3, int>();
        private readonly HashSet<int3> _inFlight = new HashSet<int3>();
        private readonly Stack<AsyncContext> _freeContexts = new Stack<AsyncContext>();
        private readonly List<AsyncContext> _allContexts = new List<AsyncContext>();
        private System.Collections.Concurrent.BlockingCollection<AsyncContext> _asyncJobs;
        private readonly System.Collections.Concurrent.ConcurrentQueue<AsyncContext> _asyncDone =
            new System.Collections.Concurrent.ConcurrentQueue<AsyncContext>();
        private System.Threading.Thread _asyncThread;
        private System.Threading.CancellationTokenSource _asyncCancel;
        public int AsyncRebuildsTotal { get; private set; }
        public bool IsRebuildInFlight(int3 chunkCoord) => _inFlight.Contains(chunkCoord);

        private static int ReadArg(string flag, int fallback)
        {
            int v = fallback;
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == flag && int.TryParse(a[i + 1], out int p)) v = p;
            return v;
        }

        private void StartAsync()
        {
            _asyncStarted = true;
            for (int i = 0; i < ASYNC_CONTEXTS; i++)
            {
                var ctx = new AsyncContext();
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                {
                    int edge = 128 / LODConfig.DownsampleFactor(tier);
                    ctx.results[tier] = new byte[edge * edge * edge];
                }
                _allContexts.Add(ctx);
                _freeContexts.Push(ctx);
            }
            _asyncJobs = new System.Collections.Concurrent.BlockingCollection<AsyncContext>();
            _asyncCancel = new System.Threading.CancellationTokenSource();
            _asyncThread = new System.Threading.Thread(AsyncLoop) { IsBackground = true, Name = "CascadeRebuild" };
            _asyncThread.Start();
        }

        // ---- WORKER THREAD. Touches only the context it was handed. ----
        private void AsyncLoop()
        {
            try
            {
                foreach (var ctx in _asyncJobs.GetConsumingEnumerable(_asyncCancel.Token))
                {
                    LODDownsampler.BuildChain(ctx.chunk, ctx.pool, ctx.scratch);
                    for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                    {
                        byte[] r = LODDownsampler.ChainResultFor(tier, ctx.scratch);
                        Buffer.BlockCopy(r, 0, ctx.results[tier], 0, r.Length);
                    }
                    _asyncDone.Enqueue(ctx);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// Main thread: copy a resident, non-uniform chunk into a free context.
        private bool TrySubmitAsync(Chunk chunk, BrickDataPool pool)
        {
            if (_freeContexts.Count == 0) return false;
            var ctx = _freeContexts.Pop();
            ctx.pool.Reset();
            ctx.coord = chunk.coord;
            ctx.chunk.coord = chunk.coord;
            ctx.chunk.isUniform = false;
            ctx.epoch = _editEpoch.TryGetValue(chunk.coord, out int e) ? e : 0;
            var src = pool.RawData;
            var dst = ctx.pool.RawData;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                uint h = chunk.bricks[i].data;
                if ((h & 0x80000000u) == 0) { ctx.chunk.bricks[i].data = h; continue; }
                int from = (int)(h & 0x7FFFFFFFu), to = ctx.pool.Alloc();
                NativeArray<byte>.Copy(src, from * EngineConfig.BRICK_BODY_BYTES, dst, to * EngineConfig.BRICK_BODY_BYTES, EngineConfig.BRICK_BODY_BYTES);
                ctx.chunk.bricks[i].data = 0x80000000u | (uint)to;
            }
            _inFlight.Add(chunk.coord);
            _asyncJobs.Add(ctx);
            AsyncRebuildsTotal++;
            return true;
        }

        /// Main thread: apply every finished rebuild.
        private void DrainAsync(ChunkStore store)
        {
            while (_asyncDone.TryDequeue(out var ctx))
            {
                _inFlight.Remove(ctx.coord);
                bool clean = (_editEpoch.TryGetValue(ctx.coord, out int e) ? e : 0) == ctx.epoch;
                if (clean) _editEpoch.Remove(ctx.coord);
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                    if (_tierPools[tier].IsDirty(ctx.coord))
                        _tierPools[tier].ApplyAsyncResult(store, ctx.coord, ctx.results[tier], clean);
                _freeContexts.Push(ctx);
            }
        }

        /// SHARED-CHAIN CASCADE UPLOAD. Default ON.
        ///
        /// Set false to restore the per-tier path byte-for-byte. That is the
        /// regression baseline every prior cascade proof was written against,
        /// and it is how the mutation check is run: flip this off and the cost
        /// must come back.
        public static bool SharedChainEnabled { get; set; } = true;

        private readonly List<int3> _unionScratch = new List<int3>();
        private readonly List<int3>[] _batchScratch;
        private LODDownsampler.DownsampleScratch _chainScratch;

        /// WHAT CHANGED AND WHAT DID NOT.
        ///
        /// NOT changed: the frame budget. MAX_CASCADE_CHUNKS_PER_FRAME still
        /// caps chunks and MAX_CASCADE_MS_PER_TIER still bounds wall clock.
        /// The cascade was ALREADY budgeted -- measured backlog under a live
        /// fluid load was p50 2, max 16 chunks and did not grow, so deferring
        /// more would only have made distant terrain staler without saving
        /// anything.
        ///
        /// Changed: the WORK. Every dirty chunk is marked dirty on EVERY tier
        /// (MarkDirty loops them), and each tier then re-gathered the chunk's
        /// 128^3 materials and re-ran the halving chain from scratch. At the
        /// shipped tier sizes tier 2 repeated 89% of tier 1's chain. Driving
        /// the tiers chunk-major computes the gather and the chain ONCE and
        /// lets each tier read its own step.
        public void UploadDirty(ChunkStore store, BrickDataPool pool)
        {
            if (!SharedChainEnabled)
            {
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                    _tierPools[tier].UploadDirty(store, pool);
                return;
            }

            if (AsyncRebuildEnabled && !_asyncStarted) StartAsync();

            // Pass 1 per tier: evicted clears (unbudgeted) + batch selection.
            _unionScratch.Clear();
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
            {
                _tierPools[tier].SelectBatch(store, _batchScratch[tier]);
                _tierPools[tier].ClearBrickSlotScratch();
                foreach (int3 c in _batchScratch[tier])
                    if (!_unionScratch.Contains(c)) _unionScratch.Add(c);
            }
            // Finished async rebuilds land AFTER pass 1: its per-tier
            // ClearBrickSlotScratch would otherwise drop the body uploads they
            // queue, and they must be flushed even when nothing else is dirty.
            if (_asyncStarted) DrainAsync(store);
            if (_unionScratch.Count == 0)
            {
                if (_asyncStarted)
                    for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++) _tierPools[tier].FlushBrickBodies();
                return;
            }

            _chainScratch ??= new LODDownsampler.DownsampleScratch();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            double budgetMs = EngineConfig.MAX_CASCADE_MS_PER_TIER * (LODConfig.TIER_COUNT - 1);
            int processed = 0;

            foreach (int3 chunkCoord in _unionScratch)
            {
                // Same shape as the per-tier guard: at least one chunk always
                // runs so the queue cannot stall, and beyond that a frame that
                // is already spent stops paying.
                if (_asyncStarted)
                {
                    // In flight: its result is coming; a re-dirty in the
                    // meantime is caught by the edit epoch when it lands.
                    if (_inFlight.Contains(chunkCoord)) continue;
                    bool stillDirty = false;
                    for (int tier = 1; tier < LODConfig.TIER_COUNT && !stillDirty; tier++) stillDirty = _tierPools[tier].IsDirty(chunkCoord);
                    if (!stillDirty) continue;   // an async result just landed clean
                    Chunk ac = store.GetChunk(chunkCoord);
                    if (ac != null && !ac.isUniform && ac.bricks != null)
                    {
                        TrySubmitAsync(ac, pool);   // no free context: stays dirty, next frame
                        continue;
                    }
                    // Null or uniform: the cheap fill path below, synchronously.
                }
                if (processed > 0 && sw.Elapsed.TotalMilliseconds > budgetMs) break;
                processed++;

                double t0 = sw.Elapsed.TotalMilliseconds;
                Chunk chunk = store.GetChunk(chunkCoord);
                int steps = LODDownsampler.BuildChain(chunk, pool, _chainScratch);
                double downMs = sw.Elapsed.TotalMilliseconds - t0;

                double t1 = sw.Elapsed.TotalMilliseconds;
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                {
                    if (!_batchScratch[tier].Contains(chunkCoord)) continue;

                    byte[] result;
                    if (steps == 0)
                    {
                        // Null or uniform chunk. A REUSED BUFFER MUST BE
                        // WRITTEN IN FULL -- it still holds the last chunk.
                        result = LODDownsampler.ChainResultFor(tier, _chainScratch);
                        byte fill = (chunk == null) ? (byte)0 : chunk.uniformMaterial;
                        if (fill == 0) Array.Clear(result, 0, result.Length);
                        else Array.Fill(result, fill);
                    }
                    else result = LODDownsampler.ChainResultFor(tier, _chainScratch);

                    _tierPools[tier].ApplyChunkFromChain(store, chunkCoord, result);
                }
                double writeMs = sw.Elapsed.TotalMilliseconds - t1;

                // Attribute the shared chain to tier 1 so the existing report
                // lines keep summing to the real total rather than counting it
                // once per tier.
                _tierPools[1].AddTimings(downMs, 0);
                _tierPools[1].AddTimings(0, writeMs);
            }

            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier].FlushBrickBodies();
        }

        public void Dispose()
        {
            if (_asyncStarted)
            {
                _asyncCancel.Cancel();
                _asyncJobs.CompleteAdding();
                _asyncThread?.Join(200);
                foreach (var ctx in _allContexts) ctx.pool.Dispose();
                _asyncCancel.Dispose();
            }
            if (Active == this) Active = null;
            for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                _tierPools[tier]?.Dispose();
        }
    }
}