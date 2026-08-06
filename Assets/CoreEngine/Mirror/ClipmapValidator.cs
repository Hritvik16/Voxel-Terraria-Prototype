using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using VoxelEngine.Memory;

public static class ClipmapValidator
{
    public static void ValidateRegion(TerrainClipmap clipmap, BrickDataPool cpuPool, ChunkStore store)
    {
        // 1. Validate BrickDataBuffer (Dense Bodies)
        byte[] gpuBrickData = new byte[cpuPool.Capacity * 512];
        clipmap.BrickDataBuffer.GetData(gpuBrickData);
        NativeArray<byte>.ReadOnly cpuData = cpuPool.RawData.AsReadOnly();

        for (int i = 0; i < gpuBrickData.Length; i++)
        {
            if (gpuBrickData[i] != cpuData[i])
            {
                Debug.LogError($"ClipmapValidator FAILURE: BrickData mismatch at raw byte index {i}. CPU: {cpuData[i]}, GPU: {gpuBrickData[i]}");
                return;
            }
        }

        // 2. Validate ClipmapBuffer (Flat Grid Handles)
        // Window dims now come from the clipmap instance itself - the single
        // source of truth (which is constructed from EngineConfig) - instead of
        // a second hardcoded int3 that could silently disagree with both the
        // clipmap's real size and the shader's. This was the actual bug: this
        // validator, TerrainClipmap, and Raymarch.compute each had their own
        // copy of the window size, and only the first two happened to agree.
        int3 windowBricks = clipmap.WindowDimsBricks;
        int totalBricks = windowBricks.x * windowBricks.y * windowBricks.z;
        uint[] gpuClipmap = new uint[totalBricks];
        clipmap.ClipmapBuffer.GetData(gpuClipmap);

        // NOTE (scope): this loop still only walks the 8x8x1 chunk region the
        // Phase 2 bootstrapper actually generates (cx/cz 0..7, y=0). That's a
        // coverage limitation of this validator call, separate from the window-
        // dims fix above - it means GREEN here proves correctness only for the
        // chunks that exist, not full-window coverage. Worth widening once the
        // world generation extends further, but out of scope for this fix.
        for (int cz = 0; cz < 8; cz++)
        for (int cx = 0; cx < 8; cx++)
        {
            int3 chunkCoord = new int3(cx, 0, cz);
            Chunk chunk = store.GetChunk(chunkCoord);
            if (chunk == null) continue;

            int3 baseBrick = chunkCoord * 16;
            for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int3 localBrick = new int3(x, y, z);
                int3 worldBrick = baseBrick + localBrick;

                int3 wrapped = worldBrick & (windowBricks - new int3(1, 1, 1));
                int flatIndex = wrapped.x + (wrapped.y * windowBricks.x) + (wrapped.z * windowBricks.x * windowBricks.y);

                uint expectedHandle = chunk.isUniform ? chunk.uniformMaterial : chunk.bricks[CoordMath.LocalBrickIndex(localBrick)].data;

                if (gpuClipmap[flatIndex] != expectedHandle)
                {
                    Debug.LogError($"ClipmapValidator FAILURE: Handle mismatch at chunk {chunkCoord}, local {localBrick}. Expected {expectedHandle}, got {gpuClipmap[flatIndex]}");
                    return;
                }
            }
        }

        Debug.Log("ClipmapValidator: GREEN. Both Clipmap grid and BrickData exactly match CPU truth.");
    }
}