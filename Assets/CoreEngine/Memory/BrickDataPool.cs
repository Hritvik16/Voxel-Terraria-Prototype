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

        /// Sort the TOP `k` entries of the free stack so the next `k` Allocs
        /// return CONSECUTIVE indices.
        ///
        /// WHY: a chunk's dense bodies are uploaded to the GPU by
        /// TerrainClipmap.UploadDirtyBrickBodies, which sorts the dirty slots
        /// and coalesces CONSECUTIVE ones into a single SetData. The free stack
        /// starts perfectly ordered, but eviction pushes slots back in whatever
        /// order chunks happen to die, so after churn a chunk's bricks land on
        /// scattered indices and each short run costs its own driver call.
        ///
        /// MEASURED (2026-08-31, release standalone, acceptance rig):
        ///   dense slots/frame  p99 3308  max 4832
        ///   runs (SetData)     p99  241  max 1084
        ///   runs/slots = 0.224 -- average run only ~4.5 bricks (~2.3 KB/call)
        /// "brick bodies" is the dominant upload phase (p99 0.29-1.15ms) and at
        /// ~1084 calls/frame the per-call overhead, not the bytes, is what costs.
        ///
        /// Sorting DESCENDING means Alloc (which pops from the top) hands back
        /// ASCENDING consecutive indices, which is the order the run-coalescer
        /// wants. Only the top slice is touched, so this is O(k log k) on a few
        /// thousand entries rather than a full sort of the 500,000-entry stack.
        ///
        /// SAFETY: any free index is a valid allocation, so reordering the free
        /// list cannot change correctness -- only WHICH slot a brick lands in.
        /// The CPU handle and the GPU mirror are both written from that slot
        /// afterwards, so ClipmapValidator is the check that this held.
        public void SortFreeTop(int k)
        {
            if (_freeCount < 2) return;
            int n = Math.Min(k, _freeCount);
            int start = _freeCount - n;
            var tmp = new int[n];
            for (int i = 0; i < n; i++) tmp[i] = _freeStack[start + i];
            Array.Sort(tmp);
            // descending into the stack => ascending out of Alloc
            for (int i = 0; i < n; i++) _freeStack[start + i] = tmp[n - 1 - i];
        }

        public NativeArray<byte> RawData => _brickData;

        public void Dispose()
        {
            if (_brickData.IsCreated) _brickData.Dispose();
            if (_freeStack.IsCreated) _freeStack.Dispose();
        }
    }
}