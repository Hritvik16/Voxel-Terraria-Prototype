using NUnit.Framework;
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Memory;

// DIAGNOSTIC (not a pass/fail assertion yet). Generates the chunk that contains
// the world location where the blue "sky-hole" square appears, then dumps the
// vertical column of voxel materials at that X,Z straight to the Console.
//
// This follows the project's Rule 2: prove/inspect on the CPU where you can
// breakpoint before touching the shader. If the generator produced a hole
// (air where there should be solid, below the surface), it will show up here as
// a plain list of numbers - no raymarcher, no GPU, no ambiguity.
//
// Blue square observed at camera (52, 30, 52.8) pointing straight down, so the
// world column under it is world metres X=52, Z=52.8 => world voxels X=520,
// Z=528 (metres * 10, §2.3). We dump the full vertical voxel column there.
public class GenerationDiagnosticTests
{
    [Test]
    public void DumpColumn_AtBlueSquareLocation()
    {
        var pool = new BrickDataPool(5000);
        var allocator = new ChunkHandleAllocator(10);

        try
        {
            // World voxel column under the blue square.
            int worldVoxelX = 520;
            int worldVoxelZ = 528;

            // Which chunk owns it? (voxel >> 7 == chunk, per CoordMath.)
            int3 chunkCoord = CoordMath.VoxelToChunk(new int3(worldVoxelX, 0, worldVoxelZ));

            // Generate that chunk exactly as the real pipeline does.
            var chunk = new Chunk();
            ChunkGenerator.GenerateChunk(0, chunkCoord, ref chunk, allocator, pool);

            // Read the material at every voxel Y from 0..127 at this X,Z by hand,
            // walking the chunk's brick/handle structure directly (no ChunkStore,
            // no GPU) so we see exactly what generation wrote.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Column dump at world voxel X={worldVoxelX}, Z={worldVoxelZ}, chunk={chunkCoord}");
            sb.AppendLine("Y : material   (0=air, 2=stone)");

            int firstAirAboveSolid = -1;
            byte prev = 2; // assume solid below

            for (int worldVoxelY = 0; worldVoxelY < 128; worldVoxelY++)
            {
                byte mat = ReadVoxelFromChunk(chunk, pool, new int3(worldVoxelX, worldVoxelY, worldVoxelZ));
                sb.AppendLine($"{worldVoxelY,3} : {mat}");

                // Flag the FIRST air voxel that appears above a solid one, and
                // whether any SOLID reappears above it (a hole = solid, air, solid).
                if (prev == 2 && mat == 0 && firstAirAboveSolid < 0)
                    firstAirAboveSolid = worldVoxelY;
                prev = mat;
            }

            // Hole detector: does solid reappear ABOVE the first air-over-solid?
            bool holeFound = false;
            if (firstAirAboveSolid >= 0)
            {
                for (int y = firstAirAboveSolid + 1; y < 128; y++)
                {
                    if (ReadVoxelFromChunk(chunk, pool, new int3(worldVoxelX, y, worldVoxelZ)) == 2)
                    {
                        holeFound = true;
                        sb.AppendLine($">>> HOLE: solid voxel at Y={y} sits ABOVE air starting at Y={firstAirAboveSolid}");
                        break;
                    }
                }
            }

            sb.AppendLine(holeFound
                ? ">>> RESULT: this column contains a sky-hole (air trapped under solid)."
                : ">>> RESULT: clean column - solid up to the surface, then air. No hole HERE.");

            Debug.Log(sb.ToString());

            // Not asserting yet - this run is to SEE the data. Once we know what
            // the bug looks like, we convert this into a hard assertion.
            Assert.Pass("Diagnostic dump complete - read the Console output.");
        }
        finally
        {
            pool.Dispose();
        }
    }

    // Reads one voxel's material directly out of a generated Chunk's structure,
    // mirroring ChunkStore.GetVoxel's logic but on a standalone chunk.
    private static byte ReadVoxelFromChunk(Chunk chunk, BrickDataPool pool, int3 worldVoxel)
    {
        if (chunk.isUniform) return chunk.uniformMaterial;

        int3 localBrick = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(worldVoxel));
        int brickFlatIndex = CoordMath.LocalBrickIndex(localBrick);

        uint handleData = chunk.bricks[brickFlatIndex].data;
        bool isDense = (handleData & 0x80000000) != 0;

        if (!isDense) return (byte)(handleData & 0xFF);

        int poolIndex = (int)(handleData & 0x3FFFFFFF);
        int3 localVoxel = CoordMath.LocalVoxelIndex3D(worldVoxel);
        int voxelFlatIndex = CoordMath.LocalVoxelIndex(localVoxel);
        return pool.RawData[(poolIndex * 512) + voxelFlatIndex];
    }
}