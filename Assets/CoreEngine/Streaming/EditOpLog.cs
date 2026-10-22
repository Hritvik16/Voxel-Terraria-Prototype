// Assets/CoreEngine/Streaming/EditOpLog.cs
//
// Amendment 8.14 rows 8-10 (engine step 13a, task 81): the EDIT-OP LOG, save
// file D.4 `ops.log`, beside the frozen D.1 deltas. A far edit (outside the
// loaded chunks, where the frozen IEditService.SetVoxel refuses) is stored as
// an operation and applied when its area generates -- in GenerateChunk's
// output at 0.1 m and in GenerateShellChunk at each shell's own voxel size
// (EditOpApply). The log does not depend on the quality preset.
//
// D.4 LAYOUT (little-endian, formatVersion 1):
//   header  {uint seed, ushort formatVersion, uint opCount}          10 B
//   ops     opCount x {uint seq, byte kind, int3 centreVoxel,
//                      ushort size, byte material}                   20 B each
//   side    {uint sideCount} + sideCount x
//           {int3 chunk, uint seqAtSave, uint deltaCrc}               4 + 20 B each
//   trailer {uint crc32} over everything before it (WorldMeta.Crc32, the one
//           CRC in the engine), written `.tmp` + atomic rename exactly as D.1
//           (DeltaCodec.WriteAtomic).
// seq is 1-based and contiguous: op i has seq i + 1, so "ops with seq <= S"
// is the first S ops.
//
// PRECEDENCE (row 10, owner Q3 (a): by order of time). A chunk loads as
// generation + ops with seq <= S, then its D.1 delta, then ops with seq > S.
// S = the op count when the chunk's delta was written, kept in the SIDE table
// here and never in D.1 (D.1 stays byte for byte). The side entry is written
// BEFORE the delta (SaveDelta: log, then delta), so a crash between the two
// leaves an entry whose deltaCrc does not match the delta on disk -> "all ops,
// then D.1" (row 10's fallback). A delta with NO side entry was written while
// the log had no ops (every old save, and every delta saved before the first
// far edit): S = 0 is then exact, all ops come after it.
// Old saves have no ops.log: zero ops, so they load unchanged.
//
// THREADING (single-writer, CLAUDE.md): every mutator is main-thread only
// (asserted). Workers read Snapshot, an immutable object the main thread
// replaces wholesale after each change -- no lock anywhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.Mathematics;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    /// D.4 op kinds. Carve vs fill is the material (Materials.Air carves).
    public enum EditOpKind : byte
    {
        /// Every voxel with dx^2 + dy^2 + dz^2 <= size^2 (FluidGpuSimulation.SphereCovers,
        /// the rule EditService.SetSphere uses).
        Sphere = 1,
        /// Every voxel with |dx|, |dy|, |dz| <= size (an inclusive cube of side 2 size + 1).
        Box = 2,
        /// RESERVED for D.3 prefab placement (row 9). No D.3 `.vx` format exists in this
        /// engine yet, so the value is reserved and a log containing it is rejected.
        PrefabReserved = 3,
    }

    public struct EditOp
    {
        public uint seq;
        public EditOpKind kind;
        public int3 centreVoxel;
        public ushort size;
        public byte material;

        public EditOp(EditOpKind kind, int3 centreVoxel, ushort size, byte material)
        { seq = 0; this.kind = kind; this.centreVoxel = centreVoxel; this.size = size; this.material = material; }

        public override string ToString() => $"#{seq} {kind} c{centreVoxel} s{size} m{material}";
    }

    public enum OpLogRejectReason
    {
        None,
        FileMissing,
        FileUnreadable,
        TooShort,
        CrcMismatch,
        SeedMismatch,
        UnknownVersion,
        LengthInconsistent,
        BadSequence,
        UnknownKind,
    }

    /// An immutable view of the log for workers: the ops and the side table as
    /// of one moment. Never mutated after construction.
    public sealed class EditOpSnapshot
    {
        public struct SideEntry { public uint seqAtSave, deltaCrc; }

        public static readonly EditOpSnapshot Empty = new EditOpSnapshot(Array.Empty<EditOp>(), new Dictionary<int3, SideEntry>());

        public readonly EditOp[] Ops;
        private readonly Dictionary<int3, SideEntry> _side;
        public int Count => Ops.Length;

        internal EditOpSnapshot(EditOp[] ops, Dictionary<int3, SideEntry> side) { Ops = ops; _side = side; }

        /// Row 10's S for a chunk whose delta on disk has trailer `deltaCrc`: how many
        /// ops go BEFORE the delta. No entry -> 0 (the delta predates every op); an
        /// entry whose CRC does not match (a crash between the two writes) -> Count
        /// ("all ops, then D.1").
        public int OpsBeforeDelta(int3 chunk, uint deltaCrc)
        {
            if (!_side.TryGetValue(chunk, out var e)) return 0;
            if (e.deltaCrc != deltaCrc) return Count;
            return (int)math.min(e.seqAtSave, (uint)Count);
        }

        public bool TryGetSide(int3 chunk, out SideEntry e) => _side.TryGetValue(chunk, out e);
        public int SideCount => _side.Count;
    }

    public sealed class EditOpLog
    {
        public const string FILE_NAME = "ops.log";
        public const ushort FORMAT_VERSION = 1;
        public const int HEADER_BYTES = sizeof(uint) + sizeof(ushort) + sizeof(uint);                              // 10
        public const int OP_BYTES = sizeof(uint) + sizeof(byte) + 3 * sizeof(int) + sizeof(ushort) + sizeof(byte);  // 20
        public const int SIDE_BYTES = 3 * sizeof(int) + sizeof(uint) + sizeof(uint);                               // 20
        public const int TRAILER_BYTES = sizeof(uint);
        /// Largest op size accepted (4096 voxels = 409.6 m): keeps size^2 sums in int, as SphereCovers computes them.
        public const ushort MAX_SIZE = 4096;

        public uint Seed { get; }
        /// Full path of ops.log, or null for an in-memory log (tests).
        public string FilePath { get; }
        /// Why an existing ops.log was not loaded (None if it loaded or did not exist).
        /// A rejected file is moved aside to `ops.log.rejected`, never overwritten.
        public OpLogRejectReason LoadReject { get; private set; }
        public long Writes { get; private set; }

        private readonly List<EditOp> _ops = new List<EditOp>();
        private readonly Dictionary<int3, EditOpSnapshot.SideEntry> _side = new Dictionary<int3, EditOpSnapshot.SideEntry>();
        private readonly int _mainThread;
        private EditOpSnapshot _snapshot = EditOpSnapshot.Empty;

        public int Count => _ops.Count;
        /// The current immutable snapshot. Safe from any thread.
        public EditOpSnapshot Snapshot => Volatile.Read(ref _snapshot);

        private EditOpLog(uint seed, string filePath)
        {
            Seed = seed; FilePath = filePath;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        /// An empty log that is never written (tests).
        public static EditOpLog InMemory(uint seed) => new EditOpLog(seed, null);

        /// The log of the save in `directory`: loads `ops.log` if present. A file
        /// that fails validation is moved aside and the log starts empty.
        public static EditOpLog Open(string directory, uint seed)
        {
            var log = new EditOpLog(seed, Path.Combine(directory, FILE_NAME));
            if (!File.Exists(log.FilePath)) return log;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(log.FilePath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { log.LoadReject = OpLogRejectReason.FileUnreadable; return log; }

            if (TryDecode(bytes, seed, out var ops, out var side, out var reason))
            {
                log._ops.AddRange(ops);
                foreach (var kv in side) log._side[kv.Key] = kv.Value;
                log.Publish();
            }
            else
            {
                log.LoadReject = reason;
                UnityEngine.Debug.LogWarning($"[EditOpLog] {log.FilePath} REJECTED ({reason}); starting with no ops, file moved to {FILE_NAME}.rejected");
                try { File.Copy(log.FilePath, log.FilePath + ".rejected", true); File.Delete(log.FilePath); }
                catch (IOException) { }
            }
            return log;
        }

        /// Appends an op (its seq is assigned here and returned) and persists the log.
        /// MAIN THREAD ONLY.
        public uint Append(EditOp op)
        {
            RequireMainThread();
            if (op.kind != EditOpKind.Sphere && op.kind != EditOpKind.Box)
                throw new ArgumentException($"op kind {op.kind} is not supported (D.3 prefab placement is reserved, no D.3 format exists)");
            if (op.size > MAX_SIZE) throw new ArgumentOutOfRangeException(nameof(op), $"size {op.size} > {MAX_SIZE}");
            op.seq = (uint)_ops.Count + 1;
            _ops.Add(op);
            Publish();
            Save();
            return op.seq;
        }

        /// Called by SaveDelta BEFORE the delta file is written: records S = Count and
        /// the delta's trailer CRC, and persists. With no ops nothing is recorded or
        /// written (S = 0 is implied). MAIN THREAD ONLY.
        public void NoteDeltaAboutToBeWritten(int3 chunk, byte[] deltaBytes)
        {
            RequireMainThread();
            if (_ops.Count == 0) { if (_side.Remove(chunk)) Publish(); return; }
            _side[chunk] = new EditOpSnapshot.SideEntry { seqAtSave = (uint)_ops.Count, deltaCrc = TrailerCrc(deltaBytes) };
            Publish();
            Save();
        }

        /// The chunk's delta was deleted (coalesced back to baseline). The entry is
        /// dropped in memory; the file catches up at its next write (a stale entry
        /// without a delta file is never read). MAIN THREAD ONLY.
        public void NoteDeltaDeleted(int3 chunk)
        {
            RequireMainThread();
            if (_side.Remove(chunk)) Publish();
        }

        public static uint TrailerCrc(byte[] fileBytes)
        {
            int n = fileBytes.Length;
            return (uint)(fileBytes[n - 4] | (fileBytes[n - 3] << 8) | (fileBytes[n - 2] << 16) | (fileBytes[n - 1] << 24));
        }

        // =====================================================================
        // Codec
        // =====================================================================

        public byte[] Encode() => Encode(Seed, _ops, _side);

        public static byte[] Encode(uint seed, IReadOnlyList<EditOp> ops, IReadOnlyDictionary<int3, EditOpSnapshot.SideEntry> side)
        {
            using var ms = new MemoryStream(HEADER_BYTES + ops.Count * OP_BYTES + 4 + side.Count * SIDE_BYTES + TRAILER_BYTES);
            using var w = new BinaryWriter(ms);
            w.Write(seed); w.Write(FORMAT_VERSION); w.Write((uint)ops.Count);
            for (int i = 0; i < ops.Count; i++)
            {
                var o = ops[i];
                w.Write(o.seq); w.Write((byte)o.kind);
                w.Write(o.centreVoxel.x); w.Write(o.centreVoxel.y); w.Write(o.centreVoxel.z);
                w.Write(o.size); w.Write(o.material);
            }
            // Sorted, so the same log state always encodes to the same bytes.
            var keys = new List<int3>(side.Keys);
            keys.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
            w.Write((uint)keys.Count);
            foreach (var k in keys)
            {
                var e = side[k];
                w.Write(k.x); w.Write(k.y); w.Write(k.z); w.Write(e.seqAtSave); w.Write(e.deltaCrc);
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

        /// Total: validates CRC, seed, version, lengths, seq order and kinds before
        /// returning anything; never throws on bad input.
        public static bool TryDecode(byte[] bytes, uint expectedSeed,
            out List<EditOp> ops, out Dictionary<int3, EditOpSnapshot.SideEntry> side, out OpLogRejectReason reason)
        {
            ops = null; side = null; reason = OpLogRejectReason.None;
            if (bytes == null || bytes.Length < HEADER_BYTES + 4 + TRAILER_BYTES) { reason = OpLogRejectReason.TooShort; return false; }
            int bodyLen = bytes.Length - TRAILER_BYTES;
            if (WorldMeta.Crc32(bytes, 0, bodyLen) != TrailerCrc(bytes)) { reason = OpLogRejectReason.CrcMismatch; return false; }

            int p = 0;
            uint seed = U32(bytes, ref p);
            ushort version = (ushort)(bytes[p] | (bytes[p + 1] << 8)); p += 2;
            uint opCount = U32(bytes, ref p);
            if (seed != expectedSeed) { reason = OpLogRejectReason.SeedMismatch; return false; }
            if (version != FORMAT_VERSION) { reason = OpLogRejectReason.UnknownVersion; return false; }
            if ((long)opCount * OP_BYTES > bodyLen - HEADER_BYTES - 4) { reason = OpLogRejectReason.LengthInconsistent; return false; }

            var list = new List<EditOp>((int)opCount);
            for (uint i = 0; i < opCount; i++)
            {
                var o = new EditOp { seq = U32(bytes, ref p), kind = (EditOpKind)bytes[p++] };
                o.centreVoxel = new int3(I32(bytes, ref p), I32(bytes, ref p), I32(bytes, ref p));
                o.size = (ushort)(bytes[p] | (bytes[p + 1] << 8)); p += 2;
                o.material = bytes[p++];
                if (o.seq != i + 1) { reason = OpLogRejectReason.BadSequence; return false; }
                if ((o.kind != EditOpKind.Sphere && o.kind != EditOpKind.Box) || o.size > MAX_SIZE) { reason = OpLogRejectReason.UnknownKind; return false; }
                list.Add(o);
            }
            uint sideCount = U32(bytes, ref p);
            if ((long)p + (long)sideCount * SIDE_BYTES != bodyLen) { reason = OpLogRejectReason.LengthInconsistent; return false; }
            var table = new Dictionary<int3, EditOpSnapshot.SideEntry>((int)sideCount);
            for (uint i = 0; i < sideCount; i++)
            {
                var c = new int3(I32(bytes, ref p), I32(bytes, ref p), I32(bytes, ref p));
                table[c] = new EditOpSnapshot.SideEntry { seqAtSave = U32(bytes, ref p), deltaCrc = U32(bytes, ref p) };
            }
            ops = list; side = table;
            return true;
        }

        private static uint U32(byte[] b, ref int p)
        {
            uint v = (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
            p += 4;
            return v;
        }

        private static int I32(byte[] b, ref int p) => (int)U32(b, ref p);

        // =====================================================================

        private void Save()
        {
            if (FilePath == null) return;
            DeltaCodec.WriteAtomic(FilePath, Encode());
            Writes++;
        }

        private void Publish()
        {
            var snap = new EditOpSnapshot(_ops.ToArray(), new Dictionary<int3, EditOpSnapshot.SideEntry>(_side));
            Volatile.Write(ref _snapshot, snap);
        }

        private void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread)
                throw new InvalidOperationException("EditOpLog is main-thread only (single-writer rule); workers read Snapshot.");
        }
    }
}
