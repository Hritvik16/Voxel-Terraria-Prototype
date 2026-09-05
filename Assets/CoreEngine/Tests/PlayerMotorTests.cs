// Assets/CoreEngine/Tests/PlayerMotorTests.cs
//
// Correctness proof for §13 Phase 6 file 1's movement mechanism.
//
// These run against a synthetic world -- a HashSet of solid voxels and an
// explicit residency answer -- so every assertion is on an exact position, with
// no terrain generation, no GPU and no frames. The REAL-terrain half of the
// acceptance slice (uneven ground, live PlayerConfig reload, screenshots) is
// Phase6PlayerRig; see run-phase6-player.sh.
//
// WHY A FAKE WORLD AND NOT A ChunkStore: the cases that matter here are
// geometric edge cases -- a 3-voxel step versus a 4-voxel step, a one-voxel
// wall at run speed, a chunk that is not loaded. Building those in generated
// terrain would mean hunting for them; building them in a HashSet means stating
// them.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

public class PlayerMotorTests
{
    // =====================================================================
    // Fakes
    // =====================================================================

    private sealed class FakeWorld : IWorldQuery
    {
        public readonly HashSet<int3> Solid = new HashSet<int3>();
        public byte Fill = Materials.Stone;
        public byte GetVoxel(int3 v) => Solid.Contains(v) ? Fill : Materials.Air;

        /// Fills an inclusive voxel box.
        public void Box(int3 lo, int3 hi)
        {
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
                Solid.Add(new int3(x, y, z));
        }

        /// A floor plane at voxel y, spanning a generous area around the origin.
        public void Floor(int y) => Box(new int3(-200, y, -200), new int3(200, y, 200));
    }

    /// Everything resident. Named rather than passing null, because null would
    /// mean "reintroduce the §9.4 bug" and that should never be implicit.
    private sealed class AllResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => true;
    }

    private sealed class ResidentExcept : IVoxelResidency
    {
        public readonly HashSet<int3> Missing = new HashSet<int3>();
        public bool IsResident(int3 c) => !Missing.Contains(c);
    }

    private static PlayerConfig Cfg()
    {
        // Explicit rather than relying on field initialisers, so a change to the
        // shipped defaults cannot silently change what these tests mean.
        return new PlayerConfig
        {
            accelerationMps2 = 55f,
            maxSpeedMps = 5.2f,
            jumpImpulseMps = 6.2f,
            airControl = 0.35f,
            frictionMps2 = 60f,
            stepHeightVoxels = 3,
            coyoteTimeSeconds = 0.12f,
            gravityMps2 = 22f,
            terminalSpeedMps = 55f,
            bodyWidthM = 0.6f,
            bodyHeightM = 1.8f,
            maxSubstepVoxels = 0.5f,
        };
    }

    private const float Dt = 1f / 60f;

    /// Drops the player onto whatever is beneath and runs until settled.
    ///
    /// The second loop is not padding. Grounded goes true when the ground PROBE
    /// sees a surface, which is up to GroundProbeM (2 cm) before contact, so
    /// returning on that edge hands back a player still falling the last
    /// centimetre. From a big drop the approach speed hides it; from a 5 cm drop
    /// it does not, and the caller sees a position 1.3 cm off the floor.
    private static PlayerMotor Standing(FakeWorld w, IVoxelResidency r, PlayerConfig c, float3 startM)
    {
        var m = new PlayerMotor(w, r, c);
        m.Teleport(startM);
        for (int i = 0; i < 240 && !m.Grounded; i++) m.Step(Dt, float2.zero, false);
        for (int i = 0; i < 30; i++) m.Step(Dt, float2.zero, false);   // settle onto the surface
        return m;
    }

    // =====================================================================
    // Falling, landing, standing
    // =====================================================================

    [Test]
    public void APlayerFalls_LandsOnTheFloor_AndStaysThere()
    {
        var w = new FakeWorld();
        w.Floor(9);                       // solid voxel row y=9 => its top is y=1.0 m
        var m = Standing(w, new AllResident(), Cfg(), new float3(0f, 5f, 0f));

        Assert.IsTrue(m.Grounded, "the player must come to rest on the floor");
        Assert.AreEqual(1.0f, m.PositionM.y, 0.01f,
            "feet rest on top of voxel row 9, i.e. y = 1.0 m");

        float restY = m.PositionM.y;
        for (int i = 0; i < 120; i++) m.Step(Dt, float2.zero, false);
        Assert.AreEqual(restY, m.PositionM.y, 1e-3f, "and does not creep or sink while idle");
    }

    [Test]
    public void WithNoFloor_ThePlayerKeepsFalling_AndIsNeverGrounded()
    {
        var w = new FakeWorld();
        var m = new PlayerMotor(w, new AllResident(), Cfg());
        m.Teleport(new float3(0f, 50f, 0f));
        for (int i = 0; i < 60; i++) m.Step(Dt, float2.zero, false);

        Assert.Less(m.PositionM.y, 50f, "gravity applies");
        Assert.IsFalse(m.Grounded, "nothing to stand on");
    }

    // =====================================================================
    // §13's acceptance line: "3-voxel steps climbable un-jumped"
    // =====================================================================

    [Test]
    public void AThreeVoxelStep_IsClimbed_WithoutJumping()
    {
        var w = new FakeWorld();
        w.Floor(9);
        // A 3-voxel-tall ledge starting at x >= 2.0 m (voxel 20).
        w.Box(new int3(20, 10, -200), new int3(200, 12, 200));

        var m = Standing(w, new AllResident(), Cfg(), new float3(1.0f, 3f, 0f));
        float startY = m.PositionM.y;

        // Walk +X into the ledge for two seconds. NEVER jumps.
        for (int i = 0; i < 120; i++) m.Step(Dt, new float2(1f, 0f), false);

        Assert.Greater(m.PositionM.y, startY + 0.25f,
            $"the player must end up on top of the 3-voxel step (y {startY:F3} -> {m.PositionM.y:F3})");
        Assert.AreEqual(1.3f, m.PositionM.y, 0.02f, "which is the top of voxel row 12, y = 1.3 m");
        Assert.Greater(m.PositionM.x, 2.0f, "and must have got past the face of the step");
        Assert.IsTrue(m.Grounded, "standing on it, not falling past it");
    }

    [Test]
    public void AFourVoxelStep_IsNotClimbed_SoTheStepTestIsNotVacuous()
    {
        var w = new FakeWorld();
        w.Floor(9);
        w.Box(new int3(20, 10, -200), new int3(200, 13, 200));   // 4 voxels tall

        var m = Standing(w, new AllResident(), Cfg(), new float3(1.0f, 3f, 0f));
        float startY = m.PositionM.y;

        for (int i = 0; i < 120; i++) m.Step(Dt, new float2(1f, 0f), false);

        Assert.AreEqual(startY, m.PositionM.y, 0.02f,
            "a 4-voxel step exceeds stepHeightVoxels=3 and must NOT be climbed");
        Assert.Less(m.PositionM.x, 2.0f, "the player is stopped by its face");
    }

    [Test]
    public void StepHeightIsTunable_NotHardcoded()
    {
        // The same 4-voxel step the previous test refuses becomes climbable
        // purely by changing the config -- which is §8.1's whole point.
        var w = new FakeWorld();
        w.Floor(9);
        w.Box(new int3(20, 10, -200), new int3(200, 13, 200));

        var c = Cfg();
        c.stepHeightVoxels = 4;
        var m = Standing(w, new AllResident(), c, new float3(1.0f, 3f, 0f));
        float startY = m.PositionM.y;

        for (int i = 0; i < 120; i++) m.Step(Dt, new float2(1f, 0f), false);

        Assert.Greater(m.PositionM.y, startY + 0.25f,
            "with stepHeightVoxels=4 the same geometry is climbable");
    }

    // =====================================================================
    // §13's acceptance line: no tunneling at walk/run speeds
    // =====================================================================

    [Test]
    public void AOneVoxelWall_IsNeverTunneled_AtWalkAndRunSpeeds()
    {
        // Swept across a wide speed range, including well past the shipped
        // maxSpeed, because "no tunneling at walk/run" should not be true only
        // at the exact number the config happens to hold today.
        //
        // THE TOP TWO SPEEDS ARE WHAT GIVE THIS TEST TEETH, and they were added
        // after a mutation check showed it passing with substepping disabled.
        // The body is 0.6 m wide, so to skip a 1-voxel wall in a single
        // unsubstepped move the player must travel from maxX < 3.0 to
        // minX > 3.1 -- more than 0.7 m in one frame, i.e. faster than ~42 m/s
        // at 60 Hz. Below that the AABB still overlaps the wall at the end of
        // the move and gets snapped back whatever the step size, so the lower
        // speeds alone cannot distinguish a working substepper from none.
        // 50 and 60 m/s are past walk/run on purpose; they are here to prove the
        // MECHANISM, and they are not a claim about §8.2's grapple case, which
        // is SweptCCD's job in file 2.
        // START OFFSETS MATTER, and this too came out of a mutation check.
        // Whether an unsubstepped move skips the wall depends on where the frame
        // boundaries happen to land: from x=0 at 60 m/s the positions are
        // 1.0, 2.0, 3.0 and the AABB still clips the wall at 3.0, so nothing
        // tunnels even with substepping removed. From x=0.45 they are 1.45,
        // 2.45, 3.45 -- and 3.45 puts the whole 0.6 m body clear on the far
        // side. Sweeping the phase is what makes the tunneling assertion
        // capable of failing rather than merely true.
        var progress = new List<string>();
        foreach (float startX in new[] { 0f, 0.25f, 0.45f, 0.7f })
        foreach (float speed in new[] { 2f, 5.2f, 12f, 25f, 40f, 50f, 60f })
        {
            var w = new FakeWorld();
            w.Floor(9);
            // A wall exactly ONE voxel thick at x voxel 30 (3.0 .. 3.1 m),
            // tall enough that no step-up can clear it.
            w.Box(new int3(30, 10, -200), new int3(30, 40, 200));

            var c = Cfg();
            c.maxSpeedMps = speed;
            c.accelerationMps2 = 10000f;    // reach the speed immediately
            var m = Standing(w, new AllResident(), c, new float3(startX, 3f, 0f));

            for (int i = 0; i < 240; i++)
            {
                m.Step(Dt, new float2(1f, 0f), false);
                Assert.Less(m.PositionM.x + c.bodyWidthM * 0.5f, 3.0f + 1e-3f,
                    $"from x={startX} at {speed} m/s the player's leading face passed INTO " +
                    $"the wall at x=3.0 (x={m.PositionM.x:F4}, substeps={m.LastSubstepCount})");
            }

            // Deferred, not asserted here. These are "the player still moves
            // properly" checks, and letting one of them throw first would stop
            // the sweep before later phases got to run their TUNNELING check --
            // which is the assertion that actually matters and the one whose
            // teeth were being verified.
            if (m.PositionM.x <= 2.0f)
                progress.Add($"from x={startX} at {speed} m/s the player never reached the wall " +
                             $"(x={m.PositionM.x:F4})");
            else if (!m.LastBlockedHorizontally)
                progress.Add($"from x={startX} at {speed} m/s the wall did not register as a block");
        }

        CollectionAssert.IsEmpty(progress,
            "the player must actually travel up to the wall and be stopped by it, " +
            "not merely fail to pass through it: " + string.Join(" | ", progress));
    }

    [Test]
    public void TheSubstepCount_ScalesWithSpeed_WhichIsWhatPreventsTunneling()
    {
        var w = new FakeWorld();
        w.Floor(9);
        var c = Cfg();
        c.maxSpeedMps = 40f;
        c.accelerationMps2 = 10000f;
        var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));

        m.Step(Dt, new float2(1f, 0f), false);
        int fast = m.LastSubstepCount;

        // 40 m/s * (1/60) s = 0.667 m = 6.67 voxels; at 0.5 voxel per substep
        // that is at least 13 substeps.
        Assert.GreaterOrEqual(fast, 13,
            $"a 0.667 m move must be split into >=13 substeps, got {fast}");
    }

    // =====================================================================
    // Jump, and the config driving it (§8.1's live-tuning claim, at motor level)
    // =====================================================================

    [Test]
    public void JumpApexHeight_FollowsJumpImpulse_FromConfig()
    {
        float ApexFor(float impulse)
        {
            var w = new FakeWorld();
            w.Floor(9);
            var c = Cfg();
            c.jumpImpulseMps = impulse;
            var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));
            float floor = m.PositionM.y;

            m.Step(Dt, float2.zero, true);            // jump on this frame only
            float apex = m.PositionM.y;
            for (int i = 0; i < 200; i++)
            {
                m.Step(Dt, float2.zero, false);
                if (m.PositionM.y > apex) apex = m.PositionM.y;
                if (m.Grounded && i > 5) break;
            }
            return apex - floor;
        }

        float low = ApexFor(4f);
        float high = ApexFor(8f);

        // v^2/2g: 4^2/44 = 0.364 m, 8^2/44 = 1.455 m. Generous tolerance because
        // a discrete 60 Hz integrator lands slightly under the closed form.
        Assert.AreEqual(0.364f, low, 0.05f, "apex for a 4 m/s impulse");
        Assert.AreEqual(1.455f, high, 0.08f, "apex for an 8 m/s impulse");
        Assert.Greater(high, low * 3f, "and doubling the impulse must roughly quadruple the height");
    }

    [Test]
    public void ChangingJumpImpulseMidFlight_AffectsTheNextJump_NotTheCurrentOne()
    {
        // This is the motor-level half of the hot-reload acceptance item: the
        // motor reads Config every Step, so swapping the object mid-run is
        // enough. The rig proves the file watcher end of it.
        var w = new FakeWorld();
        w.Floor(9);
        var c = Cfg();
        c.jumpImpulseMps = 4f;
        var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));
        float floor = m.PositionM.y;

        m.Step(Dt, float2.zero, true);
        float apex1 = m.PositionM.y;
        for (int i = 0; i < 200; i++)
        {
            m.Step(Dt, float2.zero, false);
            if (m.PositionM.y > apex1) apex1 = m.PositionM.y;
            if (m.Grounded && i > 5) break;
        }

        var c2 = Cfg();
        c2.jumpImpulseMps = 8f;
        m.Config = c2;                                  // "the JSON changed"

        m.Step(Dt, float2.zero, true);
        float apex2 = m.PositionM.y;
        for (int i = 0; i < 200; i++)
        {
            m.Step(Dt, float2.zero, false);
            if (m.PositionM.y > apex2) apex2 = m.PositionM.y;
            if (m.Grounded && i > 5) break;
        }

        Assert.Greater(apex2 - floor, (apex1 - floor) * 2f,
            $"the next jump after the config change must be markedly higher " +
            $"({apex1 - floor:F3} m -> {apex2 - floor:F3} m)");
    }

    [Test]
    public void JumpDoesNothing_WhileAlreadyAirborne()
    {
        var w = new FakeWorld();
        w.Floor(9);
        var c = Cfg();
        c.coyoteTimeSeconds = 0f;
        var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));

        m.Step(Dt, float2.zero, true);
        for (int i = 0; i < 10; i++) m.Step(Dt, float2.zero, false);

        float yBefore = m.PositionM.y;
        float vBefore = m.VelocityMps.y;
        m.Step(Dt, float2.zero, true);                  // spam jump in mid-air

        Assert.Less(m.VelocityMps.y, vBefore + 1e-3f,
            "a mid-air jump must not add upward velocity (no double jump)");
        Assert.Less(m.PositionM.y, yBefore + 0.2f);
    }

    // =====================================================================
    // Coyote time
    // =====================================================================

    [Test]
    public void CoyoteTime_LetsAJumpLandJustAfterWalkingOffALedge()
    {
        var w = new FakeWorld();
        // A platform that ends at x voxel 30 (3.0 m); open air beyond.
        w.Box(new int3(-200, 9, -200), new int3(29, 9, 200));

        var c = Cfg();
        c.coyoteTimeSeconds = 0.12f;
        var m = Standing(w, new AllResident(), c, new float3(2.0f, 3f, 0f));

        // Walk off the edge.
        for (int i = 0; i < 200 && m.Grounded; i++) m.Step(Dt, new float2(1f, 0f), false);
        Assert.IsFalse(m.Grounded, "the player has left the ledge");

        // One frame later -- comfortably inside a 0.12 s window.
        float yBefore = m.PositionM.y;
        m.Step(Dt, new float2(1f, 0f), true);

        Assert.Greater(m.VelocityMps.y, 0f,
            "a jump within the coyote window must still fire");
        Assert.Greater(m.PositionM.y, yBefore, "and actually lift the player");
    }

    [Test]
    public void AfterCoyoteTimeExpires_TheJumpIsRefused()
    {
        var w = new FakeWorld();
        w.Box(new int3(-200, 9, -200), new int3(29, 9, 200));

        var c = Cfg();
        c.coyoteTimeSeconds = 0.05f;
        var m = Standing(w, new AllResident(), c, new float3(2.0f, 3f, 0f));

        for (int i = 0; i < 200 && m.Grounded; i++) m.Step(Dt, new float2(1f, 0f), false);
        Assert.IsFalse(m.Grounded);

        // Burn well past the window.
        for (int i = 0; i < 12; i++) m.Step(Dt, new float2(1f, 0f), false);
        Assert.Greater(m.TimeSinceGroundedS, c.coyoteTimeSeconds);

        float vBefore = m.VelocityMps.y;
        m.Step(Dt, new float2(1f, 0f), true);
        Assert.Less(m.VelocityMps.y, vBefore + 1e-3f,
            "past the coyote window a jump must be refused, or it is just a double jump");
    }

    [Test]
    public void CoyoteTime_IsSpentOnce_NotOncePerFrame()
    {
        var w = new FakeWorld();
        w.Box(new int3(-200, 9, -200), new int3(29, 9, 200));
        var m = Standing(w, new AllResident(), Cfg(), new float3(2.0f, 3f, 0f));

        for (int i = 0; i < 200 && m.Grounded; i++) m.Step(Dt, new float2(1f, 0f), false);

        m.Step(Dt, new float2(1f, 0f), true);           // the coyote jump
        float vAfterFirst = m.VelocityMps.y;
        m.Step(Dt, new float2(1f, 0f), true);           // holding jump

        Assert.Less(m.VelocityMps.y, vAfterFirst,
            "the second press must not re-fire from the same coyote window");
    }

    // =====================================================================
    // Speed, friction, air control
    // =====================================================================

    [Test]
    public void HorizontalSpeed_IsCappedByMaxSpeed()
    {
        var w = new FakeWorld();
        w.Floor(9);
        var c = Cfg();
        var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));

        for (int i = 0; i < 300; i++) m.Step(Dt, new float2(1f, 0f), false);

        float speed = math.length(new float2(m.VelocityMps.x, m.VelocityMps.z));
        Assert.AreEqual(c.maxSpeedMps, speed, 0.05f, "run speed settles at maxSpeedMps");
    }

    [Test]
    public void ADiagonalInput_IsNotFasterThanAStraightOne()
    {
        var w = new FakeWorld();
        w.Floor(9);
        var c = Cfg();
        var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));

        for (int i = 0; i < 300; i++) m.Step(Dt, new float2(1f, 1f), false);

        float speed = math.length(new float2(m.VelocityMps.x, m.VelocityMps.z));
        Assert.LessOrEqual(speed, c.maxSpeedMps + 0.05f,
            "diagonal movement must not exceed maxSpeedMps (the classic 1.41x bug)");
    }

    [Test]
    public void Friction_StopsThePlayer_WhenInputIsReleased()
    {
        var w = new FakeWorld();
        w.Floor(9);
        var m = Standing(w, new AllResident(), Cfg(), new float3(0f, 3f, 0f));

        for (int i = 0; i < 120; i++) m.Step(Dt, new float2(1f, 0f), false);
        Assert.Greater(math.abs(m.VelocityMps.x), 1f, "moving before release");

        for (int i = 0; i < 60; i++) m.Step(Dt, float2.zero, false);
        Assert.Less(math.length(new float2(m.VelocityMps.x, m.VelocityMps.z)), 0.01f,
            "friction brings the player to rest");
    }

    [Test]
    public void AirControl_IsWeakerThanGroundAcceleration()
    {
        float GainedIn(bool airborne, float airControl)
        {
            var w = new FakeWorld();
            w.Floor(9);
            var c = Cfg();
            c.airControl = airControl;
            var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));

            if (airborne)
            {
                m.Step(Dt, float2.zero, true);
                for (int i = 0; i < 5; i++) m.Step(Dt, float2.zero, false);
            }
            float before = m.VelocityMps.x;
            m.Step(Dt, new float2(1f, 0f), false);
            return m.VelocityMps.x - before;
        }

        float onGround = GainedIn(false, 0.35f);
        float inAir = GainedIn(true, 0.35f);

        Assert.Greater(onGround, 0f, "ground acceleration moves the player");
        Assert.Greater(inAir, 0f, "airControl 0.35 still allows some steering");
        Assert.AreEqual(0.35f, inAir / onGround, 0.05f,
            "air acceleration is airControl x ground acceleration");
    }

    [Test]
    public void AirControlZero_MeansNoSteeringInAir()
    {
        var w = new FakeWorld();
        w.Floor(9);
        var c = Cfg();
        c.airControl = 0f;
        var m = Standing(w, new AllResident(), c, new float3(0f, 3f, 0f));

        m.Step(Dt, float2.zero, true);
        for (int i = 0; i < 5; i++) m.Step(Dt, float2.zero, false);
        float before = m.VelocityMps.x;
        for (int i = 0; i < 10; i++) m.Step(Dt, new float2(1f, 0f), false);

        Assert.AreEqual(before, m.VelocityMps.x, 1e-4f, "airControl 0 means no mid-air steering");
    }

    // =====================================================================
    // Ceilings and fluid
    // =====================================================================

    [Test]
    public void JumpingIntoACeiling_StopsAtIt_AndDoesNotPenetrate()
    {
        var w = new FakeWorld();
        w.Floor(9);
        // Ceiling row at voxel y=30 => underside at 3.0 m. Body is 1.8 m, feet
        // rest at 1.0 m, so the head at 2.8 m has 0.2 m of clearance.
        w.Box(new int3(-200, 30, -200), new int3(200, 30, 200));

        var c = Cfg();
        c.jumpImpulseMps = 20f;                          // far more than enough
        // Spawn BELOW the ceiling. Spawning at y=3 puts the feet inside the
        // ceiling row itself, and the player starts embedded rather than jumping
        // into it -- which is a broken test, not a broken motor.
        var m = Standing(w, new AllResident(), c, new float3(0f, 1.05f, 0f));
        Assert.AreEqual(1.0f, m.PositionM.y, 0.01f, "starts on the floor, under the ceiling");

        m.Step(Dt, float2.zero, true);
        for (int i = 0; i < 120; i++)
        {
            m.Step(Dt, float2.zero, false);
            Assert.LessOrEqual(m.PositionM.y + c.bodyHeightM, 3.0f + 1e-3f,
                $"the head passed through the ceiling at 3.0 m (head={m.PositionM.y + c.bodyHeightM:F4})");
        }
    }

    [Test]
    public void WaterAndLavaDoNotBlockMovement_ButSandDoes()
    {
        // §8.2: "fluid doesn't block movement the way solid terrain does".
        // §8.6 gives buoyancy its own probes; this file is not it.
        var w = new FakeWorld();
        var r = new AllResident();
        var m = new PlayerMotor(w, r, Cfg());

        w.Solid.Add(new int3(0, 0, 0));

        w.Fill = Materials.Water;
        Assert.IsFalse(m.IsBlocking(new int3(0, 0, 0)), "water does not block");
        w.Fill = Materials.Lava;
        Assert.IsFalse(m.IsBlocking(new int3(0, 0, 0)), "lava does not block");
        w.Fill = Materials.Honey;
        Assert.IsFalse(m.IsBlocking(new int3(0, 0, 0)), "honey does not block");

        w.Fill = Materials.Sand;
        Assert.IsTrue(m.IsBlocking(new int3(0, 0, 0)),
            "sand DOES block -- it is a falling solid, not a fluid, and standing " +
            "on a sand pile has to work");
        w.Fill = Materials.Stone;
        Assert.IsTrue(m.IsBlocking(new int3(0, 0, 0)), "stone blocks");
    }

    [Test]
    public void APlayerDoesNotStandOnWater()
    {
        var w = new FakeWorld { Fill = Materials.Water };
        w.Floor(9);
        var m = new PlayerMotor(w, new AllResident(), Cfg());
        m.Teleport(new float3(0f, 5f, 0f));
        for (int i = 0; i < 120; i++) m.Step(Dt, float2.zero, false);

        Assert.IsFalse(m.Grounded, "water is not ground");
        Assert.Less(m.PositionM.y, 0.5f, "the player sinks straight through it");
    }

    // =====================================================================
    // THE §9.4 LESSON: Air is ambiguous between air and not-loaded
    // =====================================================================

    [Test]
    public void ANonResidentChunk_BlocksMovement_RatherThanReadingAsAir()
    {
        var w = new FakeWorld();
        w.Floor(9);

        // Evict the chunk the player would walk into. Chunks are 128 voxels, so
        // chunk (1,0,0) starts at voxel x=128, i.e. 12.8 m.
        var r = new ResidentExcept();
        r.Missing.Add(new int3(1, 0, 0));

        var c = Cfg();
        c.maxSpeedMps = 20f;
        c.accelerationMps2 = 10000f;
        var m = Standing(w, r, c, new float3(11.0f, 3f, 0f));

        for (int i = 0; i < 240; i++) m.Step(Dt, new float2(1f, 0f), false);

        Assert.Less(m.PositionM.x + c.bodyWidthM * 0.5f, 12.8f + 1e-3f,
            "the player must stop at the edge of the loaded world, not walk into it");
        Assert.IsTrue(m.LastBlockedByNonResident,
            "and the motor must SAY it was a residency edge, not ordinary terrain -- " +
            "otherwise a streaming problem is invisible and looks like a movement bug");
    }

    [Test]
    public void ANonResidentChunkUnderfoot_DoesNotDropThePlayer()
    {
        // The nastier half of §9.4: not walking INTO unloaded space, but having
        // the ground beneath you evict. Reading Air there means falling out of
        // the world.
        var w = new FakeWorld();          // deliberately NO solid voxels at all
        var r = new ResidentExcept();
        r.Missing.Add(new int3(50, 50, 50));   // somewhere else entirely

        var m = new PlayerMotor(w, new AllResident(), Cfg());
        m.Teleport(new float3(0f, 5f, 0f));
        for (int i = 0; i < 60; i++) m.Step(Dt, float2.zero, false);
        Assert.IsFalse(m.Grounded, "with everything resident and empty, the player falls");

        // Same empty world, but nothing is resident.
        var none = new ResidentExcept();
        none.Missing.Add(CoordMath.VoxelToChunk(new int3(0, 40, 0)));
        var m2 = new PlayerMotor(w, none, Cfg());
        m2.Teleport(new float3(0f, 4.05f, 0f));
        float y0 = m2.PositionM.y;
        for (int i = 0; i < 60; i++) m2.Step(Dt, float2.zero, false);

        Assert.AreEqual(y0, m2.PositionM.y, 0.05f,
            "over a NON-RESIDENT chunk the player must be held up, not dropped");
        Assert.IsTrue(m2.Grounded, "and read as grounded, because 'unknown' is not 'empty'");
    }

    [Test]
    public void BindingAControllerWithoutResidency_IsRejectedLoudly()
    {
        // A null residency would silently reinstate the §9.4 bug, so it must be
        // impossible to do by accident.
        var go = new UnityEngine.GameObject("pc");
        try
        {
            var pc = go.AddComponent<PlayerController>();
            Assert.Throws<System.ArgumentNullException>(() => pc.Bind(new FakeWorld(), null));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    // =====================================================================
    // Teleport / spawn
    // =====================================================================

    [Test]
    public void ResolveSpawn_LiftsThePlayerOutOfSolidTerrain()
    {
        var w = new FakeWorld();
        w.Box(new int3(-200, 0, -200), new int3(200, 19, 200));   // solid to y = 2.0 m

        var m = new PlayerMotor(w, new AllResident(), Cfg());
        m.Teleport(new float3(0f, 0.5f, 0f));                     // buried
        Assert.IsTrue(m.OverlapsSolid(m.PositionM), "spawn starts inside terrain");

        Assert.IsTrue(m.ResolveSpawn(), "resolve must succeed with only 2 m of rock");
        Assert.IsFalse(m.OverlapsSolid(m.PositionM), "and leave the player in free space");
        Assert.AreEqual(2.0f, m.PositionM.y, 0.11f, "sitting on the surface");
    }

    [Test]
    public void ResolveSpawn_FailsHonestly_WhenThereIsNoRoom()
    {
        var w = new FakeWorld();
        w.Box(new int3(-200, 0, -200), new int3(200, 200, 200));  // solid everywhere

        var m = new PlayerMotor(w, new AllResident(), Cfg());
        m.Teleport(new float3(0f, 1f, 0f));
        Assert.IsFalse(m.ResolveSpawn(8),
            "with nowhere to go it must report failure rather than pretending");
    }
}
