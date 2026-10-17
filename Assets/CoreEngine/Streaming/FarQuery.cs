// Assets/CoreEngine/Streaming/FarQuery.cs
//
// Amendment 8.15 §7, row Q1 (task 104): IFarQuery.GetVoxels for tg2 worlds.
// Per point, the answer its chunk would LOAD with (StreamManager's worker
// order, 8.14 row 10: generation, ops <= S, D.1, ops > S):
//   1. the chunk is RESIDENT          -> ChunkStore.GetVoxel (the live voxels,
//                                        unsaved edits and every op included);
//   2. its D.1 delta holds the brick  -> the delta's byte, then ops > S
//                                        (S from the log's side table: no entry
//                                        = 0, CRC mismatch = all ops);
//   3. otherwise                      -> the generator (TerrainGenChunkSource.
//                                        PointVoxel: ShapeField + DetailField
//                                        column, exact) with every op in seq order
//                                        (a brick the delta does not hold IS
//                                        generation + ops <= S, and ops > S follow).
// A delta the codec rejects (CRC, coord, seed, structure: DeltaCodec.
// TryDecodeOnto, the same check the worker runs) is no delta: rule 3.
//
// SPLIT (single-writer, CLAUDE.md): rules 1-2 and the plan run on the MAIN
// thread (the store, the delta files); rule 3 and every op run in a Burst job
// (PointJob, parallel over batches) that reads only the source's read-only map
// arrays, a copy of the ops Snapshot and the per-point plan. No lock.
//
// DELTA INDEX CACHE: an LRU of parsed D.1 files (and of "no file") keyed by
// chunk, so a repeated query costs no file I/O. StreamManager.SaveDelta calls
// ForgetDelta for each delta it writes or deletes; anything else that writes a
// delta file (a test) must call ForgetDelta / ForgetAllDeltas itself.
//
// sizeClass 0/1 worlds (the legacy generator) have no point generator: no
// FarQuery is made for them (StreamManager.FarQuery is null).
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    public sealed class FarQuery : IFarQuery, IDisposable
    {
        /// Points per job batch (one NodeCache and column memo per batch).
        public const int BATCH = 32;
        /// Parsed delta files kept (and the byte bound on their bodies).
        public const int DELTA_CACHE_ENTRIES = 128;
        public const long DELTA_CACHE_BYTES = 16L << 20;

        private readonly IWorldQuery _world;
        private readonly IVoxelResidency _residency;
        private readonly TerrainGenChunkSource _src;
        private readonly EditOpLog _log;
        private readonly string _deltaDir;
        private readonly uint _seed;

        /// False = the job runs on the calling thread (cost-per-point reports).
        public bool Parallel = true;

        // ---- diagnostics (since construction) ----
        public long PointsResident { get; private set; }
        public long PointsDelta { get; private set; }
        public long PointsGenerated { get; private set; }
        public long DeltaFilesParsed { get; private set; }
        public long DeltaCacheHits { get; private set; }
        public int DeltaCacheCount => _cache.Count;

        public FarQuery(IWorldQuery world, IVoxelResidency residency, TerrainGenChunkSource src,
                        EditOpLog log, string deltaDir, uint seed)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _residency = residency ?? throw new ArgumentNullException(nameof(residency));
            _src = src ?? throw new ArgumentNullException(nameof(src));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _deltaDir = deltaDir;
            _seed = seed;
        }

        // =====================================================================
        // The delta index (main thread)
        // =====================================================================

        private sealed class DeltaIndex
        {
            public int3 coord;
            public byte[] bytes;    // null = no delta (absent, unreadable or rejected)
            public int[] at;        // per brick: payload offset in bytes, or 0 = not in the delta
            public uint crc;
            public long Weight => bytes == null ? 64 : bytes.Length + EngineConfig.BRICKS_PER_CHUNK * sizeof(int);

            /// The D.1 byte of voxel v, or -1 if the delta does not hold its brick.
            public int Value(int3 v)
            {
                if (bytes == null) return -1;
                int3 l = v & (EngineConfig.CHUNK_EDGE_VOXELS - 1);
                int p = at[((l.z >> 3) << 8) | ((l.y >> 3) << 4) | (l.x >> 3)];
                if (p == 0) return -1;
                if (bytes[p - 1] == (byte)DeltaBrickKind.Uniform) return bytes[p];
                return bytes[p + (((l.z & 7) << 6) | ((l.y & 7) << 3) | (l.x & 7))];
            }
        }

        private readonly Dictionary<int3, LinkedListNode<DeltaIndex>> _cache = new Dictionary<int3, LinkedListNode<DeltaIndex>>();
        private readonly LinkedList<DeltaIndex> _lru = new LinkedList<DeltaIndex>();
        private long _cacheBytes;
        private BrickDataPool _decodePool;

        /// Drops the cached index of `chunk`'s delta (its file was written or deleted).
        public void ForgetDelta(int3 chunk)
        {
            if (!_cache.TryGetValue(chunk, out var node)) return;
            _cacheBytes -= node.Value.Weight;
            _lru.Remove(node);
            _cache.Remove(chunk);
        }

        public void ForgetAllDeltas() { _cache.Clear(); _lru.Clear(); _cacheBytes = 0; }

        private DeltaIndex Delta(int3 chunk)
        {
            if (_cache.TryGetValue(chunk, out var node))
            {
                DeltaCacheHits++;
                _lru.Remove(node); _lru.AddFirst(node);
                return node.Value;
            }
            var d = Parse(chunk);
            _cache[chunk] = _lru.AddFirst(d);
            _cacheBytes += d.Weight;
            while (_lru.Count > 1 && (_lru.Count > DELTA_CACHE_ENTRIES || _cacheBytes > DELTA_CACHE_BYTES))
            {
                var last = _lru.Last;
                _cacheBytes -= last.Value.Weight;
                _cache.Remove(last.Value.coord);
                _lru.RemoveLast();
            }
            return d;
        }

        /// Reads and validates `chunk`'s delta with the worker's own decode (onto a
        /// scratch chunk, then freed), and indexes its records.
        private DeltaIndex Parse(int3 chunk)
        {
            var d = new DeltaIndex { coord = chunk };
            if (_deltaDir == null) return d;
            if (!DeltaCodec.TryReadFile(DeltaCodec.PathFor(_deltaDir, chunk), out byte[] bytes, out _)) return d;
            DeltaFilesParsed++;
            if (_decodePool == null) _decodePool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            var scratch = new Chunk { coord = chunk, isUniform = true, uniformMaterial = Materials.Air };
            bool ok = DeltaCodec.TryDecodeOnto(bytes, chunk, _seed, scratch, _decodePool, out _);
            if (scratch.bricks != null)
                for (int i = 0; i < scratch.bricks.Length; i++)
                    if ((scratch.bricks[i].data & 0x80000000u) != 0) _decodePool.Free((int)(scratch.bricks[i].data & 0x3FFFFFFFu));
            if (!ok) return d;

            // Structure is proven by the decode; later records win, as there.
            var at = new int[EngineConfig.BRICKS_PER_CHUNK];
            int p = DeltaCodec.HEADER_BYTES, end = bytes.Length - DeltaCodec.TRAILER_BYTES;
            while (p < end)
            {
                int bi = bytes[p] | (bytes[p + 1] << 8);
                byte kind = bytes[p + 2];
                at[bi] = p + DeltaCodec.RECORD_HEADER_BYTES;
                p += DeltaCodec.RECORD_HEADER_BYTES +
                     (kind == (byte)DeltaBrickKind.Uniform ? DeltaCodec.UNIFORM_PAYLOAD_BYTES : DeltaCodec.DensePayloadBytes);
            }
            d.bytes = bytes; d.at = at; d.crc = EditOpLog.TrailerCrc(bytes);
            return d;
        }

        // =====================================================================
        // GetVoxels
        // =====================================================================

        private EditOpSnapshot _opsSnap;
        private NativeArray<EditOp> _ops;

        private void SyncOps(EditOpSnapshot snap)
        {
            if (ReferenceEquals(snap, _opsSnap) && _ops.IsCreated) return;
            if (_ops.IsCreated) _ops.Dispose();
            _ops = new NativeArray<EditOp>(math.max(1, snap.Count), Allocator.Persistent);
            if (snap.Count > 0) NativeArray<EditOp>.Copy(snap.Ops, _ops, snap.Count);
            _opsSnap = snap;
        }

        public void GetVoxels(NativeArray<int3> points, NativeArray<byte> results)
        {
            int n = points.Length;
            if (results.Length < n) throw new ArgumentException($"results ({results.Length}) shorter than points ({n})");
            if (n == 0) return;
            EditOpSnapshot snap = _log.Snapshot;
            SyncOps(snap);

            // PLAN (main thread): -1 = answered here (resident); else
            // (first op to apply << 1) | (1 = generate first, 0 = results[i] is the D.1 byte).
            var plan = new NativeArray<int>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            try
            {
                int3 lastChunk = default; bool haveLast = false, resident = false;
                DeltaIndex delta = null; int s = 0;
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    int3 v = points[i];
                    int3 cc = v >> 7;
                    if (!haveLast || math.any(cc != lastChunk))
                    {
                        lastChunk = cc; haveLast = true;
                        resident = _residency.IsResident(cc);
                        delta = resident ? null : Delta(cc);
                        s = delta != null && delta.bytes != null && snap.Count > 0 ? snap.OpsBeforeDelta(cc, delta.crc) : 0;
                    }
                    if (resident) { results[i] = _world.GetVoxel(v); plan[i] = -1; PointsResident++; continue; }
                    int dv = delta.Value(v);
                    if (dv >= 0) { results[i] = (byte)dv; plan[i] = s << 1; PointsDelta++; }
                    else { plan[i] = 1; PointsGenerated++; }
                    any = true;
                }
                if (!any) return;

                var job = new PointJob
                {
                    points = points, plan = plan, results = results, count = n,
                    ops = _ops, opCount = snap.Count,
                    h = _src.PointHeight, r = _src.PointRough, w = _src.PointWater, cls = _src.PointWaterClass,
                    flow = _src.PointFlow, ore = _src.PointOre, toEngine = _src.PointToEngine,
                    spec = _src.Spec, o = _src.O,
                };
                int batches = (n + BATCH - 1) / BATCH;
                if (Parallel) job.Schedule(batches, 1).Complete();
                else job.Run(batches);
            }
            finally { plan.Dispose(); }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]   // Generate's mode: the same bytes
        private struct PointJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int3> points;
            [ReadOnly] public NativeArray<int> plan;
            [NativeDisableParallelForRestriction] public NativeArray<byte> results;   // batch b writes [b * BATCH, +BATCH)
            [ReadOnly] public NativeArray<EditOp> ops;
            public int opCount, count;
            [ReadOnly] public NativeArray<float> h, r, w;
            [ReadOnly] public NativeArray<byte> cls, ore, toEngine;
            [ReadOnly] public NativeArray<ushort> flow;
            public TerrainGen.TerrainSpec spec;
            public TerrainGen.Offsets o;

            public unsafe void Execute(int b)
            {
                var map = new TerrainGen.MapView
                {
                    height = (float*)h.GetUnsafeReadOnlyPtr(), rough = (float*)r.GetUnsafeReadOnlyPtr(),
                    water = (float*)w.GetUnsafeReadOnlyPtr(), waterClass = (byte*)cls.GetUnsafeReadOnlyPtr(),
                    flowDir = flow.Length > 1 ? (ushort*)flow.GetUnsafeReadOnlyPtr() : null,   // as ColumnJob
                    cells = spec.mapCells, cellVoxels = spec.mapCellVoxels,
                };
                var nodes = new TerrainGen.DetailField.NodeCache();
                var col = new TerrainGenChunkSource.PointColumn();
                int end = math.min(count, (b + 1) * BATCH);
                for (int i = b * BATCH; i < end; i++)
                {
                    int p = plan[i];
                    if (p < 0) continue;
                    int3 v = points[i];
                    byte m = (p & 1) != 0
                        ? TerrainGenChunkSource.PointVoxel(in spec, in o, in map, ore, toEngine, v, ref col, ref nodes)
                        : results[i];
                    for (int j = p >> 1; j < opCount; j++)
                    {
                        var op = ops[j];
                        if (EditOpApply.Covers(in op, v)) m = op.material;
                    }
                    results[i] = m;
                }
            }
        }

        public void Dispose()
        {
            if (_ops.IsCreated) _ops.Dispose();
            _decodePool?.Dispose();
            _decodePool = null;
            ForgetAllDeltas();
        }
    }
}
