// Assets/CoreEngine/Memory/IEditService.cs
using Unity.Mathematics;

public interface IEditService
{
    void SetVoxel(int3 worldVoxelCoord, byte material);
}