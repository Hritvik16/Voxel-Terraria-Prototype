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

        /// Non-throwing form of Alloc.
        ///
        /// WHY BOTH EXIST: Alloc's exception is the right contract for the CPU
        /// path -- §3.6's LRU valve is supposed to make exhaustion impossible, so
        /// reaching it is a bug that should be loud rather than silently degraded
        /// terrain. But Burst has no useful exception support, so any allocation
        /// reachable from compiled job code needs a return-code form.
        ///
        /// Alloc() keeps its contract and all of its callers. This is additive:
        /// nothing switches to TryAlloc in this commit, and a caller that ignores
        /// a false return has written the silent-degradation bug the throwing
        /// form exists to prevent.
        public bool TryAlloc(out int index)
        {
            if (_freeCount == 0) { index = -1; return false; }
            index = _freeStack[--_freeCount];
            return true;
        }

        /// Slots currently available. Exposed so a caller can reserve capacity
        /// for a whole chunk before starting one, rather than discovering
        /// exhaustion halfway through and leaving a chunk half-built.
        public int FreeCount => _freeCount;

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