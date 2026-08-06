// Assets/CoreEngine/Streaming/IChunkProvider.cs
using Unity.Mathematics;

public interface IChunkProvider
{
    bool TryGetChunk(int3 chunkCoord, out object chunk);
}