// ==========================================
// Assets/CoreEngine/Gameplay/VoxelRayWalker.cs
//
// THE ONE VOXEL-DDA STEPPING MACHINE, shared by SweptCCD (§8.2) and
// ProjectileTrace (§8.4).
//
// Amanatides & Woo traversal, factored so the ARITHMETIC lives once and each
// caller supplies its own idea of what a cell means. §8.2 stops at the first
// solid and accumulates fluid distance on the way; §8.4 stops at the first
// solid and reports what it hit. Those differ in what they ask about a cell,
// not in how they find the next one -- so what is shared here is the walk, and
// what stays with the caller is the question.
//
// WHY FACTORED AND NOT COPIED. Two hand-rolled DDAs is two chances to get
// tMax/tDelta initialisation subtly different, and the failure mode is a
// projectile that passes through a wall the player's sweep stops at. That
// disagreement would look like a projectile bug and actually be a duplication
// bug. Same reasoning as VoxelCollision.cs, which shares "what is solid"
// between the same two files.
//
// BURST. This struct is deliberately unmanaged -- no interfaces, no class
// references, only int3/float3 -- so it is Burst-compatible as written, which
// is what §8.4's "in a Burst job" needs from the traversal half. See
// ProjectileTrace's header for why the JOB half is not wired: the world it
// would have to read (ChunkStore's managed Chunk objects) is not Burst-legal,
// and that is a §3.x layout question, not something this file can decide.
//
// USAGE:
//     var w = VoxelRayWalker.Create(p0, p1);
//     while (w.MoveNext())
//     {
//         if (IsBlocking(w.Voxel)) { /* hit at w.TEnter */ break; }
//         Accumulate(w.Voxel, w.TExit - w.TEnter);
//     }

using Unity.Mathematics;

public struct VoxelRayWalker
{
    /// The cell currently being visited.
    public int3 Voxel;

    /// Distance along the ray, in metres, at which `Voxel` was entered. This is
    /// the impact distance if the caller decides this cell is a hit.
    public float TEnter;

    /// Distance at which the ray leaves `Voxel`, clamped to the segment end.
    /// TExit - TEnter is the length travelled INSIDE this cell, which is what
    /// §8.2's fluid accumulator integrates.
    public float TExit;

    /// Total length of the segment, metres.
    public float Length;

    /// True when the current cell is the last one the segment reaches.
    public bool AtEnd;

    private int3 _step;
    private float3 _tMax, _tDelta;
    private int _axis;
    private int _remaining;
    private bool _started;

    public static VoxelRayWalker Create(float3 p0, float3 p1)
    {
        var w = new VoxelRayWalker();
        float3 d = p1 - p0;
        w.Length = math.length(d);
        if (w.Length <= 1e-9f) { w._remaining = 0; return w; }

        float3 dir = d / w.Length;
        const float s = VoxelCollision.VoxelSizeM;

        w.Voxel = CoordMath.WorldToVoxel(p0);
        w._step = new int3(dir.x > 0f ? 1 : -1, dir.y > 0f ? 1 : -1, dir.z > 0f ? 1 : -1);

        // Distance along the ray to the next voxel boundary on each axis, and
        // the distance between successive boundaries on that axis.
        for (int k = 0; k < 3; k++)
        {
            if (math.abs(dir[k]) < 1e-12f)
            {
                // Parallel to this axis: it never contributes a crossing.
                w._tDelta[k] = float.PositiveInfinity;
                w._tMax[k] = float.PositiveInfinity;
            }
            else
            {
                w._tDelta[k] = math.abs(s / dir[k]);
                float boundary = (w.Voxel[k] + (w._step[k] > 0 ? 1 : 0)) * s;
                w._tMax[k] = (boundary - p0[k]) / dir[k];
            }
        }

        // A finite bound, so a degenerate direction cannot spin forever. The
        // ray cannot visit more cells than its length in voxels on all three
        // axes, plus slack for the starting cell and boundary ties.
        w._remaining = (int)(w.Length / s) * 3 + 8;
        w._started = false;
        w.AtEnd = false;
        return w;
    }

    /// Advances to the next cell. False when the segment is exhausted.
    public bool MoveNext()
    {
        if (_remaining <= 0) return false;

        if (!_started)
        {
            _started = true;                 // the starting cell is already in Voxel
        }
        else
        {
            if (AtEnd) return false;         // the previous cell reached the end
            TEnter = _tMax[_axis];
            Voxel[_axis] += _step[_axis];
            _tMax[_axis] += _tDelta[_axis];
        }

        // Which axis boundary comes next, and where this cell is left.
        _axis = _tMax.x < _tMax.y ? (_tMax.x < _tMax.z ? 0 : 2)
                                  : (_tMax.y < _tMax.z ? 1 : 2);
        TExit = math.min(_tMax[_axis], Length);
        AtEnd = _tMax[_axis] >= Length;
        _remaining--;
        return true;
    }
}
