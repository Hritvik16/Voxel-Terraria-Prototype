// ==========================================
// Assets/CoreEngine/Tests/ScheduledPathEquivalenceTests.cs
//
// STAGE 5b: the bit-identical gate for the SCHEDULED path.
//
// GenerationOracleTests covers GenerateChunkFull, which is the .Run() path the
// delta-bearing workers still take. It says nothing about the scheduled path,
// and after the hybrid dispatch a chunk takes one route or the other purely on
// whether it happens to have a delta file. If the two routes disagreed by even
// one voxel, the world would change depending on whether the player had edited
// a chunk -- and the oracle would stay green while it happened.
//
// So both halves are compared directly, on the same coords the oracle uses:
//   1. chunk content, via ChunkContentHash
//   2. every cascade tier, byte for byte
//
// The downsample comparison is the one that would catch the chain restructure:
// the scheduled chain descends ONCE and copies each tier out on the way down,
// where the Run path restarts from tier 0 for every tier. Those are the same
// halvings in the same order, but "should be identical" is exactly the kind of
// claim this project has been burned by, so it is measured rather than argued.
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.WorldGen;

public class ScheduledPathEquivalenceTests
{
    private const uint SEED = 42;

    private static readonly (byte sizeClass, int3 coord, string note)[] Cases =
    {
        (0, new int3(11, 0, 11),   "sc0 island centre"),
        (0, new int3(0, 0, 0),     "sc0 ocean corner"),
        (1, new int3(100, 0, 100), "sc1 island centre"),
        (1, new int3(95, 0, 103),  "sc1 inland"),
        (1, new int3(0, 0, 0),     "sc1 far ocean"),
    };

    [Test]
    public void ScheduledGeneration_MatchesRunPath_BitForBit()
    {
        foreach (var (sizeClass, coord, note) in Cases)
        {
            WorldMetaData meta = AnchorPlanner.Plan(SEED, sizeClass);

            var poolRun = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            var poolJob = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            try
            {
                var runChunk = new Chunk();
                ChunkGeneratorFull.GenerateChunkFull(
                    meta, coord, runChunk, new ChunkHandleAllocator(2), poolRun);
                uint runHash = ChunkContentHash.Hash(runChunk, poolRun);

                using var st = ColumnSampler.CreateState(meta);
                var gen = GeneratedChunk.Create(Allocator.Persistent);
                try
                {
                    var handle = ChunkGeneratorFull.ScheduleChunkNative(
                        in st, meta, coord, ref gen, out var res);
                    handle.Complete();
                    gen.denseCount = res.denseOut[0];
                    res.Dispose();

                    var jobChunk = new Chunk();
                    Assert.IsTrue(
                        GeneratedChunkConverter.TryToChunk(
                            in gen, coord, jobChunk, new ChunkHandleAllocator(2), poolJob),
                        $"TryToChunk refused a chunk the pool should fit ({note})");

                    uint jobHash = ChunkContentHash.Hash(jobChunk, poolJob);
                    Assert.AreEqual(runHash, jobHash,
                        $"Scheduled generation disagrees with the .Run() path for {note} {coord}: " +
                        $"0x{runHash:X8} vs 0x{jobHash:X8}. A chunk would generate differently " +
                        $"depending on whether it had a delta file.");
                }
                finally { gen.Dispose(); }
            }
            finally { poolRun.Dispose(); poolJob.Dispose(); }
        }
    }

    [Test]
    public void ScheduledDownsample_MatchesRunPath_EveryTier()
    {
        foreach (var (sizeClass, coord, note) in Cases)
        {
            WorldMetaData meta = AnchorPlanner.Plan(SEED, sizeClass);
            var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
            var runScratch = new LODDownsampler.DownsampleScratch();
            var jobScratch = new LODDownsampler.DownsampleScratch();
            try
            {
                var chunk = new Chunk();
                ChunkGeneratorFull.GenerateChunkFull(
                    meta, coord, chunk, new ChunkHandleAllocator(2), pool);

                // Run path, exactly as the worker performs it.
                bool tier0Ready = LODDownsampler.PrepareTier0(chunk, pool, runScratch);
                var expected = new byte[LODConfig.TIER_COUNT - 1][];
                for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                {
                    var r = LODDownsampler.DownsampleTierFromScratch(chunk, tier, runScratch, tier0Ready);
                    expected[tier - 1] = new byte[r.Length];
                    r.CopyTo(expected[tier - 1]);
                }

                // Scheduled path, from the native chunk.
                var gen = GeneratedChunkConverter.FromChunk(chunk, pool, Allocator.Persistent);
                try
                {
                    LODDownsampler.ScheduleAllTiersFromNative(in gen, jobScratch, default).Complete();

                    for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
                    {
                        var actual = jobScratch.TierOut[tier - 1];
                        var exp = expected[tier - 1];
                        Assert.AreEqual(exp.Length, actual.Length,
                            $"tier {tier} length differs for {note}");

                        for (int i = 0; i < exp.Length; i++)
                        {
                            if (exp[i] == actual[i]) continue;
                            Assert.Fail(
                                $"Scheduled downsample differs from .Run() at tier {tier}, " +
                                $"voxel {i}, for {note} {coord}: expected {exp[i]} got {actual[i]}.");
                        }
                    }
                }
                finally { gen.Dispose(); }
            }
            finally { pool.Dispose(); runScratch.Dispose(); jobScratch.Dispose(); }
        }
    }
}
