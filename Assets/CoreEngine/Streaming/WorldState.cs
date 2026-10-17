// Assets/CoreEngine/Streaming/WorldState.cs
//
// Amendment 8.15 row C1 (task 101): the WORLD-STATE file `world.state`, beside
// the frozen D.1 deltas and D.4 `ops.log`. It holds what §4 (catch-up on
// return) and §14 (stage flip) owe the engine, and NOTHING reads it yet:
//   - the persistent WORLD CLOCK (game seconds, saved),
//   - the WORLD STAGE byte (0 until row S1; a generator input from S1 on),
//   - a per-chunk side table: the chunk's LAST-SEEN time (game seconds; stamped
//     when a resident chunk is evicted or saved) and its GENERATION STAGE (the
//     stage it was last generated at, §14's pin hook).
// D.2 `world.meta` stays write-once; D.1's bytes, GetVoxel/SetVoxel, the §4.4
// table and the chunk/brick layout are untouched (side tables only).
//
// LAYOUT (little-endian, formatVersion 1):
//   header  {uint seed, ushort formatVersion, double clockSeconds,
//            byte worldStage, uint chunkCount}                         19 B
//   chunks  chunkCount x {int3 chunk, double lastSeenSeconds,
//                         byte generationStage}                       21 B each
//           sorted by (x, y, z), so the same state encodes to the same bytes;
//           lastSeenSeconds = NEVER (-1) for a chunk generated but not yet seen
//           leaving (never written in practice: SaveNow stamps residents first).
//   trailer {uint crc32} over everything before it (WorldMeta.Crc32), written
//           `.tmp` + atomic rename exactly as D.1 (DeltaCodec.WriteAtomic).
//
// DEFAULTS: no file -> clock 0, stage 0, every chunk last-seen NEVER and
// generation stage 0. A file that fails validation is moved aside to
// `world.state.rejected` with a warning and the same defaults are used.
//
// WHEN IT IS WRITTEN: only when the save is real -- the file already existed,
// or this session wrote a delta or holds edit ops (MarkPersistent; the owner of
// the save decides). Runs that never save terrain (most rigs and capture runs)
// still ADVANCE the clock but never create the file.
//
// THREADING (single-writer, CLAUDE.md): main thread only (asserted).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Mathematics;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    public enum WorldStateRejectReason
    {
        None,
        FileUnreadable,
        TooShort,
        CrcMismatch,
        SeedMismatch,
        UnknownVersion,
        LengthInconsistent,
        BadValue,
    }

    public sealed class WorldState
    {
        public const string FILE_NAME = "world.state";
        public const ushort FORMAT_VERSION = 1;
        public const int HEADER_BYTES = sizeof(uint) + sizeof(ushort) + sizeof(double) + sizeof(byte) + sizeof(uint); // 19
        public const int CHUNK_BYTES = 3 * sizeof(int) + sizeof(double) + sizeof(byte);                               // 21
        public const int TRAILER_BYTES = sizeof(uint);
        /// Last-seen of a chunk never seen leaving NEAR (or not in the table).
        public const double NEVER = -1.0;

        public struct ChunkEntry { public double lastSeenSeconds; public byte generationStage; }

        public uint Seed { get; }
        /// Full path of world.state, or null for an in-memory state (tests).
        public string FilePath { get; }
        /// Why an existing world.state was not loaded (None if it loaded or did not exist).
        public WorldStateRejectReason LoadReject { get; private set; }
        /// The file existed when the save was opened (loaded or rejected).
        public bool HadFile { get; private set; }
        /// The file may be written (see header, WHEN IT IS WRITTEN).
        public bool Persistent { get; private set; }
        public long Writes { get; private set; }

        /// Game seconds since the world was created; advances with game time.
        public double ClockSeconds { get; private set; }
        /// The world stage (§14). 0 until row S1 adds the flip.
        public byte Stage { get; private set; }
        public int ChunkCount => _chunks.Count;

        private readonly Dictionary<int3, ChunkEntry> _chunks = new Dictionary<int3, ChunkEntry>();
        private readonly int _mainThread;

        private WorldState(uint seed, string filePath)
        {
            Seed = seed; FilePath = filePath;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        /// A state that is never written (tests).
        public static WorldState InMemory(uint seed) => new WorldState(seed, null);

        /// The world state of the save in `directory`: loads `world.state` if present.
        /// A file that fails validation is moved aside and the defaults are used.
        public static WorldState Open(string directory, uint seed)
        {
            var s = new WorldState(seed, Path.Combine(directory, FILE_NAME));
            if (!File.Exists(s.FilePath)) return s;
            s.HadFile = true;
            s.Persistent = true;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(s.FilePath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                s.LoadReject = WorldStateRejectReason.FileUnreadable;
                UnityEngine.Debug.LogWarning($"[WorldState] {s.FilePath} UNREADABLE; using clock 0, stage 0, no chunk entries");
                return s;
            }

            if (TryDecode(bytes, seed, out double clock, out byte stage, out var chunks, out var reason))
            {
                s.ClockSeconds = clock; s.Stage = stage;
                foreach (var kv in chunks) s._chunks[kv.Key] = kv.Value;
            }
            else
            {
                s.LoadReject = reason;
                UnityEngine.Debug.LogWarning($"[WorldState] {s.FilePath} REJECTED ({reason}); using clock 0, stage 0, no chunk entries; file moved to {FILE_NAME}.rejected");
                try { File.Copy(s.FilePath, s.FilePath + ".rejected", true); File.Delete(s.FilePath); }
                catch (IOException) { }
            }
            return s;
        }

        /// Advances the world clock by `dtSeconds` of game time (ignored unless finite and > 0).
        public void Advance(double dtSeconds)
        {
            RequireMainThread();
            if (dtSeconds > 0 && !double.IsInfinity(dtSeconds)) ClockSeconds += dtSeconds;
        }

        /// Allows the file to be written from now on (the save is real).
        public void MarkPersistent() => Persistent = true;

        /// A chunk was generated (became resident) at `stage`. Keeps its last-seen.
        public void NoteGenerated(int3 chunk, byte stage)
        {
            RequireMainThread();
            var e = _chunks.TryGetValue(chunk, out var old) ? old : new ChunkEntry { lastSeenSeconds = NEVER };
            e.generationStage = stage;
            _chunks[chunk] = e;
        }

        /// The chunk was resident at the current clock (evicted or saved now).
        public void StampSeen(int3 chunk)
        {
            RequireMainThread();
            var e = _chunks.TryGetValue(chunk, out var old) ? old : default;
            e.lastSeenSeconds = ClockSeconds;
            _chunks[chunk] = e;
        }

        /// Game seconds at which the chunk was last seen resident, or NEVER.
        public double LastSeen(int3 chunk) => _chunks.TryGetValue(chunk, out var e) ? e.lastSeenSeconds : NEVER;

        /// The stage the chunk was last generated at (0 if unknown).
        public byte GenerationStage(int3 chunk) => _chunks.TryGetValue(chunk, out var e) ? e.generationStage : (byte)0;

        /// Writes the file if Persistent (atomic). Returns true if it wrote.
        public bool SaveIfPersistent()
        {
            RequireMainThread();
            if (FilePath == null || !Persistent) return false;
            DeltaCodec.WriteAtomic(FilePath, Encode());
            Writes++;
            return true;
        }

        // =====================================================================
        // Codec
        // =====================================================================

        public byte[] Encode() => Encode(Seed, ClockSeconds, Stage, _chunks);

        public static byte[] Encode(uint seed, double clockSeconds, byte stage, IReadOnlyDictionary<int3, ChunkEntry> chunks)
        {
            using var ms = new MemoryStream(HEADER_BYTES + chunks.Count * CHUNK_BYTES + TRAILER_BYTES);
            using var w = new BinaryWriter(ms);
            w.Write(seed); w.Write(FORMAT_VERSION); w.Write(clockSeconds); w.Write(stage); w.Write((uint)chunks.Count);
            var keys = new List<int3>(chunks.Keys);
            keys.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
            foreach (var k in keys)
            {
                var e = chunks[k];
                w.Write(k.x); w.Write(k.y); w.Write(k.z); w.Write(e.lastSeenSeconds); w.Write(e.generationStage);
            }
            w.Flush();
            byte[] body = ms.ToArray();
            uint crc = WorldMeta.Crc32(body, 0, body.Length);
            byte[] result = new byte[body.Length + TRAILER_BYTES];
            Buffer.BlockCopy(body, 0, result, 0, body.Length);
            result[body.Length + 0] = (byte)(crc & 0xFF);
            result[body.Length + 1] = (byte)((crc >> 8) & 0xFF);
            result[body.Length + 2] = (byte)((crc >> 16) & 0xFF);
            result[body.Length + 3] = (byte)((crc >> 24) & 0xFF);
            return result;
        }

        /// Total: validates length, CRC, seed, version, counts and values before
        /// returning anything; never throws on bad input.
        public static bool TryDecode(byte[] bytes, uint expectedSeed, out double clockSeconds, out byte stage,
            out Dictionary<int3, ChunkEntry> chunks, out WorldStateRejectReason reason)
        {
            clockSeconds = 0; stage = 0; chunks = null; reason = WorldStateRejectReason.None;
            if (bytes == null || bytes.Length < HEADER_BYTES + TRAILER_BYTES) { reason = WorldStateRejectReason.TooShort; return false; }
            int bodyLen = bytes.Length - TRAILER_BYTES;
            if (WorldMeta.Crc32(bytes, 0, bodyLen) != EditOpLog.TrailerCrc(bytes)) { reason = WorldStateRejectReason.CrcMismatch; return false; }

            int p = 0;
            uint seed = U32(bytes, ref p);
            ushort version = (ushort)(bytes[p] | (bytes[p + 1] << 8)); p += 2;
            double clock = F64(bytes, ref p);
            byte st = bytes[p++];
            uint count = U32(bytes, ref p);
            if (seed != expectedSeed) { reason = WorldStateRejectReason.SeedMismatch; return false; }
            if (version != FORMAT_VERSION) { reason = WorldStateRejectReason.UnknownVersion; return false; }
            if ((long)HEADER_BYTES + (long)count * CHUNK_BYTES != bodyLen) { reason = WorldStateRejectReason.LengthInconsistent; return false; }
            if (!IsTime(clock) || clock < 0) { reason = WorldStateRejectReason.BadValue; return false; }

            var table = new Dictionary<int3, ChunkEntry>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var c = new int3(I32(bytes, ref p), I32(bytes, ref p), I32(bytes, ref p));
                var e = new ChunkEntry { lastSeenSeconds = F64(bytes, ref p), generationStage = bytes[p++] };
                if (!IsTime(e.lastSeenSeconds) || (e.lastSeenSeconds < 0 && e.lastSeenSeconds != NEVER)) { reason = WorldStateRejectReason.BadValue; return false; }
                table[c] = e;
            }
            clockSeconds = clock; stage = st; chunks = table;
            return true;
        }

        private static bool IsTime(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static uint U32(byte[] b, ref int p)
        {
            uint v = (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
            p += 4;
            return v;
        }

        private static int I32(byte[] b, ref int p) => (int)U32(b, ref p);

        private static double F64(byte[] b, ref int p)
        {
            ulong lo = U32(b, ref p), hi = U32(b, ref p);
            return BitConverter.Int64BitsToDouble((long)(lo | (hi << 32)));
        }

        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread)
                throw new InvalidOperationException("WorldState is main-thread only (single-writer rule).");
        }
    }
}
