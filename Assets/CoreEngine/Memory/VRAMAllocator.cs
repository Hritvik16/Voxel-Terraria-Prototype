using UnityEngine;
using System.Runtime.InteropServices;

namespace CoreEngine.Memory
{
    public class VRAMAllocator : MonoBehaviour
    {
        [Header("Global Memory Pools")]
        public ComputeBuffer StaticBrickPool;
        public ComputeBuffer DynamicFluidPoolA;
        public ComputeBuffer DynamicFluidPoolB;
        public ComputeBuffer SpatialHashTable;

        // Static Pool: 1.5 million bricks. 1m³ each (1,000 voxels). 1-Byte payload per voxel.
        // Total VRAM: ~1.5 GB
        private const int STATIC_BRICK_COUNT = 1500000;
        private const int BYTES_PER_STATIC_BRICK = 1000;

        // Dynamic Pool (Double Buffered): Total 400 MB capacity split into A and B.
        // Each buffer holds 200 MB (approx 25,000 bricks if 8-byte payload per voxel).
        private const int DYNAMIC_BRICK_COUNT_PER_BUFFER = 25000;
        private const int BYTES_PER_DYNAMIC_BRICK = 8000; 

        // Spatial Hash: 2 million buckets, 32-bit uint pointers (4 bytes).
        // Total VRAM: ~8 MB
        private const int SPATIAL_HASH_BUCKETS = 2000000;

        private void Awake()
        {
            InitializeMemoryPools();
        }

        private void InitializeMemoryPools()
        {
            // 1. Allocate the Static Brickmap Pool
            StaticBrickPool = new ComputeBuffer(STATIC_BRICK_COUNT, BYTES_PER_STATIC_BRICK, ComputeBufferType.Raw);
            
            // 2. Allocate the Double-Buffered Dynamic Fluid Pools (Ping-Pong buffers for lock-free physics)
            DynamicFluidPoolA = new ComputeBuffer(DYNAMIC_BRICK_COUNT_PER_BUFFER, BYTES_PER_DYNAMIC_BRICK, ComputeBufferType.Raw);
            DynamicFluidPoolB = new ComputeBuffer(DYNAMIC_BRICK_COUNT_PER_BUFFER, BYTES_PER_DYNAMIC_BRICK, ComputeBufferType.Raw);

            // 3. Allocate the Indirection Grid
            SpatialHashTable = new ComputeBuffer(SPATIAL_HASH_BUCKETS, sizeof(uint), ComputeBufferType.Raw);

            Debug.Log("<color=#00FF00>VRAM Pools Successfully Seized.</color> Static: 1.5GB | Dynamic: 400MB");
            
            // --- NEW: Phase 1 Test Injection ---
            InjectDevCheckerboard();
        
        }

        private void InjectDevCheckerboard()
        {
            // A 1m³ brick is 10x10x10 voxels (1,000 bytes)
            byte[] mockBrick = new byte[1000];

            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    for (int z = 0; z < 10; z++)
                    {
                        // 1D Array Flattening
                        int index = x + (y * 10) + (z * 100);
                        
                        // 3D Parity Check (Alternates between 1 and 2)
                        mockBrick[index] = (byte)(((x + y + z) % 2 == 0) ? 1 : 2);
                    }
                }
            }

            // Upload directly to the GPU's ByteAddressBuffer
            StaticBrickPool.SetData(mockBrick, 0, 0, 1000);
            Debug.Log("<color=#FFA500>Dev Checkerboard injected into VRAM Brick 0.</color>");
        }

        private void OnDestroy()
        {
            // NEVER forget to release ComputeBuffers, or you will leak memory into macOS.
            StaticBrickPool?.Release();
            DynamicFluidPoolA?.Release();
            DynamicFluidPoolB?.Release();
            SpatialHashTable?.Release();
        }
    }
}