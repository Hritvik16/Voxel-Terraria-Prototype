// Assets/CoreEngine/Tests/GlideTests.cs
//
// Task 93g1 (GAMEPLAY_PHASE_PLAN.md step 6): gliding in PlayerMotor.Step(dt,
// in MotorInput). Acceptance: the glide ratio and the landing. A synthetic
// world (a stone floor at y = -1, optional water from y = 0), everything
// resident, the shipped PlayerConfig defaults.

using NUnit.Framework;
using Unity.Mathematics;

public class GlideTests
{
    private sealed class FloorWorld : IWorldQuery
    {
        public int WaterTop = -1;                  // water from y = 0 to WaterTop
        public byte GetVoxel(int3 v)
        {
            if (v.y == -1) return Materials.Stone;
            if (v.y >= 0 && v.y <= WaterTop) return Materials.Water;
            return Materials.Air;
        }
    }

    private sealed class AllResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => true;
    }

    private const float Dt = 1f / 60f;

    private static PlayerMotor InAir(float heightM, PlayerConfig c = null, FloorWorld w = null)
    {
        var m = new PlayerMotor(w ?? new FloorWorld(), new AllResident(), c ?? new PlayerConfig());
        m.Teleport(new float3(0.05f, heightM, 0.05f));
        return m;
    }

    private static MotorInput Hold(float2 move = default, float yaw = 0f)
    {
        var i = MotorInput.FromLook(move, yaw, 0f);
        i.UpHeld = true;
        return i;
    }

    /// The double jump, Space kept held.
    private static void DoubleJump(PlayerMotor m, float yaw = 0f)
    {
        var i = Hold(default, yaw);
        i.JumpPressed = true;
        m.Step(Dt, i);
        Assert.IsTrue(m.LastDoubleJump, "the press is the air jump");
    }

    [Test]
    public void GlideRatio_IsForwardSpeedOverSinkRate()
    {
        var c = new PlayerConfig();
        var m = InAir(60f, c);
        DoubleJump(m);
        for (int i = 0; i < 180; i++) m.Step(Dt, Hold());       // apex, opening, settling (3 s)
        Assert.IsTrue(m.Gliding);
        float3 p0 = m.PositionM;
        for (int i = 0; i < 300; i++) m.Step(Dt, Hold());       // 5 s steady
        float3 p1 = m.PositionM;
        float run = math.length((p1 - p0).xz), drop = p0.y - p1.y;
        Assert.IsTrue(m.Gliding && !m.Grounded);
        Assert.AreEqual(c.glideSinkMps, -m.VelocityMps.y, 1e-3f, "sink rate");
        Assert.AreEqual(c.glideSpeedMps, math.length(m.VelocityMps.xz), 1e-3f, "forward speed");
        Assert.AreEqual(c.glideSpeedMps / c.glideSinkMps, run / drop, 0.02f, "glide ratio");
        Assert.AreEqual(5f, run / drop, 0.02f, "the brief's 10 / 2");
        Assert.Greater(p1.z - p0.z, 0.99f * run, "along the look yaw (0 deg = +z)");
    }

    [Test]
    public void GlideLanding_IsSoft_NoFallDamage()
    {
        var c = new PlayerConfig();
        // Gliding from 30 m (a free fall from there is lethal).
        var m = InAir(30f, c);
        DoubleJump(m);
        float impact = 0f;
        int i = 0;
        for (; i < 60 * 30 && !m.Grounded; i++) { m.Step(Dt, Hold()); impact = math.max(impact, m.LastImpactDownMps); }
        Assert.IsTrue(m.Grounded, "landed");
        Assert.IsFalse(m.Gliding, "the glide ends on the ground");
        Assert.AreEqual(0f, m.PositionM.y, 0.01f, "feet on the floor");
        Assert.LessOrEqual(impact, c.glideSinkMps + 0.01f);
        Assert.AreEqual(0f, PlayerVitals.FallDamage(c, impact));
        float t = i * Dt;
        Assert.AreEqual(30f / c.glideSinkMps, t, 2.5f, "the drop takes ~height / sink");

        // The same drop without holding Space: a fall that hurts.
        var f = InAir(30f, c);
        DoubleJump(f);
        float fImpact = 0f;
        for (int k = 0; k < 600 && !f.Grounded; k++) { f.Step(Dt, default(MotorInput)); fImpact = math.max(fImpact, f.LastImpactDownMps); }
        Assert.IsTrue(f.Grounded);
        Assert.Greater(PlayerVitals.FallDamage(c, fImpact), 0f, "the control fall does damage");
    }

    [Test]
    public void NoGlide_BeforeTheDoubleJump()
    {
        var m = InAir(40f);
        for (int i = 0; i < 60; i++) { m.Step(Dt, Hold()); Assert.IsFalse(m.Gliding); }
        Assert.Less(m.VelocityMps.y, -10f, "falls freely: the air jump is not spent");
    }

    [Test]
    public void ReleasingSpace_FallsNormally()
    {
        var c = new PlayerConfig();
        var m = InAir(60f, c);
        DoubleJump(m);
        for (int i = 0; i < 120; i++) m.Step(Dt, Hold());
        Assert.IsTrue(m.Gliding);
        for (int i = 0; i < 30; i++) m.Step(Dt, default(MotorInput));
        Assert.IsFalse(m.Gliding);
        Assert.Less(m.VelocityMps.y, -c.glideSinkMps - 5f, "gravity again");
        // Holding Space again re-opens the glide (the air jump is still spent).
        for (int i = 0; i < 120; i++) m.Step(Dt, Hold());
        Assert.IsTrue(m.Gliding);
        Assert.AreEqual(c.glideSinkMps, -m.VelocityMps.y, 1e-3f, "braked back to the sink rate");
    }

    [Test]
    public void TheMouseSteers_SBrakes_ADDrift()
    {
        var c = new PlayerConfig();
        var m = InAir(80f, c);
        DoubleJump(m);
        for (int i = 0; i < 180; i++) m.Step(Dt, Hold(default, 90f));       // look +x
        Assert.AreEqual(c.glideSpeedMps, m.VelocityMps.x, 1e-3f);
        Assert.AreEqual(0f, m.VelocityMps.z, 1e-3f);
        for (int i = 0; i < 120; i++) m.Step(Dt, Hold(new float2(0f, -1f), 90f));   // S
        Assert.AreEqual(c.glideSlowSpeedMps, m.VelocityMps.x, 1e-3f, "S brakes");
        Assert.AreEqual(c.glideSinkMps, -m.VelocityMps.y, 1e-3f, "sink unchanged");
        for (int i = 0; i < 120; i++) m.Step(Dt, Hold(new float2(1f, 0f), 90f));    // D
        Assert.AreEqual(c.glideSpeedMps, m.VelocityMps.x, 1e-3f);
        Assert.AreEqual(-c.glideStrafeMps, m.VelocityMps.z, 1e-3f, "D drifts right of +x = -z");
    }

    [Test]
    public void GlideIntoWater_Swims()
    {
        var w = new FloorWorld { WaterTop = 59 };              // 6 m of water
        var m = InAir(20f, null, w);
        DoubleJump(m);
        bool glided = false;
        for (int i = 0; i < 60 * 12 && !m.Swimming; i++) { m.Step(Dt, Hold()); glided |= m.Gliding; }
        Assert.IsTrue(glided);
        Assert.IsTrue(m.Swimming, "lands in the water");
        Assert.IsFalse(m.Gliding);
    }

    [Test]
    public void GlideSpeedZero_TurnsItOff()
    {
        var c = new PlayerConfig { glideSpeedMps = 0f };
        var m = InAir(40f, c);
        DoubleJump(m);
        for (int i = 0; i < 90; i++) { m.Step(Dt, Hold()); Assert.IsFalse(m.Gliding); }
        Assert.Less(m.VelocityMps.y, -10f);
    }
}
