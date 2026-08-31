using Unity.Collections;
using System;

namespace VoxelEngine.Memory
{
    public class BrickDataPool : IDisposable
    {
        private NativeArray<byte> _brickData;
        public readonly int Capacity;

        // FREE LIST AS RUNS, not a flat stack of slots.
        //
        // WHY (measured 2026-08-31): TerrainClipmap.UploadDirtyBrickBodies can
        // merge only CONSECUTIVE pool slots into one SetData. Allocating a
        // chunk's ~460 dense bodies one slot at a time left them scattered
        // after eviction churn -- runs/slots 0.224, ~4.5-13.7 bricks per driver
        // call, ~241 calls/frame at p99 -- and per-call overhead is what makes
        // "brick bodies" the dominant upload phase (0.29-1.15ms of a 1.0ms
        // budget). Allocating a chunk's bodies as ONE contiguous range collapses
        // that to one call per chunk.
        //
        // Sorted by start, adjacent runs merged on insert, so a freed chunk's
        // range comes back as a single reusable run instead of ~460 fragments.
        // See scratchpad/brick_contiguity_plan.md for the reviewed design.
        private readonly System.Collections.Generic.List<Run> _free =
            new System.Collections.Generic.List<Run>();
        private int _freeCount;

        private struct Run
        {
            public int start, len;
            public Run(int s, int l) { start = s; len = l; }
            public int End => start + len;   // exclusive
        }

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
            _freeCount = capacity;
            if (capacity > 0) _free.Add(new Run(0, capacity));
        }

        /// Index of the run containing `slot`, or the bitwise complement of the
        /// insertion point if no run contains it. Binary search over starts.
        private int FindRun(int slot)
        {
            int lo = 0, hi = _free.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                Run r = _free[mid];
                if (slot < r.start) hi = mid - 1;
                else if (slot >= r.End) lo = mid + 1;
                else return mid;
            }
            return ~lo;
        }

        /// Takes `n` slots off the front of run `i`, removing it when emptied.
        private int TakeFront(int i, int n)
        {
            Run r = _free[i];
            int baseIndex = r.start;
            if (r.len == n) _free.RemoveAt(i);
            else _free[i] = new Run(r.start + n, r.len - n);
            _freeCount -= n;
            int used = Capacity - _freeCount;
            if (used > PeakUsed) PeakUsed = used;
            return baseIndex;
        }

        /// Insert [start,len) and merge with any adjacent runs.
        private void Insert(int start, int len)
        {
            int i = FindRun(start);
            if (i >= 0)
                throw new InvalidOperationException(
                    $"BrickDataPool double-free: slot {start} is already in the free list. " +
                    "This is the CPU/GPU desync class §0.3 exists to prevent, not a leak.");
            i = ~i;
            _free.Insert(i, new Run(start, len));
            _freeCount += len;

            // merge right then left, so at most two joins per insert
            if (i + 1 < _free.Count && _free[i].End == _free[i + 1].start)
            {
                _free[i] = new Run(_free[i].start, _free[i].len + _free[i + 1].len);
                _free.RemoveAt(i + 1);
            }
            if (i > 0 && _free[i - 1].End == _free[i].start)
            {
                _free[i - 1] = new Run(_free[i - 1].start, _free[i - 1].len + _free[i].len);
                _free.RemoveAt(i);
            }
        }

        /// Single-slot allocation. LOWEST free index, which keeps allocations
        /// packed at the bottom of the pool and leaves the top free for ranges.
        ///
        /// NOTE for MemoryModelTests.Pool_DoesNotLeak_AfterAllocAndFree, which
        /// asserts free-then-alloc returns the SAME slot: that test allocates
        /// the very first brick (slot 0, because the old free stack was seeded
        /// descending), frees it, and re-allocates. Lowest-first returns 0, so
        /// the assertion holds -- satisfied by first-fit, not by LIFO recency.
        /// The distinction matters if that test is ever generalised.
        public int Alloc()
        {
            if (_freeCount == 0)
                throw new InvalidOperationException(
                    "BrickDataPool exhausted. LRU valve failed or cap is too low.");
            return TakeFront(0, 1);
        }

        /// Single-slot allocation PREFERRING a slot at or after `hint`, falling
        /// back to the nearest run before it, then to lowest-first.
        ///
        /// THE CASE THIS EXISTS FOR (raised in §0.3 review, and it is a real
        /// hole in the plan's original argument): contiguity established at
        /// admission survives the evict/re-admit cycle, but NOT mid-residency
        /// churn. §4.5 coalescing frees a brick that has gone uniform while its
        /// chunk stays resident; a later dig re-densifies it through
        /// ChunkStore.SetVoxel -> Alloc(). With plain lowest-first that slot
        /// comes from the bottom of the pool, nowhere near its chunk's range,
        /// and ordinary digging degrades contiguity brick by brick on a cycle
        /// eviction never touches. Handing SetVoxel a hint from a neighbouring
        /// dense brick in the same chunk keeps the re-densified body inside its
        /// own range, where the hole it left almost always still is.
        public int AllocNear(int hint)
        {
            if (_freeCount == 0)
                throw new InvalidOperationException(
                    "BrickDataPool exhausted. LRU valve failed or cap is too low.");
            if (_free.Count == 0) return TakeFront(0, 1);

            int i = FindRun(hint);
            if (i >= 0)
            {
                // hint itself is free -- take exactly it, not the run's front
                Run r = _free[i];
                if (hint == r.start) return TakeFront(i, 1);
                if (hint == r.End - 1)
                {
                    _free[i] = new Run(r.start, r.len - 1);
                    _freeCount--;
                    int u = Capacity - _freeCount;
                    if (u > PeakUsed) PeakUsed = u;
                    return hint;
                }
                // split the run around hint
                _free[i] = new Run(r.start, hint - r.start);
                _free.Insert(i + 1, new Run(hint + 1, r.End - hint - 1));
                _freeCount--;
                int u2 = Capacity - _freeCount;
                if (u2 > PeakUsed) PeakUsed = u2;
                return hint;
            }

            int ins = ~i;
            if (ins < _free.Count) return TakeFront(ins, 1);          // nearest run after
            if (ins > 0)                                             // else nearest before
            {
                Run r = _free[ins - 1];
                _free[ins - 1] = new Run(r.start, r.len - 1);
                if (_free[ins - 1].len == 0) _free.RemoveAt(ins - 1);
                _freeCount--;
                int u = Capacity - _freeCount;
                if (u > PeakUsed) PeakUsed = u;
                return r.End - 1;
            }
            return TakeFront(0, 1);
        }

        /// `n` CONTIGUOUS slots, or false. Best-fit: the smallest run that
        /// still fits, so large runs stay intact for later chunks.
        ///
        /// Returning false is normal, not an error -- the caller falls back to
        /// per-slot allocation and that chunk simply uploads as several runs,
        /// which is exactly today's behaviour. Allocation never fails while
        /// free slots exist, so §3.6's "the triggering edit always succeeds"
        /// is preserved.
        public bool TryAllocRange(int n, out int baseIndex)
        {
            baseIndex = -1;
            if (n <= 0 || _freeCount < n) return false;

            int best = -1, bestLen = int.MaxValue;
            for (int i = 0; i < _free.Count; i++)
            {
                int len = _free[i].len;
                if (len >= n && len < bestLen) { best = i; bestLen = len; if (len == n) break; }
            }
            if (best < 0) return false;
            baseIndex = TakeFront(best, n);
            return true;
        }

        public void Free(int index)
        {
            if (index < 0 || index >= Capacity)
                throw new ArgumentOutOfRangeException(nameof(index));
            Insert(index, 1);
        }

        /// Return a whole run in one operation. Used by eviction, so a chunk's
        /// range comes back intact instead of as ~460 single-slot inserts that
        /// only re-merge if every merge is correct.
        public void FreeRange(int baseIndex, int n)
        {
            if (n <= 0) return;
            if (baseIndex < 0 || baseIndex + n > Capacity)
                throw new ArgumentOutOfRangeException(nameof(baseIndex));
            Insert(baseIndex, n);
        }

        /// Diagnostic only: how many distinct free runs exist. A rising count
        /// is external fragmentation, the failure mode this design trades for.
        public int FreeRunCount => _free.Count;

        public NativeArray<byte> RawData => _brickData;

        public void Dispose()
        {
            if (_brickData.IsCreated) _brickData.Dispose();
        }
    }
}