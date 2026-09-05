// ==========================================
// Assets/CoreEngine/Gameplay/Buoyancy.cs
//
// §13 Phase 6, file 6 of 6 (§8.6 Buoyancy):
//   "Three probes (Feet, Center, Head) sampled from CPU terrain bytes via
//    GetVoxel -- the same bounded-latency fluid state described in §8.2
//    (typically 1-3 frames behind the GPU's decision, mitigated by the
//    depenetration backstop + speed clamp). Probe IDs drive upward force + drag
//    from the Registry (F = (rho_f - rho_e)*g*Vsub, C.6). A 60 m/s dive gets
//    buoyancy within that same bounded window of entering water -- not instant,
//    but never a silent lakebed slam, since the speed clamp engages before
//    staleness could exceed the mitigated bound. The sim never knows the player
//    exists (one-way sampling)."
//
// =========================================================================
// ONE-WAY SAMPLING, AND WHY IT IS WORTH SAYING OUT LOUD
// =========================================================================
// "The sim never knows the player exists." Nothing in this file writes a voxel,
// wakes a slot, or touches FluidGpuSimulation. It reads terrain bytes and
// returns forces. That is what keeps the CA's determinism intact -- a fluid
// simulation whose state depended on where a body happened to be standing could
// not be replayed, and §7's conservation proofs would be describing a different
// system every run.
//
// =========================================================================
// THIS IS WHERE §8.2'S SPEED CLAMP FINALLY HAS A CONSUMER
// =========================================================================
// SweptCCD shipped SpeedClampMps as a tested but UNWIRED pure policy, with a
// note saying the wiring belonged with whatever first needed it. That is this
// file. §8.6's safety argument depends on it by name: a 60 m/s dive is only
// safe "since the speed clamp engages before staleness could exceed the
// mitigated bound". The signal is FluidOpListReadback.FramesSinceLastApplied,
// added for this; ClampedSpeedFor below is the join.
//
// WHAT THE REGISTRY DID NOT HAVE. A.7's MaterialData declares `density` and
// `viscosityDrag`, and §8.6 says the force comes "from the Registry" -- but
// nothing in the project had ever populated either field. The values now live
// in MaterialRules beside the flags table; see the comment there. They are
// content to be tuned, not settled physics, and this file makes no claim about
// whether they FEEL right.

using Unity.Mathematics;

/// Which of §8.6's three probes is which. Ordered feet-upward so a caller can
/// treat the index as a height.
public enum BuoyancyProbe
{
    Feet = 0,
    Center = 1,
    Head = 2,
}

/// One frame's buoyancy answer for one body.
public struct BuoyancyState
{
    /// What each of the three probes found. Materials.Air when dry.
    public byte FeetMaterial, CenterMaterial, HeadMaterial;

    /// 0, 1/3, 2/3 or 1 -- how many probes are in fluid. §8.6's Vsub, expressed
    /// as the fraction of the body submerged.
    public float SubmergedFraction;

    /// True if ANY probe is in fluid.
    public bool InFluid => SubmergedFraction > 0f;

    /// True only when every probe is in fluid.
    public bool FullySubmerged => SubmergedFraction >= 1f;

    /// The fluid the body is mostly in, or Air.
    public byte DominantFluid;

    /// Net vertical acceleration from buoyancy, m/s^2. POSITIVE IS UP.
    /// Appendix C.6: F = (rho_f - rho_e) * g * Vsub, divided through by the
    /// body's mass to give an acceleration the motor can just add.
    public float BuoyantAccelMps2;

    /// Linear drag per second to apply to the body's velocity while it is in
    /// this fluid. 0 when dry.
    public float DragPerSecond;

    /// True when the readback has stalled far enough that §8.2's clamp is
    /// engaged. Surfaced so a rig can assert the clamp fires when it should,
    /// rather than inferring it from a speed that might be low for other reasons.
    public bool SpeedClampEngaged;

    /// The speed the body should be limited to this frame.
    public float ClampedSpeedMps;
}

public sealed class Buoyancy
{
    private readonly IWorldQuery _world;
    private readonly IVoxelResidency _residency;

    /// The body's own density, kg/m^3. A human is ~985 -- just under water, so
    /// a player floats with their head out rather than bobbing like cork or
    /// sinking like stone. This is a feel number; see MaterialRules' note.
    public float BodyDensityKgM3 = 985f;

    /// Gravity used by the buoyancy term. Kept separate from PlayerConfig's so
    /// buoyancy can be reasoned about without a player attached.
    public float GravityMps2 = 22f;

    /// Diagnostics; nothing branches on these.
    public long SamplesTaken;
    public long SamplesInFluid;

    public Buoyancy(IWorldQuery world, IVoxelResidency residency)
    {
        _world = world;
        _residency = residency;
    }

    /// Where the three probes sit for a body whose feet are at `feetCentre`.
    ///
    /// Feet and head are inset by a skin so a body resting exactly on a surface
    /// does not read the floor as its feet probe, and a body with its head
    /// exactly at a water line does not flicker.
    public static float3 ProbePosition(float3 feetCentre, float bodyHeightM, BuoyancyProbe which)
    {
        switch (which)
        {
            case BuoyancyProbe.Feet:   return feetCentre + new float3(0f, VoxelCollision.SkinM, 0f);
            case BuoyancyProbe.Center: return feetCentre + new float3(0f, bodyHeightM * 0.5f, 0f);
            default:                   return feetCentre + new float3(0f, bodyHeightM - VoxelCollision.SkinM, 0f);
        }
    }

    /// §8.6's sample. `framesSinceOpListApplied` comes from
    /// FluidOpListReadback.FramesSinceLastApplied; pass 0 when there is no fluid
    /// simulation running, which is the honest value -- nothing is stale if
    /// nothing is being simulated.
    public BuoyancyState Sample(float3 feetCentre, float bodyHeightM,
                                float requestedSpeedMps, int framesSinceOpListApplied)
    {
        SamplesTaken++;
        var s = new BuoyancyState
        {
            FeetMaterial = Materials.Air,
            CenterMaterial = Materials.Air,
            HeadMaterial = Materials.Air,
            DominantFluid = Materials.Air,
        };

        // THREE PROBES, READ-ONLY. §8.6's whole mechanism.
        s.FeetMaterial = SampleFluidAt(ProbePosition(feetCentre, bodyHeightM, BuoyancyProbe.Feet));
        s.CenterMaterial = SampleFluidAt(ProbePosition(feetCentre, bodyHeightM, BuoyancyProbe.Center));
        s.HeadMaterial = SampleFluidAt(ProbePosition(feetCentre, bodyHeightM, BuoyancyProbe.Head));

        int wet = 0;
        if (s.FeetMaterial != Materials.Air) wet++;
        if (s.CenterMaterial != Materials.Air) wet++;
        if (s.HeadMaterial != Materials.Air) wet++;
        s.SubmergedFraction = wet / 3f;

        // The dominant fluid is the one most probes are in; ties go to the
        // lowest probe, because that is the one the body is entering first.
        s.DominantFluid = Dominant(s.FeetMaterial, s.CenterMaterial, s.HeadMaterial);

        if (wet > 0)
        {
            SamplesInFluid++;

            // Appendix C.6: F = (rho_f - rho_e) * g * Vsub.
            // Divided by the body's mass (rho_e * V) to give an acceleration,
            // this is ((rho_f - rho_e)/rho_e) * g * submergedFraction. A body
            // denser than the fluid gets a NEGATIVE term, which is correct --
            // stone sinks -- and the motor adds it to gravity rather than
            // replacing it.
            float rhoF = MaterialRules.Density(s.DominantFluid);
            float rhoE = math.max(1e-3f, BodyDensityKgM3);
            s.BuoyantAccelMps2 = ((rhoF - rhoE) / rhoE) * GravityMps2 * s.SubmergedFraction;

            s.DragPerSecond = MaterialRules.ViscosityDrag(s.DominantFluid) * s.SubmergedFraction;
        }

        // §8.2's speed clamp, wired here because this is the first system whose
        // safety argument depends on it.
        s.ClampedSpeedMps = SweptCCD.SpeedClampMps(requestedSpeedMps, framesSinceOpListApplied);
        s.SpeedClampEngaged = s.ClampedSpeedMps < requestedSpeedMps;
        return s;
    }

    /// The material at a point IF it is a fluid, else Air.
    ///
    /// A NON-RESIDENT CHUNK READS AS DRY, and that is the opposite of the rule
    /// PlayerMotor uses -- deliberately. For MOVEMENT, "unknown" must fail
    /// closed (block), or the body falls through unstreamed world. For
    /// BUOYANCY, failing closed would mean inventing an upward force from
    /// terrain nobody has loaded, shoving the player into the sky at the edge of
    /// the window. The safe default differs because the consequence differs:
    /// missing buoyancy for a frame is a missed splash, invented buoyancy is a
    /// launch.
    private byte SampleFluidAt(float3 pointM)
    {
        int3 v = CoordMath.WorldToVoxel(pointM);
        byte m;
        return VoxelCollision.IsFluid(_world, _residency, v, out m) ? m : Materials.Air;
    }

    private static byte Dominant(byte feet, byte center, byte head)
    {
        // Ties to the lowest non-air probe: entering water feet-first should
        // report water before the body is half in.
        if (feet != Materials.Air && (feet == center || feet == head)) return feet;
        if (center != Materials.Air && center == head) return center;
        if (feet != Materials.Air) return feet;
        if (center != Materials.Air) return center;
        return head;
    }

    /// Convenience for a caller that only wants the clamp. Same policy object
    /// as §8.2's, so the two can never disagree about the threshold.
    public static float ClampedSpeedFor(float requestedMps, int framesSinceOpListApplied)
        => SweptCCD.SpeedClampMps(requestedMps, framesSinceOpListApplied);
}
