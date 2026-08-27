// ==========================================
// Assets/CoreEngine/Mirror/ClipmapValidator.cs
//
// PHASE 4 REVISION. Two changes:
//
//  1. Matches the chunk-major GPU layout by asking TerrainClipmap for the
//     index (GpuIndexOf) instead of recomputing it. This validator's own
//     history is the argument: its previous header records that the real bug
//     was three files each keeping a private copy of the window size, "and
//     only the first two happened to agree." A validator that reproduces the
//     formula it is validating can only catch data bugs, never layout bugs.
//
//  2. Walks ACTUAL RESIDENT CHUNKS rather than a hardcoded 8x8x1 (or full
//     window) sweep. Under streaming, residency changes every frame and most
//     window slots are legitimately empty, so a fixed sweep both misses real
//     chunks and reads uninitialised slots.
//
// It also now RETURNS a result instead of only logging, so the Phase 4
// acceptance rig can gate on it rather than parsing the console.
//
// COST: this reads back the whole BrickDataBuffer (brick pool cap x 512B).
// That is a debug-only, deliberately expensive operation (§10.4: "a debug-only
// clipmap validator periodically reads back a region and byte-compares against
// the CPU source"). Never call it per-frame in a timing run -- it forces a GPU
// sync and will contaminate any ms figure measured around it.
using System.Text;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using VoxelEngine.Memory;

public static class ClipmapValidator
{
    public struct Result
    {
        public bool pass;
        public int chunksChecked;
        public int handleMismatches;
        public int brickByteMismatches;
        public string firstFailure;

        public override string ToString() => pass
            ? $"GREEN. {chunksChecked} resident chunks: clipmap handles and dense bodies byte-match CPU truth."
            : $"RED. {chunksChecked} chunks checked, {handleMismatches} handle mismatch(es), " +
              $"{brickByteMismatches} brick-byte mismatch(es). First: {firstFailure}";
    }

    /// `maxChunks` bounds the walk so a full 2,048-slot window doesn't stall
    /// the rig for minutes. Pass int.MaxValue for an exhaustive pass.
    public static Result ValidateRegion(TerrainClipmap clipmap, BrickDataPool cpuPool, ChunkStore store,
        int maxChunks = int.MaxValue, bool validateBrickBodies = true)
    {
        var result = new Result { pass = true };
        var sb = new StringBuilder();

        // ---- 1. Clipmap handles, per resident chunk, chunk-major ----
        int totalBricks = clipmap.WindowDimsBricks.x * clipmap.WindowDimsBricks.y * clipmap.WindowDimsBricks.z;
        uint[] gpuClipmap = new uint[totalBricks];
        clipmap.ClipmapBuffer.GetData(gpuClipmap);

        foreach (Chunk chunk in store.ResidentChunks())
        {
            if (result.chunksChecked >= maxChunks) break;
            result.chunksChecked++;

            for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int3 localBrick = new int3(x, y, z);
                int local = CoordMath.LocalBrickIndex(localBrick);

                uint expected = chunk.isUniform ? chunk.uniformMaterial : chunk.bricks[local].data;
                uint actual = gpuClipmap[clipmap.GpuIndexOf(chunk.coord, localBrick)];

                if (actual != expected)
                {
                    result.handleMismatches++;
                    if (result.firstFailure == null)
                        result.firstFailure = $"chunk {chunk.coord} localBrick {localBrick}: " +
                                              $"CPU 0x{expected:X8}, GPU 0x{actual:X8}";
                }
            }
        }

        // ---- 2. Dense bodies ----
        // Only the slots resident chunks actually reference are checked. The
        // rest of the pool is legitimately stale: incremental upload writes
        // only live slots, so comparing the whole buffer would report freed
        // slots as "mismatches" that are simply nobody's data any more.
        if (validateBrickBodies)
        {
            byte[] gpuBrickData = new byte[cpuPool.Capacity * 512];
            clipmap.BrickDataBuffer.GetData(gpuBrickData);
            NativeArray<byte>.ReadOnly cpuData = cpuPool.RawData.AsReadOnly();

            int checkedChunks = 0;
            foreach (Chunk chunk in store.ResidentChunks())
            {
                if (checkedChunks++ >= maxChunks) break;
                if (chunk.isUniform || chunk.bricks == null) continue;

                for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
                {
                    uint data = chunk.bricks[i].data;
                    if ((data & 0x80000000u) == 0) continue;

                    int slot = (int)(data & 0x3FFFFFFFu);
                    int start = slot * 512;
                    for (int v = 0; v < 512; v++)
                    {
                        if (gpuBrickData[start + v] == cpuData[start + v]) continue;
                        result.brickByteMismatches++;
                        if (result.firstFailure == null)
                            result.firstFailure = $"chunk {chunk.coord} brick {i} slot {slot} byte {v}: " +
                                                  $"CPU {cpuData[start + v]}, GPU {gpuBrickData[start + v]}";
                        v = 512; // one report per brick is enough
                    }
                }
            }
        }

        result.pass = result.handleMismatches == 0 && result.brickByteMismatches == 0;
        if (result.pass) Debug.Log("[ClipmapValidator] " + result);
        else Debug.LogError("[ClipmapValidator] " + result);
        return result;
    }
}