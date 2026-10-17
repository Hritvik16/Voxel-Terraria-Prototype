// Assets/CoreEngine/Mirror/TerrainClipmap.GpuPool.cs
//
// GPU BRICK-BODY POOL (task 93f, Amendment 8.15 row D3 / §2 item 3 / §10 row 2;
// owner Q2 (a): "§3.7 layout change approved"). A LAYOUT change, not an API change.
//
// §3.7 made BrickDataBuffer "a mirror of the CPU pool's bodies": sized to the CPU
// BrickDataPool's capacity (750 k on tg2 = 366 MB) and indexed by the CPU handle.
// With the shells on, only the drawn tier-0 box (16^3 after 93e) and the fluid
// terrain directory need a GPU copy, so the GPU gets its own body pool:
//
//   _gpuOfCpu[cpuSlot]  -> GPU slot, -1 = none     (the CPU-handle -> GPU-slot table)
//   _gpuRefs[cpuSlot]   -> holders that wrote it   (a box slot, or a fluid dir entry)
//   _held[holder]       -> the CPU slots that holder's last write referenced
//
// A HOLDER is a mirror box slot (ChunkSlot) or a fluid terrain directory entry
// (chunkSlots + e): exactly the places whose GPU handles point at bodies. Each
// write of a holder ACQUIRES its new dense set (refs++, a GPU slot from the free
// stack when refs was 0) and RELEASES its previous set (refs--, slot back to the
// free stack at 0). Slots kept by both keep their GPU slot. Invariant: every
// handle a holder wrote on the GPU points at _gpuOfCpu of a CPU slot in its list,
// and _gpuOfCpu only changes when refs is 0 -- so no live GPU entry is ever
// re-pointed. The staged handles the shader reads (ClipmapBuffer,
// FluidDirHandles) carry the GPU slot in bits [29:0], flags [31:30] kept; every
// GPU reader (Raymarch / RaymarchStripped / RaymarchMemoryProbe / FluidCA:
// `BrickDataBuffer[(handle & 0x3FFFFFFF) * 128 + ..]`) is therefore unchanged.
// The CPU copies (_clipmapLocal for the air mips and the fluid CA twin,
// _fdHandles) keep CPU handles.
//
// FULL POOL: the chunk is REFUSED and counted (GpuPoolRefusedTotal), never a
// silent overwrite: its holder is cleared to air, it is reported missing to the
// shell hand-over (IsGpuRefused -> FarField.UpdateResidency drops its column, so
// shell 1 draws it, like task 32's out-of-box skip), and it is re-dirtied once a
// GPU slot has been freed. A refused fluid-dir chunk stays unpublished (the fluid
// sim waits, FluidDirHolds false).
//
// Off (cap 0, shells off / rigs / `-gpupool 0`): nothing here is allocated, the
// handles go through unchanged and BrickDataBuffer is CPU-pool sized: the old
// layout, byte for byte.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public partial class TerrainClipmap
{
    public bool GpuPoolEnabled => _gpuOfCpu != null;
    /// GPU body slots (BrickDataBuffer = cap x 512 B). 0 = the old layout.
    public int GpuPoolCapacity { get; private set; }
    public int GpuPoolInUse => GpuPoolEnabled ? GpuPoolCapacity - _gpuFreeTop : 0;
    public int GpuPoolPeakUsed { get; private set; }
    /// Chunk writes refused because the GPU pool could not hold their bricks.
    public long GpuPoolRefusedTotal { get; private set; }
    /// Chunks refused and not yet retried (held as air, drawn by their shell).
    public int GpuPoolRefusedNow => _gpuRefused.Count;
    public long GpuPoolTableBytes => GpuPoolEnabled ? (long)_gpuOfCpu.Length * 4 + (long)_gpuRefs.Length * 2 : 0;

    private int[] _gpuOfCpu;
    private ushort[] _gpuRefs;
    private int[] _gpuFree;            // stack; pops 0, 1, 2, ... from a fresh pool (runs coalesce)
    private int _gpuFreeTop;
    private List<int>[] _held;         // per holder
    private int _chunkSlotCount;
    private readonly HashSet<int3> _gpuRefused = new HashSet<int3>();
    private readonly List<int3> _gpuRefusedScratch = new List<int3>();
    private int _gpuFreeAtRefusal;
    private readonly List<int> _acqNew = new List<int>();
    private readonly List<bool> _acqKept = new List<bool>();
    private readonly List<long> _bodyPairs = new List<long>();   // (gpu << 32) | cpu, sorted by gpu

    private void InitGpuPool(int chunkSlots, int cpuCapacity, int gpuCap)
    {
        _chunkSlotCount = chunkSlots;
        if (gpuCap <= 0) return;
        GpuPoolCapacity = gpuCap;
        _gpuOfCpu = new int[cpuCapacity];
        for (int i = 0; i < cpuCapacity; i++) _gpuOfCpu[i] = -1;
        _gpuRefs = new ushort[cpuCapacity];
        _gpuFree = new int[gpuCap];
        for (int i = 0; i < gpuCap; i++) _gpuFree[i] = gpuCap - 1 - i;
        _gpuFreeTop = gpuCap;
        _held = new List<int>[chunkSlots + FluidDirMaxEntries];
    }

    /// The handle the GPU holds for a CPU handle: unchanged when uniform or the
    /// pool is off; dense -> GPU slot in [29:0]; dense but unmapped -> 0xFFFFFFFF
    /// (never written by an upload, so a validator sees it as a mismatch).
    public uint GpuHandleOf(uint cpuHandle)
    {
        if (_gpuOfCpu == null || (cpuHandle & 0x80000000u) == 0) return cpuHandle;
        int g = _gpuOfCpu[(int)(cpuHandle & 0x3FFFFFFFu)];
        return g < 0 ? 0xFFFFFFFFu : (cpuHandle & 0xC0000000u) | (uint)g;
    }

    /// GPU slot of a CPU pool slot, -1 if unmapped; the CPU slot itself when off.
    public int GpuSlotOf(int cpuSlot) => _gpuOfCpu == null ? cpuSlot : _gpuOfCpu[cpuSlot];

    /// Resident chunk refused by a full GPU pool: tier 0 draws air there and the
    /// shell hand-over treats its column as missing.
    public bool IsGpuRefused(int3 chunkCoord) => _gpuRefused.Count > 0 && _gpuRefused.Contains(chunkCoord);

    /// Writes holder `h`'s new dense set (the chunk's bricks; null / uniform =
    /// none) and releases its previous one. False = refused (pool full): the
    /// holder then holds nothing and must be written as air.
    private bool AcquireHolder(int h, Chunk chunk)
    {
        if (_gpuOfCpu == null) return true;
        _acqNew.Clear(); _acqKept.Clear();
        if (chunk != null && !chunk.isUniform && chunk.bricks != null)
        {
            BrickHandle[] bricks = chunk.bricks;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
                if ((bricks[i].data & 0x80000000u) != 0) _acqNew.Add((int)(bricks[i].data & 0x3FFFFFFFu));
        }

        // 1. Keep what is already mapped (refs++), count what needs a new slot.
        int need = 0;
        for (int i = 0; i < _acqNew.Count; i++)
        {
            int c = _acqNew[i];
            bool kept = _gpuRefs[c] > 0;
            if (kept) _gpuRefs[c]++; else need++;
            _acqKept.Add(kept);
        }
        // 2. Release the previous set (its slots are free for step 3).
        ReleaseHolder(h);
        // 3. Map the rest, or refuse and undo step 1.
        if (need > _gpuFreeTop)
        {
            for (int i = 0; i < _acqNew.Count; i++) if (_acqKept[i]) Unref(_acqNew[i]);
            return false;
        }
        for (int i = 0; i < _acqNew.Count; i++)
        {
            if (_acqKept[i]) continue;
            int c = _acqNew[i];
            if (_gpuRefs[c]++ == 0) _gpuOfCpu[c] = _gpuFree[--_gpuFreeTop];   // a chunk lists a CPU slot once; guard anyway
        }
        if (_acqNew.Count > 0)
        {
            List<int> l = _held[h] ??= new List<int>(_acqNew.Count);
            l.AddRange(_acqNew);
        }
        int used = GpuPoolCapacity - _gpuFreeTop;
        if (used > GpuPoolPeakUsed) GpuPoolPeakUsed = used;
        return true;
    }

    private void ReleaseHolder(int h)
    {
        if (_gpuOfCpu == null) return;
        List<int> l = _held[h];
        if (l == null || l.Count == 0) return;
        for (int i = 0; i < l.Count; i++) Unref(l[i]);
        l.Clear();
    }

    private void Unref(int c)
    {
        if (--_gpuRefs[c] != 0) return;
        _gpuFree[_gpuFreeTop++] = _gpuOfCpu[c];
        _gpuOfCpu[c] = -1;
    }

    private void Refuse(int3 c)
    {
        _gpuRefused.Add(c);
        GpuPoolRefusedTotal++;
        _gpuFreeAtRefusal = _gpuFreeTop;
    }

    /// Start of UploadDirty: refused chunks are retried once a GPU slot was freed
    /// since the last refusal; evicted ones are forgotten (their clear runs anyway).
    private void RetryGpuRefused(ChunkStore store)
    {
        if (_gpuRefused.Count == 0 || _gpuFreeTop <= _gpuFreeAtRefusal) return;
        _gpuRefusedScratch.Clear();
        _gpuRefusedScratch.AddRange(_gpuRefused);
        _gpuRefused.Clear();
        foreach (int3 c in _gpuRefusedScratch)
            if (store.GetChunk(c) != null) _dirtyChunks.Add(c);
        _gpuFreeAtRefusal = _gpuFreeTop;
    }

    /// Uploads the bodies of _dirtyBrickSlots (CPU slots) at their GPU slots,
    /// coalescing pairs consecutive on BOTH sides into one write.
    private int UploadDirtyBrickBodiesMapped(NativeArray<uint> asUints, ref UploadStats stats)
    {
        _bodyPairs.Clear();
        for (int i = 0; i < _dirtyBrickSlots.Count; i++)
        {
            int c = _dirtyBrickSlots[i];
            int g = _gpuOfCpu[c];
            if (g >= 0) _bodyPairs.Add(((long)g << 32) | (uint)c);
        }
        _bodyPairs.Sort();
        stats.brickSlots = _bodyPairs.Count;

        int bytes = 0, k = 0;
        while (k < _bodyPairs.Count)
        {
            int g0 = (int)(_bodyPairs[k] >> 32), c0 = (int)(uint)_bodyPairs[k];
            int n = 1;
            k++;
            while (k < _bodyPairs.Count)
            {
                int g = (int)(_bodyPairs[k] >> 32), c = (int)(uint)_bodyPairs[k];
                if (g == g0 + n - 1 && c == c0 + n - 1) { k++; continue; }   // duplicate
                if (g != g0 + n || c != c0 + n) break;
                n++; k++;
            }
            WriteBrickRunAt(asUints, c0 * 128, g0 * 128, n * 128, ref stats);
            stats.brickRuns++;
            bytes += n * 128 * 4;
        }
        _dirtyBrickSlots.Clear();
        return bytes;
    }

    // --- CPU SHADOW of the two GPU buffers (tests: the suite runs -nographics and
    // GraphicsBuffer.GetData reads zeros there). Off unless EnableCpuShadow.
    private uint[] _shadowClip, _shadowBodies;

    /// Test hook: keep a managed copy of every ClipmapBuffer / BrickDataBuffer write,
    /// which ClipmapValidator then reads instead of the GPU.
    public void EnableCpuShadow()
    {
        _shadowClip = new uint[ClipmapBuffer.count];
        _shadowBodies = new uint[BrickDataBuffer.count];
    }

    /// Test hook: overwrite one word of a body in the shadow (an injected mismatch).
    public void DebugPokeShadowBody(int gpuSlot, int word, uint value) => _shadowBodies[gpuSlot * 128 + word] = value;

    /// Test hook: swap two CPU slots' GPU slots in the table without uploading (a table mix-up).
    public void DebugSwapTable(int cpuA, int cpuB)
        => (_gpuOfCpu[cpuA], _gpuOfCpu[cpuB]) = (_gpuOfCpu[cpuB], _gpuOfCpu[cpuA]);

    /// What the GPU clipmap holds (the shadow when enabled), for the validator.
    public void ReadClipmapForValidation(uint[] dst)
    {
        if (_shadowClip != null) System.Array.Copy(_shadowClip, dst, dst.Length);
        else ClipmapBuffer.GetData(dst);
    }

    /// What BrickDataBuffer holds in [firstUint, firstUint + count), for the validator.
    public void ReadBodiesForValidation(uint[] dst, int firstUint, int count)
    {
        if (_shadowBodies != null) System.Array.Copy(_shadowBodies, firstUint, dst, 0, count);
        else BrickDataBuffer.GetData(dst, 0, firstUint, count);
    }
}
