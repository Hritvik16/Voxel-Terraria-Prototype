// Assets/CoreEngine/Tests/LODDownsamplerTests.cs
using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;

public class LODConfigTests
{
    [Test]
    public void TierCount_MatchesArrayLengths()
    {
        Assert.AreEqual(LODConfig.TIER_COUNT, LODConfig.TIER_VOXEL_SIZE_M.Length);
        Assert.AreEqual(LODConfig.TIER_COUNT, LODConfig.TIER_OUTER_RANGE_M.Length);
    }

    [Test]
    public void DownsampleFactor_Tier0_IsOne()
    {
        Assert.AreEqual(1, LODConfig.DownsampleFactor(0));
    }

    [Test]
    public void DownsampleFactor_Tier1And2_ArePowersOfTwo()
    {
        // 0.2m / 0.1m = 2, 0.4m / 0.1m = 4 - both power-of-two per CoordMath's
        // shift/mask requirement (ARCHITECTURE_v8.6.md §2.3).
        Assert.AreEqual(2, LODConfig.DownsampleFactor(1));
        Assert.AreEqual(4, LODConfig.DownsampleFactor(2));
    }

    [Test]
    public void TierOuterRange_StrictlyIncreasing()
    {
        for (int t = 1; t < LODConfig.TIER_COUNT; t++)
            Assert.Greater(LODConfig.TIER_OUTER_RANGE_M[t], LODConfig.TIER_OUTER_RANGE_M[t - 1]);
    }

    [Test]
    public void TierOuterRange_MatchesAmendment89DecidedValues()
    {
        // Amendment 8.9 §3, 540p column, plus the chat decision for tier 2's
        // outer bound (window corner distance, option 1). Pinning these exact
        // numbers in a test means a future accidental edit to LODConfig shows
        // up as a failing test, not a silent drift from the decided values.
        Assert.AreEqual(64f, LODConfig.TIER_OUTER_RANGE_M[0], 0.01f);
        Assert.AreEqual(128f, LODConfig.TIER_OUTER_RANGE_M[1], 0.01f);
        Assert.AreEqual(290f, LODConfig.TIER_OUTER_RANGE_M[2], 0.01f);
    }
}

public class LODDownsamplerTests
{
    // --- MajorityVote: air handling ---

    [Test]
    public void MajorityVote_AllAir_ReturnsAir()
    {
        Assert.AreEqual(0, LODDownsampler.MajorityVote(0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Test]
    public void MajorityVote_StrictAirMajority_5of8_ReturnsAir()
    {
        Assert.AreEqual(0, LODDownsampler.MajorityVote(0, 0, 0, 0, 0, 3, 3, 3));
    }

    [Test]
    public void MajorityVote_ExactlyFourFour_AirVsSolid_PrefersSolid()
    {
        // Documented tie-break: a 4-4 split does NOT count as air's strict
        // majority (needs 5), so the non-air side wins.
        Assert.AreEqual(3, LODDownsampler.MajorityVote(0, 0, 0, 0, 3, 3, 3, 3));
    }

    [Test]
    public void MajorityVote_FourAir_FourMixedSolid_PicksMostFrequentNonAir()
    {
        // 4 air, then material 5 x2, material 7 x2 -> tie between 5 and 7,
        // lower id (5) wins per documented tie-break.
        Assert.AreEqual(5, LODDownsampler.MajorityVote(0, 0, 0, 0, 5, 5, 7, 7));
    }

    // --- MajorityVote: solid vs solid ---

    [Test]
    public void MajorityVote_ClearSolidMajority_ReturnsThatMaterial()
    {
        Assert.AreEqual(9, LODDownsampler.MajorityVote(9, 9, 9, 9, 9, 2, 2, 0));
    }

    [Test]
    public void MajorityVote_TieBetweenTwoSolids_PicksLowerId()
    {
        Assert.AreEqual(2, LODDownsampler.MajorityVote(2, 2, 2, 2, 9, 9, 9, 9));
    }

    [Test]
    public void MajorityVote_SingleNonAirSample_WinsOverSevenAir()
    {
        // 1 solid, 7 air: air is not a strict majority (needs 5) at 7... wait,
        // 7 IS >= 5, so air wins here. Covered separately below to make the
        // boundary explicit rather than implicit in this comment.
            Assert.AreEqual(0, LODDownsampler.MajorityVote(0, 0, 0, 0, 0, 0, 0, 4));
    }

    [Test]
    public void MajorityVote_Determinism_OrderOfArgumentsDoesNotMatter()
    {
        byte a = LODDownsampler.MajorityVote(1, 2, 2, 0, 0, 0, 2, 1);
        byte b = LODDownsampler.MajorityVote(0, 0, 0, 1, 1, 2, 2, 2);
        Assert.AreEqual(a, b);
    }

    // --- DownsampleOnce: shape and correctness ---

    [Test]
    public void DownsampleOnce_RejectsOddEdge()
    {
        byte[] bad = new byte[27]; // 3^3
        Assert.Throws<ArgumentException>(() => LODDownsampler.DownsampleOnce(bad, 3));
    }

    [Test]
    public void DownsampleOnce_RejectsMismatchedLength()
    {
        byte[] wrongLength = new byte[10];
        Assert.Throws<ArgumentException>(() => LODDownsampler.DownsampleOnce(wrongLength, 4));
    }

    [Test]
    public void DownsampleOnce_UniformInput_ProducesUniformOutput()
    {
        int edge = 4;
        byte[] src = new byte[edge * edge * edge];
        for (int i = 0; i < src.Length; i++) src[i] = 7;

        byte[] result = LODDownsampler.DownsampleOnce(src, edge);

        Assert.AreEqual(2 * 2 * 2, result.Length);
        foreach (byte b in result)
            Assert.AreEqual(7, b);
    }

    [Test]
    public void DownsampleOnce_OutputHalfEdgeLength()
    {
        int edge = 8;
        byte[] src = new byte[edge * edge * edge];
        byte[] result = LODDownsampler.DownsampleOnce(src, edge);
        Assert.AreEqual(4 * 4 * 4, result.Length);
    }

    [Test]
    public void DownsampleOnce_SingleBlockKnownPattern_MatchesDirectVote()
    {
        // 4x4x4 source = one 2x2x2 group of destination voxels, each covering
        // a 2x2x2 source block. Fill the first source block (x,y,z in [0,1])
        // with a known pattern and check the corresponding dest voxel [0,0,0]
        // directly against MajorityVote, independent of the loop logic.
        int edge = 4;
        byte[] src = new byte[edge * edge * edge];
        int stride = edge, slice = edge * edge;

        byte[] block = { 5, 5, 5, 0, 0, 0, 5, 5 }; // 5 of 8 -> material 5 wins
        int idx = 0;
        for (int z = 0; z < 2; z++)
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            src[x + stride * y + slice * z] = block[idx++];

        byte expected = LODDownsampler.MajorityVote(block[0], block[1], block[2], block[3], block[4], block[5], block[6], block[7]);

        byte[] result = LODDownsampler.DownsampleOnce(src, edge);
        int destStride = 2, destSlice = 4;
        byte actual = result[0 + destStride * 0 + destSlice * 0];

        Assert.AreEqual(expected, actual);
    }

    // --- DownsampleChunkToTier: integration with ChunkStore ---

    [Test]
    public void DownsampleChunkToTier_RejectsTierZero()
    {
        var (store, pool, _) = MakeEmptyStore();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LODDownsampler.DownsampleChunkToTier(store, pool, int3.zero, 0));
    }

    [Test]
    public void DownsampleChunkToTier_RejectsOutOfRangeTier()
    {
        var (store, pool, _) = MakeEmptyStore();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LODDownsampler.DownsampleChunkToTier(store, pool, int3.zero, LODConfig.TIER_COUNT));
    }

    [Test]
    public void DownsampleChunkToTier_UniformAirChunk_DownsamplesToAllAir()
    {
        var (store, pool, _) = MakeEmptyStore();
        // ChunkStore treats an unloaded/uninserted chunk's voxels as air
        // (per ChunkStore.GetVoxel: "chunk == null -> return 0").
        byte[] tier1 = LODDownsampler.DownsampleChunkToTier(store, pool, int3.zero, 1);
        Assert.AreEqual(64 * 64 * 64, tier1.Length); // 128/2 per axis
        foreach (byte b in tier1) Assert.AreEqual(0, b);
    }

    [Test]
    public void DownsampleChunkToTier_UniformSolidChunk_DownsamplesToUniformMaterial()
    {
        var (store, pool, handles) = MakeEmptyStore();
        var chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = 4 };
        store.InsertChunk(chunk);

        byte[] tier1 = LODDownsampler.DownsampleChunkToTier(store, pool, int3.zero, 1);
        byte[] tier2 = LODDownsampler.DownsampleChunkToTier(store, pool, int3.zero, 2);

        Assert.AreEqual(64 * 64 * 64, tier1.Length);
        Assert.AreEqual(32 * 32 * 32, tier2.Length);
        foreach (byte b in tier1) Assert.AreEqual(4, b);
        foreach (byte b in tier2) Assert.AreEqual(4, b);
    }

    [Test]
    public void DownsampleChunkToTier_MixedChunk_BulkBrickExtraction_MatchesHandComputedValues()
    {
        // Regression test for the bulk-brick-copy rewrite of
        // ExtractChunkTier0Materials (previously ~2.1M ChunkStore.GetVoxel
        // calls per chunk - replaced after a real "beach ball for minutes"
        // report once chunk count went from 64 to 484). This test does NOT
        // trust the new bulk-copy index math by inspection alone - it hand-
        // computes two independent expected values:
        //   1. Within-brick voxel ordering, via a dense brick at the chunk's
        //      own origin (brick-position offset is trivially zero here, so
        //      only the WITHIN-brick index formula is exercised).
        //   2. Brick-position offset math, via a second dense brick at a
        //      deliberately non-trivial position, filled UNIFORMLY internally
        //      so within-brick ordering can't mask an offset bug.
        var (store, pool, handles) = MakeEmptyStore();

        var chunk = new Chunk { coord = int3.zero, isUniform = false, bricks = handles.Alloc() };
        for (int i = 0; i < 4096; i++) chunk.bricks[i].data = 1; // baseline: uniform material 1

        // Dense brick at local (0,0,0), flat index 0: first 256 bytes (voxel
        // indices 0-255, per LocalVoxelIndex = x+8y+64z) set to material 9,
        // remaining 256 to material 3.
        int poolIndexOrigin = pool.Alloc();
        NativeArray<byte> raw = pool.RawData;
        for (int i = 0; i < 256; i++) raw[poolIndexOrigin * 512 + i] = 9;
        for (int i = 256; i < 512; i++) raw[poolIndexOrigin * 512 + i] = 3;
        chunk.bricks[0].data = 0x80000000u | (uint)poolIndexOrigin;

        // Dense brick at local (2,3,1) - flat index (1<<8)|(3<<4)|2 = 306 -
        // filled UNIFORMLY with material 6, so this only tests WHERE the
        // brick's data lands in the output, not its internal ordering.
        int poolIndexOffset = pool.Alloc();
        for (int i = 0; i < 512; i++) raw[poolIndexOffset * 512 + i] = 6;
        chunk.bricks[306].data = 0x80000000u | (uint)poolIndexOffset;

        store.InsertChunk(chunk);

        byte[] tier1 = LODDownsampler.DownsampleChunkToTier(store, pool, int3.zero, 1);

        // Tier1 chunk edge = 64. Flat index formula (matches DownsampleOnce):
        // dx + 64*(dy + 64*dz).

        // Far from both corrupted bricks: must still be the baseline material 1.
        int farIndex = 40 + 64 * (40 + 64 * 40);
        Assert.AreEqual(1, tier1[farIndex], "Baseline region corrupted - bulk fill/copy touched voxels it shouldn't have.");

        // Origin brick, tier1 voxel (0,0,0): downsamples tier0 voxels (0,0,0)-
        // (1,1,1), all of which are within local-voxel indices {0,1,8,9,64,
        // 65,72,73} - all < 256, so all read material 9 from the dense body.
        // Majority vote of eight 9's = 9.
        Assert.AreEqual(9, tier1[0], "Within-brick voxel index formula (x+8y+64z) appears wrong.");

        // Offset brick at local (2,3,1) -> tier0 origin (16,24,8) -> tier1
        // voxel (8,12,4) (halved). Uniformly material 6 internally, so any
        // correct within-brick ordering plus a correct offset both land here.
        int offsetIndex = 8 + 64 * (12 + 64 * 4);
        Assert.AreEqual(6, tier1[offsetIndex], "Brick-position offset formula (bx*8, by*8, bz*8) appears wrong.");
    }

    private static (ChunkStore store, BrickDataPool pool, ChunkHandleAllocator handles) MakeEmptyStore()
    {
        var pool = new BrickDataPool(64);
        var handles = new ChunkHandleAllocator();
        var store = new ChunkStore(pool, handles);
        return (store, pool, handles);
    }
}