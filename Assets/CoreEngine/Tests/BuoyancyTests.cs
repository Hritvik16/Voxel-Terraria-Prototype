// Assets/CoreEngine/Tests/BuoyancyTests.cs
//
// Correctness proof for §13 Phase 6 file 6 (§8.6 Buoyancy), including the
// wiring of §8.2's speed clamp, which shipped in file 2 as a tested but unwired
// policy waiting for exactly this consumer.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

public class BuoyancyTests
{
    private sealed class FakeWorld : IWorldQuery
    {
        public readonly Dictionary<int3, byte> Cells = new Dictionary<int3, byte>();
        public byte GetVoxel(int3 v) => Cells.TryGetValue(v, out byte m) ? m : Materials.Air;

        public void Box(int3 lo, int3 hi, byte m)
        {
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
                Cells[new int3(x, y, z)] = m;
        }
    }

    private sealed class AllResident : IVoxelResidency { public bool IsResident(int3 c) => true; }
    private sealed class NoneResident : IVoxelResidency { public bool IsResident(int3 c) => false; }

    private const float H = 1.8f;

    private static Buoyancy Make(FakeWorld w) => new Buoyancy(w, new AllResident());

    // =====================================================================
    // The three probes
    // =====================================================================

    [Test]
    public void ThreeProbes_SitAtFeetCentreAndHead()
    {
        float3 feet = new float3(1f, 2f, 3f);
        Assert.AreEqual(2f, Buoyancy.ProbePosition(feet, H, BuoyancyProbe.Feet).y, 0.01f);
        Assert.AreEqual(2.9f, Buoyancy.ProbePosition(feet, H, BuoyancyProbe.Center).y, 0.01f);
        Assert.AreEqual(3.8f, Buoyancy.ProbePosition(feet, H, BuoyancyProbe.Head).y, 0.01f);
    }

    [Test]
    public void ADryBody_ReportsNoFluidNoForceNoDrag()
    {
        var w = new FakeWorld();
        BuoyancyState s = Make(w).Sample(new float3(0f, 5f, 0f), H, 10f, 0);

        Assert.IsFalse(s.InFluid);
        Assert.AreEqual(0f, s.SubmergedFraction, 1e-5f);
        Assert.AreEqual(0f, s.BuoyantAccelMps2, 1e-5f);
        Assert.AreEqual(0f, s.DragPerSecond, 1e-5f);
        Assert.AreEqual(Materials.Air, s.DominantFluid);
    }

    [Test]
    public void SubmergedFraction_TracksHowManyProbesAreWet()
    {
        // THE POOL MUST BE DEEPER THAN THE BODY. A first version used a 1.0 m
        // column against a 1.8 m body, so "fully submerged" was unreachable by
        // construction and the test failed on its own geometry rather than on
        // the code. Rows 0..40 is 4.1 m of water, surface at y = 4.1.
        var w = new FakeWorld();
        w.Box(new int3(-50, 0, -50), new int3(50, 40, 50), Materials.Water);
        var b = Make(w);

        // Feet just under the surface, centre and head above it.
        BuoyancyState a = b.Sample(new float3(0f, 4.05f, 0f), H, 10f, 0);
        Assert.AreEqual(1f / 3f, a.SubmergedFraction, 0.01f, "feet only");

        // Well under.
        BuoyancyState c = b.Sample(new float3(0f, 1.0f, 0f), H, 10f, 0);
        Assert.AreEqual(1f, c.SubmergedFraction, 0.01f, "all three probes wet");
        Assert.IsTrue(c.FullySubmerged);
    }

    [Test]
    public void SolidTerrainIsNotFluid_AndProducesNoBuoyancy()
    {
        // A body buried in stone must not float out of it. Stone is denser than
        // the body, so a naive "any non-air voxel" probe would push it upward.
        var w = new FakeWorld();
        w.Box(new int3(-50, -50, -50), new int3(50, 50, 50), Materials.Stone);

        BuoyancyState s = Make(w).Sample(new float3(0f, 0f, 0f), H, 10f, 0);
        Assert.IsFalse(s.InFluid, "stone is not a fluid");
        Assert.AreEqual(0f, s.BuoyantAccelMps2, 1e-5f);
    }

    [Test]
    public void SandIsNotFluidEither_BecauseItIsAFallingSolid()
    {
        var w = new FakeWorld();
        w.Box(new int3(-50, -50, -50), new int3(50, 50, 50), Materials.Sand);
        Assert.IsFalse(Make(w).Sample(float3.zero, H, 10f, 0).InFluid);
    }

    // =====================================================================
    // Appendix C.6: F = (rho_f - rho_e) * g * Vsub
    // =====================================================================

    [Test]
    public void ABodyLighterThanTheFluid_IsPushedUp()
    {
        var w = new FakeWorld();
        w.Box(new int3(-50, -50, -50), new int3(50, 50, 50), Materials.Water);
        var b = Make(w);
        b.BodyDensityKgM3 = 500f;                       // half water's density

        BuoyancyState s = b.Sample(float3.zero, H, 10f, 0);
        Assert.Greater(s.BuoyantAccelMps2, 0f, "it floats");
        // ((1000-500)/500) * 22 * 1.0 = 22
        Assert.AreEqual(22f, s.BuoyantAccelMps2, 0.5f, "C.6, divided through by mass");
    }

    [Test]
    public void ABodyDenserThanTheFluid_IsPulledDown()
    {
        var w = new FakeWorld();
        w.Box(new int3(-50, -50, -50), new int3(50, 50, 50), Materials.Water);
        var b = Make(w);
        b.BodyDensityKgM3 = 2000f;                      // twice water's density

        BuoyancyState s = b.Sample(float3.zero, H, 10f, 0);
        Assert.Less(s.BuoyantAccelMps2, 0f,
            "denser than water means the buoyant term is NEGATIVE -- stone sinks, and the " +
            "motor adds this to gravity rather than replacing it");
    }

    [Test]
    public void BuoyancyScalesWithSubmergedFraction()
    {
        var w = new FakeWorld();
        w.Box(new int3(-50, 0, -50), new int3(50, 40, 50), Materials.Water);   // deeper than the body
        var b = Make(w);
        b.BodyDensityKgM3 = 500f;

        float partial = b.Sample(new float3(0f, 4.05f, 0f), H, 10f, 0).BuoyantAccelMps2;
        float full = b.Sample(new float3(0f, 1.0f, 0f), H, 10f, 0).BuoyantAccelMps2;

        Assert.Greater(full, partial, "more submerged means more lift");
        Assert.AreEqual(full / 3f, partial, 0.5f, "one probe of three is a third of the force");
    }

    [Test]
    public void LavaLiftsMoreThanWater_BecauseItIsDenser()
    {
        var water = new FakeWorld();
        water.Box(new int3(-50, -50, -50), new int3(50, 50, 50), Materials.Water);
        var lava = new FakeWorld();
        lava.Box(new int3(-50, -50, -50), new int3(50, 50, 50), Materials.Lava);

        float inWater = Make(water).Sample(float3.zero, H, 10f, 0).BuoyantAccelMps2;
        float inLava = Make(lava).Sample(float3.zero, H, 10f, 0).BuoyantAccelMps2;

        Assert.Greater(inLava, inWater, "lava is denser, so it lifts harder");
    }

    [Test]
    public void DragComesFromTheRegistry_AndHoneyIsTheThickest()
    {
        float Drag(byte material)
        {
            var w = new FakeWorld();
            w.Box(new int3(-50, -50, -50), new int3(50, 50, 50), material);
            return Make(w).Sample(float3.zero, H, 10f, 0).DragPerSecond;
        }

        Assert.Greater(Drag(Materials.Water), 0f);
        Assert.Greater(Drag(Materials.Lava), Drag(Materials.Water));
        Assert.Greater(Drag(Materials.Honey), Drag(Materials.Lava),
            "honey is the thickest, consistent with §7.4 already giving it the slowest tick");
    }

    [Test]
    public void TheRegistryHasDensitiesAtAll()
    {
        // A.7 declared these fields and nothing ever populated them. If this
        // regresses to a table of zeroes, every buoyancy force silently becomes
        // -g and bodies sink through water at full speed.
        Assert.AreEqual(1000f, MaterialRules.Density(Materials.Water), 1f);
        Assert.Greater(MaterialRules.Density(Materials.Lava), MaterialRules.Density(Materials.Water));
        Assert.Greater(MaterialRules.Density(Materials.Honey), MaterialRules.Density(Materials.Water));
        Assert.Less(MaterialRules.Density(Materials.Air), 10f);
        Assert.Greater(MaterialRules.Density(Materials.Stone), 2000f);
    }

    // =====================================================================
    // §8.2's speed clamp, wired here
    // =====================================================================

    [Test]
    public void TheSpeedClamp_IsNotEngagedWhileTheReadbackIsHealthy()
    {
        var w = new FakeWorld();
        BuoyancyState s = Make(w).Sample(float3.zero, H, 60f, 0);
        Assert.IsFalse(s.SpeedClampEngaged);
        Assert.AreEqual(60f, s.ClampedSpeedMps, 1e-4f);
    }

    [Test]
    public void TheSpeedClamp_EngagesOnceTheOpListStalls()
    {
        var w = new FakeWorld();
        BuoyancyState s = Make(w).Sample(float3.zero, H, 60f, SweptCCD.StaleFramesBeforeClamp);

        Assert.IsTrue(s.SpeedClampEngaged,
            "§8.6's 60 m/s dive is only safe 'since the speed clamp engages before staleness " +
            "could exceed the mitigated bound' -- so it must actually engage");
        Assert.AreEqual(SweptCCD.ClampedSpeedMps, s.ClampedSpeedMps, 1e-4f);
    }

    [Test]
    public void TheClampUsesTheSamePolicyObjectAsSweptCCD()
    {
        // If these ever diverged, §8.2 and §8.6 would disagree about when a
        // 60 m/s body is safe, and the argument in §8.6 would be resting on a
        // threshold nothing enforces.
        for (int frames = 0; frames < 8; frames++)
            Assert.AreEqual(SweptCCD.SpeedClampMps(60f, frames),
                            Buoyancy.ClampedSpeedFor(60f, frames), 1e-5f,
                            $"at {frames} stale frames");
    }

    // =====================================================================
    // One-way sampling and the residency rule
    // =====================================================================

    [Test]
    public void ANonResidentChunk_ReadsAsDry_NotAsFluid()
    {
        // The OPPOSITE of PlayerMotor's rule, deliberately. Movement fails
        // closed (unknown blocks) so a body cannot fall through unstreamed
        // world; buoyancy fails OPEN, because inventing an upward force from
        // terrain nobody has loaded would launch the player at the window edge.
        var w = new FakeWorld();
        w.Box(new int3(-50, -50, -50), new int3(50, 50, 50), Materials.Water);

        var b = new Buoyancy(w, new NoneResident());
        BuoyancyState s = b.Sample(float3.zero, H, 10f, 0);

        Assert.IsFalse(s.InFluid, "unloaded world is not treated as water");
        Assert.AreEqual(0f, s.BuoyantAccelMps2, 1e-5f,
            "so the body is never shoved upward by terrain that has not streamed in");
    }

    [Test]
    public void SamplingNeverMutatesTheWorld()
    {
        // §8.6: "The sim never knows the player exists (one-way sampling)."
        var w = new FakeWorld();
        w.Box(new int3(-20, -20, -20), new int3(20, 20, 20), Materials.Water);
        int before = w.Cells.Count;

        var b = Make(w);
        for (int i = 0; i < 50; i++) b.Sample(new float3(i * 0.01f, 0f, 0f), H, 10f, 0);

        Assert.AreEqual(before, w.Cells.Count,
            "buoyancy must not add, remove or change a single voxel");
    }

    [Test]
    public void EnteringWaterFeetFirst_ReportsWaterBeforeTheBodyIsHalfIn()
    {
        var w = new FakeWorld();
        w.Box(new int3(-50, 0, -50), new int3(50, 9, 50), Materials.Water);

        BuoyancyState s = Make(w).Sample(new float3(0f, 0.95f, 0f), H, 10f, 0);
        Assert.AreEqual(Materials.Water, s.DominantFluid,
            "a dive should register on entry, not once the head is under");
        Assert.IsTrue(s.InFluid);
    }
}
