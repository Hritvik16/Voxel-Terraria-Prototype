// ==========================================
// Assets/CoreEngine/Simulation/FluidActiveRegion.cs
//
// §7.4's near-player activity policy, as pure functions.
//
// §7.4: "fluid simulates only within an active radius around the player.
// Distant water is a settled terrain byte that looks like water but does not
// tick. On approach it wakes (slots allocate on GPU); on departure it sleeps
// back to static terrain."
//
// THE MECHANISM WAS ALREADY ON THE GPU. CSPromote has always gated promotion on
// WithinActiveRadius, and CSIntent has always force-demoted a slot whose home
// left it. What did not exist was anything to DRIVE it: FluidGpuSimulation
// .PlayerVoxel was set once at construction and never updated, so the radius was
// anchored wherever the region happened to be created. This file is the CPU-side
// policy that moves it, split out from the MonoBehaviour and from the GPU so it
// can be tested without either.
//
// TWO MECHANISMS THAT LOOK LIKE ONE AND ARE NOT:
//
//   HYSTERESIS (wake radius < sleep radius) stops a slot NEAR THE BOUNDARY from
//   flipping state when the player jitters. A slot promoted at the wake radius
//   must travel the whole gap before anything will demote it.
//
//   THE RE-CENTRE THRESHOLD stops the BOUNDARY ITSELF from moving every frame.
//   PlayerVoxel is uploaded per tick; following the player continuously sweeps
//   the boundary sub-voxel amounts every frame, which churns the ring of slots
//   sitting on it even with hysteresis, because the ring keeps moving under
//   them.
//
// Neither subsumes the other: hysteresis widens WHERE the boundary is, the
// threshold quantises WHEN it moves. Both are needed and both are tested.

using Unity.Mathematics;

public static class FluidActiveRegion
{
    /// Ratio of the demote radius to the wake radius. 1.15 means a slot
    /// promoted exactly at the wake boundary must travel 15% of the radius
    /// outward before it can be demoted.
    ///
    /// ENGINEERING DEFAULT, NOT MEASURED. Chosen to be obviously larger than any
    /// per-frame movement (at the shipped 1280-voxel radius the band is 192
    /// voxels ~ 19 m) while staying small enough that the active set is not
    /// meaningfully larger than §7.4's radius. Exposed so it can be tuned.
    public const float SleepRadiusRatio = 1.15f;

    /// How far the player must move before the active centre follows, in
    /// voxels. 16 voxels = 1.6 m.
    ///
    /// ENGINEERING DEFAULT, NOT MEASURED. Small enough that the centre tracks a
    /// walking player closely (walk speed 5.2 m/s covers it in ~0.3 s), large
    /// enough that sub-voxel jitter and a stationary player never move it.
    public const int DefaultRecentreThresholdVoxels = 16;

    /// The demote radius for a given wake radius. Rounded UP so it is always
    /// strictly greater than the wake radius even for tiny radii -- if the two
    /// ever collapsed to the same value the hysteresis band would vanish and
    /// boundary thrash would return silently.
    public static int SleepRadiusFor(int wakeRadiusVoxels)
    {
        if (wakeRadiusVoxels <= 0) return 0;
        int r = (int)math.ceil(wakeRadiusVoxels * SleepRadiusRatio);
        return math.max(r, wakeRadiusVoxels + 1);
    }

    /// Should the active centre move from `current` to `player`?
    ///
    /// Compares SQUARED distance against a squared threshold: §0's coordinate
    /// rule is integer maths, and a sqrt here would be both slower and a
    /// needless float.
    public static bool ShouldRecentre(int3 current, int3 player, int thresholdVoxels)
    {
        if (thresholdVoxels <= 0) return !current.Equals(player);
        int3 d = player - current;
        long d2 = (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z;
        return d2 >= (long)thresholdVoxels * thresholdVoxels;
    }

    /// Would a cell at `voxel` be promoted, with the centre at `centre`?
    /// Mirrors the shader's WithinActiveRadius exactly.
    public static bool WithinWakeRadius(int3 voxel, int3 centre, int wakeRadiusVoxels)
    {
        int3 d = voxel - centre;
        long d2 = (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z;
        return d2 <= (long)wakeRadiusVoxels * wakeRadiusVoxels;
    }

    /// Would a slot at `voxel` be force-demoted, with the centre at `centre`?
    /// Mirrors the shader's BeyondSleepRadius exactly. NOT the negation of
    /// WithinWakeRadius -- the gap between them is the hysteresis band.
    public static bool BeyondSleepRadius(int3 voxel, int3 centre, int sleepRadiusVoxels)
    {
        int3 d = voxel - centre;
        long d2 = (long)d.x * d.x + (long)d.y * d.y + (long)d.z * d.z;
        return d2 > (long)sleepRadiusVoxels * sleepRadiusVoxels;
    }

    /// True when a cell is in the band: too far to be newly promoted, not far
    /// enough to be demoted. A slot here KEEPS its state, whatever it is, and
    /// that is the whole point.
    public static bool InHysteresisBand(int3 voxel, int3 centre,
                                        int wakeRadiusVoxels, int sleepRadiusVoxels)
        => !WithinWakeRadius(voxel, centre, wakeRadiusVoxels)
        && !BeyondSleepRadius(voxel, centre, sleepRadiusVoxels);
}
