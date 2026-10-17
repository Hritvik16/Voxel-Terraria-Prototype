// Assets/CoreEngine/Tests/FluidTerrainDirTests.cs
//
// Task 93e, owner Q55 (b): the GPU fluid CA reads terrain from the tier-0
// mirror (FluidCA.compute SampleVoxel), which with the shells on is a 16^3 box
// (~90 m) while fluid stays live to 128 m on every preset (Amendment 8.15).
// TerrainClipmap.FluidDir.cs gives the CA a second lookup for resident chunks
// OUTSIDE the box that a fluid tile touches. The suite runs -nographics, so the
// GPU read is checked through its CPU twin, TerrainClipmap.SampleVoxelAsFluidCA
// (the shader's SampleVoxel line for line, on the managed copies of what was
// uploaded), against the ChunkStore truth -- and the flow test runs the CPU
// reference CA (FluidReferenceCPU) on the terrain each of them sees.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;

public class FluidTerrainDirTests
{
    // The real geometry: a 16^3 box (Low-High with shells) at chunk origin 0 =
    // camera chunk (8, 8, 8) - 8, inside the 32 x 16 x 32 store ring. The camera
    // chunk starts at x = 102.4 m.
    private static readonly int3 Box16 = new int3(16, 16, 16);
    private static readonly int3 InsideChunk = new int3(10, 1, 8);    // 25.6-38.4 m from the camera
    private static readonly int3 OutsideChunk = new int3(16, 1, 8);   // 102.4-115.2 m: past the box (89.6 m)
    // The sandbox sits 8..72 voxels into the chunk: 103-110 m from the camera.
    private static readonly int3 SandboxOffset = new int3(8, 0, 8);
    private const int SBX = 8, SBY = 4, SBZ = 8;   // bricks (pow2): 64 x 32 x 64 voxels

    private BrickDataPool _pool;
    private ChunkStore _store;
    private readonly List<TerrainClipmap> _clips = new List<TerrainClipmap>();

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(16384);
        _store = new ChunkStore(_pool, new ChunkHandleAllocator(128));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var c in _clips) c.Dispose();
        _clips.Clear();
        _pool?.Dispose();
    }

    private TerrainClipmap NewClip(int3 dims, bool fluidDir)
    {
        var clip = new TerrainClipmap(dims, _pool.Capacity) { RecentreDirtiesStaleSlots = true };
        _clips.Add(clip);
        clip.SetWindowOrigin(int3.zero);
        if (fluidDir) clip.EnableFluidDir(new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ));
        return clip;
    }

    private void InsertAir(int3 chunk)
    {
        _store.InsertChunk(new Chunk { coord = chunk, isUniform = true, uniformMaterial = Materials.Air });
    }

    /// A shelf and a lower floor with a pillar, written through SetVoxel (dense
    /// bricks in the pool), in sandbox-local coordinates from `origin`.
    private static bool BasinSolid(int3 p, out byte m)
    {
        m = Materials.Stone;
        if (p.y <= 3) return true;                                  // floor
        if (p.x <= 23 && p.y <= 11) return true;                    // upper shelf, top at y = 11
        if (p.z == 0 || p.z == SBZ * 8 - 1) return p.y <= 20;       // side walls
        if (p.x >= 34 && p.x <= 37 && p.z >= 20 && p.z <= 27 && p.y <= 9)
        { m = Materials.MossyStone; return true; }                 // pillar in the lower area
        return false;
    }

    private void BuildBasin(int3 chunk, params TerrainClipmap[] clips)
    {
        InsertAir(chunk);
        int3 origin = chunk * 128 + SandboxOffset;
        for (int z = 0; z < SBZ * 8; z++)
        for (int y = 0; y < SBY * 8; y++)
        for (int x = 0; x < SBX * 8; x++)
            if (BasinSolid(new int3(x, y, z), out byte m)) _store.SetVoxel(origin + new int3(x, y, z), m);
        foreach (var c in clips) c.MarkDirty(chunk);
    }

    /// The CPU reference CA on the terrain `sample` gives, water poured on the
    /// shelf, `ticks` ticks. Returns the sandbox and the summed moves.
    private static FluidReferenceCPU Flow(System.Func<int3, byte> sample, int3 origin, int ticks, out long moves)
    {
        var sim = new FluidReferenceCPU(SBX, SBY, SBZ);
        for (int z = 0; z < SBZ * 8; z++)
        for (int y = 0; y < SBY * 8; y++)
        for (int x = 0; x < SBX * 8; x++)
        {
            byte m = sample(origin + new int3(x, y, z));
            if (m != Materials.Air) sim.EditVoxel(x, y, z, m);
        }
        sim.ResetLedgerToCurrentState();
        sim.FillBox(new int3(16, 12, 20), new int3(21, 19, 27), Materials.Water);   // near the shelf edge
        moves = 0;
        for (int t = 0; t < ticks; t++) { sim.Tick(); moves += sim.MovesThisTick; }
        return sim;
    }

    private static int Differences(FluidReferenceCPU a, FluidReferenceCPU b)
    {
        int n = 0;
        for (int z = 0; z < SBZ * 8; z++)
        for (int y = 0; y < SBY * 8; y++)
        for (int x = 0; x < SBX * 8; x++)
            if (a.GetVoxel(x, y, z) != b.GetVoxel(x, y, z)) n++;
        return n;
    }

    private static int WaterBelow(FluidReferenceCPU s, int yMax)
    {
        int n = 0;
        for (int z = 0; z < SBZ * 8; z++)
        for (int y = 0; y < yMax; y++)
        for (int x = 0; x < SBX * 8; x++)
            if (s.GetVoxel(x, y, z) == Materials.Water) n++;
        return n;
    }

    // ---- The acceptance test --------------------------------------------------

    [Test]
    public void WaterAt106m_OutsideThe16Box_FlowsTheSameAsInsideIt()
    {
        var clip = NewClip(Box16, fluidDir: true);
        var noDir = NewClip(Box16, fluidDir: false);
        BuildBasin(InsideChunk, clip, noDir);
        BuildBasin(OutsideChunk, clip, noDir);
        Assert.IsTrue(clip.ContainsChunk(InsideChunk) && !clip.ContainsChunk(OutsideChunk), "geometry");

        // What the fluid sim publishes for a tile on the shelf (32-voxel tiles).
        var wanted = new HashSet<int3>();
        int3 tile = (OutsideChunk * 128 + SandboxOffset + new int3(6, 14, 22)) >> 5;
        FluidGpuSimulation.AddChunksTouchedByTile(tile, 32, wanted);
        CollectionAssert.Contains(wanted, OutsideChunk);
        clip.SetFluidWanted(wanted);
        noDir.SetFluidWanted(wanted);   // no-op: off

        var s = clip.FlushAllDirty(_store, _pool);
        noDir.FlushAllDirty(_store, _pool);
        Assert.AreEqual(1, s.fluidDirChunks, "only the resident out-of-box chunk gets an entry");
        Assert.AreEqual(1, clip.FluidDirEntries);
        Assert.AreEqual(clip.UploadEpoch, clip.LastUploadEpoch(OutsideChunk), "a fluid wake there is released at once");

        const int Ticks = 160;
        foreach (int3 chunk in new[] { InsideChunk, OutsideChunk })
        {
            int3 origin = chunk * 128 + SandboxOffset;
            var truth = Flow(v => _store.GetVoxel(v), origin, Ticks, out long mTruth);
            var gpu = Flow(v => clip.SampleVoxelAsFluidCA(v, _pool), origin, Ticks, out long mGpu);
            Assert.AreEqual(0, Differences(truth, gpu), $"{chunk}: the GPU's terrain gives the CPU's flow");
            Assert.AreEqual(mTruth, mGpu, $"{chunk}: moves");
            Assert.Greater(mTruth, 500, $"{chunk}: the water must actually flow");
            Assert.Greater(WaterBelow(truth, 12), 0, $"{chunk}: water falls off the shelf onto the floor");
            Assert.IsTrue(truth.CheckConservation().Ok, $"{chunk}: conservation");
        }

        // Inside vs outside: the same basin flows the same, move for move.
        var a = Flow(v => clip.SampleVoxelAsFluidCA(v, _pool), InsideChunk * 128 + SandboxOffset, Ticks, out long mIn);
        var b = Flow(v => clip.SampleVoxelAsFluidCA(v, _pool), OutsideChunk * 128 + SandboxOffset, Ticks, out long mOut);
        Assert.AreEqual(mIn, mOut, "inside vs outside the box: moves");
        Assert.AreEqual(0, Differences(a, b), "inside vs outside the box: final bytes");

        // Control, today's opt-in box without the directory: the CA saw AIR there.
        int3 shelfStone = OutsideChunk * 128 + SandboxOffset + new int3(5, 8, 22);
        Assert.AreEqual(Materials.Stone, _store.GetVoxel(shelfStone));
        Assert.AreEqual(Materials.Air, noDir.SampleVoxelAsFluidCA(shelfStone, _pool), "control: without the dir the CA reads AIR");
        var ctrl = Flow(v => noDir.SampleVoxelAsFluidCA(v, _pool), OutsideChunk * 128 + SandboxOffset, Ticks, out _);
        var tr = Flow(v => _store.GetVoxel(v), OutsideChunk * 128 + SandboxOffset, Ticks, out _);
        Assert.Greater(Differences(ctrl, tr), 0, "control: without the dir the flow differs from the CPU's");
    }

    [Test]
    public void FluidReach_Is128m_OnEveryPreset_AndTheDirCoversEveryResidentChunk()
    {
        // Simulation reach does not depend on the preset (8.15 §1.2): the fluid
        // radius is one constant, and the load radius keeps it resident.
        Assert.AreEqual(1280, EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS, "128 m of 0.1 m voxels");
        Assert.LessOrEqual(EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS, EngineConfig.TG2_LOAD_RADIUS_CHUNKS * 128);
        foreach (QualityPreset p in new[] { QualityPreset.Low, QualityPreset.Medium, QualityPreset.High, QualityPreset.Ultra })
        {
            int3 box = ShellLayout.From(p).MirrorBox;
            // Every chunk the box misses within 128 m has a store-ring slot, so the
            // directory (indexed by the store ring) can hold it.
            int boxReachVoxels = (box.x >> 1) * 128 - 128;
            bool needsDir = boxReachVoxels < EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS;
            Assert.AreEqual(p != QualityPreset.Ultra, needsDir, $"{p}: box {box}");
        }
        Assert.Throws<System.ArgumentException>(() => NewClip(new int3(8, 4, 8), false).EnableFluidDir(new int3(24, 16, 32)), "pow2 (§6.2)");
    }

    [Test]
    public void TileApron_TouchesNeighbourChunks()
    {
        var set = new HashSet<int3>();
        FluidGpuSimulation.AddChunksTouchedByTile(new int3(0, 0, 0), 32, set);   // voxels 0..31 + apron -1..32
        Assert.AreEqual(8, set.Count);
        CollectionAssert.Contains(set, new int3(-1, -1, -1));
        set.Clear();
        FluidGpuSimulation.AddChunksTouchedByTile(new int3(1, 1, 1), 32, set);   // 32..63 + apron: one chunk
        CollectionAssert.AreEquivalent(new[] { int3.zero }, set);
    }

    // ---- Keeping the directory exact (scaled box: 8 x 4 x 8) -------------------

    [Test]
    public void Edit_Unwant_Evict_EnterBox_KeepTheDirectoryExact()
    {
        var clip = NewClip(new int3(8, 4, 8), fluidDir: true);
        int3 c = new int3(9, 0, 1);                  // outside the 8-box; 9 & 7 == (1, 0, 1)'s mirror slot
        int3 inBox = new int3(1, 0, 1);
        _store.InsertChunk(new Chunk { coord = inBox, isUniform = true, uniformMaterial = Materials.Sand });
        clip.MarkDirty(inBox);
        InsertAir(c);
        int3 v = c * 128 + new int3(40, 20, 40);
        _store.SetVoxel(v, Materials.Stone);
        clip.MarkDirty(c);

        // Not wanted: parked as before (task 32), the CA reads AIR there, and the
        // wake gate (FluidGpuSimulation.IsMirrorReadyForCell) holds wakes there.
        clip.FlushAllDirty(_store, _pool);
        Assert.IsFalse(clip.FluidDirHolds(c));
        Assert.AreEqual(Materials.Air, clip.SampleVoxelAsFluidCA(v, _pool));
        Assert.AreEqual(0, clip.FluidDirEntries);

        // Wanted: the parked chunk gets an entry and uploads; the in-box slot it aliases is untouched.
        clip.SetFluidWanted(new HashSet<int3> { c, inBox, new int3(9 + 32, 0, 1) });   // + a non-resident ring alias
        var s = clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(1, s.fluidDirChunks);
        Assert.AreEqual(1, clip.FluidDirEntries, "in-box and non-resident chunks get no entry");
        Assert.IsTrue(clip.FluidDirHolds(c), "published: the wake gate releases wakes there");
        Assert.AreEqual(Materials.Stone, clip.SampleVoxelAsFluidCA(v, _pool));
        Assert.AreEqual(Materials.Air, clip.SampleVoxelAsFluidCA(v + new int3(1, 0, 0), _pool));
        Assert.AreEqual(Materials.Sand, clip.SampleVoxelAsFluidCA(inBox * 128 + new int3(40, 20, 40), _pool), "aliased mirror slot");
        Assert.AreEqual(Materials.Air, clip.SampleVoxelAsFluidCA(v + new int3(32 * 128, 0, 0), _pool), "ring alias reads AIR");

        // An edit (a fluid move the CPU accepted) re-uploads the entry.
        _store.SetVoxel(v + new int3(1, 0, 0), Materials.Water);
        clip.MarkDirty(c);
        Assert.AreEqual(1, clip.FlushAllDirty(_store, _pool).fluidDirChunks);
        Assert.AreEqual(Materials.Water, clip.SampleVoxelAsFluidCA(v + new int3(1, 0, 0), _pool));

        // No longer wanted: the entry is freed and the index cleared at once.
        clip.SetFluidWanted(new HashSet<int3>());
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.FluidDirEntries);
        Assert.AreEqual(Materials.Air, clip.SampleVoxelAsFluidCA(v, _pool));

        // Wanted again, then evicted: cleared.
        clip.SetFluidWanted(new HashSet<int3> { c });
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(Materials.Stone, clip.SampleVoxelAsFluidCA(v, _pool));
        _store.EvictChunk(c);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.FluidDirEntries, "evicted");
        Assert.AreEqual(Materials.Air, clip.SampleVoxelAsFluidCA(v, _pool));

        // Back, wanted, then the box slides onto it: the mirror holds it, the entry goes.
        InsertAir(c);
        _store.SetVoxel(v, Materials.Stone);
        clip.MarkDirty(c);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(1, clip.FluidDirEntries);
        clip.SetWindowOrigin(new int3(8, 0, 0));
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(0, clip.FluidDirEntries, "in the box now");
        Assert.AreEqual(Materials.Stone, clip.SampleVoxelAsFluidCA(v, _pool), "read through the mirror");
        Assert.AreEqual(0, ClipmapValidator.CountStaleSlots(clip, _store, out string ex), $"no stale slot after the recentre: {ex}");
    }

    [Test]
    public void Directory_Grows_PastItsFirstAllocation_KeepingEntries()
    {
        var clip = NewClip(new int3(8, 4, 8), fluidDir: true);
        var wanted = new HashSet<int3>();
        int n = TerrainClipmap.FluidDirInitialEntries + 6;
        for (int i = 0; i < n; i++)
        {
            int3 c = new int3(8 + (i & 7), i >> 3 & 7, 8 + (i >> 6));
            _store.InsertChunk(new Chunk { coord = c, isUniform = true, uniformMaterial = (byte)(i % 2 == 0 ? Materials.Stone : Materials.Sand) });
            clip.MarkDirty(c);
            wanted.Add(c);
        }
        clip.SetFluidWanted(wanted);
        clip.FlushAllDirty(_store, _pool);
        Assert.AreEqual(n, clip.FluidDirEntries);
        Assert.GreaterOrEqual(clip.FluidDirCapacity, n);
        Assert.AreEqual(0, clip.FluidDirRefusedTotal);
        int k = 0;
        foreach (int3 c in wanted)
        {
            byte want = _store.GetVoxel(c * 128 + 5);
            Assert.AreEqual(want, clip.SampleVoxelAsFluidCA(c * 128 + 5, _pool), $"chunk {c}");
            k++;
        }
        Assert.AreEqual(n, k);
    }
}
