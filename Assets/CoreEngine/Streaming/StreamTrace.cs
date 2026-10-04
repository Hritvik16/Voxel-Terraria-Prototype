// Assets/CoreEngine/Streaming/StreamTrace.cs
//
// PER-CHUNK STREAMING TIMELINE (task 10, 2026-10-02: the v3 streaming baseline
// every surface-rebuild step is compared against). OPT-IN and OBSERVE-ONLY:
// with Enabled false (the default) every hook is one static bool test and
// nothing is recorded; with it true the hooks write timestamps into this
// class's own dictionaries and never touch the store, the pools, the mirror
// or any decision the streamer makes. MAIN THREAD ONLY, except the worker's
// own timings, which ride in its LoadCompletion like the chunk does.
//
// A chunk's life, in Stopwatch ticks and frame numbers:
//   request  -- first entered the pending set (RebuildPendingSet)
//   dispatch -- Unloaded -> Loading, handed to a worker (DispatchLoads)
//   worker   -- GenerateBaseline ms, cascade downsample ms, finish timestamp
//   admit    -- main thread took the completion (DrainCompletions); admitMs is
//               that chunk's own main-thread cost, split into transfer
//               (scratch -> shared pool copy) and cascade (tier 1/2 writes)
//   upload   -- the tier-0 clipmap pass that wrote it (TerrainClipmap epoch);
//               the first frame it can be DRAWN, at whatever tier its
//               distance selects
//   near     -- first frame the camera was within LODConfig.TIER_OUTER_RANGE_M[0]
//               (64 m, full detail) of the chunk's box. Tracked only until
//               upload: a chunk that is near BEFORE it is uploaded is a LATE
//               arrival -- a hole or a coarser tier inside the full-detail ring.
//
// A request that leaves the pending set without being dispatched (the camera
// moved on), a completion discarded on arrival, and an admitted chunk evicted
// before its upload are dropped from the timeline and counted.
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace VoxelEngine.Streaming
{
    public static class StreamTrace
    {
        public static bool Enabled;

        public struct Entry
        {
            public int3 coord;
            public long tRequest, tDispatch, tWorkerDone, tAdmit, tUpload, tNear;
            public int fRequest, fDispatch, fAdmit, fUpload, fNear;
            public double genMs, downMs, admitMs, transferMs, cascadeMs;
            // Task 25: the worker thread's own CPU time for the same two calls
            // (-1 when the platform clock is unavailable), and how many GC
            // collections (gen 0 count) started while the worker ran them.
            public double genCpuMs, downCpuMs;
            public int gcDuringWork;
            public bool uniform;
            public float distRequestM, distUploadM;
            internal int stamp;
            internal byte stage; // 0 requested, 1 dispatched, 2 admitted
        }

        private static readonly Dictionary<int3, Entry> _open = new Dictionary<int3, Entry>();
        private static readonly List<int3> _scratch = new List<int3>();
        /// Chunks that reached the GPU mirror, in upload order.
        public static readonly List<Entry> Done = new List<Entry>();
        public const int DONE_CAP = 200000;
        public static int DroppedUnrequested, DroppedDiscarded, DroppedEvicted;
        private static int _stamp;
        private static float3 _cam;

        public static int OpenCount => _open.Count;

        public static void Reset()
        {
            _open.Clear(); Done.Clear();
            DroppedUnrequested = DroppedDiscarded = DroppedEvicted = 0;
        }

        public static long Now => System.Diagnostics.Stopwatch.GetTimestamp();

        // ---- task 25 (2026-10-04): per-thread CPU time, diagnostic only ----
        // wall >> CPU on a worker = it was not running (descheduled or stopped
        // by the GC); CPU itself rising = it ran slower. macOS only; elsewhere
        // (or if the call fails once) ThreadCpuNs returns -1.
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("/usr/lib/libSystem.B.dylib")]
        private static extern ulong clock_gettime_nsec_np(int clockId);
        private const int CLOCK_THREAD_CPUTIME_ID = 16; // <time.h>, macOS
        private static bool _cpuClockOk = true;
        public static long ThreadCpuNs()
        {
            if (!_cpuClockOk) return -1;
            try { return (long)clock_gettime_nsec_np(CLOCK_THREAD_CPUTIME_ID); }
            catch (System.Exception) { _cpuClockOk = false; return -1; }
        }
#else
        public static long ThreadCpuNs() => -1;
#endif
        public static double CpuMs(long ns0, long ns1) => (ns0 < 0 || ns1 < 0) ? -1 : (ns1 - ns0) / 1e6;
        public static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// Nearest point of the chunk's box to the camera, metres (3D).
        public static float DistanceM(int3 c, float3 camM)
        {
            float3 lo = (float3)c * 12.8f, hi = lo + 12.8f;
            float3 d = math.max(math.max(lo - camM, camM - hi), 0f);
            return math.length(d);
        }

        internal static void BeginRebuild(float3 camM) { _stamp++; _cam = camM; }

        internal static void OnRequest(int3 c, int frame)
        {
            if (_open.TryGetValue(c, out var e)) { e.stamp = _stamp; _open[c] = e; return; }
            _open[c] = new Entry
            {
                coord = c, tRequest = Now, fRequest = frame, stamp = _stamp, stage = 0,
                distRequestM = DistanceM(c, _cam), fNear = -1, fUpload = -1,
            };
        }

        /// Requested-only entries the rebuild did not re-add are no longer wanted.
        internal static void EndRebuild()
        {
            _scratch.Clear();
            foreach (var kv in _open)
                if (kv.Value.stage == 0 && kv.Value.stamp != _stamp) _scratch.Add(kv.Key);
            foreach (var c in _scratch) { _open.Remove(c); DroppedUnrequested++; }
        }

        internal static void OnDispatch(int3 c, int frame)
        {
            if (!_open.TryGetValue(c, out var e)) return; // requested before tracing began
            e.tDispatch = Now; e.fDispatch = frame; e.stage = 1;
            _open[c] = e;
        }

        internal static void OnAdmit(int3 c, int frame, long tAdmit, double genMs, double downMs, long tWorkerDone,
                                     double admitMs, double transferMs, double cascadeMs, bool uniform,
                                     double genCpuMs, double downCpuMs, int gcDuringWork)
        {
            if (!_open.TryGetValue(c, out var e) || e.stage != 1) return;
            e.tAdmit = tAdmit; e.fAdmit = frame; e.stage = 2;
            e.genMs = genMs; e.downMs = downMs; e.tWorkerDone = tWorkerDone;
            e.genCpuMs = genCpuMs; e.downCpuMs = downCpuMs; e.gcDuringWork = gcDuringWork;
            e.admitMs = admitMs; e.transferMs = transferMs; e.cascadeMs = cascadeMs; e.uniform = uniform;
            _open[c] = e;
        }

        internal static void OnDrop(int3 c, bool evicted)
        {
            if (!_open.Remove(c)) return;
            if (evicted) DroppedEvicted++; else DroppedDiscarded++;
        }

        /// After the frame's clipmap pass: move uploaded chunks to Done, and
        /// stamp "near" on chunks still missing from the mirror.
        internal static void AfterUpload(TerrainClipmap clip, float3 camM, int frame)
        {
            long now = Now;
            long epoch = clip.UploadEpoch;
            float nearM = LODConfig.TIER_OUTER_RANGE_M[0];
            _updates.Clear();
            foreach (var kv in _open)
            {
                var e = kv.Value;
                float d = DistanceM(e.coord, camM);
                bool changed = false;
                if (e.fNear < 0 && d <= nearM) { e.fNear = frame; e.tNear = now; changed = true; }
                if (e.stage == 2 && clip.LastUploadEpoch(e.coord) == epoch)
                {
                    e.fUpload = frame; e.tUpload = now; e.distUploadM = d;
                    if (Done.Count < DONE_CAP) Done.Add(e);
                    changed = true;
                }
                if (changed) _updates.Add(e); // applied below: no mutation during enumeration
            }
            foreach (var e in _updates)
            {
                if (e.fUpload >= 0) _open.Remove(e.coord);
                else _open[e.coord] = e;
            }
        }
        private static readonly List<Entry> _updates = new List<Entry>();

        /// Chunks wanted but not yet on the GPU, and how many of them are inside
        /// the full-detail ring right now.
        public static void MissingNow(float3 camM, out int open, out int insideNear)
        {
            open = _open.Count; insideNear = 0;
            float nearM = LODConfig.TIER_OUTER_RANGE_M[0];
            foreach (var kv in _open)
                if (DistanceM(kv.Key, camM) <= nearM) insideNear++;
        }
    }
}
