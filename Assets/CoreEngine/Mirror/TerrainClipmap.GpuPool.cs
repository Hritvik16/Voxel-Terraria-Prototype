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
    // Task 207: GPU bodies each refused chunk asked for at its refusal (its dense, non-sealed bricks),
    // and the re-queued chunks still waiting in the dirty queue with the bodies they were promised.
    private readonly Dictionary<int3, int> _gpuRefusedNeed = new Dictionary<int3, int>();
    private readonly Dictionary<int3, int> _gpuRetryPending = new Dictionary<int3, int>();
    private readonly List<int3> _gpuPendingScratch = new List<int3>();
    private int _lastRefusedNeed;
    private int _gpuFreeAtRefusal;     // RetryOnlyWhatFits = false only (the old trigger)
    /// Task 207: false = the old retry (one freed slot re-queues the whole refused set); `-retryfit 0`,
    /// for before pictures. Default true.
    public static bool RetryOnlyWhatFits = true;
    private int _retryAvailSeen = -1;
    private int3 _retryCamSeen;
    private int3 _retryCam;
    private System.Comparison<int3> _retryOrder;
    /// Task 207: refused chunks put back in the dirty queue (only when their bodies fit), all time.
    public long GpuPoolRetriesTotal { get; private set; }
    /// Task 207: re-queued chunks whose upload is still queued (promised GPU bodies, not yet landed).
    /// Task 207: retried chunks refused AGAIN by their re-upload (the churn: a retry without a slot).
    public long GpuPoolReRefusedTotal { get; private set; }
    public int GpuPoolRetryPending { get { PurgeRetryPending(); return _gpuRetryPending.Count; } }
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

    /// The refused chunks now (task 194: capture diagnostics; read-only view).
    public IReadOnlyCollection<int3> GpuRefusedChunks => _gpuRefused;

    /// Writes holder `h`'s new dense set (the chunk's bricks; null / uniform =
    /// none) and releases its previous one. False = refused (pool full): the
    /// holder then holds nothing and must be written as air.
    /// Task 201a: `sealedMask` (4096 bits, null = none) = bricks staged as a uniform
    /// handle (TerrainClipmap.Sealed.cs): they take no GPU body.
    private bool AcquireHolder(int h, Chunk chunk, ulong[] sealedMask = null)
    {
        if (_gpuOfCpu == null) return true;
        _acqNew.Clear(); _acqKept.Clear();
        if (chunk != null && !chunk.isUniform && chunk.bricks != null)
        {
            BrickHandle[] bricks = chunk.bricks;
            for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
                if ((bricks[i].data & 0x80000000u) != 0 && !MaskBit(sealedMask, i)) _acqNew.Add((int)(bricks[i].data & 0x3FFFFFFFu));
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
            _lastRefusedNeed = _acqNew.Count;   // task 207: the holder holds nothing now, so a retry needs all of them
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
        if (_gpuRetryPending.Remove(c)) GpuPoolReRefusedTotal++;
        _gpuRefused.Add(c);
        _gpuRefusedNeed[c] = _lastRefusedNeed;
        GpuPoolRefusedTotal++;
        _gpuFreeAtRefusal = _gpuFreeTop;
    }

    /// c left the refused set through its own upload (landed or evicted).
    private void ForgetRefused(int3 c)
    {
        if (_gpuRefused.Remove(c)) _gpuRefusedNeed.Remove(c);
    }

    /// Start of UploadDirty: refused chunks go back in the dirty queue only as far as the
    /// free GPU bodies reach (task 207, owner Q-AB (a)). Before, ONE freed slot cleared the
    /// WHOLE refused set: up to ~1,250 re-uploads then waited in the budgeted queue holding
    /// the refusal's air, most were refused again, and the churn never settled (task 197).
    /// Now: nearest to the camera first, a chunk is re-queued (and leaves the refused set)
    /// only if the bodies it asked for at its refusal fit in what is free minus what earlier
    /// re-queued, still-queued chunks were promised; a chunk that does not fit stays refused
    /// (197's hand-over keeps drawing it) and a farther, smaller one may still go.
    /// Evicted chunks are forgotten (their clear runs anyway); a refused chunk now outside the
    /// box (and not a fluid-dir chunk) needs no body: it is re-queued to park, as before.
    /// Runs only when the free count, the promises or the camera chunk changed.
    private void RetryGpuRefused(ChunkStore store, int3 cameraChunk)
    {
        int promised = PurgeRetryPending();   // every pass: a promise ends once its upload ran
        if (_gpuRefused.Count == 0) return;
        if (!RetryOnlyWhatFits) { RetryAllRefused(store); return; }

        int avail = _gpuFreeTop - promised;
        if (avail == _retryAvailSeen && cameraChunk.Equals(_retryCamSeen)) return;
        _retryAvailSeen = avail; _retryCamSeen = cameraChunk;
        if (avail <= 0) return;

        _gpuRefusedScratch.Clear();
        _gpuRefusedScratch.AddRange(_gpuRefused);
        _retryCam = cameraChunk;
        _retryOrder ??= (a, b) =>
        {
            int d = ChebyshevXZ(a, _retryCam).CompareTo(ChebyshevXZ(b, _retryCam));
            if (d != 0) return d;
            if (a.y != b.y) return a.y.CompareTo(b.y);
            if (a.z != b.z) return a.z.CompareTo(b.z);
            return a.x.CompareTo(b.x);
        };
        _gpuRefusedScratch.Sort(_retryOrder);

        int promisedNow = 0;
        foreach (int3 c in _gpuRefusedScratch)
        {
            if (store.GetChunk(c) == null) { ForgetRefused(c); continue; }
            if (!InMirrorBox(c) && FluidDirEntryOf(c) < 0) { ForgetRefused(c); _dirtyChunks.Add(c); continue; }
            int need = _gpuRefusedNeed.TryGetValue(c, out int n) ? n : 0;
            if (_dirtyChunks.Contains(c)) { avail -= need; continue; }   // queued by an edit: its upload decides
            if (need > avail) continue;                                   // stays refused, still handed over
            avail -= need;
            ForgetRefused(c);
            _dirtyChunks.Add(c);
            _gpuRetryPending[c] = need;
            promisedNow += need;
            GpuPoolRetriesTotal++;
        }
        // = the next pass's (free - promised) while nothing changes: it then skips the sort.
        _retryAvailSeen -= promisedNow;
    }

    /// Ends the promises of re-queued chunks whose upload already ran (landed, refused again,
    /// parked, evicted: no longer dirty); returns the GPU bodies still promised.
    private int PurgeRetryPending()
    {
        if (_gpuRetryPending.Count == 0) return 0;
        int promised = 0;
        _gpuPendingScratch.Clear();
        foreach (var kv in _gpuRetryPending)
            if (!_dirtyChunks.Contains(kv.Key)) _gpuPendingScratch.Add(kv.Key);
            else promised += kv.Value;
        for (int i = 0; i < _gpuPendingScratch.Count; i++) _gpuRetryPending.Remove(_gpuPendingScratch[i]);
        return promised;
    }

    /// The pre-207 retry (`_rf0` arm only): one freed slot re-queues every refused chunk.
    private void RetryAllRefused(ChunkStore store)
    {
        _retryAvailSeen = -1;
        if (_gpuFreeTop <= _gpuFreeAtRefusal) return;
        _gpuRefusedScratch.Clear();
        _gpuRefusedScratch.AddRange(_gpuRefused);
        _gpuRefused.Clear();
        _gpuRefusedNeed.Clear();
        foreach (int3 c in _gpuRefusedScratch)
            if (store.GetChunk(c) != null) { _dirtyChunks.Add(c); _gpuRetryPending[c] = 0; GpuPoolRetriesTotal++; }   // tracked for the re-refusal count
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
