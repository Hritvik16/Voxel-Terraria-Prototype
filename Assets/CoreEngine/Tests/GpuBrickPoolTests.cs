// Assets/CoreEngine/Tests/GpuBrickPoolTests.cs
//
// Task 93f (Amendment 8.15 row D3): the GPU's own brick-body pool and its
// CPU-handle -> GPU-slot table (TerrainClipmap.GpuPool.cs). The suite runs
// -nographics (GraphicsBuffer.GetData reads zeros), so every check reads the
// clipmap's CPU SHADOW of its two GPU buffers (EnableCpuShadow) through
// ClipmapValidator, which compares CPU and GPU through the table.
//
// Covered: off = the old layout (identity handles, CPU-pool sized buffer);
// allocate / upload / free / reuse round trip; no slot leak over a long
// recentre walk (in use == the in-box residents' dense bricks after every
// step, 0 at the end); a full pool refuses and counts (no overwrite), and the
// refused chunk is retried once slots free up; the validator finds an injected
// table mix-up and an injected body byte; fluid-dir entries hold slots too.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using VoxelEngine.Memory;

public class GpuBrickPoolTests
{
    private static readonly int3 Box = new int3(4, 2, 4);
    private BrickDataPool _pool;
    private ChunkStore _store;
    private readonly List<TerrainClipmap> _clips = new List<TerrainClipmap>();

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(16384);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(256));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var c in _clips) c.Dispose();
        _clips.Clear();
        _pool?.Dispose();
    }

    private TerrainClipmap NewClip(int gpuCap, int3 dims)
    {
        var clip = new TerrainClipmap(dims, _pool.Capacity, gpuCap) { RecentreDirtiesStaleSlots = true };
        _clips.Add(clip);
        clip.EnableCpuShadow();
        clip.SetWindowOrigin(int3.zero);
        return clip;
    }

    private static int DenseCount(int3 c) => 2 + ((c.x * 7 + c.z * 13 + 640) & 3);

    /// A resident chunk with DenseCount(c) dense bricks (SetVoxel, one voxel each).
    private void InsertDense(TerrainClipmap clip, int3 c, int n = -1)
    {
        _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air });
        if (n < 0) n = DenseCount(c);
        int3 v0 = c * EngineConfig.CHUNK_EDGE_VOXELS;
        for (int i = 0; i < n; i++)
        {
            int3 b = new int3(i & 15, (i >> 4) & 15, 3);
            _store.SetVoxel(v0 + b * 8 + new int3(1, 2, 3), (byte)(Materials.Stone + (i & 1)));
        }
        clip.MarkDirty(c);
    }

    private void Evict(TerrainClipmap clip, int3 c) { _store.EvictChunk(c); clip.MarkDirty(c); }

    private static int DenseOf(Chunk ch)
    {
        if (ch.isUniform || ch.bricks == null) return 0;
        int n = 0;
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++) if ((ch.bricks[i].data & 0x80000000u) != 0) n++;
        return n;
    }

    private int InBoxDense(TerrainClipmap clip)
    {
        int n = 0;
        foreach (Chunk ch in _store.ResidentChunks())
            if (clip.ContainsChunk(ch.coord) && !clip.IsGpuRefused(ch.coord)) n += DenseOf(ch);
        return n;
    }

    private static void AssertGreen(TerrainClipmap clip, BrickDataPool pool, ChunkStore store, string what)
    {
        var r = ClipmapValidator.ValidateRegion(clip, pool, store);
        Assert.IsTrue(r.pass, $"{what}: {r.Describe()}");
        Assert.AreEqual(0, ClipmapValidator.CountStaleSlots(clip, store, out string ex), $"{what}: stale {ex}");
    }

    [Test]
    public void Off_IsTheOldLayout()
    {
        var clip = NewClip(0, Box);
        InsertDense(clip, new int3(1, 0, 1), 5);
        clip.FlushAllDirty(_store, _pool);
        Assert.IsFalse(clip.GpuPoolEnabled);
        Assert.AreEqual(_pool.Capacity * 128, clip.BrickDataBuffer.count, "CPU-pool sized");
        Chunk ch = _store.GetChunk(new int3(1, 0, 1));
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            Assert.AreEqual(ch.bricks[i].data, clip.GpuHandleOf(ch.bricks[i].data), "identity handles");
        AssertGreen(clip, _pool, _store, "off");
    }

    [Test]
    public void RoundTrip_AllocateUploadFreeReuse()
    {
        var clip = NewClip(64, Box);
        Assert.AreEqual(64 * 128, clip.BrickDataBuffer.count, "GPU pool sized");
        int3 a = new int3(0, 0, 0), b = new int3(1, 0, 0), c = new int3(2, 0, 0);
        InsertDense(clip, a, 10);
        InsertDense(clip, b, 10);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(20, clip.GpuPoolInUse);
        Chunk ca = _store.GetChunk(a);
        int g = -1;
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK && g < 0; i++)
            if ((ca.bricks[i].data & 0x80000000u) != 0) g = clip.GpuSlotOf((int)(ca.bricks[i].data & 0x3FFFFFFFu));
        Assert.That(g, Is.InRange(0, 63), "a dense brick maps to a GPU slot");
        AssertGreen(clip, _pool, _store, "two chunks");

        // Re-upload of an edited chunk keeps its mapped slots, adds one.
        _store.SetVoxel(a * 128 + new int3(100, 100, 100), Materials.Stone);
        clip.MarkDirty(a);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(21, clip.GpuPoolInUse);
        AssertGreen(clip, _pool, _store, "edit");

        // Evict: its slots return. A new chunk reuses them (CPU and GPU).
        Evict(clip, a);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(10, clip.GpuPoolInUse, "evicted chunk's GPU slots freed");
        InsertDense(clip, c, 12);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(22, clip.GpuPoolInUse);
        Assert.AreEqual(22, clip.GpuPoolPeakUsed);
        AssertGreen(clip, _pool, _store, "reuse");

        Evict(clip, b); Evict(clip, c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.GpuPoolInUse, "all freed");
        Assert.AreEqual(0, clip.GpuPoolRefusedTotal);
    }

    [Test]
    public void LongRecentreWalk_NoSlotLeak()
    {
        const int R = 3;   // residents |dx|,|dz| <= 3 around the camera, the box 4 wide: most are outside it
        var clip = NewClip(256, Box);
        var path = new List<int3>();
        for (int i = 0; i < 12; i++) path.Add(new int3(i, 0, 0));
        for (int i = 0; i < 8; i++) path.Add(new int3(11, 0, i));
        for (int i = 11; i >= -6; i--) path.Add(new int3(i, 0, 7 - (i & 3)));
        for (int i = 7; i >= 0; i--) path.Add(new int3(-6, 0, i));

        int step = 0;
        foreach (int3 cam in path)
        {
            clip.SetWindowOrigin(VoxelEngine.Streaming.StreamManager.MirrorOrigin(cam, int3.zero, _store.WindowDims, Box, true));
            var gone = new List<int3>();
            foreach (Chunk ch in _store.ResidentChunks())
                if (math.max(math.abs(ch.coord.x - cam.x), math.abs(ch.coord.z - cam.z)) > R) gone.Add(ch.coord);
            foreach (int3 c in gone) Evict(clip, c);
            for (int dz = -R; dz <= R; dz++)
            for (int dx = -R; dx <= R; dx++)
            {
                int3 c = new int3(cam.x + dx, 0, cam.z + dz);
                if (!_store.IsResident(c)) InsertDense(clip, c);
            }
            clip.FlushAllDirty(_store, _pool);
            Assert.AreEqual(InBoxDense(clip), clip.GpuPoolInUse, $"step {step} cam {cam}: GPU slots in use == in-box dense bricks");
            AssertGreen(clip, _pool, _store, $"step {step} cam {cam}");
            step++;
        }
        Assert.Greater(clip.RecentreDirtiedTotal, 0);
        Assert.AreEqual(0, clip.GpuPoolRefusedTotal);

        var all = new List<int3>();
        foreach (Chunk ch in _store.ResidentChunks()) all.Add(ch.coord);
        foreach (int3 c in all) Evict(clip, c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.GpuPoolInUse, "no slot leaked over the walk");
    }

    [Test]
    public void PoolFull_RefusedAndCounted_NoOverwrite_RetriedWhenFreed()
    {
        var clip = NewClip(25, Box);
        int3 a = new int3(0, 0, 0), b = new int3(1, 0, 0);
        InsertDense(clip, a, 20);
        clip.FlushAllDirty(_store, _pool);
        InsertDense(clip, b, 20);
        var s = clip.FlushAllDirty(_store, _pool);

        Assert.AreEqual(1, clip.GpuPoolRefusedTotal, "refused, counted");
        Assert.IsTrue(clip.IsGpuRefused(b));
        Assert.AreEqual(1, clip.GpuPoolRefusedNow);
        Assert.AreEqual(20, clip.GpuPoolInUse, "a's slots untouched");
        Assert.IsFalse(clip.IsDirty(b), "not retried every frame");
        for (int i = 0; i < 8; i++)
            Assert.AreEqual(0u, clip.CpuMirrorHandle(b, new int3(i, i >> 4, 3)), "refused chunk held as air");
        var r = ClipmapValidator.ValidateRegion(clip, _pool, _store);
        Assert.IsTrue(r.pass, r.Describe());
        Assert.AreEqual(1, r.chunksGpuRefused);

        // A frees its slots: b goes in at the next pass.
        Evict(clip, a);
        clip.FlushAllDirty(_store, _pool);   // a's clear frees 20 slots
        clip.FlushAllDirty(_store, _pool);   // b retried
        Assert.IsFalse(clip.IsGpuRefused(b));
        Assert.AreEqual(20, clip.GpuPoolInUse);
        AssertGreen(clip, _pool, _store, "retried");
    }

    [Test]
    public void Validator_FindsAnInjectedTableMixUp_AndABodyByte()
    {
        var clip = NewClip(64, Box);
        InsertDense(clip, new int3(1, 0, 1), 6);
        clip.FlushAllDirty(_store, _pool);
        AssertGreen(clip, _pool, _store, "before");

        Chunk ch = _store.GetChunk(new int3(1, 0, 1));
        var dense = new List<int>();
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            if ((ch.bricks[i].data & 0x80000000u) != 0) dense.Add((int)(ch.bricks[i].data & 0x3FFFFFFFu));

        clip.DebugSwapTable(dense[0], dense[1]);
        LogAssert.Expect(LogType.Error, new Regex(@"\[ClipmapValidator\] RED"));
        var r = ClipmapValidator.ValidateRegion(clip, _pool, _store);
        Assert.IsFalse(r.pass, "a table mix-up must be found");
        Assert.AreEqual(2, r.mismatchesInCleanChunks, "both swapped bricks, as LOST (clean chunk)");
        Assert.AreEqual(2, r.byKind[ClipmapValidator.MismatchKind.BothDenseDifferentSlot]);
        clip.DebugSwapTable(dense[0], dense[1]);
        AssertGreen(clip, _pool, _store, "swapped back");

        clip.DebugPokeShadowBody(clip.GpuSlotOf(dense[2]), 5, 0xDEADBEEFu);
        LogAssert.Expect(LogType.Error, new Regex(@"\[ClipmapValidator\] RED"));
        r = ClipmapValidator.ValidateRegion(clip, _pool, _store);
        Assert.AreEqual(1, r.brickByteMismatches, "a wrong body byte must be found");
    }

    [Test]
    public void FluidDirEntries_HoldSlots_ReleasedWithTheEntry()
    {
        var clip = NewClip(64, Box);
        clip.EnableFluidDir(new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ));
        int3 inBox = new int3(1, 0, 1), outBox = new int3(6, 0, 1);
        InsertDense(clip, inBox, 4);
        InsertDense(clip, outBox, 7);
        int3 v = outBox * 128 + new int3(1 * 8 + 1, 2, 3 * 8 + 3);   // brick (1,0,3): one of the dense ones
        clip.SetFluidWanted(new HashSet<int3> { outBox });
        clip.FlushAllDirty(_store, _pool);
        Assert.IsTrue(clip.FluidDirHolds(outBox));
        Assert.AreEqual(11, clip.GpuPoolInUse, "box + fluid-dir entry");
        Assert.AreEqual(_store.GetVoxel(v), clip.SampleVoxelAsFluidCA(v, _pool), "CPU twin unchanged");

        clip.SetFluidWanted(new HashSet<int3>());
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.FluidDirEntries);
        Assert.AreEqual(4, clip.GpuPoolInUse, "the entry's slots released");
        AssertGreen(clip, _pool, _store, "after");
    }
}
