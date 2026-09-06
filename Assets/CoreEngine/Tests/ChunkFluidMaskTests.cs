// Assets/CoreEngine/Tests/ChunkFluidMaskTests.cs
//
// The wake signal for a sparse active set, proven against a synthetic chunk.
//
// DESIGN_NOTE_7_2 §9: with tiles, nothing sweeps dormant fluid, so a settled
// pool whose tile was released is invisible and never wakes. This mask is what
// answers "is there fluid in that tile" without touching its voxels.
//
// THE ERROR DIRECTION IS THE WHOLE POINT. Over-reporting costs one wasted
// acquire-then-free cycle. Under-reporting is fluid that never wakes again --
// the defect class this design exists not to reintroduce. Several tests below
// exist ONLY to pin that asymmetry, and they are the ones to keep if the file
// is ever trimmed.

using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class ChunkFluidMaskTests
{
    private const int CE = EngineConfig.CHUNK_EDGE_VOXELS;      // 128
    private const int BE = EngineConfig.BRICK_EDGE;             // 8

    /// A chunk with all-uniform bricks of `fill`, plus a pool big enough for
    /// any dense bricks a test creates.
    private static Chunk MakeChunk(int3 coord, byte fill, out BrickDataPool pool)
    {
        pool = new BrickDataPool(64);
        var c = new Chunk
        {
            coord = coord,
            isUniform = false,
            bricks = new BrickHandle[EngineConfig.BRICKS_PER_CHUNK],
        };
        for (int i = 0; i < c.bricks.Length; i++) c.bricks[i].data = fill;   // uniform, material=fill
        return c;
    }

    /// Turns one brick dense and writes `material` at one voxel inside it.
    private static void PokeDense(Chunk c, BrickDataPool pool, int3 worldVoxel, byte material)
    {
        int3 lb = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(worldVoxel));
        int bi = CoordMath.LocalBrickIndex(lb);
        int slot = pool.Alloc();
        var raw = pool.RawData;
        for (int i = 0; i < 512; i++) raw[slot * 512 + i] = Materials.Air;
        raw[slot * 512 + CoordMath.LocalVoxelIndex(CoordMath.LocalVoxelIndex3D(worldVoxel))] = material;
        c.bricks[bi].data = 0x80000000u | (uint)slot;
    }

    // =====================================================================
    // The bit convention
    // =====================================================================

    [Test]
    public void TheMaskIsExactlyOneBitPerTile_AndAChunkHas64()
    {
        Assert.AreEqual(4, ChunkFluidMask.TILES_PER_EDGE, "128 / 32");
        Assert.AreEqual(64, ChunkFluidMask.TILES_PER_CHUNK,
            "64 tiles is what makes the mask exactly one ulong -- the reason T=32 was chosen");
        Assert.AreEqual(0, CE % ChunkFluidMask.TILE_EDGE, "the tile must divide the chunk");
    }

    [Test]
    public void EveryTileMapsToADistinctBit()
    {
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int z = 0; z < 4; z++)
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            int bit = ChunkFluidMask.TileBit(new int3(x, y, z));
            Assert.IsTrue(bit >= 0 && bit < 64);
            Assert.IsTrue(seen.Add(bit), $"tile ({x},{y},{z}) collided on bit {bit}");
        }
        Assert.AreEqual(64, seen.Count);
    }

    [Test]
    public void AVoxelMapsToTheTileThatGeometricallyContainsIt()
    {
        // The convention has to agree with the geometry, or the mask describes
        // other tiles than the ones the radius query will look at.
        int3 chunk = new int3(3, 0, -2);
        for (int t = 0; t < 4; t++)
        {
            int3 tile = new int3(t, (t + 1) & 3, (t + 2) & 3);
            ChunkFluidMask.TileBounds(chunk, tile, out int3 lo, out int3 hi);
            foreach (int3 v in new[] { lo, hi, (lo + hi) / 2 })
                Assert.AreEqual(tile, ChunkFluidMask.TileInChunkOf(v),
                    $"voxel {v} must map back to tile {tile}");
        }
    }

    [Test]
    public void TileBoundsNeverCrossAChunkBoundary()
    {
        int3 chunk = new int3(-5, 0, 7);
        for (int z = 0; z < 4; z++)
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            ChunkFluidMask.TileBounds(chunk, new int3(x, y, z), out int3 lo, out int3 hi);
            Assert.AreEqual(chunk, CoordMath.VoxelToChunk(lo));
            Assert.AreEqual(chunk, CoordMath.VoxelToChunk(hi),
                "a tile spanning two chunks reintroduces the Phase 5d residency-edge bug");
        }
    }

    // =====================================================================
    // Rebuild -- exactness in both directions
    // =====================================================================

    [Test]
    public void AChunkOfSolidStone_HasNoFluidTiles()
    {
        var c = MakeChunk(int3.zero, Materials.Stone, out var pool);
        Assert.AreEqual(0UL, ChunkFluidMask.Rebuild(c, pool),
            "stone is not mobile; a mask that reported it would acquire tiles for the whole world");
        pool.Dispose();
    }

    [Test]
    public void AChunkOfAir_HasNoFluidTiles()
    {
        var c = MakeChunk(int3.zero, Materials.Air, out var pool);
        Assert.AreEqual(0UL, ChunkFluidMask.Rebuild(c, pool));
        pool.Dispose();
    }

    [Test]
    public void OneWaterVoxelInADenseBrick_SetsExactlyItsOwnTile()
    {
        // THE UNDER-REPORTING TEST. A single settled voxel is the hardest thing
        // to notice and exactly what must never be missed.
        var c = MakeChunk(int3.zero, Materials.Air, out var pool);
        int3 v = new int3(70, 40, 100);                     // tile (2,1,3)
        PokeDense(c, pool, v, Materials.Water);

        ulong mask = ChunkFluidMask.Rebuild(c, pool);
        int3 tile = ChunkFluidMask.TileInChunkOf(v);

        Assert.IsTrue(ChunkFluidMask.IsSet(mask, tile), $"tile {tile} holds the water and must be set");
        Assert.AreEqual(1, ChunkFluidMask.PopCount(mask),
            "and ONLY that tile -- a mask that set neighbours would acquire tiles with no fluid");
        pool.Dispose();
    }

    [Test]
    public void AUniformWaterBrick_SetsItsTile()
    {
        var c = MakeChunk(int3.zero, Materials.Air, out var pool);
        int3 v = new int3(10, 10, 10);
        int bi = CoordMath.LocalBrickIndex(CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(v)));
        c.bricks[bi].data = Materials.Water;               // uniform, material = water

        ulong mask = ChunkFluidMask.Rebuild(c, pool);
        Assert.IsTrue(ChunkFluidMask.IsSet(mask, ChunkFluidMask.TileInChunkOf(v)),
            "a uniform brick of water is 512 fluid voxels and must be seen without a byte scan");
        pool.Dispose();
    }

    [Test]
    public void SandCounts_BecauseCSPromoteGatesOnIsMobile_NotOnIsFluid()
    {
        // The mask must gate on the SAME predicate the CA promotes on, or the
        // two disagree about what is simulatable. Sand is a falling solid: mobile
        // but not a fluid.
        Assert.IsTrue(MaterialRules.IsMobile(Materials.Sand), "premise");
        Assert.IsFalse(MaterialRules.IsFluidMaterial(Materials.Sand), "premise");

        var c = MakeChunk(int3.zero, Materials.Air, out var pool);
        int3 v = new int3(5, 5, 5);
        PokeDense(c, pool, v, Materials.Sand);

        Assert.IsTrue(ChunkFluidMask.IsSet(ChunkFluidMask.Rebuild(c, pool),
                                           ChunkFluidMask.TileInChunkOf(v)),
            "sand must set the bit; gating on IsFluidMaterial would strand every sand pile");
        pool.Dispose();
    }

    [Test]
    public void AUniformFluidChunk_SetsEveryTile()
    {
        var c = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Water };
        Assert.AreEqual(ulong.MaxValue, ChunkFluidMask.Rebuild(c, null),
            "an ocean chunk is fluid everywhere");
    }

    // =====================================================================
    // Clearing -- the expensive direction, done exactly
    // =====================================================================

    [Test]
    public void RebuildTile_ClearsOnlyWhenTheTileIsGenuinelyEmpty()
    {
        var c = MakeChunk(int3.zero, Materials.Air, out var pool);
        int3 a = new int3(5, 5, 5), b = new int3(70, 40, 100);
        PokeDense(c, pool, a, Materials.Water);
        PokeDense(c, pool, b, Materials.Water);

        ulong mask = ChunkFluidMask.Rebuild(c, pool);
        Assert.AreEqual(2, ChunkFluidMask.PopCount(mask));

        // Drain tile A only.
        int3 lb = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(a));
        int slot = (int)(c.bricks[CoordMath.LocalBrickIndex(lb)].data & 0x3FFFFFFFu);
        var rawA = pool.RawData;
        rawA[slot * 512 + CoordMath.LocalVoxelIndex(CoordMath.LocalVoxelIndex3D(a))] = Materials.Air;

        mask = ChunkFluidMask.RebuildTile(c, pool, ChunkFluidMask.TileInChunkOf(a), mask);
        Assert.IsFalse(ChunkFluidMask.IsSet(mask, ChunkFluidMask.TileInChunkOf(a)), "A drained");
        Assert.IsTrue(ChunkFluidMask.IsSet(mask, ChunkFluidMask.TileInChunkOf(b)),
            "B still holds water and must NOT be cleared by A's rescan");
        pool.Dispose();
    }

    [Test]
    public void RebuildTile_DoesNotClearATileThatStillHoldsFluidElsewhereInIt()
    {
        // A tile is 4x4x4 bricks. Draining one brick must not clear the tile
        // while another brick in it still holds water -- that is precisely an
        // under-report, i.e. fluid that never wakes.
        var c = MakeChunk(int3.zero, Materials.Air, out var pool);
        int3 v1 = new int3(1, 1, 1);                    // tile (0,0,0), brick (0,0,0)
        int3 v2 = new int3(1, 1, 20);                   // tile (0,0,0), a DIFFERENT brick
        Assert.AreEqual(ChunkFluidMask.TileInChunkOf(v1), ChunkFluidMask.TileInChunkOf(v2), "premise");

        PokeDense(c, pool, v1, Materials.Water);
        PokeDense(c, pool, v2, Materials.Water);
        ulong mask = ChunkFluidMask.Rebuild(c, pool);

        int3 lb = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(v1));
        int slot = (int)(c.bricks[CoordMath.LocalBrickIndex(lb)].data & 0x3FFFFFFFu);
        var raw1 = pool.RawData;
        raw1[slot * 512 + CoordMath.LocalVoxelIndex(CoordMath.LocalVoxelIndex3D(v1))] = Materials.Air;

        mask = ChunkFluidMask.RebuildTile(c, pool, ChunkFluidMask.TileInChunkOf(v1), mask);
        Assert.IsTrue(ChunkFluidMask.IsSet(mask, ChunkFluidMask.TileInChunkOf(v1)),
            "the tile still holds water in another brick and must stay set");
        pool.Dispose();
    }

    // =====================================================================
    // The safe direction, pinned
    // =====================================================================

    [Test]
    public void WithoutAPool_ADenseBrickIsAssumedToHoldFluid()
    {
        // Cannot tell -> say yes. Over-reporting costs an acquire-and-free;
        // under-reporting strands fluid forever.
        var c = MakeChunk(int3.zero, Materials.Air, out var pool);
        c.bricks[0].data = 0x80000000u | 7u;               // dense, unreadable without the pool

        Assert.AreNotEqual(0UL, ChunkFluidMask.Rebuild(c, null),
            "an unreadable dense brick must err toward 'there might be fluid here'");
        pool.Dispose();
    }

    [Test]
    public void WithVoxel_IsPurelyAdditive()
    {
        // The SetVoxel path only ever sets. Clearing is RebuildTile's job,
        // because clearing correctly needs a scan and guessing it wrong strands
        // fluid.
        ulong m = 0UL;
        int3 v = new int3(40, 40, 40);
        m = ChunkFluidMask.WithVoxel(m, v);
        ulong once = m;
        m = ChunkFluidMask.WithVoxel(m, v);
        Assert.AreEqual(once, m, "idempotent");
        Assert.IsTrue(ChunkFluidMask.HasFluidAt(m, v));
        Assert.AreEqual(1, ChunkFluidMask.PopCount(m));
    }

    [Test]
    public void AbsoluteTileCoordinates_AreContiguousAcrossChunks()
    {
        // FluidTileMap indexes by absolute tile coord, so chunk (0,0,0) tile
        // (3,0,0) and chunk (1,0,0) tile (0,0,0) must be adjacent, not equal.
        int3 a = ChunkFluidMask.AbsoluteTile(new int3(0, 0, 0), new int3(3, 0, 0));
        int3 b = ChunkFluidMask.AbsoluteTile(new int3(1, 0, 0), new int3(0, 0, 0));
        Assert.AreEqual(a + new int3(1, 0, 0), b, "tile space must be continuous across chunks");
    }

    // =====================================================================
    // Maintenance through the real ChunkStore
    // =====================================================================

    private static ChunkStore Store(out BrickDataPool pool, out ChunkHandleAllocator alloc)
    {
        pool = new BrickDataPool(4096, rangeAware: true);
        alloc = new ChunkHandleAllocator(8);
        return new ChunkStore(pool, alloc);
    }

    [Test]
    public void SetVoxel_SetsTheBitForAMobileWrite()
    {
        var store = Store(out var pool, out var alloc);
        int3 c = int3.zero;
        var chunk = new Chunk { coord = c, isUniform = true, uniformMaterial = Materials.Air };
        store.InsertChunk(chunk);
        Assert.AreEqual(0UL, chunk.fluidTileMask, "an air chunk starts with no fluid tiles");

        int3 v = new int3(70, 40, 100);
        store.SetVoxel(v, Materials.Water);

        Assert.IsTrue(ChunkFluidMask.HasFluidAt(chunk.fluidTileMask, v),
            "the only terrain writer must record the wake signal");
        pool.Dispose();
    }

    [Test]
    public void SetVoxel_DoesNotSetABitForASolidWrite()
    {
        var store = Store(out var pool, out var alloc);
        var chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air };
        store.InsertChunk(chunk);

        store.SetVoxel(new int3(70, 40, 100), Materials.Stone);
        Assert.AreEqual(0UL, chunk.fluidTileMask,
            "digging and building must not make the whole world look like fluid");
        pool.Dispose();
    }

    [Test]
    public void ClearingTheLastFluidVoxel_ResolvesToAClearBit_ButOnlyOffTheWritePath()
    {
        var store = Store(out var pool, out var alloc);
        var chunk = new Chunk { coord = int3.zero, isUniform = true, uniformMaterial = Materials.Air };
        store.InsertChunk(chunk);

        int3 v = new int3(70, 40, 100);
        store.SetVoxel(v, Materials.Water);
        store.SetVoxel(v, Materials.Air);

        // Still set: clearing is deferred, and that is the SAFE direction.
        Assert.IsTrue(ChunkFluidMask.HasFluidAt(chunk.fluidTileMask, v),
            "the write path must not pay for a brick scan");
        Assert.AreNotEqual(0UL, chunk.fluidTileDirty, "but it must record the suspicion");

        store.ResolveDirtyFluidTiles(chunk, budget: 64);

        Assert.IsFalse(ChunkFluidMask.HasFluidAt(chunk.fluidTileMask, v),
            "and the off-path resolve must clear it exactly");
        Assert.AreEqual(0UL, chunk.fluidTileDirty);
        pool.Dispose();
    }

    [Test]
    public void InsertChunk_BuildsTheMask_SoAReloadedChunkIsNotBlind()
    {
        // Delta decode writes straight into a chunk's bricks, NOT through
        // SetVoxel, so hooking the writer alone would leave every reloaded edit
        // invisible to the wake scan. Insert is the one point both paths share.
        var store = Store(out var pool, out var alloc);
        var chunk = new Chunk
        {
            coord = int3.zero,
            isUniform = false,
            bricks = new BrickHandle[EngineConfig.BRICKS_PER_CHUNK],
        };
        for (int i = 0; i < chunk.bricks.Length; i++) chunk.bricks[i].data = Materials.Air;
        int3 v = new int3(70, 40, 100);
        int bi = CoordMath.LocalBrickIndex(CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(v)));
        chunk.bricks[bi].data = Materials.Water;              // as if a delta had restored it

        store.InsertChunk(chunk);

        Assert.IsTrue(ChunkFluidMask.HasFluidAt(chunk.fluidTileMask, v),
            "a chunk arriving with fluid already in it must be seen at insert");
        pool.Dispose();
    }
}
