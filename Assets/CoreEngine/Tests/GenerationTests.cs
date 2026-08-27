// Assets/CoreEngine/Tests/GenerationTests.cs
//
// Phase 3, file 6 of the spec's ordered list (§13 Phase 3): the 3a suite.
// Per the phase's own discipline these tests must be GREEN BEFORE the new
// generator output is ever viewed through the renderer ("CPU-proven before
// drawn"). §13 Phase 3 acceptance assertions covered here:
//
//   - Determinism: byte-identical (via content hash) across repeated calls
//     AND across opposite visit orders.
//   - Uniform/dense distribution sane: dense fraction is surface-skin scale,
//     not whole-chunk.
//   - Biome strata correct per column.
//   - A feature anchor produces dense bricks only where it intersects.
//   - Plus: static water only at/below sea level; world.meta round-trip with
//     CRC corruption detection; AnchorPlanner determinism + non-overlap.
//
// The single highest-value test here is FullChunk_MatchesPerVoxelOracle: it
// walks EVERY voxel of generated chunks and compares against the pure
// per-voxel rule — this is what catches a uniform-classification predicate
// drifting out of sync with the fill rule (the classic bug class this
// generator's structure invites).
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

public class GenerationTests
{
    private const uint SEED = 42;
    private const byte SIZE_CLASS = 0;

    // Chunk coords chosen to cover: ocean corner, island interior, and the
    // region center (most likely to be inside anchor influence).
    private static readonly int3[] SampleCoords =
    {
        new int3(0, 0, 0),    // ocean corner
        new int3(11, 0, 11),  // region center (island interior)
        new int3(6, 0, 14),   // mid
        new int3(16, 0, 7),   // mid
        new int3(21, 0, 21),  // ocean corner
    };

    // ---------- helpers ----------

    private static Chunk Gen(WorldMetaData meta, int3 coord, ChunkHandleAllocator alloc, BrickDataPool pool)
    {
        var chunk = new Chunk();
        ChunkGeneratorFull.GenerateChunkFull(meta, coord, chunk, alloc, pool);
        return chunk;
    }

    private static byte ReadVoxel(Chunk chunk, BrickDataPool pool, int3 worldVoxel)
    {
        if (chunk.isUniform) return chunk.uniformMaterial;
        int3 localBrick = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(worldVoxel));
        int brickFlatIndex = CoordMath.LocalBrickIndex(localBrick);
        uint handleData = chunk.bricks[brickFlatIndex].data;
        if ((handleData & 0x80000000) == 0) return (byte)(handleData & 0xFF);
        int poolIndex = (int)(handleData & 0x3FFFFFFF);
        int voxelFlatIndex = CoordMath.LocalVoxelIndex(CoordMath.LocalVoxelIndex3D(worldVoxel));
        return pool.RawData[(poolIndex * 512) + voxelFlatIndex];
    }

    private static FeatureAnchor[] CavesOf(WorldMetaData meta)
    {
        var list = new List<FeatureAnchor>();
        foreach (var a in meta.anchors)
            if (a.kind == FeatureKind.Cave) list.Add(a);
        return list.ToArray();
    }

    private static float DenseFraction(Chunk chunk)
    {
        int dense = 0;
        for (int i = 0; i < 4096; i++)
            if ((chunk.bricks[i].data & 0x80000000) != 0) dense++;
        return dense / 4096f;
    }

    // ---------- determinism (§13 Phase 3, assertion 1) ----------

    [Test]
    public void Determinism_RepeatedCalls_ContentHashIdentical()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        foreach (var coord in SampleCoords)
        {
            var poolA = new BrickDataPool(20000);
            var poolB = new BrickDataPool(20000);
            try
            {
                var a = Gen(meta, coord, new ChunkHandleAllocator(2), poolA);
                var b = Gen(meta, coord, new ChunkHandleAllocator(2), poolB);
                Assert.AreEqual(ChunkContentHash.Hash(a, poolA), ChunkContentHash.Hash(b, poolB),
                    $"Repeated generation of chunk {coord} produced different content.");
            }
            finally { poolA.Dispose(); poolB.Dispose(); }
        }
    }

    [Test]
    public void Determinism_OppositeVisitOrders_ContentHashIdentical()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);

        // A 3x3 block around the island center, visited forward then reversed.
        var coords = new List<int3>();
        for (int z = 10; z <= 12; z++)
            for (int x = 10; x <= 12; x++)
                coords.Add(new int3(x, 0, z));

        var poolA = new BrickDataPool(40000);
        var poolB = new BrickDataPool(40000);
        try
        {
            var allocA = new ChunkHandleAllocator(16);
            var allocB = new ChunkHandleAllocator(16);
            var hashesA = new Dictionary<int3, uint>();
            var hashesB = new Dictionary<int3, uint>();

            foreach (var c in coords)
                hashesA[c] = ChunkContentHash.Hash(Gen(meta, c, allocA, poolA), poolA);

            coords.Reverse();
            foreach (var c in coords)
                hashesB[c] = ChunkContentHash.Hash(Gen(meta, c, allocB, poolB), poolB);

            foreach (var c in coords)
                Assert.AreEqual(hashesA[c], hashesB[c],
                    $"Chunk {c} content differs between forward and reverse visit order — a generation step is impure (§13 Phase 3 failure signature).");
        }
        finally { poolA.Dispose(); poolB.Dispose(); }
    }

    // ---------- the per-voxel oracle (strongest consistency check) ----------

    [Test]
    public void FullChunk_MatchesPerVoxelOracle()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        var st = ColumnSampler.CreateState(meta);
        var caves = CavesOf(meta);

        foreach (var coord in SampleCoords)
        {
            var pool = new BrickDataPool(20000);
            try
            {
                var chunk = Gen(meta, coord, new ChunkHandleAllocator(2), pool);
                int3 baseVoxel = coord * 128;
                int mismatches = 0;
                string firstMismatch = null;

                for (int lz = 0; lz < 128; lz++)
                for (int lx = 0; lx < 128; lx++)
                {
                    int wx = baseVoxel.x + lx, wz = baseVoxel.z + lz;
                    ColumnSampler.SampleColumn(in st, wx, wz, out int h, out byte biome);
                    for (int ly = 0; ly < 128; ly++)
                    {
                        int wy = baseVoxel.y + ly;
                        byte expected = ChunkGeneratorFull.VoxelMaterial(wx, wy, wz, h, biome, caves, testCaves: true);
                        byte actual = ReadVoxel(chunk, pool, new int3(wx, wy, wz));
                        if (expected != actual)
                        {
                            mismatches++;
                            firstMismatch ??= $"chunk {coord} voxel ({wx},{wy},{wz}): expected {expected}, stored {actual} (colH={h}, biome={biome})";
                        }
                    }
                }
                Assert.AreEqual(0, mismatches,
                    $"Generated chunk disagrees with the per-voxel rule at {mismatches} voxels. " +
                    $"A uniform-classification predicate has drifted from VoxelMaterial. First: {firstMismatch}");
            }
            finally { pool.Dispose(); }
        }
    }

    // ---------- uniform/dense distribution (§13 Phase 3, assertion 2) ----------

    [Test]
    public void DenseFraction_IsSurfaceSkinScale_NotWholeChunk()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        float sum = 0f;
        foreach (var coord in SampleCoords)
        {
            var pool = new BrickDataPool(20000);
            try
            {
                var chunk = Gen(meta, coord, new ChunkHandleAllocator(2), pool);
                float f = DenseFraction(chunk);
                sum += f;
                Assert.Less(f, 0.5f, $"Chunk {coord} dense fraction {f:P1} — not surface-skin scale.");
                Assert.Greater(f, 0f, $"Chunk {coord} has ZERO dense bricks — no surface skin at all is wrong for this world.");
            }
            finally { pool.Dispose(); }
        }
        Assert.Less(sum / SampleCoords.Length, 0.35f,
            "Mean dense fraction across sampled chunks exceeds 35% — dense is not confined to surfaces.");
    }

    // ---------- biome strata (§13 Phase 3, assertion 3) ----------

    [Test]
    public void BiomeStrata_CorrectPerColumn_SyntheticSingleBiomeWorlds()
    {
        const int SEA = WorldGenConstants.SEA_LEVEL_VOXEL_Y;
        const int DEEP = WorldGenConstants.DEEP_STRATUM_TOP_Y;
        const int SURF = WorldGenConstants.SURFACE_STRATUM_THICKNESS;

        foreach (var biome in Biomes.Table)
        {
            // Synthetic meta: NO anchors, ONE biome seed => whole world is this
            // biome, terrain is pure base heightfield. Strata semantics are then
            // checkable column-by-column with no confounds.
            var meta = new WorldMetaData
            {
                seed = SEED, sizeClass = SIZE_CLASS,
                formatVersion = WorldMeta.FORMAT_VERSION,
                contentVersionHash = WorldGenConstants.ContentVersionHash(),
                anchors = Array.Empty<FeatureAnchor>(),
                biomeSeeds = new[] { new BiomeSeed { x = 1408f, z = 1408f, biomeId = biome.id } },
            };
            var st = ColumnSampler.CreateState(meta);

            var pool = new BrickDataPool(20000);
            try
            {
                // Interior chunk: columns well above sea level.
                int3 coord = new int3(11, 0, 11);
                var chunk = Gen(meta, coord, new ChunkHandleAllocator(2), pool);
                int3 baseVoxel = coord * 128;

                // Spot-check a grid of columns (every 16th) across the chunk.
                for (int lz = 0; lz < 128; lz += 16)
                for (int lx = 0; lx < 128; lx += 16)
                {
                    int wx = baseVoxel.x + lx, wz = baseVoxel.z + lz;
                    ColumnSampler.SampleColumn(in st, wx, wz, out int h, out byte b);
                    Assert.AreEqual(biome.id, b, "Single-seed world produced a different biome.");

                    for (int wy = 0; wy < 128; wy++)
                    {
                        byte mat = ReadVoxel(chunk, pool, new int3(wx, wy, wz));
                        byte expected;
                        if (wy <= h)
                        {
                            if (wy < DEEP) expected = biome.deepMaterial;
                            else if (h - wy < SURF) expected = biome.surfaceMaterial;
                            else expected = biome.bulkMaterial;
                        }
                        else expected = wy <= SEA ? Materials.Water : Materials.Air;

                        Assert.AreEqual(expected, mat,
                            $"{biome.name} column ({wx},{wz}) h={h}: y={wy} expected {expected}, got {mat}.");
                    }
                }
            }
            finally { pool.Dispose(); }
        }
    }

    // ---------- static water (§5.5) ----------

    [Test]
    public void Water_PresentBeyondCoast_AndNeverAboveSeaLevel()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        const int SEA = WorldGenConstants.SEA_LEVEL_VOXEL_Y;

        foreach (var coord in new[] { new int3(0, 0, 0), new int3(11, 0, 11) })
        {
            var pool = new BrickDataPool(20000);
            try
            {
                var chunk = Gen(meta, coord, new ChunkHandleAllocator(2), pool);
                int waterCount = 0, aboveSeaViolations = 0;
                int3 baseVoxel = coord * 128;

                for (int lz = 0; lz < 128; lz += 4)
                for (int lx = 0; lx < 128; lx += 4)
                for (int ly = 0; ly < 128; ly++)
                {
                    byte mat = ReadVoxel(chunk, pool, baseVoxel + new int3(lx, ly, lz));
                    if (mat == Materials.Water)
                    {
                        waterCount++;
                        if (baseVoxel.y + ly > SEA) aboveSeaViolations++;
                    }
                }

                Assert.AreEqual(0, aboveSeaViolations, $"Chunk {coord}: water found ABOVE sea level.");
                if (coord.Equals(new int3(0, 0, 0)))
                    Assert.Greater(waterCount, 0, "Ocean-corner chunk contains no water — coastline/sea fill is broken.");
            }
            finally { pool.Dispose(); }
        }
    }

    // ---------- feature anchors (§13 Phase 3, assertion 4) ----------

    [Test]
    public void CaveAnchor_ChangesBricksOnlyWhereItIntersects()
    {
        // Same world with and without exactly one synthetic cave. The brick-level
        // classification difference must lie entirely inside the cave's
        // conservative AABB, and the cave interior must actually be air.
        int3 coord = new int3(11, 0, 11);
        int3 baseVoxel = coord * 128;
        var cave = new FeatureAnchor
        {
            kind = FeatureKind.Cave,
            cx = baseVoxel.x + 64f, cy = 34f, cz = baseVoxel.z + 64f,
            radius = 7f, magnitude = 0f,
            dirX = 1f, dirZ = 0f, halfLength = 30f, salt = 1234u,
        };

        WorldMetaData MakeMeta(bool withCave) => new WorldMetaData
        {
            seed = SEED, sizeClass = SIZE_CLASS,
            formatVersion = WorldMeta.FORMAT_VERSION,
            contentVersionHash = WorldGenConstants.ContentVersionHash(),
            anchors = withCave ? new[] { cave } : Array.Empty<FeatureAnchor>(),
            biomeSeeds = new[] { new BiomeSeed { x = 1408f, z = 1408f, biomeId = Biomes.ForestId } },
        };

        var poolA = new BrickDataPool(20000);
        var poolB = new BrickDataPool(20000);
        try
        {
            var without = Gen(MakeMeta(false), coord, new ChunkHandleAllocator(2), poolA);
            var with = Gen(MakeMeta(true), coord, new ChunkHandleAllocator(2), poolB);

            FeatureCarve.CaveAabb(in cave, out float3 mn, out float3 mx);
            int differing = 0;
            for (int i = 0; i < 4096; i++)
            {
                uint a = without.bricks[i].data;
                uint b = with.bricks[i].data;
                bool denseA = (a & 0x80000000) != 0;
                bool denseB = (b & 0x80000000) != 0;

                // Compare CLASSIFICATION (uniform material or dense-ness), not raw
                // pool indices which legitimately differ between pools.
                bool sameClass = denseA == denseB && (denseA || a == b);
                if (denseA && denseB)
                {
                    // Both dense: compare bodies byte-for-byte.
                    int sa = (int)(a & 0x3FFFFFFF) * 512, sb = (int)(b & 0x3FFFFFFF) * 512;
                    for (int v = 0; v < 512; v++)
                        if (poolA.RawData[sa + v] != poolB.RawData[sb + v]) { sameClass = false; break; }
                }
                if (sameClass) continue;

                differing++;
                // Brick AABB from flat index: (bz<<8)|(by<<4)|bx.
                int bx = i & 15, by = (i >> 4) & 15, bz = (i >> 8) & 15;
                float x0 = baseVoxel.x + bx * 8, y0 = baseVoxel.y + by * 8, z0 = baseVoxel.z + bz * 8;
                bool intersects = mx.x > x0 && mn.x < x0 + 8
                               && mx.y > y0 && mn.y < y0 + 8
                               && mx.z > z0 && mn.z < z0 + 8;
                Assert.IsTrue(intersects,
                    $"Brick ({bx},{by},{bz}) changed but does NOT intersect the cave AABB — the anchor is leaking outside its bounding volume.");
            }
            Assert.Greater(differing, 0, "The cave anchor changed nothing — carve kernel never fired.");

            // Interior of the capsule is genuinely air (clearance).
            byte center = ReadVoxel(with, poolB, new int3((int)cave.cx, (int)cave.cy, (int)cave.cz));
            Assert.AreEqual(Materials.Air, center, "Cave center voxel is not air.");
        }
        finally { poolA.Dispose(); poolB.Dispose(); }
    }

    [Test]
    public void HeightAnchors_MountainRaises_CraterLowers()
    {
        var baseMeta = new WorldMetaData
        {
            seed = SEED, sizeClass = SIZE_CLASS,
            formatVersion = WorldMeta.FORMAT_VERSION,
            contentVersionHash = WorldGenConstants.ContentVersionHash(),
            anchors = Array.Empty<FeatureAnchor>(),
            biomeSeeds = new[] { new BiomeSeed { x = 1408f, z = 1408f, biomeId = Biomes.ForestId } },
        };
        var stBase = ColumnSampler.CreateState(baseMeta);
        ColumnSampler.SampleColumn(in stBase, 1408, 1408, out int baseH, out _);

        var mountain = new FeatureAnchor { kind = FeatureKind.Mountain, cx = 1408f, cz = 1408f, radius = 300f, magnitude = 45f };
        var mMeta = new WorldMetaData
        {
            seed = SEED, sizeClass = SIZE_CLASS, formatVersion = WorldMeta.FORMAT_VERSION,
            contentVersionHash = baseMeta.contentVersionHash,
            anchors = new[] { mountain }, biomeSeeds = baseMeta.biomeSeeds,
        };
        var stM = ColumnSampler.CreateState(mMeta);
        // Sample the MAX over a grid spanning the peak region, not the single
        // centre column. With ridged noise (FeatureCarve WARP_AMPLITUDE=0.55)
        // a gully can legitimately run across dead centre — the meaningful
        // assertion is "this feature raises the terrain", not "this exact
        // column is raised". Worst-case centre delta re-derived by hand for
        // mag=45: shaped ranges [0.36, 1.64] -> delta [16.2, 73.8], so the
        // old single-column +20 threshold was no longer sound; the grid max
        // is both robust and closer to what the test actually means.
        int mH = int.MinValue;
        for (int dz = -60; dz <= 60; dz += 20)
        for (int dx = -60; dx <= 60; dx += 20)
        {
            ColumnSampler.SampleColumn(in stM, 1408 + dx, 1408 + dz, out int h, out _);
            if (h > mH) mH = h;
        }
        Assert.Greater(mH, baseH + 20, "Mountain anchor did not meaningfully raise terrain anywhere near its centre.");

        var crater = new FeatureAnchor { kind = FeatureKind.Crater, cx = 1408f, cz = 1408f, radius = 120f, magnitude = 16f };
        var cMeta = new WorldMetaData
        {
            seed = SEED, sizeClass = SIZE_CLASS, formatVersion = WorldMeta.FORMAT_VERSION,
            contentVersionHash = baseMeta.contentVersionHash,
            anchors = new[] { crater }, biomeSeeds = baseMeta.biomeSeeds,
        };
        var stC = ColumnSampler.CreateState(cMeta);
        ColumnSampler.SampleColumn(in stC, 1408, 1408, out int cH, out _);
        Assert.Less(cH, baseH - 8, "Crater anchor did not meaningfully lower the column at its center.");
    }

    // ---------- AnchorPlanner ----------

    [Test]
    public void AnchorPlanner_Deterministic_AndConstraintsHold()
    {
        var a = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        var b = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        CollectionAssert.AreEqual(WorldMeta.Serialize(a), WorldMeta.Serialize(b),
            "Two plans with the same seed serialized differently — the planner is not deterministic.");

        Assert.Greater(a.anchors.Length, 0, "Planner produced zero anchors.");
        Assert.Greater(a.biomeSeeds.Length, 0, "Planner produced zero biome seeds.");

        // Caves strictly above sea level (bore bottom above SEA) — the invariant
        // the water-fill pass relies on.
        foreach (var anchor in a.anchors)
        {
            if (anchor.kind != FeatureKind.Cave) continue;
            Assert.Greater(anchor.cy - anchor.radius, (float)WorldGenConstants.SEA_LEVEL_VOXEL_Y,
                "A cave's bore dips to/below sea level — planner constraint violated.");
        }

        // Pairwise XZ separation (bounding circles must not touch).
        for (int i = 0; i < a.anchors.Length; i++)
        for (int j = i + 1; j < a.anchors.Length; j++)
        {
            float ri = a.anchors[i].kind == FeatureKind.Cave ? a.anchors[i].halfLength + a.anchors[i].radius : a.anchors[i].radius;
            float rj = a.anchors[j].kind == FeatureKind.Cave ? a.anchors[j].halfLength + a.anchors[j].radius : a.anchors[j].radius;
            float dx = a.anchors[i].cx - a.anchors[j].cx;
            float dz = a.anchors[i].cz - a.anchors[j].cz;
            Assert.Greater(math.sqrt(dx * dx + dz * dz), ri + rj,
                $"Anchors {i} and {j} overlap — §5.2 requires non-overlapping bounding volumes.");
        }

        // All four §5.5 biomes represented.
        var seen = new HashSet<byte>();
        foreach (var s in a.biomeSeeds) seen.Add(s.biomeId);
        Assert.AreEqual(Biomes.Table.Length, seen.Count, "Not every biome in the roster got a Voronoi seed.");
    }

    // ---------- world.meta (D.2) ----------

    [Test]
    public void WorldMeta_SerializeRoundTrip_ByteIdentical_AndCrcDetectsCorruption()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        byte[] bytes = WorldMeta.Serialize(meta);

        Assert.IsTrue(WorldMeta.TryDeserialize(bytes, out var back), "Round-trip deserialize failed on clean bytes.");
        CollectionAssert.AreEqual(bytes, WorldMeta.Serialize(back), "Serialize(Deserialize(x)) != x.");

        // Flip one byte in the body: CRC must reject.
        byte[] corrupt = (byte[])bytes.Clone();
        corrupt[8] ^= 0xFF;
        Assert.IsFalse(WorldMeta.TryDeserialize(corrupt, out _), "CRC failed to detect a corrupted body byte.");

        // Truncated file: must reject, not throw.
        byte[] truncated = new byte[bytes.Length - 6];
        Buffer.BlockCopy(bytes, 0, truncated, 0, truncated.Length);
        Assert.IsFalse(WorldMeta.TryDeserialize(truncated, out _), "Truncated stream was accepted.");
    }

    [Test]
    public void WorldMeta_FileWriteRead_AtomicPath()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        string path = Path.Combine(Path.GetTempPath(), $"voxel_phase3_test_{Guid.NewGuid():N}", "world.meta");
        try
        {
            WorldMeta.WriteAtomic(path, meta);
            Assert.IsTrue(WorldMeta.TryRead(path, out var back), "TryRead failed on a freshly written file.");
            CollectionAssert.AreEqual(WorldMeta.Serialize(meta), WorldMeta.Serialize(back));

            // Overwrite path (File.Replace branch) must also work.
            WorldMeta.WriteAtomic(path, meta);
            Assert.IsTrue(WorldMeta.TryRead(path, out _), "TryRead failed after overwrite.");
        }
        finally
        {
            string dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}