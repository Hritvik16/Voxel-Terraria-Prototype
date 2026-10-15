// Assets/CoreEngine/Mirror/ShellStreamer.EditSummaries.cs
//
// Task 93a (Amendment 8.15 §5 "Live", engine row E1): resident edited chunks
// redraw into the shells that cover them.
//
// WHY: shells hold generator + D.4 ops (tasks 81/82); a block-by-block edit
// (D.1 / SetVoxel) was drawn only by tier 0, i.e. inside shell 0's hand-over
// (69 m on High), although the CPU keeps it live to 128 m. Now:
//   1. ChunkStore.SetVoxel marks the edited brick (Chunk.editMask) and tells
//      this streamer once per chunk (IChunkEditSink); a chunk reloaded with a
//      D.1 arrives with its records' bricks marked (StreamManager's worker).
//      Ops are not marked (EditMarksMuted): they are in the shells already.
//   2. An edited chunk some shell k >= 1 wants a covering chunk of (c >> k) is
//      re-summarised at most once per EDIT_SUMMARY_MIN_INTERVAL_S, at most
//      EDIT_SUMMARIES_PER_FRAME per frame (more are REFUSED and counted, and
//      retried next frame): the main thread copies its bricks into a scratch
//      it owns, a Burst job votes on a job worker (EditSummary.cs), the main
//      thread cuts out the summary when the job is done.
//   3. A new summary is published in a new immutable snapshot, and every shell
//      chunk covering it that a shell holds, uploads or generates is queued for
//      a redraw (the op path: generated again -- now generator + ops + the
//      summaries, then the fill -- and re-stored gap-free).
//   4. EVICTION KEEPS NOTHING (8.15 §5's saved summaries are task E2): the
//      summary leaves the snapshot, nothing is redrawn for it; a shell chunk
//      drawn with it keeps it until it is generated again (it leaves the box,
//      or another edit or op redraws it), then the edit is gone from the far
//      shells until the chunk is resident again.
// Shells off: nothing attaches the sink, so SetVoxel marks nothing.
//
// THREADING: everything here is main thread; the job reads only its scratch.
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;

namespace VoxelEngine.Mirror
{
    public sealed partial class ShellStreamer : IChunkEditSink
    {
        /// False (`-editsummaries 0`): edits are still tracked but nothing is
        /// summarised -- the pre-93a picture. Turning it on summarises them.
        public bool SummariesEnabled = true;
        /// Seconds, for the once-per-interval rule; tests drive it. Null = real time.
        public Func<double> SummaryClock;

        /// Edit notices from the store, summaries built (and published), builds refused
        /// by the per-frame cap or the scratch count, summaries dropped on eviction,
        /// shell chunks queued for a redraw because of a summary.
        public long SummaryNotices, SummariesBuilt, SummariesRefused, SummariesDropped, SummaryRedrawsQueued;
        public double SummaryBuildMsMain;   // main-thread snapshot + extract time, summed
        public int SummariesHeld => _sumSnap.Count;
        public int EditedChunksTracked => _sum.Count;
        public EditSummarySnapshot Summaries => _sumSnap;
        public long SummaryBytes { get { long b = 0; foreach (var s in _sumSnap.All) b += s.Bytes; return b; } }

        private ChunkStore _editStore;
        private sealed class SumEntry
        {
            public Chunk Chunk;
            public bool Stale, Building;
            public double LastBuild = double.NegativeInfinity;
            public EditSummary Summary;
        }
        private sealed class SumBuild
        {
            public EditSummaryOps.Scratch S;
            public int3 Coord;
            public Chunk Chunk;
            public ulong[] Mask;
        }
        private readonly HashSet<int3> _editNotices = new HashSet<int3>();
        private readonly Dictionary<int3, SumEntry> _sum = new Dictionary<int3, SumEntry>();
        private EditSummarySnapshot _sumSnap = EditSummarySnapshot.Empty;
        private int _sumVersion;
        private readonly List<SumBuild> _builds = new List<SumBuild>();
        private readonly Stack<EditSummaryOps.Scratch> _sumFree = new Stack<EditSummaryOps.Scratch>();
        private readonly List<EditSummaryOps.Scratch> _sumAll = new List<EditSummaryOps.Scratch>();
        private readonly List<int3> _sumGone = new List<int3>(), _sumNew = new List<int3>();
        private bool _sumWaiting;
        private readonly System.Diagnostics.Stopwatch _sumWatch = System.Diagnostics.Stopwatch.StartNew();

        private bool SummariesBusy => _builds.Count > 0 || _sumWaiting;
        private double SumNow => SummaryClock != null ? SummaryClock() : _sumWatch.Elapsed.TotalSeconds;

        /// Start tracking `store`'s edits (main thread, once). Chunks already
        /// resident with marks are picked up too.
        public void AttachEdits(ChunkStore store)
        {
            if (_editStore != null) throw new InvalidOperationException("ShellStreamer.AttachEdits: already attached");
            _editStore = store ?? throw new ArgumentNullException(nameof(store));
            store.EditSink = this;
            foreach (var ch in store.ResidentChunks())
                if (ch.editMask != null) { ch.editPending = true; ChunkEdited(ch.coord); }
        }

        public void ChunkEdited(int3 chunkCoord)
        {
            _editNotices.Add(chunkCoord);
            SummaryNotices++;
        }

        /// Does some shell k >= 1 want the chunk covering 0.1 m chunk `c`?
        public bool SummaryCovered(int3 c)
        {
            for (int k = FirstShell; k <= LastShell; k++)
                if (_ccValid[k] && Wanted(k, c >> k)) return true;
            return false;
        }

        /// Update's step between the boxes moving and the op redraws (header EDIT SUMMARIES).
        private void UpdateSummaries()
        {
            if (_editStore == null) return;
            bool changed = false;
            _sumNew.Clear();

            // 1. Finished builds -> summaries.
            for (int i = 0; i < _builds.Count; i++)
            {
                var b = _builds[i];
                if (!b.S.Job.IsCompleted) continue;
                b.S.Job.Complete();
                _builds.RemoveAt(i--);
                if (_sum.TryGetValue(b.Coord, out var e) && e.Chunk == b.Chunk)
                {
                    e.Building = false;
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    e.Summary = EditSummaryOps.Extract(b.Coord, b.Mask, b.S);
                    SummaryBuildMsMain += t.Elapsed.TotalMilliseconds;
                    SummariesBuilt++;
                    changed = true;
                    _sumNew.Add(b.Coord);
                }
                _sumFree.Push(b.S);
            }

            // 2. Edit notices.
            foreach (var c in _editNotices)
            {
                var ch = _editStore.GetChunk(c);
                if (ch == null || ch.editMask == null) continue;
                if (!_sum.TryGetValue(c, out var e) || e.Chunk != ch)
                {
                    if (e != null && e.Summary != null) { changed = true; SummariesDropped++; }
                    _sum[c] = e = new SumEntry { Chunk = ch };
                }
                e.Stale = true;
            }
            _editNotices.Clear();

            // 3. Evicted (or replaced) chunks: keep nothing (header, point 4).
            _sumGone.Clear();
            foreach (var kv in _sum)
                if (_editStore.GetChunk(kv.Key) != kv.Value.Chunk) _sumGone.Add(kv.Key);
            foreach (var c in _sumGone)
            {
                if (_sum[c].Summary != null) { changed = true; SummariesDropped++; }
                _sum.Remove(c);
            }

            // 4. Start builds: edited since the last one, covered, interval passed, under the cap.
            _sumWaiting = false;
            if (SummariesEnabled)
            {
                double now = SumNow;
                int started = 0;
                foreach (var kv in _sum)
                {
                    var e = kv.Value;
                    if (!e.Stale || e.Building || now - e.LastBuild < EngineConfig.EDIT_SUMMARY_MIN_INTERVAL_S || !SummaryCovered(kv.Key)) continue;
                    if (started >= EngineConfig.EDIT_SUMMARIES_PER_FRAME || !TakeSummaryScratch(out var s))
                    {
                        SummariesRefused++; _sumWaiting = true;
                        continue;
                    }
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    e.Chunk.editPending = false;   // the next edit tells us again
                    var mask = (ulong[])e.Chunk.editMask.Clone();
                    EditSummaryOps.Snapshot(e.Chunk, _editStore.BrickPool, s);
                    SummaryBuildMsMain += t.Elapsed.TotalMilliseconds;
                    e.Stale = false; e.Building = true; e.LastBuild = now;
                    started++;
                    if (Synchronous) EditSummaryOps.Run(s); else EditSummaryOps.Schedule(s);
                    _builds.Add(new SumBuild { S = s, Coord = kv.Key, Chunk = e.Chunk, Mask = mask });
                }
            }

            if (!changed) return;
            var all = new List<EditSummary>(_sum.Count);
            foreach (var kv in _sum) if (kv.Value.Summary != null) all.Add(kv.Value.Summary);
            _sumSnap = new EditSummarySnapshot(all.ToArray(), ++_sumVersion);
            foreach (var c in _sumNew) QueueSummaryRedraws(c);
        }

        /// Every shell chunk covering 0.1 m chunk `c` that a shell holds, uploads or
        /// generates -> a redraw (one generating waits in the queue, NextRedraw).
        private void QueueSummaryRedraws(int3 c)
        {
            for (int k = FirstShell; k <= LastShell; k++)
            {
                int3 sc = c >> k;
                if (!_ccValid[k] || !Wanted(k, sc)) continue;
                var st = _stores[k];
                long key = Key(k, sc);
                bool uploading = st.JobActive && st.JobCoord.Equals(sc);
                if (!st.Holds(sc) && !uploading && !_inFlight.Contains(key)) continue;   // missing: its generation reads the new snapshot
                if (_redrawSet.Add(key)) { _redrawQ.Enqueue(key); RedrawsQueued++; SummaryRedrawsQueued++; }
            }
        }

        private bool TakeSummaryScratch(out EditSummaryOps.Scratch s)
        {
            if (_sumFree.Count > 0) { s = _sumFree.Pop(); return true; }
            if (_sumAll.Count < EngineConfig.EDIT_SUMMARY_SCRATCH)
            {
                s = new EditSummaryOps.Scratch();
                _sumAll.Add(s);
                return true;
            }
            s = null;
            return false;
        }

        private void DisposeSummaries()
        {
            foreach (var s in _sumAll) s.Dispose();   // completes any job first
            _sumAll.Clear(); _sumFree.Clear(); _builds.Clear();
            if (_editStore != null && _editStore.EditSink == this) _editStore.EditSink = null;
            _editStore = null;
        }
    }
}
