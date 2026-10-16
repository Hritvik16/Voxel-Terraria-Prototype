// Assets/CoreEngine/Mirror/ShellStreamer.Counters.cs
//
// Task 93b2 (step 1 of owner Q52 (a)): WHERE DO THE SHELL CHUNKS GO while the
// camera walks. Task 93b's counter run generated 2,347 shell chunks in 3 min
// and held 170. Per shell, counted on the main thread, no behaviour change:
//   requested / generated, by Chebyshev ring around the camera chunk at request
//   generated as uniform air / uniform other / mixed (dense bricks summed)
//   stored (uniform at once, or a multi-frame upload completed)
//   refused (ShellStore FULL: pages or bricks), dropped on arrival (no longer
//   wanted / already held), uploads aborted (left the box mid-upload)
//   evicted: left the box, entered the finer shell's hole, ring alias (a
//   Store over another chunk's slot), redraw with no room -- and the mean
//   time those evicted chunks had been stored (s)
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Mirror
{
    public sealed partial class ShellStreamer
    {
        public const int RING_BUCKETS = 8;   // Chebyshev ring 0..6, 7 = beyond

        public sealed class ShellCounters
        {
            public long Requested, Generated, GenUniformAir, GenUniformOther, GenMixed, GenDenseBricks;
            public long Stored, Refused, DroppedArrival, Aborted;
            public long ColumnReused, FollowUps;   // column pass skipped; requests that were a scratch's follow-up
            public long EvictBox, EvictHole, EvictAlias, EvictNoRoom;
            public double ColumnMs, VoxelMs, RestMs;   // worker-side, summed over Generated
            public double EvictedAgeSum;      // seconds stored, summed over EvictBox + EvictHole
            public readonly long[] ReqRing = new long[RING_BUCKETS];
            public readonly long[] StoredRing = new long[RING_BUCKETS];
        }

        private ShellCounters[] _counters;
        private float[][] _storedAt;          // per shell, per ring slot: Time.realtimeSinceStartup when stored

        public ShellCounters Counters(int k)
        {
            EnsureCounters();
            return _counters[k];
        }

        private void EnsureCounters()
        {
            if (_counters != null) return;
            int n = Layout.ShellCount;
            _counters = new ShellCounters[n]; _storedAt = new float[n][];
            for (int k = FirstShell; k <= LastShell; k++)
            {
                _counters[k] = new ShellCounters();
                _storedAt[k] = new float[_stores[k].SlotCount];
            }
        }

        private static float Now => Time.realtimeSinceStartup;

        private int Ring(int k, int3 c) => math.min(RING_BUCKETS - 1, math.cmax(math.abs(c - _cc[k])));

        private void CountRequest(int k, int3 c)
        {
            EnsureCounters();
            var s = _counters[k]; s.Requested++; s.ReqRing[Ring(k, c)]++;
        }

        private void CountGenerated(TerrainGenChunkSource.ShellChunk sc)
        {
            EnsureCounters();
            var s = _counters[sc.Shell]; s.Generated++;
            s.ColumnMs += sc.ColumnMs; s.VoxelMs += sc.VoxelMs; s.RestMs += sc.RestMs;
            if (sc.ColumnReused) s.ColumnReused++;
            if (sc.IsUniformAir) { s.GenUniformAir++; return; }
            ShellStore.UploadBytes(sc, out int dense, out bool uniform, out _);
            if (uniform) s.GenUniformOther++; else { s.GenMixed++; s.GenDenseBricks += dense; }
        }

        private void CountStored(int k, int3 c)
        {
            EnsureCounters();
            var s = _counters[k]; s.Stored++; s.StoredRing[Ring(k, c)]++;
            _storedAt[k][_stores[k].Slot(c)] = Now;
        }

        private void CountEvicted(int k, int slot, bool hole)
        {
            EnsureCounters();
            var s = _counters[k];
            if (hole) s.EvictHole++; else s.EvictBox++;
            s.EvictedAgeSum += Now - _storedAt[k][slot];
        }

        private static double Per(double v, long n) => n > 0 ? v / n : 0;

        /// One line per shell for the capture logs.
        public string CountersReport(string indent = "    ")
        {
            EnsureCounters();
            var sb = new StringBuilder();
            sb.Append(indent).Append("shell: req gen [air/uni/mixed, dense bricks] ms/chunk column+voxel+rest, column reused, follow-ups | stored refused dropArr abort | evict box/hole/alias/noroom, mean age s | held, pool bricks free/cap, pages free/cap | req by ring 0..7 | stored by ring\n");
            for (int k = FirstShell; k <= LastShell; k++)
            {
                var s = _counters[k]; var st = _stores[k]; var L = Layout[k];
                long ev = s.EvictBox + s.EvictHole;
                sb.Append(indent).Append($"s{k}: {s.Requested} {s.Generated} [{s.GenUniformAir}/{s.GenUniformOther}/{s.GenMixed}, {s.GenDenseBricks}] " +
                    $"{Per(s.ColumnMs, s.Generated):F0}+{Per(s.VoxelMs, s.Generated):F0}+{Per(s.RestMs, s.Generated):F0}, {s.ColumnReused}, {s.FollowUps} | " +
                    $"{s.Stored} {s.Refused} {s.DroppedArrival} {s.Aborted} | {s.EvictBox}/{s.EvictHole}/{s.EvictAlias}/{s.EvictNoRoom}, " +
                    $"{(ev > 0 ? s.EvictedAgeSum / ev : 0):F1} | {st.ChunksStored}, {st.FreeBricks}/{L.PoolCap}, {st.FreePages}/{L.PageCap} | " +
                    $"{string.Join(",", s.ReqRing)} | {string.Join(",", s.StoredRing)}\n");
            }
            return sb.ToString();
        }
    }
}
