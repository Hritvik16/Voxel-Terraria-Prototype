// Assets/CoreEngine/Coord/CoordMath.hlsl
#ifndef COORD_MATH_INCLUDED
#define COORD_MATH_INCLUDED

int3 WorldToVoxel(float3 worldPos)
{
    return (int3)floor(worldPos * 10.0);
}

int3 VoxelToBrick(int3 voxelCoord)
{
    return voxelCoord >> 3;
}

int3 BrickToChunk(int3 brickCoord)
{
    return brickCoord >> 4;
}

int3 LocalVoxelIndex3D(int3 voxelCoord)
{
    return voxelCoord & 7;
}

int3 LocalBrickIndex3D(int3 brickCoord)
{
    return brickCoord & 15;
}

int LocalVoxelIndex(int3 localVoxel)
{
    return (localVoxel.z << 6) | (localVoxel.y << 3) | localVoxel.x;
}

int LocalBrickIndex(int3 localBrick)
{
    return (localBrick.z << 8) | (localBrick.y << 4) | localBrick.x;
}

#endif