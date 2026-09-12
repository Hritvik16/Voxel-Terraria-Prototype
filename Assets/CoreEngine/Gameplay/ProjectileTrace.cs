// ==========================================
// Assets/CoreEngine/Gameplay/ProjectileTrace.cs
//
// §13 Phase 6, file 4 of 6 (§8.4 Projectiles):
//   "Swept segments traced against CPU terrain via GetVoxel in a Burst job. No
//    GPU raycast service (v7 needed one only because terrain lived on the GPU)
//    -- deleted. A 2.5m/frame arrow can't tunnel a 0.1m wall; the DDA visits
//    every cell."
//
// The traversal is VoxelRayWalker, shared with §8.2's SweptCCD, and the "what
// is solid" rule is VoxelCollision, shared with both it and PlayerMotor. This
// file is the projectile-shaped question asked of them.
//
// =========================================================================
// THE BURST HALF IS NOT WIRED, AND HERE IS EXACTLY WHY
// =========================================================================
// §8.4 says "in a Burst job". The traversal is ready for it -- VoxelRayWalker
// is an unmanaged struct with no interfaces or class references, which is what
// Burst requires. The WORLD is not:
//
//     StructHeaders.cs:  public class Chunk { ... public BrickHandle[] bricks; }
//     ChunkStore:        a managed array of those Chunk objects
//
// ChunkStore.GetVoxel walks managed class instances and a managed array. Burst
// compiles only unmanaged data -- NativeArray and blittable structs -- so a job
// cannot read the world as it is currently laid out. Making it able to is a
// §3.2/§3.3 memory-layout change (a Burst-legal snapshot or a NativeArray chunk
// table), which is an architecture decision and not something a Phase 6
// gameplay file gets to make on its own.
//
// So: the ALGORITHM §8.4 specifies is implemented and proven, single-threaded,
// against the real CPU terrain. The JOB is not, and NO PERFORMANCE CLAIM IS
// MADE. When the layout question is settled, the walker moves into a job
// unchanged and only the world access changes.
//
// =========================================================================
// THE PlayerFeedback HOOK §8.4 IMPLIES DOES NOT EXIST YET
// =========================================================================
// §8.1 describes a "PlayerFeedback event bus (8.4 -- the missing gameplay-
// response plumbing)": PlayerFeedback.Emit(EventType, position, magnitude),
// called by EditService, Buoyancy and DestructionReducer "on the relevant
// moments (a block breaks, a splash occurs, A HIT LANDS)".
//
// There is no PlayerFeedback type anywhere in the project. This file does NOT
// create one -- an engine-wide event bus that three other systems are supposed
// to call into is not something to introduce as a side effect of the
// projectile file, and §8.1 places it as its own piece of plumbing. A hit is
// returned to the caller instead, which is where a bus would get its data from
// anyway. THE GAP IS REAL AND IS FLAGGED, not silently filled.

using Unity.Mathematics;

/// What a traced segment met.
public struct ProjectileHit
{
    /// Did the segment meet solid terrain (or an unloaded chunk)?
    public bool Hit;

    /// Distance along the segment, metres, at which it did. When Hit is false
    /// this is the full segment length, so a caller can advance by it either way.
    public float DistanceM;

    /// The voxel that stopped it, and what that voxel was made of.
    public int3 Voxel;
    public byte Material;

    /// The last free point before the hit, in metres -- where a projectile
    /// should come to rest, or where an impact effect belongs.
    public float3 PointM;

    /// True when the segment was stopped by an UNLOADED chunk rather than by
    /// terrain. Same distinction PlayerMotor and SweptCCD draw: a streaming
    /// stall must not be able to masquerade as an impact.
    public bool WasNonResident;

    /// Distance spent inside fluid, and the fluid most of it was spent in.
    /// Same semantics as §8.2's CCDResult: an arrow crossing a water sheet
    /// should be able to raise a splash even when it hits nothing.
    public float FluidTraversedDistanceM;
    public byte PrimaryFluidMaterial;

    public static ProjectileHit None(float lengthM, float3 endM) => new ProjectileHit
    {
        Hit = false,
        DistanceM = lengthM,
        PointM = endM,
        Material = Materials.Air,
        PrimaryFluidMaterial = Materials.Air,
    };
}

public sealed class ProjectileTrace
{
    /// §8.4's own worked example: "A 2.5m/frame arrow can't tunnel a 0.1m
    /// wall". Kept as a named constant because it is the figure the acceptance
    /// test is written against.
    public const float ReferenceArrowSpeedMpf = 2.5f;

    private readonly IWorldQuery _world;
    private readonly IVoxelResidency _residency;

    /// Diagnostics. Rigs read these; nothing branches on them.
    public long TracesRun;
    public long TracesThatHit;

    public ProjectileTrace(IWorldQuery world, IVoxelResidency residency)
    {
        _world = world;
        _residency = residency;
    }

    /// Traces a swept segment against CPU terrain and returns the first solid
    /// hit, plus any fluid crossed on the way.
    ///
    /// THE DDA VISITS EVERY CELL, which is the whole of §8.4's tunneling
    /// argument: the segment is walked cell by cell rather than sampled, so
    /// there is no step size for a thin wall to hide between. A projectile
    /// moving any distance in one frame is as safe as one moving a millimetre.
    public ProjectileHit Trace(float3 fromM, float3 toM)
    {
        TracesRun++;

        var walker = VoxelRayWalker.Create(fromM, toM);
        var tally = new SweptCCD.FluidTally();
        float3 dir = toM - fromM;
        float len = math.length(dir);
        if (len > 1e-9f) dir /= len;

        while (walker.MoveNext())
        {
            if (VoxelCollision.IsBlocking(_world, _residency, walker.Voxel))
            {
                TracesThatHit++;
                byte dominant;
                float dominantDist;
                tally.Best(out dominant, out dominantDist);

                // Back off by a skin so the resting point is OUTSIDE the voxel
                // that stopped it, not exactly on its face -- an impact effect
                // spawned exactly on the boundary renders inside the wall.
                float back = math.max(0f, walker.TEnter - VoxelCollision.SkinM);
                return new ProjectileHit
                {
                    Hit = true,
                    DistanceM = walker.TEnter,
                    Voxel = walker.Voxel,
                    Material = _world.GetVoxel(walker.Voxel),
                    PointM = fromM + dir * back,
                    WasNonResident = VoxelCollision.IsNonResident(_residency, walker.Voxel),
                    FluidTraversedDistanceM = tally.Total,
                    PrimaryFluidMaterial = dominant,
                };
            }

            byte fluid;
            if (VoxelCollision.IsFluid(_world, _residency, walker.Voxel, out fluid))
                tally.Add(fluid, walker.TExit - walker.TEnter);
        }

        var miss = ProjectileHit.None(walker.Length, toM);
        byte m2;
        float d2;
        tally.Best(out m2, out d2);
        miss.FluidTraversedDistanceM = tally.Total;
        miss.PrimaryFluidMaterial = m2;
        return miss;
    }

    /// Convenience for the common per-frame form: where a projectile is, how
    /// fast it is going, and how long the frame was.
    ///
    /// §13's solo-dev note for this phase: "Keep any provisional projectile
    /// motion purely visual until a trace confirms, so retro-correction never
    /// becomes desync." The authoritative answer is the returned hit; a caller
    /// that has already drawn the projectile somewhere should move it to
    /// PointM rather than the other way round.
    public ProjectileHit TraceMotion(float3 posM, float3 velocityMps, float dtSeconds)
        => Trace(posM, posM + velocityMps * dtSeconds);
}
