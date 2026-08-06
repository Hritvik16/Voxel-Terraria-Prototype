using NUnit.Framework;
using Unity.Mathematics;

public class CoordMathTests
{
    [Test]
    public void WorldToVoxel_NegativeCoords_FloorsCorrectly()
    {
        // Asserting a voxel at world -0.05m resolves accurately to the -1 index block
        float3 pos = new float3(-0.05f, 0, 0);
        int3 expected = new int3(-1, 0, 0);
        Assert.AreEqual(expected, CoordMath.WorldToVoxel(pos));
    }

    [Test]
    public void VoxelToChunk_NegativeCoords_UsesArithmeticShift()
    {
        int3 voxel = new int3(-1, -1, -1);
        int3 expectedChunk = new int3(-1, -1, -1);
        Assert.AreEqual(expectedChunk, CoordMath.VoxelToChunk(voxel));
    }

    [Test]
    public void LocalIndices_RoundTrip()
    {
        // Voxel Round-Trip [0, 511]
        for (int z = 0; z < 8; z++)
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            int3 original = new int3(x, y, z);
            int index = CoordMath.LocalVoxelIndex(original);
            Assert.IsTrue(index >= 0 && index < 512, "Voxel flat index out of bounds.");
            
            int3 decoded = new int3(index & 7, (index >> 3) & 7, (index >> 6) & 7);
            Assert.AreEqual(original, decoded, $"Voxel decode failed for {original}");
        }

        // Brick Round-Trip [0, 4095]
        for (int z = 0; z < 16; z++)
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            int3 original = new int3(x, y, z);
            int index = CoordMath.LocalBrickIndex(original);
            Assert.IsTrue(index >= 0 && index < 4096, "Brick flat index out of bounds.");
            
            int3 decoded = new int3(index & 15, (index >> 4) & 15, (index >> 8) & 15);
            Assert.AreEqual(original, decoded, $"Brick decode failed for {original}");
        }
    }
}