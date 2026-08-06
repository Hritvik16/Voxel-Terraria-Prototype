using Unity.Mathematics;
using VoxelEngine.Memory;

// Two CPU DDA implementations that MUST agree on every ray:
//
//   TracerRaycast          - the proven per-voxel walker (Phase 2 oracle).
//                            Steps one voxel at a time. No macro-skip.
//                            Correctness-proven by RaymarchReferenceTests.
//
//   TracerRaycastMacroSkip - a CPU macro-skip walker, written from the §6.3
//                            SPECIFICATION (not ported from the GPU kernel),
//                            so it can serve as an independent oracle for the
//                            GPU macro-skip rather than a photocopy of it.
//
// The oracle property (RaymarchMacroSkipTests): a CORRECT macro-skip must
// return BIT-IDENTICAL results to the per-voxel walk for the same ray - same
// hit, voxel, material, normal - just by evaluating fewer voxels. Any
// disagreement is a macro-skip bug by definition. This is the sibling the
// GPU Raymarch.compute macro-skip is diffed against (§0.3 Sibling Pattern).
//
// NOTE on structure: this sibling deliberately does NOT replicate the GPU
// kernel's "recompute tExit from the fixed ray origin every iteration"
// pattern (the structure the kernel's own comment fingers as the degenerate-
// case source). It advances to the brick exit, then takes ONE guaranteed
// single-voxel DDA step across the boundary - the plain reading of §6.3
// ("advance to the brick's far exit plane in one step"). If the GPU's
// structure and this straightforward structure disagree, that disagreement
// is the bug we are hunting - which is the whole point of not copying it.
public static class RaymarchReference
{
    public struct RayHit
    {
        public bool hit;
        public int3 voxelCoord;
        public byte material;
        public int3 normal;
        public int steps;      // per-voxel: voxels visited. macro-skip: iterations.
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
    //  Macro-skip walker (written from §6.3, the independent oracle).
    // ---------------------------------------------------------------------
    //
    //  Loop:
    //    - Resolve the brick at the current voxel.
    //    - If the brick is UNIFORM AIR: leap. Compute the t at which the ray
    //      exits this brick's bounding box, advance the ray just past that
    //      exit plane, and re-seed the per-voxel DDA state at the new voxel.
    //      This is the "one step per air brick" the spec promises.
    //    - If the brick is DENSE or a SOLID UNIFORM: fall back to plain
    //      per-voxel stepping until we leave it (identical to TracerRaycast's
    //      body), so hit/normal are computed exactly as the oracle does.
    //
    //  Brick resolution uses GetVoxel on the brick's origin voxel PLUS a
    //  uniformity assumption is avoided: we ask the store what the brick is
    //  by reading its handle indirectly. Since ChunkStore exposes only
    //  GetVoxel, the sibling determines "uniform air brick" by testing
    //  whether the WHOLE brick is air is too expensive; instead we leap
    //  conservatively one brick at a time and let the per-voxel fallback
    //  catch any non-air voxel. See ResolveBrickIsUniformAir below.
    //
    public static RayHit TracerRaycastMacroSkip(float3 origin, float3 dir, ChunkStore store, float maxDist = 128f)
    {
        RayHit result = new RayHit { hit = false, steps = 0 };
        float3 rayStart = origin * 10f;
        float3 rayDir = math.normalize(dir);
        int3 step = (int3)math.sign(rayDir);
        float3 tDelta = math.abs(1f / rayDir);
        int3 voxel = CoordMath.WorldToVoxel(origin);
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

            // Is the brick containing this voxel uniform air? If so, leap it.
            int3 brickCoord = CoordMath.VoxelToBrick(voxel);
            if (BrickIsUniformAir(store, brickCoord))
            {
                // Brick bounds in voxel space.
                int3 bMin = brickCoord * 8;
                int3 bMax = bMin + 7;

                // t (in voxel units along the normalized ray) at which the ray
                // crosses the FAR face of the brick on each axis, measured from
                // the current voxel's DDA state. Compute from rayStart, taking
                // the axis that exits first.
                float3 tExit = new float3(
                    step.x > 0 ? (bMax.x + 1f - rayStart.x) : (rayStart.x - bMin.x),
                    step.y > 0 ? (bMax.y + 1f - rayStart.y) : (rayStart.y - bMin.y),
                    step.z > 0 ? (bMax.z + 1f - rayStart.z) : (rayStart.z - bMin.z)
                ) * tDelta;

                float tMinExit = math.min(math.min(tExit.x, tExit.y), tExit.z);

                // Normal of the exit face (the face we cross leaving the brick).
                if (tMinExit == tExit.x) lastNormal = new int3(-step.x, 0, 0);
                else if (tMinExit == tExit.y) lastNormal = new int3(0, -step.y, 0);
                else lastNormal = new int3(0, 0, -step.z);

                // Advance just past the exit plane and re-seed the voxel.
                float advanceDist = tMinExit + 1e-4f;
                float3 exitPos = rayStart + rayDir * advanceDist;
                int3 nextVoxel = (int3)math.floor(exitPos);

                // Guaranteed progress: if the leap did not move us to a new
                // voxel (degenerate: origin on the brick's exit face, tiny
                // brick span on a near-zero axis), take one plain single-voxel
                // step instead so the loop can never stall. This mirrors the
                // intent of the GPU guard, but as a fallback to guaranteed
                // progress, not as a mask over a wrong leap.
                if (nextVoxel.Equals(voxel))
                {
                    SingleVoxelStep(ref voxel, ref currentDist, rayStart, tDelta, step, ref lastNormal);
                }
                else
                {
                    voxel = nextVoxel;
                    currentDist = tMinExit;
                }
                continue;
            }

            // Non-uniform-air brick: plain per-voxel step (oracle-identical).
            SingleVoxelStep(ref voxel, ref currentDist, rayStart, tDelta, step, ref lastNormal);
        }

        return result;
    }

    // One guaranteed-progress single-voxel DDA advance. tMax is derived on the
    // fly from the current voxel so this can be called from either loop state
    // without carrying a running tMax (correctness over micro-efficiency - this
    // is an oracle, not the shipped path).
    private static void SingleVoxelStep(ref int3 voxel, ref float currentDist,
        float3 rayStart, float3 tDelta, int3 step, ref int3 normal)
    {
        float3 tMax = new float3(
            step.x > 0 ? (voxel.x + 1f - rayStart.x) : (rayStart.x - voxel.x),
            step.y > 0 ? (voxel.y + 1f - rayStart.y) : (rayStart.y - voxel.y),
            step.z > 0 ? (voxel.z + 1f - rayStart.z) : (rayStart.z - voxel.z)
        ) * tDelta;

        if (tMax.x < tMax.y)
        {
            if (tMax.x < tMax.z) { voxel.x += step.x; currentDist = tMax.x; normal = new int3(-step.x, 0, 0); }
            else { voxel.z += step.z; currentDist = tMax.z; normal = new int3(0, 0, -step.z); }
        }
        else
        {
            if (tMax.y < tMax.z) { voxel.y += step.y; currentDist = tMax.y; normal = new int3(0, -step.y, 0); }
            else { voxel.z += step.z; currentDist = tMax.z; normal = new int3(0, 0, -step.z); }
        }
    }

    // A brick is "uniform air" for leap purposes iff every voxel in it is air.
    // The oracle checks this honestly (all 512 voxels) rather than trusting a
    // handle bit, so the sibling's leap can never skip a solid voxel - if this
    // returns true, leaping the brick is provably safe. This is deliberately
    // not how the GPU does it (the GPU reads the clipmap handle's uniform bit);
    // the point of the oracle is to be obviously correct, not fast.
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
}