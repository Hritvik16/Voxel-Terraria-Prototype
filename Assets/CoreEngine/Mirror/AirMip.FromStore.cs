// Assets/CoreEngine/Mirror/AirMip.FromStore.cs
//
// Amendment 8.7 — B2 addition: build the flat L0 handle array from a ChunkStore,
// then build the pyramid from it. This is a SEPARATE partial-class file so it
// does not touch the proven Step 1 AirMip.cs logic at all.
//
// WHY THIS IS THE ONE CONVERSION
//   The pyramid must be built from the SAME handle bits the GPU clipmap holds
//   and the shader reads — otherwise "what counts as air" could drift between
//   the CPU pyramid and the GPU render. TerrainClipmap.UploadDirty is the
//   authority for "store -> clipmap handle":
//       uniform chunk    -> chunk.uniformMaterial (a bare material byte)
//       populated chunk  -> chunk.bricks[brickIdx].data (the packed handle)
//       null chunk       -> 0 (unmapped == air, matching zero-init)
//   BuildL0FromStore reproduces that branch VERBATIM. The tests and the Step-3
//   GPU upload both go through here, so there is exactly one store->handle
//   conversion in the codebase.
//
//   NOTE: this iterates the FULL window (dims.x*y*z bricks). For the real
//   512x256x512 window that is ~67M slots — fine for an occasional build, but
//   the per-geometry Mip_* tests pass a SMALL dims so the fuzz stays fast. The
//   production/GPU path passes the real window dims.

using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    public static partial class AirMip
    {
        // Build the flat L0 handle array from a ChunkStore, exactly as
        // TerrainClipmap.UploadDirty would populate _clipmapLocal for every brick
        // in the window. Toroidal flat index identical to the clipmap's own.
        public static uint[] BuildL0FromStore(ChunkStore store, int3 windowDimsBricks)
        {
            int total = windowDimsBricks.x * windowDimsBricks.y * windowDimsBricks.z;
            var l0 = new uint[total]; // zero-init == air for every unmapped slot

            int3 windowChunks = windowDimsBricks / 16;
            int3 brickMask = windowDimsBricks - new int3(1, 1, 1);

            for (int cz = 0; cz < windowChunks.z; cz++)
            for (int cy = 0; cy < windowChunks.y; cy++)
            for (int cx = 0; cx < windowChunks.x; cx++)
            {
                int3 chunkCoord = new int3(cx, cy, cz);
                Chunk chunk = store.GetChunk(chunkCoord);
                if (chunk == null) continue; // leave zero (air) — matches UploadDirty skipping

                int3 baseBrickCoord = chunkCoord * 16;

                for (int z = 0; z < 16; z++)
                for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    int3 localBrick = new int3(x, y, z);
                    int3 worldBrick = baseBrickCoord + localBrick;

                    int3 wrapped = worldBrick & brickMask;
                    int flatIndex = wrapped.x
                                  + wrapped.y * windowDimsBricks.x
                                  + wrapped.z * windowDimsBricks.x * windowDimsBricks.y;

                    // VERBATIM from UploadDirty:
                    if (chunk.isUniform)
                        l0[flatIndex] = chunk.uniformMaterial;
                    else
                        l0[flatIndex] = chunk.bricks[CoordMath.LocalBrickIndex(localBrick)].data;
                }
            }

            return l0;
        }

        // Convenience: store -> L0 -> full pyramid, one call.
        public static AirMipData BuildFromStore(ChunkStore store, int3 windowDimsBricks, int requestedLevels = 4)
        {
            uint[] l0 = BuildL0FromStore(store, windowDimsBricks);
            return Build(l0, windowDimsBricks, requestedLevels);
        }
    }
}