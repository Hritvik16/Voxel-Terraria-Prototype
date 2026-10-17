// Assets/CoreEngine/Tests/WorldStateTests.cs
//
// Task 101 (Amendment 8.15 row C1): the world-state file `world.state` --
// world clock, world stage byte, per-chunk last-seen + generation stage.
// Nothing reads it yet; these check that it is kept and survives a reload:
//   - save/load round trip (clock, stage, entries; same bytes re-encoded);
//   - no file -> clock 0, stage 0, last-seen NEVER, generation stage 0, and a
//     state that was never made persistent writes nothing;
//   - corrupted / short / wrong-seed files -> defaults + a warning, no throw,
//     the bad file moved aside;
//   - StreamManager stamps last-seen on eviction, notes the generation stage on
//     arrival, writes no file while nothing is saved, and writes one once a
//     delta is saved (FlushAllDirty).
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using VoxelEngine.Memory;
using VoxelEngine.Mirror;
using VoxelEngine.Streaming;
using VoxelEngine.WorldGen;

public class WorldStateTests
{
    private const uint SEED = 42u;
    private string _dir;

    [SetUp]
    public void MakeDir()
    {
        _dir = Path.Combine(Path.GetTempPath(), "world_state_test_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void RemoveDir() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private string FilePath => Path.Combine(_dir, WorldState.FILE_NAME);

    [Test]
    public void MissingFile_LoadsDefaults_AndWritesNothingUntilPersistent()
    {
        var s = WorldState.Open(_dir, SEED);
        Assert.IsFalse(s.HadFile);
        Assert.AreEqual(WorldStateRejectReason.None, s.LoadReject);
        Assert.AreEqual(0.0, s.ClockSeconds);
        Assert.AreEqual(0, s.Stage);
        Assert.AreEqual(0, s.ChunkCount);
        Assert.AreEqual(WorldState.NEVER, s.LastSeen(new int3(3, 0, -7)));
        Assert.AreEqual(0, s.GenerationStage(new int3(3, 0, -7)));

        s.Advance(12.5);
        s.StampSeen(new int3(1, 0, 1));
        Assert.IsFalse(s.SaveIfPersistent(), "a run that saves nothing must not create world.state");
        Assert.IsFalse(File.Exists(FilePath));

        s.Advance(-5); s.Advance(double.NaN); s.Advance(double.PositiveInfinity);
        Assert.AreEqual(12.5, s.ClockSeconds, "negative / NaN / infinite steps are ignored");
    }

    [Test]
    public void SaveLoad_RoundTrip_KeepsClockStageAndChunkEntries()
    {
        var a = new int3(5, 0, -3);
        var b = new int3(-200, 1, 77);
        var c = new int3(0, 0, 0);
        var s = WorldState.Open(_dir, SEED);
        s.Advance(1234.25);
        s.NoteGenerated(a, 0);
        s.StampSeen(a);
        s.NoteGenerated(b, 3);          // a non-zero generation stage must persist (§14's pin byte)
        s.Advance(10);
        s.StampSeen(c);                 // seen without a generation note
        s.MarkPersistent();
        Assert.IsTrue(s.SaveIfPersistent());
        Assert.IsTrue(File.Exists(FilePath));
        Assert.IsFalse(File.Exists(FilePath + ".tmp"), "atomic write leaves no .tmp");
        Assert.AreEqual(WorldState.HEADER_BYTES + 3 * WorldState.CHUNK_BYTES + WorldState.TRAILER_BYTES,
                        new FileInfo(FilePath).Length, "file size = header + 3 entries + trailer");

        var r = WorldState.Open(_dir, SEED);
        Assert.IsTrue(r.HadFile);
        Assert.AreEqual(WorldStateRejectReason.None, r.LoadReject);
        Assert.IsTrue(r.Persistent, "an existing save keeps being written");
        Assert.AreEqual(1244.25, r.ClockSeconds);
        Assert.AreEqual(0, r.Stage);
        Assert.AreEqual(3, r.ChunkCount);
        Assert.AreEqual(1234.25, r.LastSeen(a));
        Assert.AreEqual(0, r.GenerationStage(a));
        Assert.AreEqual(WorldState.NEVER, r.LastSeen(b));
        Assert.AreEqual(3, r.GenerationStage(b));
        Assert.AreEqual(1244.25, r.LastSeen(c));
        Assert.AreEqual(0, r.GenerationStage(c));
        CollectionAssert.AreEqual(File.ReadAllBytes(FilePath), r.Encode(), "reload re-encodes to the same bytes");

        // Re-generation updates the stage and keeps last-seen.
        r.NoteGenerated(a, 1);
        Assert.AreEqual(1, r.GenerationStage(a));
        Assert.AreEqual(1234.25, r.LastSeen(a));
    }

    [Test]
    public void CorruptShortOrForeignFile_LoadsDefaults_WithAWarning()
    {
        var s = WorldState.Open(_dir, SEED);
        s.Advance(99); s.StampSeen(new int3(1, 0, 2)); s.NoteGenerated(new int3(1, 0, 2), 2);
        s.MarkPersistent(); s.SaveIfPersistent();
        byte[] good = File.ReadAllBytes(FilePath);

        byte[] flipped = (byte[])good.Clone(); flipped[8] ^= 0x40;
        byte[] shortFile = new byte[7];
        byte[] truncated = new byte[good.Length - 5]; System.Array.Copy(good, truncated, truncated.Length);
        var cases = new (byte[] bytes, uint seed, WorldStateRejectReason why)[]
        {
            (flipped, SEED, WorldStateRejectReason.CrcMismatch),
            (shortFile, SEED, WorldStateRejectReason.TooShort),
            (truncated, SEED, WorldStateRejectReason.CrcMismatch),
            (good, SEED + 1, WorldStateRejectReason.SeedMismatch),
            (new byte[0], SEED, WorldStateRejectReason.TooShort),
        };
        foreach (var (bytes, seed, why) in cases)
        {
            File.WriteAllBytes(FilePath, bytes);
            if (File.Exists(FilePath + ".rejected")) File.Delete(FilePath + ".rejected");
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WorldState\] .* REJECTED \(" + why + @"\)"));
            WorldState r = null;
            Assert.DoesNotThrow(() => r = WorldState.Open(_dir, seed), why.ToString());
            Assert.AreEqual(why, r.LoadReject);
            Assert.AreEqual(0.0, r.ClockSeconds, why.ToString());
            Assert.AreEqual(0, r.Stage);
            Assert.AreEqual(0, r.ChunkCount);
            Assert.AreEqual(WorldState.NEVER, r.LastSeen(new int3(1, 0, 2)));
            Assert.IsFalse(File.Exists(FilePath), "a rejected file is moved aside, never overwritten in place");
            Assert.IsTrue(File.Exists(FilePath + ".rejected"));
        }

        // A length that disagrees with the count (CRC recomputed, so only the length check can catch it).
        byte[] lying = WorldState.Encode(SEED, 5, 0, new System.Collections.Generic.Dictionary<int3, WorldState.ChunkEntry>
            { { new int3(1, 1, 1), new WorldState.ChunkEntry { lastSeenSeconds = 2, generationStage = 0 } } });
        lying[15] = 2;  // chunkCount low byte (offset 4 + 2 + 8 + 1 = 15): 1 -> 2
        uint crc = WorldMeta.Crc32(lying, 0, lying.Length - 4);
        for (int i = 0; i < 4; i++) lying[lying.Length - 4 + i] = (byte)(crc >> (8 * i));
        Assert.IsFalse(WorldState.TryDecode(lying, SEED, out _, out _, out _, out var reason));
        Assert.AreEqual(WorldStateRejectReason.LengthInconsistent, reason);

        // A NaN clock (CRC valid) is rejected, not loaded.
        byte[] nan = WorldState.Encode(SEED, double.NaN, 0, new System.Collections.Generic.Dictionary<int3, WorldState.ChunkEntry>());
        Assert.IsFalse(WorldState.TryDecode(nan, SEED, out _, out _, out _, out reason));
        Assert.AreEqual(WorldStateRejectReason.BadValue, reason);
    }

    [Test]
    public void StreamManager_StampsLastSeenOnEviction_AndWritesOnlyOnceTheRunSaves()
    {
        var meta = new WorldMetaData { seed = SEED, sizeClass = 0 };
        var pool = new BrickDataPool(400000, rangeAware: true);
        var alloc = new ChunkHandleAllocator(1024);
        var store = new ChunkStore(pool, alloc);
        var mirror = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.MIRROR_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        var clip = new TerrainClipmap(mirror, pool.Capacity);
        var cascades = new LODCascadeManager(mirror, tier => EngineConfig.CascadeTierPoolCap(tier));
        StreamManager sm = null;
        try
        {
            sm = new StreamManager(store, pool, alloc, clip, cascades, meta, _dir, 2);
            Assert.AreEqual(0.0, sm.World.ClockSeconds, "a save with no world.state starts at clock 0");

            Vector3 cam = new Vector3(64f, 20f, 64f);
            sm.PrimeWindow(cam);
            Settle(sm, cam);
            var start = new System.Collections.Generic.List<int3>();
            foreach (var ch in store.ResidentChunks()) start.Add(ch.coord);
            Assert.Greater(start.Count, 0);
            foreach (var c in start)
            {
                Assert.AreEqual(0, sm.World.GenerationStage(c), $"{c}: generated at stage 0");
                Assert.AreEqual(WorldState.NEVER, sm.World.LastSeen(c), $"{c}: resident, not yet seen leaving");
            }
            Assert.GreaterOrEqual(sm.World.ChunkCount, start.Count, "every arrival is noted");

            // Clean eviction: last-seen stamped at the clock; nothing saved -> no file.
            sm.AdvanceClock(100.0);
            Vector3 far = cam + new Vector3(2000f, 0f, 1500f);
            Settle(sm, far);
            foreach (var c in start)
            {
                Assert.IsFalse(store.IsResident(c));
                Assert.AreEqual(100.0, sm.World.LastSeen(c), $"{c}: last-seen not stamped on eviction");
            }
            Assert.AreEqual(0, sm.FlushAllDirty());
            Assert.IsFalse(File.Exists(FilePath), "a run that saved no terrain must not write world.state");

            // Dirty eviction: a delta is written -> the save is real -> FlushAllDirty writes world.state.
            sm.AdvanceClock(50.0);
            var dirty = default(int3);
            foreach (var ch in store.ResidentChunks()) { ch.deltaDirty = true; dirty = ch.coord; break; }
            Vector3 far2 = cam + new Vector3(4000f, 0f, 3000f);   // not back to cam: start[] must stay away
            Settle(sm, far2);
            Assert.IsFalse(store.IsResident(dirty));
            Assert.AreEqual(150.0, sm.World.LastSeen(dirty));
            sm.AdvanceClock(25.0);
            sm.FlushAllDirty();
            Assert.IsTrue(File.Exists(FilePath), "a run that saved a delta writes world.state");

            var reread = WorldState.Open(_dir, SEED);
            Assert.AreEqual(WorldStateRejectReason.None, reread.LoadReject);
            Assert.AreEqual(175.0, reread.ClockSeconds);
            Assert.AreEqual(100.0, reread.LastSeen(start[0]));
            Assert.AreEqual(150.0, reread.LastSeen(dirty));
            foreach (var ch in store.ResidentChunks())
                Assert.AreEqual(175.0, reread.LastSeen(ch.coord), "residents are stamped seen at the save");
        }
        finally
        {
            sm?.Dispose();
            cascades.Dispose(); clip.Dispose(); pool.Dispose();
        }
    }

    private static void Settle(StreamManager sm, Vector3 cam)
    {
        var clk = System.Diagnostics.Stopwatch.StartNew();
        do { sm.Update(cam); sm.WaitForIdle(); }
        while ((sm.LoadDeficit() > 0 || sm.PendingLoads > 0 || sm.InFlightLoads > 0 || sm.EvictionDeferred) && clk.Elapsed.TotalSeconds < 120);
        for (int i = 0; i < 2; i++) { sm.Update(cam); sm.WaitForIdle(); }
    }
}
