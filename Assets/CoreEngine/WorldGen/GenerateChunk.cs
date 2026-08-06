using Unity.Mathematics;
using VoxelEngine.Memory;

public static class ChunkGenerator
{
    public static void GenerateChunk(int seed, int3 chunkCoord, ref Chunk chunk, ChunkHandleAllocator allocator, BrickDataPool pool)
    {
        chunk.coord = chunkCoord;
        chunk.isUniform = false;
        chunk.bricks = allocator.Alloc();

        int3 baseVoxel = chunkCoord * 128;

        for (int bz = 0; bz < 16; bz++)
        for (int bx = 0; bx < 16; bx++)
        {
            float worldX = (baseVoxel.x + (bx * 8)) * 0.1f;
            float worldZ = (baseVoxel.z + (bz * 8)) * 0.1f;
            
            // Fixed naming collision
            float noiseVal = noise.cnoise(new float2(worldX * 0.05f, worldZ * 0.05f));
            int surfaceHeightVoxel = (int)(math.unlerp(-1f, 1f, noiseVal) * 50);

            for (int by = 0; by < 16; by++)
            {
                int currentYMinVoxel = baseVoxel.y + (by * 8);
                int currentYMaxVoxel = currentYMinVoxel + 7;
                int brickFlatIndex = (bz << 8) | (by << 4) | bx;

                if (currentYMinVoxel > surfaceHeightVoxel)
                {
                    chunk.bricks[brickFlatIndex].data = 0; // Air
                }
                else if (currentYMaxVoxel < surfaceHeightVoxel)
                {
                    chunk.bricks[brickFlatIndex].data = 2; // Stone
                }
                else
                {
                    int poolIdx = pool.Alloc();
                    chunk.bricks[brickFlatIndex].data = 0x80000000 | (uint)poolIdx;
                    
                    int startOffset = poolIdx * 512;
                    var rawData = pool.RawData;

                    for (int vy = 0; vy < 8; vy++)
                    {
                        int voxelYWorld = currentYMinVoxel + vy;
                        byte mat = (byte)(voxelYWorld <= surfaceHeightVoxel ? 2 : 0);

                        for (int vz = 0; vz < 8; vz++)
                        for (int vx = 0; vx < 8; vx++)
                        {
                            int vIdx = (vz << 6) | (vy << 3) | vx;
                            rawData[startOffset + vIdx] = mat;
                        }
                    }
                }
            }
        }
    }
}
