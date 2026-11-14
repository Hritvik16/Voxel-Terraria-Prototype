// Assets/CoreEngine/Mirror/TerrainClipmap.Sealed.cs
//
// TASK 201a (owner 2026-11-14: the GPU body pool was FULL at every pose since 195a;
// no bigger pool). SEALED BRICKS take no GPU body.
//
// A tier-0 brick is SEALED when every voxel in it AND every voxel of the 1-voxel
// shell around it (the 10^3 box minus the brick) is SEAL-SOLID: not air, no fluid,
// no glass, no river lip, nothing emissive. The task-201a probe found 26-37 % of the
// pool's bodies are such bricks (buried rock, ore lumps, strata seams).
//
// WHY NO GPU READER CAN TELL (exactness argument, Raymarch.compute / RaymarchStripped /
// FluidCA): every tier-0 march passes air or a clear fluid (passMat 0 or the eye's
// fluid, 66b) and stops at the FIRST voxel whose material differs; it steps face to
// face, so to reach a sealed voxel it must first step onto a shell voxel, which is
// seal-solid and stops it. Every neighbour read (AO corners, emissive bleed, the
// river-lip check, normals) is of voxels touching an air / fluid voxel, which a sealed
// voxel never is (all 26 of its neighbours are seal-solid). Shadow / torch / AO /
// height-pyramid reads test only "non-zero"; a sealed brick is non-zero in every voxel
// either way. So the GPU gets a UNIFORM handle of one of the brick's own materials
// (voxel 0) in place of the dense one, and no body. Only an eye INSIDE rock (camera
// in solid, which collision forbids) could see the difference.
//
// INNER BRICKS ONLY (brick coords 1..14 on every axis): the shell then lies inside the
// same chunk, so sealing depends on that chunk alone -- a refused or evicted
// neighbour (air on the GPU) cannot expose it, and every write of the chunk (any
// path: SetVoxel, ops, deltas, coalesce) must re-dirty it for its upload anyway, which
// recomputes the mask from the CPU state. Nothing is cached across uploads.
//
// The CPU side is untouched: _clipmapLocal keeps CPU handles (air mips, fluid twin),
// the fluid terrain directory uploads every dense body as before (its CA reads them).
// Pool off (rigs, `-gpupool 0`) or `-gpuseal 0`: nothing here runs -- the old upload,
// byte for byte.
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using VoxelEngine.Memory;

public partial class TerrainClipmap
{
    /// `-gpuseal 0` turns it off (set before the first upload; Phase4Bootstrapper).
    public static bool SealHiddenBricks = true;

    /// Sealed bricks now staged uniform (no GPU body), summed over box slots.
    public int SealedBricksNow { get; private set; }
    /// Sealed bricks staged uniform over the run (counted per chunk upload).
    public long SealedBricksStagedTotal { get; private set; }

    private ulong[][] _sealedOfSlot;     // per box chunk slot: 64 words = 4096 bits (LocalBrickIndex), null = none
    private int[] _sealedCountOfSlot;
    private readonly ulong[] _sealScratch = new ulong[64];
    private static byte[] s_sealSolid;   // 256-entry table: 1 = seal-solid material

    /// Seal-solid = a voxel every tier-0 ray stops at whose material nothing samples
    /// unless it touches air or fluid: not air, fluid (6, 11, 12, 18-22), torch (14-17),
    /// glass (24) or river lip (TG2 + RiverLip = 136); see the header.
    public static bool IsSealSolid(byte m)
        => m != 0 && m != 6 && m != 11 && m != 12 && !(m >= 14 && m <= 22) && m != 24 && m != 136;

    private static byte[] SealTable()
    {
        if (s_sealSolid != null) return s_sealSolid;
        var t = new byte[256];
        for (int i = 0; i < 256; i++) t[i] = IsSealSolid((byte)i) ? (byte)1 : (byte)0;
        return s_sealSolid = t;
    }

    private bool SealActive => SealHiddenBricks && _gpuOfCpu != null;

    /// Computes the chunk's sealed mask into _sealScratch; returns how many bricks are sealed.
    private unsafe int ComputeSealed(Chunk chunk, BrickDataPool cpuPool)
    {
        for (int i = 0; i < 64; i++) _sealScratch[i] = 0;
        if (chunk == null || chunk.isUniform || chunk.bricks == null) return 0;
        byte[] table = SealTable();
        byte* raw = (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(cpuPool.RawData);
        fixed (BrickHandle* h = chunk.bricks)
        fixed (byte* t = table)
        fixed (ulong* outMask = _sealScratch)
            return SealedBricks.Mask((uint*)h, raw, t, outMask);
    }

    /// Records the slot's sealed mask (copied from _sealScratch; count 0 = none).
    private void SetSlotSealed(int slot, int count)
    {
        if (_sealedOfSlot == null) { _sealedOfSlot = new ulong[_chunkSlotCount][]; _sealedCountOfSlot = new int[_chunkSlotCount]; }
        SealedBricksNow += count - _sealedCountOfSlot[slot];
        _sealedCountOfSlot[slot] = count;
        if (count == 0) { if (_sealedOfSlot[slot] != null) System.Array.Clear(_sealedOfSlot[slot], 0, 64); return; }
        ulong[] m = _sealedOfSlot[slot] ??= new ulong[64];
        System.Array.Copy(_sealScratch, m, 64);
        SealedBricksStagedTotal += count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MaskBit(ulong[] m, int local) => m != null && (m[local >> 6] & (1UL << (local & 63))) != 0;

    /// The uniform handle a sealed dense brick is staged as: its voxel 0's material.
    private static uint SealedUniformHandle(uint cpuHandle, BrickDataPool cpuPool)
        => cpuPool.RawData[(int)(cpuHandle & 0x3FFFFFFFu) * 512];

    private BrickDataPool _sealPool;     // the CPU pool the last upload read (validators' expected handles)

    /// What the GPU clipmap should hold for this box brick: the sealed uniform handle when the
    /// last upload of its slot sealed it, else GpuHandleOf. Validators / diagnostics.
    public uint ExpectedGpuHandle(int3 chunkCoord, int3 localBrick, uint cpuHandle)
    {
        if (_sealedOfSlot != null && (cpuHandle & 0x80000000u) != 0 && _sealPool != null
            && MaskBit(_sealedOfSlot[ChunkSlot(chunkCoord)], CoordMath.LocalBrickIndex(localBrick)))
            return SealedUniformHandle(cpuHandle, _sealPool);
        return GpuHandleOf(cpuHandle);
    }

    /// Capture diagnostic (task 201a's `_gs0` survey arm): re-dirties every resident in-box chunk
    /// so the next uploads restage them under the current SealHiddenBricks. Returns how many.
    public int DebugRedirtyBox(ChunkStore store)
    {
        int n = 0;
        for (int z = 0; z < _windowDimsChunks.z; z++)
        for (int y = 0; y < _windowDimsChunks.y; y++)
        for (int x = 0; x < _windowDimsChunks.x; x++)
        {
            int3 c = _windowOrigin + new int3(x, y, z);
            if (store.GetChunk(c) == null) continue;
            MarkDirty(c); n++;
        }
        return n;
    }

    /// True if the last upload of this box brick staged it sealed (tests / probe).
    public bool IsSealedBrick(int3 chunkCoord, int3 localBrick)
        => _sealedOfSlot != null && MaskBit(_sealedOfSlot[ChunkSlot(chunkCoord)], CoordMath.LocalBrickIndex(localBrick));
}

/// Task 201a: the sealed-brick scan, Burst-compiled (direct call; ~0.1 ms per dense chunk).
[BurstCompile]
public static class SealedBricks
{
    /// Fills outMask (64 words) with the sealed inner bricks of one chunk; returns the count.
    /// handles = the chunk's 4096 BrickHandles (LocalBrickIndex order), raw = the CPU pool bytes.
    [BurstCompile]
    public static unsafe int Mask(uint* handles, byte* raw, byte* solid, ulong* outMask)
    {
        // Per brick: 0 = not all solid, 1 = all solid (uniform solid or a dense all-solid body).
        byte* allSolid = stackalloc byte[4096];
        for (int b = 0; b < 4096; b++)
        {
            uint hd = handles[b];
            if ((hd & 0x80000000u) == 0) { allSolid[b] = solid[hd & 0xFFu]; continue; }
            byte* body = raw + (long)(hd & 0x3FFFFFFFu) * 512;
            byte ok = 1;
            for (int v = 0; v < 512; v++) if (solid[body[v]] == 0) { ok = 0; break; }
            allSolid[b] = ok;
        }
        int count = 0;
        for (int bz = 1; bz < 15; bz++)
        for (int by = 1; by < 15; by++)
        for (int bx = 1; bx < 15; bx++)
        {
            int b = (bz << 8) | (by << 4) | bx;
            if ((handles[b] & 0x80000000u) == 0 || allSolid[b] == 0) continue;   // only dense all-solid bricks
            bool sealedB = true;
            for (int dz = -1; dz <= 1 && sealedB; dz++)
            for (int dy = -1; dy <= 1 && sealedB; dy++)
            for (int dx = -1; dx <= 1 && sealedB; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                int n = ((bz + dz) << 8) | ((by + dy) << 4) | (bx + dx);
                if (allSolid[n] != 0) continue;
                uint hn = handles[n];
                if ((hn & 0x80000000u) == 0) { sealedB = false; break; }   // a uniform non-solid neighbour
                byte* body = raw + (long)(hn & 0x3FFFFFFFu) * 512;
                // The neighbour's voxels touching brick b: a -1 side gives coordinate 7, +1 gives 0, 0 gives 0..7.
                int x0 = dx < 0 ? 7 : 0, x1 = dx > 0 ? 0 : 7;
                int y0 = dy < 0 ? 7 : 0, y1 = dy > 0 ? 0 : 7;
                int z0 = dz < 0 ? 7 : 0, z1 = dz > 0 ? 0 : 7;
                for (int vz = z0; vz <= z1 && sealedB; vz++)
                for (int vy = y0; vy <= y1 && sealedB; vy++)
                for (int vx = x0; vx <= x1; vx++)
                    if (solid[body[(vz << 6) | (vy << 3) | vx]] == 0) { sealedB = false; break; }
            }
            if (!sealedB) continue;
            outMask[b >> 6] |= 1UL << (b & 63);
            count++;
        }
        return count;
    }
}
