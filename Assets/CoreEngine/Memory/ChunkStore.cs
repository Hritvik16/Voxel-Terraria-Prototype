// Assets/CoreEngine/Memory/ChunkStore.cs
using System;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Memory;

public class ChunkStore : IWorldQuery, IEditService
{
    private readonly Chunk[] _residentWindow;
    private readonly int3 _windowMask;
    private readonly int3 _windowDims;
    
    private readonly BrickDataPool _brickPool;
    private readonly ChunkHandleAllocator _handleAllocator;

    public ChunkStore(BrickDataPool brickPool, ChunkHandleAllocator handleAllocator)
    {
        _brickPool = brickPool;
        _handleAllocator = handleAllocator;

        // Window dimensions must be powers of two for bitwise masking
        _windowDims = new int3(EngineConfig.WINDOW_CHUNKS_XZ, EngineConfig.WINDOW_CHUNKS_Y, EngineConfig.WINDOW_CHUNKS_XZ);
        _windowMask = _windowDims - new int3(1, 1, 1);
        
        _residentWindow = new Chunk[_windowDims.x * _windowDims.y * _windowDims.z];
    }

    private int GetFlatIndex(int3 chunkCoord)
    {
        // Toroidal ring-buffer masking per axis[cite: 2]
        int3 wrapped = chunkCoord & _windowMask;
        return wrapped.x + _windowDims.x * (wrapped.y + _windowDims.y * wrapped.z);
    }

    public void InsertChunk(Chunk chunk)
    {
        _residentWindow[GetFlatIndex(chunk.coord)] = chunk;
    }

    public Chunk GetChunk(int3 chunkCoord)
    {
        int flatIndex = GetFlatIndex(chunkCoord);
        Chunk c = _residentWindow[flatIndex];
        
        if (c != null && c.coord.Equals(chunkCoord)) 
        {
            return c;
        }
        return null;
    }

    public byte GetVoxel(int3 worldVoxelCoord)
    {
        int3 chunkCoord = CoordMath.VoxelToChunk(worldVoxelCoord);
        Chunk chunk = GetChunk(chunkCoord);
        
        // 1. Chunk uniform check[cite: 2]
        if (chunk == null) return 0; // Unloaded treats as air
        if (chunk.isUniform) return chunk.uniformMaterial;

        int3 localBrick = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(worldVoxelCoord));
        int brickFlatIndex = CoordMath.LocalBrickIndex(localBrick);
        
        uint handleData = chunk.bricks[brickFlatIndex].data;
        bool isDense = (handleData & 0x80000000) != 0;

        // 2. Brick uniform check[cite: 2]
        if (!isDense)
        {
            return (byte)(handleData & 0xFF);
        }

        // 3. Dense body read[cite: 2]
        int poolIndex = (int)(handleData & 0x3FFFFFFF);
        int3 localVoxel = CoordMath.LocalVoxelIndex3D(worldVoxelCoord);
        int voxelFlatIndex = CoordMath.LocalVoxelIndex(localVoxel);
        
        return _brickPool.RawData[(poolIndex * 512) + voxelFlatIndex];
    }

    public void SetVoxel(int3 worldVoxelCoord, byte material)
    {
        int3 chunkCoord = CoordMath.VoxelToChunk(worldVoxelCoord);
        Chunk chunk = GetChunk(chunkCoord);
        
        if (chunk == null) return; // Cannot edit unloaded chunks

        // 1. Chunk uniform check and expansion[cite: 2]
        if (chunk.isUniform)
        {
            if (chunk.uniformMaterial == material) return; // No-op fast path[cite: 2]
            
            chunk.isUniform = false;
            chunk.bricks = _handleAllocator.Alloc();
            
            for (int i = 0; i < 4096; i++)
            {
                chunk.bricks[i].data = chunk.uniformMaterial;
            }
        }

        int3 localBrick = CoordMath.LocalBrickIndex3D(CoordMath.VoxelToBrick(worldVoxelCoord));
        int brickFlatIndex = CoordMath.LocalBrickIndex(localBrick);
        
        uint handleData = chunk.bricks[brickFlatIndex].data;
        bool isDense = (handleData & 0x80000000) != 0;

        // 2. Brick uniform check and expansion[cite: 2]
        if (!isDense)
        {
            byte brickMaterial = (byte)(handleData & 0xFF);
            if (brickMaterial == material) return; // No-op fast path[cite: 2]

            int poolIndex = _brickPool.Alloc();
            int startOffset = poolIndex * 512;
            
            NativeArray<byte> rawData = _brickPool.RawData;
            for (int i = 0; i < 512; i++)
            {
                rawData[startOffset + i] = brickMaterial;
            }

            chunk.bricks[brickFlatIndex].data = 0x80000000 | (uint)poolIndex;
            handleData = chunk.bricks[brickFlatIndex].data;
        }

        // 3. Write Voxel[cite: 2]
        int poolIdx = (int)(handleData & 0x3FFFFFFF);
        int3 localVoxel = CoordMath.LocalVoxelIndex3D(worldVoxelCoord);
        int voxelFlatIndex = CoordMath.LocalVoxelIndex(localVoxel);

        NativeArray<byte> finalData = _brickPool.RawData;
        finalData[(poolIdx * 512) + voxelFlatIndex] = material;

        // 4. Mark dirty for clipmap upload and delta save[cite: 2]
        chunk.dirty = true;
        chunk.deltaDirty = true;
    }
}