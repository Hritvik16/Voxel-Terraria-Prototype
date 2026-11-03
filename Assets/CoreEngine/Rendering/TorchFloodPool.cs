// ==========================================
// Assets/CoreEngine/Rendering/TorchFloodPool.cs
//
// Task 158 (TORCH_LIGHT_PLAN.md step L2): the GPU LIGHT POOL for the torch
// flood, behind -torchflood 1, OFF by default. Task 165 (step L3, owner doc
// Q101 1 (a) + 2 (a)): INCREMENTAL and off the main thread.
//
// WHAT IT DOES. Each frame (LightingDriver.Update, main thread) Tick():
//   1. diffs the registered torch set (LightSourceBuffer.Revision, then a sorted
//      (voxel, material) signature): a removed torch drops its cache, an added
//      one is queued;
//   2. queues every torch whose 12 m box an edit touched (NotifyVoxelChanged:
//      EditService, the fluid op readback and the edit-op replay call it) AND
//      that the edit can change (task 169 cut 2, EditLitOnly: the torch lights a
//      brick of the edit grown by 1 voxel, or its own brick; exact), or whose box
//      chunks were admitted / evicted (the Chunk object per chunk);
//   3. collects finished flood jobs (one torch per job, TorchFloodBuild.
//      FloodBrickJob on a worker): the torch's new cache replaces its old one and
//      every brick either lit is queued for a re-merge;
//   4. applies a finished merge (MergeJob, worker: the union cells of a batch of
//      bricks = per-channel max over the caches that cover it) into the CPU
//      union (ApplyJob, Burst on the main thread) and uploads only the dirty
//      ranges; schedules the next merge batch;
//   5. starts new flood jobs (a main-thread Snapshot of the box, then the job)
//      while a slot is free and the frame's budget (FrameBudgetMs) lasts.
// Lights stream in over a few frames. Correct at every quiet point: the union
// equals a from-scratch rebuild exactly (TorchFloodL3Tests), and the from-scratch
// rebuild equals Encode(task 157's oracle) exactly.
//
// SINGLE WRITER. The main thread is the only writer of the union, the torch
// records and the GPU buffers. A flood job writes only the scratch and the
// cache its slot owns (filled by Snapshot before it is scheduled); a merge job
// reads caches nobody writes any more (a replaced cache is disposed only once
// no merge is in flight) and writes its own output. Nothing here writes terrain.
// The pool is derived data (not ChunkStore, not saved), so D.1 and every frozen
// API are untouched.
//
// LAYOUT: see TorchFlood.hlsl. SampleLevels() below is its C# twin, used by the
// tests and the in-player GPU probe (Probe()).
//
// KNOWN GAPS (L4, not this step): no memory cap / furthest-first fallback;
// coloured torch materials all reach the same brightness (the seed keeps the
// hue, not the emissive magnitude); a mobile emissive registered as a point
// light (AllowMobileEmissives) is ignored here. Fluid writes DO re-flood (task
// 165), so a flowing fluid inside a lit box re-floods its torches at the fluid
// tick rate.
// ==========================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using Debug = UnityEngine.Debug;

namespace VoxelEngine.Rendering
{
    public sealed unsafe class TorchFloodPool : IDisposable
    {
        // ---- switches (LightingDriver.Awake parses the flags) ----
        /// -torchflood 1. OFF = today's analytic torch path, byte for byte.
        public static bool Enabled;
        /// The world the flood reads (Phase4Bootstrapper sets it).
        public static ChunkStore Store;
        /// -tfi / -tfk / -tfreach / -tfwrap: the level -> light map (see TorchFlood.hlsl FloodLight).
        public static float Intensity = 2.0f, FalloffK = 0.10f, ReachM = LightFloodReference.DEFAULT_REACH_M, Wrap = 0.3f;
        /// -tfdir 0|1|2|3 (task 164): the direction term (TorchFlood.hlsl FloodLight). 0 = task 158's one-step rise.
        public static int DirMode;
        /// Keep a per-voxel oracle after a synchronous Rebuild (the capture's GPU == oracle probe).
        public static bool KeepOracle;

        public static void ParseArgs()
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
            {
                string v = a[i + 1];
                switch (a[i].ToLowerInvariant())
                {
                    case "-torchflood": Enabled = v == "1"; break;
                    case "-tfi": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Intensity); break;
                    case "-tfk": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out FalloffK); break;
                    case "-tfwrap": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out Wrap); break;
                    case "-tfdir": int.TryParse(v, out DirMode); break;
                    case "-tfanchor": TorchFloodBuild.AnchorsOn = v != "0"; break;
                    case "-tfeditlit": EditLitOnly = v != "0"; break;
                    case "-tfreach": float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ReachM); break;
                }
            }
        }

        private static TorchFloodPool _inst;
        public static TorchFloodPool Instance => _inst ??= new TorchFloodPool(null, ReachM, true);
        /// The renderer draws the flood kernel only when this is true.
        public static bool Ready => Enabled && _inst != null && _inst._uploaded;

        /// Edit hook (EditService, FluidOpListReadback, the edit-op replay): queues
        /// the torches whose box holds v. A no-op while OFF.
        public static void NotifyVoxelChanged(int3 v)
        {
            if (!Enabled || _inst == null) return;
            _inst.OnVoxelChanged(v);
        }

        /// The same for every voxel of the box [lo, hi] (inclusive).
        public static void NotifyBox(int3 lo, int3 hi)
        {
            if (!Enabled || _inst == null) return;
            int3 a = lo >> 3, b = hi >> 3;
            for (int z = a.z; z <= b.z; z++)
            for (int y = a.y; y <= b.y; y++)
            for (int x = a.x; x <= b.x; x++)
            {
                int3 bb = new int3(x, y, z) << 3;
                _inst.AddEdit(math.max(lo, bb), math.min(hi, bb + 7));
            }
        }

        /// Teardown (Phase4Bootstrapper): completes the jobs and frees everything.
        public static void Shutdown() { _inst?.Dispose(); _inst = null; }

        // ---- encoded form ----
        public const uint KIND_AIR = 1u, KIND_MIXED = 2u, ESCAPE_BIT = 0x40000000u, OFFSET_MASK = 0x3FFFFFFFu;

        public sealed class Encoded
        {
            public int3 origin, dims;
            public uint[] dir = Array.Empty<uint>(), tables = Array.Empty<uint>(), data = Array.Empty<uint>();
            public int chunks, airBricks, mixedBricks, escapes, splitCells;
            public long Bytes => 4L * (dir.Length + tables.Length + data.Length);
            public bool Any => chunks > 0;
        }

        // =====================================================================
        // State
        // =====================================================================

        private sealed class TorchRec
        {
            public int3 v; public byte m; public uint seed; public int top;
            public int3 lo, dims, db;
            public TorchFloodCache cache;
            public bool need = true;
            public Slot job;
            public long skipPass;
        }

        private sealed class Slot
        {
            public TorchFloodScratch s = new TorchFloodScratch();
            public TorchFloodCache cache;
            public TorchRec rec;
            public JobHandle h;
            public NativeArray<int> stats = new NativeArray<int>(8, Allocator.Persistent);
            public Stopwatch sw = new Stopwatch();
            public bool busy;
        }

        private ChunkStore _store;
        private readonly float _reachM;
        /// Tests: false (no GraphicsBuffers in batchmode -nographics).
        public bool Gpu;
        public int MaxJobs = 2;
        public int MergeBatch = 4096;
        /// Main-thread budget per Tick for starting new floods (each one Snapshots its box).
        public double FrameBudgetMs = 0.5;

        private readonly Dictionary<int3, TorchRec> _recs = new Dictionary<int3, TorchRec>();
        private readonly Dictionary<int3, List<TorchRec>> _chunkRecs = new Dictionary<int3, List<TorchRec>>();
        private readonly Dictionary<int3, Chunk> _watch = new Dictionary<int3, Chunk>();
        /// Edited voxels since the last Step: per brick, the bounding box (world voxels) of its edited voxels.
        private readonly Dictionary<int3, (int3 lo, int3 hi)> _editBoxes = new Dictionary<int3, (int3, int3)>();
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<TorchFloodCache> _retired = new List<TorchFloodCache>();
        private long _serial, _seenRev = -1, _editPass;
        private (int3 v, byte m)[] _builtSig = Array.Empty<(int3, byte)>();

        // the union (the CPU copy of the GPU buffers)
        private NativeList<uint> _tables, _data;
        private NativeHashMap<int3, int> _chunkTable;
        private NativeList<int> _chunkUsed, _freeTables, _free8, _free64;
        private NativeList<ulong> _dirtyData;
        private NativeList<byte> _dirtyTable;
        private NativeList<int3> _added, _removed;
        private NativeArray<int> _counts;     // air bricks, mixed bricks, escapes, chunks
        private NativeHashSet<int3> _dirtyBricks;
        private uint[] _dir = Array.Empty<uint>();
        private int3 _dirOrigin, _dirDims;
        private bool _dirDirty = true, _tablesRealloc = true, _dataRealloc = true;
        private long _version, _exportVersion = -1;
        private Encoded _export;

        // the merge in flight
        private bool _merging;
        private JobHandle _mergeH;
        private NativeList<int3> _mergeBricks;
        private NativeArray<TorchFloodBuild.CacheDesc> _mergeDescs;
        private NativeArray<int2> _mergeRec;
        private NativeList<uint> _mergeOut;

        // GPU
        private GraphicsBuffer _dirB, _tablesB, _dataB;
        private bool _uploaded;

        // ---- stats ----
        public LightFloodReference Oracle { get; private set; }
        public int Rebuilds { get; private set; }
        public double LastRebuildMs, LastFloodMs, LastEncodeMs, LastUploadMs;
        public int LastTorches, LastTorchesSkipped;
        public int FloodsDone, MergesDone;
        /// Task 169: bricks the anchored test made direct, summed over finished floods.
        public long AnchoredSum;
        public long UploadBytesTotal;
        /// Main-thread ms of every Tick that had work (the L3 report).
        public readonly List<float> BusyTickMs = new List<float>();
        /// Per finished flood: (worker wall ms from schedule to seen done, reached voxels, lit bricks, direct bricks).
        public readonly List<float4> FloodLog = new List<float4>();
        /// Sync Rebuild with TimeFloods: each torch's flood run alone (.Run()) and timed: the worker cost of one torch.
        public static bool TimeFloods;
        public readonly List<float> FloodRunMs = new List<float>();

        public TorchFloodPool(ChunkStore store, float reachM, bool gpu)
        {
            _store = store;
            _reachM = reachM;
            Gpu = gpu;
            AllocUnion();
        }

        private void AllocUnion()
        {
            _tables = new NativeList<uint>(4096, Allocator.Persistent);
            _data = new NativeList<uint>(4096, Allocator.Persistent);
            _chunkTable = new NativeHashMap<int3, int>(64, Allocator.Persistent);
            _chunkUsed = new NativeList<int>(64, Allocator.Persistent);
            _freeTables = new NativeList<int>(16, Allocator.Persistent);
            _free8 = new NativeList<int>(1024, Allocator.Persistent);
            _free64 = new NativeList<int>(256, Allocator.Persistent);
            _dirtyData = new NativeList<ulong>(64, Allocator.Persistent);
            _dirtyTable = new NativeList<byte>(64, Allocator.Persistent);
            _added = new NativeList<int3>(16, Allocator.Persistent);
            _removed = new NativeList<int3>(16, Allocator.Persistent);
            _counts = new NativeArray<int>(4, Allocator.Persistent);
            _dirtyBricks = new NativeHashSet<int3>(1024, Allocator.Persistent);
            _mergeBricks = new NativeList<int3>(MergeBatch, Allocator.Persistent);
            _mergeOut = new NativeList<uint>(MergeBatch * 16, Allocator.Persistent);
        }

        private void FreeUnion()
        {
            _tables.Dispose(); _data.Dispose(); _chunkTable.Dispose(); _chunkUsed.Dispose(); _freeTables.Dispose();
            _free8.Dispose(); _free64.Dispose(); _dirtyData.Dispose(); _dirtyTable.Dispose(); _added.Dispose(); _removed.Dispose();
            _counts.Dispose(); _dirtyBricks.Dispose(); _mergeBricks.Dispose(); _mergeOut.Dispose();
        }

        public int TorchCount => _recs.Count;
        public int Chunks => _counts[3];
        public int AirBricks => _counts[0];
        public int MixedBricks => _counts[1];
        public int Escapes => _counts[2];
        public int DataLength => _data.Length;
        public int DirtyBrickCount => _dirtyBricks.Count;

        /// Work still to do (floods queued or running, a merge pending or in flight).
        public bool Busy
        {
            get
            {
                if (_merging || _editBoxes.Count > 0 || !_dirtyBricks.IsEmpty) return true;
                foreach (var s in _slots) if (s.busy) return true;
                foreach (var r in _recs.Values) if (r.need) return true;
                return false;
            }
        }

        public long CacheBytes { get { long b = 0; foreach (var r in _recs.Values) if (r.cache != null) b += r.cache.Bytes; return b; } }
        public long ScratchBytes { get { long b = 0; foreach (var s in _slots) b += s.s.Bytes + (s.cache != null ? s.cache.Bytes : 0); return b; } }
        public long UnionBytes =>
            4L * (_tables.Capacity + _data.Capacity + _dir.Length + _chunkUsed.Capacity + _freeTables.Capacity + _free8.Capacity + _free64.Capacity)
            + 8L * _dirtyData.Capacity + _dirtyTable.Capacity + 16L * _chunkTable.Capacity + 16L * _dirtyBricks.Capacity
            + 4L * _mergeOut.Capacity + 12L * _mergeBricks.Capacity;
        /// What the union holds (the GPU layout's size), without list / buffer growth slack.
        public long UnionContentBytes => 4L * (_dir.Length + 4096L * _counts[3] + 8L * _counts[0] + 64L * _counts[1] + 8L * _counts[2]);
        public long CacheContentBytes
        {
            get
            {
                long b = 0;
                foreach (var r in _recs.Values)
                    if (r.cache != null) b += 8L * r.cache.direct.Length + 4L * r.cache.keys.Length + 4L * r.cache.entries.Length + 2L * r.cache.data.Length;
                return b;
            }
        }
        public long GpuBytes => 4L * ((_dirB?.count ?? 0) + (_tablesB?.count ?? 0) + (_dataB?.count ?? 0));
        public int CacheCount { get { int n = 0; foreach (var r in _recs.Values) if (r.cache != null) n++; return n; } }

        public Vector4 Params => new Vector4(Intensity, FalloffK, ReachM, (float)(LightFloodReference.UNITS_PER_VOXEL * 10.0));
        public Vector4 Params2 => new Vector4(LightFloodReference.ReachLevel(ReachM), LightFloodReference.STOPS_AT_REACH, Wrap, DirMode);

        public GraphicsBuffer DirBuffer => _dirB;
        public GraphicsBuffer TablesBuffer => _tablesB;
        public GraphicsBuffer DataBuffer => _dataB;

        // =====================================================================
        // Per frame
        // =====================================================================

        /// Main thread, once per frame after the light upload.
        public void Tick(LightSourceBuffer lights)
        {
            if (!Enabled || Store == null || lights == null) return;
            if (!ReferenceEquals(_store, Store)) ResetAll(Store);
            var sw = Stopwatch.StartNew();
            bool was = Busy;
            if (lights.Revision != _seenRev)
            {
                _seenRev = lights.Revision;
                var sig = Signature(lights);
                if (!SameSig(sig, _builtSig)) { SetTorches(sig); _builtSig = sig; }
            }
            Step(true);
            if (!_uploaded && Gpu) Upload();
            bool busy = Busy;
            ScratchPeakBytes = Math.Max(ScratchPeakBytes, ScratchBytes);
            if (was || busy) BusyTickMs.Add((float)sw.Elapsed.TotalMilliseconds);
            if (!busy && _slots.Count > 0) FreeSlots();   // the job scratch exists only while flooding
        }

        /// Replaces the torch set (tests and Tick). Removed torches drop their light; added ones are queued.
        public void SetTorches(IReadOnlyList<(int3 v, byte m)> torches)
        {
            var want = new Dictionary<int3, byte>(torches.Count);
            foreach (var (v, m) in torches) want[v] = m;
            var drop = new List<TorchRec>();
            foreach (var r in _recs.Values) if (!want.TryGetValue(r.v, out byte m) || m != r.m) drop.Add(r);
            foreach (var r in drop) RemoveRec(r);
            foreach (var kv in want) if (!_recs.ContainsKey(kv.Key)) AddRec(kv.Key, kv.Value);
        }

        public void OnVoxelChanged(int3 v) => AddEdit(v, v);

        /// [lo, hi] lies inside one brick.
        private void AddEdit(int3 lo, int3 hi)
        {
            int3 b = lo >> 3;
            if (_editBoxes.TryGetValue(b, out var e)) _editBoxes[b] = (math.min(e.lo, lo), math.max(e.hi, hi));
            else _editBoxes[b] = (lo, hi);
        }

        /// Task 169 cut 2 (-tfeditlit 0|1, default ON): an edit re-floods only the torches it can change. Exact:
        /// CLOSING an open voxel e changes torch t's costs only if e was lit by t (a path or a move's footprint
        /// through e makes d(e) < reach); OPENING e only if a voxel next to e (its 3^3, the torch voxel included)
        /// was lit (the first point of a new path next to e is reached without e). Either way the edited brick
        /// changes its kind / split cells, which matters only where t lights that brick. So t re-floods iff it
        /// lights a brick of the edit's box grown by 1 voxel, or that box reaches t's own brick. A torch whose
        /// flood is in flight (snapshot taken before the edit) or that has no cache re-floods as before. 0 =
        /// task 165's rule (every torch whose 12 m box holds the edit).
        public static bool EditLitOnly = true;
        /// Edit -> torch decisions since the last ResetAll / ResetEditStats: re-flooded, skipped (cut 2).
        public int EditRefloods, EditSkips;
        public void ResetEditStats() { EditRefloods = EditSkips = 0; }

        private static bool EditTouchesLit(TorchRec r, int3 lo, int3 hi)
        {
            int3 a = (lo - 1) >> 3, b = (hi + 1) >> 3, tb = r.v >> 3;
            for (int z = a.z; z <= b.z; z++)
            for (int y = a.y; y <= b.y; y++)
            for (int x = a.x; x <= b.x; x++)
            {
                int3 wb = new int3(x, y, z);
                if (wb.Equals(tb) || r.cache.LightsBrick(wb)) return true;
            }
            return false;
        }

        private void AddRec(int3 v, byte m)
        {
            var r = new TorchRec { v = v, m = m, seed = LightFloodReference.SeedForMaterial(m, _reachM) };
            r.top = LightFloodReference.MaxChannel(r.seed);
            LightFloodReference.TorchBox(v, r.top, out r.lo, out r.dims);
            r.db = r.dims >> 3;
            _recs[v] = r;
            int3 c0 = r.lo >> 7, c1 = (r.lo + r.dims - 1) >> 7;
            for (int z = c0.z; z <= c1.z; z++)
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                var c = new int3(x, y, z);
                if (!_chunkRecs.TryGetValue(c, out var l)) { _chunkRecs[c] = l = new List<TorchRec>(); _watch[c] = _store.GetChunk(c); }
                l.Add(r);
            }
        }

        private void RemoveRec(TorchRec r)
        {
            _recs.Remove(r.v);
            DropCache(r);
            int3 c0 = r.lo >> 7, c1 = (r.lo + r.dims - 1) >> 7;
            for (int z = c0.z; z <= c1.z; z++)
            for (int y = c0.y; y <= c1.y; y++)
            for (int x = c0.x; x <= c1.x; x++)
            {
                var c = new int3(x, y, z);
                if (!_chunkRecs.TryGetValue(c, out var l)) continue;
                l.Remove(r);
                if (l.Count == 0) { _chunkRecs.Remove(c); _watch.Remove(c); }
            }
        }

        private void DropCache(TorchRec r)
        {
            if (r.cache == null) return;
            Collect(r.cache);
            _retired.Add(r.cache);
            r.cache = null;
        }

        private void Collect(TorchFloodCache c) => new TorchFloodBuild.CollectJob { c = c.Desc(), set = _dirtyBricks }.Run();

        private static bool InBox(TorchRec r, int3 brick)
        {
            int3 q = brick - (r.lo >> 3);
            return math.all(q >= 0) && math.all(q < r.db);
        }

        /// One round of work. `budgeted`: the per-frame form (jobs are collected only when done, floods
        /// start while the budget lasts); otherwise everything started is completed now (Flush).
        /// Main-thread ms per Step phase (the L3 report): 0 edits + chunk poll, 1 finished floods (cache swap + collect),
        /// 2 finished merge (apply), 3 upload, 4 new floods (snapshots), 5 next merge (take + schedule). Max and sum.
        public readonly double[] PhaseMaxMs = new double[6], PhaseSumMs = new double[6];
        private void Phase(int i, Stopwatch sw, ref double last)
        {
            double t = sw.Elapsed.TotalMilliseconds, d = t - last;
            last = t;
            PhaseSumMs[i] += d;
            if (d > PhaseMaxMs[i]) PhaseMaxMs[i] = d;
        }
        public void ResetPhaseStats() { Array.Clear(PhaseMaxMs, 0, 6); Array.Clear(PhaseSumMs, 0, 6); }

        private void Step(bool budgeted)
        {
            var sw = Stopwatch.StartNew();
            double last = 0;
            // edits -> the torches whose box holds them (cut 2: and that they can change)
            if (_editBoxes.Count > 0)
            {
                _editPass++;
                foreach (var kv in _editBoxes)
                {
                    int3 b = kv.Key;
                    if (!_chunkRecs.TryGetValue(b >> 4, out var l)) continue;
                    foreach (var r in l)
                    {
                        if (r.need || !InBox(r, b)) continue;
                        if (!EditLitOnly || r.job != null || r.cache == null || EditTouchesLit(r, kv.Value.lo, kv.Value.hi))
                        {
                            r.need = true;
                            EditRefloods++;
                            if (r.skipPass == _editPass) EditSkips--;
                            r.skipPass = 0;
                        }
                        else if (r.skipPass != _editPass) { r.skipPass = _editPass; EditSkips++; }
                    }
                }
                _editBoxes.Clear();
            }
            // admitted / evicted chunks
            List<int3> changed = null;
            foreach (var kv in _watch)
                if (!ReferenceEquals(_store.GetChunk(kv.Key), kv.Value)) (changed ??= new List<int3>()).Add(kv.Key);
            if (changed != null)
                foreach (int3 c in changed)
                {
                    _watch[c] = _store.GetChunk(c);
                    foreach (var r in _chunkRecs[c]) r.need = true;
                }
            Phase(0, sw, ref last);
            // finished floods
            foreach (var s in _slots)
                if (s.busy && (!budgeted || s.h.IsCompleted)) FinishFlood(s);
            Phase(1, sw, ref last);
            // a finished merge
            if (_merging && (!budgeted || _mergeH.IsCompleted)) FinishMerge();
            Phase(2, sw, ref last);
            PhaseSumMs[2] -= LastUploadMsStep; if (LastUploadMsStep > PhaseMaxMs[3]) PhaseMaxMs[3] = LastUploadMsStep;
            PhaseSumMs[3] += LastUploadMsStep; LastUploadMsStep = 0;
            // new floods
            foreach (var r in _recs.Values)
            {
                if (!r.need || r.job != null) continue;
                if (budgeted && sw.Elapsed.TotalMilliseconds > FrameBudgetMs) break;
                Slot s = FreeSlot();
                if (s == null) break;
                StartFlood(r, s);
            }
            JobHandle.ScheduleBatchedJobs();
            Phase(4, sw, ref last);
            // the next merge
            if (!_merging && !_dirtyBricks.IsEmpty) StartMerge(budgeted ? MergeBatch : int.MaxValue);
            if (!budgeted && _merging) FinishMerge();
            Phase(5, sw, ref last);
        }

        private double LastUploadMsStep;

        private void FreeSlots()
        {
            foreach (var s in _slots) { if (s.busy) s.h.Complete(); s.cache?.Dispose(); s.s.Dispose(); s.stats.Dispose(); }
            _slots.Clear();
        }

        /// Peak of ScratchBytes seen at the end of a Tick (the job scratch while flooding).
        public long ScratchPeakBytes;

        private Slot FreeSlot()
        {
            foreach (var s in _slots) if (!s.busy) return s;
            if (_slots.Count >= MaxJobs) return null;
            var n = new Slot();
            _slots.Add(n);
            return n;
        }

        private bool Open(int3 v)
        {
            Chunk ch = _store.GetChunk(v >> 7);
            if (ch == null) return false;
            if (ch.isUniform) return ch.uniformMaterial == 0;
            uint h = ch.bricks[CoordMath.LocalBrickIndex((v >> 3) & 15)].data;
            if ((h & 0x80000000u) == 0) return (h & 0xFFu) == 0;
            return _store.BrickPool.RawData[(int)(h & 0x3FFFFFFFu) * 512 + CoordMath.LocalVoxelIndex(v & 7)] == 0;
        }

        private void StartFlood(TorchRec r, Slot s)
        {
            r.need = false;
            if (_store.GetChunk(r.v >> 7) == null || Open(r.v))
            {
                // not resident (dark until admitted), or a stale registration over air (skipped, as task 158)
                DropCache(r);
                return;
            }
            TorchFloodBuild.Snapshot(_store, r.lo, r.db, s.s);
            s.cache = new TorchFloodCache { torch = r.v, lo = r.lo, db = r.db, seed = r.seed, top = r.top, serial = ++_serial };
            var job = TorchFloodBuild.MakeFloodJob(s.s, s.cache, r.v - r.lo, r.dims, r.db, r.top, s.stats);
            s.rec = r; r.job = s; s.busy = true;
            s.sw.Restart();
            if (TimeFloods && _sync) { job.Run(); FloodRunMs.Add((float)s.sw.Elapsed.TotalMilliseconds); s.h = default; }
            else s.h = job.Schedule();
        }

        /// Inside Flush: TimeFloods runs each flood alone on this thread (its worker cost, timed).
        private bool _sync;

        private void FinishFlood(Slot s)
        {
            s.h.Complete();
            float ms = (float)s.sw.Elapsed.TotalMilliseconds;
            var c = s.cache; var r = s.rec;
            s.busy = false; s.cache = null; s.rec = null;
            c.reached = s.stats[0]; c.directBricks = s.stats[1]; c.storedBricks = s.stats[2]; c.escapes = s.stats[3]; c.seeds = s.stats[4]; c.anchoredBricks = s.stats[7];
            AnchoredSum += c.anchoredBricks;
            c.floodMs = ms;
            FloodsDone++;
            FloodLog.Add(new float4(ms, c.reached, c.LitBricks, c.directBricks));
            if (r.job == s) r.job = null;
            if (!_recs.TryGetValue(r.v, out var cur) || !ReferenceEquals(cur, r)) { c.Dispose(); return; }   // removed meanwhile
            DropCache(r);
            r.cache = c;
            Collect(c);
        }

        private void StartMerge(int max)
        {
            new TorchFloodBuild.TakeJob { set = _dirtyBricks, outList = _mergeBricks, max = max }.Run();
            int n = _mergeBricks.Length;
            if (n == 0) return;
            int3 lo = new int3(int.MaxValue), hi = new int3(int.MinValue);
            for (int i = 0; i < n; i++) { lo = math.min(lo, _mergeBricks[i]); hi = math.max(hi, _mergeBricks[i]); }
            var descs = new List<TorchFloodBuild.CacheDesc>();
            foreach (var r in _recs.Values)
            {
                if (r.cache == null) continue;
                int3 b0 = r.lo >> 3, b1 = b0 + r.db - 1;
                if (math.any(b1 < lo) || math.any(b0 > hi)) continue;
                descs.Add(r.cache.Desc());
            }
            _mergeDescs = new NativeArray<TorchFloodBuild.CacheDesc>(math.max(1, descs.Count), Allocator.Persistent);
            for (int i = 0; i < descs.Count; i++) _mergeDescs[i] = descs[i];
            _mergeRec = new NativeArray<int2>(n, Allocator.Persistent);
            _mergeH = new TorchFloodBuild.MergeJob
            {
                bricks = _mergeBricks.AsArray(), torches = _mergeDescs.GetSubArray(0, descs.Count), outData = _mergeOut, outRec = _mergeRec,
            }.Schedule();
            _merging = true;
        }

        private void FinishMerge()
        {
            _mergeH.Complete();
            _merging = false;
            _added.Clear(); _removed.Clear();
            new TorchFloodBuild.ApplyJob
            {
                bricks = _mergeBricks.AsArray(), rec = _mergeRec, src = _mergeOut, tables = _tables, data = _data,
                chunkTable = _chunkTable, chunkUsed = _chunkUsed, freeTables = _freeTables, free8 = _free8, free64 = _free64,
                dirtyData = _dirtyData, dirtyTable = _dirtyTable, chunksAdded = _added, chunksRemoved = _removed, counts = _counts,
            }.Run();
            _mergeDescs.Dispose(); _mergeRec.Dispose();
            _mergeBricks.Clear();
            MergesDone++;
            _version++;
            UpdateDir();
            if (_tablesB == null || _tables.Length > _tablesB.count) _tablesRealloc = true;
            if (_dataB == null || _data.Length > _dataB.count) _dataRealloc = true;
            foreach (var c in _retired) c.Dispose();
            _retired.Clear();
            if (Gpu) Upload();
        }

        private void UpdateDir()
        {
            if (_added.Length == 0 && _removed.Length == 0) return;
            bool rebuild = _dir.Length == 0;
            for (int i = 0; i < _added.Length && !rebuild; i++)
            {
                int3 d = _added[i] - _dirOrigin;
                if (math.any(d < 0) || math.any(d >= _dirDims)) rebuild = true;
            }
            if (rebuild)
            {
                var keys = _chunkTable.GetKeyArray(Allocator.Temp);
                int3 mn = new int3(int.MaxValue), mx = new int3(int.MinValue);
                for (int i = 0; i < keys.Length; i++) { mn = math.min(mn, keys[i]); mx = math.max(mx, keys[i]); }
                if (keys.Length == 0) { mn = mx = int3.zero; }
                _dirOrigin = mn; _dirDims = mx - mn + 1;
                _dir = new uint[_dirDims.x * _dirDims.y * _dirDims.z];
                for (int i = 0; i < keys.Length; i++) SetDir(keys[i], (uint)_chunkTable[keys[i]] + 1u);
                keys.Dispose();
            }
            else
            {
                for (int i = 0; i < _removed.Length; i++) SetDir(_removed[i], 0u);
                for (int i = 0; i < _added.Length; i++) SetDir(_added[i], (uint)_chunkTable[_added[i]] + 1u);
            }
            _dirDirty = true;
        }

        private void SetDir(int3 c, uint v)
        {
            int3 d = c - _dirOrigin;
            if (math.any(d < 0) || math.any(d >= _dirDims)) return;
            _dir[(d.z * _dirDims.y + d.y) * _dirDims.x + d.x] = v;
        }

        // =====================================================================
        // Synchronous paths (capture, tests)
        // =====================================================================

        /// Runs every queued flood and merge to completion now.
        public void Flush()
        {
            _sync = true;
            int guard = 0;
            do Step(false); while (Busy && guard++ < 1000000);   // one Step even when idle: it polls the box chunks
            _sync = false;
            if (Gpu && !_uploaded) Upload();
        }

        /// Synchronous refill from scratch (the capture's "build it now before timing" and the reference).
        public void Rebuild(LightSourceBuffer lights)
        {
            if (Store != null && !ReferenceEquals(_store, Store)) _store = Store;
            var sw = Stopwatch.StartNew();
            var sig = Signature(lights);
            ResetAll(_store);
            SetTorches(sig);
            _builtSig = sig;
            _seenRev = lights.Revision;
            FloodRunMs.Clear();
            Flush();
            LastRebuildMs = sw.Elapsed.TotalMilliseconds;
            LastFloodMs = 0; foreach (var f in FloodLog) LastFloodMs += f.x;
            LastEncodeMs = 0; LastUploadMs = 0;
            Oracle?.Dispose(); Oracle = null;
            int skipped = 0;
            foreach (var r in _recs.Values) if (r.cache == null) skipped++;
            if (KeepOracle)
            {
                var f = new LightFloodReference(_store);
                foreach (var (v, m) in sig)
                    if (_store.GetChunk(v >> 7) != null && !f.Open(v)) f.AddTorch(v, LightFloodReference.SeedForMaterial(m, _reachM));
                Oracle = f;
            }
            Rebuilds++;
            LastTorches = sig.Length - skipped; LastTorchesSkipped = skipped;
            Debug.Log($"[torchflood] rebuild {Rebuilds}: {LastTorches} torches ({skipped} skipped), {Chunks} lit chunks, " +
                      $"air bricks {AirBricks}, mixed {MixedBricks}, escapes {Escapes}, GPU {GpuBytes / 1048576.0:F2} MB, CPU union " +
                      $"{UnionBytes / 1048576.0:F2} + caches {CacheBytes / 1048576.0:F2} + scratch {ScratchBytes / 1048576.0:F2} MB; " +
                      $"total {LastRebuildMs:F0} ms (floods {FloodsDone}, merges {MergesDone})");
        }

        /// Rebuilds synchronously only if the registered set differs from the built one (the cost capture).
        public bool EnsureBuilt(LightSourceBuffer lights)
        {
            var sig = Signature(lights);
            if (_uploaded && SameSig(sig, _builtSig) && !Busy) return false;
            Rebuild(lights);
            return true;
        }

        /// Drops every torch, cache and union brick (buffers are kept, re-uploaded on the next change).
        public void ResetAll(ChunkStore store)
        {
            foreach (var s in _slots) if (s.busy) { s.h.Complete(); s.cache?.Dispose(); s.cache = null; s.busy = false; s.rec = null; }
            if (_merging) { _mergeH.Complete(); _merging = false; _mergeDescs.Dispose(); _mergeRec.Dispose(); _mergeBricks.Clear(); }
            foreach (var r in _recs.Values) r.cache?.Dispose();
            foreach (var c in _retired) c.Dispose();
            _retired.Clear(); _recs.Clear(); _chunkRecs.Clear(); _watch.Clear(); _editBoxes.Clear();
            _builtSig = Array.Empty<(int3, byte)>(); _seenRev = -1;
            FreeUnion(); AllocUnion();
            _dir = Array.Empty<uint>(); _dirOrigin = _dirDims = int3.zero;
            _dirDirty = _tablesRealloc = _dataRealloc = true;
            _version++;
            _store = store;
            FloodsDone = MergesDone = 0; EditRefloods = EditSkips = 0; AnchoredSum = 0; FloodLog.Clear(); BusyTickMs.Clear(); UploadBytesTotal = 0; ScratchPeakBytes = 0; ResetPhaseStats();
            _uploaded = false;
        }

        private static (int3 v, byte m)[] Signature(LightSourceBuffer lights)
        {
            var reg = lights.SnapshotRegistered();
            var l = new List<(int3, byte)>(reg.Length);
            foreach (var r in reg)
                if (!MaterialRules.IsMobile(r.material)) l.Add((r.voxel, r.material));
            l.Sort((p, q) => p.Item1.x != q.Item1.x ? p.Item1.x.CompareTo(q.Item1.x)
                           : p.Item1.y != q.Item1.y ? p.Item1.y.CompareTo(q.Item1.y)
                           : p.Item1.z != q.Item1.z ? p.Item1.z.CompareTo(q.Item1.z) : p.Item2.CompareTo(q.Item2));
            return l.ToArray();
        }

        private static bool SameSig((int3 v, byte m)[] a, (int3 v, byte m)[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!a[i].v.Equals(b[i].v) || a[i].m != b[i].m) return false;
            return true;
        }

        /// The union as an Encoded (a copy; cached until the union changes). Tests and the capture probe.
        public Encoded Current
        {
            get
            {
                if (_exportVersion == _version && _export != null) return _export;
                _export = new Encoded
                {
                    origin = _dirOrigin, dims = _dirDims, dir = (uint[])_dir.Clone(),
                    tables = _tables.AsArray().ToArray(), data = _data.AsArray().ToArray(),
                    chunks = _counts[3], airBricks = _counts[0], mixedBricks = _counts[1], escapes = _counts[2], splitCells = _counts[2],
                };
                _exportVersion = _version;
                return _export;
            }
        }

        /// Logical equality of two encodings (layouts may differ): the same lit chunks and bricks, kinds,
        /// cell values and escapes. Returns null if equal.
        public static string Diff(Encoded a, Encoded b)
        {
            var ba = Bricks(a); var bb = Bricks(b);
            if (ba.Count != bb.Count) return $"{ba.Count} lit bricks vs {bb.Count}";
            foreach (var kv in ba)
            {
                if (!bb.TryGetValue(kv.Key, out var y)) return $"brick {kv.Key} only in the first";
                var x = kv.Value;
                if (x.Length != y.Length) return $"brick {kv.Key}: kind {(x.Length == 8 ? "air" : "mixed")} vs {(y.Length == 8 ? "air" : "mixed")}";
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i]) return $"brick {kv.Key} cell {i}: {LightFloodReference.Str(x[i])} vs {LightFloodReference.Str(y[i])}";
            }
            if (a.chunks != b.chunks || a.airBricks != b.airBricks || a.mixedBricks != b.mixedBricks || a.escapes != b.escapes)
                return $"counts: chunks {a.chunks}/{b.chunks} air {a.airBricks}/{b.airBricks} mixed {a.mixedBricks}/{b.mixedBricks} escapes {a.escapes}/{b.escapes}";
            return null;
        }

        /// Per lit brick: 8 air cells, or 64 mixed cells + 8 per cell (escape: the 8 voxels, else 8 copies of 0xFFFFFFFF).
        private static Dictionary<int3, uint[]> Bricks(Encoded e)
        {
            var d = new Dictionary<int3, uint[]>();
            if (!e.Any) return d;
            for (int z = 0; z < e.dims.z; z++)
            for (int y = 0; y < e.dims.y; y++)
            for (int x = 0; x < e.dims.x; x++)
            {
                uint t = e.dir[(z * e.dims.y + y) * e.dims.x + x];
                if (t == 0) continue;
                int3 c = e.origin + new int3(x, y, z);
                for (int bi = 0; bi < 4096; bi++)
                {
                    uint en = e.tables[(t - 1) * 4096 + bi];
                    if (en == 0) continue;
                    uint off = en & OFFSET_MASK;
                    int3 wb = (c << 4) + new int3(bi & 15, (bi >> 4) & 15, bi >> 8);
                    if ((en >> 30) == KIND_AIR) { var a = new uint[8]; Array.Copy(e.data, off, a, 0, 8); d[wb] = a; continue; }
                    var m = new uint[64 + 512];
                    for (int ci = 0; ci < 64; ci++)
                    {
                        uint p = e.data[off + ci];
                        if ((p & ESCAPE_BIT) != 0)
                        {
                            m[ci] = 0xFFFFFFFFu;
                            for (int k = 0; k < 8; k++) m[64 + ci * 8 + k] = e.data[(p & OFFSET_MASK) + k];
                        }
                        else { m[ci] = p; for (int k = 0; k < 8; k++) m[64 + ci * 8 + k] = 0xFFFFFFFFu; }
                    }
                    d[wb] = m;
                }
            }
            return d;
        }

        // =====================================================================
        // Encode (task 157's oracle -> the GPU layout) and its sampler twin
        // =====================================================================

        /// 2x2x2 open-voxel mask -> number of face-connected groups.
        private static readonly byte[] Groups = BuildGroups();
        public static byte GroupsOf(int mask) => Groups[mask];

        private static byte[] BuildGroups()
        {
            var g = new byte[256];
            for (int mask = 0; mask < 256; mask++)
            {
                int left = mask, n = 0;
                while (left != 0)
                {
                    int seed = left & -left, comp = seed, grow;
                    do
                    {
                        grow = comp;
                        for (int i = 0; i < 8; i++)
                            if ((comp & (1 << i)) != 0)
                                for (int bit = 1; bit < 8; bit <<= 1) grow |= (1 << (i ^ bit)) & mask;
                        if (grow == comp) break;
                        comp = grow;
                    } while (true);
                    left &= ~comp; n++;
                }
                g[mask] = (byte)n;
            }
            return g;
        }

        /// Pure air brick, from its CONTENT (task 165: the header of a not-yet-compacted all-air brick says
        /// "dense"; 164b saw the same tunnel encoded both ways): its 0.4 m cells may be blended.
        private static bool BrickAllAir(LightFloodReference f, int3 brick)
        {
            int3 b = brick << 3;
            for (int i = 0; i < 512; i++) if (!f.Open(b + new int3(i & 7, (i >> 3) & 7, i >> 6))) return false;
            return true;
        }

        /// The oracle's per-voxel field in the GPU layout. Air cell = the per-channel MAX of the 2^3 voxels
        /// round its centre (task 165: a max decomposes over torches, the mean 158 used does not); mixed cell =
        /// the max over its voxels, a split cell escapes to its 8 voxels.
        public static Encoded Encode(LightFloodReference f, ChunkStore store)
        {
            var e = new Encoded();
            var bricks = new List<(int3 brick, uint[] data)>(f.LitBricks());
            if (bricks.Count == 0) return e;
            var chunkIdx = new Dictionary<int3, int>();
            int3 mn = new int3(int.MaxValue), mx = new int3(int.MinValue);
            foreach (var (b, _) in bricks)
            {
                int3 c = b >> 4;
                if (!chunkIdx.ContainsKey(c)) chunkIdx[c] = chunkIdx.Count;
                mn = math.min(mn, c); mx = math.max(mx, c);
            }
            e.origin = mn; e.dims = mx - mn + 1; e.chunks = chunkIdx.Count;
            e.dir = new uint[e.dims.x * e.dims.y * e.dims.z];
            foreach (var kv in chunkIdx)
            {
                int3 d = kv.Key - mn;
                e.dir[(d.z * e.dims.y + d.y) * e.dims.x + d.x] = (uint)kv.Value + 1u;
            }
            e.tables = new uint[e.chunks * 4096];
            var data = new List<uint>(bricks.Count * 64);
            var cell = new uint[8];
            foreach (var (b, lv) in bricks)
            {
                int ti = chunkIdx[b >> 4];
                int bi = CoordMath.LocalBrickIndex(b & 15);
                int off = data.Count;
                if (BrickAllAir(f, b))
                {
                    for (int ci = 0; ci < 8; ci++)
                    {
                        int3 c0 = new int3(ci & 1, (ci >> 1) & 1, ci >> 2) * 4 + 1;
                        uint m = 0;
                        for (int k = 0; k < 8; k++)
                            m = LightFloodReference.Max(m, lv[CoordMath.LocalVoxelIndex(c0 + new int3(k & 1, (k >> 1) & 1, k >> 2))]);
                        data.Add(m);
                    }
                    e.tables[ti * 4096 + bi] = (KIND_AIR << 30) | (uint)off;
                    e.airBricks++;
                    continue;
                }
                for (int ci = 0; ci < 64; ci++) data.Add(0u);
                int3 vb = b << 3;
                for (int ci = 0; ci < 64; ci++)
                {
                    int3 c0 = new int3(ci & 3, (ci >> 2) & 3, ci >> 4) * 2;
                    int open = 0; uint mxv = 0; bool any = false;
                    for (int k = 0; k < 8; k++)
                    {
                        int3 l = c0 + new int3(k & 1, (k >> 1) & 1, k >> 2);
                        uint p = lv[CoordMath.LocalVoxelIndex(l)];
                        cell[k] = p;
                        if (p != 0) { any = true; mxv = LightFloodReference.Max(mxv, p); }
                        if (f.Open(vb + l)) open |= 1 << k;
                    }
                    if (!any) continue;
                    if (Groups[open] >= 2)
                    {
                        e.splitCells++;
                        data[off + ci] = ESCAPE_BIT | (uint)data.Count;
                        for (int k = 0; k < 8; k++) data.Add(cell[k]);
                        e.escapes++;
                    }
                    else data[off + ci] = mxv;
                }
                e.tables[ti * 4096 + bi] = (KIND_MIXED << 30) | (uint)off;
                e.mixedBricks++;
            }
            e.data = data.ToArray();
            return e;
        }

        private static float3 Unpack(uint p) => new float3(p & 1023u, (p >> 10) & 1023u, (p >> 20) & 1023u);

        /// TorchFlood.hlsl FloodLevels, line for line.
        public static float3 SampleLevels(Encoded e, int3 v)
        {
            if (!e.Any) return 0f;
            int3 c = (v >> 7) - e.origin;
            if (math.any(c < 0) || math.any(c >= e.dims)) return 0f;
            uint t = e.dir[(c.z * e.dims.y + c.y) * e.dims.x + c.x];
            if (t == 0u) return 0f;
            int3 b = (v >> 3) & 15;
            uint en = e.tables[(t - 1u) * 4096u + (uint)((b.z << 8) | (b.y << 4) | b.x)];
            if (en == 0u) return 0f;
            uint off = en & OFFSET_MASK;
            int3 l = v & 7;
            if ((en >> 30) == KIND_MIXED)
            {
                int3 cl = l >> 1;
                uint p = e.data[off + (uint)((cl.z << 4) | (cl.y << 2) | cl.x)];
                if ((p & ESCAPE_BIT) != 0u)
                {
                    int3 q = l & 1;
                    p = e.data[(p & OFFSET_MASK) + (uint)((q.z << 2) | (q.y << 1) | q.x)];
                }
                return Unpack(p);
            }
            float3 fr = math.saturate(((float3)l + 0.5f - 2f) * 0.25f);
            float3 c000 = Unpack(e.data[off + 0]), c100 = Unpack(e.data[off + 1]);
            float3 c010 = Unpack(e.data[off + 2]), c110 = Unpack(e.data[off + 3]);
            float3 c001 = Unpack(e.data[off + 4]), c101 = Unpack(e.data[off + 5]);
            float3 c011 = Unpack(e.data[off + 6]), c111 = Unpack(e.data[off + 7]);
            float3 x00 = math.lerp(c000, c100, fr.x), x10 = math.lerp(c010, c110, fr.x);
            float3 x01 = math.lerp(c001, c101, fr.x), x11 = math.lerp(c011, c111, fr.x);
            return math.lerp(math.lerp(x00, x10, fr.y), math.lerp(x01, x11, fr.y), fr.z);
        }

        private static float MaxLevel(Encoded e, int3 v) => math.cmax(SampleLevels(e, v));

        /// C# twin of TorchFlood.hlsl FloodGradCos (task 164, -tfdir 1).
        public static float GradCos(Encoded e, int3 v, int3 n, float m)
        {
            float3 g = 0f;
            for (int a = 0; a < 3; a++)
            {
                int3 o = new int3(a == 0 ? 2 : 0, a == 1 ? 2 : 0, a == 2 ? 2 : 0);
                float p = MaxLevel(e, v + o), q = MaxLevel(e, v - o);
                g[a] = (p > 0f && q > 0f) ? (p - q) * 0.5f : p > 0f ? p - m : q > 0f ? m - q : 0f;
            }
            float gl = math.length(g);
            return gl > 1e-3f ? math.saturate(math.dot((float3)n, g) / gl) : 0.5f;
        }

        /// C# twin of Raymarch.compute FloodTorchCos (task 164, -tfdir 2); torchesM = voxel centres in metres.
        public static float TorchCos(IReadOnlyList<float3> torchesM, float3 pM, float3 nF, float dPathM, out float cosT)
        {
            float best = 1e9f, wBest = 0f;
            cosT = 0f;
            if (torchesM == null) return 0f;
            foreach (var t in torchesM)
            {
                float3 dv = t - pM;
                float de = math.length(dv);
                float tol = 0.08f * de + 0.25f;
                float ex = dPathM - de;
                if (ex < -tol || math.abs(ex) >= best) continue;
                best = math.abs(ex);
                wBest = 1f - math.saturate((ex - tol) / tol);
                cosT = math.saturate(math.dot(nF, dv / math.max(de, 1e-4f)));
            }
            return wBest;
        }

        /// C# twin of TorchFlood.hlsl FloodLight: the light at the air voxel v in front of a face with normal n
        /// (n = 0: no direction term), with the map p / p2 (Params / Params2; p2.w = the direction mode).
        public static float3 LightTwin(Encoded e, int3 v, int3 n, float4 p, float4 p2, IReadOnlyList<float3> torchesM = null)
        {
            float3 L = SampleLevels(e, v);
            float m = math.cmax(L);
            if (m <= 0f) return 0f;
            float d = math.max(p2.x - m, 0f) / p.w;
            float dir = 1f;
            if (math.any(n != 0))
            {
                int mode = (int)(p2.w + 0.5f);
                float ndl;
                if (mode == 1) ndl = GradCos(e, v, n, m);
                else if (mode == 2)
                {
                    float wS = TorchCos(torchesM, ((float3)v + 0.5f) * 0.1f, (float3)n, d, out float cosT);
                    ndl = wS >= 1f ? cosT : math.lerp(GradCos(e, v, n, m), cosT, wS);
                }
                else if (mode == 3) ndl = 0.5f;
                else
                {
                    float m2 = MaxLevel(e, v + n * 2);
                    ndl = m2 > 0f ? math.saturate((m2 - m) * 0.125f) : 0.5f;
                }
                dir = p2.z + (1f - p2.z) * ndl;
            }
            float x = math.saturate(d / p.z);
            x *= x; x *= x;
            float w = 1f - x;
            w *= w;
            float s = p.x * w / (1f + p.y * d * d);
            float3 col = math.exp2(-p2.y * (m - L) / p2.x);
            col = math.select(0f, col, L > 0f);
            return s * dir * col;
        }

        // =====================================================================
        // GPU
        // =====================================================================

        private static GraphicsBuffer Fit(GraphicsBuffer b, int count, out bool fresh)
        {
            count = math.max(count, 1);
            fresh = false;
            if (b != null && b.count >= count) return b;
            b?.Release();
            fresh = true;
            return new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.ceilpow2(count), 4);
        }

        /// Uploads what changed: the directory whole (small), the dirty chunk tables, the dirty data runs
        /// (gaps under 64 units merged). A buffer that had to grow is re-uploaded whole.
        private void Upload()
        {
            var sw = Stopwatch.StartNew();
            _dirB = Fit(_dirB, _dir.Length, out bool f0);
            if (_dirDirty || f0) { if (_dir.Length > 0) { _dirB.SetData(_dir); UploadBytesTotal += 4L * _dir.Length; } _dirDirty = false; }
            _tablesB = Fit(_tablesB, _tables.Length, out bool f1);
            if (f1 || _tablesRealloc)
            {
                if (_tables.Length > 0) { _tablesB.SetData(_tables.AsArray(), 0, 0, _tables.Length); UploadBytesTotal += 4L * _tables.Length; }
                for (int i = 0; i < _dirtyTable.Length; i++) _dirtyTable[i] = 0;
                _tablesRealloc = false;
            }
            else
            {
                for (int i = 0; i < _dirtyTable.Length; i++)
                {
                    if (_dirtyTable[i] == 0) continue;
                    int j = i;
                    while (j + 1 < _dirtyTable.Length && _dirtyTable[j + 1] != 0) j++;
                    _tablesB.SetData(_tables.AsArray(), i * 4096, i * 4096, (j - i + 1) * 4096);
                    UploadBytesTotal += 4L * 4096 * (j - i + 1);
                    for (int k = i; k <= j; k++) _dirtyTable[k] = 0;
                    i = j;
                }
            }
            _dataB = Fit(_dataB, _data.Length, out bool f2);
            if (f2 || _dataRealloc)
            {
                if (_data.Length > 0) { _dataB.SetData(_data.AsArray(), 0, 0, _data.Length); UploadBytesTotal += 4L * _data.Length; }
                for (int i = 0; i < _dirtyData.Length; i++) _dirtyData[i] = 0;
                _dataRealloc = false;
            }
            else
            {
                int units = _data.Length >> 3, runStart = -1, last = -1000;
                for (int w = 0; w < _dirtyData.Length; w++)
                {
                    ulong bits = _dirtyData[w];
                    if (bits == 0) continue;
                    _dirtyData[w] = 0;
                    while (bits != 0)
                    {
                        int u = w * 64 + math.tzcnt(bits); bits &= bits - 1;
                        if (u >= units) break;
                        if (runStart < 0) { runStart = u; last = u; continue; }
                        if (u - last <= 64) { last = u; continue; }
                        UploadRun(runStart, last); runStart = u; last = u;
                    }
                }
                if (runStart >= 0) UploadRun(runStart, last);
            }
            _uploaded = true;
            LastUploadMs = sw.Elapsed.TotalMilliseconds;
            LastUploadMsStep += LastUploadMs;
        }

        private void UploadRun(int u0, int u1)
        {
            int o = u0 << 3, n = (u1 - u0 + 1) << 3;
            _dataB.SetData(_data.AsArray(), o, o, n);
            UploadBytesTotal += 4L * n;
        }

        /// Reads the three GPU buffers back and compares them with the CPU copy (capture only; synchronous).
        public string VerifyGpu()
        {
            if (!_uploaded || _dirB == null) return "not uploaded";
            var d = new uint[_dir.Length]; if (d.Length > 0) _dirB.GetData(d, 0, 0, d.Length);
            for (int i = 0; i < d.Length; i++) if (d[i] != _dir[i]) return $"dir[{i}] {d[i]} vs {_dir[i]}";
            var t = new uint[_tables.Length]; if (t.Length > 0) _tablesB.GetData(t, 0, 0, t.Length);
            for (int i = 0; i < t.Length; i++) if (t[i] != _tables[i]) return $"tables[{i}] {t[i]:X8} vs {_tables[i]:X8}";
            // data: only the words a table entry reaches (freed blocks may hold stale words on either side)
            var g = new uint[_data.Length]; if (g.Length > 0) _dataB.GetData(g, 0, 0, g.Length);
            for (int ti = 0; ti < _chunkUsed.Length; ti++)
                for (int bi = 0; bi < 4096; bi++)
                {
                    uint en = _tables[ti * 4096 + bi];
                    if (en == 0) continue;
                    int off = (int)(en & OFFSET_MASK), n = (en >> 30) == KIND_AIR ? 8 : 64;
                    for (int k = 0; k < n; k++)
                    {
                        uint c = _data[off + k];
                        if (g[off + k] != c) return $"data[{off + k}] {g[off + k]:X8} vs {c:X8}";
                        if (n == 64 && (c & ESCAPE_BIT) != 0)
                            for (int q = 0; q < 8; q++)
                            {
                                int e = (int)(c & OFFSET_MASK) + q;
                                if (g[e] != _data[e]) return $"escape data[{e}] {g[e]:X8} vs {_data[e]:X8}";
                            }
                    }
                }
            return null;
        }

        public void Bind(UnityEngine.Rendering.ComputeCommandBuffer cmd, ComputeShader cs, int kernel)
        {
            cmd.SetComputeBufferParam(cs, kernel, "_FloodDir", _dirB);
            cmd.SetComputeBufferParam(cs, kernel, "_FloodTables", _tablesB);
            cmd.SetComputeBufferParam(cs, kernel, "_FloodData", _dataB);
            cmd.SetComputeIntParams(cs, "_FloodDirOrigin", _dirOrigin.x, _dirOrigin.y, _dirOrigin.z, 0);
            cmd.SetComputeIntParams(cs, "_FloodDirDims", _dirDims.x, _dirDims.y, _dirDims.z, _counts[3] > 0 ? 1 : 0);
            cmd.SetComputeVectorParam(cs, "_FloodParams", Params);
            cmd.SetComputeVectorParam(cs, "_FloodParams2", Params2);
        }

        /// GPU == C# twin probe: samples FloodLevels on the GPU at `voxels`
        /// (synchronous readback; capture only). Returns (levels xyz, luma w).
        public float4[] Probe(IReadOnlyList<int3> voxels)
        {
            if (!_uploaded || voxels.Count == 0) return Array.Empty<float4>();
            var cs = Resources.Load<ComputeShader>("TorchFloodProbe");
            if (cs == null) { Debug.LogError("[torchflood] TorchFloodProbe.compute missing"); return Array.Empty<float4>(); }
            int k = cs.FindKernel("CSFloodProbe");
            var inB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels.Count, 16);
            var outB = new GraphicsBuffer(GraphicsBuffer.Target.Structured, voxels.Count, 16);
            var arr = new int4[voxels.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = new int4(voxels[i], 0);
            inB.SetData(arr);
            cs.SetBuffer(k, "_ProbeIn", inB);
            cs.SetBuffer(k, "_ProbeOut", outB);
            cs.SetInt("_ProbeCount", voxels.Count);
            cs.SetBuffer(k, "_FloodDir", _dirB);
            cs.SetBuffer(k, "_FloodTables", _tablesB);
            cs.SetBuffer(k, "_FloodData", _dataB);
            cs.SetInts("_FloodDirOrigin", _dirOrigin.x, _dirOrigin.y, _dirOrigin.z, 0);
            cs.SetInts("_FloodDirDims", _dirDims.x, _dirDims.y, _dirDims.z, _counts[3] > 0 ? 1 : 0);
            cs.SetVector("_FloodParams", Params);
            cs.SetVector("_FloodParams2", Params2);
            cs.Dispatch(k, (voxels.Count + 63) / 64, 1, 1);
            var res = new float4[voxels.Count];
            outB.GetData(res);
            inB.Release(); outB.Release();
            return res;
        }

        public void Dispose()
        {
            ResetAll(_store);
            FreeUnion();
            foreach (var s in _slots) { s.s.Dispose(); s.stats.Dispose(); }
            _slots.Clear();
            _dirB?.Release(); _tablesB?.Release(); _dataB?.Release();
            _dirB = _tablesB = _dataB = null;
            Oracle?.Dispose(); Oracle = null;
            _uploaded = false;
            if (ReferenceEquals(_inst, this)) _inst = null;
        }
    }
}
