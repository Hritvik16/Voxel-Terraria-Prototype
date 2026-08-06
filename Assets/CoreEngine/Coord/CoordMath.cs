// Assets/CoreEngine/Coord/CoordMath.cs
using Unity.Mathematics;
using Unity.Burst;
using System.Runtime.CompilerServices;

[BurstCompile]
public static class CoordMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int3 WorldToVoxel(float3 worldPos)
    {
        return (int3)math.floor(worldPos * 10.0f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int3 VoxelToBrick(int3 voxelCoord)
    {
        return voxelCoord >> 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int3 BrickToChunk(int3 brickCoord)
    {
        return brickCoord >> 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int3 VoxelToChunk(int3 voxelCoord)
    {
        return (voxelCoord >> 3) >> 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int3 LocalVoxelIndex3D(int3 voxelCoord)
    {
        return voxelCoord & 7;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int3 LocalBrickIndex3D(int3 brickCoord)
    {
        return brickCoord & 15;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalVoxelIndex(int3 localVoxel)
    {
        return (localVoxel.z << 6) | (localVoxel.y << 3) | localVoxel.x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LocalBrickIndex(int3 localBrick)
    {
        return (localBrick.z << 8) | (localBrick.y << 4) | localBrick.x;
    }
}