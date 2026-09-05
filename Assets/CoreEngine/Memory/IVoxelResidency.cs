// Assets/CoreEngine/Memory/IVoxelResidency.cs
//
// "Is this chunk actually loaded?" -- the question GetVoxel deliberately cannot
// answer.
//
// ChunkStore.GetVoxel returns Air for a non-resident chunk, and its own comment
// says that ambiguity is intentional and frozen (§12): the raymarcher, the CPU
// oracle and physics all want "there is nothing solid here", and an unloaded
// chunk satisfies that for them.
//
// It does NOT satisfy it for a character controller. "Air" under the player's
// feet means fall; "not loaded yet" must not. PHASE_5C_COMPLETION.md §9.4 is
// the precedent -- the fluid CA silently lost mass for a whole phase by reading
// Air across a residency edge and acting on it. This interface exists so
// movement asks the residency question explicitly instead of inferring it from
// a material byte.
//
// ChunkStore already had IsResident with exactly this signature; this only
// names it as a contract so PlayerMotor can be tested against a fake.

using Unity.Mathematics;

public interface IVoxelResidency
{
    /// True if the chunk is loaded and its voxels are real. False means
    /// "unknown", NOT "empty".
    bool IsResident(int3 chunkCoord);
}
