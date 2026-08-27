// ==========================================
// Assets/CoreEngine/Tests/StreamingTests.cs
//
// Phase 4a: everything provable on the CPU, before anything is drawn -- the
// same 3a/3b ordering that made Phase 3 diagnosable.
//
// Coverage:
//   1. §4.4's transition table, all 16 state pairs, including every forbidden one
//   2. D.1 delta round-trip, corruption, truncation, wrong-coord/seed
//   3. §4.5 eviction returns handle arrays AND dense bricks (the leak §13 names)
//   4. Toroidal ring aliasing: a coord outside the window must never read a
//      resident chunk's data
using System;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class StreamingTests
{
    // =====================================================================
    // 1. Lifetime state machine (§4.4)
    // =====================================================================

    [Test]
    public void TransitionTable_HasExactlyFiveLegalPairs()
    {
        int legal = 0;
        foreach (ChunkState from in Enum.GetValues(typeof(ChunkState)))
        foreach (ChunkState to in Enum.GetValues(typeof(ChunkState)))
            if (ChunkLifecycle.Classify(from, to, true) == TransitionVerdict.Legal) legal++;

        Assert.AreEqual(5, legal, "§4.4's table lists exactly five legal transitions.");
    }

    [TestCase(ChunkState.Unloaded, ChunkState.Loading)]
    [TestCase(ChunkState.Loading, ChunkState.Resident)]
    [TestCase(ChunkState.Resident, ChunkState.Saving)]
    [TestCase(ChunkState.Resident, ChunkState.Unloaded)]
    [TestCase(ChunkState.Saving, ChunkState.Unloaded)]
    public void LegalTransitions_AreAccepted(ChunkState from, ChunkState to)
        => Assert.AreEqual(TransitionVerdict.Legal, ChunkLifecycle.Classify(from, to, true));

    [Test]
    public void LoadingToUnloaded_IsForbidden()
        => Assert.AreEqual(TransitionVerdict.ForbiddenAbandonLoad,
            ChunkLifecycle.Classify(ChunkState.Loading, ChunkState.Unloaded, true));

    [TestCase(ChunkState.Resident)]
    [TestCase(ChunkState.Loading)]
    public void SavingCannotBeInterrupted(ChunkState to)
        => Assert.AreEqual(TransitionVerdict.ForbiddenSaveInterrupt,
            ChunkLifecycle.Classify(ChunkState.Saving, to, true));

    [Test]
    public void SelfTransitions_AreRejected()
    {
        foreach (ChunkState s in Enum.GetValues(typeof(ChunkState)))
            Assert.AreEqual(TransitionVerdict.NotInTable, ChunkLifecycle.Classify(s, s, true),
                $"{s}->{s} is a double-drain, which is a real bug, not a no-op.");
    }

    [Test]
    public void FailedPrecondition_IsDistinctFromIllegalPair()
        => Assert.AreEqual(TransitionVerdict.PreconditionFailed,
            ChunkLifecycle.Classify(ChunkState.Unloaded, ChunkState.Loading, false));

    [Test]
    public void Transition_ThrowsOnViolation()
    {
        var rec = new ChunkRecord { coord = int3.zero, state = ChunkState.Loading };
        Assert.Throws<InvalidOperationException>(
            () => ChunkLifecycle.Transition(rec, ChunkState.Unloaded, true));
        Assert.AreEqual(ChunkState.Loading, rec.state, "A rejected transition must not mutate state.");
    }

    [Test]
    public void Generation_IncrementsOnlyOnReturnToUnloaded()
    {
        var rec = new ChunkRecord { coord = int3.zero };
        ChunkLifecycle.Transition(rec, ChunkState.Loading, true);
        Assert.AreEqual(0, rec.generation);
        ChunkLifecycle.Transition(rec, ChunkState.Resident, true);
        Assert.AreEqual(0, rec.generation);
        ChunkLifecycle.Transition(rec, ChunkState.Unloaded, true);
        Assert.AreEqual(1, rec.generation, "The stale-completion guard depends on this bump.");
    }

    // =====================================================================
    // 2. Delta codec (D.1, §4.2)
    // =====================================================================

    private const uint SEED = 42;
    private static readonly int3 COORD = new int3(11, 0, 11);

    private static WorldMetaData Meta() => AnchorPlanner.Plan(SEED, 0);

    private static Chunk Generate(WorldMetaData meta, BrickDataPool pool)
    {
        var chunk = new Chunk();
        ChunkGeneratorFull.GenerateChunkFull(meta, COORD, chunk, new ChunkHandleAllocator(2), pool);
        return chunk;
    }

    [Test]
    public void HeaderSize_MatchesWhatTheWriterEmits()
    {
        // PHASE_3_COMPLETION.md §6.5's worst harness bug was a hand-counted
        // header size (17 vs 19) disagreeing with the writer. This asserts the
        // constant against reality rather than against arithmetic done twice.
        Assert.AreEqual(20, DeltaCodec.HEADER_BYTES,
            "int3(12) + uint seed(4) + ushort version(2) + ushort recordCount(2)");
        Assert.AreEqual(3, DeltaCodec.RECORD_HEADER_BYTES);
        Assert.AreEqual(512, DeltaCodec.DensePayloadBytes);
    }

    [Test]
    public void PristineChunk_EncodesToNothing()
    {
        var meta = Meta();
        using var a = new BrickDataPool(4096);
        using var b = new BrickDataPool(4096);
        var live = Generate(meta, a);
        var baseline = Generate(meta, b);

        Assert.IsNull(DeltaCodec.Encode(COORD, SEED, live, a, baseline, b),
            "§4.1: a never-edited chunk costs ZERO bytes and its file must not exist.");
    }

    [Test]
    public void EditedChunk_RoundTripsContentExact()
    {
        var meta = Meta();
        using var a = new BrickDataPool(4096);
        using var b = new BrickDataPool(4096);
        using var c = new BrickDataPool(4096);

        var live = Generate(meta, a);
        var store = new ChunkStore(a, new ChunkHandleAllocator(4));
        store.InsertChunk(live);
        for (int i = 0; i < 300; i++)
            store.SetVoxel(new int3(COORD.x * 128 + (i * 7) % 128, 30 + (i % 40), COORD.z * 128 + (i * 13) % 128),
                           Materials.Stone);

        var baseline = Generate(meta, b);
        byte[] bytes = DeltaCodec.Encode(COORD, SEED, live, a, baseline, b);
        Assert.IsNotNull(bytes);

        var restored = Generate(meta, c);
        Assert.IsTrue(DeltaCodec.TryDecodeOnto(bytes, COORD, SEED, restored, c, out var reason), $"reason={reason}");
        Assert.AreEqual(ChunkContentHash.Hash(live, a), ChunkContentHash.Hash(restored, c));
    }

    [Test]
    public void EverySingleBitCorruption_IsRejected()
    {
        var meta = Meta();
        using var a = new BrickDataPool(4096);
        using var b = new BrickDataPool(4096);

        var live = Generate(meta, a);
        var store = new ChunkStore(a, new ChunkHandleAllocator(4));
        store.InsertChunk(live);
        for (int i = 0; i < 50; i++)
            store.SetVoxel(new int3(COORD.x * 128 + i, 40, COORD.z * 128 + i), Materials.Stone);

        var baseline = Generate(meta, b);
        byte[] bytes = DeltaCodec.Encode(COORD, SEED, live, a, baseline, b);
        Assert.IsNotNull(bytes);

        var rng = new System.Random(99);
        for (int trial = 0; trial < 300; trial++)
        {
            byte[] corrupt = (byte[])bytes.Clone();
            corrupt[rng.Next(corrupt.Length)] ^= (byte)(1 << rng.Next(8));

            using var vp = new BrickDataPool(4096);
            var victim = Generate(meta, vp);
            uint pristine = ChunkContentHash.Hash(victim, vp);

            bool accepted = DeltaCodec.TryDecodeOnto(corrupt, COORD, SEED, victim, vp, out _);
            Assert.IsFalse(accepted, $"trial {trial}: a corrupted delta was accepted.");
            Assert.AreEqual(pristine, ChunkContentHash.Hash(victim, vp),
                $"trial {trial}: a REJECTED delta still modified the baseline (partial apply).");
        }
    }

    [Test]
    public void EveryTruncation_IsRejectedAndNeverThrows()
    {
        var meta = Meta();
        using var a = new BrickDataPool(4096);
        using var b = new BrickDataPool(4096);

        var live = Generate(meta, a);
        var store = new ChunkStore(a, new ChunkHandleAllocator(4));
        store.InsertChunk(live);
        for (int i = 0; i < 50; i++)
            store.SetVoxel(new int3(COORD.x * 128 + i, 40, COORD.z * 128 + i), Materials.Stone);

        byte[] bytes = DeltaCodec.Encode(COORD, SEED, live, a, Generate(meta, b), b);
        Assert.IsNotNull(bytes);

        for (int len = 0; len < bytes.Length; len += Math.Max(1, bytes.Length / 200))
        {
            byte[] trunc = new byte[len];
            Array.Copy(bytes, trunc, len);
            using var vp = new BrickDataPool(4096);
            var victim = Generate(meta, vp);

            Assert.DoesNotThrow(() => DeltaCodec.TryDecodeOnto(trunc, COORD, SEED, victim, vp, out _),
                $"§4.2 requires a total decode: length {len} threw.");
        }
    }

    [Test]
    public void WrongCoordOrSeed_IsRejected()
    {
        var meta = Meta();
        using var a = new BrickDataPool(4096);
        using var b = new BrickDataPool(4096);
        using var c = new BrickDataPool(4096);

        var live = Generate(meta, a);
        var store = new ChunkStore(a, new ChunkHandleAllocator(4));
        store.InsertChunk(live);
        store.SetVoxel(new int3(COORD.x * 128 + 3, 40, COORD.z * 128 + 3), Materials.Stone);

        byte[] bytes = DeltaCodec.Encode(COORD, SEED, live, a, Generate(meta, b), b);
        var victim = Generate(meta, c);

        Assert.IsFalse(DeltaCodec.TryDecodeOnto(bytes, new int3(0, 0, 0), SEED, victim, c, out var r1));
        Assert.AreEqual(DeltaRejectReason.ChunkCoordMismatch, r1);

        Assert.IsFalse(DeltaCodec.TryDecodeOnto(bytes, COORD, SEED + 1, victim, c, out var r2));
        Assert.AreEqual(DeltaRejectReason.SeedMismatch, r2);
    }

    // =====================================================================
    // 3. Eviction and the ring (§4.5, §3.3)
    // =====================================================================

    [Test]
    public void Eviction_ReturnsEveryDenseBrickToThePool()
    {
        var meta = Meta();
        using var pool = new BrickDataPool(20000);
        var alloc = new ChunkHandleAllocator(8);
        var store = new ChunkStore(pool, alloc);

        var chunk = new Chunk();
        ChunkGeneratorFull.GenerateChunkFull(meta, COORD, chunk, alloc, pool);
        store.InsertChunk(chunk);

        int held = store.DenseBricksHeld;
        Assert.Greater(held, 0, "the test chunk must actually contain dense bricks to be meaningful");

        int freed = store.EvictChunk(COORD);
        Assert.AreEqual(held, freed, "§4.5: every dense brick returns to the free-list on eviction.");
        Assert.AreEqual(0, store.DenseBricksHeld);
        Assert.AreEqual(0, store.ResidentCount);
        Assert.IsNull(chunk.bricks, "the inlined BrickHandle[4096] must go back to the allocator.");
    }

    [Test]
    public void RepeatedAdmitEvict_DoesNotLeak()
    {
        // §13's "memory creep -> a pool free path missed on eviction", as a
        // unit test rather than a 10-minute soak.
        var meta = Meta();
        using var pool = new BrickDataPool(20000);
        var alloc = new ChunkHandleAllocator(8);
        var store = new ChunkStore(pool, alloc);

        int baseline = -1;
        for (int cycle = 0; cycle < 20; cycle++)
        {
            var chunk = new Chunk();
            ChunkGeneratorFull.GenerateChunkFull(meta, COORD, chunk, alloc, pool);
            store.InsertChunk(chunk);
            if (baseline < 0) baseline = store.DenseBricksHeld;
            else Assert.AreEqual(baseline, store.DenseBricksHeld, $"cycle {cycle}: dense count drifted.");
            store.EvictChunk(COORD);
            Assert.AreEqual(0, store.DenseBricksHeld, $"cycle {cycle}: bricks left behind after eviction.");
        }
    }

    [Test]
    public void CoordOutsideWindow_NeverAliasesOntoAResidentChunk()
    {
        // The CPU half of the §6.2 phantom bug. Masking alone maps an
        // out-of-window coord onto SOME slot; the identity check is what stops
        // it reading that slot's data.
        var meta = Meta();
        using var pool = new BrickDataPool(20000);
        var alloc = new ChunkHandleAllocator(8);
        var store = new ChunkStore(pool, alloc);

        var chunk = new Chunk();
        ChunkGeneratorFull.GenerateChunkFull(meta, COORD, chunk, alloc, pool);
        store.InsertChunk(chunk);

        // Exactly one window-width away: masks to the SAME ring slot.
        var aliased = new int3(COORD.x + EngineConfig.WINDOW_CHUNKS_XZ, COORD.y, COORD.z);
        Assert.IsNull(store.GetChunk(aliased),
            "a coord one full window away masks to the same slot and MUST NOT resolve to it.");
        Assert.AreEqual(0, store.GetVoxel(new int3(aliased.x * 128 + 5, 40, aliased.z * 128 + 5)),
            "an aliased read must return air, not the resident chunk's material.");
    }

    [Test]
    public void InsertOverLiveChunk_Throws()
    {
        var meta = Meta();
        using var pool = new BrickDataPool(20000);
        var alloc = new ChunkHandleAllocator(8);
        var store = new ChunkStore(pool, alloc);

        var a = new Chunk();
        ChunkGeneratorFull.GenerateChunkFull(meta, COORD, a, alloc, pool);
        store.InsertChunk(a);

        var b = new Chunk { coord = new int3(COORD.x + EngineConfig.WINDOW_CHUNKS_XZ, COORD.y, COORD.z), isUniform = true };
        Assert.Throws<InvalidOperationException>(() => store.InsertChunk(b),
            "overwriting a live ring slot without evicting is the memory-creep bug; it must be loud.");
    }

    [Test]
    public void WindowOrigin_DoesNotChangeRingIndexing()
    {
        // The claim in ChunkStore's header: under a power-of-two mask,
        // subtracting the origin only rotates the ring. If this ever fails,
        // GetFlatIndex genuinely does need to become origin-relative.
        var meta = Meta();
        using var pool = new BrickDataPool(20000);
        var alloc = new ChunkHandleAllocator(8);
        var store = new ChunkStore(pool, alloc);

        var chunk = new Chunk();
        ChunkGeneratorFull.GenerateChunkFull(meta, COORD, chunk, alloc, pool);
        store.InsertChunk(chunk);

        store.SetWindowOrigin(new int3(-7, 0, 13));
        Assert.AreSame(chunk, store.GetChunk(COORD),
            "sliding the origin must not orphan an already-resident chunk.");
    }

    [Test]
    public void IsInWindow_TracksTheOrigin()
    {
        using var pool = new BrickDataPool(1024);
        var store = new ChunkStore(pool, new ChunkHandleAllocator(2));

        store.SetWindowOrigin(new int3(100, 0, 100));
        Assert.IsTrue(store.IsInWindow(new int3(100, 0, 100)));
        Assert.IsTrue(store.IsInWindow(new int3(100 + EngineConfig.WINDOW_CHUNKS_XZ - 1, 0, 100)));
        Assert.IsFalse(store.IsInWindow(new int3(99, 0, 100)));
        Assert.IsFalse(store.IsInWindow(new int3(100 + EngineConfig.WINDOW_CHUNKS_XZ, 0, 100)));
        Assert.IsFalse(store.IsInWindow(new int3(100, EngineConfig.WINDOW_CHUNKS_Y, 100)));
    }
}