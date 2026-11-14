// Assets/CoreEngine/Mirror/TerrainClipmap.SlotProbe.cs
//
// TASK 197: what the mirror holds for one chunk. DebugSlotState (DIAGNOSTIC, read-only) feeds the
// capture's `[mirror]` probe (PlaygroundCapture.MirrorProbe.cs); Tier0LacksChunk feeds the tier-0
// hand-over (FarField.UpdateResidency). Neither changes what is uploaded.
using Unity.Mathematics;
using VoxelEngine.Memory;

public partial class TerrainClipmap
{
    /// Does chunk c's slot hold c's own last write (owner c and not cleared/refused)?
    /// plus the bookkeeping that explains a "no": dirty (upload queued), parked
    /// (out of box), refused (full GPU pool), last upload epoch (-1 = never).
    public void DebugSlotState(int3 c, out bool inBox, out bool ownerIsC, out bool held,
                               out bool dirty, out bool parked, out bool refused, out long lastEpoch)
    {
        inBox = InMirrorBox(c);
        int s = ChunkSlot(c);
        ownerIsC = _slotOwner[s].Equals(c);
        held = _slotHeld[s];
        dirty = _dirtyChunks.Contains(c);
        parked = _parkedOutOfBox.Contains(c);
        refused = _gpuRefused.Contains(c);
        lastEpoch = _lastUploadEpoch.TryGetValue(c, out long e) ? e : -1;
    }
}

public partial class TerrainClipmap
{
    /// TASK 197: does tier 0 lack RESIDENT chunk c's real body? True when c is in the mirror box and
    /// its slot does not hold c's own write -- the slot was last cleared or refused (held false), or a
    /// different chunk wrote it (owner != c) -- unless c is uniform air and the slot holds nothing (air
    /// drawn as air is right). This is every way a resident column shows air (a PIT) or another chunk:
    ///   refused by the full GPU pool (task 194's path; IsGpuRefused implies it),
    ///   refused, then RETRIED (RetryGpuRefused clears the refused set at once, but the re-upload waits
    ///     in the budgeted, nearest-first dirty queue -- 194b's jungle_high pits with 0 refused),
    ///   first upload still queued (a chunk generated after the box reached it).
    /// chunk == null (not resident, still streaming) -> false: that column stays tier 0's (task 194's
    /// rule; near the camera it lies in shell 1's hole). Read-only.
    public bool Tier0LacksChunk(int3 c, Chunk chunk)
    {
        if (chunk == null || !InMirrorBox(c)) return false;
        int s = ChunkSlot(c);
        if (_slotHeld[s] && _slotOwner[s].Equals(c)) return false;
        bool air = chunk.isUniform && chunk.uniformMaterial == 0;
        return !air || _slotHeld[s];
    }
}

public partial class TerrainClipmap
{
    /// TASK 197 (DIAGNOSTIC, capture only; a GPU readback, stalls): chunk c's 4096 GPU clipmap
    /// entries against what the CPU believes it wrote (the CPU mirror through the body table).
    /// Returns the differing entries; gpuAirCpuSolid = of those, GPU air where the CPU has a solid brick.
    public int DebugGpuChunkDiff(int3 c, uint[] scratch, out int gpuAirCpuSolid)
    {
        gpuAirCpuSolid = 0;
        ClipmapBuffer.GetData(scratch, 0, ChunkSlot(c) * EngineConfig.BRICKS_PER_CHUNK, EngineConfig.BRICKS_PER_CHUNK);
        int diff = 0;
        for (int z = 0; z < 16; z++)
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            int3 lb = new int3(x, y, z);
            uint cpu = CpuMirrorHandle(c, lb);
            uint exp = ExpectedGpuHandle(c, lb, cpu);   // task 201a
            uint gpu = scratch[CoordMath.LocalBrickIndex(lb)];
            if (gpu == exp) continue;
            diff++;
            if (gpu == 0u && cpu != 0u) gpuAirCpuSolid++;
        }
        return diff;
    }

    /// TASK 197 (DIAGNOSTIC): packed air-mip words on the GPU that differ from the CPU's packed pyramid.
    public int DebugAirMipDiffWords()
    {
        if (_packed == null || AirMipPackedBuffer == null) return -1;
        var g = new uint[_packed.Words.Length];
        AirMipPackedBuffer.GetData(g);
        int n = 0;
        for (int i = 0; i < g.Length; i++) if (g[i] != _packed.Words[i]) n++;
        return n;
    }
}
