using System;
using System.Collections.Generic;

namespace VoxelEngine.Memory
{
    public class ChunkHandleAllocator
    {
        private readonly Stack<BrickHandle[]> _pool = new Stack<BrickHandle[]>();

        public ChunkHandleAllocator(int initialCapacity = 1000)
        {
            for (int i = 0; i < initialCapacity; i++)
            {
                _pool.Push(new BrickHandle[4096]);
            }
        }

        public BrickHandle[] Alloc()
        {
            return _pool.Count > 0 ? _pool.Pop() : new BrickHandle[4096];
        }

        public void Free(BrickHandle[] handles)
        {
            // Clear the array before pushing back to the pool to prevent handle ghosting
            Array.Clear(handles, 0, handles.Length);
            _pool.Push(handles);
        }
    }
}