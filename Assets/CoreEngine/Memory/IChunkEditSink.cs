// Assets/CoreEngine/Memory/IChunkEditSink.cs
//
// Amendment 8.15 §5 "Live" (task 93a): "this resident chunk now differs from
// the generator by a player edit". ChunkStore.SetVoxel (the sole terrain
// writer) marks the edited brick in Chunk.editMask and tells the sink once per
// chunk until the sink clears Chunk.editPending; InsertChunk tells it about a
// chunk that arrives with marks (a reloaded D.1). The shell streamer is the
// only sink: it re-summarises the chunk into the shells that draw it.
// Main thread only, like SetVoxel.
using Unity.Mathematics;

namespace VoxelEngine.Memory
{
    public interface IChunkEditSink
    {
        void ChunkEdited(int3 chunkCoord);
    }
}
