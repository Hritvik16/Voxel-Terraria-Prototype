// Assets/CoreEngine/Mirror/TerrainClipmap.FluidDir.cs
//
// FLUID TERRAIN DIRECTORY (task 93e, owner Q55 (b), 2026-10-16).
//
// The GPU fluid CA reads its terrain from the tier-0 mirror (FluidCA.compute
// SampleVoxel) and got AIR outside the box. With the shells on the box shrinks
// to 16^3 (~90 m reach) while fluid stays live to 128 m on every preset
// (Amendment 8.15 §1.2), so water 90-128 m away saw rock as air and froze (the
// CPU re-checks every op and dropped them all).
//
// This is a second, small terrain lookup for exactly those chunks: RESIDENT
// chunks OUTSIDE the box that a resident fluid tile (+1 voxel apron, the CA's
// widest read) touches. Written FROM the CPU state in the same UploadDirty pass
// and under the same byte / chunk budget as the mirror (CPU stays the sole
// terrain writer). Three GPU buffers:
//   FluidDirIndex   ring slot of the chunk (store ring dims, pow2, & mask) -> entry, -1 none
//   FluidDirCoords  entry * 4 + xyz = the chunk it holds (aliasing check)
//   FluidDirHandles entry * 4096 + LocalBrickIndex = the brick handle, as ClipmapBuffer
// Dense bodies go to BrickDataBuffer at their CPU pool index, as the mirror's do
// (the body buffer is CPU-pool sized until task 93f / D3, which must keep these
// bricks mapped).
//
// An index slot is published only AFTER its entry's handles were written, and is
// cleared at once when the chunk leaves the set, is evicted or enters the box.
// Off (EnableFluidDir never called, i.e. today's box) = nothing allocated and
// the shader's old AIR branch.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Memory;

public partial class TerrainClipmap
{
    public const int FluidDirInitialEntries = 64;     // 1 MB of handles
    public const int FluidDirMaxEntries = 1024;       // 16 MB of handles

    public bool FluidDirEnabled => _fdIndex != null;
    public GraphicsBuffer FluidDirIndexBuffer { get; private set; }
    public GraphicsBuffer FluidDirCoordBuffer { get; private set; }
    public GraphicsBuffer FluidDirHandleBuffer { get; private set; }
    public int3 FluidDirRingDims => _fdRingDims;
    public int FluidDirCapacity => _fdCapacity;
    public int FluidDirEntries => _fdEntryOf.Count;
    public int FluidDirPeakEntries { get; private set; }
    /// Chunks published into the directory (uploads), summed.
    public long FluidDirUploadsTotal { get; private set; }
    /// Wanted chunks refused because the directory was at FluidDirMaxEntries.
    public long FluidDirRefusedTotal { get; private set; }
    public long FluidDirGpuBytes =>
        _fdIndex == null ? 0 : (long)_fdIndex.Length * 4 + (long)_fdCapacity * (4 * 4 + EngineConfig.BRICKS_PER_CHUNK * 4);

    private int3 _fdRingDims, _fdRingMask;
    private int[] _fdIndex;                 // managed copy of FluidDirIndex
    private int[] _fdCoords;                // managed copy of FluidDirCoords
    private uint[] _fdHandles;              // managed copy of FluidDirHandles (CPU twin)
    private int _fdCapacity;
    private bool _fdIndexChanged;
    private readonly Dictionary<int3, int> _fdEntryOf = new Dictionary<int3, int>();
    private readonly HashSet<int3> _fdPublished = new HashSet<int3>();
    private readonly Stack<int> _fdFree = new Stack<int>();
    private readonly HashSet<int3> _fdWanted = new HashSet<int3>();
    private readonly List<int3> _fdScratch = new List<int3>();

    /// Turns the directory on. ringDims = the CPU store ring (pow2): every
    /// resident chunk has its own ring slot there, so two entries never collide.
    public void EnableFluidDir(int3 ringDims)
    {
        if (!math.ispow2(ringDims.x) || !math.ispow2(ringDims.y) || !math.ispow2(ringDims.z))
            throw new System.ArgumentException($"fluid dir ring {ringDims} must be a power of two (§6.2)");
        _fdRingDims = ringDims;
        _fdRingMask = ringDims - new int3(1, 1, 1);
        _fdIndex = new int[ringDims.x * ringDims.y * ringDims.z];
        for (int i = 0; i < _fdIndex.Length; i++) _fdIndex[i] = -1;
        FluidDirIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _fdIndex.Length, 4);
        FluidDirIndexBuffer.SetData(_fdIndex);
        AllocFluidDirEntries(FluidDirInitialEntries);
    }

    private void AllocFluidDirEntries(int capacity)
    {
        int old = _fdCapacity;
        System.Array.Resize(ref _fdCoords, capacity * 4);
        System.Array.Resize(ref _fdHandles, capacity * EngineConfig.BRICKS_PER_CHUNK);
        for (int e = old; e < capacity; e++) { _fdCoords[e * 4 + 3] = -1; }
        for (int e = capacity - 1; e >= old; e--) _fdFree.Push(e);
        _fdCapacity = capacity;

        FluidDirCoordBuffer?.Release();
        FluidDirHandleBuffer?.Release();
        FluidDirCoordBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 4, 4);
        FluidDirHandleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * EngineConfig.BRICKS_PER_CHUNK, 4);
        FluidDirCoordBuffer.SetData(_fdCoords);
        FluidDirHandleBuffer.SetData(_fdHandles);   // old entries keep their handles
    }

    /// The fluid sim's chunks (FluidGpuSimulation, once per tick): every chunk a
    /// resident tile + 1 voxel touches. Inside-the-box and non-resident chunks are
    /// filtered at the next UploadDirty.
    public void SetFluidWanted(HashSet<int3> chunks)
    {
        if (_fdIndex == null) return;
        _fdWanted.Clear();
        foreach (int3 c in chunks) _fdWanted.Add(c);
    }

    private int FluidDirRingSlot(int3 c)
    {
        int3 w = c & _fdRingMask;
        return w.x + _fdRingDims.x * (w.y + _fdRingDims.y * w.z);
    }

    /// Start of UploadDirty: drop entries no longer wanted / evicted / now in the
    /// box (their index slot cleared at once, before any body slot is reused),
    /// then give newly wanted out-of-box residents an entry and mark them dirty.
    private void SyncFluidDir(ChunkStore store)
    {
        if (_fdIndex == null) return;

        _fdScratch.Clear();
        foreach (var kv in _fdEntryOf)
        {
            int3 c = kv.Key;
            if (!_fdWanted.Contains(c) || store.GetChunk(c) == null || InMirrorBox(c)) _fdScratch.Add(c);
        }
        foreach (int3 c in _fdScratch)
        {
            int e = _fdEntryOf[c];
            _fdEntryOf.Remove(c);
            if (_fdPublished.Remove(c))
            {
                int s = FluidDirRingSlot(c);
                if (_fdIndex[s] == e) { _fdIndex[s] = -1; _fdIndexChanged = true; }
            }
            _fdCoords[e * 4 + 3] = -1;
            _fdFree.Push(e);
        }

        foreach (int3 c in _fdWanted)
        {
            if (_fdEntryOf.ContainsKey(c) || InMirrorBox(c) || store.GetChunk(c) == null) continue;
            if (_fdFree.Count == 0)
            {
                if (_fdCapacity >= FluidDirMaxEntries) { FluidDirRefusedTotal++; continue; }
                AllocFluidDirEntries(math.min(_fdCapacity * 2, FluidDirMaxEntries));
            }
            _fdEntryOf[c] = _fdFree.Pop();
            _parkedOutOfBox.Remove(c);
            _dirtyChunks.Add(c);
        }
        if (_fdEntryOf.Count > FluidDirPeakEntries) FluidDirPeakEntries = _fdEntryOf.Count;
        FlushFluidDirIndex();
    }

    /// True once the chunk's entry is published (readable by the CA). The fluid
    /// sim's wake gate waits for it outside the box.
    public bool FluidDirHolds(int3 c) => _fdPublished.Contains(c);

    /// Entry of an out-of-box resident chunk the fluid needs, or -1.
    private int FluidDirEntryOf(int3 c)
        => _fdIndex != null && _fdEntryOf.TryGetValue(c, out int e) ? e : -1;

    /// Writes one chunk's 4096 handles into its entry, queues its dense bodies,
    /// then publishes the index slot.
    private void UploadFluidDirChunk(Chunk chunk, int3 c, int e)
    {
        int b0 = e * EngineConfig.BRICKS_PER_CHUNK;
        for (int i = 0; i < EngineConfig.BRICKS_PER_CHUNK; i++)
        {
            uint handle = chunk.isUniform ? chunk.uniformMaterial : chunk.bricks[i].data;
            _fdHandles[b0 + i] = handle;
            if ((handle & 0x80000000u) != 0) _dirtyBrickSlots.Add((int)(handle & 0x3FFFFFFFu));
        }
        _fdCoords[e * 4 + 0] = c.x; _fdCoords[e * 4 + 1] = c.y; _fdCoords[e * 4 + 2] = c.z; _fdCoords[e * 4 + 3] = 0;
        if (!SuppressGpuUploads)
        {
            FluidDirHandleBuffer.SetData(_fdHandles, b0, b0, EngineConfig.BRICKS_PER_CHUNK);
            FluidDirCoordBuffer.SetData(_fdCoords, e * 4, e * 4, 4);
        }
        int s = FluidDirRingSlot(c);
        if (_fdIndex[s] != e) { _fdIndex[s] = e; _fdIndexChanged = true; }
        _fdPublished.Add(c);
        FluidDirUploadsTotal++;
    }

    private void FlushFluidDirIndex()
    {
        if (!_fdIndexChanged) return;
        _fdIndexChanged = false;
        if (!SuppressGpuUploads) FluidDirIndexBuffer.SetData(_fdIndex);   // 64 KB, only when the set changed
    }

    /// CPU twin of FluidCA.compute SampleVoxel, line for line: the mirror inside
    /// the box, the fluid directory outside it, AIR otherwise. Reads the managed
    /// copies of what was uploaded and the CPU pool at the same body index.
    public byte SampleVoxelAsFluidCA(int3 voxel, BrickDataPool cpuPool)
    {
        int3 brick = CoordMath.VoxelToBrick(voxel);
        int3 chunk = CoordMath.BrickToChunk(brick);
        uint handle;
        if (InMirrorBox(chunk)) handle = _clipmapLocal[BrickLinearIndex(brick)];
        else
        {
            if (_fdIndex == null) return 0;
            int e = _fdIndex[FluidDirRingSlot(chunk)];
            if (e < 0 || _fdCoords[e * 4 + 0] != chunk.x || _fdCoords[e * 4 + 1] != chunk.y || _fdCoords[e * 4 + 2] != chunk.z)
                return 0;
            handle = _fdHandles[e * EngineConfig.BRICKS_PER_CHUNK + CoordMath.LocalBrickIndex(CoordMath.LocalBrickIndex3D(brick))];
        }
        if ((handle & 0x80000000u) == 0) return (byte)(handle & 0xFFu);
        int poolIdx = (int)(handle & 0x3FFFFFFFu);
        int v = CoordMath.LocalVoxelIndex(CoordMath.LocalVoxelIndex3D(voxel));
        return cpuPool.RawData[poolIdx * EngineConfig.BRICK_BODY_BYTES + v];
    }

    private void DisposeFluidDir()
    {
        FluidDirIndexBuffer?.Release(); FluidDirIndexBuffer = null;
        FluidDirCoordBuffer?.Release(); FluidDirCoordBuffer = null;
        FluidDirHandleBuffer?.Release(); FluidDirHandleBuffer = null;
    }
}
