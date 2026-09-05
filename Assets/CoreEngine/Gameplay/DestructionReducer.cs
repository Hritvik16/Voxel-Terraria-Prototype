// ==========================================
// Assets/CoreEngine/Gameplay/DestructionReducer.cs
//
// §13 Phase 6, file 5 of 6 (§8.5 Mass Destruction):
//   "A Burst job over the event's bounding region calls the batch SetVoxel
//    path: read material (for a tally histogram), write Air, mark dirty. A 400K
//    event splits across 2-3 frames if it exceeds a per-frame work budget. On
//    completion, one Proxy Drop Rigidbody carries the aggregate tally -- one
//    PhysX box regardless of blast size. The CPU never loops per-voxel on the
//    main thread (Burst-parallel, frame-split if large)."
//
// §13's acceptance line: "400K detonation: recovery <=3 frames, one Proxy Drop,
// plausible tally."
//
// =========================================================================
// WHERE THIS DEPARTS FROM §8.5'S WORDING, AND WHY IT HAS TO
// =========================================================================
// §8.5 asks for two things that cannot both be true in this codebase, and the
// conflict is not mine to resolve quietly:
//
//   §8.5      "Burst-parallel", "the CPU never loops per-voxel on the main
//             thread"
//   CLAUDE.md "Single-writer: only the main thread mutates ChunkStore... If you
//             find yourself reaching for a lock anywhere in the streaming path,
//             that's a sign the single-writer rule was about to be violated"
//
// The hard invariant wins: WRITES STAY ON THE MAIN THREAD, through
// ChunkStore.SetVoxel via EditService. What actually bounds the cost is the
// other half of §8.5's own sentence -- FRAME-SPLITTING under a per-frame work
// budget -- which is implemented here and is the thing §13's "recovery <=3
// frames" actually measures.
//
// The parallel half is additionally blocked by the same layout fact recorded in
// ProjectileTrace: Chunk is a managed class holding a managed BrickHandle[], so
// no Burst job can read or write the world at all today. Even the tally, which
// is read-only and would parallelise cleanly, cannot run in a job until that
// §3.2/§3.3 question is answered. NO PERFORMANCE CLAIM IS MADE HERE; the rig
// reports voxels and frames, never milliseconds.
//
// =========================================================================
// THE PROXY DROP IS A DESCRIPTOR, NOT A Rigidbody
// =========================================================================
// §8.5 says the Proxy Drop is a Rigidbody. Spawning one is Game-layer content
// -- §12's ownership rules keep the engine ignorant of what a drop LOOKS like,
// the same reason §8.1 puts the PlayerFeedback response in Game/ and only the
// hook in the engine. This file produces the ProxyDrop VALUE (where, how much,
// of what) exactly once per event; a Game-layer caller turns it into a
// Rigidbody. Making it a value is also what lets EditMode assert "exactly one
// per detonation" without a physics scene.

using System;
using Unity.Mathematics;
using VoxelEngine.Simulation;

/// The aggregate result of one destruction event. §8.5: "one Proxy Drop
/// Rigidbody carries the aggregate tally -- one PhysX box regardless of blast
/// size."
public struct ProxyDrop
{
    /// Where the drop belongs, in voxels and in metres.
    public int3 CentreVoxel;
    public float3 CentreM;

    /// Total voxels actually removed (not the volume swept -- air and
    /// already-empty cells do not count).
    public int TotalVoxels;

    /// How many frames the event took to drain.
    public int Frames;

    /// Per-material counts, indexed by material id. This is §8.5's "tally
    /// histogram" and what a game layer turns into loot.
    public int[] Tally;

    /// The most-destroyed material, or Air when nothing was removed.
    public byte DominantMaterial;
}

public sealed class DestructionReducer
{
    /// §8.5: "A 400K event splits across 2-3 frames if it exceeds a per-frame
    /// work budget."
    ///
    /// SIZED FROM THE REFERENCE EVENT'S REAL CELL COUNT, not from the round
    /// number. §13 says "400K", but the smallest sphere covering at least
    /// 400,000 cells is radius 46, which covers 407,720 -- so a budget of
    /// exactly 400,000/3 = 133,334 drains it in FOUR frames and fails §13's
    /// "<=3" by construction. That is not a tuning accident; it is what
    /// happens when a budget is derived from the label instead of the thing.
    /// 150,000 covers events up to 450,000 cells in three frames, giving
    /// headroom for a blast somewhat larger than the reference.
    ///
    /// THIS IS A WORK BUDGET, NOT A TIME BUDGET. It bounds voxels per frame,
    /// which is the only thing this file can bound honestly -- what that costs
    /// in milliseconds is unmeasured and deliberately unclaimed.
    public const int DefaultVoxelsPerFrame = 150000;

    private readonly EditService _edits;
    private readonly IWorldQuery _world;

    // ---- In-flight event state ----
    private bool _active;
    private int3 _centre;
    private int _radius;
    private byte _replaceWith;
    private int3 _lo, _hi;
    private int _cx, _cy, _cz;          // cursor within the bounding box
    private int _removed;
    private int _frames;
    private int[] _tally;

    /// Set once when an event finishes, and cleared by TakeCompleted.
    private ProxyDrop? _completed;

    public bool InProgress => _active;
    public int VoxelsRemovedSoFar => _removed;
    public int FramesSoFar => _frames;

    /// Total events begun and completed. A rig asserts these move in lockstep:
    /// one drop per detonation, never two, never none.
    public long EventsBegun { get; private set; }
    public long EventsCompleted { get; private set; }

    public DestructionReducer(EditService edits, IWorldQuery world)
    {
        _edits = edits ?? throw new ArgumentNullException(nameof(edits));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// Starts a spherical detonation. Throws if one is already running --
    /// overlapping events would interleave their tallies and produce two drops
    /// for one blast, or one drop for two.
    public void Detonate(int3 centreVoxel, int radiusVoxels, byte replaceWith = Materials.Air)
    {
        if (_active)
            throw new InvalidOperationException(
                "a destruction event is already in progress; drain it with Step() before " +
                "starting another, or the two events' tallies interleave");
        if (radiusVoxels < 0) throw new ArgumentOutOfRangeException(nameof(radiusVoxels));

        _active = true;
        _centre = centreVoxel;
        _radius = radiusVoxels;
        _replaceWith = replaceWith;
        _lo = centreVoxel - new int3(radiusVoxels, radiusVoxels, radiusVoxels);
        _hi = centreVoxel + new int3(radiusVoxels, radiusVoxels, radiusVoxels);
        _cx = _lo.x; _cy = _lo.y; _cz = _lo.z;
        _removed = 0;
        _frames = 0;
        _tally = new int[256];
        _completed = null;
        EventsBegun++;
    }

    /// Processes up to `voxelBudget` cells of the in-flight event. Returns how
    /// many were actually removed this call. Call once per frame until
    /// InProgress goes false.
    ///
    /// WHAT THE BUDGET CHARGES FOR, precisely, because two obvious answers are
    /// both wrong:
    ///
    ///   NOT cells REMOVED. A blast through mostly-empty air removes nothing
    ///   and would run the entire volume in one frame while "under budget" --
    ///   the split would stop working on exactly the large sparse blast where
    ///   it matters most.
    ///
    ///   NOT cells in the BOUNDING BOX either. A sphere fills only pi/6 (~52%)
    ///   of its box, so charging for the box makes §13's 400K event cost
    ///   ~804,000 units and take 6 frames against a budget sized for 400,000.
    ///   That is what a first version of this did, and the acceptance test
    ///   caught it at 7 frames against §13's "<=3".
    ///
    /// It charges for cells INSIDE THE SPHERE, whether or not they turned out
    /// to be solid -- those are the ones that cost a GetVoxel and possibly a
    /// SetVoxel. A cell outside the sphere is three multiplies and no memory
    /// access, so it is skipped uncharged. That keeps "400K event" meaning the
    /// 400K voxels §13 is talking about, while still splitting an all-air blast.
    public int Step(int voxelBudget = DefaultVoxelsPerFrame)
    {
        if (!_active) return 0;
        if (voxelBudget <= 0) return 0;

        _frames++;
        int examined = 0;
        int removedThisStep = 0;

        for (; _cz <= _hi.z; _cz++, _cy = _lo.y)
        {
            for (; _cy <= _hi.y; _cy++, _cx = _lo.x)
            {
                for (; _cx <= _hi.x; _cx++)
                {
                    // The SHARED brush shape, so a detonation and a mining
                    // brush of equal radius cover exactly the same cells.
                    // Tested BEFORE the budget check and NOT charged: a cell
                    // outside the sphere costs three multiplies and no memory
                    // access at all.
                    int dx = _cx - _centre.x, dy = _cy - _centre.y, dz = _cz - _centre.z;
                    if (!FluidGpuSimulation.SphereCovers(dx, dy, dz, _radius)) continue;

                    if (examined >= voxelBudget)
                        return removedThisStep;             // resume at this cell next frame

                    examined++;
                    int3 v = new int3(_cx, _cy, _cz);
                    byte was = _world.GetVoxel(v);
                    if (was == _replaceWith) continue;      // already gone

                    // §8.3's path, minus the per-voxel wake scan; the region is
                    // notified once on completion. See WriteVoxelDeferred.
                    if (!_edits.WriteVoxelDeferred(v, _replaceWith)) continue;

                    _tally[was]++;
                    _removed++;
                    removedThisStep++;
                }
            }
        }

        Finish();
        return removedThisStep;
    }

    /// Drains the whole event regardless of budget. For tests and for a caller
    /// that genuinely wants it done now.
    public ProxyDrop DetonateNow(int3 centreVoxel, int radiusVoxels, byte replaceWith = Materials.Air)
    {
        Detonate(centreVoxel, radiusVoxels, replaceWith);
        while (_active) Step(int.MaxValue);
        ProxyDrop d;
        if (!TryTakeCompleted(out d))
            throw new InvalidOperationException("event drained but produced no Proxy Drop");
        return d;
    }

    private void Finish()
    {
        // ONE wake scan for the whole blast, and it is not optional: without it
        // fluid at the edge of a breach never re-promotes and a detonation that
        // opens a lake does nothing. Per-voxel scanning would be ~27 probes x
        // 400K cells for one event.
        _edits.NotifyEditedRegion(_lo, _hi);

        byte dominant = Materials.Air;
        int best = 0;
        for (int m = 0; m < _tally.Length; m++)
            if (m != Materials.Air && _tally[m] > best) { best = _tally[m]; dominant = (byte)m; }

        _completed = new ProxyDrop
        {
            CentreVoxel = _centre,
            CentreM = new float3(_centre.x, _centre.y, _centre.z) * VoxelCollision.VoxelSizeM,
            TotalVoxels = _removed,
            Frames = _frames,
            Tally = _tally,
            DominantMaterial = dominant,
        };
        _active = false;
        EventsCompleted++;
    }

    /// Hands over the drop for the event that just finished, exactly once.
    /// §8.5: ONE proxy drop per blast, regardless of size.
    public bool TryTakeCompleted(out ProxyDrop drop)
    {
        if (_completed.HasValue)
        {
            drop = _completed.Value;
            _completed = null;
            return true;
        }
        drop = default;
        return false;
    }

    /// How many voxels a radius-r sphere covers -- what a caller sizes its
    /// budget against, and what §13's "400K" refers to.
    ///
    /// Uses the same containment rule as every other sphere brush in the engine
    /// so a detonation and a mining brush of equal radius agree about their own
    /// extent.
    public static int SphereVoxelCount(int radius)
    {
        int n = 0;
        for (int z = -radius; z <= radius; z++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
            if (FluidGpuSimulation.SphereCovers(x, y, z, radius)) n++;
        return n;
    }

    /// The smallest radius whose sphere covers at least `voxels` cells. Used to
    /// build §13's 400K event without hardcoding a magic radius.
    public static int RadiusForAtLeast(int voxels)
    {
        for (int r = 1; r < 512; r++)
            if (SphereVoxelCount(r) >= voxels) return r;
        return 511;
    }
}
