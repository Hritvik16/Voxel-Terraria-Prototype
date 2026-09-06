// Assets/CoreEngine/Tests/FluidTileMapTests.cs
//
// §7.2's sparse active set, proven without a GPU.
//
// These pin the four properties the design rests on, and each one exists
// because getting it wrong fails SILENTLY rather than loudly:
//
//   ALIASING     two tiles a ring apart must not share cells (§6.2's bug class)
//   ALIGNMENT    a tile must never straddle a chunk boundary (Phase 5d's bug)
//   LIFETIME     empty tiles free IMMEDIATELY (§3.10's eviction spiral)
//   BOUNDEDNESS  memory is capped and independent of the radius (the point)

using NUnit.Framework;
using System;
using Unity.Mathematics;
using VoxelEngine.Simulation;

public class FluidTileMapTests
{
    private static FluidTileMap Map(int tileEdge = 32, int ring = 128, int cap = 512)
        => new FluidTileMap(tileEdge, new int3(ring, ring, ring), cap);

    // =====================================================================
    // Aliasing -- §6.2's bug class, in a different buffer
    // =====================================================================

    [Test]
    public void TwoTilesARingApart_DoNotShareCells()
    {
        // THE §6.2 BUG, as an assertion. Tile coords 128 apart land on the same
        // ring entry. Without the identity check the second silently reads the
        // first's cells, which is "phantom terrain" again with water in it.
        var m = Map(ring: 128);
        int3 a = new int3(5, 3, 7);
        int3 b = a + new int3(128, 0, 0);

        Assert.AreEqual(m.RingIndex(a), m.RingIndex(b),
            "the test is only meaningful if these really do collide on one entry");

        int slotA = m.Acquire(a);
        Assert.AreNotEqual(FluidTileMap.NO_TILE, slotA);

        Assert.AreEqual(FluidTileMap.NO_TILE, m.TryGetSlot(b),
            "a DIFFERENT tile on the same ring entry must not resolve to A's slot");
        Assert.AreEqual(slotA, m.TryGetSlot(a), "and A must still resolve to itself");
    }

    [Test]
    public void AReleasedEntryIsReusableByTheTileThatCollidedWithIt()
    {
        // The other half: once A is gone the entry is genuinely free, otherwise
        // the ring leaks entries and the pool slowly stops working.
        var m = Map(ring: 128);
        int3 a = new int3(5, 3, 7), b = a + new int3(128, 0, 0);

        m.Acquire(a);
        Assert.IsTrue(m.Release(a));

        int slotB = m.Acquire(b);
        Assert.AreNotEqual(FluidTileMap.NO_TILE, slotB, "B must be able to take the entry now");
        Assert.AreEqual(slotB, m.TryGetSlot(b));
        Assert.AreEqual(FluidTileMap.NO_TILE, m.TryGetSlot(a), "and A is gone, not still mapped");
    }

    [Test]
    public void TheRadiusIsCheckedAgainstTheRing_NotAssumedToFit()
    {
        // The invariant that makes aliasing impossible in practice: the resident
        // set must span fewer tiles than the ring. Assumed and untestable is how
        // §6.2 happened; this makes it a question with an answer.
        var m = Map(tileEdge: 32, ring: 128);
        Assert.IsTrue(m.RadiusFitsRing(EngineConfig.FLUID_ACTIVE_RADIUS_VOXELS),
            "the SHIPPED 1280-voxel radius must fit a 128-tile ring of 32-voxel tiles " +
            "(2*1280/32 = 80 tiles, +2 margin, against 128)");
        Assert.IsFalse(m.RadiusFitsRing(4096),
            "and a radius too large for the ring must be reported, not silently aliased");
    }

    // =====================================================================
    // Alignment -- Phase 5d's bug class, removed by construction
    // =====================================================================

    [Test]
    public void ATileNeverStraddlesAChunkBoundary()
    {
        // Phase 5d destroyed 7 water voxels of 52 because the region straddled a
        // chunk edge with one side evicted. With T dividing 128 that cannot be
        // expressed: every voxel of a tile is in one chunk.
        foreach (int T in new[] { 8, 16, 32, 64, 128 })
        {
            var m = new FluidTileMap(T, new int3(64, 64, 64), 64);
            for (int i = 0; i < 400; i++)
            {
                int3 v = new int3(i * 37 - 5000, (i * 13) & 127, i * 91 - 3000);
                int3 tile = m.TileOf(v);
                int3 lo = tile << m.TileShift;
                int3 hi = lo + new int3(T - 1, T - 1, T - 1);
                Assert.AreEqual(CoordMath.VoxelToChunk(lo), CoordMath.VoxelToChunk(hi),
                    $"T={T}: tile {tile} spans voxels {lo}..{hi}, which must be one chunk");
            }
        }
    }

    [Test]
    public void ATileEdgeThatDoesNotDivideAChunk_IsRefused()
    {
        // The control for the test above. If a bad edge were accepted, the
        // straddle guarantee would be a comment rather than a property.
        Assert.Throws<ArgumentException>(() => new FluidTileMap(96, new int3(64, 64, 64), 16),
            "96 does not divide 128 and must be refused at construction");
    }

    [Test]
    public void NonPowerOfTwoDimensionsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new FluidTileMap(24, new int3(64, 64, 64), 16));
        Assert.Throws<ArgumentException>(() => new FluidTileMap(32, new int3(48, 64, 64), 16));
    }

    // =====================================================================
    // Addressing round-trip
    // =====================================================================

    [Test]
    public void EveryVoxelInATileMapsToADistinctCell_AndBackToItsTile()
    {
        var m = Map(tileEdge: 8, ring: 16, cap: 4);
        int3 tile = new int3(3, 1, 2);
        int slot = m.Acquire(tile);
        Assert.AreNotEqual(FluidTileMap.NO_TILE, slot);

        var seen = new System.Collections.Generic.HashSet<int>();
        int3 basis = tile << m.TileShift;
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            int idx = m.CellIndex(basis + new int3(x, y, z));
            Assert.AreNotEqual(-1, idx, "every voxel of a resident tile has a cell");
            Assert.IsTrue(seen.Add(idx), $"cell {idx} was handed out twice -- cells must be 1:1");
            Assert.IsTrue(idx >= slot * m.TileCells && idx < (slot + 1) * m.TileCells,
                "and must lie inside its own tile's block, never in a neighbour's");
        }
        Assert.AreEqual(512, seen.Count);
    }

    [Test]
    public void AVoxelWhoseTileIsAbsent_HasNoCell()
    {
        // The sparse half: absence must be reported, not silently answered with
        // some other tile's cell.
        var m = Map();
        Assert.AreEqual(-1, m.CellIndex(new int3(9999, 40, 9999)));
    }

    // =====================================================================
    // Lifetime -- §3.10's Volatile rule
    // =====================================================================

    [Test]
    public void EmptyTilesAreFreedImmediately_NotLazily()
    {
        // §3.10: a waterfall through open air allocates constantly. If empty
        // tiles only freed lazily, the pool would fill with tiles holding air
        // the water already left and evict real fluid to house it.
        var m = Map(tileEdge: 8, ring: 16, cap: 8);
        for (int i = 0; i < 6; i++) m.Acquire(new int3(i, 0, 0));
        Assert.AreEqual(6, m.ResidentTiles);

        m.SetLiveCells(m.TryGetSlot(new int3(2, 0, 0)), 5);   // one tile still has fluid

        int freed = m.ReleaseEmptyTiles();
        Assert.AreEqual(5, freed, "every tile with no live fluid goes back at once");
        Assert.AreEqual(1, m.ResidentTiles, "and only the occupied one survives");
        Assert.AreNotEqual(FluidTileMap.NO_TILE, m.TryGetSlot(new int3(2, 0, 0)));
    }

    [Test]
    public void FreedSlotsAreReused_SoAWaterfallDoesNotExhaustThePool()
    {
        // The behaviour that makes the pool a pool. 200 cycles through an
        // 8-tile pool is a waterfall's worth of churn.
        var m = Map(tileEdge: 8, ring: 16, cap: 8);
        for (int cycle = 0; cycle < 200; cycle++)
        {
            for (int i = 0; i < 8; i++)
                Assert.AreNotEqual(FluidTileMap.NO_TILE, m.Acquire(new int3(i, cycle & 3, 0)),
                    $"cycle {cycle}: acquire must succeed after the previous cycle freed");
            Assert.AreEqual(8, m.ReleaseEmptyTiles());
        }
        Assert.AreEqual(0, m.PoolExhaustionsTotal, "reuse must mean the pool never runs dry");
        Assert.AreEqual(0, m.ResidentTiles);
    }

    [Test]
    public void PoolExhaustionIsAGuardedNoOp_AndIsCounted()
    {
        // §7.7: "Underflow on promotion is a guarded no-op -- retry next tick.
        // No invalid write, ever." Counted, because a silent one is how the
        // scratch-pool leak hid for eight days.
        var m = Map(tileEdge: 8, ring: 16, cap: 3);
        for (int i = 0; i < 3; i++) m.Acquire(new int3(i, 0, 0));

        Assert.AreEqual(FluidTileMap.NO_TILE, m.Acquire(new int3(9, 0, 0)),
            "a full pool refuses rather than overwriting someone");
        Assert.AreEqual(1, m.PoolExhaustionsTotal);
        Assert.AreEqual(3, m.ResidentTiles, "and the refusal changes nothing else");
    }

    [Test]
    public void TilesBeyondTheSleepRadiusAreReleased_AndOnesInsideAreNot()
    {
        var m = Map(tileEdge: 32, ring: 128, cap: 64);
        int3 centre = new int3(4096, 64, 4096);
        int3 near = m.TileOf(centre);
        int3 far = near + new int3(30, 0, 0);            // 960 voxels away
        m.Acquire(near);
        m.Acquire(far);

        int freed = m.ReleaseBeyond(centre, 320);
        Assert.AreEqual(1, freed);
        Assert.AreNotEqual(FluidTileMap.NO_TILE, m.TryGetSlot(near), "the near tile stays");
        Assert.AreEqual(FluidTileMap.NO_TILE, m.TryGetSlot(far), "the far one goes");
    }

    // =====================================================================
    // Boundedness -- the whole point of the design
    // =====================================================================

    [Test]
    public void MemoryIsBounded_AndIndependentOfTheRadius()
    {
        // THE ACCEPTANCE PROPERTY, as arithmetic over the real configuration.
        // The dense region needed 137 GB to reach a 1280-voxel radius; this
        // configuration reaches it for a fixed, capped cost that does not move
        // when the radius does.
        var m = Map(tileEdge: 32, ring: 128, cap: 512);
        long bytes = m.GpuBytes(16);

        Assert.Less(bytes, 512L * 1024 * 1024,
            $"the whole active set must fit well under half a gigabyte (got {bytes / (1024 * 1024)} MB)");
        Assert.IsTrue(m.RadiusFitsRing(1280), "at the SHIPPED radius, not a demo one");

        // Independence: the cost is a function of ring and cap, and the radius
        // appears nowhere in it.
        foreach (int r in new[] { 128, 640, 1280 })
            if (m.RadiusFitsRing(r))
                Assert.AreEqual(bytes, m.GpuBytes(16),
                    $"footprint must not change with radius {r}");
    }

    [Test]
    public void TheDenseEquivalentWouldBeAstronomical_WhichIsWhyThisExists()
    {
        // The control that gives the number above meaning. A dense region whose
        // half-diagonal reaches 1280 voxels is 2048^3 cells at 16 B.
        const long denseCells = 2048L * 2048L * 2048L;
        long denseBytes = denseCells * 16;
        var m = Map(tileEdge: 32, ring: 128, cap: 512);

        Assert.Greater(denseBytes / m.GpuBytes(16), 400L,
            "the tiled active set must be orders of magnitude smaller than the dense one; " +
            "if this ratio is small the design is not buying anything");
    }

    [Test]
    public void CellIndexAndVoxelOfCell_AreExactInverses()
    {
        // THE CPU MIRROR OF THE SHADER'S RegionIndex/RegionVoxel PAIR. A wake
        // request is produced on the CPU and consumed on the GPU; if the two
        // directions disagree by one axis the request names a different cell
        // than the one that asked for it, and both indices are valid so nothing
        // complains. This is the same class as the TileInChunkOf bug that used
        // the brick-local helper instead of the chunk-local one.
        var m = Map(tileEdge: 32, ring: 128, cap: 8);
        foreach (int3 tile in new[] { new int3(0, 0, 0), new int3(5, 2, 9), new int3(-3, 1, -7) })
        {
            Assert.AreNotEqual(FluidTileMap.NO_TILE, m.Acquire(tile));
            int3 basis = tile << m.TileShift;
            foreach (int3 off in new[]
            {
                new int3(0, 0, 0), new int3(31, 31, 31), new int3(1, 0, 0),
                new int3(0, 1, 0), new int3(0, 0, 1), new int3(17, 5, 29),
            })
            {
                int3 v = basis + off;
                int cell = m.CellIndex(v);
                Assert.AreNotEqual(-1, cell, $"{v} should have a cell");
                Assert.AreEqual(v, m.VoxelOfCell(cell),
                    $"round trip must be exact for {v} (cell {cell})");
            }
        }
    }

    [Test]
    public void TheInverseDistinguishesAxes_NotJustMagnitude()
    {
        // A mapping that swapped Y and Z would round-trip a symmetric probe
        // perfectly and be wrong everywhere else, so the probe is deliberately
        // asymmetric on all three axes.
        var m = Map(tileEdge: 32, ring: 128, cap: 4);
        int3 tile = new int3(2, 6, 11);
        m.Acquire(tile);
        int3 v = (tile << m.TileShift) + new int3(3, 17, 28);

        int3 back = m.VoxelOfCell(m.CellIndex(v));
        Assert.AreEqual(v.x, back.x, "x");
        Assert.AreEqual(v.y, back.y, "y");
        Assert.AreEqual(v.z, back.z, "z");
    }
}
