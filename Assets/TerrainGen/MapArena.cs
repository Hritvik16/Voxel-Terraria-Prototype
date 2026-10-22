// Assets/TerrainGen/MapArena.cs
//
// ENGINE PLAN STEP 1 (owner Q26 (a), 2026-10-09): the world map is built by ONE
// compiled path -- Burst, in the Editor and in the IL2CPP player alike
// (MapJob.cs). Burst code holds no managed arrays, so the generator's buffers
// are raw pointers from this arena: ONE block, taken from the OS by the
// managed caller (Marshal.AllocHGlobal: no Unity type, so TerrainGen keeps its
// portability contract, TerrainSpec.cs), carved by pointer arithmetic only.
//   Alloc    scratch, from the bottom up. Mark() / Release(mark) frees
//            everything allocated since the mark: a stage frees its own
//            scratch on the way out, so the build's peak is one stage's
//            scratch plus the results, not the sum of every stage.
//   Root     from the top down; lives until Dispose (the map's planes, the
//            lists, the results).
// The block is reserved, not touched: the OS commits only the pages the build
// writes (PeakBytes reports how much that was). Running out sets Overflow and
// throws; MapJobs turns that into a managed exception. Where TerrainGen.Burst
// registers an OS page allocator (MapJobs.BlockAlloc; macOS: mmap / munmap) the
// block comes from it, because a freed malloc block stays resident on macOS
// (libmalloc keeps its dirty pages: +~100 MB for the whole run in the
// 2026-10-10 player, Logs/tg2world_20261010_005419_mem_peak_breakdown.txt).
// PList<T> is a growable list. On Root memory (the default) it may outlive any
// Mark; with temp = true it must die inside the Mark it was made in. The old
// block of a grown list stays until Dispose / Release (geometric growth bounds
// that waste by the list's final size).
using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace TerrainGen
{
    public unsafe struct MapArena : IDisposable
    {
        /// Bytes reserved per arena. Seed 42's sizeClass 2 v4 build peaks at
        /// 69 MB (WorldMapBurstTests logs it, task 45); untouched pages cost nothing.
        public const long DEFAULT_BYTES = 768L << 20;

        private byte* _base;
        private long _cap, _lo, _hi;
        private bool _os;   // the block came from MapJobs.BlockAlloc
        /// High-water mark of scratch + root bytes in use.
        public long PeakBytes;
        public bool Overflow;

        /// Managed only (the block comes from the OS); pass the arena to Burst by pointer.
        public static MapArena Create(long bytes = DEFAULT_BYTES)
        {
            var a = new MapArena { _cap = bytes, _hi = bytes, _os = MapJobs.BlockAlloc != null };
            a._base = (byte*)(a._os ? MapJobs.BlockAlloc(bytes) : Marshal.AllocHGlobal((IntPtr)bytes));
            return a;
        }

        public bool IsCreated => _base != null;

        private void* Take(long bytes, bool root)
        {
            bytes = (math.max(bytes, 16L) + 15L) & ~15L;
            if (_lo + bytes > _hi)
            {
                Overflow = true;
                throw new InvalidOperationException("MapArena: out of memory (raise MapArena.DEFAULT_BYTES)");
            }
            byte* p;
            if (root) { _hi -= bytes; p = _base + _hi; }
            else { p = _base + _lo; _lo += bytes; }
            PeakBytes = math.max(PeakBytes, _lo + (_cap - _hi));
            for (long i = 0; i < bytes; i++) p[i] = 0;
            return p;
        }

        /// Zeroed scratch for `count` elements, freed by Release.
        public T* Alloc<T>(int count) where T : unmanaged => (T*)Take((long)count * sizeof(T), false);

        /// Zeroed memory for `count` elements that lives until Dispose.
        public T* Root<T>(int count) where T : unmanaged => (T*)Take((long)count * sizeof(T), true);

        public T* Fill<T>(int count, T v) where T : unmanaged
        {
            T* p = Alloc<T>(count);
            for (int i = 0; i < count; i++) p[i] = v;
            return p;
        }

        public T* Copy<T>(T* src, int count) where T : unmanaged
        {
            T* p = Alloc<T>(count);
            for (int i = 0; i < count; i++) p[i] = src[i];
            return p;
        }

        public T* RootCopy<T>(T* src, int count) where T : unmanaged
        {
            T* p = Root<T>(count);
            if (src != null) for (int i = 0; i < count; i++) p[i] = src[i];
            return p;
        }

        public int Mark() => (int)_lo;

        public void Release(int mark) => _lo = mark;

        public void Dispose()
        {
            if (_base == null) return;
            if (_os) MapJobs.BlockFree((IntPtr)_base, _cap);
            else Marshal.FreeHGlobal((IntPtr)_base);
            _base = null; _lo = 0; _hi = _cap;
        }
    }

    /// A growable list on a MapArena (see the file header). Pass it by ref:
    /// a copy that grows leaves the original behind.
    public unsafe struct PList<T> where T : unmanaged
    {
        public T* Ptr;
        public int Length;
        private int _cap;
        private MapArena* _a;
        private bool _temp;

        public PList(MapArena* a, int capacity, bool temp = false)
        {
            _a = a; _temp = temp; _cap = math.max(4, capacity);
            Ptr = temp ? a->Alloc<T>(_cap) : a->Root<T>(_cap);
            Length = 0;
        }

        public ref T this[int i] => ref Ptr[i];

        public void Add(T v)
        {
            if (Length == _cap)
            {
                T* np = _temp ? _a->Alloc<T>(_cap * 2) : _a->Root<T>(_cap * 2);
                for (int i = 0; i < Length; i++) np[i] = Ptr[i];
                Ptr = np; _cap *= 2;
            }
            Ptr[Length++] = v;
        }

        public void Insert(int at, T v)
        {
            Add(v);
            for (int i = Length - 1; i > at; i--) Ptr[i] = Ptr[i - 1];
            Ptr[at] = v;
        }

        public void RemoveAt(int at)
        {
            for (int i = at; i < Length - 1; i++) Ptr[i] = Ptr[i + 1];
            Length--;
        }

        public void RemoveLast() => Length--;
        public void Clear() => Length = 0;

        public void CopyFrom(ref PList<T> src)
        {
            Length = 0;
            for (int i = 0; i < src.Length; i++) Add(src.Ptr[i]);
        }
    }

    /// Deterministic in-place sorts for the generator (keys are distinct
    /// wherever order matters, so any correct sort gives the same result).
    public static unsafe class PSort
    {
        public static void Longs(long* a, int n)
        {
            for (int i = n / 2 - 1; i >= 0; i--) SiftL(a, i, n);
            for (int end = n - 1; end > 0; end--) { long t = a[0]; a[0] = a[end]; a[end] = t; SiftL(a, 0, end); }
        }

        private static void SiftL(long* a, int i, int n)
        {
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < n && a[l] > a[m]) m = l;
                if (r < n && a[r] > a[m]) m = r;
                if (m == i) return;
                long t = a[i]; a[i] = a[m]; a[m] = t; i = m;
            }
        }

        public static void Ints(int* a, int n)
        {
            for (int i = n / 2 - 1; i >= 0; i--) SiftI(a, i, n);
            for (int end = n - 1; end > 0; end--) { int t = a[0]; a[0] = a[end]; a[end] = t; SiftI(a, 0, end); }
        }

        private static void SiftI(int* a, int i, int n)
        {
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < n && a[l] > a[m]) m = l;
                if (r < n && a[r] > a[m]) m = r;
                if (m == i) return;
                int t = a[i]; a[i] = a[m]; a[m] = t; i = m;
            }
        }
    }
}
