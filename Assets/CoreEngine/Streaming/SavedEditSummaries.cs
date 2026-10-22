// Assets/CoreEngine/Streaming/SavedEditSummaries.cs
//
// Amendment 8.15 §5 / §10 row 6 (engine row E2, task 108): SAVED edit summaries,
// save file D.5 `summary/`, one file per edited chunk, beside the frozen D.1
// deltas, D.4 `ops.log` and `world.state`. Task 93a's live summaries were
// dropped on eviction, so a build vanished from the shells once its chunks left
// the CPU; now the summary is written when the chunk's D.1 is (eviction,
// autosave, shutdown flush) and the shells and the far band draw it from disk.
//
// BUILD: the same Burst pass as 93a (EditSummaryOps.Snapshot/Run/Extract over
// the live chunk), for the bricks D.1 records (MaskFromDelta: the bricks that
// differ from the baseline, generator + ops <= S). No second downsampler.
// Task 113 (off-thread, 93a's pattern): SaveDelta, right after D.1's rename,
// only SNAPSHOTS the chunk into a scratch this store owns and schedules the vote
// (a worker writes only that scratch). Pump (StreamManager.Update, every frame)
// finishes completed jobs on the main thread: Extract, encode, `.tmp` + rename,
// index. The bytes are those of the synchronous build (`Synchronous` = 108's
// inline path). Up to EngineConfig.SAVED_SUMMARY_SCRATCH builds are in flight;
// one more completes the oldest first (SyncFallbacks).
//
// PENDING (a D.1 written, its D.5 not yet): Has() is true, TryGet answers
// NotLoaded (no disk read), Warm skips it and the far band keeps the coord's
// previous spans, so readers keep the look they have until the build lands
// (ShellStreamer keeps the dropped live summary as a stand-in). The snapshot is
// a copy, so an eviction, a pool slot reissued, a reload or new edits before the
// job finishes cannot change it. A newer D.1 of the same chunk SUPERSEDES it
// (only the newest is written); a D.1 deleted (coalesced) or written with
// summaries off DROPS it. CompleteAll (FlushAllDirty, Dispose) finishes every
// pending build and writes it; a crash before that leaves the old or no D.5:
// StaleDelta / none, as below.
//
// D.5 LAYOUT `summary/{cx}_{cy}_{cz}.sum` (little-endian, formatVersion 1):
//   header  {uint magic 'ESUM', ushort formatVersion, uint seed, int3 chunk,
//            uint deltaCrc, uint opsBefore (S), ushort brickCount}        32 B
//   bricks  brickCount x ushort brick index (LocalBrickIndex), ascending
//   level 1 per brick, in that order: byte tag {0: 64 raw voxels follow
//           (y, z, x order), 1: uniform, 1 material byte follows}
//   levels 4-7  one byte per edited group (EditSummaryOps' groups, ascending)
//   trailer {uint crc32} over everything before it (WorldMeta.Crc32), written
//           `.tmp` + atomic rename (DeltaCodec.WriteAtomic).
// Levels 2-3 are NOT stored: a level-2/3 voxel lies inside one edited brick, so
// it is the vote of that brick's level-1 voxels (exactly what BuildJob does).
// Levels 4-7 span unedited bricks too, so they are stored (<= 4 B per brick).
// Every level of a loaded summary therefore equals the live 93a summary built
// from the same chunk with the same mask, byte for byte.
//
// STALE (8.15 §12, invariant 11): the summary carries the CRC of the D.1 it was
// built with. On load it must equal D.1's CRC on disk (the trailer, read once;
// in-session the CRC this store last saw written) -- else it is ignored and the
// shells show the generator. A crash between D.1 and D.5 leaves exactly that.
// A corrupt or truncated file is rejected (counted, never thrown).
//
// RAM: an index of edited chunks (Entry, ~48 B each with the dictionary slot),
// their far-band spans (2 sampled columns per edited brick column, FarSpan
// 32 B), an LRU of loaded summaries (EngineConfig.SAVED_SUMMARY_LRU_BYTES) and
// one 4.3 MB build scratch, allocated on the first save with edits. RamBytes.
//
// THREADING (single-writer): main thread only, except the vote job, which
// reads and writes only its own scratch (main thread touches a scratch only
// after Complete). Shell workers read summaries through ShellStreamer's
// immutable snapshot; far-band jobs get their own copy of the spans they need
// (FarField). No locks.
//
// Coordinates are integer shifts and masks only (§6.2).
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    public enum SummaryRejectReason : byte
    {
        None = 0,
        FileMissing,
        FileUnreadable,
        TooShort,
        CrcMismatch,
        BadHeader,      // magic, version, seed or chunk do not match
        BadPayload,     // brick list or tags malformed, or the length is wrong
        DeltaMissing,   // no D.1 beside it: nothing to summarise
        StaleDelta,     // D.1's CRC is not the one it was built with
    }

    /// One far-band column of a saved summary: the column's edited bricks of one
    /// contiguous run, as the 2.5D band needs them (FarField.ApplySpan). Engine voxels.
    public struct FarSpan
    {
        public const int NO_TOP = int.MinValue;
        public int x, z;          // the fine column (a far-band texel centre column)
        public int ylo, yhi;      // fine voxels the summary knows (the run of edited bricks)
        public int top;           // first air above the highest non-air voxel, or NO_TOP (all air)
        public int fluidBase;     // the top voxel is Water/Lava: the lowest voxel of that run; else top
        public int opsBefore;     // S: ops <= S come before it, ops > S after
        public byte mat;          // the top voxel's material
    }

    public static class SummaryCodec
    {
        public const uint MAGIC = 0x4D555345u;   // "ESUM"
        public const ushort FORMAT_VERSION = 1;
        public const int HEADER_BYTES = 4 + 2 + 4 + 12 + 4 + 4 + 2;   // 32
        public const int TRAILER_BYTES = 4;
        private const int L1 = 64;   // level-1 voxels per brick (4^3)

        public static byte[] Encode(EditSummary s, uint seed, uint deltaCrc)
        {
            var bricks = Bricks(s.Mask);
            using var ms = new MemoryStream(HEADER_BYTES + bricks.Count * 67 + 64);
            using var w = new BinaryWriter(ms);
            w.Write(MAGIC); w.Write(FORMAT_VERSION); w.Write(seed);
            w.Write(s.Coord.x); w.Write(s.Coord.y); w.Write(s.Coord.z);
            w.Write(deltaCrc); w.Write((uint)math.max(0, s.OpsBefore)); w.Write((ushort)bricks.Count);
            foreach (int b in bricks) w.Write((ushort)b);
            var v1 = s.Values[1];
            for (int r = 0; r < bricks.Count; r++)
            {
                int o = r * L1;
                bool uni = true;
                for (int i = 1; i < L1 && uni; i++) uni = v1[o + i] == v1[o];
                if (uni) { w.Write((byte)1); w.Write(v1[o]); }
                else { w.Write((byte)0); w.Write(v1, o, L1); }
            }
            for (int k = 4; k <= EditSummaryOps.MAX_LEVEL; k++) w.Write(s.Values[k]);
            w.Flush();
            var body = ms.ToArray();
            uint crc = WorldMeta.Crc32(body, 0, body.Length);
            var outp = new byte[body.Length + TRAILER_BYTES];
            Buffer.BlockCopy(body, 0, outp, 0, body.Length);
            outp[body.Length] = (byte)crc; outp[body.Length + 1] = (byte)(crc >> 8);
            outp[body.Length + 2] = (byte)(crc >> 16); outp[body.Length + 3] = (byte)(crc >> 24);
            return outp;
        }

        /// Decodes a D.5 file of chunk `coord`. Never throws on bad bytes.
        public static bool TryDecode(byte[] bytes, uint seed, int3 coord, out EditSummary s, out uint deltaCrc, out SummaryRejectReason reason)
        {
            s = null; deltaCrc = 0; reason = SummaryRejectReason.None;
            if (bytes == null || bytes.Length < HEADER_BYTES + TRAILER_BYTES) { reason = SummaryRejectReason.TooShort; return false; }
            int bodyLen = bytes.Length - TRAILER_BYTES;
            uint stored = (uint)(bytes[bodyLen] | (bytes[bodyLen + 1] << 8) | (bytes[bodyLen + 2] << 16) | (bytes[bodyLen + 3] << 24));
            if (WorldMeta.Crc32(bytes, 0, bodyLen) != stored) { reason = SummaryRejectReason.CrcMismatch; return false; }
            int p = 0;
            uint U32() { uint v = (uint)(bytes[p] | (bytes[p + 1] << 8) | (bytes[p + 2] << 16) | (bytes[p + 3] << 24)); p += 4; return v; }
            int U16() { int v = bytes[p] | (bytes[p + 1] << 8); p += 2; return v; }
            if (U32() != MAGIC || U16() != FORMAT_VERSION || U32() != seed) { reason = SummaryRejectReason.BadHeader; return false; }
            var c = new int3((int)U32(), (int)U32(), (int)U32());
            if (!c.Equals(coord)) { reason = SummaryRejectReason.BadHeader; return false; }
            deltaCrc = U32();
            uint opsBefore = U32();
            int n = U16();
            if (n == 0 || n > EngineConfig.BRICKS_PER_CHUNK || p + n * 2 > bodyLen) { reason = SummaryRejectReason.BadPayload; return false; }
            var mask = new ulong[EngineConfig.BRICKS_PER_CHUNK >> 6];
            int prev = -1;
            for (int i = 0; i < n; i++)
            {
                int b = U16();
                if (b <= prev || b >= EngineConfig.BRICKS_PER_CHUNK) { reason = SummaryRejectReason.BadPayload; return false; }
                mask[b >> 6] |= 1UL << (b & 63);
                prev = b;
            }
            var values = new byte[EditSummaryOps.MAX_LEVEL + 1][];
            var groups = new ulong[EditSummaryOps.MAX_LEVEL + 1][];
            for (int k = 1; k <= EditSummaryOps.MAX_LEVEL; k++) groups[k] = EditSummaryOps.GroupBits(mask, k);
            var v1 = values[1] = new byte[n * L1];
            for (int r = 0; r < n; r++)
            {
                if (p >= bodyLen) { reason = SummaryRejectReason.BadPayload; return false; }
                byte tag = bytes[p++];
                if (tag == 1)
                {
                    if (p >= bodyLen) { reason = SummaryRejectReason.BadPayload; return false; }
                    byte m = bytes[p++];
                    for (int i = 0; i < L1; i++) v1[r * L1 + i] = m;
                }
                else if (tag == 0)
                {
                    if (p + L1 > bodyLen) { reason = SummaryRejectReason.BadPayload; return false; }
                    Buffer.BlockCopy(bytes, p, v1, r * L1, L1);
                    p += L1;
                }
                else { reason = SummaryRejectReason.BadPayload; return false; }
            }
            // Levels 2-3: the vote of the brick's own level-1 voxels (header).
            var v2 = values[2] = new byte[n * 8];
            var v3 = values[3] = new byte[n];
            for (int r = 0; r < n; r++)
            {
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                for (int x = 0; x < 2; x++)
                {
                    ulong pk = 0; int i = 0;
                    for (int dy = 0; dy < 2; dy++)
                    for (int dz = 0; dz < 2; dz++)
                    for (int dx = 0; dx < 2; dx++)
                        pk |= (ulong)v1[r * L1 + (((y << 1) + dy) << 4) + (((z << 1) + dz) << 2) + (x << 1) + dx] << (i++ << 3);
                    v2[r * 8 + (y << 2) + (z << 1) + x] = EditSummaryOps.Vote(pk);
                }
                ulong q = 0;
                for (int i = 0; i < 8; i++) q |= (ulong)v2[r * 8 + i] << (i << 3);
                v3[r] = EditSummaryOps.Vote(q);
            }
            for (int k = 4; k <= EditSummaryOps.MAX_LEVEL; k++)
            {
                int g = 0;
                foreach (ulong w in groups[k]) g += math.countbits(w);
                if (p + g > bodyLen) { reason = SummaryRejectReason.BadPayload; return false; }
                values[k] = new byte[g];
                Buffer.BlockCopy(bytes, p, values[k], 0, g);
                p += g;
            }
            if (p != bodyLen) { reason = SummaryRejectReason.BadPayload; return false; }
            s = new EditSummary(coord, mask, groups, values, (int)math.min(opsBefore, (uint)int.MaxValue));
            return true;
        }

        private static List<int> Bricks(ulong[] mask)
        {
            var l = new List<int>();
            for (int w = 0; w < mask.Length; w++)
            {
                ulong m = mask[w];
                while (m != 0) { l.Add((w << 6) | math.tzcnt(m)); m &= m - 1; }
            }
            return l;
        }

        /// The far-band spans of `s` (FarSpan): for every edited brick column, the two
        /// fine columns the band ever samples there (texel centres: x & 7 == 4 at
        /// level 0, x & 7 == 0 above, same in z), one span per run of edited bricks.
        public static FarSpan[] FarSpans(EditSummary s)
        {
            var outp = new List<FarSpan>();
            var bricks = Bricks(s.Mask);
            var rank = new Dictionary<int, int>(bricks.Count);
            for (int r = 0; r < bricks.Count; r++) rank[bricks[r]] = r;
            var v1 = s.Values[1];
            int3 o = s.Coord << 7;
            var cols = new HashSet<int>();
            foreach (int b in bricks) cols.Add(b & 0xF0F);   // bx | (bz << 8)
            foreach (int col in cols)
            {
                int bx = col & 15, bz = col >> 8;
                for (int by = 0; by < 16; by++)
                {
                    int bi = (bz << 8) | (by << 4) | bx;
                    if (!rank.ContainsKey(bi) || (by > 0 && rank.ContainsKey(bi - 16))) continue;   // a run starts here
                    int byHi = by;
                    while (byHi < 15 && rank.ContainsKey(bi + ((byHi + 1 - by) << 4))) byHi++;
                    for (int sc = 0; sc < 2; sc++)
                    {
                        int fx = (bx << 3) + (sc == 0 ? 4 : 0), fz = (bz << 3) + (sc == 0 ? 4 : 0);   // fine, chunk-local
                        int lx = (fx >> 1) & 3, lz = (fz >> 1) & 3;   // level-1, brick-local
                        var span = new FarSpan
                        {
                            x = o.x + fx, z = o.z + fz,
                            ylo = o.y + (by << 3), yhi = o.y + (byHi << 3) + 7,
                            top = FarSpan.NO_TOP, fluidBase = FarSpan.NO_TOP, opsBefore = s.OpsBefore,
                        };
                        // Top-down through the run's level-1 voxels.
                        for (int yb = byHi; yb >= by && span.top == FarSpan.NO_TOP; yb--)
                        {
                            int r = rank[(bz << 8) | (yb << 4) | bx];
                            for (int ly = 3; ly >= 0; ly--)
                            {
                                byte m = v1[r * 64 + (ly << 4) + (lz << 2) + lx];
                                if (m == Materials.Air) continue;
                                int y1 = (yb << 2) + ly;   // chunk-local level-1 y
                                span.top = o.y + (y1 << 1) + 2; span.mat = m; span.fluidBase = span.top;
                                if (m == Materials.Water || m == Materials.Lava)
                                {
                                    int yy = y1;
                                    while (yy - 1 >= (by << 2) && v1[rank[(bz << 8) | (((yy - 1) >> 2) << 4) | bx] * 64 + (((yy - 1) & 3) << 4) + (lz << 2) + lx] == m) yy--;
                                    span.fluidBase = o.y + (yy << 1);
                                }
                                break;
                            }
                        }
                        outp.Add(span);
                    }
                }
            }
            return outp.ToArray();
        }
    }

    /// The save's D.5 summaries: index, LRU, far spans. Main thread only.
    public sealed class SavedEditSummaries : IDisposable
    {
        public const string DIR_NAME = "summary";
        public const string EXT = ".sum";

        public readonly string DeltaDirectory;
        public readonly string SummaryDirectory;
        private readonly uint _seed;

        /// Saves write summaries (false: `-savesummaries 0`; D.1 is unchanged either way,
        /// and a summary left from before goes stale on the next D.1 write).
        public bool WriteEnabled = true;
        /// Summaries the LRU may hold before dropping the least recently used.
        public long LruBudgetBytes = EngineConfig.SAVED_SUMMARY_LRU_BYTES;

        // ---- counters ----
        public long Written, Deleted, Loaded, Rejected, WriteFailures, BytesWritten, DeltaBytesSeen;
        /// Off-thread builds (task 113): scheduled; finished early because every scratch
        /// was busy; discarded (superseded by a newer D.1, or its D.1 deleted).
        public long JobsScheduled, SyncFallbacks, Superseded;
        /// Main-thread build time (Snapshot + Extract; + the vote when Synchronous) and write time.
        public double BuildMsTotal, WriteMsTotal;
        /// True = 108's inline build (tests' reference); false = the vote on a worker.
        public bool Synchronous;
        public SummaryRejectReason LastReject { get; private set; }

        /// Bumped by every change a reader must see (write, delete, a load that
        /// validates or rejects an entry).
        public int Version { get; private set; }

        private sealed class Entry
        {
            public uint DeltaCrc;
            public bool CrcKnown;
            public byte State;                 // 0 unknown, 1 valid, 2 rejected
            public SummaryRejectReason Reject;
            public FarSpan[] Far;
            public int ChangedAt;
        }
        private readonly Dictionary<int3, Entry> _index = new Dictionary<int3, Entry>();
        private readonly Dictionary<int3, LinkedListNode<EditSummary>> _lruMap = new Dictionary<int3, LinkedListNode<EditSummary>>();
        private readonly LinkedList<EditSummary> _lru = new LinkedList<EditSummary>();
        private long _lruBytes;
        private readonly Dictionary<int3, int> _removedAt = new Dictionary<int3, int>();
        private sealed class Pending
        {
            public int3 Coord;
            public ulong[] Mask;
            public uint Crc;
            public int OpsBefore;
            public EditSummaryOps.Scratch S;
        }
        private readonly List<Pending> _inFlight = new List<Pending>();          // schedule order
        private readonly Dictionary<int3, Pending> _pendingBy = new Dictionary<int3, Pending>();   // the newest per chunk
        private readonly Stack<EditSummaryOps.Scratch> _scratchFree = new Stack<EditSummaryOps.Scratch>();
        private readonly List<EditSummaryOps.Scratch> _scratchAll = new List<EditSummaryOps.Scratch>();
        private readonly List<int3> _warm = new List<int3>();
        private bool _maybeUnvalidated = true;   // some entry may have State 0 (Warm's early out)

        private SavedEditSummaries(string deltaDir, uint seed)
        {
            DeltaDirectory = deltaDir; _seed = seed;
            SummaryDirectory = Path.Combine(deltaDir, DIR_NAME);
        }

        /// The summaries of the save in `deltaDir`: indexes `summary/*.sum` (nothing is
        /// read until a reader asks; stale `.tmp` files are removed).
        public static SavedEditSummaries Open(string deltaDir, uint seed)
        {
            var s = new SavedEditSummaries(deltaDir, seed);
            if (!Directory.Exists(s.SummaryDirectory)) return s;
            foreach (var f in Directory.GetFiles(s.SummaryDirectory, "*" + EXT + ".tmp"))
                try { File.Delete(f); } catch (IOException) { }
            foreach (var f in Directory.GetFiles(s.SummaryDirectory, "*" + EXT))
                if (TryParseName(Path.GetFileName(f), out int3 c)) s._index[c] = new Entry();
            return s;
        }

        public static string FileName(int3 c) => $"{c.x}_{c.y}_{c.z}{EXT}";
        public static string PathFor(string deltaDir, int3 c) => Path.Combine(deltaDir, DIR_NAME, FileName(c));

        private static bool TryParseName(string name, out int3 c)
        {
            c = default;
            if (!name.EndsWith(EXT, StringComparison.Ordinal)) return false;
            var parts = name.Substring(0, name.Length - EXT.Length).Split('_');
            return parts.Length == 3 && int.TryParse(parts[0], out c.x) && int.TryParse(parts[1], out c.y) && int.TryParse(parts[2], out c.z);
        }

        /// Clears the save's summaries with its deltas (StreamManager.ClearSaveFiles).
        public static void DeleteAll(string deltaDir)
        {
            string d = Path.Combine(deltaDir, DIR_NAME);
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }

        public int Count => _index.Count;
        /// A summary exists or is being built (pending) for `c`.
        public bool Has(int3 c) => _index.ContainsKey(c) || _pendingBy.ContainsKey(c);
        public void CopyCoords(List<int3> outp)
        {
            outp.Clear(); outp.AddRange(_index.Keys);
            foreach (var c in _pendingBy.Keys) if (!_index.ContainsKey(c)) outp.Add(c);
        }
        /// A D.1 of `c` was written and its summary build has not landed yet.
        public bool IsPending(int3 c) => _pendingBy.ContainsKey(c);
        public int PendingCount => _pendingBy.Count;
        public int InFlight => _inFlight.Count;
        public int ScratchCount => _scratchAll.Count;

        public long LruBytes => _lruBytes;
        public int LruCount => _lru.Count;
        public int ValidCount { get { int n = 0; foreach (var e in _index.Values) if (e.State == 1) n++; return n; } }
        public long FarSpanCount { get { long n = 0; foreach (var e in _index.Values) if (e.Far != null) n += e.Far.Length; return n; } }
        public const int INDEX_ENTRY_BYTES = 48;   // Entry object + dictionary slot, approximate
        public const int FAR_SPAN_BYTES = 32;
        /// Index + far spans + LRU + the build scratches: the store's RAM (approximate).
        public long RamBytes => (long)_index.Count * INDEX_ENTRY_BYTES + FarSpanCount * FAR_SPAN_BYTES + _lruBytes
                                + _scratchAll.Count * ScratchBytes;
        public static long ScratchBytes => EngineConfig.BRICKS_PER_CHUNK * 4L + EngineConfig.BRICKS_PER_CHUNK * (long)EngineConfig.BRICK_BODY_BYTES
                                           + (128L * 128 * 128) + EditSummaryOps.LevelsBytes;

        // =====================================================================
        // Save side (StreamManager.SaveDelta, main thread)
        // =====================================================================

        /// D.1 `deltaBytes` of chunk `coord` was just renamed into place; `live` is the
        /// chunk it encodes, `opsBefore` the op count it holds (S). Snapshots the chunk
        /// and schedules the summary build (Pump finishes it); Synchronous: builds and
        /// writes it now and returns it (else null). Never throws: a failure is counted
        /// and the entry goes stale-checked.
        public EditSummary DeltaWritten(int3 coord, byte[] deltaBytes, Chunk live, BrickDataPool pool, int opsBefore)
        {
            uint crc = EditOpLog.TrailerCrc(deltaBytes);
            DeltaBytesSeen += deltaBytes.Length;
            DropPending(coord);   // a newer D.1: an older build of this chunk is superseded
            if (!WriteEnabled)
            {
                // The old file (if any) no longer matches: mark it for a stale check.
                if (_index.TryGetValue(coord, out var e0)) { e0.DeltaCrc = crc; e0.CrcKnown = true; e0.State = 0; e0.Far = null; DropLru(coord); Changed(coord, e0); }
                return null;
            }
            EditSummaryOps.Scratch scr = null;
            try
            {
                var t = System.Diagnostics.Stopwatch.StartNew();
                var mask = EditSummaryOps.MaskFromDelta(deltaBytes);
                if (mask == null) { DeltaDeleted(coord); return null; }
                scr = TakeScratch();
                var p = new Pending { Coord = coord, Mask = mask, Crc = crc, OpsBefore = opsBefore, S = scr };
                EditSummaryOps.Snapshot(live, pool, scr);
                scr = null;   // owned by p from here (Finish frees it)
                if (Synchronous)
                {
                    EditSummaryOps.Run(p.S);
                    BuildMsTotal += t.Elapsed.TotalMilliseconds;
                    _pendingBy[coord] = p;   // (Finish checks it; never in flight)
                    return Finish(p);
                }
                EditSummaryOps.Schedule(p.S);
                BuildMsTotal += t.Elapsed.TotalMilliseconds;
                JobsScheduled++;
                _pendingBy[coord] = p;
                _inFlight.Add(p);
                Version++;   // readers see the chunk as pending
                return null;
            }
            catch (Exception ex)
            {
                if (scr != null) _scratchFree.Push(scr);
                Failed(coord, crc, ex);
                return null;
            }
        }

        /// MAIN THREAD, every frame: finishes the builds whose jobs completed.
        public int Pump()
        {
            int n = 0;
            for (int i = 0; i < _inFlight.Count; i++)
            {
                if (!_inFlight[i].S.Job.IsCompleted) continue;
                var p = _inFlight[i];
                _inFlight.RemoveAt(i--);
                Finish(p);
                n++;
            }
            return n;
        }

        /// Waits for every pending build and writes it (flush, shutdown).
        public void CompleteAll()
        {
            while (_inFlight.Count > 0)
            {
                var p = _inFlight[0];
                _inFlight.RemoveAt(0);
                Finish(p);
            }
        }

        /// A free scratch; all EngineConfig.SAVED_SUMMARY_SCRATCH busy -> the oldest build finishes now.
        private EditSummaryOps.Scratch TakeScratch()
        {
            if (_scratchFree.Count > 0) return _scratchFree.Pop();
            if (_scratchAll.Count < EngineConfig.SAVED_SUMMARY_SCRATCH || _inFlight.Count == 0)
            {
                var s = new EditSummaryOps.Scratch();
                _scratchAll.Add(s);
                return s;
            }
            var oldest = _inFlight[0];
            _inFlight.RemoveAt(0);
            SyncFallbacks++;
            Finish(oldest);
            return _scratchFree.Pop();
        }

        /// Completes `p`'s job, frees its scratch and, unless superseded or dropped,
        /// extracts, encodes, writes and indexes the summary.
        private EditSummary Finish(Pending p)
        {
            p.S.Job.Complete();
            try
            {
                if (!_pendingBy.TryGetValue(p.Coord, out var cur) || cur != p) { Superseded++; return null; }
                _pendingBy.Remove(p.Coord);
                var t = System.Diagnostics.Stopwatch.StartNew();
                var s = EditSummaryOps.Extract(p.Coord, p.Mask, p.S, p.OpsBefore);
                BuildMsTotal += t.Elapsed.TotalMilliseconds;
                t.Restart();
                var bytes = SummaryCodec.Encode(s, _seed, p.Crc);
                DeltaCodec.WriteAtomic(PathFor(DeltaDirectory, p.Coord), bytes);
                WriteMsTotal += t.Elapsed.TotalMilliseconds;
                Written++; BytesWritten += bytes.Length;
                if (!_index.TryGetValue(p.Coord, out var e)) _index[p.Coord] = e = new Entry();
                _removedAt.Remove(p.Coord);
                e.DeltaCrc = p.Crc; e.CrcKnown = true; e.State = 1; e.Reject = SummaryRejectReason.None;
                e.Far = SummaryCodec.FarSpans(s);
                PutLru(s);
                Changed(p.Coord, e);
                return s;
            }
            catch (Exception ex)
            {
                Failed(p.Coord, p.Crc, ex);
                return null;
            }
            finally { _scratchFree.Push(p.S); }
        }

        private void Failed(int3 coord, uint crc, Exception ex)
        {
            WriteFailures++;
            UnityEngine.Debug.LogError($"[SavedEditSummaries] summary save failed for {coord}: {ex}");
            if (_index.TryGetValue(coord, out var e)) { e.DeltaCrc = crc; e.CrcKnown = true; e.State = 0; e.Far = null; DropLru(coord); Changed(coord, e); }
        }

        /// Forgets `c`'s pending build (its job still runs; Finish discards it).
        private void DropPending(int3 c)
        {
            if (_pendingBy.Remove(c)) Version++;
        }

        /// D.1 of `coord` was deleted (the chunk coalesced back to its baseline).
        public void DeltaDeleted(int3 coord)
        {
            DropPending(coord);
            try
            {
                string p = PathFor(DeltaDirectory, coord);
                if (File.Exists(p)) File.Delete(p);
                if (File.Exists(p + ".tmp")) File.Delete(p + ".tmp");
            }
            catch (IOException) { /* best-effort: a leftover goes stale against the missing D.1 */ }
            if (_index.Remove(coord))
            {
                Deleted++;
                DropLru(coord);
                _removedAt[coord] = ++Version;
            }
        }

        // =====================================================================
        // Read side
        // =====================================================================

        public enum GetResult : byte { Valid, None, NotLoaded }

        /// The summary of `c`: Valid (s set), None (no file, or rejected), or NotLoaded
        /// (a disk read is needed and `loadBudget` is 0). A read costs one of the budget.
        public GetResult TryGet(int3 c, ref int loadBudget, out EditSummary s)
        {
            s = null;
            if (_pendingBy.ContainsKey(c)) return GetResult.NotLoaded;   // the build lands in a frame or two
            if (!_index.TryGetValue(c, out var e) || e.State == 2) return GetResult.None;
            if (_lruMap.TryGetValue(c, out var node) && e.State == 1)
            {
                _lru.Remove(node); _lru.AddFirst(node);
                s = node.Value;
                return GetResult.Valid;
            }
            if (loadBudget <= 0) return GetResult.NotLoaded;
            loadBudget--;
            s = Load(c, e);
            return s != null ? GetResult.Valid : GetResult.None;
        }

        /// Validates up to `maxLoads` entries no reader has asked for yet (their far
        /// spans), oldest-indexed first. Returns the number read.
        public int Warm(int maxLoads)
        {
            if (maxLoads <= 0 || !_maybeUnvalidated) return 0;
            _warm.Clear();
            foreach (var kv in _index)
            {
                if (kv.Value.State != 0 || _pendingBy.ContainsKey(kv.Key)) continue;
                _warm.Add(kv.Key);
                if (_warm.Count >= maxLoads) break;
            }
            foreach (var c in _warm) Load(c, _index[c]);
            if (_warm.Count < maxLoads) _maybeUnvalidated = false;
            return _warm.Count;
        }

        /// Entries not read yet (Warm's backlog).
        public int Unvalidated { get { int n = 0; foreach (var e in _index.Values) if (e.State == 0) n++; return n; } }

        public SummaryRejectReason RejectOf(int3 c) => _index.TryGetValue(c, out var e) ? e.Reject : SummaryRejectReason.FileMissing;

        /// Valid entries' far spans whose chunk column overlaps fine-voxel box [lo, hi] (xz).
        public void FarSpansIn(int2 lo, int2 hi, List<FarSpan> outp)
        {
            foreach (var kv in _index)
            {
                var e = kv.Value;
                if (e.State != 1 || e.Far == null) continue;
                int2 c0 = kv.Key.xz << 7, c1 = c0 + 127;
                if (c1.x < lo.x || c0.x > hi.x || c1.y < lo.y || c0.y > hi.y) continue;
                foreach (var f in e.Far)
                    if (f.x >= lo.x && f.x <= hi.x && f.z >= lo.y && f.z <= hi.y) outp.Add(f);
            }
        }

        /// Chunks whose far look changed after version `since` (written, deleted,
        /// validated, rejected).
        public void ChangedSince(int since, List<int3> outp)
        {
            outp.Clear();
            foreach (var kv in _index) if (kv.Value.ChangedAt > since) outp.Add(kv.Key);
            foreach (var kv in _removedAt) if (kv.Value > since) outp.Add(kv.Key);
        }

        private EditSummary Load(int3 c, Entry e)
        {
            Loaded++;
            SummaryRejectReason why;
            EditSummary s = null;
            byte[] bytes = null;
            string path = PathFor(DeltaDirectory, c);
            try { bytes = File.Exists(path) ? File.ReadAllBytes(path) : null; why = bytes == null ? SummaryRejectReason.FileMissing : SummaryRejectReason.None; }
            catch (Exception) { why = SummaryRejectReason.FileUnreadable; }
            if (why == SummaryRejectReason.None && SummaryCodec.TryDecode(bytes, _seed, c, out s, out uint dcrc, out why))
            {
                if (!e.CrcKnown)
                {
                    if (TryReadDeltaCrc(DeltaCodec.PathFor(DeltaDirectory, c), out uint onDisk)) { e.DeltaCrc = onDisk; e.CrcKnown = true; }
                    else why = SummaryRejectReason.DeltaMissing;
                }
                if (why == SummaryRejectReason.None && e.DeltaCrc != dcrc) why = SummaryRejectReason.StaleDelta;
            }
            if (why != SummaryRejectReason.None)
            {
                Rejected++; LastReject = why;
                e.State = 2; e.Reject = why; e.Far = null;
                DropLru(c);
                Changed(c, e);
                return null;
            }
            bool wasValid = e.State == 1 && e.Far != null;
            e.State = 1; e.Reject = SummaryRejectReason.None;
            if (!wasValid) { e.Far = SummaryCodec.FarSpans(s); Changed(c, e); }
            PutLru(s);
            return s;
        }

        /// D.1's CRC (its trailer) without reading the whole file.
        public static bool TryReadDeltaCrc(string path, out uint crc)
        {
            crc = 0;
            try
            {
                if (!File.Exists(path)) return false;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < DeltaCodec.HEADER_BYTES + DeltaCodec.TRAILER_BYTES) return false;
                fs.Seek(-DeltaCodec.TRAILER_BYTES, SeekOrigin.End);
                var b = new byte[4];
                if (fs.Read(b, 0, 4) != 4) return false;
                crc = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
                return true;
            }
            catch (Exception) { return false; }
        }

        private void Changed(int3 c, Entry e)
        {
            e.ChangedAt = ++Version;
            if (e.State == 0) _maybeUnvalidated = true;
        }

        private void PutLru(EditSummary s)
        {
            DropLru(s.Coord);
            _lruMap[s.Coord] = _lru.AddFirst(s);
            _lruBytes += s.Bytes;
            while (_lruBytes > LruBudgetBytes && _lru.Count > 1)
            {
                var last = _lru.Last;
                _lru.RemoveLast();
                _lruMap.Remove(last.Value.Coord);
                _lruBytes -= last.Value.Bytes;
            }
        }

        private void DropLru(int3 c)
        {
            if (!_lruMap.TryGetValue(c, out var node)) return;
            _lru.Remove(node); _lruMap.Remove(c);
            _lruBytes -= node.Value.Bytes;
        }

        /// Writes every pending build (shutdown after FlushAllDirty), then frees the scratches.
        public void Dispose()
        {
            CompleteAll();
            foreach (var s in _scratchAll) s.Dispose();
            _scratchAll.Clear(); _scratchFree.Clear();
        }
    }
}
