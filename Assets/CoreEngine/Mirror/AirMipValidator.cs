// Assets/CoreEngine/Mirror/AirMipValidator.cs
//
// Amendment 8.7 — Step 3: debug-only validator that the GPU air-mip buffers
// exactly match a fresh CPU rebuild of the pyramid from the live clipmap.
//
// This is the mip analog of ClipmapValidator, and deliberately STRICTER: it
// compares the FULL mip buffers (every cell, every level) against a full CPU
// BuildFromStore over the whole window - not just the 8x8 generated region. A
// toroidal-wrap bug or a level-dim mismatch would show here even though it would
// be masked by a region-limited check. If this is GREEN, the GPU pyramid the
// shader (Step 4) will trust is provably identical to CPU truth.
//
// It rebuilds from the STORE (BuildFromStore) rather than reading the clipmap's
// private _clipmapLocal, so it is an INDEPENDENT recompute: if the clipmap's
// own RebuildRegion maintenance had drifted from a clean full build, this
// catches it (clean full build vs incrementally-maintained GPU state).

using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Memory;

public static class AirMipValidator
{
    public static void ValidateAll(TerrainClipmap clipmap, ChunkStore store)
    {
        int levels = clipmap.AirMipLevelCount;
        if (levels == 0)
        {
            Debug.LogError("AirMipValidator FAILURE: no mip levels built.");
            return;
        }

        // Independent CPU recompute of the whole pyramid from the store, using
        // the same store->handle conversion the clipmap uses (BuildFromStore
        // mirrors UploadDirty). This is the "truth" the GPU buffers are checked
        // against.
        AirMipData cpuTruth = AirMip.BuildFromStore(store, clipmap.WindowDimsBricks, TerrainClipmap.NUM_AIR_MIP_LEVELS);

        if (cpuTruth.NumLevels != levels)
        {
            Debug.LogError($"AirMipValidator FAILURE: level count mismatch. GPU has {levels}, CPU rebuild has {cpuTruth.NumLevels}.");
            return;
        }

        for (int k = 1; k <= levels; k++)
        {
            uint[] cpuCells = cpuTruth.Level(k);
            int3 dims = cpuTruth.DimsOfLevel(k);
            int count = cpuCells.Length;

            uint[] gpuCells = new uint[count];
            clipmap.AirMipBuffer(k).GetData(gpuCells);

            for (int i = 0; i < count; i++)
            {
                if (gpuCells[i] != cpuCells[i])
                {
                    // Decode the flat index back to a cell coord for a useful message.
                    int cx = i % dims.x;
                    int cy = (i / dims.x) % dims.y;
                    int cz = i / (dims.x * dims.y);
                    Debug.LogError(
                        $"AirMipValidator FAILURE: L{k} cell ({cx},{cy},{cz}) [flat {i}] " +
                        $"mismatch. CPU: {cpuCells[i]}, GPU: {gpuCells[i]}.");
                    return;
                }
            }
        }

        Debug.Log($"AirMipValidator: GREEN. All {levels} GPU air-mip levels exactly match CPU truth.");
    }
}