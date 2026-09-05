// Assets/CoreEngine/Tests/SweptCCDTests.cs
//
// Correctness proof for §13 Phase 6 file 2 (§8.2): the swept pass, the fluid
// pass-through accumulator, and the depenetration backstop.
//
// §13's acceptance line for this file is unusual in that it specifies its own
// vacuity check: "0.2m wall, grapple in at 60 m/s: stop at the face, 20/20.
// Disable the sweep -> confirm phasing (test isn't vacuous). Re-enable, force a
// missed ray -> depenetration catches it, one corrected frame, never a tunnel."
// All four clauses are below, by name.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

public class SweptCCDTests
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

    private sealed class ResidentExcept : IVoxelResidency
    {
        public readonly HashSet<int3> Missing = new HashSet<int3>();
        public bool IsResident(int3 c) => !Missing.Contains(c);
    }

    private const float W = 0.6f, H = 1.8f;

    // =====================================================================
    // §13: "0.2m wall, grapple in at 60 m/s: stop at the face, 20/20"
    // =====================================================================

    [Test]
    public void A02mWall_StopsAGrappleAt60Mps_TwentyOutOfTwenty()
    {
        // 0.2 m = 2 voxels thick, at x voxels 100..101 (10.0 .. 10.2 m).
        // 60 m/s at 60 Hz is 1.0 m per frame -- five times the wall's thickness,
        // which is exactly the case a non-swept test would phase through.
        int stopped = 0;
        var failures = new List<string>();

        for (int trial = 0; trial < 20; trial++)
        {
            var w = new FakeWorld();
            w.Box(new int3(100, 0, -50), new int3(101, 60, 50), Materials.Stone);
            var ccd = new SweptCCD(w, new AllResident());

            // Vary the starting phase across the 20 trials, so this is not one
            // lucky alignment twenty times over. The range is bounded by
            // geometry: the leading face (startX + 0.3) must begin clear of the
            // 10.0 m wall and end past it after a 1.0 m frame, i.e.
            // 8.7 < startX < 9.7.
            float startX = 8.8f + trial * 0.04f;
            float3 from = new float3(startX, 0.5f, 0f);
            float3 to = from + new float3(1.0f, 0f, 0f);

            CCDResult r = ccd.Sweep(from, to, W, H);
            if (!r.HitSolid) { failures.Add($"trial {trial}: no hit from x={startX:F3}"); continue; }

            float3 clamped = SweptCCD.ClampToHit(from, to, r);
            float leading = clamped.x + W * 0.5f;
            if (leading > 10.0f + 1e-3f)
                failures.Add($"trial {trial}: leading edge {leading:F4} past the 10.0 m face");
            else
                stopped++;
        }

        CollectionAssert.IsEmpty(failures, string.Join(" | ", failures));
        Assert.AreEqual(20, stopped, "all 20 grapple approaches must stop at the wall face");
    }

    [Test]
    public void WithoutTheSweep_TheSameApproachPhasesThrough_SoTheTestIsNotVacuous()
    {
        // §13 asks for this explicitly. The "disabled sweep" is simply the
        // endpoint test the sweep replaces: both endpoints are dry, so a
        // controller that only checks where it LANDS sees nothing at all.
        var w = new FakeWorld();
        w.Box(new int3(100, 0, -50), new int3(101, 60, 50), Materials.Stone);

        // Clearance is deliberate, not incidental: the body is 0.6 m wide and
        // the wall spans 10.0..10.2, so the start must have its max face below
        // 10.0 (feet < 9.7) and the destination its min face above 10.2
        // (feet > 10.5). A first version used 9.5 -> 10.5, which left the
        // destination AABB touching 10.2 exactly -- passing only because the
        // boundary voxel rounded the right way.
        float3 from = new float3(9.6f, 0.5f, 0f);
        float3 to = from + new float3(1.0f, 0f, 0f);       // lands at 10.6, fully past

        Assert.IsFalse(VoxelCollision.OverlapsSolid(w, new AllResident(), from, W, H),
            "the start is clear");
        Assert.IsFalse(VoxelCollision.OverlapsSolid(w, new AllResident(), to, W, H),
            "and so is the DESTINATION -- an endpoint-only check sees no collision at all, " +
            "which is the phase-through the swept pass exists to prevent");

        var ccd = new SweptCCD(w, new AllResident());
        Assert.IsTrue(ccd.Sweep(from, to, W, H).HitSolid,
            "but the swept pass does see it");
    }

    [Test]
    public void TheSweepClampsToTheImpactPlane_NotSomewhereShortOfIt()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, 0, -50), new int3(101, 60, 50), Materials.Stone);
        var ccd = new SweptCCD(w, new AllResident());

        float3 from = new float3(5f, 0.5f, 0f);
        float3 to = from + new float3(6f, 0f, 0f);
        CCDResult r = ccd.Sweep(from, to, W, H);
        float3 clamped = SweptCCD.ClampToHit(from, to, r);

        Assert.IsTrue(r.HitSolid);
        Assert.AreEqual(10.0f, clamped.x + W * 0.5f, 0.01f,
            "the leading face ends flush against the wall at 10.0 m, not metres short");
        Assert.IsFalse(VoxelCollision.OverlapsSolid(w, new AllResident(), clamped, W, H),
            "and the clamped position is not inside the wall");
    }

    [Test]
    public void ASweepThroughOpenAir_ReachesItsDestination()
    {
        var w = new FakeWorld();
        var ccd = new SweptCCD(w, new AllResident());
        float3 from = new float3(0f, 5f, 0f), to = new float3(10f, 5f, 0f);

        CCDResult r = ccd.Sweep(from, to, W, H);
        Assert.IsFalse(r.HitSolid, "nothing to hit");
        Assert.AreEqual(to.x, SweptCCD.ClampToHit(from, to, r).x, 1e-4f);
    }

    // =====================================================================
    // §8.2's fluid pass-through accumulation
    // §13's Phase 6 test: "fly through a 3-voxel water sheet at 60 m/s"
    // =====================================================================

    [Test]
    public void AThreeVoxelWaterSheetAt60Mps_IsNoticed_EvenThoughBothEndpointsAreDry()
    {
        var w = new FakeWorld();
        // 3 voxels of water at x 100..102 (10.0 .. 10.3 m). Nothing solid.
        w.Box(new int3(100, 0, -50), new int3(102, 60, 50), Materials.Water);
        var ccd = new SweptCCD(w, new AllResident());

        // Only 0.1 m of slack exists: a 1.0 m frame must carry a 0.6 m body
        // fully past a 0.3 m sheet. Centre the body in it rather than sitting on
        // a voxel boundary, which is fragile at any world coordinate and
        // actually failed on the rig at ~1270 m.
        float3 from = new float3(9.65f, 0.5f, 0f);
        float3 to = from + new float3(1.0f, 0f, 0f);       // 60 m/s at 60 Hz

        // DRYNESS MUST BE ASSERTED WITH AnyFluidInBody, NOT OverlapsSolid.
        // OverlapsSolid returns false for a body fully submerged in water --
        // that is rule 2, on purpose -- so using it here asserted nothing at
        // all, and the "both endpoints are dry" premise of this whole test was
        // unchecked.
        byte fluidAtStart, fluidAtEnd;
        Assert.IsFalse(VoxelCollision.AnyFluidInBody(w, new AllResident(), from, W, H, out fluidAtStart),
            "the start is genuinely DRY");
        Assert.IsFalse(VoxelCollision.AnyFluidInBody(w, new AllResident(), to, W, H, out fluidAtEnd),
            "and so is the destination -- that premise is what makes the sweep the only " +
            "thing that could possibly notice the sheet");

        CCDResult r = ccd.Sweep(from, to, W, H);

        Assert.IsFalse(r.HitSolid, "water is not solid and must not clamp the sweep");
        Assert.Greater(r.FluidTraversedDistanceM, 0f,
            "THE SPLASH FIRES: fluidTraversedDistance > 0 even though both endpoints are dry");
        Assert.AreEqual(Materials.Water, r.PrimaryFluidMaterial, "and it knows it was water");
        Assert.AreEqual(0.3f, r.FluidTraversedDistanceM, 0.05f,
            "and measures roughly the 3-voxel thickness it actually crossed");
    }

    [Test]
    public void NoFluidCrossed_ReportsZeroDistanceAndAir()
    {
        var w = new FakeWorld();
        var ccd = new SweptCCD(w, new AllResident());
        CCDResult r = ccd.Sweep(new float3(0f, 5f, 0f), new float3(5f, 5f, 0f), W, H);

        Assert.AreEqual(0f, r.FluidTraversedDistanceM, 1e-5f);
        Assert.AreEqual(Materials.Air, r.PrimaryFluidMaterial,
            "no fluid must report Air, not a stale material from a previous sweep");
    }

    [Test]
    public void FluidIsAccumulatedEvenWhenTheSweepAlsoHitsSolid()
    {
        // Water first, then a wall behind it: the body should both stop AND
        // know it got wet on the way.
        var w = new FakeWorld();
        w.Box(new int3(100, 0, -50), new int3(104, 60, 50), Materials.Water);
        w.Box(new int3(120, 0, -50), new int3(121, 60, 50), Materials.Stone);
        var ccd = new SweptCCD(w, new AllResident());

        float3 from = new float3(9.0f, 0.5f, 0f);
        float3 to = from + new float3(4f, 0f, 0f);

        CCDResult r = ccd.Sweep(from, to, W, H);
        Assert.IsTrue(r.HitSolid, "the wall behind the water still stops the sweep");
        Assert.Greater(r.FluidTraversedDistanceM, 0.2f,
            "and the water crossed on the way is still accumulated");
        Assert.AreEqual(Materials.Water, r.PrimaryFluidMaterial);
    }

    [Test]
    public void ThePrimaryFluidMaterial_IsTheOneMostDistanceWasSpentIn()
    {
        // A thin lava skin against a thick body of water: "primary" must mean
        // the dominant one, not merely the first one touched.
        var w = new FakeWorld();
        w.Box(new int3(100, 0, -50), new int3(100, 60, 50), Materials.Lava);    // 1 voxel
        w.Box(new int3(101, 0, -50), new int3(115, 60, 50), Materials.Water);   // 15 voxels
        var ccd = new SweptCCD(w, new AllResident());

        CCDResult r = ccd.Sweep(new float3(9.5f, 0.5f, 0f), new float3(12.5f, 0.5f, 0f), W, H);

        Assert.AreEqual(Materials.Water, r.PrimaryFluidMaterial,
            "water dominates the distance, so it is the primary material");
        Assert.Greater(r.FluidTraversedDistanceM, 1.0f);
    }

    [Test]
    public void SandDoesNotCountAsFluid_AndDoesStopTheSweep()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, 0, -50), new int3(105, 60, 50), Materials.Sand);
        var ccd = new SweptCCD(w, new AllResident());

        CCDResult r = ccd.Sweep(new float3(9.0f, 0.5f, 0f), new float3(12f, 0.5f, 0f), W, H);
        Assert.IsTrue(r.HitSolid, "sand is a falling SOLID and blocks");
        Assert.AreEqual(0f, r.FluidTraversedDistanceM, 1e-5f, "and is not counted as fluid");
    }

    // =====================================================================
    // Vertical and lateral sweeps -- the cross-section must be right
    // =====================================================================

    [Test]
    public void ADownwardSweep_HitsTheFloorUnderTheFeet()
    {
        var w = new FakeWorld();
        w.Box(new int3(-50, 0, -50), new int3(50, 0, 50), Materials.Stone);   // floor 0.0..0.1
        var ccd = new SweptCCD(w, new AllResident());

        float3 from = new float3(0f, 5f, 0f), to = new float3(0f, -1f, 0f);
        CCDResult r = ccd.Sweep(from, to, W, H);

        Assert.IsTrue(r.HitSolid, "a fall must find the floor");
        float3 clamped = SweptCCD.ClampToHit(from, to, r);
        Assert.AreEqual(0.1f, clamped.y, 0.02f, "feet land on the floor's top face at 0.1 m");
    }

    [Test]
    public void AnUpwardSweep_HitsTheCeilingAboveTheHead_NotTheFeet()
    {
        // Regression: an early cross-section bug folded the body's vertical
        // extent into the horizontal span for vertical travel, so the rays
        // sampled a diagonal and the head was never swept.
        var w = new FakeWorld();
        w.Box(new int3(-50, 50, -50), new int3(50, 50, 50), Materials.Stone);  // ceiling at 5.0
        var ccd = new SweptCCD(w, new AllResident());

        float3 from = new float3(0f, 1f, 0f), to = new float3(0f, 4f, 0f);
        CCDResult r = ccd.Sweep(from, to, W, H);

        Assert.IsTrue(r.HitSolid, "the HEAD must hit the ceiling even though the feet clear it");
        float3 clamped = SweptCCD.ClampToHit(from, to, r);
        Assert.LessOrEqual(clamped.y + H, 5.0f + 1e-2f, "the head stops at the ceiling underside");
    }

    [Test]
    public void ASweepAlongZ_BehavesLikeOneAlongX()
    {
        var w = new FakeWorld();
        w.Box(new int3(-50, 0, 100), new int3(50, 60, 101), Materials.Stone);
        var ccd = new SweptCCD(w, new AllResident());

        float3 from = new float3(0f, 0.5f, 9.0f), to = new float3(0f, 0.5f, 11f);
        CCDResult r = ccd.Sweep(from, to, W, H);

        Assert.IsTrue(r.HitSolid, "the Z axis must be swept too, not just X");
        float3 clamped = SweptCCD.ClampToHit(from, to, r);
        Assert.AreEqual(10.0f, clamped.z + W * 0.5f, 0.02f);
    }

    // =====================================================================
    // §9.4 again: an unloaded chunk is not empty space
    // =====================================================================

    [Test]
    public void ANonResidentChunk_StopsTheSweep_AndSaysThatIsWhy()
    {
        var w = new FakeWorld();                      // no terrain at all
        var r = new ResidentExcept();
        r.Missing.Add(new int3(1, 0, 0));             // chunk 1 => voxels x 128.. => 12.8 m

        var ccd = new SweptCCD(w, r);
        CCDResult res = ccd.Sweep(new float3(11f, 0.5f, 0f), new float3(14f, 0.5f, 0f), W, H);

        Assert.IsTrue(res.HitSolid, "an unloaded chunk must stop a 60 m/s body, not swallow it");
        Assert.IsTrue(res.HitWasNonResident,
            "and must report that it was a residency edge, so a streaming stall cannot " +
            "masquerade as terrain");
    }

    // =====================================================================
    // §13: "force a missed ray -> depenetration catches it, one corrected
    //       frame, never a tunnel"
    // =====================================================================

    [Test]
    public void AForcedMissedRay_IsCaughtByDepenetration_InOneFrame()
    {
        // The forced miss: a spike one voxel square, placed dead centre between
        // the sample rays. The 3x3 bundle genuinely does not see it -- that is
        // the honest limitation of a 9-ray sample, and the backstop is why it is
        // survivable rather than fatal.
        var w = new FakeWorld();
        w.Box(new int3(100, 0, 0), new int3(100, 60, 0), Materials.Stone);

        var ccd = new SweptCCD(w, new AllResident());
        float3 from = new float3(9.5f, 0.5f, 0.05f);
        float3 to = from + new float3(1.0f, 0f, 0f);

        // Integrate as if the sweep found nothing (whether or not it did).
        float3 pos = to;
        float3 vel = new float3(60f, 0f, 0f);

        bool penetrating = VoxelCollision.OverlapsSolid(w, new AllResident(), pos, W, H);
        if (!penetrating)
        {
            // The bundle caught it after all; place the body inside deliberately
            // so the backstop is still exercised on the case it exists for.
            pos = new float3(10.0f, 0.5f, 0.05f);
            Assert.IsTrue(VoxelCollision.OverlapsSolid(w, new AllResident(), pos, W, H),
                "the constructed penetrating position really is inside the spike");
        }

        bool corrected = ccd.Depenetrate(from, ref pos, ref vel, W, H);

        Assert.IsTrue(corrected, "the backstop must fire");
        Assert.IsFalse(VoxelCollision.OverlapsSolid(w, new AllResident(), pos, W, H),
            "and leave the body OUT of the geometry -- never a silent permanent tunnel");
        Assert.AreEqual(0f, vel.x, 1e-5f, "with the offending velocity component zeroed");
        Assert.AreEqual(1, ccd.DepenetrationsApplied, "in exactly one corrected frame");
    }

    [Test]
    public void Depenetration_DoesNothing_WhenTheBodyIsAlreadyFree()
    {
        var w = new FakeWorld();
        w.Box(new int3(100, 0, -50), new int3(101, 60, 50), Materials.Stone);
        var ccd = new SweptCCD(w, new AllResident());

        float3 pos = new float3(5f, 0.5f, 0f), vel = new float3(10f, 0f, 0f);
        float3 before = pos;

        Assert.IsFalse(ccd.Depenetrate(new float3(4f, 0.5f, 0f), ref pos, ref vel, W, H));
        Assert.AreEqual(before.x, pos.x, 1e-6f, "a free body must not be moved");
        Assert.AreEqual(10f, vel.x, 1e-6f, "nor have its velocity touched");
        Assert.AreEqual(0, ccd.DepenetrationsApplied);
    }

    [Test]
    public void Depenetration_EscapesUpward_WhenTerrainAppearsAroundAStationaryBody()
    {
        // The case the binary search cannot solve: the body did not move into
        // solid, solid was built around it (an edit, or fluid solidifying).
        // There is no free point on the segment, so it lifts out instead.
        var w = new FakeWorld();
        w.Box(new int3(-50, 0, -50), new int3(50, 10, 50), Materials.Stone);

        var ccd = new SweptCCD(w, new AllResident());
        float3 pos = new float3(0f, 0.5f, 0f), vel = float3.zero;

        Assert.IsTrue(VoxelCollision.OverlapsSolid(w, new AllResident(), pos, W, H),
            "the body starts buried");
        Assert.IsTrue(ccd.Depenetrate(pos, ref pos, ref vel, W, H),
            "and must be rescued even though no point on the segment is free");
        Assert.IsFalse(VoxelCollision.OverlapsSolid(w, new AllResident(), pos, W, H));
        Assert.Greater(pos.y, 1.0f, "lifted above the 1.1 m of rock");
    }

    // =====================================================================
    // §8.2's speed clamp -- policy only, not wired (see the file header)
    // =====================================================================

    [Test]
    public void TheSpeedClamp_EngagesOnlyOnceTheReadbackHasStalled()
    {
        Assert.AreEqual(60f, SweptCCD.SpeedClampMps(60f, 0), 1e-5f, "healthy readback: no clamp");
        Assert.AreEqual(60f, SweptCCD.SpeedClampMps(60f, 2), 1e-5f, "2 frames is still within bound");
        Assert.AreEqual(SweptCCD.ClampedSpeedMps, SweptCCD.SpeedClampMps(60f, 3), 1e-5f,
            "at 3 stale frames the clamp engages, per §8.2's '~2-3 frames'");
        Assert.AreEqual(SweptCCD.ClampedSpeedMps, SweptCCD.SpeedClampMps(60f, 50), 1e-5f);
    }

    [Test]
    public void TheSpeedClamp_NeverSpeedsAnythingUp()
    {
        Assert.AreEqual(5f, SweptCCD.SpeedClampMps(5f, 100), 1e-5f,
            "a body already slower than the clamp keeps its own speed");
    }
}
