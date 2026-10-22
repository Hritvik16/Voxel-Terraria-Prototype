// Assets/CoreEngine/Streaming/EditSummary.cs
//
// Amendment 8.15 §5 "Live" (engine row E1, task 93a; the 8.14 row 12 downsample
// that was never built). With shells on, a shell chunk is generator + D.4 ops;
// a block-by-block (D.1 / SetVoxel) edit was drawn only inside tier 0's band.
// An EDIT SUMMARY is a resident edited 0.1 m chunk downsampled 2x per shell
// step by majority vote (LODDownsampler's rule, the cascades'), kept only where
// the chunk's edited bricks are (Chunk.editMask), at shell levels 1-7. A shell
// chunk is then generated as generator + ops + every summary inside it (the
// resident chunk is the truth: it already holds D.1 and the ops), and THEN task
// 83's cave fill (shells >= 3).
//
// WHAT A SUMMARY OVERWRITES at level k: the coarse voxels whose 2^k fine cube
// meets an edited brick. Per level a "group" is the fine bricks one coarse
// voxel spans (1 brick for k <= 3, 2^(k-3) per axis above), and each edited
// group is a block of max(8 >> k, 1)^3 coarse voxels. Unedited bricks keep the
// generator's look (its point sample and top-voxel rule), so a summary never
// paints a 12.8 m patch of different grass around a small build.
//
// THREADING (single-writer, §0.1, CLAUDE.md): Snapshot runs on the MAIN thread
// and only copies the chunk's brick words and dense bodies into a Scratch the
// caller owns; BuildJob (Burst) expands and votes on a job worker from that
// Scratch alone; Extract (main thread) cuts the footprint out into an
// immutable EditSummary. Shell workers read summaries through an immutable
// EditSummarySnapshot handed to them at dispatch. No locks.
//
// Coordinates are integer shifts and masks only (§6.2).
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    /// One edited resident chunk, summarised at shell levels 1-7. Immutable.
    public sealed class EditSummary
    {
        public readonly int3 Coord;             // 0.1 m chunk
        public readonly ulong[] Mask;           // edited bricks when snapshotted (LocalBrickIndex bits)
        internal readonly ulong[][] Groups;     // [k]: edited groups at level k (bit = group index)
        internal readonly byte[][] Values;      // [k]: footprint voxels, EditSummaryOps.ForEach order
        public readonly int Bytes;
        /// Coarse voxels this summary writes at shell level k.
        public int VoxelsAt(int k) => Values[k].Length;
        /// Bounding box of the edited bricks (chunk-local brick coords, inclusive); Lo > Hi = none.
        public readonly int3 BrickLo, BrickHi;

        internal EditSummary(int3 coord, ulong[] mask, ulong[][] groups, byte[][] values)
        {
            Coord = coord; Mask = mask; Groups = groups; Values = values;
            BrickLo = new int3(16); BrickHi = new int3(-1);
            for (int w = 0; w < mask.Length; w++)
            {
                ulong m = mask[w];
                while (m != 0)
                {
                    int bi = (w << 6) | math.tzcnt(m);
                    m &= m - 1;
                    var bc = new int3(bi & 15, (bi >> 4) & 15, bi >> 8);
                    BrickLo = math.min(BrickLo, bc); BrickHi = math.max(BrickHi, bc);
                }
            }
            int b = mask.Length * 8;
            for (int k = 1; k <= EditSummaryOps.MAX_LEVEL; k++) b += values[k].Length + groups[k].Length * 8;
            Bytes = b;
        }
    }

    /// The summaries a shell chunk generation sees. Immutable; the main thread
    /// publishes a new one whenever a summary is built or dropped.
    public sealed class EditSummarySnapshot
    {
        public static readonly EditSummarySnapshot Empty = new EditSummarySnapshot(Array.Empty<EditSummary>(), 0);
        public readonly EditSummary[] All;
        public readonly int Version;
        public int Count => All.Length;
        public EditSummarySnapshot(EditSummary[] all, int version) { All = all; Version = version; }
    }

    public static class EditSummaryOps
    {
        /// Shell levels summarised: 1 (0.2 m) .. 7 (12.8 m), TerrainGenChunkSource.MAX_SHELL.
        public const int MAX_LEVEL = 7;
        private const int E = EngineConfig.CHUNK_EDGE_VOXELS;   // 128
        private const int GROUP_WORDS = EngineConfig.BRICKS_PER_CHUNK >> 6;   // 64

        /// Level k's offset in Scratch.Levels (level k is (128 >> k)^3 bytes, index (y << 2s) | (z << s) | x, s = 7 - k).
        public static int LevelOffset(int k)
        {
            int o = 0;
            for (int j = 1; j < k; j++) { int s = 7 - j; o += 1 << (3 * s); }
            return o;
        }
        public static readonly int LevelsBytes = LevelOffset(MAX_LEVEL + 1);

        /// LODDownsampler.MajorityVote over 8 bytes packed little-endian into `p`:
        /// air only on 5+ of 8; else the most frequent non-air; ties -> the lowest id.
        public static byte Vote(ulong p)
        {
            int air = 0;
            for (int i = 0; i < 8; i++) if (((p >> (i << 3)) & 0xFF) == 0) air++;
            if (air >= 5) return 0;
            int best = 0, bestC = 0;
            for (int i = 0; i < 8; i++)
            {
                int v = (int)((p >> (i << 3)) & 0xFF);
                if (v == 0) continue;
                int c = 0;
                for (int j = 0; j < 8; j++) if ((int)((p >> (j << 3)) & 0xFF) == v) c++;
                if (c > bestC || (c == bestC && v < best)) { best = v; bestC = c; }
            }
            return (byte)best;
        }

        // =====================================================================
        // D.1 -> edited bricks
        // =====================================================================

        /// The bricks a D.1 delta records (each one deviates from the baseline),
        /// as an editMask; null when it records none. Call only on bytes
        /// DeltaCodec.TryDecodeOnto accepted (the structure is proven there);
        /// bounds are still checked, and a short walk just stops. Reads the
        /// frozen layout (DeltaCodec's header), changes nothing in it.
        public static ulong[] MaskFromDelta(byte[] bytes)
        {
            if (bytes == null || bytes.Length < DeltaCodec.HEADER_BYTES + DeltaCodec.TRAILER_BYTES) return null;
            int count = bytes[DeltaCodec.HEADER_BYTES - 2] | (bytes[DeltaCodec.HEADER_BYTES - 1] << 8);
            if (count == 0) return null;
            var mask = new ulong[GROUP_WORDS];
            int pos = DeltaCodec.HEADER_BYTES, end = bytes.Length - DeltaCodec.TRAILER_BYTES;
            for (int n = 0; n < count && pos + DeltaCodec.RECORD_HEADER_BYTES <= end; n++)
            {
                int bi = (bytes[pos] | (bytes[pos + 1] << 8)) & (EngineConfig.BRICKS_PER_CHUNK - 1);
                var kind = (DeltaBrickKind)bytes[pos + 2];
                mask[bi >> 6] |= 1UL << (bi & 63);
                pos += DeltaCodec.RECORD_HEADER_BYTES + (kind == DeltaBrickKind.Uniform ? DeltaCodec.UNIFORM_PAYLOAD_BYTES : DeltaCodec.DensePayloadBytes);
            }
            return mask;
        }

        // =====================================================================
        // Build: snapshot (main) -> vote (job worker) -> extract (main)
        // =====================================================================

        /// One build's storage, owned by one caller at a time (~4.3 MB).
        public sealed class Scratch : IDisposable
        {
            public NativeArray<uint> Handles;   // 4096: uniform material, or 0x80000000 | body slot
            public NativeArray<byte> Bodies;    // 4096 x 512
            public NativeArray<byte> Fine;      // 128^3, TerrainGenChunkSource.Index order
            public NativeArray<byte> Levels;    // levels 1-7 (LevelOffset)
            public JobHandle Job;
            public bool Busy;

            public Scratch()
            {
                Handles = new NativeArray<uint>(EngineConfig.BRICKS_PER_CHUNK, Allocator.Persistent);
                Bodies = new NativeArray<byte>(EngineConfig.BRICKS_PER_CHUNK * EngineConfig.BRICK_BODY_BYTES, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                Fine = new NativeArray<byte>(E * E * E, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                Levels = new NativeArray<byte>(LevelsBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                Job.Complete();
                if (Handles.IsCreated) Handles.Dispose();
                if (Bodies.IsCreated) Bodies.Dispose();
                if (Fine.IsCreated) Fine.Dispose();
                if (Levels.IsCreated) Levels.Dispose();
            }
        }

        /// MAIN THREAD: copy the chunk's brick words and dense bodies into `s`.
        public static void Snapshot(Chunk chunk, BrickDataPool pool, Scratch s)
        {
            if (chunk.isUniform || chunk.bricks == null)
            {
                for (int b = 0; b < EngineConfig.BRICKS_PER_CHUNK; b++) s.Handles[b] = chunk.uniformMaterial;
                return;
            }
            var raw = pool.RawData;
            int n = 0;
            for (int b = 0; b < EngineConfig.BRICKS_PER_CHUNK; b++)
            {
                uint h = chunk.bricks[b].data;
                if ((h & 0x80000000) == 0) { s.Handles[b] = h & 0xFF; continue; }
                NativeArray<byte>.Copy(raw, (int)(h & 0x3FFFFFFF) * EngineConfig.BRICK_BODY_BYTES,
                                       s.Bodies, n * EngineConfig.BRICK_BODY_BYTES, EngineConfig.BRICK_BODY_BYTES);
                s.Handles[b] = 0x80000000u | (uint)n;
                n++;
            }
        }

        public static void Schedule(Scratch s)
        {
            s.Job = Job(s).Schedule();
            JobHandle.ScheduleBatchedJobs();
        }

        public static void Run(Scratch s) => Job(s).Run();

        private static BuildJob Job(Scratch s) =>
            new BuildJob { handles = s.Handles, bodies = s.Bodies, fine = s.Fine, levels = s.Levels };

        /// The level-k voxel (k = 1..7, chunk-local coarse x, y, z) of a finished build (tests).
        public static byte LevelVoxel(Scratch s, int k, int3 p)
        {
            int sh = 7 - k;
            return s.Levels[LevelOffset(k) + ((p.y << (sh << 1)) | (p.z << sh) | p.x)];
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct BuildJob : IJob
        {
            [ReadOnly] public NativeArray<uint> handles;
            [ReadOnly] public NativeArray<byte> bodies;
            public NativeArray<byte> fine;
            public NativeArray<byte> levels;

            public void Execute()
            {
                for (int b = 0; b < EngineConfig.BRICKS_PER_CHUNK; b++)
                {
                    int ox = (b & 15) << 3, oy = ((b >> 4) & 15) << 3, oz = (b >> 8) << 3;
                    uint h = handles[b];
                    bool dense = (h & 0x80000000) != 0;
                    int body = (int)(h & 0x7FFFFFFF) * EngineConfig.BRICK_BODY_BYTES;
                    byte m = (byte)(h & 0xFF);
                    for (int z = 0; z < 8; z++)
                    for (int y = 0; y < 8; y++)
                    {
                        int row = ((oy + y) << 14) | ((oz + z) << 7) | ox;
                        int src = body + ((z << 6) | (y << 3));
                        for (int x = 0; x < 8; x++) fine[row + x] = dense ? bodies[src + x] : m;
                    }
                }
                Down(fine, 0, 7, LevelOffset(1));
                for (int k = 2; k <= MAX_LEVEL; k++) Down(levels, LevelOffset(k - 1), 8 - k, LevelOffset(k));
            }

            /// One 2x vote step: source edge 1 << ss at `src` (in `from`) -> levels at `dst`.
            private void Down(NativeArray<byte> from, int src, int ss, int dst)
            {
                int ds = ss - 1, de = 1 << ds;
                for (int y = 0; y < de; y++)
                for (int z = 0; z < de; z++)
                for (int x = 0; x < de; x++)
                {
                    ulong p = 0; int i = 0;
                    for (int dy = 0; dy < 2; dy++)
                    for (int dz = 0; dz < 2; dz++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int si = src + ((((y << 1) + dy) << (ss << 1)) | (((z << 1) + dz) << ss) | ((x << 1) + dx));
                        p |= (ulong)from[si] << (i << 3);
                        i++;
                    }
                    levels[dst + ((y << (ds << 1)) | (z << ds) | x)] = Vote(p);
                }
            }
        }

        /// MAIN THREAD, after the job: the summary of chunk `coord` for the edited
        /// bricks `mask` (copied by the caller when it snapshotted the chunk).
        public static EditSummary Extract(int3 coord, ulong[] mask, Scratch s)
        {
            var groups = new ulong[MAX_LEVEL + 1][];
            var values = new byte[MAX_LEVEL + 1][];
            for (int k = 1; k <= MAX_LEVEL; k++)
            {
                var g = groups[k] = GroupBits(mask, k);
                int be = BlockEdge(k), n = 0;
                for (int w = 0; w < g.Length; w++) n += math.countbits(g[w]);
                var v = values[k] = new byte[n * be * be * be];
                int off = LevelOffset(k), sh = 7 - k, i = 0;
                for (int w = 0; w < g.Length; w++)
                {
                    ulong m = g[w];
                    while (m != 0)
                    {
                        int3 co = GroupOrigin((w << 6) | math.tzcnt(m), k);
                        m &= m - 1;
                        for (int y = 0; y < be; y++)
                        for (int z = 0; z < be; z++)
                        for (int x = 0; x < be; x++)
                        {
                            int3 p = co + new int3(x, y, z);
                            v[i++] = s.Levels[off + ((p.y << (sh << 1)) | (p.z << sh) | p.x)];
                        }
                    }
                }
            }
            return new EditSummary(coord, (ulong[])mask.Clone(), groups, values);
        }

        // =====================================================================
        // Footprint
        // =====================================================================

        /// log2 of the fine bricks per axis one level-k coarse voxel spans (0 for k <= 3).
        private static int GroupShift(int k) => math.max(k - 3, 0);
        /// Coarse voxels per axis one edited group writes.
        public static int BlockEdge(int k) => math.max(8 >> k, 1);

        /// Edited groups at level k: group (bx, by, bz) >> GroupShift(k), index (gz << 2L) | (gy << L) | gx, L = 4 - shift.
        private static ulong[] GroupBits(ulong[] mask, int k)
        {
            int g = GroupShift(k), L = 4 - g;
            var bits = new ulong[math.max(1, (1 << (3 * L)) >> 6)];
            for (int w = 0; w < mask.Length; w++)
            {
                ulong m = mask[w];
                while (m != 0)
                {
                    int b = (w << 6) | math.tzcnt(m);
                    m &= m - 1;
                    int gx = (b & 15) >> g, gy = ((b >> 4) & 15) >> g, gz = (b >> 8) >> g;
                    int gi = (gz << (L << 1)) | (gy << L) | gx;
                    bits[gi >> 6] |= 1UL << (gi & 63);
                }
            }
            return bits;
        }

        /// Group `gi`'s first coarse voxel (chunk-local, level k). Groups go in
        /// ascending index order everywhere (Extract and ApplyToShell agree).
        private static int3 GroupOrigin(int gi, int k)
        {
            int g = GroupShift(k), L = 4 - g, G = (1 << L) - 1;
            return (new int3(gi & G, (gi >> L) & G, gi >> (L << 1)) << (g + 3)) >> k;
        }

        /// The coarse box (shell-chunk local, inclusive) summary `s` writes in shell
        /// chunk `shellCoord` of shell k; false when it is not inside that chunk or empty.
        public static bool ShellBox(EditSummary s, int3 shellCoord, int k, out int3 lo, out int3 hi)
        {
            lo = hi = default;
            if (k < 1 || k > MAX_LEVEL || !(s.Coord >> k).Equals(shellCoord) || math.any(s.BrickHi < s.BrickLo)) return false;
            int3 region = (s.Coord & ((1 << k) - 1)) << (7 - k);
            lo = region + ((s.BrickLo << 3) >> k);
            hi = region + ((((s.BrickHi + 1) << 3) - 1) >> k);
            return true;
        }

        // =====================================================================
        // Apply (shell workers)
        // =====================================================================

        /// Writes every summary of `snap` inside shell chunk `sc` (shells 1-7) over
        /// its voxels and refreshes the shell bricks touched. Returns the number of
        /// summaries applied. Writes only `sc` (owned by the caller).
        public static int ApplyToShell(TerrainGenChunkSource.ShellChunk sc, EditSummarySnapshot snap)
        {
            int k = sc.Shell;
            if (snap == null || snap.Count == 0 || k < 1 || k > MAX_LEVEL) return 0;
            int applied = 0;
            Span<ulong> touched = stackalloc ulong[GROUP_WORDS];
            touched.Clear();
            int be = BlockEdge(k);
            foreach (var s in snap.All)
            {
                if (!(s.Coord >> k).Equals(sc.Coord)) continue;
                var v = s.Values[k];
                if (v.Length == 0) continue;
                applied++;
                if (sc.IsUniformAir)
                {
                    bool solid = false;
                    for (int i = 0; i < v.Length && !solid; i++) solid = v[i] != Materials.Air;
                    if (!solid) continue;   // air over air
                    for (int j = 0; j < sc.Mats.Length; j++) sc.Mats[j] = Materials.Air;   // bricks are already uniform air
                    sc.IsUniformAir = false;
                }
                int3 region = (s.Coord & ((1 << k) - 1)) << (7 - k);
                var g = s.Groups[k];
                int n = 0;
                for (int w = 0; w < g.Length; w++)
                {
                    ulong m = g[w];
                    while (m != 0)
                    {
                        int3 o = region + GroupOrigin((w << 6) | math.tzcnt(m), k);
                        m &= m - 1;
                        for (int y = 0; y < be; y++)
                        for (int z = 0; z < be; z++)
                        for (int x = 0; x < be; x++)
                            sc.Mats[TerrainGenChunkSource.Index(o.x + x, o.y + y, o.z + z)] = v[n++];
                        int bi = ((o.z >> 3) << 8) | ((o.y >> 3) << 4) | (o.x >> 3);   // a block never crosses a shell brick
                        touched[bi >> 6] |= 1UL << (bi & 63);
                    }
                }
            }
            if (applied == 0) return 0;
            for (int w = 0; w < GROUP_WORDS; w++)
            {
                ulong m = touched[w];
                while (m != 0)
                {
                    int bi = (w << 6) | math.tzcnt(m);
                    m &= m - 1;
                    EditOpApply.RecomputeShellBrick(sc, bi & 15, (bi >> 4) & 15, bi >> 8);
                }
            }
            return applied;
        }
    }
}
