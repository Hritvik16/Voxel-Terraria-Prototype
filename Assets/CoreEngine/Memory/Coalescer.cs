using Unity.Mathematics;
using Unity.Collections;

namespace VoxelEngine.Memory
{
    public static class Coalescer
    {
        // Returns true if the chunk was fully coalesced into a uniform chunk
        public static bool TryCoalesce(Chunk chunk, BrickDataPool brickPool)
        {
            if (chunk.isUniform) return true;

            bool allBricksUniform = true;
            byte firstMaterial = 0;
            bool firstMaterialSet = false;

            for (int i = 0; i < 4096; i++)
            {
                uint handleData = chunk.bricks[i].data;
                bool isDense = (handleData & 0x80000000) != 0; // [31] dense flag

                if (isDense)
                {
                    int poolIndex = (int)(handleData & 0x3FFFFFFF); // [29:0] index
                    NativeArray<byte> rawData = brickPool.RawData;
                    int startOffset = poolIndex * 512;

                    byte firstByte = rawData[startOffset];
                    bool isBrickUniform = true;

                    // Scan the 512-byte body
                    for (int v = 1; v < 512; v++)
                    {
                        if (rawData[startOffset + v] != firstByte)
                        {
                            isBrickUniform = false;
                            break;
                        }
                    }

                    if (isBrickUniform)
                    {
                        // Coalesce brick: free the 512-byte payload and write a uniform handle
                        brickPool.Free(poolIndex);
                        chunk.bricks[i].data = firstByte; // [31] = 0 (uniform), [7:0] = material
                        isDense = false;
                        handleData = firstByte;
                    }
                }

                // If the brick is uniform (originally or newly coalesced), check it against the chunk consensus
                if (!isDense)
                {
                    byte mat = (byte)(handleData & 0xFF); // [7:0] material
                    
                    if (!firstMaterialSet)
                    {
                        firstMaterial = mat;
                        firstMaterialSet = true;
                    }
                    else if (mat != firstMaterial)
                    {
                        allBricksUniform = false;
                    }
                }
                else
                {
                    allBricksUniform = false;
                }
            }

            if (allBricksUniform)
            {
                // The whole chunk is uniform. 
                // We update the state here. The calling streaming/eviction system is responsible 
                // for returning chunk.bricks back to the ChunkHandleAllocator to avoid tight coupling.
                chunk.isUniform = true;
                chunk.uniformMaterial = firstMaterial;
                return true;
            }

            return false;
        }
    }
}