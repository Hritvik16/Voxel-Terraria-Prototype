// ==========================================
// Assets/CoreEngine/WorldGen/ChunkFillJob.cs
//
// STAGE 4 of the Job System conversion: the brick loop becomes a Burst job.
//
// Stage 3 left this loop reading only blittable state (column heights/biomes in
// NativeArrays) and writing only blittable state (GeneratedChunk's handles and
// inline bodies). The two things still standing between it and Burst were:
//
//   Biomes.Get returns a BiomeDefinition containing a `string name`, which is
//   not blittable. BiomeMaterials below carries only the three material bytes
//   the fill rule actually reads.
//
//   Caves arrived as a managed FeatureAnchor[]. They are now culled into a
//   NativeArray by the caller.
//
// .Run() NOT .Schedule(). Run executes the Burst-compiled Execute on the
// CALLING thread, so the existing raw generation workers keep working exactly
// as they do today and the completion queue is untouched. Schedule() -- with
// main-thread scheduling and JobHandle completion -- is Stage 5, the only stage
// that tests the co-scheduling hypothesis, and is deliberately not attempted
// here.
//
// FLOATMODE IS DEFAULT, NOT STRICT, AND THAT IS DELIBERATE: this job does no
// float arithmetic. Column heights and biome ids arrive precomputed as int/byte
// (ColumnSampleJob already did that math under FloatMode.Strict); the only
// floats here are the cave containment test, which compares squared distances
// against a radius and whose result feeds a boolean. The oracle is what
// confirms this, not the reasoning.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace VoxelEngine.WorldGen
{
    /// The blittable slice of BiomeDefinition the per-voxel rule reads.
    /// BiomeDefinition itself carries a string name and cannot enter a job.
    public struct BiomeMaterials
    {
        public byte surfaceMaterial;
        public byte bulkMaterial;
        public byte deepMaterial;
    }

    [BurstCompile]
    public struct ChunkFillJob : IJob
    {
        [ReadOnly] public NativeArray<int> colHeights;
        [ReadOnly] public NativeArray<byte> colBiomes;
        [ReadOnly] public NativeArray<FeatureAnchor> caves;
        [ReadOnly] public NativeArray<BiomeMaterials> biomes;

        public int3 baseVoxel;

        public NativeArray<uint> handles;
        public NativeArray<byte> bodies;
        /// Single element. A job cannot return a value, and denseCount is the
        /// one piece of state the fill produces besides the arrays themselves.
        public NativeArray<int> denseCount;

        public const int CHUNK_EDGE = 128;
        public const uint DENSE_BIT = 0x80000000u;

        public void Execute()
        {
            const int SEA = WorldGenConstants.SEA_LEVEL_VOXEL_Y;
            const int DEEP = WorldGenConstants.DEEP_STRATUM_TOP_Y;
            const int SURF = WorldGenConstants.SURFACE_STRATUM_THICKNESS;

            int dense = 0;

            for (int bz = 0; bz < 16; bz++)
            for (int bx = 0; bx < 16; bx++)
            {
                int baseVoxelX = baseVoxel.x + (bx * 8);
                int baseVoxelZ = baseVoxel.z + (bz * 8);

                // Footprint summary over this brick column's 64 columns.
                int minH = int.MaxValue, maxH = int.MinValue;
                bool biomesUniform = true;
                byte firstBiome = colBiomes[((bz << 3) + 0) * CHUNK_EDGE + ((bx << 3) + 0)];
                for (int lz = 0; lz < 8; lz++)
                for (int lx = 0; lx < 8; lx++)
                {
                    int g = ((bz << 3) + lz) * CHUNK_EDGE + ((bx << 3) + lx);
                    int h = colHeights[g];
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                    if (colBiomes[g] != firstBiome) biomesUniform = false;
                }

                BiomeMaterials footprintBiome = biomes[firstBiome];

                for (int by = 0; by < 16; by++)
                {
                    int y0 = baseVoxel.y + (by * 8);
                    int y7 = y0 + 7;
                    int brickFlatIndex = (bz << 8) | (by << 4) | bx;

                    bool caveHit = caves.Length > 0 &&
                                   BrickIntersectsAnyCave(caves, baseVoxelX, y0, baseVoxelZ);

                    // ---- Step 2: uniform fill (the sticky-note economy) ----
                    if (y0 > maxH)
                    {
                        if (y0 > SEA)
                            handles[brickFlatIndex] = Materials.Air;
                        else if (y7 <= SEA)
                            handles[brickFlatIndex] = Materials.Water;
                        else
                            FillDense(ref dense, brickFlatIndex, bx, bz, baseVoxelX, y0, baseVoxelZ, caveHit);
                    }
                    else if (y7 <= minH && !caveHit)
                    {
                        if (y7 < DEEP)
                        {
                            if (biomesUniform) handles[brickFlatIndex] = footprintBiome.deepMaterial;
                            else FillDense(ref dense, brickFlatIndex, bx, bz, baseVoxelX, y0, baseVoxelZ, false);
                        }
                        else if (y0 >= DEEP && (minH - y7) >= SURF && biomesUniform)
                        {
                            handles[brickFlatIndex] = footprintBiome.bulkMaterial;
                        }
                        else
                        {
                            FillDense(ref dense, brickFlatIndex, bx, bz, baseVoxelX, y0, baseVoxelZ, false);
                        }
                    }
                    else
                    {
                        // ---- Step 3: dense — surface skin, water/terrain
                        //      interface, or feature intersection ----
                        FillDense(ref dense, brickFlatIndex, bx, bz, baseVoxelX, y0, baseVoxelZ, caveHit);
                    }
                }
            }

            denseCount[0] = dense;
        }

        private void FillDense(ref int dense, int brickFlatIndex, int bx, int bz,
                               int baseVoxelX, int y0, int baseVoxelZ, bool testCaves)
        {
            int bodyIndex = dense++;
            handles[brickFlatIndex] = DENSE_BIT | (uint)bodyIndex;
            int startOffset = bodyIndex * 512;

            for (int vy = 0; vy < 8; vy++)
            {
                int wy = y0 + vy;
                for (int vz = 0; vz < 8; vz++)
                for (int vx = 0; vx < 8; vx++)
                {
                    int g = ((bz << 3) + vz) * CHUNK_EDGE + ((bx << 3) + vx);
                    byte mat = VoxelMaterial(
                        baseVoxelX + vx, wy, baseVoxelZ + vz,
                        colHeights[g], colBiomes[g],
                        caves, testCaves, biomes);
                    bodies[startOffset + ((vz << 6) | (vy << 3) | vx)] = mat;
                }
            }
        }

        /// THE per-voxel material rule, in Burst-safe form. The
        /// uniform-classification predicates above are written to be exactly
        /// conservative w.r.t. this function — if you change one, re-derive the
        /// other. GenerationTests' per-voxel oracle is the tripwire, and it
        /// calls this same implementation.
        public static byte VoxelMaterial(int wx, int wy, int wz, int colHeight, byte biomeId,
            NativeArray<FeatureAnchor> caves, bool testCaves, NativeArray<BiomeMaterials> biomes)
        {
            const int SEA = WorldGenConstants.SEA_LEVEL_VOXEL_Y;

            if (wy <= colHeight)
            {
                if (testCaves && caves.Length > 0)
                {
                    var p = new float3(wx + 0.5f, wy + 0.5f, wz + 0.5f);
                    for (int i = 0; i < caves.Length; i++)
                    {
                        FeatureAnchor a = caves[i];
                        if (FeatureCarve.CaveContains(in a, p))
                            return wy <= SEA ? Materials.Water : Materials.Air;
                    }
                }

                BiomeMaterials biome = biomes[biomeId];
                if (wy < WorldGenConstants.DEEP_STRATUM_TOP_Y) return biome.deepMaterial;
                if (colHeight - wy < WorldGenConstants.SURFACE_STRATUM_THICKNESS) return biome.surfaceMaterial;
                return biome.bulkMaterial;
            }

            return wy <= SEA ? Materials.Water : Materials.Air;
        }

        public static bool BrickIntersectsAnyCave(NativeArray<FeatureAnchor> caves,
                                                  int bx0, int by0, int bz0)
        {
            for (int i = 0; i < caves.Length; i++)
            {
                // Byte-for-byte the original predicate: STRICT inequalities
                // against +8, i.e. an open interval. An earlier draft of this
                // used >= against +7, which disagrees whenever an AABB edge
                // lands exactly on a brick boundary -- a silent terrain change
                // in cave-adjacent bricks.
                FeatureAnchor a = caves[i];
                FeatureCarve.CaveAabb(in a, out float3 mn, out float3 mx);
                if (mx.x > bx0 && mn.x < bx0 + 8
                 && mx.y > by0 && mn.y < by0 + 8
                 && mx.z > bz0 && mn.z < bz0 + 8) return true;
            }
            return false;
        }
    }
}
