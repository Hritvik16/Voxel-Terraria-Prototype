// ==========================================
// Assets/CoreEngine/Mirror/Tier0ExtractJob.cs
//
// STAGE 5a of the Job System conversion: tier-0 gather, in native form.
//
// WHY THIS EXISTS, GIVEN D3 WAS DECLARED NOT WORTH DOING:
//   It was measured at 0.09ms of the 1.98ms downsample and dismissed on COST.
//   That reasoning was about the wrong axis. Stage 5 chains the downsample
//   behind the fill job with a JobHandle, and a job cannot read chunk.bricks --
//   the managed Chunk does not exist until the main thread converts. So the
//   gather has to read the GeneratedChunk the fill job actually produced. This
//   is a STRUCTURAL prerequisite for chaining, not a speedup, and §8 of the
//   design doc missed it.
//
// EXACTLY EQUIVALENT to ExtractChunkTier0MaterialsInto, with one substitution:
// a dense handle's low bits index GeneratedChunk.bodies (bodyIndex * 512)
// rather than a BrickDataPool slot. Everything else -- clear-first, the air
// skip, the region write order -- is preserved deliberately, because the
// oracle compares the result of the whole chain.
//
// The chunk-uniform and null cases are NOT handled here. They are the caller's
// fast path in DownsampleTierFromScratch, exactly as with PrepareTier0, which
// returns false for both rather than gathering.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Mirror
{
    [BurstCompile]
    public struct Tier0ExtractJob : IJob
    {
        [ReadOnly] public NativeArray<uint> handles;
        [ReadOnly] public NativeArray<byte> bodies;

        /// chunkEdgeVoxels^3. Cleared by this job, not by the caller.
        public NativeArray<byte> result;
        public int chunkEdgeVoxels;

        public void Execute()
        {
            for (int i = 0; i < result.Length; i++) result[i] = 0;

            const int bricksPerChunkEdge = 16;
            const int brickEdgeVoxels = 8;
            int stride = chunkEdgeVoxels;
            int slice = chunkEdgeVoxels * chunkEdgeVoxels;

            for (int bz = 0; bz < bricksPerChunkEdge; bz++)
            for (int by = 0; by < bricksPerChunkEdge; by++)
            for (int bx = 0; bx < bricksPerChunkEdge; bx++)
            {
                int brickFlatIndex = CoordMath.LocalBrickIndex(new int3(bx, by, bz));
                uint handleData = handles[brickFlatIndex];
                bool isDense = (handleData & 0x80000000u) != 0;

                int ox = bx * brickEdgeVoxels;
                int oy = by * brickEdgeVoxels;
                int oz = bz * brickEdgeVoxels;

                if (!isDense)
                {
                    byte m = (byte)(handleData & 0xFF);
                    if (m == 0) continue;   // air: already zero
                    for (int z = 0; z < brickEdgeVoxels; z++)
                    for (int y = 0; y < brickEdgeVoxels; y++)
                    {
                        int rowStart = ox + stride * (oy + y) + slice * (oz + z);
                        for (int x = 0; x < brickEdgeVoxels; x++) result[rowStart + x] = m;
                    }
                }
                else
                {
                    int srcOffset = (int)(handleData & 0x3FFFFFFFu) * 512;
                    int srcIdx = 0;
                    for (int z = 0; z < brickEdgeVoxels; z++)
                    for (int y = 0; y < brickEdgeVoxels; y++)
                    {
                        int rowStart = ox + stride * (oy + y) + slice * (oz + z);
                        for (int x = 0; x < brickEdgeVoxels; x++)
                            result[rowStart + x] = bodies[srcOffset + srcIdx++];
                    }
                }
            }
        }
    }
}
