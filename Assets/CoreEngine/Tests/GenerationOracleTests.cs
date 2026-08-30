// ==========================================
// Assets/CoreEngine/Tests/GenerationOracleTests.cs
//
// STAGE 0 of the Job System conversion: the bit-identical gate.
//
// The conversion's stop condition is "any stage that cannot reach bit-identical
// generation output" -- that is not a puzzle to debug around, it means the
// storage change altered CONTENT, which is the §0.3 failure mode the whole plan
// exists to prevent. A stop condition nobody can evaluate is not a stop
// condition, so this file turns it into a measurement taken automatically on
// every EditMode run.
//
// The expected hashes below were captured on the branch point, before any
// conversion stage touched generation. Every later stage is compared against
// them.
//
// WHY ChunkContentHash IS THE RIGHT ORACLE HERE: it is content-canonical by
// construction -- it folds effective voxel bytes and nothing else, so
// representation (chunk-uniform vs brick-uniform vs dense, and which pool slot
// a body happens to live in) cannot move it. That is exactly the property this
// gate needs: it must catch a change in what the world IS, while staying blind
// to how the world is STORED, because changing how it is stored is the entire
// point of the conversion.
//
// IF THIS TEST GOES RED during the conversion, the correct response is to
// revert the stage, not to update the expected values. Updating them would
// silently redefine the world and invalidate every .delta and content hash
// already on disk.
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class GenerationOracleTests
{
    private const uint SEED = 42;

    // Baseline captured on the branch point (2026-08-29), before any conversion
    // stage touched generation. sc0 (11,0,11) independently cross-checks against
    // 0x9E82F7F3 recorded by Gate D on 2026-08-28 -- that chunk was pristine in
    // that run because of the edit-site harness bug fixed in 65d7057.
    //
    // CAPTURE MODE: an expected of 0 reports the observed value instead of
    // asserting, so a baseline can be re-recorded without a red run. It is NOT a
    // way to silence a mismatch -- see the assert message.
    private struct Sample
    {
        public byte sizeClass;
        public int3 coord;
        public uint expected;
        public string note;
    }

    private static readonly Sample[] Samples =
    {
        // sizeClass 0 -- the frozen dev region. Every content hash on record was
        // produced against it, so it is the one that must not move.
        new Sample { sizeClass = 0, coord = new int3(11, 0, 11), expected = 0x9E82F7F3u, note = "sc0 island centre" },
        new Sample { sizeClass = 0, coord = new int3(0, 0, 0),   expected = 0x344EF8A3u, note = "sc0 ocean corner" },

        // sizeClass 1 -- the shipping world. Island centre is chunk (100,0,100).
        new Sample { sizeClass = 1, coord = new int3(100, 0, 100), expected = 0x177EECFBu, note = "sc1 island centre" },
        new Sample { sizeClass = 1, coord = new int3(95, 0, 103),  expected = 0x9FD779A7u, note = "sc1 inland" },
        new Sample { sizeClass = 1, coord = new int3(107, 0, 97),  expected = 0xD8A47371u, note = "sc1 inland, other quadrant" },
        new Sample { sizeClass = 1, coord = new int3(0, 0, 0),     expected = 0x3BC052F3u, note = "sc1 far ocean" },
    };

    private static uint HashOf(byte sizeClass, int3 coord)
    {
        WorldMetaData meta = AnchorPlanner.Plan(SEED, sizeClass);
        var pool = new BrickDataPool(EngineConfig.BRICKS_PER_CHUNK);
        try
        {
            var alloc = new ChunkHandleAllocator(2);
            var chunk = new Chunk();
            ChunkGeneratorFull.GenerateChunkFull(meta, coord, chunk, alloc, pool);
            return ChunkContentHash.Hash(chunk, pool);
        }
        finally { pool.Dispose(); }
    }

    [Test]
    public void GeneratedChunks_MatchRecordedOracle()
    {
        var report = new System.Text.StringBuilder();
        int captured = 0, mismatched = 0;

        foreach (var s in Samples)
        {
            uint actual = HashOf(s.sizeClass, s.coord);

            if (s.expected == 0u)
            {
                captured++;
                report.AppendLine($"  CAPTURE sc{s.sizeClass} {s.coord} = 0x{actual:X8}u   // {s.note}");
                continue;
            }

            if (actual != s.expected)
            {
                mismatched++;
                report.AppendLine(
                    $"  MISMATCH sc{s.sizeClass} {s.coord} ({s.note}): " +
                    $"expected 0x{s.expected:X8} got 0x{actual:X8}");
            }
        }

        if (captured > 0)
        {
            UnityEngine.Debug.Log(
                $"[GenerationOracle] CAPTURE MODE -- {captured} baseline hash(es) not yet " +
                $"recorded. Paste these into Samples:\n{report}");
        }

        Assert.Zero(mismatched,
            "Generated terrain no longer matches the recorded oracle. This is the §0.3 " +
            "stop condition: a conversion stage changed world CONTENT, not just storage. " +
            "REVERT THE STAGE -- do not update these expectations, that would silently " +
            "redefine the world and invalidate every .delta and content hash on disk.\n" +
            report);
    }

    /// Determinism within a single run, independent of the recorded baseline.
    /// Guards the case where generation becomes nondeterministic (e.g. a job
    /// racing, or scratch reused without clearing) while still agreeing with the
    /// oracle on the first call of each pair.
    [Test]
    public void GeneratedChunks_AreSelfConsistentAcrossRepeatedCalls()
    {
        foreach (var s in Samples)
        {
            uint a = HashOf(s.sizeClass, s.coord);
            uint b = HashOf(s.sizeClass, s.coord);
            Assert.AreEqual(a, b,
                $"Generation is not deterministic for sc{s.sizeClass} {s.coord} ({s.note}): " +
                $"0x{a:X8} then 0x{b:X8}.");
        }
    }
}
