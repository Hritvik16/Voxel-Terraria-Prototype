// ==========================================
// Assets/CoreEngine/Gameplay/SweptCCD.cs
//
// §13 Phase 6, file 2 of 6: "swept pass + depenetration backstop (§8.2), both
// vs CPU terrain".
//
// =========================================================================
// THE THREE THINGS §8.2 ASKS FOR
// =========================================================================
// 1. PRIMARY CCD (SWEPT). "above ~18 m/s a swept pass runs before integration
//    -- a 3x3 DDA ray bundle traces PositionPrev -> PositionNext against CPU
//    terrain (GetVoxel); first solid hit clamps to the impact plane."
//
// 2. FLUID PASS-THROUGH ACCUMULATION. "the swept pass clamps only on *solid*,
//    so at 60 m/s (1m/frame, 10 voxels) a player crossing a thin (e.g. 3-voxel)
//    water or lava layer between frames would phase through with no splash,
//    drag, or damage -- the probes sample only the endpoints, both dry. Fix:
//    the same DDA sweep accumulates the continuous distance traveled *inside*
//    fluid voxels and the primary fluid material encountered, returned
//    alongside the solid hit." That is CCDResult below, field for field.
//
// 3. DEPENETRATION BACKSTOP. "each frame after PhysX integration... any cage
//    voxel in solid => binary-search back along the movement vector to the last
//    free position, zero the offending velocity component. A silent permanent
//    tunnel is impossible." PhysX is not involved here (§8.1's fork went the
//    probe way), but the backstop is unchanged in substance: it runs after
//    integration, it binary-searches, and it is the thing that makes a missed
//    ray survivable.
//
// =========================================================================
// HOW THIS RELATES TO PlayerMotor's SUBSTEPPING
// =========================================================================
// PlayerMotor already splits a frame so no step exceeds half a voxel, which
// makes tunneling structurally impossible for the speeds IT produces. That does
// NOT make this file redundant, and it is worth being precise about why:
//   * At 60 m/s a frame is 10 voxels, so substepping costs ~20 full-body
//     overlap tests. A 3x3 ray bundle is 9 DDA walks. The sweep is the cheaper
//     shape at grapple speed, which is the speed §13 actually tests.
//   * Substepping resolves collisions but accumulates NOTHING. The fluid
//     traversal distance and material in (2) cannot be recovered from it -- a
//     body that substepped through a water sheet stops correctly and reports
//     nothing, which is precisely the silent phase-through §8.2 names.
//   * The backstop in (3) catches a bad state whatever produced it, including a
//     bug in either of the other two.
//
// WHAT IS NOT IN THIS FILE, DELIBERATELY: §8.2's SPEED CLAMP is written here as
// a pure policy function (SpeedClampMps) and unit-tested, but it is NOT WIRED to
// anything. The signal it clamps on is op-list readback staleness, and the
// consumer that cares is buoyancy/fluid contact -- §13's file 6. Wiring a
// clamp now, to a staleness source nothing yet reads, would be an unverified
// coupling of the kind this project has repeatedly paid for. The policy is here
// and proven; the wiring belongs with its consumer.

using Unity.Mathematics;

/// §8.2's named result struct, plus the bookkeeping a caller needs to act on it.
public struct CCDResult
{
    /// Did the sweep meet solid terrain (or a non-resident chunk)?
    public bool HitSolid;

    /// Distance along the sweep, in metres, at which the first solid was met.
    /// Only meaningful when HitSolid. §8.2's `solidHitDistance`.
    public float SolidHitDistanceM;

    /// Continuous distance travelled INSIDE fluid voxels, in metres, whether or
    /// not a solid was hit. §8.2's `fluidTraversedDistance`; > 0 is what fires a
    /// splash, drag or damage even when both endpoints are dry.
    public float FluidTraversedDistanceM;

    /// The fluid the body spent most of that distance in. §8.2's
    /// `primaryFluidMaterial`. Materials.Air when no fluid was crossed.
    public byte PrimaryFluidMaterial;

    /// The voxel that stopped the sweep, when HitSolid.
    public int3 HitVoxel;

    /// True when the sweep was stopped by an UNLOADED chunk rather than by
    /// terrain. Same distinction PlayerMotor draws: a streaming problem must
    /// not be able to masquerade as a collision.
    public bool HitWasNonResident;

    public static CCDResult None => new CCDResult { PrimaryFluidMaterial = Materials.Air };
}

public sealed class SweptCCD
{
    /// §8.2: "above ~18 m/s a swept pass runs before integration". Below this a
    /// caller can skip the sweep entirely; PlayerMotor's substepping already
    /// covers that range at lower cost.
    public const float SweepThresholdMps = 18f;

    /// §8.2's clamp target: "A speed clamp (to ~20 m/s, matching the original
    /// v7/pre-8.1-v8 pattern) engages if op-list readback stalls beyond ~2-3
    /// frames".
    public const float ClampedSpeedMps = 20f;
    public const int StaleFramesBeforeClamp = 3;

    private readonly IWorldQuery _world;
    private readonly IVoxelResidency _residency;

    /// Diagnostics. Rigs read these; nothing branches on them.
    public long SweepsRun;
    public long SweepsThatHitSolid;
    public long DepenetrationsApplied;
    public int LastDepenetrationIterations;

    public SweptCCD(IWorldQuery world, IVoxelResidency residency)
    {
        _world = world;
        _residency = residency;
    }

    // =====================================================================
    // §8.2's speed clamp -- POLICY ONLY, not wired. See the header.
    // =====================================================================

    /// The speed a body should be limited to given how many frames it has been
    /// since the fluid op-list last applied. Returns `requested` while the
    /// readback is healthy, ClampedSpeedMps once it has stalled.
    ///
    /// The clamp exists because both CCD defenses read CPU terrain, which is
    /// exact for solids but bounded-stale for fluid (§7.2, §8.2). Slowing down
    /// shrinks the distance a body can cover inside that staleness window.
    public static float SpeedClampMps(float requestedMps, int framesSinceLastApplied)
    {
        if (framesSinceLastApplied < StaleFramesBeforeClamp) return requestedMps;
        return math.min(requestedMps, ClampedSpeedMps);
    }

    // =====================================================================
    // 1 + 2. The swept pass
    // =====================================================================

    /// Traces a body's AABB from `fromFeetM` to `toFeetM` with a 3x3 ray bundle
    /// and reports the first solid hit and the fluid crossed on the way.
    ///
    /// THE BUNDLE. Nine rays on the cross-section perpendicular to travel: the
    /// four corners, four edge midpoints and the centre of the leading face,
    /// each inset by a skin so a ray does not graze the neighbouring voxel of a
    /// surface the body is already flush against. Nine is §8.2's number, and it
    /// is a SAMPLE, not a proof -- a spike thinner than half the body's width
    /// can pass between two rays. That is what the backstop below is for, and
    /// §13 tests exactly that case ("force a missed ray -> depenetration catches
    /// it").
    public CCDResult Sweep(float3 fromFeetM, float3 toFeetM, float widthM, float heightM)
    {
        SweepsRun++;
        var result = CCDResult.None;

        float3 delta = toFeetM - fromFeetM;
        float dist = math.length(delta);
        if (dist <= 1e-9f) return result;

        // Which face leads determines which plane the 3x3 grid spans.
        float3 a = math.abs(delta);
        int major = a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);

        float half = widthM * 0.5f;
        float inset = math.min(VoxelCollision.SkinM * 10f, half * 0.5f);

        // Offsets from the feet-centre for each sampled point, in the two axes
        // that are not the direction of travel.
        Span3 uAxis, vAxis;
        float3 lead;
        GetCrossSectionAxes(major, delta[major] > 0f, half, heightM, inset,
                            out uAxis, out vAxis, out lead);

        float bestSolid = float.PositiveInfinity;
        int3 bestVoxel = default;
        bool bestNonResident = false;
        bool hit = false;

        // Fluid is accumulated per material and the winner reported, so a sweep
        // clipping the corner of a lava pool on its way through water reports
        // the one it actually spent the distance in.
        // PER RAY, THEN THE MAX -- not one shared tally. Nine rays through the
        // same water sheet each measure the same crossing, so summing them
        // reported nine times the distance the body actually travelled wet.
        // The max is the right reducer: it is the deepest any part of the body
        // went, so a sheet clipped by one corner still fires, and a sheet
        // crossed squarely reports its true thickness.
        float bestFluidDist = 0f;
        byte bestFluidMat = Materials.Air;

        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            float3 offset = lead + uAxis.At(i) + vAxis.At(j);
            float3 p0 = fromFeetM + offset;
            float3 p1 = toFeetM + offset;

            float rayHitT;
            int3 rayVoxel;
            bool rayNonResident;
            var rayFluid = new FluidTally();
            bool rayHit = TraceRay(p0, p1, ref rayFluid,
                                   out rayHitT, out rayVoxel, out rayNonResident);
            if (rayHit && rayHitT < bestSolid)
            {
                bestSolid = rayHitT;
                bestVoxel = rayVoxel;
                bestNonResident = rayNonResident;
                hit = true;
            }

            if (rayFluid.Total > bestFluidDist)
            {
                bestFluidDist = rayFluid.Total;
                byte dominant;
                float dominantDist;
                rayFluid.Best(out dominant, out dominantDist);
                bestFluidMat = dominant;
            }
        }

        result.HitSolid = hit;
        result.SolidHitDistanceM = hit ? bestSolid : dist;
        result.HitVoxel = bestVoxel;
        result.HitWasNonResident = bestNonResident;
        result.FluidTraversedDistanceM = bestFluidDist;
        result.PrimaryFluidMaterial = bestFluidMat;
        if (hit) SweepsThatHitSolid++;
        return result;
    }

    /// Where the body should be placed given a sweep result: at the impact
    /// plane if it hit, at the requested destination if it did not.
    ///
    /// Backs off by a skin so the body rests just short of the surface rather
    /// than exactly on it, which would read as overlapping next frame.
    public static float3 ClampToHit(float3 fromFeetM, float3 toFeetM, CCDResult r)
    {
        if (!r.HitSolid) return toFeetM;
        float3 delta = toFeetM - fromFeetM;
        float dist = math.length(delta);
        if (dist <= 1e-9f) return fromFeetM;
        float travel = math.max(0f, r.SolidHitDistanceM - VoxelCollision.SkinM);
        return fromFeetM + delta / dist * travel;
    }

    // =====================================================================
    // 3. Depenetration backstop
    // =====================================================================

    /// Runs AFTER integration. If the body ended up inside solid, binary-searches
    /// back along the movement vector to the last free position and zeroes the
    /// offending velocity component.
    ///
    /// Returns true if it had to correct anything.
    ///
    /// §8.2: "A silent permanent tunnel is impossible." The word doing the work
    /// is SILENT -- this does not claim a body never enters solid, it claims it
    /// never STAYS there without the correction being counted
    /// (DepenetrationsApplied) and the velocity along the offending axis killed.
    ///
    /// If NO position along the segment is free -- the body was already inside
    /// solid before it moved, e.g. terrain was edited around it -- the search
    /// cannot help, and it falls back to lifting straight up, the same escape
    /// PlayerMotor.ResolveSpawn uses. Refusing to move is not an option here:
    /// the body is already somewhere illegal.
    public bool Depenetrate(float3 lastKnownFreeM, ref float3 posM, ref float3 velMps,
                            float widthM, float heightM, int iterations = 20)
    {
        LastDepenetrationIterations = 0;
        if (!VoxelCollision.OverlapsSolid(_world, _residency, posM, widthM, heightM)) return false;

        float3 from = lastKnownFreeM, to = posM;

        if (VoxelCollision.OverlapsSolid(_world, _residency, from, widthM, heightM))
        {
            // Nowhere on the segment is safe. Escape upward instead.
            for (int i = 1; i <= 64; i++)
            {
                float3 up = posM + new float3(0f, i * VoxelCollision.VoxelSizeM, 0f);
                if (!VoxelCollision.OverlapsSolid(_world, _residency, up, widthM, heightM))
                {
                    posM = up;
                    velMps = float3.zero;
                    DepenetrationsApplied++;
                    LastDepenetrationIterations = i;
                    return true;
                }
            }
            return false;                      // genuinely entombed; caller's problem
        }

        // Binary search for the last free point on [from, to].
        float lo = 0f, hi = 1f;
        for (int i = 0; i < iterations; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float3 p = math.lerp(from, to, mid);
            if (VoxelCollision.OverlapsSolid(_world, _residency, p, widthM, heightM)) hi = mid;
            else lo = mid;
            LastDepenetrationIterations++;
        }

        float3 corrected = math.lerp(from, to, lo);
        float3 moved = to - from;

        // Zero the velocity along the axis the body was moving furthest in --
        // "the offending velocity component". Keeping it would drive the body
        // straight back into the surface next frame.
        float3 am = math.abs(moved);
        int axis = am.x >= am.y && am.x >= am.z ? 0 : (am.y >= am.z ? 1 : 2);
        velMps[axis] = 0f;

        posM = corrected;
        DepenetrationsApplied++;
        return true;
    }

    // =====================================================================
    // DDA
    // =====================================================================

    /// Amanatides & Woo voxel traversal from p0 to p1.
    ///
    /// Accumulates fluid distance per material into `tally` as it goes, and
    /// stops at the first blocking voxel. Distances are in metres along the
    /// segment.
    private bool TraceRay(float3 p0, float3 p1, ref FluidTally tally,
                          out float hitT, out int3 hitVoxel, out bool nonResident)
    {
        hitT = 0f;
        hitVoxel = default;
        nonResident = false;

        float3 d = p1 - p0;
        float len = math.length(d);
        if (len <= 1e-9f) return false;
        float3 dir = d / len;

        const float s = VoxelCollision.VoxelSizeM;
        int3 v = CoordMath.WorldToVoxel(p0);

        int3 step = new int3(dir.x > 0f ? 1 : -1, dir.y > 0f ? 1 : -1, dir.z > 0f ? 1 : -1);

        // Distance along the ray to the next voxel boundary on each axis, and
        // the distance between successive boundaries.
        float3 tDelta = default, tMax = default;
        for (int k = 0; k < 3; k++)
        {
            if (math.abs(dir[k]) < 1e-12f)
            {
                tDelta[k] = float.PositiveInfinity;
                tMax[k] = float.PositiveInfinity;
            }
            else
            {
                tDelta[k] = math.abs(s / dir[k]);
                float boundary = (v[k] + (step[k] > 0 ? 1 : 0)) * s;
                tMax[k] = (boundary - p0[k]) / dir[k];
            }
        }

        float t = 0f;
        // A generous but finite bound: the ray cannot visit more cells than its
        // length in voxels on all three axes, plus slack.
        int maxCells = (int)(len / s) * 3 + 8;

        for (int guard = 0; guard < maxCells; guard++)
        {
            if (VoxelCollision.IsBlocking(_world, _residency, v))
            {
                hitT = t;
                hitVoxel = v;
                nonResident = VoxelCollision.IsNonResident(_residency, v);
                return true;
            }

            // Advance to the next cell.
            int axis = tMax.x < tMax.y ? (tMax.x < tMax.z ? 0 : 2) : (tMax.y < tMax.z ? 1 : 2);
            float tNext = math.min(tMax[axis], len);

            byte fluid;
            if (VoxelCollision.IsFluid(_world, _residency, v, out fluid))
                tally.Add(fluid, tNext - t);

            if (tMax[axis] >= len) return false;      // reached the destination

            t = tMax[axis];
            v[axis] += step[axis];
            tMax[axis] += tDelta[axis];
        }
        return false;
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    /// Three offsets along one axis: -extent, 0, +extent.
    public struct Span3
    {
        public float3 A, B, C;
        public float3 At(int i) => i == 0 ? A : (i == 1 ? B : C);
    }

    /// The nine sample points for a sweep along `major`.
    ///
    /// The rays start on the LEADING FACE of the AABB, not at its centre: for a
    /// pure translation that face is the only part of the body entering new
    /// space, so sweeping it is both sufficient and the cheapest sufficient
    /// thing. `lead` is the offset from the feet-centre to that face; u and v
    /// span the cross-section perpendicular to travel.
    ///
    /// An earlier version folded the body's vertical extent into the horizontal
    /// span for vertical travel, which coupled X and Y and left the 3x3 grid
    /// sampling a diagonal rather than a cross-section. The three cases are
    /// written out separately here precisely because that mistake is easy to
    /// make and silent when made.
    private static void GetCrossSectionAxes(int major, bool positive, float half, float heightM,
                                            float inset, out Span3 u, out Span3 v, out float3 lead)
    {
        float yLo = inset, yMid = heightM * 0.5f, yHi = heightM - inset;
        float wLo = -half + inset, wHi = half - inset;

        u = default; v = default; lead = float3.zero;

        if (major == 0)                       // along X: cross-section is Y-Z
        {
            lead = new float3(positive ? half : -half, 0f, 0f);
            u.A = new float3(0f, yLo, 0f);  u.B = new float3(0f, yMid, 0f);  u.C = new float3(0f, yHi, 0f);
            v.A = new float3(0f, 0f, wLo);  v.B = float3.zero;               v.C = new float3(0f, 0f, wHi);
        }
        else if (major == 1)                  // along Y: cross-section is X-Z
        {
            // Feet lead going down, head leads going up.
            lead = new float3(0f, positive ? heightM : 0f, 0f);
            u.A = new float3(wLo, 0f, 0f);  u.B = float3.zero;               u.C = new float3(wHi, 0f, 0f);
            v.A = new float3(0f, 0f, wLo);  v.B = float3.zero;               v.C = new float3(0f, 0f, wHi);
        }
        else                                  // along Z: cross-section is X-Y
        {
            lead = new float3(0f, 0f, positive ? half : -half);
            u.A = new float3(wLo, 0f, 0f);  u.B = float3.zero;               u.C = new float3(wHi, 0f, 0f);
            v.A = new float3(0f, yLo, 0f);  v.B = new float3(0f, yMid, 0f);  v.C = new float3(0f, yHi, 0f);
        }
    }

    /// Fixed-size tally of fluid distance by material. No allocation, and no
    /// dictionary in a per-frame path.
    public struct FluidTally
    {
        private byte _m0, _m1, _m2;
        private float _d0, _d1, _d2;

        public void Add(byte material, float distance)
        {
            if (distance <= 0f) return;
            if (_d0 == 0f || _m0 == material) { _m0 = material; _d0 += distance; return; }
            if (_d1 == 0f || _m1 == material) { _m1 = material; _d1 += distance; return; }
            if (_d2 == 0f || _m2 == material) { _m2 = material; _d2 += distance; return; }
            // A fourth fluid in one sweep is not a case worth carrying state
            // for; charge it to the leader so the total distance stays honest.
            _d0 += distance;
        }

        public void Best(out byte material, out float distance)
        {
            material = _m0; distance = _d0;
            if (_d1 > distance) { material = _m1; distance = _d1; }
            if (_d2 > distance) { material = _m2; distance = _d2; }
            if (distance <= 0f) material = Materials.Air;
        }

        /// Total across every material, for a caller that wants "how long was I
        /// in anything at all".
        public float Total => _d0 + _d1 + _d2;
    }
}
