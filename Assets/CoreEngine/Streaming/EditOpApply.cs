// Assets/CoreEngine/Streaming/EditOpApply.cs
//
// Amendment 8.14 row 9 (engine step 13a, task 81): applies D.4 edit ops to a
// freshly generated chunk, in seq order.
//   ApplyToChunk  a 0.1 m chunk in storage the caller owns (a worker's scratch
//                 pool, SaveDelta's baseline, a test). Per voxel it does what
//                 ChunkStore.SetVoxel does (uniform chunk -> expanded, uniform
//                 brick -> densified, then the byte), so an op applied here
//                 gives the same voxels as the same edit made near through
//                 EditService (EditOpTests). A brick the op covers WHOLLY is
//                 set uniform instead: same voxels, no 512-byte body.
//   ApplyToShell  a shell-k chunk (GenerateShellChunk's output). Coarse voxel
//                 l takes the op's material iff the op covers its fine sample
//                 origin + (l << k) + half (row 13's sample), so a shell chunk
//                 is the coarse sample of the edited 0.1 m world. The top-voxel
//                 rule (task 74 Q1 (a)) is generation's and is not redone: a
//                 top voxel whose sample the op misses keeps its surface colour.
// Coordinates are integer voxels throughout; bounds use shifts, never division.
//
// THREADING: pure functions of their arguments; they write only the chunk and
// pool passed in (single-writer, CLAUDE.md). Workers pass an EditOpSnapshot.
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Streaming
{
    public static class EditOpApply
    {
        private const int CHUNK_SHIFT = 7;                       // 128 voxels
        private const int EDGE = EngineConfig.CHUNK_EDGE_VOXELS;
        private const uint DENSE_BIT = 0x80000000u;
        private const uint INDEX_MASK = 0x3FFFFFFFu;

        /// Does the op write voxel `v` (world voxel coordinates, 0.1 m)?
        public static bool Covers(in EditOp op, int3 v)
        {
            int3 d = v - op.centreVoxel;
            int s = op.size;
            if (op.kind == EditOpKind.Box) return math.all(math.abs(d) <= s);
            return d.x * d.x + d.y * d.y + d.z * d.z <= s * s;   // FluidGpuSimulation.SphereCovers
        }

        /// The inclusive world-voxel box the op can write.
        public static void Bounds(in EditOp op, out int3 lo, out int3 hi)
        {
            lo = op.centreVoxel - op.size;
            hi = op.centreVoxel + op.size;
        }

        /// Does the op reach shell `shell`'s chunk `coord` (in that shell's chunk units)?
        public static bool TouchesChunk(in EditOp op, int3 coord, int shell = 0)
        {
            Bounds(op, out int3 lo, out int3 hi);
            int3 c0 = coord << (CHUNK_SHIFT + shell);
            int3 c1 = c0 + ((EDGE << shell) - 1);
            return math.all(hi >= c0) && math.all(lo <= c1);
        }

        // =====================================================================
        // 0.1 m chunks
        // =====================================================================

        /// Applies ops[from..to) that reach `coord` onto `chunk`. Returns voxels written.
        public static int ApplyToChunk(Chunk chunk, int3 coord, ChunkHandleAllocator allocator, BrickDataPool pool,
                                       EditOp[] ops, int from, int to)
        {
            int written = 0;
            int3 c0 = coord << CHUNK_SHIFT;
            for (int i = from; i < to; i++)
            {
                var op = ops[i];
                if (!TouchesChunk(op, coord)) continue;
                Bounds(op, out int3 lo, out int3 hi);
                int3 l0 = math.max(lo - c0, 0), l1 = math.min(hi - c0, EDGE - 1);   // local voxel box
                int3 b0 = l0 >> 3, b1 = l1 >> 3;
                for (int bz = b0.z; bz <= b1.z; bz++)
                for (int by = b0.y; by <= b1.y; by++)
                for (int bx = b0.x; bx <= b1.x; bx++)
                {
                    int bi = (bz << 8) | (by << 4) | bx;
                    int3 v0 = c0 + (new int3(bx, by, bz) << 3);
                    if (CoversBrick(op, v0)) { written += SetBrickUniform(chunk, allocator, pool, bi, op.material); continue; }
                    int3 s0 = math.max(l0, new int3(bx, by, bz) << 3), s1 = math.min(l1, (new int3(bx, by, bz) << 3) + 7);
                    for (int z = s0.z; z <= s1.z; z++)
                    for (int y = s0.y; y <= s1.y; y++)
                    for (int x = s0.x; x <= s1.x; x++)
                        if (Covers(op, c0 + new int3(x, y, z)) &&
                            WriteVoxel(chunk, allocator, pool, bi, ((z & 7) << 6) | ((y & 7) << 3) | (x & 7), op.material))
                            written++;
                }
            }
            return written;
        }

        /// All 8 corners covered -> the whole brick is (spheres and boxes are convex).
        private static bool CoversBrick(in EditOp op, int3 v0)
        {
            for (int c = 0; c < 8; c++)
                if (!Covers(op, v0 + new int3(c & 1, (c >> 1) & 1, c >> 2) * 7)) return false;
            return true;
        }

        private static void ExpandIfUniform(Chunk chunk, ChunkHandleAllocator allocator)
        {
            if (!chunk.isUniform) return;
            if (chunk.bricks == null) chunk.bricks = allocator.Alloc();
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) chunk.bricks[i].data = chunk.uniformMaterial;
            chunk.isUniform = false;
        }

        /// ChunkStore.SetVoxel's steps on owned storage. True if the byte changed.
        private static bool WriteVoxel(Chunk chunk, ChunkHandleAllocator allocator, BrickDataPool pool, int bi, int vi, byte m)
        {
            if (chunk.isUniform)
            {
                if (chunk.uniformMaterial == m) return false;
                ExpandIfUniform(chunk, allocator);
            }
            uint h = chunk.bricks[bi].data;
            NativeArray<byte> raw = pool.RawData;
            if ((h & DENSE_BIT) == 0)
            {
                byte bm = (byte)(h & 0xFF);
                if (bm == m) return false;
                int idx = pool.Alloc();
                int start = idx * EngineConfig.BRICK_BODY_BYTES;
                for (int i = 0; i < EngineConfig.BRICK_BODY_BYTES; i++) raw[start + i] = bm;
                h = DENSE_BIT | (uint)idx;
                chunk.bricks[bi].data = h;
            }
            int flat = (int)(h & INDEX_MASK) * EngineConfig.BRICK_BODY_BYTES + vi;
            if (raw[flat] == m) return false;
            raw[flat] = m;
            return true;
        }

        /// Returns the voxels that changed (0 or the brick's 512, counted coarsely: 512 if anything did).
        private static int SetBrickUniform(Chunk chunk, ChunkHandleAllocator allocator, BrickDataPool pool, int bi, byte m)
        {
            if (chunk.isUniform)
            {
                if (chunk.uniformMaterial == m) return 0;
                ExpandIfUniform(chunk, allocator);
            }
            uint h = chunk.bricks[bi].data;
            if ((h & DENSE_BIT) != 0) pool.Free((int)(h & INDEX_MASK));
            else if ((byte)(h & 0xFF) == m) return 0;
            chunk.bricks[bi].data = m;
            return EngineConfig.BRICK_BODY_BYTES;
        }

        // =====================================================================
        // Shell chunks
        // =====================================================================

        /// Applies ops[from..to) that reach the shell chunk onto it, at its own voxel
        /// size. Returns coarse voxels written. Row 13's default sample only.
        public static int ApplyToShell(TerrainGenChunkSource.ShellChunk sc, EditOp[] ops, int from, int to)
        {
            int k = sc.Shell;
            int half = k == 0 ? 0 : (1 << (k - 1)) - 1;
            int3 origin = sc.Coord << (CHUNK_SHIFT + k);
            int3 sampleOrigin = origin + half;
            int written = 0;
            for (int i = from; i < to; i++)
            {
                var op = ops[i];
                if (!TouchesChunk(op, sc.Coord, k)) continue;
                if (sc.IsUniformAir)
                {
                    if (op.material == Materials.Air) continue;
                    for (int j = 0; j < sc.Mats.Length; j++) sc.Mats[j] = Materials.Air;   // BrickMat/BrickUniform are already air/uniform
                    sc.IsUniformAir = false;
                }
                Bounds(op, out int3 lo, out int3 hi);
                // Coarse l with lo <= sampleOrigin + (l << k) <= hi: ceil and floor by shifts.
                int3 l0 = math.max((lo - sampleOrigin + ((1 << k) - 1)) >> k, 0);
                int3 l1 = math.min((hi - sampleOrigin) >> k, EDGE - 1);
                if (math.any(l1 < l0)) continue;
                bool any = false;
                for (int ly = l0.y; ly <= l1.y; ly++)
                for (int lz = l0.z; lz <= l1.z; lz++)
                for (int lx = l0.x; lx <= l1.x; lx++)
                {
                    if (!Covers(op, sampleOrigin + (new int3(lx, ly, lz) << k))) continue;
                    int mi = TerrainGenChunkSource.Index(lx, ly, lz);
                    if (sc.Mats[mi] == op.material) continue;
                    sc.Mats[mi] = op.material; written++; any = true;
                }
                if (!any) continue;
                int3 b0 = l0 >> 3, b1 = l1 >> 3;
                for (int bz = b0.z; bz <= b1.z; bz++)
                for (int by = b0.y; by <= b1.y; by++)
                for (int bx = b0.x; bx <= b1.x; bx++)
                    RecomputeShellBrick(sc, bx, by, bz);
            }
            return written;
        }

        private static void RecomputeShellBrick(TerrainGenChunkSource.ShellChunk sc, int bx, int by, int bz)
        {
            int bi = (bz << 8) | (by << 4) | bx;
            byte first = sc.Mats[TerrainGenChunkSource.Index(bx << 3, by << 3, bz << 3)];
            bool uni = true;
            for (int z = 0; z < 8 && uni; z++)
            for (int y = 0; y < 8 && uni; y++)
            for (int x = 0; x < 8; x++)
                if (sc.Mats[TerrainGenChunkSource.Index((bx << 3) + x, (by << 3) + y, (bz << 3) + z)] != first) { uni = false; break; }
            sc.BrickUniform[bi] = uni;
            sc.BrickMat[bi] = first;
        }
    }
}
