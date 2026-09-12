// Assets/CoreEngine/Mirror/IChunkDirtySink.cs
//
// "This chunk changed; the GPU mirror needs to hear about it."
//
// §3.7's clipmap is written FROM the CPU state and never the reverse (CLAUDE.md
// hard invariant), so every CPU edit has to tell it which chunk moved. That is
// TerrainClipmap.MarkDirty, and this interface only names it as a contract so
// EditService can be driven by an EditMode test without a GPU.
//
// NOTE THE TWO SEPARATE DIRTY FLAGS, because they are easy to confuse:
//   Chunk.dirty / Chunk.deltaDirty   set inside ChunkStore.SetVoxel, for the
//                                    upload budget and the save delta (§4.2)
//   TerrainClipmap._dirtyChunks      the mirror's own upload queue, set here
// ChunkStore does the first pair itself; nothing but a caller can do the second,
// which is why every hand-written edit path in this repo does SetVoxel followed
// by MarkDirty, and why §8.3 folds the pair into one call.

using Unity.Mathematics;

public interface IChunkDirtySink
{
    void MarkDirty(int3 chunkCoord);
}
