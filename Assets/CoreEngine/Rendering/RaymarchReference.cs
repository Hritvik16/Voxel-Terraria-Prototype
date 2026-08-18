using Unity.Mathematics;
using VoxelEngine.Memory;

// Three CPU DDA implementations that MUST agree on every ray:
//
//   TracerRaycast          - the proven per-voxel walker (Phase 2 oracle).
//   TracerRaycastMacroSkip - an INDEPENDENT macro-skip oracle (different code
//                            path from O1; not folded onto LeapSpan on purpose).
//   TracerRaycastO1        - the O(1) brick leap; the version ported to the
//                            shader. Uses the shared LeapSpan (B1).
//
//   TracerRaycastMip (B2)  - the air-mip pyramid tracer. Probes L4->L1 and
//                            crosses whole air CELLS via the SAME LeapSpan,
//                            falling through to the O1 L0 logic when no mip
//                            level is air. This is the CPU sibling the GPU mip
//                            traversal (Step 4) is diffed against.
//
// The oracle property: a correct macro-skip / O1 / mip tracer returns
// BIT-IDENTICAL results to the per-voxel walk (hit, voxel, material, normal),
// only visiting fewer voxels.
//
// -------------------------------------------------------------------------
//  THE SHARED LEAP (B1): LeapSpan(spanMin, spanVoxels) crosses an axis-aligned
//  uniform-air span in one shot. The O1 path calls it with spanVoxels=8 (a
//  brick); the mip tracer calls it with spanVoxels = 8<<k (a level-k cell). One
//  leap body, two callers, no drift. The EXIT axis lands by EXACT integer count
//  (no float derives a coordinate; §2.3 holds), so a C#-vs-Metal rounding
//  difference cannot hole the leap regardless of span size.
//
//  TWO TRUST MODELS (deliberate, do not blur):
//   - O1 honestly re-scans each brick (BrickIsUniformAir, all 512 voxels) before
//     leaping it. Its guarantee: "agrees with the walk," full stop.
//   - Mip TRUSTS the AirMip cell (built from clipmap handles, not re-scanned).
//     Its guarantee: "agrees with the walk GIVEN a correctly-built pyramid."
//     The pyramid's correctness is proven separately by AirMipTests. The two
//     guarantees compose; neither alone covers both.
// -------------------------------------------------------------------------
//
//  DIVERGENCE FIX ATTEMPT #3 (RESEED, this addition):
//
//  The inner-iteration diagnostic (GPU) found the exit-axis integer for-loop -
//  NOT the non-exit while-loops - is the real cost: up to exitCount iterations
//  (up to 128 for an L4 leap), all measured on one axis in the pathological
//  case. Two arithmetic collapses (multiply-based closed form, and a
//  tree-summation reduction) were tested directly against the loop in a
//  200,000-case float32 simulation and BOTH disagreed 87-88% of the time (up
//  to 32 ULP) - repeated addition is a specific, path-dependent float
//  computation that essentially nothing else reproduces bit-exactly. Chasing
//  bit-exact agreement with the loop's tMax is a dead end.
//
//  THE REFRAME: nothing in this codebase has ever required tMax to match
//  bit-exactly between implementations - every existing proof (O1 fuzz, Mip
//  fuzz) checks only the FINAL ray outcome (hit/voxel/material/normal) against
//  the oracle. So instead of matching the loop's accumulated tMax, this
//  variant ELIMINATES the exit-axis loop by RESEEDING tMax from the landed
//  integer voxel, using the EXACT SAME formula already used and trusted to
//  seed tMax at ray origin (TracerRaycast's own seed line). That formula is a
//  pure function of (voxel, rayStart, tDelta) - no accumulation, so no
//  accumulated-vs-single-step disagreement is even POSSIBLE. It is
//  mathematically exact (not an approximation): tMax.a always represents "t
//  measured from the ORIGINAL ray start to axis a's next boundary," which
//  holds at any voxel position, not just the starting one.
//
//  ONLY the exit axis is reseeded (that's the loop that was actually
//  expensive - the non-exit axes' tight-guarded loops are already cheap,
//  confirmed 0 iterations in the measured pathological case). Non-exit axes
//  are untouched, still the proven tight-guard loop.
//
//  Correctness bar: the SAME bar as every other tracer in this file - full
//  differential fuzz against TracerRaycast (the oracle), checking final
//  hit/voxel/material/normal agreement. See RaymarchMipReseedTests.cs.
// -------------------------------------------------------------------------
public static partial class RaymarchReference
{
    public struct RayHit
    {
        public bool hit;
        public int3 voxelCoord;
        public byte material;
        public int3 normal;
        public int steps;      // per-voxel: voxels visited. leap tracers: outer iterations.
    }

    // ---------------------------------------------------------------------
    //  Per-voxel walker (unchanged - the proven oracle).
    // ---------------------------------------------------------------------
    public static RayHit TracerRaycast(float3 origin, float3 dir, ChunkStore store, float maxDist = 128f)
    {
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;
        float currentDist = 0f;
        float maxVoxelDist = maxDist * 10f;

        while (currentDist < maxVoxelDist && result.steps < 1000)
        {
            result.steps++;
            byte mat = store.GetVoxel(voxel);
            if (mat != 0)
            {
                result.hit = true;
                result.voxelCoord = voxel;
                result.material = mat;
                return result;
            }

            if (tMax.x < tMax.y)
            {
                if (tMax.x < tMax.z)
                { voxel.x += step.x; currentDist = tMax.x; tMax.x += tDelta.x; result.normal = new int3(-step.x, 0, 0); }
                else
                { voxel.z += step.z; currentDist = tMax.z; tMax.z += tDelta.z; result.normal = new int3(0, 0, -step.z); }
            }
            else
            {
                if (tMax.y < tMax.z)
                { voxel.y += step.y; currentDist = tMax.y; tMax.y += tDelta.y; result.normal = new int3(0, -step.y, 0); }
                else
                { voxel.z += step.z; currentDist = tMax.z; tMax.z += tDelta.z; result.normal = new int3(0, 0, -step.z); }
            }
        }
        return result;
    }

    // ---------------------------------------------------------------------
    //  Macro-skip walker - INDEPENDENT oracle. UNTOUCHED.
    // ---------------------------------------------------------------------
    public static RayHit TracerRaycastMacroSkip(float3 origin, float3 dir, ChunkStore store, float maxDist = 128f)
    {
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);

        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;

        float currentDist = 0f;
        float maxVoxelDist = maxDist * 10f;
        int3 lastNormal = int3.zero;

        while (currentDist < maxVoxelDist && result.steps < 1000)
        {
            result.steps++;

            byte mat = store.GetVoxel(voxel);
            if (mat != 0)
            {
                result.hit = true;
                result.voxelCoord = voxel;
                result.material = mat;
                result.normal = lastNormal;
                return result;
            }

            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            if (BrickIsUniformAir(store, brickCoord))
            {
                int guard = 0;
                while (CoordMath.VoxelToBrick(voxel).Equals(brickCoord))
                {
                    StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
                    if (currentDist >= maxVoxelDist) break;
                    if (++guard >= 24) break; // unreachable for a straight ray; pure safety
                }
                continue;
            }

            StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
        }

        return result;
    }

    // One single-voxel DDA advance. The walk's transition; shared by macro-skip
    // and the O1/mip L0-dense fallback path.
    private static void StepDda(ref int3 voxel, ref float3 tMax, ref float currentDist,
        float3 tDelta, int3 step, ref int3 normal)
    {
        if (tMax.x < tMax.y)
        {
            if (tMax.x < tMax.z)
            { voxel.x += step.x; currentDist = tMax.x; tMax.x += tDelta.x; normal = new int3(-step.x, 0, 0); }
            else
            { voxel.z += step.z; currentDist = tMax.z; tMax.z += tDelta.z; normal = new int3(0, 0, -step.z); }
        }
        else
        {
            if (tMax.y < tMax.z)
            { voxel.y += step.y; currentDist = tMax.y; tMax.y += tDelta.y; normal = new int3(0, -step.y, 0); }
            else
            { voxel.z += step.z; currentDist = tMax.z; tMax.z += tDelta.z; normal = new int3(0, 0, -step.z); }
        }
    }

    // ---------------------------------------------------------------------
    //  O(1) LEAP walker - ported to Raymarch.compute. Uses shared LeapSpan.
    // ---------------------------------------------------------------------
    public static RayHit TracerRaycastO1(float3 origin, float3 dir, ChunkStore store, float maxDist = 128f)
    {
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;
        float currentDist = 0f;
        float maxVoxelDist = maxDist * 10f;
        int3 lastNormal = int3.zero;

        while (currentDist < maxVoxelDist && result.steps < 1000)
        {
            result.steps++;

            byte mat = store.GetVoxel(voxel);
            if (mat != 0)
            {
                result.hit = true;
                result.voxelCoord = voxel;
                result.material = mat;
                result.normal = lastNormal;
                return result;
            }

            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            if (BrickIsUniformAir(store, brickCoord))
            {
                LeapBrickO1(ref voxel, ref tMax, ref currentDist, tDelta, step, brickCoord, ref lastNormal);
                continue;
            }

            StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
        }

        return result;
    }

    // ---------------------------------------------------------------------
    //  AIR-MIP TRACER (B2) - the version whose logic the GPU mip traversal
    //  (Step 4) transcribes. Probes the pyramid top-down; on an air CELL at
    //  level k, crosses the whole cell via the shared LeapSpan with
    //  spanVoxels = 8<<k. When no mip level is air at the current position,
    //  falls through to the EXACT O1 L0 logic (uniform-air brick leap / uniform-
    //  solid hit / dense micro-step), unchanged.
    // ---------------------------------------------------------------------
    public static RayHit TracerRaycastMip(float3 origin, float3 dir, ChunkStore store,
        AirMipData mips, float maxDist = 128f)
    {
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;
        float currentDist = 0f;
        float maxVoxelDist = maxDist * 10f;
        int3 lastNormal = int3.zero;

        while (currentDist < maxVoxelDist && result.steps < 1000)
        {
            result.steps++;

            byte mat = store.GetVoxel(voxel);
            if (mat != 0)
            {
                result.hit = true;
                result.voxelCoord = voxel;
                result.material = mat;
                result.normal = lastNormal;
                return result;
            }

            bool leapt = false;
            for (int k = mips.NumLevels; k >= 1; k--)
            {
                int shift = 3 + k;
                int3 cell = voxel >> shift;
                int3 dims = mips.DimsOfLevel(k);
                if (mips.Levels[k - 1][AirMip.FlatIndex(cell, dims)] == 0u)
                {
                    int spanVoxels = 8 << k;
                    int3 spanMin = cell << shift;
                    LeapSpan(ref voxel, ref tMax, ref currentDist, tDelta, step,
                             spanMin, spanVoxels, ref lastNormal);
                    leapt = true;
                    break;
                }
            }
            if (leapt) continue;

            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            if (BrickIsUniformAir(store, brickCoord))
            {
                LeapBrickO1(ref voxel, ref tMax, ref currentDist, tDelta, step, brickCoord, ref lastNormal);
                continue;
            }

            StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
        }

        return result;
    }

    // The O(1) brick leap - thin wrapper over LeapSpan (spanVoxels = 8).
    private static void LeapBrickO1(ref int3 voxel, ref float3 tMax, ref float currentDist,
        float3 tDelta, int3 step, int3 brickCoord, ref int3 lastNormal)
    {
        int3 bMin = brickCoord * 8;
        LeapSpan(ref voxel, ref tMax, ref currentDist, tDelta, step, bMin, 8, ref lastNormal);
    }

    // THE ONE SHARED LEAP BODY (unchanged - still the proven loop-based version,
    // still what O1/TracerRaycastMip above call). NOT modified by the reseed
    // addition below - the reseed variant is a SEPARATE function so this proven
    // path is never at risk.
    private static void LeapSpan(ref int3 voxel, ref float3 tMax, ref float currentDist,
        float3 tDelta, int3 step, int3 spanMin, int spanVoxels, ref int3 lastNormal)
    {
        int3 exitCount = new int3(
            step.x > 0 ? (spanMin.x + spanVoxels - voxel.x) : (voxel.x - (spanMin.x - 1)),
            step.y > 0 ? (spanMin.y + spanVoxels - voxel.y) : (voxel.y - (spanMin.y - 1)),
            step.z > 0 ? (spanMin.z + spanVoxels - voxel.z) : (voxel.z - (spanMin.z - 1)));

        float tExitX = tMax.x + (exitCount.x - 1) * tDelta.x;
        float tExitY = tMax.y + (exitCount.y - 1) * tDelta.y;
        float tExitZ = tMax.z + (exitCount.z - 1) * tDelta.z;

        int axis;
        if (tExitX <= tExitY) axis = (tExitX <= tExitZ) ? 0 : 2;
        else                  axis = (tExitY <= tExitZ) ? 1 : 2;

        float tHit = (axis == 0) ? tExitX : (axis == 1) ? tExitY : tExitZ;

        int nonExitGuard = spanVoxels + 1;

        AdvanceExitAxis(ref voxel.x, ref tMax.x, tDelta.x, step.x, axis == 0 ? exitCount.x : -1);
        AdvanceExitAxis(ref voxel.y, ref tMax.y, tDelta.y, step.y, axis == 1 ? exitCount.y : -1);
        AdvanceExitAxis(ref voxel.z, ref tMax.z, tDelta.z, step.z, axis == 2 ? exitCount.z : -1);

        if (axis != 0) AdvanceNonExitAxis(ref voxel.x, ref tMax.x, tDelta.x, step.x, tHit, nonExitGuard);
        if (axis != 1) AdvanceNonExitAxis(ref voxel.y, ref tMax.y, tDelta.y, step.y, tHit, nonExitGuard);
        if (axis != 2) AdvanceNonExitAxis(ref voxel.z, ref tMax.z, tDelta.z, step.z, tHit, nonExitGuard);

        currentDist = tHit;
        lastNormal = (axis == 0) ? new int3(-step.x, 0, 0)
                   : (axis == 1) ? new int3(0, -step.y, 0)
                                 : new int3(0, 0, -step.z);
    }

    private static void AdvanceExitAxis(ref int axisVoxel, ref float axisTMax,
        float axisTDelta, int axisStep, int exactCount)
    {
        if (exactCount < 0) return;
        for (int k = 0; k < exactCount; k++)
        {
            axisVoxel += axisStep;
            axisTMax += axisTDelta;
        }
    }

    private static void AdvanceNonExitAxis(ref int axisVoxel, ref float axisTMax,
        float axisTDelta, int axisStep, float tHit, int guardMax)
    {
        const float eps = 1e-4f;
        int guard = 0;
        while (axisTMax <= tHit + eps && guard < guardMax)
        {
            axisVoxel += axisStep;
            axisTMax += axisTDelta;
            guard++;
        }
    }

    private static bool BrickIsUniformAir(ChunkStore store, int3 brickCoord)
    {
        int3 bMin = brickCoord * 8;
        for (int z = 0; z < 8; z++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (store.GetVoxel(bMin + new int3(x, y, z)) != 0)
                        return false;
        return true;
    }

    // =======================================================================
    //  DIVERGENCE FIX ATTEMPT #3 (RESEED) — new, additive, nothing above
    //  touched. See the file-header comment for the full derivation.
    // =======================================================================

    // The exit-axis's tMax reseed formula - IDENTICAL to the seed line every
    // tracer in this file uses at ray origin, just re-applied at the landed
    // voxel instead of the starting voxel. Pure function of (voxel, rayStart,
    // tDelta): no accumulation, so no accumulated-vs-single-step disagreement
    // is possible. Mathematically exact at any voxel position, not an
    // approximation valid only at t=0.
    private static float ReseedTMaxAxis(int voxelAxis, float rayStartAxis, float tDeltaAxis, int stepAxis)
    {
        return (stepAxis > 0 ? (voxelAxis + 1f - rayStartAxis) : (rayStartAxis - voxelAxis)) * tDeltaAxis;
    }

    // LeapSpanReseed: identical structure/inputs to LeapSpan, PLUS rayStart (the
    // ray's fixed original start position, needed for the reseed formula).
    // Non-exit axes: UNCHANGED, still the proven tight-guard loop (already
    // cheap - confirmed 0 iterations in the measured pathological case, so
    // there's nothing to gain by touching them, and every change to them is
    // unnecessary added risk).
    // Exit axis: voxel advances by the SAME exact integer count as before (no
    // change - integer math, always exact, never the cost). tMax is no longer
    // accumulated via a loop; it is RESEEDED via the formula above. This
    // eliminates the O(exitCount) loop entirely - O(1) replaces O(up to 128).
    // [FIX, v2] The original single-axis reseed made the exit axis PERFECTLY
    // accurate (zero accumulated float error) while the two non-exit axes kept
    // their normal small accumulated error from the loop. On near-diagonal rays
    // (axes nearly tied), that asymmetry - not a magnitude of error, but an
    // INCONSISTENCY of error between axes that used to share the same
    // accumulation process - was enough to flip a later tie-break, caught by
    // Reseed_Diagonal_AgreesWithOracle / Reseed_NegativeXYZ_Diagonal_AgreesWithOracle.
    //
    // FIX: reseed ALL THREE axes' tMax together, unconditionally, after voxel
    // is fully advanced on every axis. The non-exit axes' LOOP is still used to
    // determine how many voxel steps to take (that part was never wrong - the
    // loop's voxel output was always correct, only its carried tMax created the
    // asymmetry) - the loop's resulting tMax is simply discarded and replaced.
    // This puts all three axes back on the SAME footing: all reseeded, all
    // equally exact, matching the property that made pure accumulation
    // (all three axes equally error-laden) self-consistent in the first place.
    private static void LeapSpanReseed(ref int3 voxel, ref float3 tMax, ref float currentDist,
        float3 tDelta, int3 step, float3 rayStart, int3 spanMin, int spanVoxels, ref int3 lastNormal)
    {
        int3 exitCount = new int3(
            step.x > 0 ? (spanMin.x + spanVoxels - voxel.x) : (voxel.x - (spanMin.x - 1)),
            step.y > 0 ? (spanMin.y + spanVoxels - voxel.y) : (voxel.y - (spanMin.y - 1)),
            step.z > 0 ? (spanMin.z + spanVoxels - voxel.z) : (voxel.z - (spanMin.z - 1)));

        float tExitX = tMax.x + (exitCount.x - 1) * tDelta.x;
        float tExitY = tMax.y + (exitCount.y - 1) * tDelta.y;
        float tExitZ = tMax.z + (exitCount.z - 1) * tDelta.z;

        int axis;
        if (tExitX <= tExitY) axis = (tExitX <= tExitZ) ? 0 : 2;
        else                  axis = (tExitY <= tExitZ) ? 1 : 2;

        float tHit = (axis == 0) ? tExitX : (axis == 1) ? tExitY : tExitZ;

        int nonExitGuard = spanVoxels + 1;

        // EXIT AXIS: exact integer voxel advance. tMax NOT reseeded here
        // individually anymore - deferred to the unconditional reseed-all below.
        if (axis == 0) voxel.x += step.x * exitCount.x; // exact int, no float involved
        if (axis == 1) voxel.y += step.y * exitCount.y;
        if (axis == 2) voxel.z += step.z * exitCount.z;

        // NON-EXIT AXES: still use the proven tight-guard loop to determine how
        // far to advance voxel (this part was always correct). Its resulting
        // tMax will be overwritten below - only the VOXEL output is kept here.
        if (axis != 0) AdvanceNonExitAxis(ref voxel.x, ref tMax.x, tDelta.x, step.x, tHit, nonExitGuard);
        if (axis != 1) AdvanceNonExitAxis(ref voxel.y, ref tMax.y, tDelta.y, step.y, tHit, nonExitGuard);
        if (axis != 2) AdvanceNonExitAxis(ref voxel.z, ref tMax.z, tDelta.z, step.z, tHit, nonExitGuard);

        // RESEED ALL THREE AXES from the now-fully-correct integer voxel. This
        // is the fix: every axis ends this leap on the same "freshly, exactly
        // derived from true position" footing, eliminating the asymmetry that
        // caused the diagonal tie-break bug.
        tMax.x = ReseedTMaxAxis(voxel.x, rayStart.x, tDelta.x, step.x);
        tMax.y = ReseedTMaxAxis(voxel.y, rayStart.y, tDelta.y, step.y);
        tMax.z = ReseedTMaxAxis(voxel.z, rayStart.z, tDelta.z, step.z);

        currentDist = tHit;
        lastNormal = (axis == 0) ? new int3(-step.x, 0, 0)
                   : (axis == 1) ? new int3(0, -step.y, 0)
                                 : new int3(0, 0, -step.z);
    }

    private static void LeapBrickO1Reseed(ref int3 voxel, ref float3 tMax, ref float currentDist,
        float3 tDelta, int3 step, float3 rayStart, int3 brickCoord, ref int3 lastNormal)
    {
        int3 bMin = brickCoord * 8;
        LeapSpanReseed(ref voxel, ref tMax, ref currentDist, tDelta, step, rayStart, bMin, 8, ref lastNormal);
    }

    // TracerRaycastMipReseed: structurally IDENTICAL to TracerRaycastMip, the
    // only difference is calling LeapSpanReseed/LeapBrickO1Reseed instead of
    // LeapSpan/LeapBrickO1. This is the function RaymarchMipReseedTests.cs
    // fuzzes against the oracle (TracerRaycast) - the SAME correctness bar
    // every other tracer in this file is held to.
    public static RayHit TracerRaycastMipReseed(float3 origin, float3 dir, ChunkStore store,
        AirMipData mips, float maxDist = 128f)
    {
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;
        float currentDist = 0f;
        float maxVoxelDist = maxDist * 10f;
        int3 lastNormal = int3.zero;

        while (currentDist < maxVoxelDist && result.steps < 1000)
        {
            result.steps++;

            byte mat = store.GetVoxel(voxel);
            if (mat != 0)
            {
                result.hit = true;
                result.voxelCoord = voxel;
                result.material = mat;
                result.normal = lastNormal;
                return result;
            }

            bool leapt = false;
            for (int k = mips.NumLevels; k >= 1; k--)
            {
                int shift = 3 + k;
                int3 cell = voxel >> shift;
                int3 dims = mips.DimsOfLevel(k);
                if (mips.Levels[k - 1][AirMip.FlatIndex(cell, dims)] == 0u)
                {
                    int spanVoxels = 8 << k;
                    int3 spanMin = cell << shift;
                    LeapSpanReseed(ref voxel, ref tMax, ref currentDist, tDelta, step,
                             rayStart, spanMin, spanVoxels, ref lastNormal);
                    leapt = true;
                    break;
                }
            }
            if (leapt) continue;

            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            if (BrickIsUniformAir(store, brickCoord))
            {
                LeapBrickO1Reseed(ref voxel, ref tMax, ref currentDist, tDelta, step, rayStart, brickCoord, ref lastNormal);
                continue;
            }

            StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
        }

        return result;
    }

    // Exposes exit-axis-vs-non-exit-axis iteration counts for the reseed
    // variant, mirroring the GPU split diagnostic, so a CPU-side sanity check
    // of the work-saving claim is possible without a GPU readback.
    public static (RayHit hit, int exitIters, int nonExitIters) TracerRaycastMipReseedWithCounts(
        float3 origin, float3 dir, ChunkStore store, AirMipData mips, float maxDist = 128f)
    {
        // Re-implemented with counters rather than modifying the shipped-shape
        // function above, so the "what ships" function has zero extraneous state.
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;
        float currentDist = 0f;
        float maxVoxelDist = maxDist * 10f;
        int3 lastNormal = int3.zero;
        int exitItersTotal = 0; // exit axis is now O(1) per leap - count LEAPS, not loop iterations
        int nonExitItersTotal = 0;

        while (currentDist < maxVoxelDist && result.steps < 1000)
        {
            result.steps++;

            byte mat = store.GetVoxel(voxel);
            if (mat != 0)
            {
                result.hit = true;
                result.voxelCoord = voxel;
                result.material = mat;
                result.normal = lastNormal;
                return (result, exitItersTotal, nonExitItersTotal);
            }

            bool leapt = false;
            for (int k = mips.NumLevels; k >= 1; k--)
            {
                int shift = 3 + k;
                int3 cell = voxel >> shift;
                int3 dims = mips.DimsOfLevel(k);
                if (mips.Levels[k - 1][AirMip.FlatIndex(cell, dims)] == 0u)
                {
                    int spanVoxels = 8 << k;
                    int3 spanMin = cell << shift;
                    exitItersTotal += 1; // O(1) exit-axis work per leap now
                    LeapSpanReseed(ref voxel, ref tMax, ref currentDist, tDelta, step,
                             rayStart, spanMin, spanVoxels, ref lastNormal);
                    leapt = true;
                    break;
                }
            }
            if (leapt) continue;

            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            if (BrickIsUniformAir(store, brickCoord))
            {
                exitItersTotal += 1;
                LeapBrickO1Reseed(ref voxel, ref tMax, ref currentDist, tDelta, step, rayStart, brickCoord, ref lastNormal);
                continue;
            }

            StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
        }

        return (result, exitItersTotal, nonExitItersTotal);
    }

    // =======================================================================
    //  VARIANT A (CLOSED-FORM NON-EXIT-AXIS COUNT), Amendment 8.8/8.9 — new,
    //  additive, nothing above touched. See TightGuardTests.LoopCountClosedForm
    //  for the derivation and its own isolated fuzz proof (integer-count-only,
    //  no accumulation, so no accumulated-vs-single-step disagreement is
    //  possible - same reasoning that made the exit-axis reseed safe).
    //
    //  Structurally identical to LeapSpanReseed above (exit axis: exact int
    //  advance, tHit/axis selection unchanged) with ONE substitution: the
    //  non-exit axes' O(spanVoxels) tight-guard WHILE LOOP is replaced by the
    //  O(1) closed-form count, then reseeded via the SAME ReseedTMaxAxis used
    //  for the exit axis and used to seed every tracer's tMax at ray origin.
    //
    //  FLAGGED, NOT SILENTLY ABSORBED: because every axis this function
    //  touches ends up reseeded from its landed voxel (exit axis, as before;
    //  non-exit axes, newly), this function's behavior is structurally closer
    //  to this file's current TracerRaycastMipReseed (which reseeds all three
    //  axes, per its own [FIX, v2] comment above) than to what
    //  Raymarch.compute's mode-1 KERNEL currently does on GPU (which reseeds
    //  only the exit axis and leaves non-exit tMax loop-accumulated). That
    //  asymmetry in the shipped mode-1 kernel is a separate, pre-existing,
    //  UNFIXED item - not something this pass fixes or should be credited
    //  for fixing. It means a mode-1-vs-mode-3 GPU A/B is not a pure
    //  single-variable test of "loop vs closed form" - see session summary.
    // =======================================================================
    private static void LeapSpanReseedClosedForm(ref int3 voxel, ref float3 tMax, ref float currentDist,
        float3 tDelta, int3 step, float3 rayStart, int3 spanMin, int spanVoxels, ref int3 lastNormal)
    {
        int3 exitCount = new int3(
            step.x > 0 ? (spanMin.x + spanVoxels - voxel.x) : (voxel.x - (spanMin.x - 1)),
            step.y > 0 ? (spanMin.y + spanVoxels - voxel.y) : (voxel.y - (spanMin.y - 1)),
            step.z > 0 ? (spanMin.z + spanVoxels - voxel.z) : (voxel.z - (spanMin.z - 1)));

        float tExitX = tMax.x + (exitCount.x - 1) * tDelta.x;
        float tExitY = tMax.y + (exitCount.y - 1) * tDelta.y;
        float tExitZ = tMax.z + (exitCount.z - 1) * tDelta.z;

        int axis;
        if (tExitX <= tExitY) axis = (tExitX <= tExitZ) ? 0 : 2;
        else                  axis = (tExitY <= tExitZ) ? 1 : 2;

        float tHit = (axis == 0) ? tExitX : (axis == 1) ? tExitY : tExitZ;

        int nonExitGuard = spanVoxels + 1;

        // EXIT AXIS: unchanged from LeapSpanReseed - exact integer advance.
        if (axis == 0) voxel.x += step.x * exitCount.x;
        if (axis == 1) voxel.y += step.y * exitCount.y;
        if (axis == 2) voxel.z += step.z * exitCount.z;

        // NON-EXIT AXES: the substitution. Closed-form count instead of the
        // tight-guard while loop.
        if (axis != 0) AdvanceNonExitAxisClosedForm(ref voxel.x, step.x, tMax.x, tDelta.x, tHit, nonExitGuard);
        if (axis != 1) AdvanceNonExitAxisClosedForm(ref voxel.y, step.y, tMax.y, tDelta.y, tHit, nonExitGuard);
        if (axis != 2) AdvanceNonExitAxisClosedForm(ref voxel.z, step.z, tMax.z, tDelta.z, tHit, nonExitGuard);

        // Reseed every axis from the now-fully-correct landed voxel - same
        // formula used to seed tMax at ray origin, pure function of (voxel,
        // rayStart, tDelta, step), no accumulation.
        tMax.x = ReseedTMaxAxis(voxel.x, rayStart.x, tDelta.x, step.x);
        tMax.y = ReseedTMaxAxis(voxel.y, rayStart.y, tDelta.y, step.y);
        tMax.z = ReseedTMaxAxis(voxel.z, rayStart.z, tDelta.z, step.z);

        currentDist = tHit;
        lastNormal = (axis == 0) ? new int3(-step.x, 0, 0)
                   : (axis == 1) ? new int3(0, -step.y, 0)
                                 : new int3(0, 0, -step.z);
    }

    private static void AdvanceNonExitAxisClosedForm(ref int axisVoxel, int axisStep,
        float axisTMax, float axisTDelta, float tHit, int guardMax)
    {
        // Mirrors TightGuard.LoopCountClosedForm exactly (same formula, same
        // eps) - kept as a literal duplicate rather than a shared reference so
        // this file's reseed section stays self-contained (same pattern as
        // ReseedTMaxAxis's own duplication below), and so the two proofs
        // (TightGuardTests' integer-count fuzz, this file's ray-outcome fuzz)
        // stay independently checkable against independently-written code.
        const float eps = 1e-4f;
        float absDir = 1f / axisTDelta;
        int n = (int)System.MathF.Floor((tHit + eps - axisTMax) * absDir) + 1;
        n = System.Math.Clamp(n, 0, guardMax);
        axisVoxel += axisStep * n;
    }

    private static void LeapBrickO1ReseedClosedForm(ref int3 voxel, ref float3 tMax, ref float currentDist,
        float3 tDelta, int3 step, float3 rayStart, int3 brickCoord, ref int3 lastNormal)
    {
        int3 bMin = brickCoord * 8;
        LeapSpanReseedClosedForm(ref voxel, ref tMax, ref currentDist, tDelta, step, rayStart, bMin, 8, ref lastNormal);
    }

    // TracerRaycastMipReseedClosedForm: structurally identical to
    // TracerRaycastMipReseed, only difference is calling
    // LeapSpanReseedClosedForm/LeapBrickO1ReseedClosedForm instead of
    // LeapSpanReseed/LeapBrickO1Reseed. This is the function
    // RaymarchMipReseedTests.cs fuzzes against the oracle (TracerRaycast) -
    // the SAME correctness bar every other tracer in this file is held to.
    public static RayHit TracerRaycastMipReseedClosedForm(float3 origin, float3 dir, ChunkStore store,
        AirMipData mips, float maxDist = 128f)
    {
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;
        float currentDist = 0f;
        float maxVoxelDist = maxDist * 10f;
        int3 lastNormal = int3.zero;

        while (currentDist < maxVoxelDist && result.steps < 1000)
        {
            result.steps++;

            byte mat = store.GetVoxel(voxel);
            if (mat != 0)
            {
                result.hit = true;
                result.voxelCoord = voxel;
                result.material = mat;
                result.normal = lastNormal;
                return result;
            }

            bool leapt = false;
            for (int k = mips.NumLevels; k >= 1; k--)
            {
                int shift = 3 + k;
                int3 cell = voxel >> shift;
                int3 dims = mips.DimsOfLevel(k);
                if (mips.Levels[k - 1][AirMip.FlatIndex(cell, dims)] == 0u)
                {
                    int spanVoxels = 8 << k;
                    int3 spanMin = cell << shift;
                    LeapSpanReseedClosedForm(ref voxel, ref tMax, ref currentDist, tDelta, step,
                             rayStart, spanMin, spanVoxels, ref lastNormal);
                    leapt = true;
                    break;
                }
            }
            if (leapt) continue;

            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            if (BrickIsUniformAir(store, brickCoord))
            {
                LeapBrickO1ReseedClosedForm(ref voxel, ref tMax, ref currentDist, tDelta, step, rayStart, brickCoord, ref lastNormal);
                continue;
            }

            StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
        }

        return result;
    }
}