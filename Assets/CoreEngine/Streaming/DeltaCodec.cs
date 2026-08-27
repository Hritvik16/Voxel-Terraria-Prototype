// ==========================================
// Assets/CoreEngine/Streaming/DeltaCodec.cs
//
// Phase 4, file 3 of §13 Phase 4's ordered list: "D.1 encode/decode, CRC,
// atomic .tmp->rename, uniform + dense brick records."
//
// D.1 (Appendix D), quoted exactly, because this layout is FROZEN:
//   file `{cx}_{cy}_{cz}.delta`:
//     header  {int3 chunkCoord, uint seed, ushort formatVersion, ushort recordCount}
//     per deviating brick {ushort brickIndex, byte kind}
//         kind 0 uniform (+1 material byte)
//         kind 1 dense   (+512-byte body)
//     trailer {uint crc32}
//
// This file is on §0.3's "AI may NOT modify without explicit review" list
// (chunk/brick serialization + delta format). Treat as frozen after review:
// a change here makes existing saves unloadable, which is the exact
// undiagnosable-bug class the v8 rebuild exists to avoid.
//
// ---------------------------------------------------------------------------
// THE PERSISTENCE INVARIANT THIS IMPLEMENTS (§4.1)
// ---------------------------------------------------------------------------
// "Procedural generation is immutable law -- terrain is never saved." Only
// player deviations persist. A never-edited chunk costs ZERO BYTES, and the
// ABSENCE of a delta file is itself information: that chunk is bit-exactly its
// baseline. Encode therefore diffs against a fresh baseline regeneration
// (§4.2) and emits records only for bricks that actually deviate.
//
// ---------------------------------------------------------------------------
// FAULT TOLERANCE (§4.2) -- the "never corrupt a save" guarantee
// ---------------------------------------------------------------------------
// CRC32 trailer; write `.tmp` then atomic rename. On load, CRC mismatch OR
// truncation OR any structural inconsistency => DISCARD the delta and
// regenerate the pristine baseline for that one chunk. Worst case: edits lost
// in one 12.8m chunk -- never a corrupted world, never a crash.
//
// TryDecode therefore returns false rather than throwing, for EVERY malformed
// input, and never partially applies. It is written to be total: there is no
// byte sequence that makes it throw, and none that makes it write a partial
// result into the caller's chunk. §13 Phase 4's acceptance test
// ("hex-corrupt a .delta: that chunk regenerates pristine, game continues")
// is a direct exercise of this property, and DeltaCodecTests fuzzes it.
//
// ---------------------------------------------------------------------------
// HEADER SIZE IS COMPUTED AND ASSERTED, NOT ASSUMED
// ---------------------------------------------------------------------------
// PHASE_3_COMPLETION.md §6.5 records the single worst harness bug of Phase 3:
// "world.meta header size 17 vs actual 19 bytes -- every read-back rejected --
// blocked Phase 3a entirely." That was a hand-counted constant disagreeing
// with what the writer actually emitted. This file does not hand-count:
// HEADER_BYTES is derived from the field widths below, and DeltaCodecTests
// asserts it against a real serialized header. Same for RECORD_HEADER_BYTES.
using System;
using System.IO;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    public enum DeltaBrickKind : byte
    {
        Uniform = 0, // followed by 1 material byte
        Dense   = 1, // followed by EngineConfig.BRICK_BODY_BYTES body bytes
    }

    /// Why a decode attempt failed. Reported so the CRC/discard log demanded by
    /// §13 Phase 4 ("CRC log shows the discard") can name the reason instead of
    /// just counting failures -- a bare count cannot distinguish "disk
    /// corruption" from "we wrote it wrong", and those need opposite responses.
    public enum DeltaRejectReason
    {
        None = 0,
        FileMissing,
        FileUnreadable,
        TooShort,
        CrcMismatch,
        UnknownFormatVersion,
        ChunkCoordMismatch,
        SeedMismatch,
        LengthInconsistent,
        BrickIndexOutOfRange,
        UnknownRecordKind,
        BaselineUnavailable,
    }

    public static class DeltaCodec
    {
        // Bump ONLY together with a documented format change. Decode rejects
        // any other value outright (§1.3 lists save-format migration as a
        // v1 non-goal, so "reject" is the whole migration story for now).
        public const ushort FORMAT_VERSION = 1;

        // Derived from the D.1 field widths, never hand-counted:
        //   int3 chunkCoord   3 x 4 = 12
        //   uint seed               =  4
        //   ushort formatVersion    =  2
        //   ushort recordCount      =  2
        public const int HEADER_BYTES = (3 * sizeof(int)) + sizeof(uint) + sizeof(ushort) + sizeof(ushort); // 20
        public const int RECORD_HEADER_BYTES = sizeof(ushort) + sizeof(byte);  // brickIndex + kind = 3
        public const int TRAILER_BYTES = sizeof(uint);                          // crc32
        public const int UNIFORM_PAYLOAD_BYTES = 1;
        public static int DensePayloadBytes => EngineConfig.BRICK_BODY_BYTES;   // 512

        // §11.4: "Worst-case fully-edited chunk delta ~= 4,096 x 512B ~= 2MB".
        // Used to size the encode buffer and to sanity-bound decode before
        // allocating anything from a length field an attacker/corruption
        // controls.
        public static int MaxPlausibleBytes =>
            HEADER_BYTES
            + EngineConfig.BRICKS_PER_CHUNK * (RECORD_HEADER_BYTES + DensePayloadBytes)
            + TRAILER_BYTES;

        // =====================================================================
        // ENCODE
        // =====================================================================

        /// Diffs `live` against `baseline` and serializes only deviating bricks
        /// (§4.2). Returns null when the chunk is bit-exactly its baseline --
        /// the caller MUST then delete any existing .delta rather than writing
        /// an empty one, because §4.1 makes file ABSENCE meaningful.
        ///
        /// `livePool` and `baselinePool` are separate because the baseline is a
        /// fresh regeneration into scratch storage; pool INDICES legitimately
        /// differ between them and are never compared. Only voxel CONTENT is.
        /// (This is the same discipline ChunkContentHash uses, and for the same
        /// reason -- Phase3Bootstrapper's parallel generation made pool indices
        /// run-to-run unstable on purpose.)
        public static byte[] Encode(
            int3 chunkCoord, uint seed,
            Chunk live, BrickDataPool livePool,
            Chunk baseline, BrickDataPool baselinePool)
        {
            if (live == null) throw new ArgumentNullException(nameof(live));
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));

            using var ms = new MemoryStream(4096);
            using var w = new BinaryWriter(ms);

            // Header. recordCount is backfilled once known -- it cannot be
            // predicted without doing the diff, and doing the diff twice would
            // be the kind of "clever" that drifts out of sync with the writer.
            w.Write(chunkCoord.x);
            w.Write(chunkCoord.y);
            w.Write(chunkCoord.z);
            w.Write(seed);
            w.Write(FORMAT_VERSION);
            w.Write((ushort)0); // recordCount placeholder

            int records = 0;
            var liveRaw = livePool.RawData;
            var baseRaw = baselinePool.RawData;
            int bodyBytes = DensePayloadBytes;

            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                ReadBrick(live, i, out bool liveDense, out byte liveMat, out int liveStart);
                ReadBrick(baseline, i, out bool baseDense, out byte baseMat, out int baseStart);

                // --- Case 1: both uniform. Deviates iff the material differs. ---
                if (!liveDense && !baseDense)
                {
                    if (liveMat == baseMat) continue;
                    w.Write((ushort)i);
                    w.Write((byte)DeltaBrickKind.Uniform);
                    w.Write(liveMat);
                    records++;
                    continue;
                }

                // --- Case 2: live is uniform, baseline is dense. ---
                // Deviates unless the baseline body happens to be entirely
                // liveMat (a dug-out then refilled brick the coalescer already
                // collapsed). Checking that is cheap and saves 512 bytes.
                if (!liveDense)
                {
                    if (BodyIsAllOf(baseRaw, baseStart, bodyBytes, liveMat)) continue;
                    w.Write((ushort)i);
                    w.Write((byte)DeltaBrickKind.Uniform);
                    w.Write(liveMat);
                    records++;
                    continue;
                }

                // --- Case 3: live is dense. ---
                // Emit a Dense record unless the live body matches the baseline
                // byte-for-byte. Note a dense live brick whose body is uniform
                // is NOT rewritten as a Uniform record here: the coalescer
                // (§4.5) owns that collapse, and duplicating the decision in
                // two places is how the two drift apart. Encode reports what
                // the chunk IS.
                bool identical = baseDense
                    ? BodiesEqual(liveRaw, liveStart, baseRaw, baseStart, bodyBytes)
                    : BodyIsAllOf(liveRaw, liveStart, bodyBytes, baseMat);
                if (identical) continue;

                w.Write((ushort)i);
                w.Write((byte)DeltaBrickKind.Dense);
                for (int v = 0; v < bodyBytes; v++) w.Write(liveRaw[liveStart + v]);
                records++;
            }

            if (records == 0) return null; // pristine -- see doc comment

            w.Flush();
            byte[] body = ms.ToArray();

            // Backfill recordCount at its known header offset.
            const int RECORD_COUNT_OFFSET = (3 * sizeof(int)) + sizeof(uint) + sizeof(ushort); // 18
            body[RECORD_COUNT_OFFSET + 0] = (byte)(records & 0xFF);
            body[RECORD_COUNT_OFFSET + 1] = (byte)((records >> 8) & 0xFF);

            // CRC32 trailer over everything preceding it. Same reflected
            // poly 0xEDB88320 implementation as world.meta -- deliberately
            // reused rather than reimplemented, so there is exactly one CRC
            // in the engine and no chance of two subtly different ones.
            uint crc = WorldMeta.Crc32(body, 0, body.Length);
            byte[] result = new byte[body.Length + TRAILER_BYTES];
            Buffer.BlockCopy(body, 0, result, 0, body.Length);
            result[body.Length + 0] = (byte)(crc & 0xFF);
            result[body.Length + 1] = (byte)((crc >> 8) & 0xFF);
            result[body.Length + 2] = (byte)((crc >> 16) & 0xFF);
            result[body.Length + 3] = (byte)((crc >> 24) & 0xFF);
            return result;
        }

        // =====================================================================
        // DECODE
        // =====================================================================

        /// Applies a delta onto an ALREADY-REGENERATED baseline chunk, in place.
        ///
        /// Contract, and the reason for its shape: the caller regenerates the
        /// baseline FIRST, then calls this. If decode rejects, the caller
        /// already holds the pristine chunk §4.2 demands -- there is no
        /// recovery path to write, no half-applied state to unwind, and the
        /// failure mode is "edits lost in one chunk", exactly as specified.
        ///
        /// Validation is performed COMPLETELY before any mutation, so a reject
        /// leaves `chunk` byte-identical to the baseline it arrived as.
        public static bool TryDecodeOnto(
            byte[] bytes, int3 expectedCoord, uint expectedSeed,
            Chunk chunk, BrickDataPool pool,
            out DeltaRejectReason reason)
        {
            reason = DeltaRejectReason.None;

            if (chunk == null) { reason = DeltaRejectReason.BaselineUnavailable; return false; }
            if (bytes == null || bytes.Length < HEADER_BYTES + TRAILER_BYTES)
            { reason = DeltaRejectReason.TooShort; return false; }
            if (bytes.Length > MaxPlausibleBytes)
            { reason = DeltaRejectReason.LengthInconsistent; return false; }

            int bodyLen = bytes.Length - TRAILER_BYTES;
            uint storedCrc = (uint)(bytes[bodyLen]
                           | (bytes[bodyLen + 1] << 8)
                           | (bytes[bodyLen + 2] << 16)
                           | (bytes[bodyLen + 3] << 24));
            if (WorldMeta.Crc32(bytes, 0, bodyLen) != storedCrc)
            { reason = DeltaRejectReason.CrcMismatch; return false; }

            // ---- PASS 1: validate structure completely, mutate nothing. ----
            // Every record's offset and payload length is walked here, so PASS 2
            // cannot run off the end regardless of what the header claims.
            int recordCount;
            var offsets = new int[EngineConfig.BRICKS_PER_CHUNK];
            try
            {
                using var ms = new MemoryStream(bytes, 0, bodyLen, writable: false);
                using var r = new BinaryReader(ms);

                int cx = r.ReadInt32(), cy = r.ReadInt32(), cz = r.ReadInt32();
                uint seed = r.ReadUInt32();
                ushort formatVersion = r.ReadUInt16();
                recordCount = r.ReadUInt16();

                if (formatVersion != FORMAT_VERSION)
                { reason = DeltaRejectReason.UnknownFormatVersion; return false; }
                if (cx != expectedCoord.x || cy != expectedCoord.y || cz != expectedCoord.z)
                { reason = DeltaRejectReason.ChunkCoordMismatch; return false; }
                if (seed != expectedSeed)
                { reason = DeltaRejectReason.SeedMismatch; return false; }
                if (recordCount > EngineConfig.BRICKS_PER_CHUNK)
                { reason = DeltaRejectReason.LengthInconsistent; return false; }

                int bodyBytes = DensePayloadBytes;
                for (int n = 0; n < recordCount; n++)
                {
                    if (ms.Position + RECORD_HEADER_BYTES > bodyLen)
                    { reason = DeltaRejectReason.LengthInconsistent; return false; }

                    offsets[n] = (int)ms.Position;
                    ushort brickIndex = r.ReadUInt16();
                    byte kind = r.ReadByte();

                    if (brickIndex >= EngineConfig.BRICKS_PER_CHUNK)
                    { reason = DeltaRejectReason.BrickIndexOutOfRange; return false; }

                    int payload;
                    switch ((DeltaBrickKind)kind)
                    {
                        case DeltaBrickKind.Uniform: payload = UNIFORM_PAYLOAD_BYTES; break;
                        case DeltaBrickKind.Dense:   payload = bodyBytes; break;
                        default: reason = DeltaRejectReason.UnknownRecordKind; return false;
                    }

                    if (ms.Position + payload > bodyLen)
                    { reason = DeltaRejectReason.LengthInconsistent; return false; }
                    ms.Position += payload;
                }

                // Trailing garbage after the last record is a structural
                // inconsistency, not something to tolerate: it means the writer
                // and reader disagree about the format.
                if (ms.Position != bodyLen)
                { reason = DeltaRejectReason.LengthInconsistent; return false; }
            }
            catch (Exception)
            {
                // Total by construction (§4.2): no input throws upward.
                reason = DeltaRejectReason.LengthInconsistent;
                return false;
            }

            // ---- PASS 2: apply. Structure is proven; this cannot fail. ----
            // A delta always lands on a POPULATED chunk: if the baseline came
            // back uniform, expand it first so brick handles exist to write.
            if (chunk.isUniform) ExpandUniformChunk(chunk, pool);

            var raw = pool.RawData;
            int bodyLen2 = DensePayloadBytes;

            for (int n = 0; n < recordCount; n++)
            {
                int o = offsets[n];
                ushort brickIndex = (ushort)(bytes[o] | (bytes[o + 1] << 8));
                var kind = (DeltaBrickKind)bytes[o + 2];
                int payloadAt = o + RECORD_HEADER_BYTES;

                if (kind == DeltaBrickKind.Uniform)
                {
                    // Returning a dense brick to uniform frees its body --
                    // missing this is the "memory creep" failure signature
                    // §13 Phase 4 names ("a pool free path missed on eviction").
                    FreeIfDense(chunk, pool, brickIndex);
                    chunk.bricks[brickIndex].data = bytes[payloadAt];
                }
                else
                {
                    int poolIndex = EnsureDense(chunk, pool, brickIndex);
                    int start = poolIndex * bodyLen2;
                    for (int v = 0; v < bodyLen2; v++) raw[start + v] = bytes[payloadAt + v];
                }
            }

            chunk.dirty = true;      // GPU clipmap must be re-uploaded (§3.7)
            chunk.deltaDirty = false; // just loaded FROM disk; nothing unsaved
            return true;
        }

        // =====================================================================
        // FILE I/O -- atomic .tmp -> rename (§4.2)
        // =====================================================================

        /// `{cx}_{cy}_{cz}.delta` per D.1.
        public static string FileName(int3 chunkCoord) =>
            $"{chunkCoord.x}_{chunkCoord.y}_{chunkCoord.z}.delta";

        public static string PathFor(string deltaDirectory, int3 chunkCoord) =>
            Path.Combine(deltaDirectory, FileName(chunkCoord));

        /// Writes atomically: full write to `.tmp`, flush to disk, then rename.
        /// A force-quit therefore leaves EITHER the old file or the new one,
        /// never a half-written one -- §13 Phase 4's force-quit acceptance test
        /// ("at most the in-flight chunk reverted") depends on exactly this.
        ///
        /// The explicit Flush(true) matters and is not decoration: a rename can
        /// otherwise land in the directory entry before the file's own data
        /// reaches stable storage, which produces a correctly-named file with
        /// garbage inside -- the one failure this whole path exists to prevent.
        public static void WriteAtomic(string path, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(path)) File.Replace(tmp, path, null); // atomic on APFS
            else File.Move(tmp, path);
        }

        /// Deletes a chunk's delta, plus any stale `.tmp` beside it. Called when
        /// a chunk coalesces back to its baseline: §4.1 makes file ABSENCE mean
        /// "bit-exactly baseline", so leaving a stale file would be a lie about
        /// the world's contents, not merely wasted bytes.
        public static void DeleteIfPresent(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                string tmp = path + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch (IOException) { /* best-effort; a stale file is not fatal */ }
        }

        public static bool TryReadFile(string path, out byte[] bytes, out DeltaRejectReason reason)
        {
            bytes = null;
            reason = DeltaRejectReason.None;
            if (!File.Exists(path)) { reason = DeltaRejectReason.FileMissing; return false; }
            try { bytes = File.ReadAllBytes(path); return true; }
            catch (IOException) { reason = DeltaRejectReason.FileUnreadable; return false; }
            catch (UnauthorizedAccessException) { reason = DeltaRejectReason.FileUnreadable; return false; }
        }

        // =====================================================================
        // Brick helpers -- the ONLY place this file touches handle bit layout.
        // Appendix A.3 / Appendix B: [31] dense flag, [30] Volatile,
        // uniform -> material in [7:0], dense -> pool index in [29:0].
        // =====================================================================

        private const uint DENSE_BIT = 0x80000000u;
        private const uint INDEX_MASK = 0x3FFFFFFFu;

        private static void ReadBrick(Chunk chunk, int brickIndex,
            out bool isDense, out byte uniformMaterial, out int bodyStart)
        {
            if (chunk.isUniform || chunk.bricks == null)
            {
                isDense = false;
                uniformMaterial = chunk.uniformMaterial;
                bodyStart = -1;
                return;
            }

            uint data = chunk.bricks[brickIndex].data;
            isDense = (data & DENSE_BIT) != 0;
            uniformMaterial = (byte)(data & 0xFF);
            bodyStart = isDense ? (int)(data & INDEX_MASK) * EngineConfig.BRICK_BODY_BYTES : -1;
        }

        private static void ExpandUniformChunk(Chunk chunk, BrickDataPool pool)
        {
            // Mirrors ChunkStore.SetVoxel's expansion exactly. Allocated
            // directly rather than through ChunkHandleAllocator because decode
            // runs on a chunk the caller already owns and will insert itself;
            // StreamManager hands the array back to the allocator on eviction.
            if (chunk.bricks == null) chunk.bricks = new BrickHandle[EngineConfig.BRICKS_PER_CHUNK];
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
                chunk.bricks[i].data = chunk.uniformMaterial;
            chunk.isUniform = false;
        }

        private static void FreeIfDense(Chunk chunk, BrickDataPool pool, int brickIndex)
        {
            uint data = chunk.bricks[brickIndex].data;
            if ((data & DENSE_BIT) != 0) pool.Free((int)(data & INDEX_MASK));
        }

        private static int EnsureDense(Chunk chunk, BrickDataPool pool, int brickIndex)
        {
            uint data = chunk.bricks[brickIndex].data;
            if ((data & DENSE_BIT) != 0) return (int)(data & INDEX_MASK);

            int poolIndex = pool.Alloc();
            chunk.bricks[brickIndex].data = DENSE_BIT | (uint)poolIndex;
            return poolIndex;
        }

        private static bool BodiesEqual(
            Unity.Collections.NativeArray<byte> a, int aStart,
            Unity.Collections.NativeArray<byte> b, int bStart, int count)
        {
            for (int i = 0; i < count; i++)
                if (a[aStart + i] != b[bStart + i]) return false;
            return true;
        }

        private static bool BodyIsAllOf(
            Unity.Collections.NativeArray<byte> a, int aStart, int count, byte material)
        {
            for (int i = 0; i < count; i++)
                if (a[aStart + i] != material) return false;
            return true;
        }
    }
}