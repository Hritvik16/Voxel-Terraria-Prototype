// Assets/CoreEngine/Mirror/TerrainClipmap.HeightDirty.cs
//
// TASK 168 (PERF_EXACT_CUTS row D, incremental roof pyramid). The shadow roof
// skip's HeightMip pyramid (Raymarch.compute) is a pure function of the
// ClipmapBuffer handles, the window's Y origin / height and the content
// ceiling: BuildHeightRaw's ring column -> world column map rotates with the X/Z
// origin but ReadClipmap only sees the ring slot, so an X/Z recentre alone
// changes nothing. WriteClipmapChunk is the ONLY writer of ClipmapBuffer after
// construction (the CPU is the sole writer; the GPU body pool re-points no live
// entry), so the ring CHUNK COLUMNS it wrote since the last pyramid build are
// exactly the columns whose level-0 height can have changed. RaymarchFeature
// takes them once per build (TakeHeightDirtyColumns) and rebuilds only those
// columns plus the dilation margin and the coarser cells above them.
using System.Collections.Generic;

public partial class TerrainClipmap
{
    private bool[] _heightDirtyFlag;
    private readonly List<uint> _heightDirtyCols = new List<uint>();   // packed x | z << 16
    /// Every ring column written at least once since construction or the last
    /// take; > 0 also means "not empty" for the caller's full-rebuild threshold.
    public int HeightDirtyCount => _heightDirtyCols.Count;
    public long HeightDirtyMarkedTotal { get; private set; }

    /// Called right after a chunk's ClipmapBuffer write (any Y layer).
    /// Ring column index = (cx & maskX) + dimsX * (cz & maskZ).
    private void MarkHeightDirty(Unity.Mathematics.int3 chunkCoord)
    {
        int cols = _windowDimsChunks.x * _windowDimsChunks.z;
        if (_heightDirtyFlag == null || _heightDirtyFlag.Length != cols) _heightDirtyFlag = new bool[cols];
        int x = chunkCoord.x & _chunkMask.x, z = chunkCoord.z & _chunkMask.z;
        int col = x + _windowDimsChunks.x * z;
        if (_heightDirtyFlag[col]) return;
        _heightDirtyFlag[col] = true;
        _heightDirtyCols.Add((uint)x | ((uint)z << 16));
        HeightDirtyMarkedTotal++;
    }

    /// Appends the dirty ring columns (packed x | z << 16, ring chunk coordinates)
    /// to dst and clears the set. Returns how many were appended.
    public int TakeHeightDirtyColumns(List<uint> dst)
    {
        int n = _heightDirtyCols.Count;
        for (int i = 0; i < n; i++)
        {
            uint p = _heightDirtyCols[i];
            dst.Add(p);
            _heightDirtyFlag[(int)(p & 0xFFFFu) + _windowDimsChunks.x * (int)(p >> 16)] = false;
        }
        _heightDirtyCols.Clear();
        return n;
    }
}
