// ==========================================
// Assets/CoreEngine/Gameplay/VoxelCollision.cs
//
// THE ONE DEFINITION OF "does this voxel stop a body", shared by PlayerMotor
// (§8.1) and SweptCCD (§8.2).
//
// WHY IT IS FACTORED OUT RATHER THAN WRITTEN TWICE. Two callers asking the same
// question of the world, each with its own copy of the answer, is exactly the
// shape of the brush-guard bug fixed in 19b8ac6: the vent path checked the
// fluid arena and the paint path did not, so one enforced a limit the other
// silently ignored. The swept pass and the substepped pass must agree about
// what is solid, or a body stopped by one is passed by the other and the
// disagreement shows up as a tunnel.
//
// TWO RULES LIVE HERE, BOTH LOAD-BEARING:
//
// 1. NON-RESIDENT COUNTS AS BLOCKING (§9.4), WITHIN ITS DOMAIN. ChunkStore
//    .GetVoxel returns Air for a chunk that is merely not loaded, deliberately
//    and frozen (§12) -- the raymarcher and the CPU oracle both want "nothing
//    solid here". Physics does not: it means the body walks into, and then
//    falls through, world that has not streamed in. Failing closed stops the
//    body at the edge of the loaded world, which is visible and harmless.
//
//    THE DOMAIN MATTERS, and getting it wrong was a real bug. "Fail closed"
//    is right where non-resident means UNKNOWN -- the horizontal streaming
//    edge, where the chunk really might be rock. It is wrong ABOVE the
//    generation ceiling, where non-resident means statically EMPTY:
//    StreamManager.RebuildPendingSet only ever admits cy in
//    [0, MAX_GENERATED_CHUNK_Y], so every chunk above it is permanently
//    non-resident at any altitude, by construction and not by timing.
//    Treating that as solid embedded a flying player in phantom rock on all
//    six sides: they could not fall, could not move, and ResolveSpawn could
//    not free them because there was no free voxel within its reach. That is
//    the whole of the "Tab at height does not drop me" bug.
//
//    BELOW the world stays fail-closed on purpose. The asymmetry is not an
//    oversight: above the ceiling is sky the generator guarantees is empty,
//    while below cy=0 is off the bottom of a world that has no floor, and
//    a body let through there falls forever with nothing to land on.
//
// 2. FLUID DOES NOT BLOCK. §8.2: "the swept pass clamps only on *solid*" and
//    "fluid doesn't block movement the way solid terrain does". Water, lava and
//    honey are passable; §8.6 gives buoyancy its own probes, and §8.2's
//    fluid-traversal accumulator is how a fast body still notices them. Sand
//    DOES block -- it is a falling SOLID, not a fluid, and standing on a sand
//    pile has to work.

using Unity.Mathematics;

public static class VoxelCollision
{
    /// Metres per voxel. CoordMath.WorldToVoxel is floor(worldPos * 10), so
    /// this is 0.1 by construction, not by choice.
    public const float VoxelSizeM = 0.1f;

    /// Nudge used when a max face sits exactly on a voxel boundary, so a body
    /// snapped flush to a surface does not count the next voxel along.
    public const float SkinM = 1e-4f;

    /// Is this voxel in a chunk layer the generator can never fill?
    ///
    /// Reads the SAME constant StreamManager admits by, so the two cannot drift
    /// apart -- see MAX_GENERATED_CHUNK_Y's own note about its readers going
    /// stale together. The moment generation grows vertically this answer moves
    /// with it, and no separate number needs remembering.
    public static bool AboveGeneratedContent(int3 voxel)
        => CoordMath.VoxelToChunk(voxel).y > VoxelEngine.Streaming.StreamManager.MAX_GENERATED_CHUNK_Y;

    public static bool IsBlocking(IWorldQuery world, IVoxelResidency residency, int3 voxel)
    {
        if (residency != null && !residency.IsResident(CoordMath.VoxelToChunk(voxel)))
            return !AboveGeneratedContent(voxel);          // rule 1, see the header

        byte m = world.GetVoxel(voxel);
        if (m == Materials.Air) return false;
        return !MaterialRules.IsFluidMaterial(m);           // rule 2
    }

    public static bool IsNonResident(IVoxelResidency residency, int3 voxel)
        => residency != null && !residency.IsResident(CoordMath.VoxelToChunk(voxel));

    /// True if a fluid occupies this voxel -- what §8.2's traversal accumulator
    /// counts. Deliberately the complement of rule 2, from the same table, so
    /// "passable" and "counts as fluid" can never disagree.
    public static bool IsFluid(IWorldQuery world, IVoxelResidency residency, int3 voxel, out byte material)
    {
        material = Materials.Air;
        if (residency != null && !residency.IsResident(CoordMath.VoxelToChunk(voxel))) return false;
        byte m = world.GetVoxel(voxel);
        if (m == Materials.Air) return false;
        if (!MaterialRules.IsFluidMaterial(m)) return false;
        material = m;
        return true;
    }

    /// Does an axis-aligned body standing with its feet at `feetCentre` overlap
    /// anything blocking? The AABB is
    ///   x,z: feetCentre.xz +/- widthM/2
    ///   y  : feetCentre.y .. feetCentre.y + heightM
    public static bool OverlapsSolid(IWorldQuery world, IVoxelResidency residency,
                                     float3 feetCentre, float widthM, float heightM,
                                     out bool nonResident)
    {
        nonResident = false;
        float half = widthM * 0.5f;
        float3 lo = new float3(feetCentre.x - half, feetCentre.y, feetCentre.z - half);
        float3 hi = new float3(feetCentre.x + half, feetCentre.y + heightM, feetCentre.z + half);

        int3 vlo = CoordMath.WorldToVoxel(lo);
        int3 vhi = CoordMath.WorldToVoxel(hi - SkinM);

        for (int z = vlo.z; z <= vhi.z; z++)
        for (int y = vlo.y; y <= vhi.y; y++)
        for (int x = vlo.x; x <= vhi.x; x++)
        {
            int3 v = new int3(x, y, z);
            if (!IsBlocking(world, residency, v)) continue;
            nonResident = IsNonResident(residency, v);
            return true;
        }
        return false;
    }

    public static bool OverlapsSolid(IWorldQuery world, IVoxelResidency residency,
                                     float3 feetCentre, float widthM, float heightM)
        => OverlapsSolid(world, residency, feetCentre, widthM, heightM, out _);

    /// Is any part of the body standing in fluid?
    ///
    /// SEPARATE FROM OverlapsSolid ON PURPOSE, and worth stating because the
    /// confusion is easy: OverlapsSolid answers "am I stuck", and by rule 2 it
    /// returns FALSE for a body fully submerged in water. It is therefore not a
    /// dryness test, and using it as one silently asserts nothing -- which is
    /// exactly what a first version of the §8.2 water-sheet test did. Anything
    /// asking "is this position wet" wants this.
    public static bool AnyFluidInBody(IWorldQuery world, IVoxelResidency residency,
                                      float3 feetCentre, float widthM, float heightM,
                                      out byte material)
    {
        material = Materials.Air;
        float half = widthM * 0.5f;
        int3 vlo = CoordMath.WorldToVoxel(new float3(feetCentre.x - half, feetCentre.y, feetCentre.z - half));
        int3 vhi = CoordMath.WorldToVoxel(new float3(feetCentre.x + half, feetCentre.y + heightM,
                                                     feetCentre.z + half) - SkinM);

        for (int z = vlo.z; z <= vhi.z; z++)
        for (int y = vlo.y; y <= vhi.y; y++)
        for (int x = vlo.x; x <= vhi.x; x++)
            if (IsFluid(world, residency, new int3(x, y, z), out material)) return true;
        return false;
    }
}
