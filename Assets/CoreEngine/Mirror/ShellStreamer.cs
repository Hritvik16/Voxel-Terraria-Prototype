// Assets/CoreEngine/Mirror/ShellStreamer.cs
//
// Amendment 8.14 (engine step 11a, task 77): fills the coarse shells 1..n-1
// of a ShellLayout around the camera from GenerateShellChunk (row 12) on
// dedicated worker threads, and uploads them on the main thread inside the
// remainder of the shared 3 MiB/frame upload cap (row 14). Step 11b (task 80):
// Raymarch.compute draws them (ShaderParams below); `Active` is the streamer
// the renderer reads, set by whoever owns it (Phase4Bootstrapper).
//
// WHAT THE GPU MAY SEE (task 80): the shader trusts a directory entry only for
// a chunk inside the PUBLISHED box -- the box as of the last directory flush
// that went up. A box moves in Slide (evicting what left it) before anything
// is stored; the directory is flushed right after, and if that flush does not
// fit the budget no chunk is stored this frame, so freed bricks and pages are
// never rewritten while a GPU entry still points at them, and a stale entry
// can never alias a chunk of the new box (the ring holds the box once, pow2).
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
using UnityEngine;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Mirror
{
    public sealed class ShellStreamer : IDisposable
    {
        /// The streamer the raymarch draws (task 80); null = no shells.
        public static ShellStreamer Active;

        public ShellLayout Layout { get; }
        /// One directory / page / brick buffer for all shells (ShellStore's header).
        public ShellStore.SharedBuffers Gpu { get; }
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
        private readonly int3[] _pubLo, _pubHi, _pubCc;   // the box the GPU directory matches
        private readonly bool[] _pubValid;
        private readonly List<int3>[] _cand;  // missing chunks, FARTHEST first (pop from the end)
        private readonly HashSet<long> _inFlight = new HashSet<long>();
        // Refused chunks (pool or pages full, row 15) -> the dense bricks they
        // need. Retried only once the shell has that much room again: a full
        // coarse shell must not keep the workers regenerating its refusals
        // while the finer shells wait (found in the task 77 player run).
        private readonly Dictionary<long, int>[] _refused;
        private readonly List<long> _pruneScratch = new List<long>();
        // A shell that just refused a chunk is FULL until it has room for one
        // that size again: its other missing chunks wait (each would cost a
        // generation only to be refused) and the finer shells get the workers.
        private readonly int[] _fullNeed;
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
            _pubLo = new int3[n]; _pubHi = new int3[n]; _pubCc = new int3[n]; _pubValid = new bool[n];
            _cand = new List<int3>[n]; _refused = new Dictionary<long, int>[n]; _fullNeed = new int[n];
            int dirN = 0, pageN = 0, brickN = 0;
            for (int k = FirstShell; k <= LastShell; k++)
            {
                var s = layout[k];
                dirN += s.RingSlots; pageN += s.PageCap; brickN += s.PoolCap;
            }
            Gpu = new ShellStore.SharedBuffers(dirN, pageN, brickN);
            dirN = 0; pageN = 0; brickN = 0;
            for (int k = FirstShell; k <= LastShell; k++)
            {
                var s = layout[k];
                _stores[k] = new ShellStore(k, s.Ring, s.PoolCap, s.PageCap, Gpu, dirN, pageN, brickN);
                dirN += s.RingSlots; pageN += s.PageCap; brickN += s.PoolCap;
                _cand[k] = new List<int3>(); _refused[k] = new Dictionary<long, int>();
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
        /// Key's chunk back (20-bit fields, sign-extended by the arithmetic shifts).
        private static int3 KeyCoord(long key) =>
            new int3((int)(key >> 40 << 44 >> 44), (int)(key >> 20 << 44 >> 44), (int)(key << 44 >> 44));

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
        public bool IsRefused(int k, int3 c) => _refused[k].ContainsKey(Key(k, c));
        public bool IsInFlight(int k, int3 c) => _inFlight.Contains(Key(k, c));

        public bool Idle
        {
            get
            {
                if (_inFlight.Count > 0 || _pending.Count > 0) return false;
                for (int k = FirstShell; k <= LastShell; k++)
                {
                    if (_stores[k].DirectoryDirty) return false;
                    if (ShellFull(k)) continue;
                    foreach (var c in _cand[k]) if (!_stores[k].Holds(c) && !Blocked(k, Key(k, c))) return false;
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
            // Publish the evictions before any freed brick or page is reused
            // (header: WHAT THE GPU MAY SEE). Over budget: store nothing now.
            bool published = true;
            for (int k = FirstShell; k <= LastShell; k++) published &= Publish(k, ref budget);
            if (!published)
            {
                OverBudgetFrames++;
                LastBytes = start - budget;
                return;
            }

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
            for (int k = FirstShell; k <= LastShell; k++) Publish(k, ref budget);
            LastBytes = start - budget;
        }

        /// Flush shell k's directory; when it is on the GPU, the current box is the published one.
        private bool Publish(int k, ref int budget)
        {
            if (!_stores[k].FlushDirectory(ref budget)) return false;
            if (_ccValid[k]) { _pubLo[k] = _lo[k]; _pubHi[k] = _hi[k]; _pubCc[k] = _cc[k]; _pubValid[k] = true; }
            return true;
        }

        /// The box the GPU directory of shell k matches (false: nothing published yet).
        public bool PublishedBox(int k, out int3 lo, out int3 hi, out int3 cc)
        {
            lo = _pubLo[k]; hi = _pubHi[k]; cc = _pubCc[k];
            return _pubValid[k];
        }

        /// Raymarch.compute's shell constants (8.14 row 1: count and bounds from a
        /// constant buffer, arrays sized for 8). Per shell k, all as floats (every
        /// value is an integer below 2^24, exact):
        ///   ring[k]   = (ring x, y, z, directory base)
        ///   lo[k]     = (published box min, page base);  hi[k] = (box max, brick base)
        ///   cam[k]    = (camera chunk, hole H; -1 = no hole)
        ///   bounds[k] = (inner, outer hand-over distance in 0.1 m voxels, voxel size in 0.1 m voxels, 0)
        /// A shell with nothing published gets an empty box (lo > hi): every chunk ABSENT.
        public void ShaderParams(Vector4[] ring, Vector4[] lo, Vector4[] hi, Vector4[] cam, Vector4[] bounds)
        {
            for (int k = 0; k < ShellLayout.MAX_SHELLS; k++)
            {
                ring[k] = new Vector4(1, 1, 1, 0); lo[k] = new Vector4(1, 1, 1, 0); hi[k] = new Vector4(0, 0, 0, 0);
                cam[k] = new Vector4(0, 0, 0, -1); bounds[k] = Vector4.zero;
                if (k >= Layout.ShellCount) continue;
                var s = Layout[k];
                bounds[k] = new Vector4(s.InnerM * 10f, s.OuterM * 10f, 1 << k, 0f);
                if (k < FirstShell || k > LastShell) continue;
                var st = _stores[k];
                ring[k] = new Vector4(st.Ring.x, st.Ring.y, st.Ring.z, st.DirBase);
                if (!PublishedBox(k, out int3 l, out int3 h, out int3 c)) continue;
                lo[k] = new Vector4(l.x, l.y, l.z, st.PageBase);
                hi[k] = new Vector4(h.x, h.y, h.z, st.BrickBase);
                cam[k] = new Vector4(c.x, c.y, c.z, s.Hole);
            }
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
                case ShellStore.StoreResult.Refused:
                    _inFlight.Remove(key);
                    ShellStore.UploadBytes(sc, out int dense, out _, out _);
                    _refused[k][key] = dense;
                    _fullNeed[k] = math.max(1, dense);
                    return true;
                default: _inFlight.Remove(key); return true;
            }
        }

        private void Recycle(TerrainGenChunkSource.ShellChunk sc) => _free.Push(sc);

        private bool NextCandidate(out int k, out int3 c)
        {
            for (k = LastShell; k >= FirstShell; k--)   // coarsest first
            {
                if (ShellFull(k)) continue;
                var list = _cand[k];
                while (list.Count > 0)
                {
                    c = list[list.Count - 1]; list.RemoveAt(list.Count - 1);
                    long key = Key(k, c);
                    if (!Wanted(k, c) || _stores[k].Holds(c) || _inFlight.Contains(key) || Blocked(k, key)) continue;
                    return true;
                }
            }
            k = -1; c = default;
            return false;
        }

        public bool ShellFull(int k)
        {
            if (_fullNeed[k] == 0) return false;
            var st = _stores[k];
            if (st.FreePages > 0 && st.FreeBricks >= _fullNeed[k]) { _fullNeed[k] = 0; return false; }
            return true;
        }

        /// A refused chunk stays out until its shell can hold it.
        private bool Blocked(int k, long key)
        {
            if (!_refused[k].TryGetValue(key, out int dense)) return false;
            var st = _stores[k];
            if (st.FreePages > 0 && st.FreeBricks >= dense) { _refused[k].Remove(key); return false; }
            return true;
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
            _pruneScratch.Clear();
            foreach (var kv in _refused[k])
                if (!Wanted(k, KeyCoord(kv.Key)))
                    _pruneScratch.Add(kv.Key);
            foreach (long key in _pruneScratch) _refused[k].Remove(key);

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
            Gpu?.Dispose();
            if (Active == this) Active = null;
        }
    }
}
