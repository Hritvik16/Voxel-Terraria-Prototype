// Assets/CoreEngine/Tests/MovementSetATests.cs
//
// Task 65 (GAMEPLAY_PHASE_PLAN.md step 2): swim/dive, double jump, dash in
// PlayerMotor.Step(dt, in MotorInput). Same shape as PlayerMotorTests: a
// synthetic world stated voxel by voxel (here with fluids), everything
// resident, exact assertions. The legacy Step stays pinned by
// PlayerMotorTests; LegacyAndNewPathAgreeWithNoAbilityUsed proves the new
// entry point does the same thing when nothing new is used.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

public class MovementSetATests
{
    /// Solid boxes in a dictionary; the floor and the fluid layer are analytic
    /// (a 30 m deep pool as cells would be millions of entries).
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

        public void Floor(int y) => FloorY = y;

        /// A pool: stone floor at y = -1, `fluid` from y = 0 to topY (inclusive).
        public static FakeWorld Pool(byte fluid, int topY)
            => new FakeWorld { FloorY = -1, FluidTop = topY, Fluid = fluid };
    }

    private sealed class AllResident : IVoxelResidency
    {
        public bool IsResident(int3 c) => true;
    }

    private static PlayerConfig Cfg()
    {
        var c = new PlayerConfig
        {
            accelerationMps2 = 55f, maxSpeedMps = 5.2f, jumpImpulseMps = 6.2f, airControl = 0.35f,
            frictionMps2 = 60f, stepHeightVoxels = 3, coyoteTimeSeconds = 0.12f, gravityMps2 = 22f,
            terminalSpeedMps = 55f, bodyWidthM = 0.6f, bodyHeightM = 1.8f, maxSubstepVoxels = 0.5f,
            swimSpeedMps = 3.0f, swimAccelMps2 = 12f, swimFloatHeightM = 1.3f, swimFloatRiseMps = 1.0f,
            swimFloatGainPerS = 4f, waterJumpSwimLockS = 0.35f, airJumps = 1, airJumpImpulseScale = 0.85f,
            dashSpeedMps = 14f, dashDurationS = 0.18f, dashCooldownS = 0.8f,
        };
        return c;
    }

    private const float Dt = 1f / 60f;
    private const float Eps = 1e-3f;

    private static MotorInput Look(float2 move, float yaw = 0f, float pitch = 0f)
        => MotorInput.FromLook(move, yaw, pitch);

    private static PlayerMotor Motor(FakeWorld w, float3 feet, PlayerConfig c = null)
    {
        var m = new PlayerMotor(w, new AllResident(), c ?? Cfg());
        m.Teleport(feet);
        return m;
    }

    private static PlayerMotor Standing(FakeWorld w, float3 feet)
    {
        var m = Motor(w, feet);
        for (int i = 0; i < 240 && !m.Grounded; i++) m.Step(Dt, default(MotorInput));
        for (int i = 0; i < 30; i++) m.Step(Dt, default(MotorInput));
        Assert.IsTrue(m.Grounded, "setup: standing");
        return m;
    }

    // =====================================================================
    // Swim / dive
    // =====================================================================

    [Test]
    public void SwimLevel_ReachesThreeMetresPerSecond_AndNoMore()
    {
        var w = FakeWorld.Pool(Materials.Water, 59);            // surface at 6.0 m
        var m = Motor(w, new float3(0f, 4.7f, 0f));
        for (int i = 0; i < 180; i++) m.Step(Dt, default(MotorInput));   // settle at the surface
        Assert.IsTrue(m.Swimming);

        float max = 0f, last = 0f;
        for (int i = 0; i < 180; i++)
        {
            m.Step(Dt, Look(new float2(0f, 1f)));
            last = math.length(m.VelocityMps);
            max = math.max(max, last);
        }
        Assert.LessOrEqual(max, 3.0f + Eps, "never above the swim cap");
        Assert.GreaterOrEqual(math.length(m.VelocityMps.xz), 2.97f, "reaches 3.0 m/s level");
        Assert.Less(math.abs(m.VelocityMps.y), 0.05f, "level swimming stays level");
    }

    [Test]
    public void SwimUpAndDown_ReachThreeMetresPerSecond_AndNoMore()
    {
        var w = FakeWorld.Pool(Materials.Water, 299);           // 30 m deep
        var up = Motor(w, new float3(0f, 5f, 0f));
        float maxUp = 0f, maxSpeed = 0f;
        for (int i = 0; i < 120; i++)
        {
            up.Step(Dt, new MotorInput { UpHeld = true });
            maxUp = math.max(maxUp, up.VelocityMps.y);
            maxSpeed = math.max(maxSpeed, math.length(up.VelocityMps));
        }
        Assert.GreaterOrEqual(maxUp, 2.97f, "Space: up at 3.0 m/s");
        Assert.LessOrEqual(maxSpeed, 3.0f + Eps);

        var down = Motor(w, new float3(0f, 20f, 0f));
        float minY = 0f; maxSpeed = 0f;
        for (int i = 0; i < 120; i++)
        {
            down.Step(Dt, new MotorInput { DownHeld = true });
            minY = math.min(minY, down.VelocityMps.y);
            maxSpeed = math.max(maxSpeed, math.length(down.VelocityMps));
        }
        Assert.LessOrEqual(minY, -2.97f, "Ctrl: down at 3.0 m/s");
        Assert.LessOrEqual(maxSpeed, 3.0f + Eps);

        // Dive: look 60 degrees down and press W -- moves along the look direction.
        var dive = Motor(w, new float3(0f, 20f, 0f));
        for (int i = 0; i < 120; i++) dive.Step(Dt, Look(new float2(0f, 1f), 0f, 60f));
        Assert.AreEqual(-3f * math.sin(math.radians(60f)), dive.VelocityMps.y, 0.03f, "dive along the look");
        Assert.AreEqual(3f * math.cos(math.radians(60f)), dive.VelocityMps.z, 0.03f);
    }

    [Test]
    public void IdleSwimmer_FloatsToTheSurface_AndStaysThere()
    {
        var w = FakeWorld.Pool(Materials.Water, 59);            // surface at 6.0 m
        var m = Motor(w, new float3(0f, 1.0f, 0f));             // 3.7 m under the float point's rest
        for (int i = 0; i < 600; i++) m.Step(Dt, default(MotorInput));
        Assert.IsTrue(m.Swimming, "still swimming at the surface");
        Assert.AreEqual(6.0f - 1.3f, m.PositionM.y, 0.05f, "the 1.3 m float point sits at the water line");
        Assert.Less(math.abs(m.VelocityMps.y), 0.05f, "at rest, not bobbing");
        Assert.IsFalse(m.Grounded);
    }

    [Test]
    public void ThickerFluids_SwimSlower_AndSteamIsNotSwum()
    {
        var c = Cfg();
        var lava = Motor(FakeWorld.Pool(Materials.Lava, 299), new float3(0f, 10f, 0f), c);
        float cap = lava.SwimCapMps(Materials.Lava);
        Assert.AreEqual(3f * MaterialRules.ViscosityDrag(Materials.Water) / MaterialRules.ViscosityDrag(Materials.Lava), cap, 1e-4f);
        float max = 0f;
        for (int i = 0; i < 120; i++) { lava.Step(Dt, new MotorInput { UpHeld = true }); max = math.max(max, math.length(lava.VelocityMps)); }
        Assert.IsTrue(lava.Swimming);
        Assert.AreEqual(cap, max, 0.01f, "lava swims at its own (lower) cap");
        Assert.Less(cap, 1f);
        Assert.AreEqual(3f, new PlayerMotor(new FakeWorld(), new AllResident(), c).SwimCapMps(Materials.Water), 1e-4f);

        var steam = Motor(FakeWorld.Pool(Materials.Steam, 59), new float3(0f, 3f, 0f));
        for (int i = 0; i < 120; i++) steam.Step(Dt, default(MotorInput));
        Assert.IsFalse(steam.Swimming, "steam is a gas: nobody swims in it");
        Assert.IsTrue(steam.Grounded, "falls through it to the floor");
    }

    [Test]
    public void JumpAtTheSurface_LeavesTheWater()
    {
        var w = FakeWorld.Pool(Materials.Water, 59);
        var m = Motor(w, new float3(0f, 4.7f, 0f));
        for (int i = 0; i < 120; i++) m.Step(Dt, default(MotorInput));
        m.Step(Dt, new MotorInput { JumpPressed = true, UpHeld = true });
        Assert.IsTrue(m.LastWaterJump);
        Assert.IsFalse(m.Swimming);
        float top = m.PositionM.y;
        for (int i = 0; i < 30; i++) { m.Step(Dt, new MotorInput { UpHeld = true }); top = math.max(top, m.PositionM.y); }
        Assert.Greater(top, 4.7f + 0.5f, "the jump lifts the body well out");
    }

    [Test]
    public void LegacyStep_StillSinksInWater()
    {
        // The rig path: no swimming at all (PlayerMotorTests.APlayerDoesNotStandOnWater).
        var w = FakeWorld.Pool(Materials.Water, 59);
        var m = Motor(w, new float3(0f, 4.7f, 0f));
        for (int i = 0; i < 180; i++) m.Step(Dt, float2.zero, false);
        Assert.IsFalse(m.Swimming);
        Assert.Less(m.PositionM.y, 0.05f, "sinks to the floor");
    }

    // =====================================================================
    // Double jump
    // =====================================================================

    [Test]
    public void DoubleJump_OncePerAirtime_ResetsOnLanding()
    {
        var w = new FakeWorld();
        w.Floor(-1);
        var m = Standing(w, new float3(0f, 0.5f, 0f));
        var c = m.Config;

        m.Step(Dt, new MotorInput { JumpPressed = true });
        Assert.AreEqual(c.jumpImpulseMps - c.gravityMps2 * Dt, m.VelocityMps.y, 1e-4f, "ground jump");
        for (int i = 0; i < 20; i++) m.Step(Dt, default(MotorInput));
        Assert.IsFalse(m.Grounded);

        m.Step(Dt, new MotorInput { JumpPressed = true });
        Assert.IsTrue(m.LastDoubleJump, "one jump in the air");
        Assert.AreEqual(c.jumpImpulseMps * 0.85f - c.gravityMps2 * Dt, m.VelocityMps.y, 1e-4f, "at 0.85x the impulse");

        for (int i = 0; i < 5; i++) m.Step(Dt, default(MotorInput));
        float vy = m.VelocityMps.y;
        m.Step(Dt, new MotorInput { JumpPressed = true });
        Assert.IsFalse(m.LastDoubleJump, "only one");
        Assert.AreEqual(vy - c.gravityMps2 * Dt, m.VelocityMps.y, 1e-4f);

        for (int i = 0; i < 240 && !m.Grounded; i++) m.Step(Dt, default(MotorInput));
        Assert.IsTrue(m.Grounded);
        Assert.AreEqual(0, m.AirJumpsUsed, "landing resets it");
        m.Step(Dt, new MotorInput { JumpPressed = true });
        for (int i = 0; i < 20; i++) m.Step(Dt, default(MotorInput));
        m.Step(Dt, new MotorInput { JumpPressed = true });
        Assert.IsTrue(m.LastDoubleJump, "available again after landing");
    }

    [Test]
    public void DoubleJump_ResetsOnEnteringFluid()
    {
        var w = FakeWorld.Pool(Materials.Water, 59);
        var m = Motor(w, new float3(0f, 9f, 0f));               // 3 m above the water
        m.Step(Dt, default(MotorInput));
        m.Step(Dt, new MotorInput { JumpPressed = true });
        Assert.IsTrue(m.LastDoubleJump);
        Assert.AreEqual(1, m.AirJumpsUsed);
        for (int i = 0; i < 240 && !m.Swimming; i++) m.Step(Dt, default(MotorInput));
        Assert.IsTrue(m.Swimming);
        Assert.AreEqual(0, m.AirJumpsUsed, "entering fluid resets the air jump");
    }

    [Test]
    public void LegacyStep_HasNoDoubleJump()
    {
        var w = new FakeWorld();
        w.Floor(-1);
        var m = Standing(w, new float3(0f, 0.5f, 0f));
        m.Step(Dt, float2.zero, true);
        for (int i = 0; i < 20; i++) m.Step(Dt, float2.zero, false);
        float vy = m.VelocityMps.y;
        m.Step(Dt, float2.zero, true);
        Assert.AreEqual(vy - m.Config.gravityMps2 * Dt, m.VelocityMps.y, 1e-5f);
    }

    // =====================================================================
    // Dash
    // =====================================================================

    [TestCase(1f / 30f)]
    [TestCase(1f / 60f)]
    [TestCase(1f / 144f)]
    public void Dash_CoversAboutTwoAndAHalfMetres_OnFlatGround(float dt)
    {
        var w = new FakeWorld();
        w.Floor(-1);
        var m = Standing(w, new float3(0f, 0.5f, 0f));
        float3 start = m.PositionM;
        m.Step(dt, new MotorInput { DashPressed = true, ForwardXZ = new float2(1f, 0f) });
        Assert.IsTrue(m.LastDashStarted);
        float peak = 0f;
        int frames = 0;
        while (m.Dashing && frames++ < 200) { peak = math.max(peak, math.length(m.VelocityMps.xz)); m.Step(dt, default(MotorInput)); }
        float dashDist = m.PositionM.x - start.x;
        Assert.That(dashDist, Is.InRange(2.3f, 2.7f), "the dash itself: 14 m/s x 0.18 s");
        Assert.AreEqual(14f, peak, 0.01f, "peaks at 14 m/s");
        Assert.AreEqual(start.z, m.PositionM.z, 1e-5f, "along the look direction when no key is held");
        Assert.AreEqual(start.y, m.PositionM.y, 1e-4f, "level");
        for (int i = 0; i < 60; i++) m.Step(dt, default(MotorInput));
        Assert.Less(m.PositionM.x - start.x, 3.0f, "friction stops the exit run");
        Assert.IsTrue(m.Grounded);
    }

    [Test]
    public void Dash_FollowsTheMoveKeys_OverTheLook()
    {
        var w = new FakeWorld();
        w.Floor(-1);
        var m = Standing(w, new float3(0f, 0.5f, 0f));
        m.Step(Dt, new MotorInput { DashPressed = true, WishXZ = new float2(0f, -1f), ForwardXZ = new float2(1f, 0f) });
        Assert.AreEqual(new float2(0f, -1f), m.DashDir);
    }

    [TestCase(1f / 60f)]
    [TestCase(0.1f)]
    public void Dash_StopsAtAWall_WithoutEnteringIt(float dt)
    {
        var w = new FakeWorld();
        w.Floor(-1);
        w.Box(new int3(15, 0, -20), new int3(15, 40, 20), Materials.Stone);   // 1-voxel wall, face at x = 1.5 m
        var m = Standing(w, new float3(0f, 0.5f, 0f));
        m.Step(dt, new MotorInput { DashPressed = true, ForwardXZ = new float2(1f, 0f) });
        for (int i = 0; i < 40; i++)
        {
            Assert.IsFalse(m.OverlapsSolid(m.PositionM), $"frame {i}: inside the wall");
            m.Step(dt, new MotorInput { WishXZ = new float2(1f, 0f) });
        }
        float front = m.PositionM.x + m.Config.bodyWidthM * 0.5f;
        Assert.LessOrEqual(front, 1.5f, "0 penetration");
        Assert.Greater(front, 1.49f, "flush against it");
        Assert.IsFalse(m.Dashing);
    }

    [Test]
    public void Dash_CooldownIsRespected()
    {
        var w = new FakeWorld();
        w.Floor(-1);
        var m = Standing(w, new float3(0f, 0.5f, 0f));
        var dash = new MotorInput { DashPressed = true, ForwardXZ = new float2(1f, 0f) };
        m.Step(Dt, dash);
        Assert.IsTrue(m.LastDashStarted);
        int f = 1;
        for (; f < 200; f++)
        {
            m.Step(Dt, dash);
            if (m.LastDashStarted) break;
        }
        Assert.AreEqual(0.8f, f * Dt, Dt + 1e-4f, "the next dash comes 0.8 s after the first");
    }

    [Test]
    public void Dash_OncePerAirtime_EvenOffCooldown()
    {
        var w = new FakeWorld();
        w.Floor(-1);
        var c = Cfg();
        c.dashCooldownS = 0f;
        var m = Motor(w, new float3(0f, 0.5f, 0f), c);
        for (int i = 0; i < 60; i++) m.Step(Dt, default(MotorInput));
        m.Step(Dt, new MotorInput { JumpPressed = true });
        for (int i = 0; i < 3; i++) m.Step(Dt, default(MotorInput));
        var dash = new MotorInput { DashPressed = true, ForwardXZ = new float2(1f, 0f) };
        m.Step(Dt, dash);
        Assert.IsTrue(m.LastDashStarted, "first air dash");
        while (m.Dashing) m.Step(Dt, default(MotorInput));
        Assert.IsFalse(m.Grounded);
        m.Step(Dt, dash);
        Assert.IsFalse(m.LastDashStarted, "second dash in the same airtime refused");
        for (int i = 0; i < 240 && !m.Grounded; i++) m.Step(Dt, default(MotorInput));
        m.Step(Dt, dash);
        Assert.IsTrue(m.LastDashStarted, "landing gives it back");
    }

    [Test]
    public void NoDashWhileSwimming()
    {
        var m = Motor(FakeWorld.Pool(Materials.Water, 299), new float3(0f, 10f, 0f));
        m.Step(Dt, default(MotorInput));
        m.Step(Dt, new MotorInput { DashPressed = true, ForwardXZ = new float2(1f, 0f) });
        Assert.IsFalse(m.LastDashStarted);
    }

    // =====================================================================
    // The new entry point with nothing new used == the legacy Step
    // =====================================================================

    [Test]
    public void LegacyAndNewPathAgreeWithNoAbilityUsed()
    {
        var w = new FakeWorld();
        w.Floor(-1);
        w.Box(new int3(10, 0, -20), new int3(30, 2, 20), Materials.Stone);   // a 3-voxel step
        w.Box(new int3(31, 0, -20), new int3(60, 6, 20), Materials.Stone);   // a ledge to jump onto
        var a = Motor(w, new float3(0f, 1f, 0f));
        var b = Motor(w, new float3(0f, 1f, 0f));
        float topY = 0f;
        for (int i = 0; i < 400; i++)
        {
            float2 wish = i < 300 ? new float2(1f, (i % 50) < 25 ? 0.3f : -0.2f) : float2.zero;
            // Jump only when grounded (an air press would be a double jump on the new path).
            bool jump = (i % 40) == 0 && a.Grounded;
            a.Step(Dt, wish, jump);
            b.Step(Dt, new MotorInput { WishXZ = wish, JumpPressed = jump, ForwardXZ = new float2(0f, 1f) });
            Assert.AreEqual(a.PositionM, b.PositionM, $"frame {i} position");
            Assert.AreEqual(a.VelocityMps, b.VelocityMps, $"frame {i} velocity");
            Assert.AreEqual(a.Grounded, b.Grounded, $"frame {i} grounded");
            topY = math.max(topY, a.PositionM.y);
        }
        Assert.Greater(topY, 0.65f, "the scenario did step up and jump onto the ledge");
    }

    // =====================================================================
    // Input and config
    // =====================================================================

    [Test]
    public void FromLook_MatchesTheOldWalkFormula_AndTiltsWithPitch()
    {
        for (float yaw = -170f; yaw < 180f; yaw += 37f)
        {
            float2 mv = new float2(0.6f, -0.8f);
            float r = math.radians(yaw), s = math.sin(r), co = math.cos(r);
            var old = new float2(mv.x * co + mv.y * s, mv.y * co - mv.x * s);   // Playground.DriveWalk's
            var i = MotorInput.FromLook(mv, yaw, 25f);
            Assert.AreEqual(old.x, i.WishXZ.x, 1e-5f);
            Assert.AreEqual(old.y, i.WishXZ.y, 1e-5f);
        }
        var down = MotorInput.FromLook(new float2(0f, 1f), 30f, 90f);
        Assert.AreEqual(-1f, down.LookWish.y, 1e-5f, "looking straight down, W dives");
        var level = MotorInput.FromLook(new float2(0f, 1f), 90f, 0f);
        Assert.AreEqual(1f, level.LookWish.x, 1e-5f);
        Assert.AreEqual(1f, level.ForwardXZ.x, 1e-5f);
    }

    [Test]
    public void Config_NewFieldsDefault_AreFoundMissing_AndClamp()
    {
        string err;
        var parsed = PlayerConfig.Parse("{\"maxSpeedMps\": 6.0}", new PlayerConfig(), out err);
        Assert.IsNull(err);
        Assert.AreEqual(14f, parsed.dashSpeedMps, "a file from before task 65 gets the default dash");
        Assert.AreEqual(3f, parsed.swimSpeedMps);
        Assert.AreEqual(1, parsed.airJumps);
        StringAssert.Contains("dashSpeedMps", PlayerConfig.MissingFields("{\"maxSpeedMps\": 6.0}"));
        Assert.IsNull(PlayerConfig.MissingFields(new PlayerConfig().ToJson()));

        var bad = new PlayerConfig { swimFloatHeightM = 5f, airJumps = -2, dashSpeedMps = float.NaN };
        Assert.IsNotNull(bad.Validate());
        Assert.AreEqual(bad.bodyHeightM, bad.swimFloatHeightM, "float point capped inside the body");
        Assert.AreEqual(0, bad.airJumps);
        Assert.AreEqual(14f, bad.dashSpeedMps);
    }
}
