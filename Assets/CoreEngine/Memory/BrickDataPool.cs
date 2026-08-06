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
            return _freeStack[--_freeCount];
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