// Assets/CoreEngine/Gameplay/EditOps.cs
//
// Amendment 8.14 rows 8-9 (engine step 13a, task 81): IEditOps over the save's
// EditOpLog. Record appends the op (persisted, and visible to the generation
// workers through the log's snapshot from then on) and applies it NOW to the
// resident chunks it reaches, through EditService -- the existing edit path,
// so near and far give the same voxels (EditOpTests). Chunks that are not
// resident, or are in flight, get it on generation (StreamManager).
// SetVoxel is untouched: it is still refused outside loaded chunks.
//
// Main thread only, like EditService and the log.
using Unity.Mathematics;
using VoxelEngine.Streaming;

public sealed class EditOps : IEditOps
{
    private readonly EditOpLog _log;
    private readonly EditService _edits;
    private readonly IVoxelResidency _residency;

    /// Voxels the near apply changed, over all Record calls.
    public long NearVoxelsWritten { get; private set; }

    /// `edits` may be null or unwired: the op is then only logged (it still
    /// reaches every chunk generated afterwards).
    public EditOps(EditOpLog log, EditService edits, IVoxelResidency residency)
    {
        _log = log ?? throw new System.ArgumentNullException(nameof(log));
        _edits = edits;
        _residency = residency;
    }

    public EditOpLog Log => _log;

    public uint Record(EditOp op)
    {
        op.seq = _log.Append(op);
        if (_edits == null || !_edits.IsWired || _residency == null) return op.seq;

        // Resident chunks only, one at a time, clipped to the op's box: a 20 m
        // crater far away must not walk 33 M voxels asking "resident?".
        EditOpApply.Bounds(op, out int3 lo, out int3 hi);
        int3 k0 = lo >> 7, k1 = hi >> 7;
        for (int cz = k0.z; cz <= k1.z; cz++)
        for (int cy = k0.y; cy <= k1.y; cy++)
        for (int cx = k0.x; cx <= k1.x; cx++)
        {
            int3 cc = new int3(cx, cy, cz);
            if (!_residency.IsResident(cc)) continue;
            int3 a = math.max(lo, cc << 7), b = math.min(hi, (cc << 7) + (EngineConfig.CHUNK_EDGE_VOXELS - 1));
            int changed = 0;
            for (int z = a.z; z <= b.z; z++)
            for (int y = a.y; y <= b.y; y++)
            for (int x = a.x; x <= b.x; x++)
            {
                var v = new int3(x, y, z);
                if (EditOpApply.Covers(op, v) && _edits.WriteVoxelDeferred(v, op.material)) changed++;
            }
            // SetSphere/SetBox's one wake scan, over this chunk's part of the box.
            if (changed > 0) _edits.NotifyEditedRegion(a, b);
            NearVoxelsWritten += changed;
        }
        return op.seq;
    }
}
