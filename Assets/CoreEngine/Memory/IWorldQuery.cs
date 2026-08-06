// Assets/CoreEngine/Memory/IWorldQuery.cs
using Unity.Mathematics;

public interface IWorldQuery
{
    byte GetVoxel(int3 worldVoxelCoord);
}