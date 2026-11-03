// Assets/CoreEngine/Mirror/UploadRing.cs
//
// TASK 168e (PERF_EXACT_CUTS row E, owner Q102 (a) "a for all": "lands a frame later" counts as exact).
// UPLOAD PACING THROUGH A STAGING RING. OFF by default (`-uploadring 1` = on).
//
// WHAT IT REPLACES. Every per-frame mirror write (TerrainClipmap: clipmap entries, dense bodies, the packed
// air-mip, the fluid terrain directory; ShellStore: bodies, pages + tails, directory, heights) was a
// GraphicsBuffer.SetData into a buffer the GPU may still be reading for an in-flight frame. What the driver
// does then is not ours to see (rename, stage, or wait); 168d found 57 % of play's > 33 ms frames have no CPU
// section >= 5 ms (a GPU / preUpdate wait), and frames with > 1 MB of uploads are > 33 ms 68-78 % of the time
// vs 20-25 % with none (168d's spikes.csv, `Logs/overnight/168e_corr.txt`).
//
// HOW. With the ring on, a write copies its words into a STAGING slot (a LockBufferForWrite GraphicsBuffer,
// i.e. CPU-visible memory the GPU is NOT reading) and appends a record (staging offset, destination offset,
// count). Flush() hands the records to `UploadScatter.compute`: one dispatch per destination buffer copies
// staging -> destination ON THE GPU, at the same place in the command stream where SetData's copy would be.
// The CPU never touches a buffer the GPU reads; the destination buffers are written only by that kernel.
//   * CPU stays the sole writer: the kernel copies CPU-produced bytes verbatim (CLAUDE.md hard invariant 1).
//   * No locks, main thread only (single-writer rule). LockBufferForWrite is a mapping, not a mutex.
//   * EXACT: a dispatch's records never overlap (checked per flush; an overlapping pair splits the flush into
//     submission-order dispatches), and flushes run in order, so every word ends as SetData's last write.
//     `UploadRingTests` drives the logic against a CPU "GPU" that executes dispatches frames LATER (so an early
//     reuse of a staging slot would corrupt the result): drained state == sequential SetData, byte for byte.
//     In the player `-uploadcheck` (UploadShadow) reads the GPU buffers back and compares every written word.
//   * A slot is reused only after the GPU passed its last dispatch: each flush ends with a 4-byte
//     AsyncGPUReadback of a ticket the kernel writes (a real RAW dependency), polled once per frame (Tick).
//     No free slot -> a TEMPORARY slot (counted); temporaries over TEMP_LIMIT -> wait for the GPU (counted;
//     only big bursts, e.g. the startup fill).
//
// PACING. FrameBudget(): with the ring on, the per-frame upload budget the streamer and the shells share
// (8.14 row 14, today 3 MiB) is capped at CapBytes (`-uploadcap KB`, default 1024; 0 = keep 3 MiB). The cap
// reuses the existing deferral paths (TerrainClipmap's dirty set FIFO by distance, ShellStore's job/budget),
// which were already exact: a deferred chunk uploads on a later frame. §3.7's exempt chunks stay unbounded.
//
// MEMORY. 4 slots x (1 MiB staging + 8,192 x 16 B records) = 4.5 MiB, + temporaries during bursts (reported;
// each at most one slot, TEMP_LIMIT 32 MiB in all, then the CPU waits for the GPU instead).

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.Memory
{
    public abstract class UploadRing
    {
        // ------------------------------------------------------------------ switches
        /// `-uploadring 1` (task 168e). Read through On.
        public static bool Enabled;
        /// `-uploadcap KB`: the shared per-frame upload cap while the ring is on; 0 = no extra cap.
        public static int CapBytes = 1 << 20;
        public const int SLOTS = 4;
        public const int SLOT_UINTS = 1 << 18;          // 1 MiB
        public const int SLOT_RECORDS = 8192;
        public const int PIECE_UINTS = 4096;            // one thread group copies at most this many words
        public const long TEMP_LIMIT_BYTES = 32L << 20;

        private static UploadRing _shared;
        private static bool _unavailable;
        /// The player's ring (null until the first use with Enabled and a GPU that can run it).
        public static UploadRing Shared => _shared;

        /// Ring writes in force: enabled, and a ring exists (created on first call).
        public static bool On
        {
            get
            {
                if (!Enabled) return false;
                if (_shared != null) return true;
                if (_unavailable) return false;
                _shared = GpuUploadRing.TryCreate();
                if (_shared == null) { _unavailable = true; Debug.LogWarning("[UploadRing] not available (no compute / async readback / shader): SetData as before"); }
                else Debug.Log($"[UploadRing] ON: {SLOTS} x {SLOT_UINTS * 4 / 1024} KiB staging, cap {(CapBytes > 0 ? $"{CapBytes / 1024} KiB" : "none (3 MiB)")} per frame (task 168e)");
                return _shared != null;
            }
        }

        /// The per-frame upload budget (bytes) for the clipmap + shells: defaultBudget, capped while the ring is on.
        public static int FrameBudget(int defaultBudget) => On && CapBytes > 0 ? math.min(defaultBudget, CapBytes) : defaultBudget;

        public static void FlushShared() { if (_shared != null) _shared.Flush(); }
        public static void TickShared() { if (_shared != null) _shared.Tick(); }

        /// Turn the ring on/off at run time (capture flips). Turning it OFF waits until the GPU has run every
        /// staged copy, so a later SetData can never be overtaken by an older ring copy of the same words.
        public static void SetEnabled(bool on)
        {
            if (!on && _shared != null) { _shared.Flush(); _shared.WaitAll(); _shared.ReapTemps(); }
            Enabled = on;
        }

        // ------------------------------------------------------------------ slots
        protected sealed class Slot
        {
            public int Cap, RecCap, Head, RecHead;
            public bool Temp;
            public bool Pending;          // a guard (the GPU passing this slot's last dispatch) is outstanding
            public object Staging, Recs;
            public AsyncGPUReadbackRequest Req;   // GPU guard (no boxing: polled every frame)
            public long Fence = -1;               // CPU stand-in's guard
        }

        private readonly List<Slot> _ring = new List<Slot>();
        private readonly List<Slot> _temps = new List<Slot>();
        private readonly int _slotUints, _recCap;
        private Slot _cur;
        private int _curRing;
        private bool _open;
        private int _openStart;
        private uint4[] _recs = new uint4[1024];     // pending: x staging, y destination, z count, w destination index
        private int _recN;
        private uint4[] _grp = new uint4[1024];
        private long[] _keys = new long[1024];
        private readonly object[] _dsts = new object[16];
        private int _dstN;
        private int _pendingFrame = int.MinValue;
        private int _statFrame = int.MinValue;
        private long _frameBytes;

        // ------------------------------------------------------------------ counters (report line)
        public long BytesStagedTotal, WritesTotal, FlushesTotal, DispatchesTotal, OverlapSplitsTotal;
        public long TempsTotal, WaitsTotal, RotationsTotal, FramesWithWrites;
        public long TempBytesOutstanding, TempBytesPeak;
        public long FrameBytesMax;
        /// Largest temporary slot (words). Writes go in pieces of at most one slot, so this never exceeds a ring slot
        /// and TEMP_LIMIT bounds the temporaries (before that fix one 60+ MB body run made one 68 MB temporary).
        public int TempSlotUintsMax;
        public int SlotUints => _slotUints;
        /// Tests only: reuse slots without waiting for the GPU (UploadRingTests' negative control).
        public bool DebugIgnoreGuards;
        public long RingBytes => (long)_ring.Count * (_slotUints * 4L + _recCap * 16L);

        protected UploadRing(int slots, int slotUints, int recCap)
        {
            _slotUints = slotUints; _recCap = recCap;
            for (int i = 0; i < slots; i++)
            {
                var s = new Slot { Cap = slotUints, RecCap = recCap };
                _ring.Add(s);
            }
            _curRing = 0;
        }

        /// Subclasses call this once their device is ready (AllocSlot may use their fields).
        protected void AllocRing() { foreach (var s in _ring) AllocSlot(s); _cur = _ring[0]; }

        // ------------------------------------------------------------------ device
        protected abstract int Frame { get; }
        protected abstract void AllocSlot(Slot s);
        protected abstract void FreeSlot(Slot s);
        protected abstract void Open(Slot s, int start, int count);
        protected abstract void Close(Slot s, int start, int written);
        protected abstract void CopyIn(Slot s, int at, uint[] src, int srcStart, int count);
        protected abstract void CopyIn(Slot s, int at, int[] src, int srcStart, int count);
        protected abstract void CopyIn(Slot s, int at, NativeArray<uint> src, int srcStart, int count);
        /// Upload recs[start, start + count) to the slot's records at recAt, then copy them into dst.
        protected abstract void Dispatch(Slot s, object dst, uint4[] recs, int start, int count, int recAt);
        /// After a flush's last dispatch: something that completes once the GPU has passed it.
        protected abstract void IssueGuard(Slot s);
        /// The guard has completed (polled; called at most once per frame per slot plus on demand).
        protected abstract bool GuardDone(Slot s);
        /// Block until the guard completed.
        protected abstract void WaitGuard(Slot s);

        // ------------------------------------------------------------------ writes
        public void Write(object dst, uint[] src, int srcStart, int dstStart, int count)
        {
            for (int done = 0; done < count; done += _slotUints)   // pieces of at most one slot (bounds a temporary)
            {
                int n = math.min(count - done, _slotUints);
                int at = Place(dst, dstStart + done, n);
                CopyIn(_cur, at, src, srcStart + done, n);
            }
        }

        public void Write(object dst, int[] src, int srcStart, int dstStart, int count)
        {
            for (int done = 0; done < count; done += _slotUints)   // pieces of at most one slot (bounds a temporary)
            {
                int n = math.min(count - done, _slotUints);
                int at = Place(dst, dstStart + done, n);
                CopyIn(_cur, at, src, srcStart + done, n);
            }
        }

        public void Write(object dst, NativeArray<uint> src, int srcStart, int dstStart, int count)
        {
            for (int done = 0; done < count; done += _slotUints)   // pieces of at most one slot (bounds a temporary)
            {
                int n = math.min(count - done, _slotUints);
                int at = Place(dst, dstStart + done, n);
                CopyIn(_cur, at, src, srcStart + done, n);
            }
        }

        private int Place(object dst, int dstStart, int count)
        {
            int f = Frame;
            if (_recN > 0 && f != _pendingFrame) Flush();   // a pass that did not flush: still in order, one frame late
            RollFrame(f);
            int di = Array.IndexOf(_dsts, dst, 0, _dstN);
            if (di < 0 && _dstN == _dsts.Length) Flush();
            int pieces = (count + PIECE_UINTS - 1) / PIECE_UINTS;
            if (_cur.Head + count > _cur.Cap || _cur.RecHead + _recN + pieces > _cur.RecCap) Rotate(count, pieces);
            di = Array.IndexOf(_dsts, dst, 0, _dstN);
            if (di < 0) { di = _dstN; _dsts[_dstN++] = dst; }
            if (!_open) { Open(_cur, _cur.Head, _cur.Cap - _cur.Head); _open = true; _openStart = _cur.Head; }
            int at = _cur.Head;
            _cur.Head += count;
            if (_recN + pieces > _recs.Length) Array.Resize(ref _recs, math.max(_recs.Length * 2, _recN + pieces));
            for (int off = 0; off < count; off += PIECE_UINTS)
                _recs[_recN++] = new uint4((uint)(at + off), (uint)(dstStart + off), (uint)math.min(PIECE_UINTS, count - off), (uint)di);
            _pendingFrame = f;
            WritesTotal++; BytesStagedTotal += count * 4L; _frameBytes += count * 4L;
            return at;
        }

        private void RollFrame(int f)
        {
            if (f == _statFrame) return;
            if (_frameBytes > 0) { FramesWithWrites++; FrameBytesMax = math.max(FrameBytesMax, _frameBytes); }
            _frameBytes = 0; _statFrame = f;
        }

        /// Dispatch every pending record (call at the end of each producer pass).
        public void Flush()
        {
            if (_open) { Close(_cur, _openStart, _cur.Head - _openStart); _open = false; }
            if (_recN == 0) { ClearDsts(); return; }
            for (int d = 0; d < _dstN; d++)
            {
                if (_grp.Length < _recN) { _grp = new uint4[_recs.Length]; _keys = new long[_recs.Length]; }
                int n = 0;
                for (int i = 0; i < _recN; i++) if (_recs[i].w == (uint)d) _grp[n++] = _recs[i];
                if (n == 0) continue;
                if (!HasOverlap(n)) { Emit(_dsts[d], 0, n); continue; }
                // Rare: two writes to the same words in one flush. Dispatch in submission order, a new dispatch
                // whenever a record overlaps one already in the current dispatch, so the later write wins.
                OverlapSplitsTotal++;
                int seg = 0;
                for (int i = 1; i <= n; i++)
                {
                    bool cut = i == n;
                    if (!cut)
                        for (int j = seg; j < i; j++)
                            if (_grp[i].y < _grp[j].y + _grp[j].z && _grp[j].y < _grp[i].y + _grp[i].z) { cut = true; break; }
                    if (cut) { Emit(_dsts[d], seg, i - seg); seg = i; }
                }
            }
            IssueGuard(_cur);
            _cur.Pending = true;
            FlushesTotal++;
            _recN = 0;
            ClearDsts();
        }

        private void ClearDsts() { for (int d = 0; d < _dstN; d++) _dsts[d] = null; _dstN = 0; }

        private bool HasOverlap(int n)
        {
            if (n < 2) return false;
            if (n > 65535) return true;   // cannot key it: treat as overlapping (sequential dispatches stay exact)
            for (int i = 0; i < n; i++) _keys[i] = ((long)_grp[i].y << 16) | (uint)i;   // y < 2^31, i < 2^16 per flush
            Array.Sort(_keys, 0, n);
            long end = -1;
            for (int i = 0; i < n; i++)
            {
                int k = (int)(_keys[i] & 0xFFFF);
                long s = _grp[k].y;
                if (s < end) return true;
                end = math.max(end, s + _grp[k].z);
            }
            return false;
        }

        private void Emit(object dst, int start, int count)
        {
            Dispatch(_cur, dst, _grp, start, count, _cur.RecHead);
            _cur.RecHead += count;
            DispatchesTotal++;
        }

        private bool Free(Slot s)
        {
            if (!s.Pending || DebugIgnoreGuards) return true;
            if (GuardDone(s)) { s.Pending = false; return true; }
            return false;
        }

        private void Rotate(int count, int pieces)
        {
            Flush();
            ReapTemps();
            RotationsTotal++;
            Slot next = FindRingSlot(count, pieces);
            if (next == null)
            {
                long need = math.max(count, _slotUints) * 4L + math.max(pieces, _recCap) * 16L;
                if (TempBytesOutstanding + need > TEMP_LIMIT_BYTES) { WaitAll(); WaitsTotal++; ReapTemps(); next = FindRingSlot(count, pieces); }
            }
            if (next == null)
            {
                next = new Slot { Temp = true, Cap = math.max(count, _slotUints), RecCap = math.max(pieces, _recCap) };
                AllocSlot(next);
                TempsTotal++;
                TempBytesOutstanding += next.Cap * 4L + next.RecCap * 16L;
                TempSlotUintsMax = math.max(TempSlotUintsMax, next.Cap);
                TempBytesPeak = math.max(TempBytesPeak, TempBytesOutstanding);
            }
            if (_cur.Temp) _temps.Add(_cur);
            _cur = next;
            _cur.Head = 0; _cur.RecHead = 0;
        }

        private Slot FindRingSlot(int count, int pieces)
        {
            if (count > _slotUints || pieces > _recCap) return null;
            for (int i = 1; i <= _ring.Count; i++)
            {
                int j = (_curRing + i) % _ring.Count;
                var s = _ring[j];
                if (s == _cur || !Free(s)) continue;
                _curRing = j;
                return s;
            }
            return null;
        }

        /// Free temporaries whose copies the GPU has run.
        public void ReapTemps()
        {
            for (int i = _temps.Count - 1; i >= 0; i--)
            {
                var t = _temps[i];
                if (!Free(t)) continue;
                FreeSlot(t);
                TempBytesOutstanding -= t.Cap * 4L + t.RecCap * 16L;
                _temps.RemoveAt(i);
            }
        }

        /// Once per frame: flush a pass that forgot to, poll the guards, free finished temporaries.
        public void Tick()
        {
            int f = Frame;
            if (_recN > 0 && f != _pendingFrame) Flush();
            foreach (var s in _ring) Free(s);
            if (_cur.Temp) Free(_cur);
            ReapTemps();
        }

        /// Block until the GPU has run every copy staged so far (bursts over TEMP_LIMIT, SetEnabled(false), checks).
        public void WaitAll()
        {
            foreach (var s in _ring) if (s.Pending) { WaitGuard(s); s.Pending = false; }
            foreach (var t in _temps) if (t.Pending) { WaitGuard(t); t.Pending = false; }
            if (_cur.Pending) { WaitGuard(_cur); _cur.Pending = false; }
        }

        public string Report()
            => $"slots {_ring.Count} x {_slotUints * 4 / 1024} KiB (+{_recCap * 16 / 1024} KiB records) = {RingBytes / 1024} KiB  cap {(CapBytes > 0 ? $"{CapBytes / 1024} KiB" : "none")}  " +
               $"staged {BytesStagedTotal / (1024.0 * 1024.0):F1} MiB in {WritesTotal} writes over {FramesWithWrites} frames (max {FrameBytesMax / 1024} KiB/frame)  " +
               $"flushes {FlushesTotal} dispatches {DispatchesTotal} overlap-splits {OverlapSplitsTotal} rotations {RotationsTotal}  " +
               $"temps {TempsTotal} (peak {TempBytesPeak / 1024} KiB, now {TempBytesOutstanding / 1024})  GPU waits {WaitsTotal}";
    }

    // ======================================================================= GPU
    public sealed class GpuUploadRing : UploadRing
    {
        private readonly ComputeShader _cs;
        private readonly int _k;
        private readonly GraphicsBuffer _ticket;
        private NativeArray<uint> _locked;
        private int _lockedStart;
        private int _ticketN;
        private static readonly int IdSrc = Shader.PropertyToID("_Src"), IdRec = Shader.PropertyToID("_Rec"), IdDst = Shader.PropertyToID("_Dst"),
                                    IdTicket = Shader.PropertyToID("_Ticket"), IdRecBase = Shader.PropertyToID("_RecBase"), IdTicketValue = Shader.PropertyToID("_TicketValue");

        public static GpuUploadRing TryCreate()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) return null;
            var cs = Resources.Load<ComputeShader>("UploadScatter");
            if (cs == null || !cs.HasKernel("CSScatter")) return null;
            return new GpuUploadRing(cs);
        }

        private GpuUploadRing(ComputeShader cs) : base(SLOTS, SLOT_UINTS, SLOT_RECORDS)
        {
            _cs = cs;
            _k = cs.FindKernel("CSScatter");
            _ticket = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
            _ticket.SetData(new uint[1]);
            AllocRing();
        }

        protected override int Frame => Time.frameCount;

        protected override void AllocSlot(Slot s)
        {
            s.Staging = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, s.Cap, 4);
            s.Recs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, s.RecCap, 16);
        }

        protected override void FreeSlot(Slot s)
        {
            ((GraphicsBuffer)s.Staging)?.Release();
            ((GraphicsBuffer)s.Recs)?.Release();
            s.Staging = s.Recs = null;
        }

        protected override void Open(Slot s, int start, int count)
        {
            _locked = ((GraphicsBuffer)s.Staging).LockBufferForWrite<uint>(start, count);
            _lockedStart = start;
        }

        protected override void Close(Slot s, int start, int written)
            => ((GraphicsBuffer)s.Staging).UnlockBufferAfterWrite<uint>(written);

        protected override void CopyIn(Slot s, int at, uint[] src, int srcStart, int count)
            => NativeArray<uint>.Copy(src, srcStart, _locked, at - _lockedStart, count);

        protected override void CopyIn(Slot s, int at, int[] src, int srcStart, int count)
            => NativeArray<int>.Copy(src, srcStart, _locked.Reinterpret<int>(), at - _lockedStart, count);

        protected override void CopyIn(Slot s, int at, NativeArray<uint> src, int srcStart, int count)
            => NativeArray<uint>.Copy(src, srcStart, _locked, at - _lockedStart, count);

        protected override void Dispatch(Slot s, object dst, uint4[] recs, int start, int count, int recAt)
        {
            var rb = (GraphicsBuffer)s.Recs;
            var na = rb.LockBufferForWrite<uint4>(recAt, count);
            NativeArray<uint4>.Copy(recs, start, na, 0, count);
            rb.UnlockBufferAfterWrite<uint4>(count);
            _cs.SetBuffer(_k, IdSrc, (GraphicsBuffer)s.Staging);
            _cs.SetBuffer(_k, IdRec, rb);
            _cs.SetBuffer(_k, IdDst, (GraphicsBuffer)dst);
            _cs.SetBuffer(_k, IdTicket, _ticket);
            _cs.SetInt(IdRecBase, recAt);
            _cs.SetInt(IdTicketValue, ++_ticketN);
            _cs.Dispatch(_k, count, 1, 1);
        }

        protected override void IssueGuard(Slot s) => s.Req = AsyncGPUReadback.Request(_ticket, 4, 0);

        protected override bool GuardDone(Slot s) => s.Req.done;

        protected override void WaitGuard(Slot s)
        {
            if (!s.Req.done) s.Req.WaitForCompletion();
        }
    }

    // ======================================================================= CPU (tests)
    /// A CPU stand-in for the GPU: dispatches queue up and run LATENCY frames later, reading the staging slot
    /// as it is THEN (so reusing a slot before its copies ran would corrupt the result, as on the GPU), and it
    /// counts any dispatch whose records overlap (the GPU would race on those).
    public sealed class CpuUploadRing : UploadRing
    {
        public int FrameNow;
        public int Latency = 2;
        public int OverlapViolations;
        public int SlotsAllocated, SlotsFreed;
        private uint[] _openSlot;
        private struct Job { public int Frame; public uint[] Staging; public uint[] Dst; public uint4[] Recs; }
        private readonly Queue<Job> _jobs = new Queue<Job>();
        private long _jobsIssued, _jobsRun;

        public CpuUploadRing(int slots, int slotUints, int recCap) : base(slots, slotUints, recCap) { AllocRing(); }

        protected override int Frame => FrameNow;

        protected override void AllocSlot(Slot s) { s.Staging = new uint[s.Cap]; s.Recs = null; SlotsAllocated++; }
        protected override void FreeSlot(Slot s) { s.Staging = null; SlotsFreed++; }
        protected override void Open(Slot s, int start, int count) => _openSlot = (uint[])s.Staging;
        protected override void Close(Slot s, int start, int written) => _openSlot = null;
        protected override void CopyIn(Slot s, int at, uint[] src, int srcStart, int count) => Array.Copy(src, srcStart, _openSlot, at, count);
        protected override void CopyIn(Slot s, int at, int[] src, int srcStart, int count)
        {
            for (int i = 0; i < count; i++) _openSlot[at + i] = unchecked((uint)src[srcStart + i]);
        }
        protected override void CopyIn(Slot s, int at, NativeArray<uint> src, int srcStart, int count)
            => NativeArray<uint>.Copy(src, srcStart, _openSlot, at, count);

        protected override void Dispatch(Slot s, object dst, uint4[] recs, int start, int count, int recAt)
        {
            var r = new uint4[count];
            Array.Copy(recs, start, r, 0, count);
            for (int i = 0; i < count; i++)
                for (int j = 0; j < i; j++)
                    if (r[i].y < r[j].y + r[j].z && r[j].y < r[i].y + r[i].z) { OverlapViolations++; i = count; break; }
            _jobs.Enqueue(new Job { Frame = FrameNow, Staging = (uint[])s.Staging, Dst = (uint[])dst, Recs = r });
            _jobsIssued++;
        }

        protected override void IssueGuard(Slot s) => s.Fence = _jobsIssued;
        protected override bool GuardDone(Slot s) => _jobsRun >= s.Fence;
        protected override void WaitGuard(Slot s) { while (_jobsRun < s.Fence) RunOne(); }

        /// Advance one frame: the "GPU" runs every job at least Latency frames old.
        public void EndFrame()
        {
            while (_jobs.Count > 0 && _jobs.Peek().Frame <= FrameNow - Latency) RunOne();
            FrameNow++;
        }

        private void RunOne()
        {
            var j = _jobs.Dequeue();
            foreach (var r in j.Recs) Array.Copy(j.Staging, (int)r.x, j.Dst, (int)r.y, (int)r.z);
            _jobsRun++;
        }

        public void Drain() { Flush(); while (_jobs.Count > 0) RunOne(); }
    }
}
