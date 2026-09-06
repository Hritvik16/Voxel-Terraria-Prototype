// Assets/CoreEngine/Tests/FluidTileResidencyTests.cs
//
// "Is there dormant fluid within the active radius" -- proven against a
// synthetic world with KNOWN fluid placement, before anything GPU touches it.
//
// The three gates are tested independently, because each one failing open or
// closed is a different bug:
//   §9.4 residency   fails open  -> simulating against unloaded world (Phase 5d)
//   the mask         fails open  -> a tile per chunk, sparsity gone
//                    fails shut  -> fluid that never wakes (the §7.4 bug class)
//   §7.4 radius      fails shut  -> approach does not wake

using NUnit.Framework;
using System.Collections.Generic;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Simulation;

public class FluidTileResidencyTests
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

    private static FluidTileMap Tiles(int cap = 256)
        => new FluidTileMap(ChunkFluidMask.TILE_EDGE, new int3(128, 128, 128), cap);

    /// An air chunk, resident, into which fluid can then be written.
    private Chunk AirChunk(int3 coord)
    {
        var c = new Chunk { coord = coord, isUniform = true, uniformMaterial = Materials.Air };
        _store.InsertChunk(c);
        return c;
    }

    /// Places one water voxel and returns its absolute tile coord.
    private int3 PlaceWater(int3 worldVoxel)
    {
        _store.SetVoxel(worldVoxel, Materials.Water);
        return ChunkFluidMask.AbsoluteTile(CoordMath.VoxelToChunk(worldVoxel),
                                           ChunkFluidMask.TileInChunkOf(worldVoxel));
    }

    // =====================================================================
    // Gate 2 -- the mask IS the sparsity
    // =====================================================================

    [Test]
    public void OnlyTilesThatActuallyContainFluidAreAcquired()
    {
        AirChunk(int3.zero);
        var tiles = Tiles();
        int3 v = new int3(70, 40, 100);
        int3 want = PlaceWater(v);

        var st = FluidTileResidency.Refresh(_store, tiles, v, 512, 640);

        Assert.AreEqual(1, tiles.ResidentTiles,
            "one water voxel means ONE tile -- a chunk has 64, and acquiring the rest is the " +
            "O(r^3) cost this design exists to remove");
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(want));
        Assert.AreEqual(1, st.TilesAcquired);
    }

    [Test]
    public void AWorldOfSolidTerrainAcquiresNothing()
    {
        // The sparsity claim, stated as its own test: no fluid, no tiles, no
        // matter how large the radius.
        for (int x = -2; x <= 2; x++)
        for (int z = -2; z <= 2; z++)
        {
            var c = new Chunk { coord = new int3(x, 0, z), isUniform = true, uniformMaterial = Materials.Stone };
            _store.InsertChunk(c);
        }
        var tiles = Tiles();

        var st = FluidTileResidency.Refresh(_store, tiles, new int3(64, 64, 64), 1280, 1472);

        Assert.AreEqual(0, tiles.ResidentTiles,
            "stone is not mobile; a radius over solid ground must cost nothing");
        Assert.Greater(st.ChunksScanned, 0, "and the scan must actually have run");
    }

    [Test]
    public void AnOceanChunkAcquiresAllSixtyFourOfItsTiles()
    {
        // The other extreme, and the one that decides whether the pool cap is
        // reachable: a uniform-water chunk is fluid everywhere.
        var c = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Water };
        _store.InsertChunk(c);
        var tiles = Tiles();

        FluidTileResidency.Refresh(_store, tiles, new int3(64, 64, 64), 512, 640);

        Assert.AreEqual(64, tiles.ResidentTiles, "a full chunk of water is 64 tiles");
    }

    // =====================================================================
    // Gate 1 -- §9.4 residency
    // =====================================================================

    [Test]
    public void FluidInANonResidentChunkIsNeverAcquired()
    {
        // §9.4. GetVoxel returns Air for a non-resident chunk and its own
        // contract calls that "deliberately ambiguous with real air", so a tile
        // admitted there would simulate against a lie. Phase 5d destroyed mass
        // this exact way.
        var c = AirChunk(int3.zero);
        int3 v = new int3(70, 40, 100);
        PlaceWater(v);
        Assert.AreNotEqual(0UL, c.fluidTileMask, "premise: the chunk knows it has fluid");

        _store.EvictChunk(int3.zero);
        Assert.IsFalse(_store.IsResident(int3.zero), "premise: it is gone");

        var tiles = Tiles();
        var st = FluidTileResidency.Refresh(_store, tiles, v, 512, 640);

        Assert.AreEqual(0, tiles.ResidentTiles, "no chunk, no tile");
        Assert.Greater(st.ChunksSkippedNonResident, 0, "and the skip is counted, not silent");
    }

    // =====================================================================
    // Gate 3 -- §7.4's radius, with hysteresis
    // =====================================================================

    [Test]
    public void FluidBeyondTheWakeRadiusIsNotAcquired()
    {
        AirChunk(int3.zero);
        AirChunk(new int3(3, 0, 0));
        int3 near = new int3(64, 64, 64);
        PlaceWater(near);
        int3 farV = new int3(3 * 128 + 64, 64, 64);        // ~384 voxels away
        PlaceWater(farV);

        var tiles = Tiles();
        FluidTileResidency.Refresh(_store, tiles, near, 128, 148);

        Assert.AreEqual(1, tiles.ResidentTiles, "only the near pool is in range");
    }

    [Test]
    public void ApproachingDistantFluidWakesIt_WhichIsTheWholeContract()
    {
        // §7.4: "on approach it wakes". This is the property the sparse design
        // nearly lost, and the reason ChunkFluidMask exists at all.
        AirChunk(int3.zero);
        AirChunk(new int3(3, 0, 0));
        int3 home = new int3(64, 64, 64);
        int3 farV = new int3(3 * 128 + 64, 64, 64);
        PlaceWater(home);
        int3 farTile = PlaceWater(farV);

        var tiles = Tiles();
        FluidTileResidency.Refresh(_store, tiles, home, 128, 148);
        Assert.AreEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(farTile), "asleep at distance");

        FluidTileResidency.Refresh(_store, tiles, farV, 128, 148);   // walk over to it
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(farTile),
            "and it MUST wake on approach -- settled fluid that can never wake again is the " +
            "defect this whole design note exists to avoid reintroducing");
    }

    [Test]
    public void DepartingReleasesTiles_ButOnlyOnceTheWholeTileIsBeyondTheSleepRadius()
    {
        AirChunk(int3.zero);
        AirChunk(new int3(3, 0, 0));
        int3 home = new int3(64, 64, 64);
        int3 farV = new int3(3 * 128 + 64, 64, 64);
        PlaceWater(home);
        PlaceWater(farV);

        var tiles = Tiles();
        FluidTileResidency.Refresh(_store, tiles, home, 128, 148);
        Assert.AreEqual(1, tiles.ResidentTiles);

        var st = FluidTileResidency.Refresh(_store, tiles, farV, 128, 148);
        Assert.AreEqual(1, st.TilesReleasedByRadius, "the home tile is left behind");
        Assert.AreEqual(1, tiles.ResidentTiles, "and the far one replaces it");
    }

    [Test]
    public void ATileInTheHysteresisBandKeepsWhateverStateItHas()
    {
        // Admit at the wake radius, release at the larger sleep radius. A tile
        // between them must not flip, or a player standing on the boundary
        // thrashes the pool every re-centre.
        AirChunk(int3.zero);
        int3 v = new int3(64, 64, 64);
        int3 tile = PlaceWater(v);
        var tiles = Tiles();

        // Distance chosen to sit between the two radii.
        int3 centre = v + new int3(140, 0, 0);
        FluidTileResidency.Refresh(_store, tiles, centre, 100, 200);
        Assert.AreEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(tile),
            "too far to be newly admitted");

        FluidTileResidency.Refresh(_store, tiles, v, 100, 200);            // arrive
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(tile));

        FluidTileResidency.Refresh(_store, tiles, centre, 100, 200);       // back to the band
        Assert.AreNotEqual(FluidTileMap.NO_TILE, tiles.TryGetSlot(tile),
            "and once admitted it must SURVIVE the band, not be released at the wake radius");
    }

    // =====================================================================
    // Capacity
    // =====================================================================

    [Test]
    public void PoolExhaustionRefusesCleanly_AndIsCounted()
    {
        var c = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Water };
        _store.InsertChunk(c);
        var tiles = Tiles(cap: 10);                       // far fewer than 64

        var st = FluidTileResidency.Refresh(_store, tiles, new int3(64, 64, 64), 512, 640);

        Assert.AreEqual(10, tiles.ResidentTiles, "exactly the cap, never more");
        Assert.Greater(st.TilesRefusedPoolFull, 0, "and every refusal is counted, never silent");
        Assert.Greater(tiles.PoolExhaustionsTotal, 0);
    }

    [Test]
    public void RefreshIsIdempotent_SoRepeatedTicksDoNotChurnThePool()
    {
        AirChunk(int3.zero);
        int3 v = new int3(70, 40, 100);
        PlaceWater(v);
        var tiles = Tiles();

        FluidTileResidency.Refresh(_store, tiles, v, 512, 640);
        long acquiredAfterFirst = tiles.TilesAcquiredTotal;

        for (int i = 0; i < 20; i++) FluidTileResidency.Refresh(_store, tiles, v, 512, 640);

        Assert.AreEqual(acquiredAfterFirst, tiles.TilesAcquiredTotal,
            "a stationary player must not re-acquire anything; churn here would be a slow " +
            "version of the boundary thrash hysteresis exists to prevent");
        Assert.AreEqual(1, tiles.ResidentTiles);
    }

    [Test]
    public void TheScanCostIsOneUlongPerChunk_NotOneTestPerCell()
    {
        // The cost claim, made checkable. 5x5 chunks of stone is 5x5x2M cells;
        // the scan must touch 25 masks and stop.
        for (int x = -2; x <= 2; x++)
        for (int z = -2; z <= 2; z++)
            _store.InsertChunk(new Chunk
            { coord = new int3(x, 0, z), isUniform = true, uniformMaterial = Materials.Stone });

        var tiles = Tiles();
        var st = FluidTileResidency.Refresh(_store, tiles, new int3(64, 64, 64), 300, 400);

        Assert.AreEqual(0, st.TilesWithFluid,
            "no tile was even considered: every chunk answered with one word");
        Assert.LessOrEqual(st.ChunksScanned, 25);
    }
}
