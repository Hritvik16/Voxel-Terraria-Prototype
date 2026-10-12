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

        private readonly int3 _mask;
        private readonly uint[] _dir;
        private readonly int3[] _tag;
        private readonly bool[] _tagged;
        private readonly int[][] _pageBricks;    // body indices per page (dense only)
        private readonly int[] _pageBrickCount;
        private readonly int[] _freeBricks; private int _freeBrickTop;
        private readonly int[] _freePages; private int _freePageTop;
        private bool _dirDirty;
        private readonly uint[] _pageStaging = new uint[PAGE_UINTS];
        private NativeArray<uint> _bodyStaging;   // up to 4096 bodies (2 MB), reused
        private readonly int[] _denseIdx = new int[EngineConfig.BRICKS_PER_CHUNK];

        // Test shadows (ShadowUploads).
        public uint[] ShadowDir, ShadowPages, ShadowBricks;

        public ShellStore(int shell, int3 ring, int poolCap, int pageCap)
        {
            if (shell < 1 || shell > TerrainGenChunkSource.MAX_SHELL) throw new ArgumentOutOfRangeException(nameof(shell));
            if (!ShellLayout.IsPow2(ring.x) || !ShellLayout.IsPow2(ring.y) || !ShellLayout.IsPow2(ring.z))
                throw new ArgumentException($"shell ring {ring} must be powers of two (§6.2 phantom terrain)");
            Shell = shell; Ring = ring; _mask = ring - 1;
            SlotCount = ring.x * ring.y * ring.z;
            PoolCap = math.max(1, poolCap); PageCap = math.max(1, pageCap);

            _dir = new uint[SlotCount];
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

            // LockBufferForWrite usage, as TerrainClipmap / CascadeTierPool: partial
            // SetData into a device-private buffer renames it (78 live copies once,
            // CascadeTierPool.cs's vmmap note).
            DirectoryBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, SlotCount, 4);
            PageBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, PageCap * PAGE_UINTS, 4);
            BrickBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, PoolCap * BRICK_UINTS, 4);
            // Zero everything (row 3). Pages and bricks are only read through a
            // directory entry that was written after them, but a zeroed buffer
            // makes a stray read show as air, not garbage.
            DirectoryBuffer.SetData(_dir);
            ZeroBuffer(PageBuffer, PageCap * PAGE_UINTS);
            ZeroBuffer(BrickBuffer, PoolCap * BRICK_UINTS);
            if (ShadowUploads)
            {
                ShadowDir = new uint[SlotCount];
                ShadowPages = new uint[PageCap * PAGE_UINTS];
                ShadowBricks = new uint[PoolCap * BRICK_UINTS];
            }
        }

        private static void ZeroBuffer(GraphicsBuffer b, int count)
        {
            const int CHUNK = 1 << 20;
            var z = new NativeArray<uint>(math.min(count, CHUNK), Allocator.Temp);
            for (int at = 0; at < count; at += CHUNK) b.SetData(z, 0, at, math.min(CHUNK, count - at));
            z.Dispose();
        }

        public long BytesAllocated => (long)SlotCount * 4 + (long)PageCap * PAGE_UINTS * 4 + (long)PoolCap * BRICK_UINTS * 4;

        /// Ring slot of a shell chunk: & masks only (pow2 ring, §6.2).
        public int Slot(int3 c) => (((c.z & _mask.z) * Ring.y) + (c.y & _mask.y)) * Ring.x + (c.x & _mask.x);

        public bool Holds(int3 c) { int s = Slot(c); return _tagged[s] && _tag[s].Equals(c); }
        public bool SlotTagged(int slot, out int3 tag) { tag = _tag[slot]; return _tagged[slot]; }
        public uint DirectoryEntry(int slot) => _dir[slot];
        public int PageBrickCount(int page) => _pageBrickCount[page];
        public int FreeBricks => _freeBrickTop;
        public int FreePages => _freePageTop;

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
            return uniform ? 4 : 4 + PAGE_UINTS * 4 + dense * BRICK_UINTS * 4;
        }

        public enum StoreResult { Stored, InProgress, OverBudget, Busy, Refused }

        // ONE chunk upload in progress (row 14): a dense chunk is up to 2 MB of
        // bodies + a 16 KB page, more than the frame's remainder of the 3 MiB
        // cap may hold. Its bodies and page go up over as many frames as the
        // budget needs; the directory entry is published only when all of it
        // is on the GPU, so the slot reads ABSENT (the coarser shell, row 5)
        // until then -- never a half-written chunk.
        private bool _job;
        private int3 _jobCoord;
        private int _jobSlot, _jobPage, _jobN, _jobDone;
        private bool _jobPageUp;
        public bool JobActive => _job;
        public int3 JobCoord => _jobCoord;

        /// Store `sc` (shell `Shell`, chunk sc.Coord) into its ring slot, evicting
        /// whatever the slot held. Uniform chunks finish at once; others reserve
        /// a page and bodies, stage them (`sc` may be reused as soon as this
        /// returns) and upload what `budget` allows (InProgress = call Continue
        /// on later frames). OverBudget / Busy / Refused leave everything untouched.
        public StoreResult Store(TerrainGenChunkSource.ShellChunk sc, ref int budget)
        {
            if (_job) return StoreResult.Busy;
            UploadBytes(sc, out int dense, out bool uniform, out byte umat);
            if (budget < (uniform ? 4 : 512)) return StoreResult.OverBudget;
            int slot = Slot(sc.Coord);
            if (!uniform)
            {
                // Room after freeing this slot's own occupant (a re-store).
                int pagesFreeAfter = _freePageTop, bricksFreeAfter = _freeBrickTop;
                if (_tagged[slot] && (_dir[slot] & PAGE) != 0)
                {
                    pagesFreeAfter++; bricksFreeAfter += _pageBrickCount[_dir[slot] & ~PAGE];
                }
                if (pagesFreeAfter == 0 || bricksFreeAfter < dense) { Refusals++; RefusedBricks += dense; return StoreResult.Refused; }
            }
            EvictSlot(slot);

            if (uniform)
            {
                _dir[slot] = UNIFORM | umat;
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
            StageBodies(sc, n);
            BricksUsed += n; PagesUsed++;
            PeakBricks = math.max(PeakBricks, BricksUsed); PeakPages = math.max(PeakPages, PagesUsed);
            _job = true; _jobCoord = sc.Coord; _jobSlot = slot; _jobPage = page; _jobN = n; _jobDone = 0; _jobPageUp = false;
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
                BrickBuffer.SetData(_bodyStaging, _jobDone * BRICK_UINTS, list[_jobDone] * BRICK_UINTS, count);
                if (ShadowUploads)
                    NativeArray<uint>.Copy(_bodyStaging, _jobDone * BRICK_UINTS, ShadowBricks, list[_jobDone] * BRICK_UINTS, count);
                budget -= count * 4; BytesUploaded += count * 4;
                _jobDone = end;
            }
            if (_jobDone < _jobN) return false;
            if (!_jobPageUp)
            {
                if (budget < PAGE_UINTS * 4) return false;
                PageBuffer.SetData(_pageStaging, 0, _jobPage * PAGE_UINTS, PAGE_UINTS);
                if (ShadowUploads) Array.Copy(_pageStaging, 0, ShadowPages, _jobPage * PAGE_UINTS, PAGE_UINTS);
                budget -= PAGE_UINTS * 4; BytesUploaded += PAGE_UINTS * 4;
                _jobPageUp = true;
            }
            _dir[_jobSlot] = PAGE | (uint)_jobPage;
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
            ChunksStored--;
            _dirDirty = true;
        }

        public void Evict(int3 c) { int s = Slot(c); if (_tagged[s] && _tag[s].Equals(c)) EvictSlot(s); }

        /// Upload the whole directory if it changed (16 KB on High; Ultra's
        /// 32^3 ring 128 KB). Returns false (and writes nothing) over budget.
        public bool FlushDirectory(ref int budget)
        {
            if (!_dirDirty) return true;
            int bytes = SlotCount * 4;
            if (bytes > budget) return false;
            DirectoryBuffer.SetData(_dir);
            if (ShadowUploads) Array.Copy(_dir, ShadowDir, SlotCount);
            _dirDirty = false;
            budget -= bytes; BytesUploaded += bytes;
            return true;
        }
        public bool DirectoryDirty => _dirDirty;

        public void Dispose()
        {
            DirectoryBuffer?.Release(); DirectoryBuffer = null;
            PageBuffer?.Release(); PageBuffer = null;
            BrickBuffer?.Release(); BrickBuffer = null;
            if (_bodyStaging.IsCreated) _bodyStaging.Dispose();
        }
    }
}
