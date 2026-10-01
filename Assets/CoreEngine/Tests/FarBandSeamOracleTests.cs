// Assets/CoreEngine/Tests/FarBandSeamOracleTests.cs
//
// The far band's F0 texel vs the voxel generator's actual top voxel, on
// today's world (sizeClass 1, seed 42 -- what the Playground streams).
//
// WHY: -farseams (2026-09-25) photographed a seam at the voxel window's edge
// from 150 m where the two bands show different SURFACE MATERIAL over an area,
// not just along a line. Either the far band's packing disagrees with the
// generator, or the difference is made after generation (render / LOD path).
// This oracle decides which, over the chunks where the seam was photographed.
//
//   exact  : FarField.Pack(SampleColumn(x, z)) must equal the generated
//            column's top (first air-or-water y above the top solid voxel) and
//            its material, column for column. Any mismatch is a far-band bug.
//   texel  : the same against F0's texel CENTRE (what the ring really holds).
//            A 0.8 m point sample differs along biome borders only; reported.
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;
using VoxelEngine.Rendering;
using VoxelEngine.WorldGen;

public class FarBandSeamOracleTests
{
    private const uint SEED = 42;
    private const byte SIZE_CLASS = 1;

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

    [Test]
    public void F0_MatchesGeneratedTopVoxel_WhereTheSeamWasPhotographed()
    {
        var meta = AnchorPlanner.Plan(SEED, SIZE_CLASS);
        var st = ColumnSampler.CreateState(meta);
        var surf = new NativeArray<byte>(256, Allocator.Temp);
        for (int i = 0; i < 256; i++) surf[i] = Materials.Stone;
        foreach (var b in Biomes.Table) surf[b.id] = b.surfaceMaterial;
        int exactH = 0, exactM = 0, texelH = 0, texelM = 0, cols = 0;
        string firstExact = null;
        try
        {
            // x 1356-1395 m, z 1100-1177 m: the window's +x edge 205 m ahead of
            // the h150 in-flight poses (camera z 1130).
            for (int cz = 86; cz <= 91; cz++)
            for (int cx = 106; cx <= 108; cx++)
            {
                using var pool = new BrickDataPool(20000);
                var chunk = new Chunk();
                ChunkGeneratorFull.GenerateChunkFull(meta, new int3(cx, 0, cz), chunk, new ChunkHandleAllocator(2), pool);
                for (int lz = 0; lz < 128; lz += 2)
                for (int lx = 0; lx < 128; lx += 2)
                {
                    int wx = cx * 128 + lx, wz = cz * 128 + lz;
                    int top = -1; byte topMat = Materials.Air;
                    for (int wy = 127; wy >= 0; wy--)
                    {
                        byte m = ReadVoxel(chunk, pool, new int3(wx, wy, wz));
                        if (m != Materials.Air && m != Materials.Water) { top = wy; topMat = m; break; }
                    }
                    cols++;
                    const int SEA = WorldGenConstants.SEA_LEVEL_VOXEL_Y;
                    uint want = top < SEA
                        ? (uint)(SEA + 1) | ((uint)Materials.Water << 16) | (FarField.FLAG_WATER << 24)
                        : (uint)(top + 1) | ((uint)topMat << 16);

                    ColumnSampler.SampleColumn(in st, wx, wz, out int h, out byte bio);
                    uint got = FarField.Pack(h, bio, surf);
                    if ((got & 0xFFFF) != (want & 0xFFFF)) exactH++;
                    if (((got >> 16) & 0xFF) != ((want >> 16) & 0xFF)) exactM++;
                    if (firstExact == null && got != want)
                        firstExact = $"({wx},{wz}): far h {got & 0xFFFF} mat {(got >> 16) & 0xFF}, voxel h {want & 0xFFFF} mat {(want >> 16) & 0xFF}";

                    int tx = wx >> 3, tz = wz >> 3;
                    ColumnSampler.SampleColumn(in st, tx * 8 + 4, tz * 8 + 4, out int ht, out byte bt);
                    uint gotT = FarField.Pack(ht, bt, surf);
                    if ((gotT & 0xFFFF) != (want & 0xFFFF)) texelH++;
                    if (((gotT >> 16) & 0xFF) != ((want >> 16) & 0xFF)) texelM++;
                }
            }
        }
        finally { st.Dispose(); surf.Dispose(); }

        UnityEngine.Debug.Log($"[FarBandSeamOracle] {cols} columns: EXACT height mismatches {exactH}, material {exactM}; " +
                              $"F0 TEXEL-CENTRE height mismatches {texelH} ({100.0 * texelH / cols:F2}%), material {texelM} ({100.0 * texelM / cols:F2}%)" +
                              (firstExact != null ? $"; first exact mismatch {firstExact}" : ""));
        Assert.AreEqual(0, exactH + exactM, $"far packing disagrees with the generator column-for-column: {firstExact}");
        Assert.Less(texelM, cols / 20, "F0 point sampling changes the top material on more than 5% of columns");
    }
}
