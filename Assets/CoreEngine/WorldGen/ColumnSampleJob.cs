// ==========================================
// Assets/CoreEngine/WorldGen/ColumnSampleJob.cs
//
// Burst-compiled column sampling. This is the payoff for porting
// ColumnSampler.State to native containers: the whole call chain
//
//     SampleColumn -> SampleHeightInternal -> FeatureCarve.HeightDelta
//                  -> SampleBiome
//
// now touches only blittable data, which is what Burst requires.
//
// WHY IT IS WORTH COMPILING: measured, the column sampling is 5.16ms of the
// 5.58ms it costs to generate one chunk -- 92.6%. The remaining 7.4% is the
// voxel fill that writes Chunk.bricks and BrickDataPool, which cannot be Burst
// compiled without the memory-layout changes §0.3 puts behind review. So the
// expensive half is exactly the half that is reachable without touching
// anything protected.
//
// .Run() RATHER THAN .Schedule(): Run executes the job immediately on the
// CALLING thread with the Burst-compiled code, so this works from the existing
// raw generation worker threads and needs no change to how work is scheduled or
// handed back. Schedule() would require main-thread scheduling and a
// JobHandle-based completion path -- i.e. redesigning the worker/queue handoff,
// which is deliberately out of scope here.
//
// FLOATMODE.STRICT IS NOT OPTIONAL. Burst's default float mode permits
// reassociation and FMA contraction, which can change results in the last bits.
// Generation determinism is on §0.3's review list, and a world that generates
// differently under Burst than under Mono would mean saved worlds disagreeing
// with regenerated baselines -- the exact undiagnosable class this project
// exists to avoid. Strict keeps IEEE semantics; the benchmark that uses this
// job asserts Burst and Mono produce byte-identical output rather than assuming
// they do.
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace VoxelEngine.WorldGen
{
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High)]
    public struct ColumnSampleJob : IJob
    {
        [ReadOnly] public ColumnSampler.State st;

        public int baseVoxelX, baseVoxelZ;
        public int edge;                       // columns per side (128 for a chunk)

        [WriteOnly] public NativeArray<int> heights;
        [WriteOnly] public NativeArray<byte> biomes;

        public void Execute()
        {
            for (int lz = 0; lz < edge; lz++)
            for (int lx = 0; lx < edge; lx++)
            {
                ColumnSampler.SampleColumn(in st, baseVoxelX + lx, baseVoxelZ + lz,
                    out int h, out byte b);
                int idx = lz * edge + lx;
                heights[idx] = h;
                biomes[idx] = b;
            }
        }
    }
}
