// Assets/CoreEngine/Mirror/ShellStore.cs
//
// Amendment 8.14 §5 rows 2, 3, 14, 15 (engine step 11a, task 77): ONE coarse
// shell's GPU storage. Shells 1+ only; shell 0 is TerrainClipmap.
//
// LAYOUT (row 2, the two-level directory; Appendix A addition):
//   DirectoryBuffer  one uint per RING slot (pow2 ring, & masks only):
//                      0                      ABSENT  -- the ray reads shell k+1 (row 5)
//                      UNIFORM | material     the whole chunk is one material (air too)
//                      PAGE    | page index   a non-uniform chunk: see PageBuffer
//   PageBuffer       4096 uints (16 KB) per page, one per non-uniform chunk, in
//                    CoordMath.LocalBrickIndex order ((bz<<8)|(by<<4)|bx), each
//                    the engine's brick handle: bit 31 = dense, low 30 bits =
//                    body index; else the uniform material byte (0 = air).
//   BrickBuffer      128 uints (512 B) per dense brick, voxel (vz<<6)|(vy<<3)|vx,
//                    the BrickDataPool body layout, so 11b's march reads a
//                    shell brick exactly as it reads a 0.1 m one.
// SHARED BUFFERS (task 80, step 11b): the raymarch binds ONE directory, ONE
// page and ONE brick buffer for all shells -- 7 shells x 3 buffers beside the
// kernel's ~23 would pass Metal's 31-buffer limit. Each shell owns a range of
// each (DirBase / PageBase / BrickBase, in entries / pages / bricks); entries,
// page indices and body indices stay shell-LOCAL, the shader adds the bases.
// A store built without SharedBuffers allocates its own three (tests).
// The CPU keeps the directory, each slot's chunk TAG (for the stale-slot
// validator) and each page's body list (to free it) -- NO brick bodies (row 1:
// "GPU only; the CPU keeps no copy"), except the test-only shadow below.
//
// RULES
//   * CPU sole writer (row 3): bodies come from GenerateShellChunk on the CPU;
//     this class only uploads. Main thread only (single-writer, CLAUDE.md).
//   * Every buffer zeroed at construction (row 3; the TerrainClipmap.cs:243 lesson).
//   * A full pool or page table REFUSES the chunk and counts it (row 15); never throws.
//   * Upload bytes are charged against a caller-given budget, the remainder
//     of the shared 3 MiB/frame cap (row 14, §0.2); over budget = not now.
//   * Bitwise integer coordinates only; the ring is a power of two (§6.2).
//   * REDRAW (task 82, 8.14 row 9 / E5): Store(sc, ref budget, replace: true)
//     re-stores a chunk the slot already holds WITHOUT a gap: the new page and
//     bodies are uploaded beside the old ones, the entry flips when they are
//     all up, and the old page and bodies are DEFERRED -- they return to the
//     free stacks only after the next successful FlushDirectory, so no body a
//     GPU entry still points at is ever rewritten (the WHAT THE GPU MAY SEE
//     rule in ShellStreamer, for a free that happens mid-Update).
//   * AIR-BRICK MASK (task 93b5, owner Q58 (a); an Appendix A addition): per
//     page, 64 bits (2 uints): bit g = brick group g ((gz << 4) | (gy << 2) | gx,
//     g = brick >> 2 per axis: 4x4x4 bricks) holds a brick whose handle is not
//     uniform air (GroupMask; a dense brick always counts). Built from the page
//     staging in Store, uploaded with the page (before the directory entry that
//     publishes it), never cleared: a page's mask is read only through an entry
//     that points at it. It lives in the TAIL of the page buffer (MaskBase, in
//     uints), not in a buffer of its own: one more binding would bring the
//     raymarch kernel nearer Metal's 31-buffer limit (SHARED BUFFERS above).
//     ShellMarch leaps an empty group's bricks without reading them.
//   * COLUMN TOPS (task 116, the column sky leap; owner Q66 (a); an Appendix A
//     addition beside the mask): per page, 16 bytes (COLTOP_UINTS uints): byte j =
//     group column j ((gz << 2) | gx, g = brick >> 2 on x and z: 4x4 bricks, every
//     brick y) holds non-air handles up to local brick y top - 1; top = 0 (all air)
//     .. 16 (ColumnTops; a handle that is not uniform air counts, as the mask's).
//     Built in Store, uploaded with the page and the mask, after them in the tail
//     (ColTopBase), read only through an entry that points at the page. Pages are
//     immutable once published (an edit is a redraw: a new page), so a table never
//     goes stale. ShellMarch leaps a rising ray above its group column's top.
//   * SOLID TOP (task 99, the sky-ray exit): SolidTopY is a running max, NEVER lowered
//     (evictions, aborts and lower redraws leave it), of the top (exclusive, 0.1 m
//     voxels) of every brick a Store was given that is not uniform air (dense bricks
//     count; fluids count). Raised in Store before anything is uploaded, so it is never
//     below anything the GPU can read through this store. CPU-only (one int).
//   * COLUMN HEIGHTS (task 153, the shells' height bound; owner Q97 (a); an Appendix A
//     addition after the column tops): per RING COLUMN (ring.x * ring.z uints, index
//     (cz & mz) * ring.x + (cx & mx)) one int, the HEIGHT of world column (cx, cz) of the
//     box the directory was last flushed with (SetHeightBox): every chunk of that column
//     inside the box and outside the hole is held -- the slot's entry is not ABSENT and
//     its tag is that chunk -- and holds nothing that is not uniform air at or above it
//     (the slot's ChunkSolidTopY; dense bricks and fluids count). A chunk that is not
//     held, or a slot holding another chunk, counts as solid to its top. int.MinValue =
//     all held and all air; HEIGHT_NONE = a ring column outside the box (never used).
//     Data-derived, no constants. Rebuilt from _dir in FlushDirectory and uploaded in the
//     SAME call as the directory (one budget check for both), so the GPU's table always
//     describes the GPU's directory and published box: raised the frame a taller chunk is
//     published (first load, op redraws, edit summaries: all come through Store), lowered
//     the frame it is replaced or evicted. All shells' tables share one region at the end
//     of the page buffer (HeightBase; this store's at HeightOffset).
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;
using VoxelEngine.WorldGen;

namespace VoxelEngine.Mirror
{
    public sealed class ShellStore : IDisposable
    {
        public const uint ABSENT = 0u;
        public const uint UNIFORM = 0x40000000u;
        public const uint PAGE = 0x80000000u;
        public const uint DENSE = 0x80000000u;   // brick handle flag, as ChunkStore's
        public const int MASK_UINTS = 2;            // task 93b5: the air-brick mask per page
        public const int COLTOP_UINTS = 4;          // task 116: the 16 group-column tops per page (bytes)
        public const int HEIGHT_NONE = int.MaxValue;  // task 153: a ring column the box does not cover
        private const int TAIL_UINTS = MASK_UINTS + COLTOP_UINTS;
        private const int BRICK_UINTS = 128;
        private const int PAGE_UINTS = EngineConfig.BRICKS_PER_CHUNK;

        /// Test hook: mirror every GPU write into CPU arrays so EditMode (which
        /// runs -nographics and cannot read buffers back) can check the upload
        /// addressing byte for byte. Off in the player. Set before construction.
        public static bool ShadowUploads;

        public int Shell { get; }
        public int3 Ring { get; }
        public int SlotCount { get; }
        public int PoolCap { get; }
        public int PageCap { get; }
        public GraphicsBuffer DirectoryBuffer { get; private set; }
        public GraphicsBuffer PageBuffer { get; private set; }
        public GraphicsBuffer BrickBuffer { get; private set; }
        /// This shell's first directory entry / page / brick in the buffers above.
        public int DirBase { get; }
        public int PageBase { get; }
        public int BrickBase { get; }
        /// Task 93b5: the first air-brick mask uint in PageBuffer (the store's page p at MaskBase + (PageBase + p) * MASK_UINTS).
        public int MaskBase { get; }
        /// Task 116: the first column-top uint in PageBuffer (page p at ColTopBase + (PageBase + p) * COLTOP_UINTS).
        public int ColTopBase { get; }
        /// Task 153: the first column-height uint in PageBuffer (every shell's); this store's table
        /// (Columns uints) at HeightBase + HeightOffset (header: COLUMN HEIGHTS).
        public int HeightBase { get; }
        public int HeightOffset { get; }
        public int Columns => Ring.x * Ring.z;
        private readonly bool _ownsBuffers;

        /// One set of buffers for every shell (see the header). Zeroed here (row 3).
        public sealed class SharedBuffers : IDisposable
        {
            public GraphicsBuffer Dir { get; private set; }
            public GraphicsBuffer Pages { get; private set; }
            public GraphicsBuffer Bricks { get; private set; }
            /// Task 93b5: where the air-brick masks start in Pages (uints), MASK_UINTS per page.
            public int MaskBase { get; }
            public long MaskBytes { get; }
            /// Task 116: where the column tops start in Pages (uints), COLTOP_UINTS per page, after the masks.
            public int ColTopBase { get; }
            /// Task 153: where the column heights start in Pages (uints), after the column tops; HeightEntries uints.
            public int HeightBase { get; }
            public int HeightEntries { get; }
            public long BytesAllocated { get; }
            public SharedBuffers(int dirEntries, int pages, int bricks, int heightEntries = 0)
            {
                dirEntries = math.max(1, dirEntries); pages = math.max(1, pages); bricks = math.max(1, bricks);
                MaskBase = pages * PAGE_UINTS;
                MaskBytes = (long)pages * MASK_UINTS * 4;
                ColTopBase = MaskBase + pages * MASK_UINTS;
                HeightBase = ColTopBase + pages * COLTOP_UINTS;
                HeightEntries = math.max(0, heightEntries);
                int pageUints = pages * (PAGE_UINTS + TAIL_UINTS);
                Dir = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, dirEntries, 4);
                Pages = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, pageUints + HeightEntries, 4);
                Bricks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, bricks * BRICK_UINTS, 4);
                ZeroBuffer(Dir, dirEntries);
                ZeroBuffer(Pages, pageUints);
                FillHeightNone(Pages, HeightBase, HeightEntries);
                ZeroBuffer(Bricks, bricks * BRICK_UINTS);
                BytesAllocated = (long)dirEntries * 4 + ((long)pageUints + HeightEntries) * 4 + (long)bricks * BRICK_UINTS * 4;
            }
            public void Dispose()
            {
                Dir?.Release(); Dir = null;
                Pages?.Release(); Pages = null;
                Bricks?.Release(); Bricks = null;
            }
        }

        // Counters (main thread).
        public int BricksUsed { get; private set; }
        public int PagesUsed { get; private set; }
        public int PeakBricks { get; private set; }
        public int PeakPages { get; private set; }
        public int ChunksStored { get; private set; }
        public int UniformChunks { get; private set; }
        public long Refusals { get; private set; }
        /// Dense bricks of the refused chunks: with BricksUsed, the demand the cap did not meet.
        public long RefusedBricks { get; private set; }
        public long BytesUploaded { get; private set; }
        /// Task 99 (header SOLID TOP): int.MinValue until a chunk holding anything but air is stored.
        public int SolidTopY { get; private set; } = int.MinValue;

        private readonly int3 _mask;
        private readonly uint[] _dir;
        private readonly int3[] _tag;
        private readonly bool[] _tagged;
        private readonly int[][] _pageBricks;    // body indices per page (dense only)
        private readonly int[] _pageBrickCount;
        private readonly int[] _freeBricks; private int _freeBrickTop;
        private readonly int[] _freePages; private int _freePageTop;
        // Pages (with their bodies) a redraw replaced: freed at the next directory flush.
        private readonly List<int> _deferredPages = new List<int>();
        private int _deferredBricks;
        private bool _dirDirty;
        private readonly uint[] _pageStaging = new uint[PAGE_UINTS];
        private readonly uint[] _maskStaging = new uint[MASK_UINTS];
        private readonly uint[] _colTopStaging = new uint[COLTOP_UINTS];
        // Task 153 (header COLUMN HEIGHTS): per slot, ChunkSolidTopY of what _dir publishes there
        // (int.MinValue: air or nothing); the job's; the table and the box it describes.
        private readonly int[] _slotTop;
        private int _jobTop;
        private readonly uint[] _height;
        private readonly bool[] _heightTouched;
        private bool _heightBoxSet, _heightDirty;
        private int3 _hLo, _hHi, _hCc;
        private int _hHole;
        private NativeArray<uint> _bodyStaging;   // up to 4096 bodies (2 MB), reused
        private readonly int[] _denseIdx = new int[EngineConfig.BRICKS_PER_CHUNK];

        // Test shadows (ShadowUploads).
        public uint[] ShadowDir, ShadowPages, ShadowBricks, ShadowMasks, ShadowColTops, ShadowHeights;

        public ShellStore(int shell, int3 ring, int poolCap, int pageCap,
                          SharedBuffers shared = null, int dirBase = 0, int pageBase = 0, int brickBase = 0, int heightOffset = 0)
        {
            if (shell < 1 || shell > TerrainGenChunkSource.MAX_SHELL) throw new ArgumentOutOfRangeException(nameof(shell));
            if (!ShellLayout.IsPow2(ring.x) || !ShellLayout.IsPow2(ring.y) || !ShellLayout.IsPow2(ring.z))
                throw new ArgumentException($"shell ring {ring} must be powers of two (§6.2 phantom terrain)");
            Shell = shell; Ring = ring; _mask = ring - 1;
            SlotCount = ring.x * ring.y * ring.z;
            PoolCap = math.max(1, poolCap); PageCap = math.max(1, pageCap);

            _dir = new uint[SlotCount];
            _slotTop = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++) _slotTop[i] = int.MinValue;
            _height = new uint[ring.x * ring.z];
            for (int i = 0; i < _height.Length; i++) _height[i] = unchecked((uint)HEIGHT_NONE);
            _heightTouched = new bool[_height.Length];
            _tag = new int3[SlotCount];
            _tagged = new bool[SlotCount];
            _pageBricks = new int[PageCap][];
            _pageBrickCount = new int[PageCap];
            // Free stacks pop ascending indices first, so a fresh fill writes
            // consecutive bodies and the upload coalesces into few runs.
            _freeBricks = new int[PoolCap];
            for (int i = 0; i < PoolCap; i++) _freeBricks[i] = PoolCap - 1 - i;
            _freeBrickTop = PoolCap;
            _freePages = new int[PageCap];
            for (int i = 0; i < PageCap; i++) _freePages[i] = PageCap - 1 - i;
            _freePageTop = PageCap;
            _bodyStaging = new NativeArray<uint>(EngineConfig.BRICKS_PER_CHUNK * BRICK_UINTS, Allocator.Persistent);

            if (shared != null)
            {
                // The shared set was zeroed when it was made (SharedBuffers).
                DirectoryBuffer = shared.Dir; PageBuffer = shared.Pages; BrickBuffer = shared.Bricks;
                DirBase = dirBase; PageBase = pageBase; BrickBase = brickBase; MaskBase = shared.MaskBase; ColTopBase = shared.ColTopBase;
                HeightBase = shared.HeightBase; HeightOffset = heightOffset;
                if ((long)(dirBase + SlotCount) > DirectoryBuffer.count || (long)(pageBase + PageCap) * PAGE_UINTS > PageBuffer.count
                    || (long)(brickBase + PoolCap) * BRICK_UINTS > BrickBuffer.count || heightOffset + Columns > shared.HeightEntries)
                    throw new ArgumentException($"shell {shell}'s ranges do not fit the shared buffers");
            }
            else
            {
                // LockBufferForWrite usage, as TerrainClipmap / CascadeTierPool: partial
                // SetData into a device-private buffer renames it (78 live copies once,
                // CascadeTierPool.cs's vmmap note).
                _ownsBuffers = true;
                DirectoryBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, SlotCount, 4);
                PageBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, PageCap * (PAGE_UINTS + TAIL_UINTS) + Columns, 4);
                MaskBase = PageCap * PAGE_UINTS;
                ColTopBase = MaskBase + PageCap * MASK_UINTS;
                HeightBase = ColTopBase + PageCap * COLTOP_UINTS; HeightOffset = 0;
                BrickBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, PoolCap * BRICK_UINTS, 4);
                // Zero everything (row 3). Pages and bricks are only read through a
                // directory entry that was written after them, but a zeroed buffer
                // makes a stray read show as air, not garbage.
                DirectoryBuffer.SetData(_dir);
                ZeroBuffer(PageBuffer, PageCap * (PAGE_UINTS + TAIL_UINTS));
                FillHeightNone(PageBuffer, HeightBase, Columns);
                ZeroBuffer(BrickBuffer, PoolCap * BRICK_UINTS);
            }
            if (ShadowUploads)
            {
                ShadowDir = new uint[SlotCount];
                ShadowPages = new uint[PageCap * PAGE_UINTS];
                ShadowMasks = new uint[PageCap * MASK_UINTS];
                ShadowColTops = new uint[PageCap * COLTOP_UINTS];
                ShadowHeights = (uint[])_height.Clone();
                ShadowBricks = new uint[PoolCap * BRICK_UINTS];
            }
        }

        // Task 168e: the per-frame writes (not the constructor's fills: those buffers are new, nothing reads them yet).
        private static void Put(GraphicsBuffer b, string name, uint[] src, int srcStart, int dstStart, int count)
        {
            if (UploadShadow.Enabled) UploadShadow.Record(b, name, src, srcStart, dstStart, count);
            if (UploadRing.On) UploadRing.Shared.Write(b, src, srcStart, dstStart, count);
            else b.SetData(src, srcStart, dstStart, count);
        }

        private static void Put(GraphicsBuffer b, string name, NativeArray<uint> src, int srcStart, int dstStart, int count)
        {
            if (UploadShadow.Enabled) UploadShadow.Record(b, name, src, srcStart, dstStart, count);
            if (UploadRing.On) UploadRing.Shared.Write(b, src, srcStart, dstStart, count);
            else b.SetData(src, srcStart, dstStart, count);
        }

        private static void ZeroBuffer(GraphicsBuffer b, int count)
        {
            const int CHUNK = 1 << 20;
            var z = new NativeArray<uint>(math.min(count, CHUNK), Allocator.Temp);
            for (int at = 0; at < count; at += CHUNK) b.SetData(z, 0, at, math.min(CHUNK, count - at));
            z.Dispose();
        }

        /// Task 153: a height region starts as HEIGHT_NONE (never leap), not 0.
        private static void FillHeightNone(GraphicsBuffer b, int at, int count)
        {
            if (count <= 0) return;
            var n = new uint[count];
            for (int i = 0; i < count; i++) n[i] = unchecked((uint)HEIGHT_NONE);
            b.SetData(n, 0, at, count);
        }

        public long BytesAllocated => (long)SlotCount * 4 + ((long)PageCap * (PAGE_UINTS + TAIL_UINTS) + Columns) * 4 + (long)PoolCap * BRICK_UINTS * 4;

        /// Task 93b5: the brick group (0..63) of a local brick index ((bz << 8) | (by << 4) | bx).
        public static int GroupOf(int localBrick) =>
            (((localBrick >> 10) & 3) << 4) | (((localBrick >> 6) & 3) << 2) | ((localBrick >> 2) & 3);

        /// Task 93b5: the air-brick mask of a page of 4096 brick handles at `offset`:
        /// bit g set = group g holds a handle that is not uniform air (0). Shared by
        /// ShellStore (the upload) and the tests / CPU views (ShellRayReference).
        public static ulong GroupMask(uint[] handles, int offset = 0)
        {
            ulong m = 0ul;
            for (int lb = 0; lb < PAGE_UINTS; lb++)
                if (handles[offset + lb] != 0u) m |= 1ul << GroupOf(lb);
            return m;
        }

        /// Task 116: the column tops of a page of 4096 brick handles at `offset` (header: COLUMN
        /// TOPS) into `tops` (COLTOP_UINTS uints, byte j of uint j >> 2 at bits (j & 3) * 8):
        /// group column j = (gz << 2) | gx holds a handle that is not uniform air (0) at local
        /// brick y top - 1 and none above; 0 = all air. Shared by Store and the tests / CPU views.
        public static void ColumnTops(uint[] handles, uint[] tops, int offset = 0)
        {
            Array.Clear(tops, 0, COLTOP_UINTS);
            for (int lb = 0; lb < PAGE_UINTS; lb++)
            {
                if (handles[offset + lb] == 0u) continue;
                int j = (((lb >> 10) & 3) << 2) | ((lb >> 2) & 3);
                uint top = (uint)((lb >> 4) & 15) + 1u;
                int sh = (j & 3) * 8;
                if (((tops[j >> 2] >> sh) & 0xFFu) < top) tops[j >> 2] = (tops[j >> 2] & ~(0xFFu << sh)) | (top << sh);
            }
        }

        /// Task 116: byte j of a column-top table (ColumnTops).
        public static int ColumnTop(uint[] tops, int j, int offset = 0) => (int)((tops[offset + (j >> 2)] >> ((j & 3) * 8)) & 0xFFu);

        /// Ring slot of a shell chunk: & masks only (pow2 ring, §6.2).
        public int Slot(int3 c) => (((c.z & _mask.z) * Ring.y) + (c.y & _mask.y)) * Ring.x + (c.x & _mask.x);

        public bool Holds(int3 c) { int s = Slot(c); return _tagged[s] && _tag[s].Equals(c); }
        public bool SlotTagged(int slot, out int3 tag) { tag = _tag[slot]; return _tagged[slot]; }
        public uint DirectoryEntry(int slot) => _dir[slot];
        public int PageBrickCount(int page) => _pageBrickCount[page];
        public int FreeBricks => _freeBrickTop;
        public int FreePages => _freePageTop;
        /// Replaced by a redraw, still referenced by the GPU's directory until the next flush.
        public int DeferredBricks => _deferredBricks;
        public int DeferredPages => _deferredPages.Count;
        /// Redraws stored (task 82), and redraws that found no room for the new
        /// copy beside the old one (the caller evicts and stores it fresh).
        public long Replaced { get; private set; }
        public long ReplaceNoRoom { get; private set; }

        /// Upload cost of a chunk, in bytes, before it is stored.
        public static int UploadBytes(TerrainGenChunkSource.ShellChunk sc, out int dense, out bool uniform, out byte uniformMat)
        {
            dense = 0; uniform = true; uniformMat = Materials.Air;
            if (sc.IsUniformAir) return 4;
            uniformMat = sc.BrickMat[0];
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
            {
                if (!sc.BrickUniform[i]) { dense++; uniform = false; }
                else if (sc.BrickMat[i] != uniformMat) uniform = false;
            }
            return uniform ? 4 : 4 + (PAGE_UINTS + TAIL_UINTS) * 4 + dense * BRICK_UINTS * 4;
        }

        public enum StoreResult { Stored, InProgress, OverBudget, Busy, Refused }

        /// Task 99: the top (exclusive, 0.1 m voxels) of shell `shell`'s chunk sc's highest brick that
        /// is not uniform air; int.MinValue = all air. Brick (bz << 8) | (by << 4) | bx of
        /// chunk c spans voxel y [((c.y << 4) + by) << (k + 3), + (1 << (k + 3))).
        public static int ChunkSolidTopY(TerrainGenChunkSource.ShellChunk sc, int shell)
        {
            if (sc.IsUniformAir) return int.MinValue;
            for (int by = 15; by >= 0; by--)
                for (int bz = 0; bz < 16; bz++)
                    for (int bx = 0; bx < 16; bx++)
                    {
                        int bi = (bz << 8) | (by << 4) | bx;
                        if (!sc.BrickUniform[bi] || sc.BrickMat[bi] != Materials.Air)
                            return ((sc.Coord.y << 4) + by + 1) << (shell + 3);
                    }
            return int.MinValue;
        }

        private void RaiseSolidTop(int top) => SolidTopY = math.max(SolidTopY, top);

        /// Task 153: what _dir[slot] now publishes has this ChunkSolidTopY (the next flush rebuilds the table).
        private void SetSlotTop(int slot, int top)
        {
            _slotTop[slot] = top;
            if (_heightBoxSet) _heightDirty = true;   // also when only held-ness changed
        }

        /// Task 153: the box (inclusive, chunks), camera chunk and hole the NEXT successful
        /// FlushDirectory publishes (ShellStreamer.Publish); the table is rebuilt for it then.
        /// Until it is first called the table stays HEIGHT_NONE (tests that never set it).
        public void SetHeightBox(int3 lo, int3 hi, int3 cc, int hole)
        {
            if (_heightBoxSet && lo.Equals(_hLo) && hi.Equals(_hHi) && cc.Equals(_hCc) && hole == _hHole) return;
            _hLo = lo; _hHi = hi; _hCc = cc; _hHole = hole;
            _heightBoxSet = true; _heightDirty = true;
        }

        /// Task 153: ring column of world chunk column (cx, cz) in the height table.
        public int HeightColumn(int cx, int cz) => ((cz & _mask.z) * Ring.x) + (cx & _mask.x);
        /// Task 153: the CPU table (what the last flush uploaded once built).
        public int Height(int ringColumn) => (int)_height[ringColumn];
        public bool HeightDirty => _heightDirty;

        /// Task 153 (header COLUMN HEIGHTS): rebuild the table from _dir for the box SetHeightBox gave.
        /// A ring column two box columns share (a box wider than the ring) keeps the max of both.
        private void BuildHeights()
        {
            Array.Clear(_heightTouched, 0, _heightTouched.Length);
            int cs = Shell + 7;
            for (int cz = _hLo.z; cz <= _hHi.z; cz++)
            for (int cx = _hLo.x; cx <= _hHi.x; cx++)
            {
                bool holeXZ = _hHole >= 0 && math.abs(cx - _hCc.x) <= _hHole && math.abs(cz - _hCc.z) <= _hHole;
                int top = int.MinValue;
                for (int cy = _hLo.y; cy <= _hHi.y; cy++)
                {
                    if (holeXZ && math.abs(cy - _hCc.y) <= _hHole) continue;
                    var c = new int3(cx, cy, cz);
                    int s = Slot(c);
                    top = math.max(top, _dir[s] != ABSENT && _tagged[s] && _tag[s].Equals(c) ? _slotTop[s] : (cy + 1) << cs);
                }
                int j = HeightColumn(cx, cz);
                _height[j] = (uint)(_heightTouched[j] ? math.max((int)_height[j], top) : top);
                _heightTouched[j] = true;
            }
            for (int j = 0; j < _height.Length; j++) if (!_heightTouched[j]) _height[j] = unchecked((uint)HEIGHT_NONE);
        }

        // ONE chunk upload in progress (row 14): a dense chunk is up to 2 MB of
        // bodies + a 16 KB page, more than the frame's remainder of the 3 MiB
        // cap may hold. Its bodies and page go up over as many frames as the
        // budget needs; the directory entry is published only when all of it
        // is on the GPU, so the slot reads ABSENT (the coarser shell, row 5)
        // until then -- never a half-written chunk.
        private bool _job;
        private int3 _jobCoord;
        private int _jobSlot, _jobPage, _jobN, _jobDone;
        private bool _jobPageUp, _jobReplace;
        public bool JobActive => _job;
        public int3 JobCoord => _jobCoord;

        /// Store `sc` (shell `Shell`, chunk sc.Coord) into its ring slot, evicting
        /// whatever the slot held. Uniform chunks finish at once; others reserve
        /// a page and bodies, stage them (`sc` may be reused as soon as this
        /// returns) and upload what `budget` allows (InProgress = call Continue
        /// on later frames). OverBudget / Busy / Refused leave everything untouched.
        /// `replace` (a redraw, task 82): the slot already holds sc.Coord and keeps
        /// drawing it until the new copy is complete (header: REDRAW); with no room
        /// for both copies it returns Refused and counts ReplaceNoRoom, not Refusals.
        public StoreResult Store(TerrainGenChunkSource.ShellChunk sc, ref int budget, bool replace = false)
        {
            if (_job) return StoreResult.Busy;
            UploadBytes(sc, out int dense, out bool uniform, out byte umat);
            if (budget < (uniform ? 4 : 512)) return StoreResult.OverBudget;
            int slot = Slot(sc.Coord);
            bool keepOld = replace && _tagged[slot] && _tag[slot].Equals(sc.Coord);
            int top = ChunkSolidTopY(sc, Shell);   // tasks 99, 153
            if (keepOld)
            {
                if (!uniform && (_freePageTop == 0 || _freeBrickTop < dense)) { ReplaceNoRoom++; return StoreResult.Refused; }
                Replaced++;
                RaiseSolidTop(top);   // task 99
                if (uniform)
                {
                    DeferSlot(slot);
                    _dir[slot] = UNIFORM | umat;
                    SetSlotTop(slot, top);   // task 153
                    UniformChunks++; ChunksStored++; _dirDirty = true;
                    budget -= 4; BytesUploaded += 4;
                    return StoreResult.Stored;
                }
            }
            else if (!uniform)
            {
                // Room after freeing this slot's own occupant (a re-store).
                int pagesFreeAfter = _freePageTop, bricksFreeAfter = _freeBrickTop;
                if (_tagged[slot] && (_dir[slot] & PAGE) != 0)
                {
                    pagesFreeAfter++; bricksFreeAfter += _pageBrickCount[_dir[slot] & ~PAGE];
                }
                if (pagesFreeAfter == 0 || bricksFreeAfter < dense) { Refusals++; RefusedBricks += dense; return StoreResult.Refused; }
            }
            if (!keepOld) { RaiseSolidTop(top); EvictSlot(slot); }   // task 99: before anything goes up

            if (uniform)
            {
                _dir[slot] = UNIFORM | umat;
                SetSlotTop(slot, top);   // task 153
                _tag[slot] = sc.Coord; _tagged[slot] = true;
                UniformChunks++; ChunksStored++; _dirDirty = true;
                budget -= 4; BytesUploaded += 4;
                return StoreResult.Stored;
            }
            int page = _freePages[--_freePageTop];
            var list = _pageBricks[page];
            if (list == null || list.Length < dense) _pageBricks[page] = list = new int[math.max(dense, 64)];
            int n = 0;
            for (int bi = 0; bi < EngineConfig.BRICKS_PER_CHUNK; bi++)
            {
                if (sc.BrickUniform[bi]) { _pageStaging[bi] = sc.BrickMat[bi]; continue; }
                int body = _freeBricks[--_freeBrickTop];
                list[n] = body; _denseIdx[n] = bi; n++;
                _pageStaging[bi] = DENSE | (uint)body;
            }
            _pageBrickCount[page] = n;
            ulong mask = GroupMask(_pageStaging);
            _maskStaging[0] = (uint)mask; _maskStaging[1] = (uint)(mask >> 32);
            ColumnTops(_pageStaging, _colTopStaging);   // task 116
            StageBodies(sc, n);
            BricksUsed += n; PagesUsed++;
            PeakBricks = math.max(PeakBricks, BricksUsed); PeakPages = math.max(PeakPages, PagesUsed);
            _job = true; _jobCoord = sc.Coord; _jobSlot = slot; _jobPage = page; _jobN = n; _jobDone = 0; _jobPageUp = false;
            _jobReplace = keepOld;
            _jobTop = top;   // task 153: the slot's top once the entry flips (Continue)
            return Continue(ref budget) ? StoreResult.Stored : StoreResult.InProgress;
        }

        /// Upload more of the chunk in progress; true when it is complete (its
        /// directory entry set, to go up with FlushDirectory).
        public bool Continue(ref int budget)
        {
            if (!_job) return true;
            var list = _pageBricks[_jobPage];
            while (_jobDone < _jobN && budget >= BRICK_UINTS * 4)
            {
                // The run of consecutive body indices from _jobDone, cut to the budget.
                int end = _jobDone + 1;
                while (end < _jobN && list[end] == list[end - 1] + 1) end++;
                end = math.min(end, _jobDone + budget / (BRICK_UINTS * 4));
                int count = (end - _jobDone) * BRICK_UINTS;
                Put(BrickBuffer, "shell bricks", _bodyStaging, _jobDone * BRICK_UINTS, (BrickBase + list[_jobDone]) * BRICK_UINTS, count);
                if (ShadowUploads)
                    NativeArray<uint>.Copy(_bodyStaging, _jobDone * BRICK_UINTS, ShadowBricks, list[_jobDone] * BRICK_UINTS, count);
                budget -= count * 4; BytesUploaded += count * 4;
                _jobDone = end;
            }
            if (_jobDone < _jobN) return false;
            if (!_jobPageUp)
            {
                if (budget < (PAGE_UINTS + TAIL_UINTS) * 4) return false;
                Put(PageBuffer, "shell pages", _pageStaging, 0, (PageBase + _jobPage) * PAGE_UINTS, PAGE_UINTS);
                // Task 93b5: the page's air-brick mask, with the page (before its entry).
                Put(PageBuffer, "shell pages", _maskStaging, 0, MaskBase + (PageBase + _jobPage) * MASK_UINTS, MASK_UINTS);
                // Task 116: and its column tops (also before its entry).
                Put(PageBuffer, "shell pages", _colTopStaging, 0, ColTopBase + (PageBase + _jobPage) * COLTOP_UINTS, COLTOP_UINTS);
                if (ShadowUploads)
                {
                    Array.Copy(_pageStaging, 0, ShadowPages, _jobPage * PAGE_UINTS, PAGE_UINTS);
                    Array.Copy(_maskStaging, 0, ShadowMasks, _jobPage * MASK_UINTS, MASK_UINTS);
                    Array.Copy(_colTopStaging, 0, ShadowColTops, _jobPage * COLTOP_UINTS, COLTOP_UINTS);
                }
                budget -= (PAGE_UINTS + TAIL_UINTS) * 4; BytesUploaded += (PAGE_UINTS + TAIL_UINTS) * 4;
                _jobPageUp = true;
            }
            // A redraw whose old copy is still there swaps it out now (deferred free).
            if (_jobReplace && _tagged[_jobSlot] && _tag[_jobSlot].Equals(_jobCoord)) DeferSlot(_jobSlot);
            _dir[_jobSlot] = PAGE | (uint)_jobPage;
            SetSlotTop(_jobSlot, _jobTop);   // task 153
            _tag[_jobSlot] = _jobCoord; _tagged[_jobSlot] = true;
            ChunksStored++; _dirDirty = true;
            _job = false;
            return true;
        }

        /// Drop the chunk in progress (it left the stored set): its page and
        /// bodies go back to the pools; the slot stays ABSENT.
        public void AbortJob()
        {
            if (!_job) return;
            var list = _pageBricks[_jobPage];
            for (int i = 0; i < _jobN; i++) _freeBricks[_freeBrickTop++] = list[i];
            BricksUsed -= _jobN; _pageBrickCount[_jobPage] = 0;
            _freePages[_freePageTop++] = _jobPage; PagesUsed--;
            _job = false;
        }

        /// Copy the dense bricks' voxels into the staging array in body order
        /// ((vz<<6)|(vy<<3)|vx, BrickDataPool's layout).
        private unsafe void StageBodies(TerrainGenChunkSource.ShellChunk sc, int n)
        {
            byte* mats = (byte*)sc.Mats.GetUnsafeReadOnlyPtr();
            byte* dst = (byte*)_bodyStaging.GetUnsafePtr();
            for (int i = 0; i < n; i++)
            {
                int bi = _denseIdx[i];
                int bx = bi & 15, by = (bi >> 4) & 15, bz = bi >> 8;
                byte* o = dst + i * 512;
                for (int vz = 0; vz < 8; vz++)
                for (int vy = 0; vy < 8; vy++)
                {
                    byte* row = mats + TerrainGenChunkSource.Index(bx << 3, (by << 3) + vy, (bz << 3) + vz);
                    UnsafeUtility.MemCpy(o + ((vz << 6) | (vy << 3)), row, 8);
                }
            }
        }

        /// A redraw's old copy: its counters go now, its page and bodies only at the
        /// next successful FlushDirectory (the GPU directory still points at them).
        /// The slot stays tagged; the caller writes the new entry.
        private void DeferSlot(int slot)
        {
            uint e = _dir[slot];
            if ((e & PAGE) != 0)
            {
                int page = (int)(e & ~PAGE);
                _deferredPages.Add(page);
                _deferredBricks += _pageBrickCount[page];
                BricksUsed -= _pageBrickCount[page];
                PagesUsed--;
            }
            else if ((e & UNIFORM) != 0) UniformChunks--;
            ChunksStored--;
        }

        private void ReleaseDeferred()
        {
            foreach (int page in _deferredPages)
            {
                var list = _pageBricks[page];
                for (int i = 0; i < _pageBrickCount[page]; i++) _freeBricks[_freeBrickTop++] = list[i];
                _pageBrickCount[page] = 0;
                _freePages[_freePageTop++] = page;
            }
            _deferredPages.Clear();
            _deferredBricks = 0;
        }

        /// Free whatever chunk the slot holds (bricks and page back to the pools).
        public void EvictSlot(int slot)
        {
            if (!_tagged[slot]) return;
            uint e = _dir[slot];
            if ((e & PAGE) != 0)
            {
                int page = (int)(e & ~PAGE);
                var list = _pageBricks[page];
                for (int i = 0; i < _pageBrickCount[page]; i++) _freeBricks[_freeBrickTop++] = list[i];
                BricksUsed -= _pageBrickCount[page];
                _pageBrickCount[page] = 0;
                _freePages[_freePageTop++] = page;
                PagesUsed--;
            }
            else if ((e & UNIFORM) != 0) UniformChunks--;
            _dir[slot] = ABSENT; _tagged[slot] = false;
            SetSlotTop(slot, int.MinValue);   // task 153 (an absent chunk counts as solid in the table)
            ChunksStored--;
            _dirDirty = true;
        }

        public void Evict(int3 c) { int s = Slot(c); if (_tagged[s] && _tag[s].Equals(c)) EvictSlot(s); }

        /// Upload the whole directory if it changed (16 KB on High; Ultra's
        /// 32^3 ring 128 KB), and (task 153) the column heights rebuilt from it for
        /// the box SetHeightBox gave, in the same call: one budget check for both,
        /// so the GPU never has one without the other. Returns false (and writes
        /// nothing) over budget.
        public bool FlushDirectory(ref int budget)
        {
            bool hd = _heightDirty;
            if (!_dirDirty && !hd) { if (_deferredPages.Count > 0) ReleaseDeferred(); return true; }
            int bytes = (_dirDirty ? SlotCount * 4 : 0) + (hd ? Columns * 4 : 0);
            if (bytes > budget) return false;
            if (_dirDirty)
            {
                Put(DirectoryBuffer, "shell dir", _dir, 0, DirBase, SlotCount);
                if (ShadowUploads) Array.Copy(_dir, ShadowDir, SlotCount);
                _dirDirty = false;
            }
            if (hd)
            {
                BuildHeights();
                Put(PageBuffer, "shell pages", _height, 0, HeightBase + HeightOffset, Columns);
                if (ShadowUploads) Array.Copy(_height, ShadowHeights, Columns);
                _heightDirty = false;
            }
            budget -= bytes; BytesUploaded += bytes;
            if (_deferredPages.Count > 0) ReleaseDeferred();   // the GPU no longer points at them
            return true;
        }
        public bool DirectoryDirty => _dirDirty || _heightDirty;

        public void Dispose()
        {
            if (_ownsBuffers)
            {
                DirectoryBuffer?.Release();
                PageBuffer?.Release();
                BrickBuffer?.Release();
            }
            DirectoryBuffer = null; PageBuffer = null; BrickBuffer = null;
            if (_bodyStaging.IsCreated) _bodyStaging.Dispose();
        }
    }
}
