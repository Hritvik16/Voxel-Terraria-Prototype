// Assets/CoreEngine/Tests/ClimbSlopeTests.cs
//
// Task 93f1 (GAMEPLAY_PHASE_PLAN.md step 5): walkable slope + climbing in
// PlayerMotor.Step(dt, in MotorInput). Synthetic height-field worlds (a
// column height per (x, z), stone floor below y = 0), everything resident.
// Acceptance: a 25 deg ramp is walkable, 45 deg is not without jumping, 70 deg
// is climbable with stamina, stamina refills on a ledge, a 3-voxel step on flat
// ground is still taken un-jumped. The legacy Step is pinned by PlayerMotorTests.

using System;
using NUnit.Framework;
using Unity.Mathematics;

public class ClimbSlopeTests
{
    /// Columns: solid from the floor (y = -1, everywhere) up to Height(x, z) - 1.
    private sealed class HeightWorld : IWorldQuery
    {
        public Func<int, int, int> Height = (x, z) => 0;
        public byte Material = Materials.Stone;

        public byte GetVoxel(int3 v)
        {
            if (v.y < 0) return v.y == -1 ? Materials.Stone : Materials.Air;
            return v.y < Height(v.x, v.z) ? Material : Materials.Air;
        }
    }

    private sealed class AllResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => true;
    }

    private const float Dt = 1f / 60f;
    private const int RampStartX = 20;             // voxels: the ramp's first column (2 m)

    private static PlayerConfig Cfg() => new PlayerConfig();   // the shipped defaults

    /// A ramp rising along +x from RampStartX at `deg`, `lengthM` long, then a
    /// plateau at the ramp's top for 20 m, then nothing (the floor).
    private static HeightWorld Ramp(float deg, float lengthM, byte material = Materials.Stone)
    {
        double t = Math.Tan(deg * Math.PI / 180.0);
        int len = (int)Math.Round(lengthM * 10.0);
        int top = (int)Math.Floor(len * t + 1e-6);
        return new HeightWorld
        {
            Material = material,
            Height = (x, z) =>
            {
                if (z < -20 || z > 20 || x < RampStartX) return 0;
                int k = x - RampStartX;
                if (k < len) return (int)Math.Floor((k + 1) * t + 1e-6);
                return k < len + 200 ? top : 0;
            },
        };
    }

    private static PlayerMotor Motor(IWorldQuery w, float3 feet)
    {
        var m = new PlayerMotor(w, new AllResident(), Cfg());
        m.Teleport(feet);
        m.ResolveSpawn();
        for (int i = 0; i < 120 && !m.Grounded; i++) m.Step(Dt, default(MotorInput));
        return m;
    }

    /// W with the camera facing +x.
    private static MotorInput East(bool jump = false)
    {
        var i = MotorInput.FromLook(new float2(0f, 1f), 90f, 0f);
        i.JumpPressed = jump;
        return i;
    }

    // =====================================================================
    // Walk band
    // =====================================================================

    [Test]
    public void Ramp25_IsWalkable()
    {
        var w = Ramp(25f, 4f);                       // 1.86 m up
        var m = Motor(w, new float3(1.0f, 0f, 0.05f));
        bool slid = false;
        for (int i = 0; i < 240; i++) { m.Step(Dt, East()); slid |= m.Sliding; }
        Assert.GreaterOrEqual(m.PositionM.y, 1.8f, "walked to the top of a 25 deg ramp");
        Assert.Greater(m.PositionM.x, RampStartX * 0.1f + 4f, "and onto the plateau");
        Assert.IsFalse(slid, "a 25 deg ramp never slides");
    }

    [Test]
    public void Ramp45_IsNotWalkable_WithoutJumping_ButIsWithJumps()
    {
        var w = Ramp(45f, 4f);
        var walk = Motor(w, new float3(1.0f, 0f, 0.05f));
        float maxY = 0f;
        bool refused = false;
        for (int i = 0; i < 240; i++) { walk.Step(Dt, East()); maxY = math.max(maxY, walk.PositionM.y); refused |= walk.LastSteepRefused; }
        Assert.LessOrEqual(maxY, 0.35f, "holding W at a 45 deg ramp gains at most one auto-step");
        Assert.IsTrue(refused, "the auto-step was refused as steep");
        Assert.IsFalse(walk.Climbing, "45 deg is Scramble, not Climb");

        // Scramble: press Space every time the feet are down.
        var hop = Motor(w, new float3(1.0f, 0f, 0.05f));
        float hopMax = 0f;
        for (int i = 0; i < 360; i++)
        {
            hop.Step(Dt, East(jump: hop.Grounded));
            hopMax = math.max(hopMax, hop.PositionM.y);
        }
        Assert.GreaterOrEqual(hopMax, 1.5f, "jumping makes progress up a 45 deg slope");
    }

    [Test]
    public void Slope50_SlidesYouDownhill()
    {
        var w = Ramp(50f, 4f);                       // 4.77 m up
        // Stand half way up (ResolveSpawn lifts the body out of the slope).
        var m = Motor(w, new float3(RampStartX * 0.1f + 2f, 0f, 0.05f));
        float x0 = m.PositionM.x, y0 = m.PositionM.y;
        Assert.Greater(y0, 1.5f, "setup: on the slope");
        bool slid = false;
        for (int i = 0; i < 120; i++) { m.Step(Dt, default(MotorInput)); slid |= m.Sliding; }
        Assert.IsTrue(slid, "50 deg ground slides");
        Assert.Less(m.PositionM.x, x0 - 1f, "pushed downhill (-x) by more than 1 m in 2 s");
        Assert.Less(m.PositionM.y, y0 - 1f, "and down");

        // Holding W uphill does not hold you there.
        var up = Motor(w, new float3(RampStartX * 0.1f + 2f, 0f, 0.05f));
        float uy0 = up.PositionM.y;
        for (int i = 0; i < 120; i++) up.Step(Dt, East());
        Assert.Less(up.PositionM.y, uy0 - 0.5f, "W uphill on 50 deg still slides back");
    }

    [Test]
    public void ThreeVoxelStep_OnFlatGround_IsStillTakenUnjumped()
    {
        var w = new HeightWorld { Height = (x, z) => x >= RampStartX && x < RampStartX + 400 ? 3 : 0 };
        var m = Motor(w, new float3(1.0f, 0f, 0.05f));
        bool stepped = false;
        for (int i = 0; i < 90; i++) { m.Step(Dt, East()); stepped |= m.LastStepUpApplied; }
        Assert.IsTrue(stepped, "auto-step taken");
        Assert.AreEqual(0.3f, m.PositionM.y, 0.01f, "standing on the 0.3 m step");
        Assert.Greater(m.PositionM.x, RampStartX * 0.1f + 0.5f, "and walked on");
    }

    [Test]
    public void LedgeEdge_DoesNotSlide()
    {
        // A 2 m block, its edge at x = 2 m; stand with the centre 0.1 m from the edge.
        var w = new HeightWorld { Height = (x, z) => x < RampStartX ? 20 : 0 };
        var m = Motor(w, new float3(RampStartX * 0.1f - 0.1f, 2.0f, 0.05f));
        for (int i = 0; i < 60; i++) m.Step(Dt, default(MotorInput));
        Assert.IsFalse(m.Sliding, "a cliff edge is a ledge, not a slope");
        Assert.AreEqual(2.0f, m.PositionM.y, 0.01f, "still standing on it");
    }

    // =====================================================================
    // Climb band
    // =====================================================================

    [Test]
    public void Slope70_IsClimbable_WithStamina()
    {
        var w = Ramp(70f, 1.6f);                     // 4.4 m up, then a plateau
        var m = Motor(w, new float3(1.0f, 0f, 0.05f));
        float full = m.StaminaS;
        bool climbed = false;
        float minStamina = full;
        for (int i = 0; i < 600; i++)
        {
            m.Step(Dt, East());
            climbed |= m.Climbing;
            minStamina = math.min(minStamina, m.StaminaS);
            if (climbed && !m.Climbing && m.Grounded && m.PositionM.x > RampStartX * 0.1f + 1.9f) break;   // on the plateau
        }
        Assert.IsTrue(climbed, "clung to the 70 deg face");
        Assert.GreaterOrEqual(m.PositionM.y, 4.25f, "climbed and mantled onto the top");
        Assert.IsFalse(m.Climbing, "standing on the plateau, not clinging");
        Assert.Less(minStamina, full - 1.5f, "climbing spent stamina");
        Assert.Greater(minStamina, 0f, "a 4.4 m climb fits in one stamina bar");
    }

    [Test]
    public void TallWall_StaminaRunsOut_AndYouFall()
    {
        var w = new HeightWorld { Height = (x, z) => x >= RampStartX && x < RampStartX + 10 ? 200 : 0 };
        var m = Motor(w, new float3(1.5f, 0f, 0.05f));
        var c = m.Config;
        float maxY = 0f, minStamina = m.StaminaS;
        bool fell = false, regripped = false, wasEmpty = false;
        for (int i = 0; i < 900 && !fell; i++)
        {
            m.Step(Dt, East());
            maxY = math.max(maxY, m.PositionM.y);
            minStamina = math.min(minStamina, m.StaminaS);
            if (wasEmpty && m.Climbing) regripped = true;
            if (minStamina <= 0f && !m.Climbing) wasEmpty = true;      // let go
            fell = maxY > 1f && m.Grounded;
        }
        float reach = c.climbStaminaS * c.climbSpeedMps;   // 10.5 m
        Assert.AreEqual(0f, minStamina, 1e-4f, "stamina spent");
        Assert.AreEqual(reach, maxY, 0.6f, "climbed stamina x speed before letting go");
        Assert.IsTrue(wasEmpty && !regripped, "no grip on the way down with no stamina");
        Assert.IsTrue(fell, "fell back to the floor");
    }

    [Test]
    public void Stamina_RefillsOnALedge_NotOnAPerch()
    {
        // A 0.6 x 0.6 m pillar (a rest ledge) and a 0.4 x 0.4 m one (a perch), 1 m tall.
        var w = new HeightWorld
        {
            Height = (x, z) => (x >= 0 && x < 6 && z >= 0 && z < 6) ? 10
                             : (x >= 40 && x < 44 && z >= 0 && z < 4) ? 10 : 0,
        };
        var ledge = Motor(w, new float3(0.3f, 1.0f, 0.3f));
        ledge.StaminaS = 1f;
        for (int i = 0; i < 60; i++) ledge.Step(Dt, default(MotorInput));
        Assert.AreEqual(1.0f, ledge.PositionM.y, 0.01f, "setup: on the ledge");
        Assert.IsTrue(ledge.Resting, "a 0.6 m ledge is a rest");
        Assert.AreEqual(1f + ledge.Config.climbStaminaRefillPerS, ledge.StaminaS, 0.1f, "refilled for 1 s");
        for (int i = 0; i < 180; i++) ledge.Step(Dt, default(MotorInput));
        Assert.AreEqual(ledge.Config.climbStaminaS, ledge.StaminaS, 1e-4f, "full, and no more");

        var perch = Motor(w, new float3(4.2f, 1.0f, 0.2f));
        perch.StaminaS = 1f;
        for (int i = 0; i < 60; i++) perch.Step(Dt, default(MotorInput));
        Assert.AreEqual(1.0f, perch.PositionM.y, 0.01f, "setup: on the perch");
        Assert.IsFalse(perch.Resting, "a 0.4 m perch is not a rest");
        Assert.AreEqual(1f, perch.StaminaS, 1e-4f, "no refill on a perch");
    }

    [Test]
    public void IceAndSand_ClimbWorseThanRock()
    {
        float Climb(byte mat, out float spent, out byte grip)
        {
            var w = new HeightWorld { Material = mat, Height = (x, z) => x >= RampStartX && x < RampStartX + 10 ? 200 : 0 };
            var m = Motor(w, new float3(1.5f, 0f, 0.05f));
            int start = -1;
            float y0 = 0f;
            grip = 255;
            for (int i = 0; i < 600; i++)
            {
                m.Step(Dt, East());
                if (m.Climbing && start < 0) { start = i; y0 = m.PositionM.y; grip = m.ClimbGrip; }
                if (start >= 0 && i - start == 60) break;            // 1 s of climbing
            }
            spent = m.Config.climbStaminaS - m.StaminaS;
            return m.PositionM.y - y0;
        }
        float rock = Climb(Materials.Stone, out float rockSpent, out byte rockGrip);
        float sand = Climb(Materials.FromTg2(12), out float sandSpent, out byte sandGrip);
        float ice = Climb(Materials.FromTg2(22), out float iceSpent, out byte iceGrip);
        var c = new PlayerConfig();
        Assert.AreEqual(MaterialRules.GripRock, rockGrip);
        Assert.AreEqual(MaterialRules.GripLoose, sandGrip);
        Assert.AreEqual(MaterialRules.GripSlick, iceGrip);
        Assert.AreEqual(c.climbSpeedMps, rock, 0.05f, "rock: 1.5 m in 1 s");
        Assert.AreEqual(c.climbSpeedMps * c.climbLooseSpeedScale, sand, 0.05f, "sand climbs slower");
        Assert.AreEqual(c.climbSpeedMps * c.climbSlickSpeedScale, ice, 0.05f, "ice slower still");
        Assert.Greater(sandSpent, rockSpent * 1.3f, "sand tires faster");
        Assert.Greater(iceSpent, sandSpent * 1.3f, "ice tires fastest");
    }

    [Test]
    public void Climbing_LetsGo_WhenTheKeyIsReleased_AndCtrlClimbsDown()
    {
        var w = new HeightWorld { Height = (x, z) => x >= RampStartX && x < RampStartX + 10 ? 200 : 0 };
        var m = Motor(w, new float3(1.5f, 0f, 0.05f));
        for (int i = 0; i < 120; i++) m.Step(Dt, East());
        Assert.IsTrue(m.Climbing, "setup: clinging");
        float y = m.PositionM.y;
        var down = East();
        down.DownHeld = true;
        for (int i = 0; i < 20; i++) m.Step(Dt, down);
        Assert.Less(m.PositionM.y, y - 0.3f, "Ctrl climbs down");
        Assert.IsTrue(m.Climbing);
        m.Step(Dt, default(MotorInput));
        Assert.IsFalse(m.Climbing, "no key into the face: let go");
    }

    [Test]
    public void LegacyStep_KeepsTheOldStepUpEverywhere()
    {
        // Rigs run the legacy Step: no slope rule, no slide, no climb.
        var w = Ramp(45f, 2f);
        var m = new PlayerMotor(w, new AllResident(), Cfg());
        m.Teleport(new float3(1.0f, 0f, 0.05f));
        for (int i = 0; i < 240; i++) m.Step(Dt, new float2(1f, 0f), false);
        // (It stalls at 0.5 m: the legacy settle's float drift, see MoveAxisWithStepUp.)
        Assert.GreaterOrEqual(m.PositionM.y, 0.3f, "legacy auto-steps onto 45 deg ground the walk band refuses");
        Assert.IsFalse(m.Climbing || m.Sliding);
    }
}
