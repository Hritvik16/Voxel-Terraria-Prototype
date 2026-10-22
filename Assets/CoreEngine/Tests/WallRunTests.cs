// Assets/CoreEngine/Tests/WallRunTests.cs
//
// Task 103 (GAMEPLAY_PHASE_PLAN.md step 8): wall-run and wall-jump in
// PlayerMotor.Step(dt, in MotorInput). Acceptance: "EditMode on a synthetic
// wall". A stone floor at y = -1, wall A (x 1.0..1.5 m, 20 m tall, z from
// WallZ0 to WallZ1) and an optional wall B (x -1.5..-0.9 m: a 1.9 m chimney),
// everything resident, the shipped PlayerConfig defaults. The legacy Step
// never wall-runs (PlayerMotorTests pins the rest of it).

using NUnit.Framework;
using Unity.Mathematics;

public class WallRunTests
{
    private sealed class WallWorld : IWorldQuery
    {
        public int WallZ0 = -50, WallZ1 = 250;     // voxels, inclusive
        public bool WallB;

        public byte GetVoxel(int3 v)
        {
            if (v.y == -1) return Materials.Stone;
            if (v.y < 0 || v.y >= 200) return Materials.Air;
            if (v.x >= 10 && v.x <= 14 && v.z >= WallZ0 && v.z <= WallZ1) return Materials.Stone;
            if (WallB && v.x >= -15 && v.x <= -10 && v.z >= WallZ0 && v.z <= WallZ1) return Materials.Stone;
            return Materials.Air;
        }
    }

    private sealed class AllResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => true;
    }

    private const float Dt = 1f / 60f;
    /// Feet x for a body 6 cm off wall A's face (x = 1.0 m).
    private const float NearA = 0.64f;

    private static PlayerMotor Motor(WallWorld w, float3 feet)
    {
        var m = new PlayerMotor(w, new AllResident(), new PlayerConfig());
        m.Teleport(feet);
        for (int i = 0; i < 60 && !m.Grounded; i++) m.Step(Dt, default(MotorInput));
        return m;
    }

    /// Keys (strafe, forward) with the camera facing +z: (0, 1) = +z, (1, 0) = +x.
    private static MotorInput Keys(float2 move, bool jump = false)
    {
        var i = MotorInput.FromLook(move, 0f, 0f);
        i.JumpPressed = jump;
        return i;
    }

    /// Runs along +z beside wall A and jumps; returns once the run starts
    /// (or after 1 s without one).
    private static bool RunAndJump(PlayerMotor m, float keyScale = 1f)
    {
        var run = Keys(new float2(0f, keyScale));
        for (int i = 0; i < 30; i++) m.Step(Dt, run);
        m.Step(Dt, Keys(new float2(0f, keyScale), jump: true));
        for (int i = 0; i < 60; i++)
        {
            m.Step(Dt, run);
            if (m.WallRunning) return true;
        }
        return false;
    }

    [Test]
    public void WallRun_AlongAWall_HoldsSpeed_LosesLittleHeight()
    {
        var c = new PlayerConfig();
        var m = Motor(new WallWorld(), new float3(NearA, 0f, -4f));
        Assert.IsTrue(RunAndJump(m), "a jump along the wall starts a run");
        Assert.AreEqual(new int2(1, 0), m.WallDir, "the wall is on +x");
        float3 p0 = m.PositionM;
        float s0 = m.StaminaS, t = 0f, top = 0f;
        var run = Keys(new float2(0f, 1f));
        for (int i = 0; i < 200 && m.WallRunning; i++)
        {
            m.Step(Dt, run);
            if (m.WallRunning) { t += Dt; top = math.max(top, math.length(m.VelocityMps.xz)); }
            Assert.LessOrEqual(m.PositionM.x + c.bodyWidthM * 0.5f, 1f, "never inside the wall");
        }
        float3 p1 = m.PositionM;
        TestContext.WriteLine($"run {t:F2} s, {p1.z - p0.z:F2} m along, {p0.y - p1.y:F2} m lost, top {top:F2} m/s, stamina {s0:F2} -> {m.StaminaS:F2}");
        Assert.AreEqual(c.wallRunMaxS, t, 2.5f * Dt, "ends at wallRunMaxS on a long wall");
        Assert.AreEqual(c.wallRunSpeedMps, top, 1e-3f, "runs at wallRunSpeedMps");
        Assert.Greater(p1.z - p0.z, 7.5f, "7 m/s for 1.2 s");
        Assert.Less(math.abs(p0.y - p1.y), 0.5f, "holds about its height");
        Assert.AreEqual(s0 - t, m.StaminaS, 0.05f, "drains climb stamina 1 s per s");
        Assert.IsFalse(m.Grounded);
        // The same wall cannot be run again before landing.
        for (int i = 0; i < 30 && !m.Grounded; i++) { m.Step(Dt, run); Assert.IsFalse(m.WallRunning, "not the same wall twice"); }
    }

    [Test]
    public void WallRun_NeedsSpeed_ASlowJumpDoesNotStick()
    {
        var m = Motor(new WallWorld(), new float3(NearA, 0f, -4f));
        Assert.IsFalse(RunAndJump(m, 0.5f), "2.6 m/s is under wallRunMinSpeedMps");
    }

    [Test]
    public void WallRun_EndsWhereTheWallEnds()
    {
        var w = new WallWorld { WallZ1 = 39 };      // the wall stops at z = 4.0 m
        var m = Motor(w, new float3(NearA, 0f, -4f));
        Assert.IsTrue(RunAndJump(m));
        var run = Keys(new float2(0f, 1f));
        float t = 0f;
        for (int i = 0; i < 200 && m.WallRunning; i++) { m.Step(Dt, run); t += Dt; }
        Assert.IsFalse(m.WallRunning);
        Assert.Less(t, new PlayerConfig().wallRunMaxS, "ended by the wall, not the timer");
        Assert.Greater(m.PositionM.z, 4.0f, "at the wall's end ...");
        Assert.Less(m.PositionM.z, 4.0f + 2.1f * 7f * Dt, "... within two frames of it");
    }

    [Test]
    public void WallJump_PushesOffTheFaceAndUp_KeepsTheAirJump()
    {
        var c = new PlayerConfig();
        var m = Motor(new WallWorld(), new float3(NearA, 0f, 0f));
        m.Teleport(new float3(NearA, 3f, 0f));
        Assert.IsFalse(m.Grounded);
        float s0 = m.StaminaS;
        m.Step(Dt, Keys(new float2(1f, 0f), jump: true));
        Assert.IsTrue(m.LastWallJump);
        Assert.IsFalse(m.LastDoubleJump);
        Assert.AreEqual(0, m.AirJumpsUsed, "the air jump is kept");
        Assert.AreEqual(-c.wallJumpPushMps, m.VelocityMps.x, 1e-4f, "out of the face");
        Assert.AreEqual(c.jumpImpulseMps - c.gravityMps2 * Dt, m.VelocityMps.y, 1e-4f, "a full jump up");
        Assert.AreEqual(s0 - c.wallJumpStaminaS, m.StaminaS, 1e-4f);

        // Steer back into the same wall: Space there is the double jump, not a wall-jump.
        bool pressed = false;
        for (int i = 0; i < 120 && !pressed; i++)
        {
            bool at = m.WallBeside(0, 1, out _) && !m.Grounded;
            m.Step(Dt, Keys(new float2(1f, 0f), jump: at));
            if (at) { pressed = true; Assert.IsFalse(m.LastWallJump, "same wall twice"); Assert.IsTrue(m.LastDoubleJump); }
        }
        Assert.IsTrue(pressed, "got back to the wall");
    }

    [Test]
    public void WallJump_Chimney_AlternatesUp_UntilStaminaRunsOut()
    {
        var c = new PlayerConfig();
        var m = Motor(new WallWorld { WallB = true }, new float3(0.05f, 0f, 0f));
        int dir = 1, jumps = 0;
        float top = 0f;
        m.Step(Dt, Keys(new float2(dir, 0f), jump: true));
        for (int i = 0; i < 1200 && jumps < 7; i++)
        {
            bool at = !m.Grounded && m.WallBeside(0, dir, out _);
            m.Step(Dt, Keys(new float2(dir, 0f), jump: at));
            if (m.LastWallJump) { jumps++; dir = -dir; }
            Assert.IsFalse(m.Climbing || m.WallRunning);
            top = math.max(top, m.PositionM.y);
        }
        TestContext.WriteLine($"{jumps} wall-jumps, top {top:F2} m, stamina {m.StaminaS:F2}");
        Assert.AreEqual(7, jumps, "7 s of stamina / 1 s per jump");
        Assert.Greater(top, 4f, "a chimney is climbed by bouncing");
        Assert.AreEqual(0f, m.StaminaS, 1e-4f);
        // Stamina out: no eighth wall-jump.
        bool tried = false;
        for (int i = 0; i < 120 && !tried && !m.Grounded; i++)
        {
            bool at = m.WallBeside(0, dir, out _);
            m.Step(Dt, Keys(new float2(dir, 0f), jump: at));
            if (at) { tried = true; Assert.IsFalse(m.LastWallJump, "no stamina, no wall-jump"); }
        }
        Assert.IsTrue(tried);
    }

    [Test]
    public void Climb_ASlowPressIntoTheWallStillClings_AndTurningIntoItEndsARun()
    {
        var w = new WallWorld();
        var m = Motor(w, new float3(NearA, 0f, 0f));
        for (int i = 0; i < 40; i++) { m.Step(Dt, Keys(new float2(0.5f, 0f))); Assert.IsFalse(m.WallRunning); }
        Assert.IsTrue(m.Climbing, "a slow walk into a 20 m wall clings (step 5)");

        m = Motor(w, new float3(NearA, 0f, -4f));
        Assert.IsTrue(RunAndJump(m));
        m.Step(Dt, Keys(new float2(1f, 0f)));
        Assert.IsFalse(m.WallRunning, "keys into the wall end the run");
        for (int i = 0; i < 30 && !m.Climbing; i++) m.Step(Dt, Keys(new float2(1f, 0f)));
        Assert.IsTrue(m.Climbing, "... and the press clings");
    }

    [Test]
    public void DashAndHook_EndARun_GlideStaysAGlide()
    {
        var w = new WallWorld();
        var m = Motor(w, new float3(NearA, 0f, -4f));
        Assert.IsTrue(RunAndJump(m));
        var d = Keys(new float2(0f, 1f));
        d.DashPressed = true;
        m.Step(Dt, d);
        Assert.IsTrue(m.Dashing && !m.WallRunning, "dash ends the run");

        m = Motor(w, new float3(NearA, 0f, -4f));
        Assert.IsTrue(RunAndJump(m));
        var h = Keys(new float2(0f, 1f));
        h.HookPressed = true;
        h.LookDir = new float3(1f, 0f, 0f);
        m.Step(Dt, h);
        Assert.IsTrue(m.Hooked && !m.WallRunning, "the hook ends the run");

        // Air jumps spent, falling, Space held, moving fast along the wall: a glide.
        m = Motor(w, new float3(NearA, 0f, -4f));
        m.Teleport(new float3(NearA, 4f, 0f));
        m.VelocityMps = new float3(0f, -1f, 6f);
        m.AirJumpsUsed = m.Config.airJumps;
        var g = Keys(new float2(0f, 1f));
        g.UpHeld = true;
        m.Step(Dt, g);
        Assert.IsTrue(m.Gliding && !m.WallRunning, "a held glide is not caught by the wall");
    }

    [Test]
    public void LegacyStep_NeverWallRuns()
    {
        var m = Motor(new WallWorld(), new float3(NearA, 0f, -4f));
        var wish = new float2(0f, 1f);
        for (int i = 0; i < 30; i++) m.Step(Dt, wish, false);
        m.Step(Dt, wish, true);
        for (int i = 0; i < 90; i++)
        {
            m.Step(Dt, wish, false);
            Assert.IsFalse(m.WallRunning);
        }
        Assert.IsTrue(m.Grounded, "a plain jump, landed");
    }
}
