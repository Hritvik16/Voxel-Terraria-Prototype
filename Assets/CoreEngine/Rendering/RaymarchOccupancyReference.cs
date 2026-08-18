// Assets/CoreEngine/Rendering/RaymarchOccupancyReference.cs
//
// Amendment 8.8 — Phase B: CPU tracer sibling using OccupancyMaskData
// (Phase A, proven) to chain multiple air-mip leaps within a SINGLE outer
// iteration, reducing outer while-loop iteration count for long straight air
// runs — e.g. the Y=84 top-down worst-case pose, where a ray can cross many
// stacked same-level mip cells along a single dominant (near-vertical) axis
// before reaching a hit.
//
// MECHANISM (see AMENDMENT_8_8_OCCUPANCY_BITMASK.md for the full derivation):
//   LeapSpanReseed takes a single scalar spanVoxels applied uniformly to all
//   three axes — a CUBIC span, by design, which is exactly what keeps it
//   provably correct for any span size (proven via RaymarchMipReseedTests).
//   Making it accept an arbitrary rectangular span would mean re-deriving and
//   re-fuzzing the leap primitive itself. This file does NOT do that.
//
//   After leaping through the finest available air cell, re-derive the cell
//   the ray is NOW actually in (voxel >> shift) and check whether THAT cell
//   is also air at the same level. If so, leap through it too — the SAME
//   already-proven LeapSpanReseed, unchanged, with a span derived fresh from
//   the ray's true current position — before the outer loop's `steps`
//   counter increments and the top-down probe re-runs.
//
//   REVISION 2 (this version, found via a hand-traced CPU fuzz failure -
//   every long-distance test was returning a MISS, not a wrong-voxel hit):
//   the previous version computed a "neighborCell" one cell further along
//   than the cell the ray had just entered, and built the chained leap's
//   span from THAT cell's bounds — while `voxel` was still positioned in the
//   cell before it. Traced by hand against Occupancy_LongAirSpan_Down: after
//   the first leap the ray sits at the top of a cell spanning voxel-y
//   [256,383]; the buggy code never checked THAT cell's air status at all,
//   and instead leaped straight into the span [128,255] while voxel.y was
//   still 383 - producing an exitCount of 256 instead of the correct 128.
//   This compounds across chain iterations and, in the traced example,
//   wraps around the toroidal window boundary (AirMip.FlatIndex's masking
//   treats a cell index of -1 as dims-1), sending the ray toward positions
//   with nothing to do with its real trajectory - explaining why every
//   substantial-distance test failed as a MISS rather than a wrong-voxel
//   hit: corrupted bookkeeping, not imprecision.
//
//   THE FIX: don't compute a hypothetical neighbor at all. After each leap,
//   re-derive the cell the ray is ACTUALLY now in and check that cell's own
//   air status before leaping through it. Since the span is always
//   recomputed from the ray's true current position, this is safe
//   regardless of which axis the previous leap exited through - so the
//   earlier "only chain along the dominant axis" restriction is no longer
//   needed to keep this safe, and has been removed (it was solving a
//   problem this version doesn't have).
//
//   Deliberately not re-probing higher levels after each chained leap (per
//   Rule 9, simplest version that could show the mechanism works) - a
//   chained leap always uses the SAME k as the leap that started the chain,
//   even if an even-larger level above would also be air at the new
//   position. This is never a correctness risk (a smaller valid air region
//   is always a safe span for LeapSpanReseed), only a possible missed
//   optimization, and is a reasonable Phase-later refinement once this
//   simpler version is proven to help at all.
//
//   OccupancyMaskData is still NOT used by this mechanism (see REVISION 1's
//   note, unchanged) - it remains valid, tested infrastructure (Phase A),
//   intended for a different future job (picking which child to descend
//   into when a cell IS occupied), which this chaining optimization never
//   needed.
//
// CORRECTNESS BAR: identical to every other tracer in this codebase — full
// differential fuzz against TracerRaycast (the oracle), final hit/voxel/
// material/normal agreement only. See RaymarchOccupancyTests.cs. Chaining
// changes ONLY how many times LeapSpanReseed is called per outer iteration
// and in what order — it introduces no new arithmetic, so if each individual
// leap is correct (already proven) and the boundary-adjacency guard holds,
// the chained result must equal what separate outer iterations would have
// produced. The fuzz test still checks this directly rather than trusting
// that argument alone.
//
// Requires RaymarchReference to be declared `partial` — see
// REQUIRED_EDIT_RaymarchReference.txt. This file adds no public API surface
// beyond TracerRaycastOccupancy; every helper it uses is the existing,
// unmodified, already-proven private machinery in RaymarchReference.cs.

using Unity.Mathematics;
using VoxelEngine.Memory;

public static partial class RaymarchReference
{
    // How many additional same-level neighbor leaps may be chained within one
    // outer iteration, beyond the first. Small and bounded on purpose — this
    // is a modest, low-risk lookahead, not an unbounded coalescing scheme.
    // (Was 3 when chaining was incorrectly parent-scoped; now that a chain
    // can cross any number of same-level cell boundaries, a slightly larger
    // bound lets a single outer iteration span more of a long air run before
    // ceding back to the top-down probe, without being unbounded.)
    private const int MaxChainLeaps = 7;

    // NOTE: `occupancy` is accepted but not currently read by this function's
    // chaining mechanism - see the file header's "REVISION" note. Kept in the
    // signature rather than removed, since Phase A's structure is still
    // intended for a future descent-target-selection use, and changing this
    // signature again later would be a second churn on callers/tests for no
    // benefit today.
    public static RayHit TracerRaycastOccupancy(float3 origin, float3 dir, ChunkStore store,
        AirMipData mips, VoxelEngine.Memory.OccupancyMaskData occupancy, float maxDist = 128f)
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
                if (mips.Levels[k - 1][AirMip.FlatIndex(cell, dims)] != 0u) continue; // not air here

                int spanVoxels = 8 << k;
                int3 spanMin = cell << shift;
                LeapSpanReseed(ref voxel, ref tMax, ref currentDist, tDelta, step,
                    rayStart, spanMin, spanVoxels, ref lastNormal);
                leapt = true;

                // --- Phase B (revision 2): chain additional same-level leaps
                // by re-deriving the cell the ray is ACTUALLY now in after
                // each leap and checking THAT cell's own air status - no
                // hypothetical neighbor, no axis restriction. See the file
                // header's "REVISION 2" note for the bug this replaced. ---
                int chainCount = 0;
                while (chainCount < MaxChainLeaps)
                {
                    int3 newCell = voxel >> shift;
                    if (mips.Levels[k - 1][AirMip.FlatIndex(newCell, dims)] != 0u) break; // not air - stop, let the outer loop's top-down probe (possibly a finer level) handle it

                    int3 newSpanMin = newCell << shift;
                    LeapSpanReseed(ref voxel, ref tMax, ref currentDist, tDelta, step,
                        rayStart, newSpanMin, spanVoxels, ref lastNormal);
                    chainCount++;
                }

                break;
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
}