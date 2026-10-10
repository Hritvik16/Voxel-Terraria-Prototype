// Assets/CoreEngine/Tests/FluidAwakeOnlyTilesTests.cs
//
// Task 54 (engine plan step 5, BUGS #3): the tile pool holds AWAKE fluid only.
// CPU half only -- FluidTileMap's bookkeeping and FluidTileResidency's
// AwakeOnly path. The GPU half (that a placed box still spreads) is proven by
// -poolprobe in the real player, not here.
//
// Each test names one way the policy can fail:
//   resting fluid admitted       -> the coast fills the pool again (BUGS #3)
//   active tile released early   -> flowing water freezes mid-move
//   quiet tile never released    -> the pool fills with settled puddles
//   mid-flow tile forgotten      -> DESIGN_NOTE_7_2 §9: water left hanging
//                                   never wakes when the player returns

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;

public class FluidAwakeOnlyTilesTests
{
    private BrickDataPool _pool;
    private ChunkHandleAllocator _alloc;
    private ChunkStore _store;

    [SetUp]
    public void SetUp()
    {
        _pool = new BrickDataPool(8192, rangeAware: true);
        _alloc = new ChunkHandleAllocator(64);
        _store = new ChunkStore(_pool, _alloc);
    }

    [TearDown]
    public void TearDown() => _pool.Dispose();

    private static FluidTileMap AwakeTiles(int cap = 256, int quiet = 20)
        => new FluidTileMap(ChunkFluidMask.TILE_EDGE, new int3(128, 128, 128), cap)
        { AwakeOnly = true, QuietTicks = quiet };

    private void AirChunk(int3 coord)
        => _store.InsertChunk(new Chunk { coord = coord, isUniform = true, uniformMaterial = Materials.Air });

    private static int3 TileOfVoxel(int3 v)
        => ChunkFluidMask.AbsoluteTile(CoordMath.VoxelToChunk(v), ChunkFluidMask.TileInChunkOf(v));

    [Test]
    public void RestingOceanInTheRadiusTakesNoTile()
    {
        // The mask policy acquires all 64 tiles of this chunk
        // (FluidTileResidencyTests.AnOceanChunkAcquiresAllSixtyFourOfItsTiles).
        _store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Water });
        var tiles = AwakeTiles();

        var st = FluidTileResidency.Refresh(_store, tiles, new int3(64, 64, 64), 512, 640);

        Assert.AreEqual(0, tiles.ResidentTiles, "resting generated fluid is plain terrain until disturbed");
        Assert.AreEqual(0, st.TilesRefusedPoolFull);
    }

    [Test]
    public void AnActiveTileIsKept_AndReleasedOnceQuiet_WithoutBeingRemembered()
    {
        AirChunk(int3.zero);
        int3 v = new int3(70, 40, 100);
        _store.SetVoxel(v, Materials.Water);
        var tiles = AwakeTiles(quiet: 20);
        int3 tc = TileOfVoxel(v);
        tiles.Clock = 100;
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.Acquire(tc), "a wake acquires the tile");

        // Ops keep landing in it: never quiet.
        for (long t = 101; t < 160; t++)
        {
            tiles.Clock = t;
            tiles.NoteActivity(v);
            FluidTileResidency.Refresh(_store, tiles, v, 512, 640);
        }
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(tc), "an active tile must stay");

        tiles.Clock = 159 + 19;
        FluidTileResidency.Refresh(_store, tiles, v, 512, 640);
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(tc), "19 quiet ticks < QuietTicks 20");

        tiles.Clock = 159 + 20;
        var st = FluidTileResidency.Refresh(_store, tiles, v, 512, 640);
        Assert.AreEqual(1, st.TilesReleasedQuiet);
        Assert.AreEqual(0, tiles.ResidentTiles);
        Assert.AreEqual(0, tiles.UnsettledCount, "a settled tile is not remembered");

        // And the resting water it left is not re-admitted by the next pass.
        FluidTileResidency.Refresh(_store, tiles, v, 512, 640);
        Assert.AreEqual(0, tiles.ResidentTiles);
    }

    [Test]
    public void ATileLeftMidFlow_IsRemembered_AndReAdmittedWithASeedOnReturn()
    {
        AirChunk(int3.zero);
        AirChunk(new int3(3, 0, 0));
        int3 home = new int3(64, 64, 64);
        int3 away = new int3(3 * 128 + 64, 64, 64);
        _store.SetVoxel(home, Materials.Water);
        var tiles = AwakeTiles(quiet: 1000);
        int3 tc = TileOfVoxel(home);
        tiles.Clock = 10;
        tiles.Acquire(tc);
        tiles.NoteActivity(home);

        var st = FluidTileResidency.Refresh(_store, tiles, away, 128, 148);
        Assert.AreEqual(1, st.TilesReleasedByRadius);
        Assert.AreEqual(1, tiles.UnsettledCount, "released while still active: remembered");
        Assert.IsFalse(tiles.SeedRequested);

        st = FluidTileResidency.Refresh(_store, tiles, home, 128, 148);
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(tc), "returning re-admits it");
        Assert.AreEqual(1, st.TilesAcquired);
        Assert.AreEqual(0, tiles.UnsettledCount);
        Assert.IsTrue(tiles.SeedRequested, "and asks the sim to seed its stopped cells");
    }

    [Test]
    public void ARememberedTileWhoseFluidIsGone_IsForgotten()
    {
        AirChunk(int3.zero);
        AirChunk(new int3(3, 0, 0));
        int3 home = new int3(64, 64, 64);
        _store.SetVoxel(home, Materials.Water);
        var tiles = AwakeTiles(quiet: 1000);
        int3 tc = TileOfVoxel(home);
        tiles.Acquire(tc);
        FluidTileResidency.Refresh(_store, tiles, new int3(3 * 128 + 64, 64, 64), 128, 148);
        Assert.AreEqual(1, tiles.UnsettledCount);

        _store.SetVoxel(home, Materials.Air);           // drained while away
        // The mask clears lazily, off the write path (ChunkStore's asymmetric
        // rule); resolve it the way the engine does before asserting.
        _store.ResolveDirtyFluidTiles(_store.GetChunk(int3.zero), 64);
        FluidTileResidency.Refresh(_store, tiles, home, 128, 148);
        Assert.AreEqual(0, tiles.ResidentTiles, "nothing mobile left: no tile");
        Assert.AreEqual(0, tiles.UnsettledCount, "and nothing left to remember");
    }

    [Test]
    public void TheMaskPolicyIsUntouched_WhenAwakeOnlyIsOff()
    {
        _store.InsertChunk(new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Water });
        var tiles = AwakeTiles();
        tiles.AwakeOnly = false;
        FluidTileResidency.Refresh(_store, tiles, new int3(64, 64, 64), 512, 640);
        Assert.AreEqual(64, tiles.ResidentTiles);
    }

    [Test]
    public void QuietTicksCoverTheSlowestMaterialsSleep()
    {
        // A tile may only be called settled once every slot in it has slept:
        // FLUID_SLEEP_TICKS evaluated ticks, one evaluation per TickInterval.
        int quiet = FluidGpuSimulation.QuietTicksFor();
        for (int m = 0; m < 256; m++)
        {
            if (!MaterialRules.IsMobile((byte)m)) continue;
            Assert.GreaterOrEqual(quiet, EngineConfig.FLUID_SLEEP_TICKS * (int)MaterialRules.TickInterval((byte)m),
                $"material {m}");
        }
    }
}
