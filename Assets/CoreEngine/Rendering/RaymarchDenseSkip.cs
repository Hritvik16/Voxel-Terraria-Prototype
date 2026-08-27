// Assets/CoreEngine/Rendering/RaymarchDenseSkip.cs
//
// Amendment 8.9/8.10 - dense-brick mip-reprobe skip.
//
// THE FINDING THIS FIXES: the mineshaft diagnostic (this session) measured
// 18 of 19 outer steps as dense per-voxel micro-steps, each one re-running
// the FULL top-down 4-level air-mip probe before falling through to the
// dense branch - even though the ray was already confirmed inside a dense
// brick and that probe is guaranteed to fail every time until the ray
// actually leaves the brick. The GenerateChunk.cs per-voxel-height fix
// (this session) made this the DEFAULT case along the entire terrain
// surface, not just inside one hand-dug tunnel - every mixed brick at the
// surface now pays this cost on every voxel step, which is why the
// resolution-ladder numbers went up 4x+ after that fix landed.
//
// THE FIX: once a dense brick is entered, stay in a dedicated inner loop
// that steps voxel-by-voxel WITHOUT re-probing the air-mip, until the ray
// actually leaves that brick's bounds. Only then does control return to the
// outer loop, where the mip probe runs again - now against a position where
// the answer can genuinely be different.
//
// WHY THIS IS SAFE - a redundant-check elimination, not a new algorithm:
// while voxel stays inside the SAME dense brick, the air-mip probe's answer
// cannot change. A cell containing a dense (mixed-material) brick is, by
// AirMip's own construction (ReduceFromL0/ReduceFromChild - a cell is air
// iff EVERY covered child is air), not air at ANY level while any part of
// that brick is occupied by this dense handle. So every mip-probe call the
// original code made while stuck inside a known dense brick was doing real
// work to produce an answer that could never change. Skipping those reads
// changes nothing about which voxels get visited, which one produces the
// hit, or what normal/material comes back - it only removes reads that were
// always going to say "not air" anyway.
//
// Same correctness bar as every other tracer in this file: full
// differential fuzz against TracerRaycast (the oracle), final hit/voxel/
// material/normal agreement only - see RaymarchDenseSkipTests.cs.

using Unity.Mathematics;
using VoxelEngine.Memory;

public static partial class RaymarchReference
{
    public static RayHit TracerRaycastMipReseedClosedFormDenseSkip(float3 origin, float3 dir, ChunkStore store,
        AirMipData mips, float maxDist = 128f)
    {
        var (hit, _, _) = TracerRaycastMipReseedClosedFormDenseSkipWithCounts(origin, dir, store, mips, maxDist);
        return hit;
    }

    // Counted variant - exposes how many times the top-down mip probe
    // actually ran (mipProbeCalls) versus how many voxel steps were taken
    // inside the dense-skip inner loop without one (denseSkipSteps). This is
    // the direct proof the optimization fires: on a dense-heavy ray,
    // denseSkipSteps should be large and mipProbeCalls should be small,
    // where the un-optimized ClosedForm tracer would have run the full
    // probe once per voxel step instead.
    public static (RayHit hit, int mipProbeCalls, int denseSkipSteps) TracerRaycastMipReseedClosedFormDenseSkipWithCounts(
        float3 origin, float3 dir, ChunkStore store, AirMipData mips, float maxDist = 128f)
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
        int mipProbeCalls = 0;
        int denseSkipSteps = 0;

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
                return (result, mipProbeCalls, denseSkipSteps);
            }

            mipProbeCalls++;
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

            // --- DENSE SKIP: not uniform air, and the check above already
            // found this exact position is air, so this is a genuinely
            // mixed/dense brick. Step voxel by voxel WITHOUT re-probing the
            // air-mip, until the ray actually leaves this brick's bounds. ---
            while (currentDist < maxVoxelDist && result.steps < 1000)
            {
                StepDda(ref voxel, ref tMax, ref currentDist, tDelta, step, ref lastNormal);
                result.steps++;
                denseSkipSteps++;

                byte innerMat = store.GetVoxel(voxel);
                if (innerMat != 0)
                {
                    result.hit = true;
                    result.voxelCoord = voxel;
                    result.material = innerMat;
                    result.normal = lastNormal;
                    return (result, mipProbeCalls, denseSkipSteps);
                }

                if (!CoordMath.VoxelToBrick(voxel).Equals(brickCoord)) break; // left this brick
            }
        }

        return (result, mipProbeCalls, denseSkipSteps);
    }
}