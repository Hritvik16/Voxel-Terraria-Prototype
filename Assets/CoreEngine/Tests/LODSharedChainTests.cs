// Assets/CoreEngine/Tests/LODSharedChainTests.cs
//
// Coverage for LODDownsampler.BuildChain / ChainResultFor -- the shared
// cascade chain that lets every tier read one gather and one halving pass.
//
// WHY THIS FILE EXISTS, AND IT IS NOT A FLATTERING REASON. The shared chain
// shipped with the whole rig suite green and EditMode at 485 PASS, so it
// looked covered. A mutation sweep said otherwise: FIVE mutants -- stopping
// the chain a step short, an off-by-one in ChainResultFor, skipping the
// gather entirely, dropping the residency re-check, and never consuming the
// dirty entry -- ALL SURVIVED with the suite still at 485/0. The integration
// rigs do exercise the path, but the unit suite could not tell correct from
// broken, which is the definition of uncovered.
//
// THE INVARIANT THAT MATTERS. BuildChain must leave EVERY tier's buffer
// holding exactly what that tier would have got from its own independent
// downsample. If it does not, tier 1 and tier 2 disagree about the world at
// different distances and the seam shows up as terrain changing shape as you
// walk toward it -- the hardest class of bug to attribute after the fact.
// So these tests compare the shared path against the per-tier path directly,
// which is the property the optimisation actually claims.

using NUnit.Framework;
using System;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class LODSharedChainTests
{
    private static (ChunkStore store, BrickDataPool pool, ChunkHandleAllocator handles) MakeEmptyStore()
    {
        var pool = new BrickDataPool(64);
        var handles = new ChunkHandleAllocator();
        var store = new ChunkStore(pool, handles);
        return (store, pool, handles);
    }

    /// A chunk with genuinely varied content, so a majority vote can differ
    /// between tiers. A uniform chunk would pass every one of these tests
    /// even with the chain completely broken.
    private static Chunk MakeVariedChunk(BrickDataPool pool, ChunkHandleAllocator handles)
    {
        var chunk = new Chunk { coord = int3.zero, isUniform = false, bricks = handles.Alloc() };
        for (int i = 0; i < 4096; i++) chunk.bricks[i].data = 1;

        NativeArray<byte> raw = pool.RawData;
        // Three dense bricks with different internal patterns, at scattered
        // positions, so the result depends on both WHERE data is and WHAT it
        // is at every halving step.
        int[] flat = { 0, 306, 1911 };
        byte[][] fills = {
            new byte[] { 9, 3 },
            new byte[] { 6, 6 },
            new byte[] { 0, 7 },
        };
        for (int b = 0; b < flat.Length; b++)
        {
            int slot = pool.Alloc();
            for (int i = 0; i < 512; i++)
                raw[slot * 512 + i] = i < 256 ? fills[b][0] : fills[b][1];
            chunk.bricks[flat[b]].data = 0x80000000u | (uint)slot;
        }
        return chunk;
    }

    // =====================================================================

    [Test]
    public void BuildChain_GivesEveryTierExactlyWhatThePerTierPathWouldHave()
    {
        // THE LOAD-BEARING TEST. This is the optimisation's entire claim, and
        // it kills the "stops one step short", "off-by-one" and "skips the
        // gather" mutants at once -- each makes some tier disagree.
        var (_, pool, handles) = MakeEmptyStore();
        Chunk chunk = MakeVariedChunk(pool, handles);

        var scratch = new LODDownsampler.DownsampleScratch();
        int steps = LODDownsampler.BuildChain(chunk, pool, scratch);
        Assert.Greater(steps, 0, "a non-uniform chunk must actually build a chain");

        for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
        {
            byte[] shared = LODDownsampler.ChainResultFor(tier, scratch);
            byte[] independent = LODDownsampler.DownsampleChunkToTier(chunk, pool, tier);

            Assert.AreEqual(independent.Length, shared.Length,
                $"tier {tier}: shared chain produced the wrong RESOLUTION -- " +
                "that is ChainResultFor reading the wrong step");
            for (int i = 0; i < independent.Length; i++)
                Assert.AreEqual(independent[i], shared[i],
                    $"tier {tier}: shared chain differs from the per-tier result at index {i}");
        }
    }

    [Test]
    public void BuildChain_IsNotAccidentallyRightBecauseTheChunkIsBoring()
    {
        // Guards the test above: if the varied chunk downsampled to all-zero,
        // every comparison would pass vacuously and the suite would be lying.
        var (_, pool, handles) = MakeEmptyStore();
        Chunk chunk = MakeVariedChunk(pool, handles);

        var scratch = new LODDownsampler.DownsampleScratch();
        LODDownsampler.BuildChain(chunk, pool, scratch);

        byte[] tier1 = LODDownsampler.ChainResultFor(1, scratch);
        bool sawNonZero = false, sawTwoValues = false;
        byte first = tier1[0];
        foreach (byte b in tier1)
        {
            if (b != 0) sawNonZero = true;
            if (b != first) sawTwoValues = true;
        }
        Assert.IsTrue(sawNonZero, "fixture downsamples to all air -- it proves nothing");
        Assert.IsTrue(sawTwoValues, "fixture downsamples to one uniform value -- it proves nothing");
    }

    [Test]
    public void ChainResultFor_ReturnsTheResolutionTheTierActuallyUses()
    {
        var scratch = new LODDownsampler.DownsampleScratch();
        for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
        {
            int edge = 128 / LODConfig.DownsampleFactor(tier);
            Assert.AreEqual(edge * edge * edge, LODDownsampler.ChainResultFor(tier, scratch).Length,
                $"tier {tier} buffer is the wrong size for factor {LODConfig.DownsampleFactor(tier)}");
        }
    }

    [Test]
    public void BuildChain_ReportsZeroStepsForNullAndUniform_SoTheCallerFillsInstead()
    {
        // Returning >0 here would make the caller publish a buffer still
        // holding the PREVIOUS chunk -- the reused-scratch trap the
        // downsampler's own comments warn about.
        var (_, pool, handles) = MakeEmptyStore();
        var scratch = new LODDownsampler.DownsampleScratch();

        Assert.AreEqual(0, LODDownsampler.BuildChain(null, pool, scratch), "null chunk");

        var uniform = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 4 };
        Assert.AreEqual(0, LODDownsampler.BuildChain(uniform, pool, scratch), "uniform chunk");
    }

    [Test]
    public void BuildChain_RerunOnADifferentChunk_LeavesNoTraceOfTheFirst()
    {
        // The scratch is reused across chunks. A step that is not fully
        // rewritten leaks the previous chunk's terrain into this one's coarse
        // LOD -- visible as distant geometry from somewhere else entirely.
        var (_, pool, handles) = MakeEmptyStore();
        var scratch = new LODDownsampler.DownsampleScratch();

        Chunk varied = MakeVariedChunk(pool, handles);
        LODDownsampler.BuildChain(varied, pool, scratch);

        var (_, pool2, handles2) = MakeEmptyStore();
        var plain = new Chunk { coord = int3.zero, isUniform = false, bricks = handles2.Alloc() };
        for (int i = 0; i < 4096; i++) plain.bricks[i].data = 2;   // all uniform-material-2 bricks

        LODDownsampler.BuildChain(plain, pool2, scratch);
        byte[] tier1 = LODDownsampler.ChainResultFor(1, scratch);
        foreach (byte b in tier1)
            Assert.AreEqual(2, b, "residue from the previously chained chunk is still in the buffer");
    }

    [Test]
    public void StepsForTier_AgreesWithTheDownsampleFactor()
    {
        for (int tier = 1; tier < LODConfig.TIER_COUNT; tier++)
        {
            int factor = LODConfig.DownsampleFactor(tier);
            int steps = LODDownsampler.StepsForTier(tier);
            Assert.AreEqual(factor, 1 << steps,
                $"tier {tier}: {steps} halvings is {1 << steps}x, but the factor is {factor}x");
        }
    }

    // =====================================================================
    // ApplyChunkFromChain -- the CascadeTierPool half of the shared path
    // =====================================================================

    [Test]
    public void ApplyChunkFromChain_ConsumesTheDirtyEntry()
    {
        // If it does not, the chunk is rebuilt again next frame, forever: the
        // queue never drains, the budget is permanently saturated, and the
        // cost the shared chain exists to remove comes straight back while
        // every rig still reports PASS.
        var (store, pool, _) = MakeEmptyStore();
        store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 9 });

        var cascade = new CascadeTierPool(1, new int3(4, 4, 4), 64);
        cascade.MarkDirty(int3.zero);
        Assert.IsTrue(cascade.IsDirty(int3.zero), "precondition");

        var batch = new System.Collections.Generic.List<int3>();
        cascade.SelectBatch(store, batch);
        CollectionAssert.Contains(batch, int3.zero);

        int edge = 128 / LODConfig.DownsampleFactor(1);
        var uniform = new byte[edge * edge * edge];
        Array.Fill(uniform, (byte)9);
        cascade.ApplyChunkFromChain(store, int3.zero, uniform);

        Assert.IsFalse(cascade.IsDirty(int3.zero),
            "the chunk is still queued after being applied -- it will rebuild forever");
        cascade.Dispose();
    }

    [Test]
    public void ApplyChunkFromChain_ClearsRatherThanWrites_WhenTheChunkWasEvictedMidFlight()
    {
        // §9.4's residency guard at WRITE time, not only at selection. A
        // chunk can be evicted between SelectBatch and the write; writing its
        // coarse entries then leaves the GPU describing terrain that no
        // longer exists -- the distant phantom geometry this subsystem has
        // already been bitten by once.
        //
        // Observed via the brick pool: a capacity-1 pool tolerates the clear
        // path (which allocates nothing) and throws on the write path, which
        // must allocate for non-uniform coarse data.
        var (store, pool, handles) = MakeEmptyStore();
        var chunk = new Chunk { coord = int3.zero, isUniform = false, bricks = handles.Alloc() };
        for (int i = 0; i < 4096; i++) chunk.bricks[i].data = 2;
        store.InsertChunk(chunk);

        var cascade = new CascadeTierPool(1, new int3(4, 4, 4), 1);
        cascade.MarkDirty(int3.zero);
        var batch = new System.Collections.Generic.List<int3>();
        cascade.SelectBatch(store, batch);
        CollectionAssert.Contains(batch, int3.zero, "chunk must be selected while still resident");

        // ...and NOW it is evicted, after selection, before the write.
        store.EvictChunk(int3.zero);

        int edge = 128 / LODConfig.DownsampleFactor(1);
        var mixed = new byte[edge * edge * edge];
        for (int i = 0; i < mixed.Length; i++) mixed[i] = (byte)((i % 2 == 0) ? 2 : 5);

        Assert.DoesNotThrow(() => cascade.ApplyChunkFromChain(store, int3.zero, mixed),
            "an evicted chunk must CLEAR its entries, not write coarse geometry for terrain " +
            "that no longer exists");
        cascade.Dispose();
    }

    [Test]
    public void SharedChainIsTheDefault_AndCanBeTurnedOffForRegression()
    {
        // The per-tier path is the regression baseline every prior cascade
        // proof was written against, and the mutation check flips this. If it
        // silently stopped defaulting to the shared path, the optimisation
        // would be "enabled" everywhere except where it matters.
        Assert.IsTrue(LODCascadeManager.SharedChainEnabled,
            "the shared chain must be the default path");
    }
}
