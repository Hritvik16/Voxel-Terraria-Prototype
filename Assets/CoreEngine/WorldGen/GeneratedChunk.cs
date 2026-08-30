// ==========================================
// Assets/CoreEngine/WorldGen/GeneratedChunk.cs
//
// STAGE 2 of the Job System conversion (Option A).
//
// A blittable, job-writable representation of one generated chunk, plus the
// main-thread conversion into the existing managed Chunk.
//
// WHY THIS SHAPE, AND NOT A NATIVE Chunk:
//   Option A's whole point is that Chunk.bricks never changes. Converting it
//   would touch the frozen chunk/brick memory layout (§0.3 review) across 84
//   call sites in 19 files, and would buy a capability -- jobs mutating
//   RESIDENT chunks -- that §0.1.5's single-writer rule forbids using anyway.
//
//   So jobs write a native RESULT, and the main thread converts it into today's
//   Chunk. The conversion is not new overhead bolted on: StreamManager already
//   performs exactly one transfer per generated chunk at the same point
//   ("Transfer scratch -> shared pool. This is the only place shared pool
//   allocation happens for generated chunks."). This slots into that seam.
//
// SELF-CONTAINED BODIES, NOT POOL SLOTS. A job cannot allocate from
// BrickDataPool -- allocation is single-owner main-thread state (§0.1.5), and
// the pool's Alloc throws, which Burst cannot express. So a GeneratedChunk
// carries its dense bodies inline, densely packed in discovery order, and the
// handle array stores the BODY INDEX rather than a pool slot. ToChunk performs
// the pool allocation, on the main thread, exactly as today.
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;

namespace VoxelEngine.WorldGen
{
    /// One generated chunk in blittable form. The producer owns the containers
    /// and must Dispose; ToChunk copies out and leaves this untouched.
    public struct GeneratedChunk
    {
        public const uint DENSE_BIT = 0x80000000u;
        public const uint SLOT_MASK = 0x3FFFFFFFu;

        /// 4096 entries. Dense entries hold DENSE_BIT | bodyIndex, where
        /// bodyIndex addresses `bodies`, NOT a BrickDataPool slot.
        public NativeArray<uint> handles;

        /// Dense bodies, 512 bytes each, packed back-to-back. Length is
        /// denseCount * 512.
        public NativeArray<byte> bodies;

        public int denseCount;
        public bool isUniform;
        public byte uniformMaterial;

        public bool IsCreated => handles.IsCreated;

        public void Dispose()
        {
            if (handles.IsCreated) handles.Dispose();
            if (bodies.IsCreated) bodies.Dispose();
        }

        /// Allocates for the worst case: every brick dense. 4096 * 512 = 2 MB,
        /// which is the same order as the per-worker scratch BrickDataPool it
        /// replaces (BRICKS_PER_CHUNK * 512), so this is not new footprint.
        public static GeneratedChunk Create(Allocator alloc)
        {
            return new GeneratedChunk
            {
                handles = new NativeArray<uint>(EngineConfig.BRICKS_PER_CHUNK, alloc),
                bodies = new NativeArray<byte>(
                    EngineConfig.BRICKS_PER_CHUNK * EngineConfig.BRICK_BODY_BYTES, alloc),
                denseCount = 0,
                isUniform = false,
                uniformMaterial = 0,
            };
        }
    }

    public static class GeneratedChunkConverter
    {
        /// Materialises a GeneratedChunk into the managed Chunk the rest of the
        /// engine uses, allocating dense bodies from the shared pool.
        ///
        /// MAIN THREAD ONLY. It allocates from `pool` and from `allocator`, both
        /// of which are single-writer state under §0.1.5 / §3.2.
        ///
        /// Returns false without mutating `chunk` if the pool cannot satisfy the
        /// whole chunk. Reserving up front rather than allocating as it goes is
        /// deliberate: a half-built chunk with some bricks pointing at pool slots
        /// and others left as air is worse than a refused one, and is exactly the
        /// state an exception thrown mid-loop would leave behind.
        public static bool TryToChunk(in GeneratedChunk src, int3 coord,
                                      Chunk chunk, ChunkHandleAllocator allocator,
                                      BrickDataPool pool)
        {
            chunk.coord = coord;

            if (src.isUniform)
            {
                chunk.isUniform = true;
                chunk.uniformMaterial = src.uniformMaterial;
                chunk.bricks = null;
                return true;
            }

            if (pool.FreeCount < src.denseCount) return false;

            chunk.isUniform = false;
            chunk.uniformMaterial = 0;
            if (chunk.bricks == null) chunk.bricks = allocator.Alloc();

            var raw = pool.RawData;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                uint h = src.handles[i];
                if ((h & GeneratedChunk.DENSE_BIT) == 0)
                {
                    // Uniform brick: the material rides in the low byte, exactly
                    // as BrickHandle stores it.
                    chunk.bricks[i].data = h;
                    continue;
                }

                int bodyIndex = (int)(h & GeneratedChunk.SLOT_MASK);
                int slot = pool.Alloc();          // reserved above, cannot throw
                int dst = slot * EngineConfig.BRICK_BODY_BYTES;
                int srcOff = bodyIndex * EngineConfig.BRICK_BODY_BYTES;
                for (int v = 0; v < EngineConfig.BRICK_BODY_BYTES; v++)
                    raw[dst + v] = src.bodies[srcOff + v];

                chunk.bricks[i].data = GeneratedChunk.DENSE_BIT | (uint)slot;
            }

            return true;
        }

        /// Inverse, for tests: capture an existing managed Chunk in the native
        /// form. Round-tripping a chunk through both directions must preserve its
        /// content hash, which is what proves the representation is lossless
        /// before any generator is pointed at it.
        public static GeneratedChunk FromChunk(Chunk chunk, BrickDataPool pool, Allocator alloc)
        {
            var g = GeneratedChunk.Create(alloc);

            if (chunk.isUniform || chunk.bricks == null)
            {
                g.isUniform = true;
                g.uniformMaterial = chunk.uniformMaterial;
                return g;
            }

            var raw = pool.RawData;
            int dense = 0;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                uint d = chunk.bricks[i].data;
                if ((d & GeneratedChunk.DENSE_BIT) == 0) { g.handles[i] = d; continue; }

                int slot = (int)(d & GeneratedChunk.SLOT_MASK);
                int srcOff = slot * EngineConfig.BRICK_BODY_BYTES;
                int dstOff = dense * EngineConfig.BRICK_BODY_BYTES;
                for (int v = 0; v < EngineConfig.BRICK_BODY_BYTES; v++)
                    g.bodies[dstOff + v] = raw[srcOff + v];

                g.handles[i] = GeneratedChunk.DENSE_BIT | (uint)dense;
                dense++;
            }

            g.denseCount = dense;
            return g;
        }
    }
}
