// Assets/CoreEngine/Mirror/ShellStreamer.cs
//
// Amendment 8.14 (engine step 11a, task 77): fills the coarse shells 1..n-1
// of a ShellLayout around the camera from GenerateShellChunk (row 12) on
// dedicated worker threads, and uploads them on the main thread inside the
// remainder of the shared 3 MiB/frame upload cap (row 14). Nothing renders
// from it yet: 11b adds the raymarch and the seams.
//
// WHAT EACH SHELL STORES (8.14 §1-§2; ShellLayout's header has the rules):
// the chunks within +-R of the camera's chunk (in that shell's own chunks),
// clamped to the world (x, z in [0, worldSize), y in [0, the shell's content
// top layer]), minus the hole +-H the finer shell covers (all three axes).
// A chunk leaving that set is evicted the frame the camera's chunk changes.
//
// ORDER (§3 "Upload budget"): the coarsest shell's missing chunks first,
// nearest the camera first within a shell. A coarse chunk is cheap and fills
// a hole; a missing fine chunk only looks blockier (Q2 (a)).
//
// THREADING (single-writer, CLAUDE.md): workers only write the ShellChunk
// scratch handed to them; every ShellStore mutation, every GPU write and every
// set below is main-thread only. Hand-over is the same BlockingCollection /
// ConcurrentQueue pair StreamManager uses -- no lock in this file.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Mirror
{
    public sealed class ShellStreamer : IDisposable
    {
        public ShellLayout Layout { get; }
        public int FirstShell { get; }
        public int LastShell { get; }
        /// Tests: generate inline in Update (no threads), SyncChunksPerUpdate per call.
        public bool Synchronous { get; }
        public int SyncChunksPerUpdate = 16;

        // Stats (main thread).
        public long Requested, Generated, DroppedStale, OverBudgetFrames, GenErrors;
        public double GenMsTotal;            // worker-side, summed
        public int LastBytes { get; private set; }

        private readonly TerrainGenChunkSource _src;
        private readonly ShellStore[] _stores;
        private readonly int3[] _cc;          // camera chunk per shell
        private readonly bool[] _ccValid;
        private readonly int3[] _lo, _hi;     // stored box (inclusive), world-clamped
        private readonly List<int3>[] _cand;  // missing chunks, FARTHEST first (pop from the end)
        private readonly HashSet<long> _inFlight = new HashSet<long>();
        private readonly HashSet<long>[] _refused;
        private readonly Stack<TerrainGenChunkSource.ShellChunk> _free = new Stack<TerrainGenChunkSource.ShellChunk>();
        private readonly List<TerrainGenChunkSource.ShellChunk> _all = new List<TerrainGenChunkSource.ShellChunk>();
        private readonly Queue<TerrainGenChunkSource.ShellChunk> _pending = new Queue<TerrainGenChunkSource.ShellChunk>();
        private readonly BlockingCollection<TerrainGenChunkSource.ShellChunk> _jobs;
        private readonly ConcurrentQueue<(TerrainGenChunkSource.ShellChunk sc, double ms, bool ok)> _done
            = new ConcurrentQueue<(TerrainGenChunkSource.ShellChunk, double, bool)>();
        private readonly Thread[] _threads;
        private readonly int _worldMaxVoxel;

        public ShellStreamer(ShellLayout layout, TerrainGenChunkSource src, int workers,
                             bool synchronous = false, int firstShell = 1, int lastShell = -1)
        {
            Layout = layout; _src = src; Synchronous = synchronous;
            FirstShell = math.max(1, firstShell);
            LastShell = lastShell < 0 ? layout.ShellCount - 1 : math.min(lastShell, layout.ShellCount - 1);
            _worldMaxVoxel = src.Spec.worldSizeVoxels - 1;
            int n = layout.ShellCount;
            _stores = new ShellStore[n];
            _cc = new int3[n]; _ccValid = new bool[n]; _lo = new int3[n]; _hi = new int3[n];
            _cand = new List<int3>[n]; _refused = new HashSet<long>[n];
            for (int k = FirstShell; k <= LastShell; k++)
            {
                var s = layout[k];
                _stores[k] = new ShellStore(k, s.Ring, s.PoolCap, s.PageCap);
                _cand[k] = new List<int3>(); _refused[k] = new HashSet<long>();
            }
            workers = math.max(1, workers);
            int scratch = synchronous ? 3 : workers + 2;   // + 2: results waiting for upload budget
            for (int i = 0; i < scratch; i++)
            {
                var sc = new TerrainGenChunkSource.ShellChunk();
                _all.Add(sc); _free.Push(sc);
            }
            if (!synchronous)
            {
                _jobs = new BlockingCollection<TerrainGenChunkSource.ShellChunk>();
                _threads = new Thread[workers];
                for (int i = 0; i < workers; i++)
                {
                    _threads[i] = new Thread(WorkerLoop) { IsBackground = true, Name = $"ShellGen{i}" };
                    _threads[i].Start();
                }
            }
        }

        public ShellStore Store(int k) => _stores[k];
        public int InFlight => _inFlight.Count;
        public int Pending => _pending.Count;

        private static long Key(int k, int3 c) =>
            ((long)k << 60) | ((long)(c.x & 0xFFFFF) << 40) | ((long)(c.y & 0xFFFFF) << 20) | (long)(c.z & 0xFFFFF);

        /// The camera's chunk in shell k, from its position in metres (integer
        /// voxel first, then shifts: no float division, §6.2).
        public static int3 CameraChunk(float3 camMetres, int k)
        {
            int3 v = (int3)math.floor(camMetres * 10f);
            return v >> (7 + k);
        }

        public void Box(int k, out int3 lo, out int3 hi, out int3 cc) { lo = _lo[k]; hi = _hi[k]; cc = _cc[k]; }

        /// Does shell k store chunk c for the current camera?
        public bool Wanted(int k, int3 c)
        {
            if (math.any(c < _lo[k]) || math.any(c > _hi[k])) return false;
            int H = Layout[k].Hole;
            return H < 0 || math.cmax(math.abs(c - _cc[k])) > H;
        }

        /// Chunks still missing (not stored, not refused) in shell k.
        public int Missing(int k) => _cand[k] == null ? 0 : _cand[k].Count;
        public bool IsRefused(int k, int3 c) => _refused[k].Contains(Key(k, c));
        public bool IsInFlight(int k, int3 c) => _inFlight.Contains(Key(k, c));

        public bool Idle
        {
            get
            {
                if (_inFlight.Count > 0 || _pending.Count > 0) return false;
                for (int k = FirstShell; k <= LastShell; k++)
                {
                    if (_stores[k].DirectoryDirty) return false;
                    foreach (var c in _cand[k]) if (!_stores[k].Holds(c) && !_refused[k].Contains(Key(k, c))) return false;
                }
                return true;
            }
        }

        /// One frame: move the boxes, evict, finish uploads in progress, store
        /// finished chunks, dispatch generation, flush directories -- every GPU
        /// byte inside `budgetBytes`.
        public void Update(float3 camMetres, int budgetBytes)
        {
            int budget = math.max(0, budgetBytes);
            int start = budget;
            for (int k = FirstShell; k <= LastShell; k++) Slide(k, camMetres);

            // Chunks part-way up (ShellStore's multi-frame upload) first.
            for (int k = FirstShell; k <= LastShell; k++)
            {
                var st = _stores[k];
                if (!st.JobActive) continue;
                long key = Key(k, st.JobCoord);
                if (!Wanted(k, st.JobCoord)) { st.AbortJob(); _inFlight.Remove(key); DroppedStale++; continue; }
                if (st.Continue(ref budget)) _inFlight.Remove(key);
            }
            // Then finished chunks, oldest first. A chunk that cannot start
            // (budget, or its shell is still uploading another) waits in order.
            while (_pending.Count > 0)
            {
                var sc = _pending.Peek();
                if (!Upload(sc, ref budget)) break;
                _pending.Dequeue(); Recycle(sc);
            }
            while (_done.TryDequeue(out var r))
            {
                if (!r.ok) { GenErrors++; _inFlight.Remove(Key(r.sc.Shell, r.sc.Coord)); Recycle(r.sc); continue; }
                Generated++; GenMsTotal += r.ms;
                if (_pending.Count > 0 || !Upload(r.sc, ref budget)) { _pending.Enqueue(r.sc); continue; }
                Recycle(r.sc);
            }
            if (_pending.Count > 0) OverBudgetFrames++;

            int made = 0;
            while (_free.Count > 0 && (!Synchronous || made < SyncChunksPerUpdate) && NextCandidate(out int k, out int3 c))
            {
                var sc = _free.Pop();
                sc.Shell = k; sc.Coord = c;
                _inFlight.Add(Key(k, c)); Requested++;
                if (!Synchronous) { _jobs.Add(sc); continue; }
                // Tests: generate here; it is stored from _done on the next Update.
                var t = System.Diagnostics.Stopwatch.StartNew();
                _src.GenerateShellChunk(k, c, sc);
                _done.Enqueue((sc, t.Elapsed.TotalMilliseconds, true));
                made++;
            }
            for (int k = FirstShell; k <= LastShell; k++) _stores[k].FlushDirectory(ref budget);
            LastBytes = start - budget;
        }

        /// Hand a finished chunk to its store. True = consumed (stored, started,
        /// refused or no longer wanted: `sc` may be reused); false = try later.
        private bool Upload(TerrainGenChunkSource.ShellChunk sc, ref int budget)
        {
            int k = sc.Shell; long key = Key(k, sc.Coord);
            if (!Wanted(k, sc.Coord) || _stores[k].Holds(sc.Coord))
            {
                _inFlight.Remove(key); DroppedStale++;
                return true;
            }
            switch (_stores[k].Store(sc, ref budget))
            {
                case ShellStore.StoreResult.OverBudget:
                case ShellStore.StoreResult.Busy: return false;
                case ShellStore.StoreResult.InProgress: return true;   // stays in flight until Continue finishes it
                case ShellStore.StoreResult.Refused: _inFlight.Remove(key); _refused[k].Add(key); return true;
                default: _inFlight.Remove(key); return true;
            }
        }

        private void Recycle(TerrainGenChunkSource.ShellChunk sc) => _free.Push(sc);

        private bool NextCandidate(out int k, out int3 c)
        {
            for (k = LastShell; k >= FirstShell; k--)   // coarsest first
            {
                var list = _cand[k];
                while (list.Count > 0)
                {
                    c = list[list.Count - 1]; list.RemoveAt(list.Count - 1);
                    long key = Key(k, c);
                    if (!Wanted(k, c) || _stores[k].Holds(c) || _inFlight.Contains(key) || _refused[k].Contains(key)) continue;
                    return true;
                }
            }
            k = -1; c = default;
            return false;
        }

        /// Recompute shell k's box when the camera changes chunk there: evict
        /// what left it, list what is missing (nearest last = popped first).
        private void Slide(int k, float3 camMetres)
        {
            int3 cc = CameraChunk(camMetres, k);
            if (_ccValid[k] && cc.Equals(_cc[k])) return;
            var s = Layout[k];
            int shift = 7 + k;
            int3 wmax = new int3(_worldMaxVoxel >> shift, _src.ShellTopLayer(k), _worldMaxVoxel >> shift);
            int3 lo = math.max(cc - s.Reach, int3.zero), hi = math.min(cc + s.Reach, wmax);
            // The vertical ring may be shorter than 2R+1 (shells 4+); the world's
            // content layers fit it (asserted below), else keep the layers nearest the eye.
            if (hi.y - lo.y + 1 > s.Ring.y)
            {
                lo.y = math.clamp(cc.y - s.Ring.y / 2, lo.y, hi.y - s.Ring.y + 1);
                hi.y = lo.y + s.Ring.y - 1;
            }
            _cc[k] = cc; _ccValid[k] = true; _lo[k] = lo; _hi[k] = hi;
            _refused[k].Clear();   // room may have freed up

            var st = _stores[k];
            for (int slot = 0; slot < st.SlotCount; slot++)
                if (st.SlotTagged(slot, out int3 tag) && !Wanted(k, tag)) st.EvictSlot(slot);

            var list = _cand[k];
            list.Clear();
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var c = new int3(x, y, z);
                if (Wanted(k, c) && !st.Holds(c)) list.Add(c);
            }
            // Farthest first, so the nearest is popped first (squared distance in chunks, integer).
            list.Sort((a, b) =>
            {
                int3 da = a - cc, db = b - cc;
                return math.csum(db * db).CompareTo(math.csum(da * da));
            });
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (var sc in _jobs.GetConsumingEnumerable())
                {
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    bool ok = true;
                    try { _src.GenerateShellChunk(sc.Shell, sc.Coord, sc); }
                    catch (Exception e) { ok = false; UnityEngine.Debug.LogError($"[ShellStreamer] shell {sc.Shell} chunk {sc.Coord}: {e}"); }
                    _done.Enqueue((sc, t.Elapsed.TotalMilliseconds, ok));
                }
            }
            catch (InvalidOperationException) { }   // CompleteAdding during shutdown
        }

        public void Dispose()
        {
            if (_jobs != null)
            {
                _jobs.CompleteAdding();
                foreach (var t in _threads) t.Join();
                _jobs.Dispose();
            }
            foreach (var sc in _all) sc.Dispose();
            _all.Clear();
            for (int k = 0; k < _stores.Length; k++) { _stores[k]?.Dispose(); _stores[k] = null; }
        }
    }
}
