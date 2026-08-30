// ==========================================
// Assets/CoreEngine/Mirror/ClipmapValidator.cs
//
// PHASE 4 REVISION 2. The previous version found a real defect and reported it
// uselessly: "15 handle mismatch(es). First: chunk int3(19,0,1) localBrick
// int3(6,0,0): CPU 0x0000000A, GPU 0x80014DAA" and nothing more. Enough to know
// something is wrong; not enough to know what. A red gate with no actionable
// finding is barely better than no gate.
//
// What was missing, in order of importance:
//
//  1. WAS THE CHUNK STILL DIRTY? A stale GPU entry has two causes that need
//     OPPOSITE fixes -- upload LAG (write queued, budget delayed it) versus a
//     LOST UPDATE (CPU changed without a mark, or the mark was consumed without
//     the write landing). Only the second corrupts frames. One boolean
//     separates them and it was not being collected.
//
//  2. WHAT KIND of mismatch? CPU-uniform/GPU-dense points at coalescing (a
//     brick collapsed and the GPU was never told). CPU-dense/GPU-uniform points
//     at an edit or expansion that went unmarked. Both-dense-different-slot
//     points at pool reallocation under the GPU. Both-uniform-different-material
//     points at generation or delta replay. Four separate investigations, and a
//     raw hex pair names none of them.
//
//  3. HOW MANY CHUNKS, not how many bricks. 15 mismatched bricks across 15
//     chunks is systemic; 15 inside one chunk is local.
//
//  4. A BOUNDED SAMPLE of real failures, not only the first.
//
// COST: reads back the clipmap plus bounded slices of the brick buffer, forcing
// a GPU sync. Debug-only per §10.4 -- never call inside a timing measurement.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using VoxelEngine.Memory;

public static class ClipmapValidator
{
    // Bounds on the dense-body check. This is a debug validator and its job is
    // to prove the CPU->GPU path is byte-exact, which a handful of real chunks
    // demonstrates as well as all of them, at a fraction of the stall. (The
    // first version read back the ENTIRE 384 MB BrickDataBuffer per call, three
    // times per run, on an 8 GB machine.)
    private const int BODY_CHECK_CHUNK_LIMIT = 4;
    private const int BODY_CHECK_SLOT_LIMIT = 8192;   // ~4 MB readback ceiling
    private const int MAX_SAMPLES = 12;

    public enum MismatchKind
    {
        CpuUniformGpuDense,      // coalescing collapsed a brick; GPU not updated
        CpuDenseGpuUniform,      // brick expanded (edit/delta); GPU not updated
        BothDenseDifferentSlot,  // pool slot reallocated under the GPU
        BothUniformDifferentMat, // generation / delta replay disagreement
        Other,
    }

    public struct Sample
    {
        public int3 chunkCoord;
        public int3 localBrick;
        public uint cpu, gpu;
        public MismatchKind kind;
        public bool chunkWasDirty;

        public override string ToString() =>
            $"chunk {chunkCoord} brick {localBrick}: CPU 0x{cpu:X8} GPU 0x{gpu:X8} " +
            $"[{kind}] stillDirty={chunkWasDirty}";
    }

    public struct Result
    {
        public bool pass;
        public int chunksChecked;
        public int chunksWithMismatch;
        public int handleMismatches;
        public int brickByteMismatches;

        /// Mismatches in chunks STILL QUEUED for upload. Expected to be nonzero
        /// on a moving window; these are lag, not corruption.
        public int mismatchesInDirtyChunks;

        /// Mismatches in chunks with NOTHING queued. Every one is a lost update
        /// and a real bug. This is the number that matters.
        public int mismatchesInCleanChunks;

        public Dictionary<MismatchKind, int> byKind;
        public List<Sample> samples;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(pass ? "GREEN" : "RED").Append(". ");
            sb.Append($"{chunksChecked} chunks checked, {chunksWithMismatch} with mismatches, ");
            sb.Append($"{handleMismatches} handle + {brickByteMismatches} body mismatches.");
            if (!pass)
            {
                sb.AppendLine();
                sb.AppendLine($"      upload LAG (chunk still queued): {mismatchesInDirtyChunks}");
                sb.AppendLine($"      LOST UPDATES (not queued):       {mismatchesInCleanChunks}   <-- real bugs");
                if (byKind != null)
                    foreach (var kv in byKind)
                        sb.AppendLine($"        {kv.Key}: {kv.Value}");
                if (samples != null)
                    foreach (var smp in samples) sb.AppendLine("        " + smp);
            }
            return sb.ToString();
        }
    }

    public static Result ValidateRegion(TerrainClipmap clipmap, BrickDataPool cpuPool, ChunkStore store,
        int maxChunks = int.MaxValue, bool validateBrickBodies = true)
    {
        var result = new Result
        {
            pass = true,
            byKind = new Dictionary<MismatchKind, int>(),
            samples = new List<Sample>(),
        };

        int totalBricks = clipmap.WindowDimsBricks.x * clipmap.WindowDimsBricks.y * clipmap.WindowDimsBricks.z;
        uint[] gpuClipmap = new uint[totalBricks];
        clipmap.ClipmapBuffer.GetData(gpuClipmap);

        foreach (Chunk chunk in store.ResidentChunks())
        {
            if (result.chunksChecked >= maxChunks) break;
            result.chunksChecked++;

            bool dirty = clipmap.IsDirty(chunk.coord);
            bool chunkHadMismatch = false;

            for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int3 localBrick = new int3(x, y, z);
                int local = CoordMath.LocalBrickIndex(localBrick);

                uint expected = chunk.isUniform ? chunk.uniformMaterial : chunk.bricks[local].data;
                uint actual = gpuClipmap[clipmap.GpuIndexOf(chunk.coord, localBrick)];
                if (actual == expected) continue;

                result.handleMismatches++;
                chunkHadMismatch = true;
                if (dirty) result.mismatchesInDirtyChunks++;
                else result.mismatchesInCleanChunks++;

                MismatchKind kind = Classify(expected, actual);
                result.byKind.TryGetValue(kind, out int n);
                result.byKind[kind] = n + 1;

                if (result.samples.Count < MAX_SAMPLES)
                    result.samples.Add(new Sample
                    {
                        chunkCoord = chunk.coord, localBrick = localBrick,
                        cpu = expected, gpu = actual, kind = kind, chunkWasDirty = dirty,
                    });
            }

            if (chunkHadMismatch) result.chunksWithMismatch++;
        }

        if (validateBrickBodies) ValidateBodies(clipmap, cpuPool, store, ref result);

        result.pass = result.handleMismatches == 0 && result.brickByteMismatches == 0;
        if (result.pass) Debug.Log("[ClipmapValidator] " + result.Describe());
        else Debug.LogError("[ClipmapValidator] " + result.Describe());
        return result;
    }

    private static MismatchKind Classify(uint cpu, uint gpu)
    {
        bool cpuDense = (cpu & 0x80000000u) != 0;
        bool gpuDense = (gpu & 0x80000000u) != 0;

        if (!cpuDense && gpuDense) return MismatchKind.CpuUniformGpuDense;
        if (cpuDense && !gpuDense) return MismatchKind.CpuDenseGpuUniform;
        if (cpuDense && gpuDense) return MismatchKind.BothDenseDifferentSlot;
        if ((cpu & 0xFF) != (gpu & 0xFF)) return MismatchKind.BothUniformDifferentMat;
        return MismatchKind.Other;
    }

    private static void ValidateBodies(TerrainClipmap clipmap, BrickDataPool cpuPool, ChunkStore store,
        ref Result result)
    {
        // PARTIAL readback: for a few chunks, read only the contiguous pool-slot
        // RANGE they occupy. Freshly generated chunks allocate consecutive slots
        // (BrickDataPool hands out a descending free-stack), so one chunk is
        // typically one tight range.
        NativeArray<byte>.ReadOnly cpuData = cpuPool.RawData.AsReadOnly();

        int checkedChunks = 0;
        foreach (Chunk chunk in store.ResidentChunks())
        {
            if (checkedChunks >= BODY_CHECK_CHUNK_LIMIT) break;
            if (chunk.isUniform || chunk.bricks == null) continue;
            checkedChunks++;

            int minSlot = int.MaxValue, maxSlot = -1;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                uint d = chunk.bricks[i].data;
                if ((d & 0x80000000u) == 0) continue;
                int slot = (int)(d & 0x3FFFFFFFu);
                if (slot < minSlot) minSlot = slot;
                if (slot > maxSlot) maxSlot = slot;
            }
            if (maxSlot < 0) continue;

            int slotCount = maxSlot - minSlot + 1;
            if (slotCount > BODY_CHECK_SLOT_LIMIT) continue; // don't degenerate into a full readback

            uint[] gpuRange = new uint[slotCount * 128];
            clipmap.BrickDataBuffer.GetData(gpuRange, 0, minSlot * 128, slotCount * 128);

            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                uint d = chunk.bricks[i].data;
                if ((d & 0x80000000u) == 0) continue;
                int slot = (int)(d & 0x3FFFFFFFu);

                int gpuByteBase = (slot - minSlot) * 512;
                int cpuByteBase = slot * 512;
                for (int v = 0; v < 512; v++)
                {
                    int gb = gpuByteBase + v;
                    byte g = (byte)((gpuRange[gb >> 2] >> ((gb & 3) * 8)) & 0xFF);
                    if (g == cpuData[cpuByteBase + v]) continue;

                    result.brickByteMismatches++;
                    if (result.samples.Count < MAX_SAMPLES)
                        result.samples.Add(new Sample
                        {
                            chunkCoord = chunk.coord,
                            localBrick = new int3(-1, i, v),   // -1 marks a body sample: (brickIndex, byte)
                            cpu = cpuData[cpuByteBase + v], gpu = g,
                            kind = MismatchKind.Other,
                            chunkWasDirty = clipmap.IsDirty(chunk.coord),
                        });
                    v = 512; // one report per brick is enough
                }
            }
        }
    }
}