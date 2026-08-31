using Unity.Collections;
using System;

namespace VoxelEngine.Memory
{
    public class BrickDataPool : IDisposable
    {
        private NativeArray<byte> _brickData;
        private NativeArray<int> _freeStack;
        private int _freeCount;
        public readonly int Capacity;

        /// High-water mark of simultaneously-allocated bricks.
        ///
        /// Every BrickDataPool instance is sized by a CONSTANT that was never
        /// measured against its own peak: the tier-0 pool by
        /// EngineConfig.BRICK_POOL_CAP, and each cascade tier pool by
        /// LODCascadeManager.DefaultTierPoolCapacity, whose own comment says
        /// "/4 is STILL a guess, not a measured number". Each unit of capacity
        /// costs 512 B of CPU array here plus 512 B of GPU buffer in the
        /// matching mirror, so a guess that is 2x too large is paid twice.
        /// This is the number to size against.
        public int PeakUsed { get; private set; }
        public int InUse => Capacity - _freeCount;

        public BrickDataPool(int capacity)
        {
            Capacity = capacity;
            
            // Allocate the raw voxel arrays
            _brickData = new NativeArray<byte>(capacity * 512, Allocator.Persistent);
            _freeStack = new NativeArray<int>(capacity, Allocator.Persistent);
            _freeCount = capacity;

            // Initialize free stack backwards so first Alloc returns index 0
            for (int i = 0; i < capacity; i++)
            {
                _freeStack[i] = capacity - 1 - i;
            }
        }

        public int Alloc()
        {
            if (_freeCount == 0)
            {
                throw new InvalidOperationException("BrickDataPool exhausted. LRU valve failed or cap is too low.");
            }
            int idx = _freeStack[--_freeCount];
            int used = Capacity - _freeCount;
            if (used > PeakUsed) PeakUsed = used;
            return idx;
        }

        public void Free(int index)
        {
            _freeStack[_freeCount++] = index;
        }

        public NativeArray<byte> RawData => _brickData;

        public void Dispose()
        {
            if (_brickData.IsCreated) _brickData.Dispose();
            if (_freeStack.IsCreated) _freeStack.Dispose();
        }
    }
}