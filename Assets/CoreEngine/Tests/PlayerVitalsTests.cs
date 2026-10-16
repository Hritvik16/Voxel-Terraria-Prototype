// Assets/CoreEngine/Tests/PlayerVitalsTests.cs
//
// Task 93d1 (GAMEPLAY_PHASE_PLAN.md step 9a): the acceptance "EditMode damage
// curves, oxygen drain/refill, respawn location". The curves are checked on
// PlayerVitals alone; the fall, the cushioning water and the lava pit through
// a real PlayerMotor on a synthetic world (PlayerMotorTests' shape), so the
// impact speed the motor reports is what the curve is fed.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

public class PlayerVitalsTests
{
    private sealed class FakeWorld : IWorldQuery
    {
        public readonly Dictionary<int3, byte> Cells = new Dictionary<int3, byte>();
        public int FloorY = int.MinValue;          // stone at this y, everywhere
        public int FluidTop = -1;                  // `Fluid` from y = 0 to FluidTop, everywhere
        public byte Fluid = Materials.Water;

        public byte GetVoxel(int3 v)
        {
            if (Cells.TryGetValue(v, out byte m)) return m;
            if (v.y == FloorY) return Materials.Stone;
            if (v.y >= 0 && v.y <= FluidTop) return Fluid;
            return Materials.Air;
        }

        public void Box(int3 lo, int3 hi, byte m)
        {
            for (int z = lo.z; z <= hi.z; z++)
            for (int y = lo.y; y <= hi.y; y++)
            for (int x = lo.x; x <= hi.x; x++)
                Cells[new int3(x, y, z)] = m;
        }
    }

    private sealed class AllResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => true;
    }

    private static PlayerConfig Cfg()
    {
        // Explicit, so a change to the shipped defaults cannot change what these tests mean.
        return new PlayerConfig
        {
            accelerationMps2 = 55f, maxSpeedMps = 5.2f, jumpImpulseMps = 6.2f, airControl = 0.35f,
            frictionMps2 = 60f, stepHeightVoxels = 3, coyoteTimeSeconds = 0.12f, gravityMps2 = 22f,
            terminalSpeedMps = 55f, bodyWidthM = 0.6f, bodyHeightM = 1.8f, maxSubstepVoxels = 0.5f,
            maxHealth = 100f, fallSafeHeightM = 4f, fallLethalHeightM = 20f,
            oxygenSeconds = 20f, oxygenRefillPerS = 5f, drownDamagePerS = 10f,
            lavaDamagePerS = 50f, acidDamagePerS = 20f, regenDelayS = 8f, regenPerS = 2f, respawnDelayS = 3f,
        };
    }

    private const float Dt = 1f / 60f;

    private static void Run(PlayerVitals v, PlayerConfig c, VitalsInput i, float seconds)
    {
        int n = (int)math.round(seconds / Dt);
        for (int k = 0; k < n; k++) v.Step(Dt, i, c);
    }

    /// Drops a body from `heightM` above the floor's top (y = 0 m) and ticks
    /// the vitals the way Playground does: Sense after each motor step.
    private static (PlayerVitals v, float impact) Drop(FakeWorld w, PlayerConfig c, float heightM)
    {
        var m = new PlayerMotor(w, new AllResident(), c);
        var v = new PlayerVitals(c);
        m.Teleport(new float3(0.05f, heightM, 0.05f));
        float impact = 0f;
        for (int k = 0; k < 600; k++)
        {
            m.Step(Dt, float2.zero, false);
            impact = math.max(impact, m.LastImpactDownMps);
            v.Step(Dt, PlayerVitals.Sense(w, new AllResident(), m.PositionM, c.bodyHeightM, m.LastImpactDownMps), c);
            if (m.Grounded && k > 5 && m.VelocityMps.y == 0f && impact > 0f) break;
        }
        return (v, impact);
    }

    // =====================================================================
    // Fall damage
    // =====================================================================

    [Test]
    public void FallCurve_IsZeroUpToTheSafeHeight_LinearInHeight_CappedAtLethal()
    {
        var c = Cfg();
        float D(float h) => PlayerVitals.FallDamage(c, PlayerVitals.ImpactSpeedMps(c, h));
        Assert.AreEqual(0f, D(0.87f), 1e-3f, "a jump's landing (0.87 m) is free");
        Assert.AreEqual(0f, D(4f), 1e-2f, "safe height: no damage");
        Assert.AreEqual(25f, D(8f), 0.05f, "8 m = 4 m over: 4 x 6.25 HP");
        Assert.AreEqual(50f, D(12f), 0.05f, "12 m: half of 100 HP");
        Assert.AreEqual(100f, D(20f), 0.05f, "lethal height: the whole health");
        Assert.AreEqual(100f, D(60f), 0.05f, "capped: never more than maxHealth");
        for (float h = 4f; h < 20f; h += 0.5f) Assert.Less(D(h), D(h + 0.5f), $"rises with height at {h} m");
        Assert.AreEqual(12f, PlayerVitals.FallHeightM(c, PlayerVitals.ImpactSpeedMps(c, 12f)), 1e-3f, "v <-> h round trip");
    }

    [Test]
    public void ARealFall_ThroughTheMotor_DamagesByTheCurve()
    {
        var c = Cfg();
        var w = new FakeWorld { FloorY = -1 };

        var (v3, i3) = Drop(w, c, 3f);
        Assert.Greater(i3, 0f, "the motor reports the landing");
        Assert.AreEqual(100f, v3.Health, 1e-3f, "a 3 m drop is under the safe height");

        var (v12, i12) = Drop(w, c, 12f);
        Assert.AreEqual(PlayerVitals.ImpactSpeedMps(c, 12f), i12, 0.6f, "impact speed ~ sqrt(2 g h) (discrete steps)");
        Assert.AreEqual(50f, 100f - v12.Health, 4f, "12 m: about half the health");
        Assert.AreEqual(DamageCause.Fall, v12.LastCause);
        Assert.IsFalse(v12.Dead);

        var (v25, _) = Drop(w, c, 25f);
        Assert.IsTrue(v25.Dead, "25 m is over the lethal height");
        Assert.AreEqual(0f, v25.Health);
        Assert.AreEqual(1, v25.Deaths);
    }

    [Test]
    public void LandingWithTheFeetInALiquid_IsCushioned()
    {
        var c = Cfg();
        // 0.3 m of water over a stone floor: the body still hits the floor at speed.
        var w = new FakeWorld { FloorY = -1, FluidTop = 2, Fluid = Materials.Water };
        var (v, impact) = Drop(w, c, 15f);
        Assert.Greater(impact, PlayerVitals.ImpactSpeedMps(c, c.fallSafeHeightM), "the floor stopped a fast fall");
        Assert.AreEqual(100f, v.Health, 1e-3f, "but the water at the feet took it");
    }

    [Test]
    public void MovementSetA_DoubleJumpAndDash_DoNotThemselvesHurt()
    {
        var c = Cfg();
        c.airJumps = 1; c.airJumpImpulseScale = 0.85f; c.dashSpeedMps = 14f; c.dashDurationS = 0.18f; c.dashCooldownS = 0.8f;
        var w = new FakeWorld { FloorY = -1 };
        var m = new PlayerMotor(w, new AllResident(), c);
        var v = new PlayerVitals(c);
        m.Teleport(new float3(0.05f, 0f, 0.05f));
        for (int k = 0; k < 240; k++)
        {
            var input = MotorInput.FromLook(new float2(0f, 1f), 0f, 0f);
            input.JumpPressed = k == 10 || k == 30;
            input.DashPressed = k == 40;
            m.Step(Dt, input);
            v.Step(Dt, PlayerVitals.Sense(w, new AllResident(), m.PositionM, c.bodyHeightM, m.LastImpactDownMps), c);
        }
        Assert.AreEqual(100f, v.Health, 1e-3f, "a jump + double jump + dash landing is well under 4 m");
    }

    // =====================================================================
    // Oxygen
    // =====================================================================

    [Test]
    public void Oxygen_DrainsUnderALiquid_ThenDrowns_AndRefillsInAir()
    {
        var c = Cfg();
        var v = new PlayerVitals(c);
        var under = new VitalsInput { HeadInLiquid = true, FeetInLiquid = true };
        Run(v, c, under, 10f);
        Assert.AreEqual(10f, v.OxygenS, 0.05f, "10 s under: half the breath");
        Assert.AreEqual(100f, v.Health, "no damage at all while breath lasts (not even a rounding sliver)");
        Assert.AreEqual(DamageCause.None, v.LastCause, "and nothing hit (the HUD's red flash stays off)");
        Run(v, c, under, 10f);
        Assert.AreEqual(0f, v.OxygenS, 0.02f, "20 s: empty (1,200 float steps)");
        Assert.AreEqual(100f, v.Health, 0.2f, "drowning starts only now");
        Run(v, c, under, 5f);
        Assert.AreEqual(50f, v.Health, 0.3f, "5 s drowning at 10 HP/s");
        Assert.AreEqual(DamageCause.Drowning, v.LastCause);
        Run(v, c, new VitalsInput { FeetInLiquid = true }, 2f);
        Assert.AreEqual(10f, v.OxygenS, 0.1f, "head out: 5 s of breath per second");
        Run(v, c, new VitalsInput(), 2f);
        Assert.AreEqual(20f, v.OxygenS, 1e-4f, "full in 4 s, never over");
        Assert.AreEqual(50f, v.Health, 0.3f, "breathing again does not heal by itself");
    }

    [Test]
    public void Sense_ReadsTheHeadProbe_AndAGasIsNotALiquid()
    {
        var c = Cfg();
        var r = new AllResident();
        var pool = new FakeWorld { FloorY = -1, FluidTop = 29, Fluid = Materials.Water };   // 3 m of water
        var deep = PlayerVitals.Sense(pool, r, new float3(0.05f, 0f, 0.05f), c.bodyHeightM, 0f);
        Assert.IsTrue(deep.HeadInLiquid && deep.FeetInLiquid, "standing on the bed of 3 m of water: head under");
        var floating = PlayerVitals.Sense(pool, r, new float3(0.05f, 3f - 1.3f, 0.05f), c.bodyHeightM, 0f);
        Assert.IsFalse(floating.HeadInLiquid, "an idle swimmer floats 1.3 m deep: the head breathes");
        var steam = new FakeWorld { FloorY = -1, FluidTop = 29, Fluid = Materials.Steam };
        var s = PlayerVitals.Sense(steam, r, new float3(0.05f, 0f, 0.05f), c.bodyHeightM, 0f);
        Assert.IsFalse(s.HeadInLiquid || s.FeetInLiquid, "steam is a gas: breathable, no cushion");
    }

    // =====================================================================
    // Lava, acid, regen
    // =====================================================================

    [Test]
    public void Lava_And_Acid_HurtPerSecond_LavaKillsInTwoSeconds()
    {
        var c = Cfg();
        var r = new AllResident();
        var lava = new FakeWorld { FloorY = -1, FluidTop = 4, Fluid = Materials.Lava };   // 0.5 m: wading
        var i = PlayerVitals.Sense(lava, r, new float3(0.05f, 0f, 0.05f), c.bodyHeightM, 0f);
        Assert.IsTrue(i.TouchingLava && !i.TouchingAcid && !i.HeadInLiquid, "wading in lava touches it, head dry");
        var v = new PlayerVitals(c);
        Run(v, c, i, 1f);
        Assert.AreEqual(50f, v.Health, 0.3f, "1 s in lava: 50 HP");
        Assert.AreEqual(DamageCause.Lava, v.LastCause);
        Run(v, c, i, 1.1f);
        Assert.IsTrue(v.Dead, "2 s in lava kills");

        var acid = new FakeWorld { FloorY = -1, FluidTop = 4, Fluid = Materials.Acid };
        var a = PlayerVitals.Sense(acid, r, new float3(0.05f, 0f, 0.05f), c.bodyHeightM, 0f);
        Assert.IsTrue(a.TouchingAcid && !a.TouchingLava);
        var va = new PlayerVitals(c);
        Run(va, c, a, 1f);
        Assert.AreEqual(80f, va.Health, 0.3f, "1 s in acid: 20 HP");
        Assert.AreEqual(DamageCause.Acid, va.LastCause);

        var water = new FakeWorld { FloorY = -1, FluidTop = 4, Fluid = Materials.Water };
        var wi = PlayerVitals.Sense(water, r, new float3(0.05f, 0f, 0.05f), c.bodyHeightM, 0f);
        Assert.IsFalse(wi.TouchingLava || wi.TouchingAcid, "water is harmless");
    }

    [Test]
    public void Health_Regenerates_OnlyAfterTheDelay()
    {
        var c = Cfg();
        var v = new PlayerVitals(c);
        v.Step(Dt, new VitalsInput { ImpactDownMps = PlayerVitals.ImpactSpeedMps(c, 12f) }, c);
        Assert.AreEqual(50f, v.Health, 0.05f);
        Run(v, c, new VitalsInput(), 7.9f);
        Assert.AreEqual(50f, v.Health, 0.05f, "nothing back before regenDelayS");
        Run(v, c, new VitalsInput(), 5.1f);
        Assert.AreEqual(60f, v.Health, 0.3f, "then 2 HP/s");
        Run(v, c, new VitalsInput(), 100f);
        Assert.AreEqual(100f, v.Health, 1e-4f, "never over maxHealth");
    }

    // =====================================================================
    // Soft death and the respawn location
    // =====================================================================

    [Test]
    public void SoftDeath_WaitsTheDelay_ThenRespawnsFull_AtThePlayStart()
    {
        var c = Cfg();
        var r = new AllResident();
        // A lava pit 20 m from a dry stone start.
        var w = new FakeWorld { FloorY = -1 };
        w.Box(new int3(195, 0, -5), new int3(205, 9, 5), Materials.Lava);
        var start = new float3(0.05f, 0f, 0.05f);
        var m = new PlayerMotor(w, r, c);
        var v = new PlayerVitals(c);
        m.Teleport(new float3(20.05f, 0f, 0.05f));
        int frames = 0;
        while (!v.Dead && frames++ < 600)
        {
            m.Step(Dt, float2.zero, false);
            v.Step(Dt, PlayerVitals.Sense(w, r, m.PositionM, c.bodyHeightM, m.LastImpactDownMps), c);
        }
        Assert.IsTrue(v.Dead, "standing in the lava pit dies");
        Assert.AreEqual(DamageCause.Lava, v.LastCause);
        float3 deathAt = m.PositionM;

        Run(v, c, new VitalsInput { TouchingLava = true }, 2.9f);
        Assert.IsFalse(v.RespawnDue(c), "the death screen lasts respawnDelayS");
        Assert.AreEqual(0f, v.Health, "no damage or regen while dead");
        Run(v, c, new VitalsInput(), 0.2f);
        Assert.IsTrue(v.RespawnDue(c));

        float3 to = PlayerVitals.RespawnTarget(true, start, deathAt);
        Assert.AreEqual(start, to, "with a play start: back to it, not into the lava");
        Assert.AreEqual(deathAt, PlayerVitals.RespawnTarget(false, start, deathAt), "no play start: the death column");
        m.Teleport(to);
        Assert.IsTrue(m.ResolveSpawn());
        v.Respawn(c);
        Assert.IsFalse(v.Dead);
        Assert.AreEqual(100f, v.Health);
        Assert.AreEqual(20f, v.OxygenS);
        Assert.AreEqual(1, v.Deaths, "the death is counted once");
        for (int k = 0; k < 120; k++)
        {
            m.Step(Dt, float2.zero, false);
            v.Step(Dt, PlayerVitals.Sense(w, r, m.PositionM, c.bodyHeightM, m.LastImpactDownMps), c);
        }
        Assert.AreEqual(100f, v.Health, 1e-3f, "the start is safe ground");
        Assert.AreEqual(0f, math.length(m.PositionM.xz - start.xz), 1e-3f, "and the body stands there");
    }

    [Test]
    public void Validate_KeepsLethalAboveSafe()
    {
        var c = Cfg();
        c.fallSafeHeightM = 10f; c.fallLethalHeightM = 5f; c.maxHealth = -3f;
        string notes = c.Validate();
        Assert.IsNotNull(notes);
        Assert.Greater(c.fallLethalHeightM, c.fallSafeHeightM);
        Assert.GreaterOrEqual(c.maxHealth, 1f);
        Assert.IsFalse(float.IsNaN(PlayerVitals.FallDamage(c, 50f)));
    }
}
