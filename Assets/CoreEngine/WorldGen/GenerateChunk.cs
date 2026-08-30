// Assets/CoreEngine/WorldGen/GenerateChunk.cs
//
// Phase 3, file 4 of the spec's ordered list (§13 Phase 3): "extend the
// Phase-2 version to §5.3 steps 1–4: heightfield+biome per column, uniform
// fill, dense surface/feature bricks, biome strata materials, static water
// below sea level."
//
// STRUCTURE OF THIS FILE (three parts):
//   1. ChunkGenerator (LEGACY, byte-for-byte the Phase-2 v2 generator).
//      Kept verbatim so the Phase 2 scene, GenerationDiagnosticTests, and
//      every historical benchmark path still produce IDENTICAL output.
//      Nothing calls into part 2 from here.
//   2. ColumnSampler + ChunkGeneratorFull (NEW, Phase 3) — the §5.3 pipeline.
//   3. ChunkContentHash (NEW) — logical-content hasher used by the 3a
//      determinism tests and the runtime acceptance rig. Hashes what the
//      voxels ARE, not pool indices, so two generations into different pools
//      compare equal iff the world content is equal.
//
// DEVIATION NOTE: §5.1 says generation runs as Burst-compiled IJobs. Phase 2's
// generator was a plain static function and this file keeps that shape — the
// pipeline stays a pure function (Burst-jobbing it is a mechanical wrapper),
// and doing the Burst move now would confound the Phase-3 re-benchmark that
// PHASE_2_COMPLETION.md §7 mandates. Schedule the Burst wrapper with Phase 4
// streaming, where generation cost actually starts to matter per-frame.
using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

// =====================================================================
// PART 1 — LEGACY Phase-2 generator. DO NOT EDIT (regression fixture).
// =====================================================================

// v2 — the terraced/stepped look in every screenshot so far wasn't a
// rendering or resolution artifact. It was this file: the height noise was
// sampled ONCE PER 8-VOXEL BRICK COLUMN (0.8m), then applied uniformly to
// every voxel in that brick's footprint. That's the actual geometry, at any
// resolution, with a perfect upscaler, forever - there was no finer detail
// for any renderer to lose.
//
// Fix: sample the height noise at every individual voxel's true world X,Z
// (0.1m), so the surface follows the noise field at the engine's real
// resolution instead of a brick-quantized approximation of it.
//
// This also changes brick classification. Before, one height value decided
// whether an entire brick was air/stone/mixed. Now the 8x8 voxel footprint
// of a brick can have 64 DIFFERENT heights - a brick must be classified by
// the MIN and MAX height across that whole footprint, not a single value,
// since a footprint that straddles the surface at varying per-voxel heights
// has to be mixed even where the old brick-level height would have called
// it uniform air or uniform stone.
//
// Noise is still sampled once per (worldVoxelX, worldVoxelZ) column, cached
// per 8x8 footprint and reused across all 16 Y-bricks in that column - not
// re-sampled per Y-brick - so this is 64 samples per column instead of 1,
// not 1024.
public static class ChunkGenerator
{
    public static void GenerateChunk(int seed, int3 chunkCoord, ref Chunk chunk, ChunkHandleAllocator allocator, BrickDataPool pool)
    {
        chunk.coord = chunkCoord;
        chunk.isUniform = false;
        chunk.bricks = allocator.Alloc();

        int3 baseVoxel = chunkCoord * 128;
        int[] heightCache = new int[64]; // reused per (bx,bz) column, indexed (lz<<3)|lx

        for (int bz = 0; bz < 16; bz++)
        for (int bx = 0; bx < 16; bx++)
        {
            int baseVoxelX = baseVoxel.x + (bx * 8);
            int baseVoxelZ = baseVoxel.z + (bz * 8);

            int minHeight = int.MaxValue;
            int maxHeight = int.MinValue;
            for (int lz = 0; lz < 8; lz++)
            for (int lx = 0; lx < 8; lx++)
            {
                int h = SampleSurfaceHeightVoxel(baseVoxelX + lx, baseVoxelZ + lz);
                heightCache[(lz << 3) | lx] = h;
                if (h < minHeight) minHeight = h;
                if (h > maxHeight) maxHeight = h;
            }

            for (int by = 0; by < 16; by++)
            {
                int currentYMinVoxel = baseVoxel.y + (by * 8);
                int currentYMaxVoxel = currentYMinVoxel + 7;
                int brickFlatIndex = (bz << 8) | (by << 4) | bx;

                if (currentYMinVoxel > maxHeight)
                {
                    // Whole footprint's lowest point is still below this
                    // brick's Y range -> entirely air, no exceptions.
                    chunk.bricks[brickFlatIndex].data = 0;
                }
                else if (currentYMaxVoxel < minHeight)
                {
                    // Whole footprint's highest point is still above this
                    // brick's Y range -> entirely stone, no exceptions.
                    chunk.bricks[brickFlatIndex].data = 2;
                }
                else
                {
                    // Straddles the surface somewhere in this footprint -
                    // per-voxel material from the cached per-column heights.
                    int poolIdx = pool.Alloc();
                    chunk.bricks[brickFlatIndex].data = 0x80000000 | (uint)poolIdx;

                    int startOffset = poolIdx * 512;
                    var rawData = pool.RawData;

                    for (int vy = 0; vy < 8; vy++)
                    {
                        int voxelYWorld = currentYMinVoxel + vy;
                        for (int vz = 0; vz < 8; vz++)
                        for (int vx = 0; vx < 8; vx++)
                        {
                            int colHeight = heightCache[(vz << 3) | vx];
                            byte mat = (byte)(voxelYWorld <= colHeight ? 2 : 0);
                            int vIdx = (vz << 6) | (vy << 3) | vx;
                            rawData[startOffset + vIdx] = mat;
                        }
                    }
                }
            }
        }
    }

    // Same noise field and vertical scale as before (unlerp(-1,1,n)*50 -
    // heights 0-50 voxels, 0-5m), just evaluated at the voxel's real world
    // position instead of the brick's corner.
    private static int SampleSurfaceHeightVoxel(int worldVoxelX, int worldVoxelZ)
    {
        float worldX = worldVoxelX * 0.1f;
        float worldZ = worldVoxelZ * 0.1f;
        float noiseVal = noise.cnoise(new float2(worldX * 0.05f, worldZ * 0.05f));
        return (int)(math.unlerp(-1f, 1f, noiseVal) * 50);
    }
}

// =====================================================================
// PART 2 — Phase 3 full pipeline (§5.3 steps 1–4)
// =====================================================================
namespace VoxelEngine.WorldGen
{
    // Step 1's per-column sampler: heightfield (island falloff per C.2 +
    // hills + height-feature deltas) and Voronoi biome ID. Pure function of
    // (meta, x, z) — the determinism guarantee of §5.3 lives or dies here.
    public static class ColumnSampler
    {
        // Precomputed, immutable per-world state so per-column sampling
        // doesn't rebuild seed offsets or re-derive geometry 16K times/chunk.
        /// Blittable except for the two containers, which are now NATIVE.
        ///
        /// WHY: this struct is the entire input to the column-sampling math, and
        /// that math is 92.6% of per-chunk generation cost (5.16ms of 5.58ms,
        /// measured). Burst cannot compile a function that touches a managed
        /// array, so the managed FeatureAnchor[]/BiomeSeed[] were the one thing
        /// standing between the hot half of generation and Burst.
        ///
        /// OWNERSHIP: CreateState allocates; the caller MUST Dispose. Length-0
        /// containers are left default (never allocated), which keeps
        /// SampleBaseHeight -- called thousands of times during world planning
        /// with no anchors at all -- from allocating per call.
        public struct State : IDisposable
        {
            public float centerX, centerZ, coastRadius, coastFalloff;
            public float2 offHills, offCoast, offFloor;
            public NativeArray<FeatureAnchor> heightAnchors; // Mountain/Crater only
            public NativeArray<BiomeSeed> biomeSeeds;

            public void Dispose()
            {
                if (heightAnchors.IsCreated) heightAnchors.Dispose();
                if (biomeSeeds.IsCreated) biomeSeeds.Dispose();
            }
        }

        public static State CreateState(WorldMetaData meta)
        {
            var st = new State();
            WorldGenConstants.DeriveIslandGeometry(meta.sizeClass,
                out st.centerX, out st.centerZ, out st.coastRadius, out st.coastFalloff);

            // Seed-derived noise-domain offsets, deterministic per seed.
            var r = new Unity.Mathematics.Random((meta.seed | 1u) * 747796405u + 2891336453u);
            st.offHills = r.NextFloat2(-10000f, 10000f);
            st.offCoast = r.NextFloat2(-10000f, 10000f);
            st.offFloor = r.NextFloat2(-10000f, 10000f);

            int heightCount = 0;
            foreach (var a in meta.anchors)
                if (a.kind != FeatureKind.Cave) heightCount++;

            // ALWAYS ALLOCATE, INCLUDING LENGTH 0. An earlier version left
            // empty containers default/unallocated to avoid a malloc in
            // SampleBaseHeight, which AnchorPlanner calls thousands of times
            // with no anchors at all. That is invalid the moment State is used
            // by a job: Unity's job safety system requires every NativeArray
            // field to be constructed, and an anchor-free world threw
            //   "ColumnSampleJob.st.heightAnchors has not been assigned or
            //    constructed. All containers must be valid when scheduling a job."
            // caught by GenerationTests' synthetic single-biome and cave-anchor
            // worlds. A zero-length Persistent allocation is one malloc; the
            // planning path pays a few thousand of them once, at world creation.
            st.heightAnchors = new NativeArray<FeatureAnchor>(heightCount, Allocator.Persistent);
            int w = 0;
            foreach (var a in meta.anchors)
                if (a.kind != FeatureKind.Cave) st.heightAnchors[w++] = a;

            int seedCount = meta.biomeSeeds != null ? meta.biomeSeeds.Length : 0;
            st.biomeSeeds = new NativeArray<BiomeSeed>(seedCount, Allocator.Persistent);
            for (int i = 0; i < seedCount; i++) st.biomeSeeds[i] = meta.biomeSeeds[i];

            return st;
        }

        /// Builds a chunk-local State holding only the anchors and biome seeds
        /// that can affect this chunk. The caller owns the result and MUST
        /// Dispose it.
        ///
        /// WHY THIS IS A PREREQUISITE FOR SCALING ANCHOR COUNTS: without it,
        /// SampleHeightInternal loops EVERY height anchor and SampleBiome loops
        /// EVERY biome seed, for all 16,384 columns of every chunk. Cost is
        /// O(anchors x columns x chunks), so raising anchor counts to populate a
        /// 2,560m world would multiply generation cost by the same factor.
        /// With culling the loops see only what is locally relevant, and a
        /// mountain of radius 240 voxels reaches barely past its own chunk.
        ///
        /// BOTH TESTS ARE CONSERVATIVE AND EXACT, not heuristics:
        ///
        ///   Height anchors: HeightDelta is exactly zero beyond a.radius, so an
        ///   anchor can matter only if its influence circle reaches the chunk:
        ///   |centre - a| <= a.radius + halfDiagonal.
        ///
        ///   Biome seeds: Voronoi needs the true nearest seed, so distance alone
        ///   is not enough. For any point p in the chunk and the seed n nearest
        ///   to the CENTRE, |p-n| <= |c-n| + halfDiag, and for any other seed s,
        ///   |p-s| >= |c-s| - halfDiag. So s can only beat n somewhere in the
        ///   chunk if |c-s| < |c-n| + 2*halfDiag. Keeping everything within that
        ///   bound cannot change any column's winner.
        public static State CullForChunk(in State world, int3 baseVoxel, int edgeVoxels,
                                         Allocator alloc)
        {
            float half = edgeVoxels * 0.5f;
            float cx = baseVoxel.x + half;
            float cz = baseVoxel.z + half;
            float halfDiag = half * 1.41421356f;

            var culled = world;   // copies the scalar fields

            int keep = 0;
            for (int i = 0; i < world.heightAnchors.Length; i++)
            {
                FeatureAnchor a = world.heightAnchors[i];
                float dx = cx - a.cx, dz = cz - a.cz;
                float reach = a.radius + halfDiag;
                if (dx * dx + dz * dz <= reach * reach) keep++;
            }
            culled.heightAnchors = new NativeArray<FeatureAnchor>(keep, alloc);
            int w = 0;
            for (int i = 0; i < world.heightAnchors.Length; i++)
            {
                FeatureAnchor a = world.heightAnchors[i];
                float dx = cx - a.cx, dz = cz - a.cz;
                float reach = a.radius + halfDiag;
                if (dx * dx + dz * dz <= reach * reach) culled.heightAnchors[w++] = a;
            }

            int seedCount = world.biomeSeeds.Length;
            if (seedCount == 0)
            {
                culled.biomeSeeds = new NativeArray<BiomeSeed>(0, alloc);
                return culled;
            }

            float nearest = float.MaxValue;
            for (int i = 0; i < seedCount; i++)
            {
                float dx = cx - world.biomeSeeds[i].x, dz = cz - world.biomeSeeds[i].z;
                float d = math.sqrt(dx * dx + dz * dz);
                if (d < nearest) nearest = d;
            }
            float bound = nearest + 2f * halfDiag;
            float boundSq = bound * bound;

            int keepS = 0;
            for (int i = 0; i < seedCount; i++)
            {
                float dx = cx - world.biomeSeeds[i].x, dz = cz - world.biomeSeeds[i].z;
                if (dx * dx + dz * dz <= boundSq) keepS++;
            }
            culled.biomeSeeds = new NativeArray<BiomeSeed>(keepS, alloc);
            int ws = 0;
            for (int i = 0; i < seedCount; i++)
            {
                float dx = cx - world.biomeSeeds[i].x, dz = cz - world.biomeSeeds[i].z;
                if (dx * dx + dz * dz <= boundSq) culled.biomeSeeds[ws++] = world.biomeSeeds[i];
            }

            return culled;
        }

        public static void SampleColumn(in State st, int worldVoxelX, int worldVoxelZ,
            out int height, out byte biomeId)
        {
            float vx = worldVoxelX + 0.5f;
            float vz = worldVoxelZ + 0.5f;

            height = SampleHeightInternal(in st, vx, vz, includeAnchors: true);
            biomeId = SampleBiome(in st, vx, vz);
        }

        // Base terrain height WITHOUT feature anchors — used by AnchorPlanner's
        // buried-cave check (mountains only ADD height, so this is conservative).
        public static int SampleBaseHeight(uint seed, byte sizeClass, int worldVoxelX, int worldVoxelZ)
        {
            var meta = new WorldMetaData { seed = seed, sizeClass = sizeClass };
            using var st = CreateState(meta);
            return SampleHeightInternal(in st, worldVoxelX + 0.5f, worldVoxelZ + 0.5f, includeAnchors: false);
        }

        private static int SampleHeightInternal(in State st, float vx, float vz, bool includeAnchors)
        {
            // Coastline (C.2): R_coast = halfWidth + fBm(X,Z)·k
            float coastWobble = Fbm3(new float2(vx, vz) * WorldGenConstants.COAST_WOBBLE_FREQ + st.offCoast) * 2f - 1f;
            float coastR = st.coastRadius + coastWobble * WorldGenConstants.COAST_WOBBLE_AMP;

            float dx = vx - st.centerX, dz = vz - st.centerZ;
            float dist = math.sqrt(dx * dx + dz * dz);

            float s = math.saturate((coastR - dist) / st.coastFalloff);
            float islandMask = s * s * (3f - 2f * s); // 1 inland, 0 ocean

            float hills = Fbm3(new float2(vx, vz) * WorldGenConstants.HILL_FREQ + st.offHills); // 0..1
            float inland = WorldGenConstants.INLAND_BASE + hills * WorldGenConstants.HILL_AMPLITUDE;

            float floorN = math.clamp(noise.cnoise(new float2(vx, vz) * WorldGenConstants.OCEAN_FLOOR_FREQ + st.offFloor), -1f, 1f);
            float oceanFloor = WorldGenConstants.OCEAN_FLOOR_MEAN + floorN * WorldGenConstants.OCEAN_FLOOR_NOISE;

            float h = math.lerp(oceanFloor, inland, islandMask);

            if (includeAnchors)
            {
                var anchors = st.heightAnchors;
                for (int i = 0; i < anchors.Length; i++)
                {
                    // One local copy, then read fields off it. A NativeArray
                    // indexer returns BY VALUE (so it cannot bind to an `in`
                    // parameter at all) and each index carries a bounds check,
                    // so hoisting is both required and cheaper than the three
                    // separate indexes this used to do.
                    FeatureAnchor a = anchors[i];
                    float adx = vx - a.cx, adz = vz - a.cz;
                    float rr = a.radius;
                    if (adx * adx + adz * adz >= rr * rr) continue;
                    h += FeatureCarve.HeightDelta(in a, vx, vz);
                }
            }

            return math.clamp((int)math.floor(h),
                WorldGenConstants.MIN_TERRAIN_HEIGHT, WorldGenConstants.MAX_TERRAIN_HEIGHT);
        }

        public static byte SampleBiome(in State st, float vx, float vz)
        {
            var seeds = st.biomeSeeds;
            // IsCreated, not == null: NativeArray is a struct, so `== null`
            // binds to C#'s LIFTED operator== and is always false -- it compiled
            // silently after the native port and checked nothing. Unallocated is
            // IsCreated == false.
            if (!seeds.IsCreated || seeds.Length == 0) return Biomes.ForestId;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < seeds.Length; i++)
            {
                float dx = vx - seeds[i].x, dz = vz - seeds[i].z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; } // strict < : ties break to lower index, deterministic
            }
            return seeds[best].biomeId;
        }

        // 3-octave fBm on cnoise, normalized to 0..1 (clamped — cnoise can
        // brush slightly past ±1 at some lattice points).
        private static float Fbm3(float2 p)
        {
            float n = noise.cnoise(p)
                    + 0.5f * noise.cnoise(p * 2.03f)
                    + 0.25f * noise.cnoise(p * 4.01f);
            return math.clamp(math.unlerp(-1.75f, 1.75f, n), 0f, 1f);
        }
    }

    // The §5.3 four-step generator. Pure function of (meta, chunkCoord) plus
    // the two allocators it writes into — asserted byte-identical across
    // repeated calls and visit orders by GenerationTests.
    public static class ChunkGeneratorFull
    {
        // Phase split for the generation micro-benchmark. Answers "how much of
        // per-chunk generation is ColumnSampler/FeatureCarve (the Burst-able
        // part) versus the voxel fill that writes into Chunk and BrickDataPool
        // (the part that would need protected layout changes to Burst)".
        // Ticks, not ms, so the per-brick reads stay cheap; the caller converts.
        public static long ColumnPhaseTicks;
        public static long TotalPhaseTicks;

        public static void ResetPhaseCounters() { ColumnPhaseTicks = 0; TotalPhaseTicks = 0; }

        public static void GenerateChunkFull(WorldMetaData meta, int3 chunkCoord, Chunk chunk,
            ChunkHandleAllocator allocator, BrickDataPool pool, object allocLock = null)
        {
            using var st = ColumnSampler.CreateState(meta);
            GenerateChunkFull(in st, meta, chunkCoord, chunk, allocator, pool, allocLock);
        }

        // State-reusing overload for bulk generation loops (bootstrapper/rig) —
        // identical output, just skips rebuilding State 484 times.
        // allocLock: when non-null, guards the (not thread-safe) brick pool and
        // handle allocator so this function can be called concurrently from
        // Parallel.For. Null = single-threaded caller, no locking cost.

        /// The fill itself: column sampling then brick classification, both
        /// Burst jobs. Writes only into `gen`.
        /// The inputs both execution paths feed to the two generation jobs.
        ///
        /// SHARED ON PURPOSE. The worker path must use .Run() (Unity forbids
        /// scheduling off the main thread) while the job path uses .Schedule(),
        /// so there are two call sites. Building their inputs in ONE place is
        /// what keeps them from drifting: if the two paths ever disagreed about
        /// a culled cave set or a biome table, they would silently generate
        /// different terrain for the same coord depending on whether the chunk
        /// happened to have a delta file.
        ///
        /// Caller disposes, and must not do so before the jobs complete.
        public struct GenJobResources : IDisposable
        {
            public NativeArray<int> colHeights;
            public NativeArray<byte> colBiomes;
            public ColumnSampler.State localSt;
            public NativeArray<FeatureAnchor> caves;
            public NativeArray<BiomeMaterials> biomeTable;
            public NativeArray<int> denseOut;

            public void Dispose()
            {
                if (colHeights.IsCreated) colHeights.Dispose();
                if (colBiomes.IsCreated) colBiomes.Dispose();
                localSt.Dispose();
                if (caves.IsCreated) caves.Dispose();
                if (biomeTable.IsCreated) biomeTable.Dispose();
                if (denseOut.IsCreated) denseOut.Dispose();
            }
        }

        private const int CHUNK_EDGE_VOXELS = 128;
        private const int CHUNK_COLUMNS = CHUNK_EDGE_VOXELS * CHUNK_EDGE_VOXELS;

        private static GenJobResources BuildResources(in ColumnSampler.State st,
                                                      WorldMetaData meta, int3 baseVoxel)
        {
            return new GenJobResources
            {
                colHeights = new NativeArray<int>(CHUNK_COLUMNS, Allocator.Persistent),
                colBiomes  = new NativeArray<byte>(CHUNK_COLUMNS, Allocator.Persistent),
                localSt    = ColumnSampler.CullForChunk(in st, baseVoxel, CHUNK_EDGE_VOXELS, Allocator.Persistent),
                caves      = CavesIntersectingChunk(meta, baseVoxel, Allocator.Persistent),
                biomeTable = BuildBiomeTable(Allocator.Persistent),
                denseOut   = new NativeArray<int>(1, Allocator.Persistent),
            };
        }

        private static ColumnSampleJob MakeColumnJob(in GenJobResources r, int3 baseVoxel) =>
            new ColumnSampleJob
            {
                st = r.localSt,
                baseVoxelX = baseVoxel.x,
                baseVoxelZ = baseVoxel.z,
                edge = CHUNK_EDGE_VOXELS,
                heights = r.colHeights,
                biomes = r.colBiomes,
            };

        private static ChunkFillJob MakeFillJob(in GenJobResources r, int3 baseVoxel,
                                                in GeneratedChunk gen) =>
            new ChunkFillJob
            {
                colHeights = r.colHeights,
                colBiomes = r.colBiomes,
                caves = r.caves,
                biomes = r.biomeTable,
                baseVoxel = baseVoxel,
                handles = gen.handles,
                bodies = gen.bodies,
                denseCount = r.denseOut,
            };

        /// WORKER-THREAD path. .Run() executes the Burst code on the calling
        /// thread, which is the only option off the main thread.
        private static void FillNative(in ColumnSampler.State st, WorldMetaData meta,
                                       int3 chunkCoord, ref GeneratedChunk gen)
        {
            int3 baseVoxel = chunkCoord * CHUNK_EDGE_VOXELS;
            GenJobResources r = BuildResources(in st, meta, baseVoxel);
            try
            {
                MakeColumnJob(in r, baseVoxel).Run();
                MakeFillJob(in r, baseVoxel, in gen).Run();
                gen.denseCount = r.denseOut[0];
            }
            finally { r.Dispose(); }
        }

        /// MAIN-THREAD path. Schedules the same two jobs, in the same order,
        /// against the same inputs, and returns without waiting.
        ///
        /// The caller owns `res` and must Dispose it AFTER completing the
        /// returned handle, and must read gen.denseCount from res.denseOut[0]
        /// at that point -- a job cannot write it back into the struct.
        public static JobHandle ScheduleChunkNative(in ColumnSampler.State st, WorldMetaData meta,
                                                    int3 chunkCoord, ref GeneratedChunk gen,
                                                    out GenJobResources res)
        {
            int3 baseVoxel = chunkCoord * CHUNK_EDGE_VOXELS;
            res = BuildResources(in st, meta, baseVoxel);
            JobHandle h = MakeColumnJob(in res, baseVoxel).Schedule();
            return MakeFillJob(in res, baseVoxel, in gen).Schedule(h);
        }

        /// Fills a GeneratedChunk and stops there -- no managed Chunk, no pool.
        ///
        /// STAGE 5a: the downsample's tier-0 gather needs the chunk in NATIVE
        /// form so it can be chained behind the fill job as a JobHandle
        /// dependency. Previously the only way to get a chunk out of here was
        /// already-converted and managed, which is why the design doc's claim
        /// that "DownsampleStepJob follows trivially" was wrong.
        ///
        /// Caller owns `gen` and disposes it.
        public static void GenerateChunkNative(in ColumnSampler.State st, WorldMetaData meta,
            int3 chunkCoord, ref GeneratedChunk gen)
        {
            long _tCallStart = System.Diagnostics.Stopwatch.GetTimestamp();
            FillNative(in st, meta, chunkCoord, ref gen);
            TotalPhaseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tCallStart;
        }

        public static void GenerateChunkFull(in ColumnSampler.State st, WorldMetaData meta,
            int3 chunkCoord, Chunk chunk, ChunkHandleAllocator allocator, BrickDataPool pool,
            object allocLock = null)
        {
            long _tCallStart = System.Diagnostics.Stopwatch.GetTimestamp();

            // STAGE 3: generation fills a blittable GeneratedChunk; the managed
            // Chunk is materialised from it at the end. The brick loop below no
            // longer touches Chunk.bricks or BrickDataPool at all, which is what
            // makes it eligible to become a job in Stage 4.
            //
            // The allocLock now covers only the transfer, not the whole fill --
            // the fill has no shared state left to protect.
            var gen = GeneratedChunk.Create(Allocator.Persistent);
            try
            {
                FillNative(in st, meta, chunkCoord, ref gen);
            // Materialise into the managed Chunk. This is the only place the
            // shared pool and handle allocator are touched (§0.1.5).
            bool ok;
            if (allocLock != null) { lock (allocLock) { ok = GeneratedChunkConverter.TryToChunk(in gen, chunkCoord, chunk, allocator, pool); } }
            else ok = GeneratedChunkConverter.TryToChunk(in gen, chunkCoord, chunk, allocator, pool);

            if (!ok)
                throw new InvalidOperationException(
                    $"BrickDataPool could not fit chunk {chunkCoord}: needs {gen.denseCount} " +
                    $"dense bricks, {pool.FreeCount} free. Same condition Alloc() threw on " +
                    $"before Stage 3, refused up front instead of part-way through.");
            }
            finally { gen.Dispose(); }

            TotalPhaseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - _tCallStart;
        }



        /// Caves whose conservative AABB touches this chunk, in a NativeArray so
        /// the fill job can read them. Almost always empty. Caller disposes.
        private static NativeArray<FeatureAnchor> CavesIntersectingChunk(
            WorldMetaData meta, int3 baseVoxel, Allocator alloc)
        {
            int count = 0;
            foreach (var a in meta.anchors)
                if (a.kind == FeatureKind.Cave && CaveTouchesChunk(in a, baseVoxel)) count++;

            var result = new NativeArray<FeatureAnchor>(count, alloc);
            int w = 0;
            foreach (var a in meta.anchors)
                if (a.kind == FeatureKind.Cave && CaveTouchesChunk(in a, baseVoxel)) result[w++] = a;
            return result;
        }

        /// The material-only slice of Biomes.Table, blittable so the fill job can
        /// index it. BiomeDefinition itself carries a string name. Caller disposes.
        public static NativeArray<BiomeMaterials> BuildBiomeTable(Allocator alloc)
        {
            var table = new NativeArray<BiomeMaterials>(Biomes.Table.Length, alloc);
            for (int i = 0; i < Biomes.Table.Length; i++)
            {
                var b = Biomes.Table[i];
                table[i] = new BiomeMaterials
                {
                    surfaceMaterial = b.surfaceMaterial,
                    bulkMaterial = b.bulkMaterial,
                    deepMaterial = b.deepMaterial,
                };
            }
            return table;
        }

        private static bool CaveTouchesChunk(in FeatureAnchor a, int3 baseVoxel)
        {
            FeatureCarve.CaveAabb(in a, out float3 mn, out float3 mx);
            return mx.x > baseVoxel.x && mn.x < baseVoxel.x + 128
                && mx.y > baseVoxel.y && mn.y < baseVoxel.y + 128
                && mx.z > baseVoxel.z && mn.z < baseVoxel.z + 128;
        }

    }

    // Logical-content hasher: FNV-1a over what the voxels ARE (uniform flag +
    // material, or the 512 dense bytes), in brick order — NOT over pool
    // indices, which legitimately differ between allocation orders. This is
    // the "hash of the output" the §13 Phase 3 determinism assertion calls for.
    public static class ChunkContentHash
    {
        /// CONTENT-canonical: hashes the effective voxel bytes and nothing
        /// else, so representation (chunk-uniform vs brick-uniform vs dense)
        /// never affects the result. The previous form mixed per-brick
        /// dense/uniform flags, which made the hash change when the coalescer
        /// (legally, §4.5) collapsed a dense brick -- Gate C's leave/return
        /// identity check then failed nondeterministically depending on how far
        /// the coalescer's round-robin cursor had gotten before the hash was
        /// taken. Not corruption; a representation-sensitive oracle.
        ///
        /// For a uniform run of the same byte, FNV-1a folding is a pure
        /// function of (h, b, count), so the per-brick uniform case uses a
        /// small fold loop of the SAME operation the dense case applies --
        /// identical math, just without needing a body to read.
        public static uint Hash(Chunk chunk, BrickDataPool pool)
        {
            uint h = 2166136261u;
            void Mix(byte b) { h ^= b; h *= 16777619u; }
            void MixRepeated(byte b, int count) { for (int n = 0; n < count; n++) { h ^= b; h *= 16777619u; } }

            if (chunk.isUniform || chunk.bricks == null)
            {
                MixRepeated(chunk.uniformMaterial, 4096 * 512);
                return h;
            }

            var raw = pool.RawData;
            for (int i = 0; i < 4096; i++)
            {
                uint data = chunk.bricks[i].data;
                if ((data & 0x80000000) == 0)
                {
                    MixRepeated((byte)(data & 0xFF), 512);
                }
                else
                {
                    int start = (int)(data & 0x3FFFFFFF) * 512;
                    for (int v = 0; v < 512; v++) Mix(raw[start + v]);
                }
            }
            return h;
        }
    }
}