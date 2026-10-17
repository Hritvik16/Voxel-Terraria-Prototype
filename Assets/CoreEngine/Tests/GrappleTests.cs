// Assets/CoreEngine/Tests/GrappleTests.cs
//
// Task 100 (GAMEPLAY_PHASE_PLAN.md step 7): the grappling hook in
// PlayerMotor.Step(dt, in MotorInput). The plan's acceptance: the hook hits
// SOLID only; the pull stops at the wall. Plus the swing and the hand-overs
// (Space, dash, swim, climb, glide, landing, a dug-out anchor, the legacy
// Step). A synthetic world: a stone floor at y = -1, optional boxes of stone
// or water, everything resident unless a test says otherwise; the shipped
// PlayerConfig defaults.

using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

public class GrappleTests
{
    private sealed class BoxWorld : IWorldQuery
    {
        public readonly List<(int3 lo, int3 hi, byte m)> Boxes = new List<(int3, int3, byte)>();
        public readonly HashSet<int3> Dug = new HashSet<int3>();
        public bool Floor = true;
        public BoxWorld Add(int3 lo, int3 hi, byte m) { Boxes.Add((lo, hi, m)); return this; }
        public byte GetVoxel(int3 v)
        {
            if (Dug.Contains(v)) return Materials.Air;
            for (int i = Boxes.Count - 1; i >= 0; i--)
            {
                var b = Boxes[i];
                if (math.all(v >= b.lo) && math.all(v <= b.hi)) return b.m;
            }
            return Floor && v.y == -1 ? Materials.Stone : Materials.Air;
        }
    }

    private sealed class Residency : IVoxelResidency
    {
        public int MaxChunkX = int.MaxValue;     // chunks with x above this are not loaded
        public bool IsResident(int3 c) => c.x <= MaxChunkX;
    }

    private const float Dt = 1f / 60f;

    /// A wall whose near face is at x = faceX voxels (faceX * 0.1 m), 2 m thick, 40 m tall, 40 m wide.
    private static BoxWorld Wall(int faceX)
        => new BoxWorld().Add(new int3(faceX, 0, -200), new int3(faceX + 19, 399, 200), Materials.Stone);

    private static PlayerMotor At(BoxWorld w, float3 feet, Residency r = null)
    {
        var m = new PlayerMotor(w, r ?? new Residency(), new PlayerConfig());
        m.Teleport(feet);
        return m;
    }

    /// Input looking along (yaw, pitch) (Unity: yaw 90 = +x, pitch > 0 = down).
    private static MotorInput Look(float yaw, float pitch, float2 move = default)
        => MotorInput.FromLook(move, yaw, pitch);

    private static MotorInput Fire(float yaw, float pitch, bool hold)
    {
        var i = Look(yaw, pitch);
        i.HookPressed = true;
        i.HookHeld = hold;
        return i;
    }

    private static MotorInput Reel(float yaw = 90f)
    {
        var i = Look(yaw, 0f);
        i.HookHeld = true;
        return i;
    }

    // ------------------------------------------------------------------
    // Acceptance 1: the hook hits solid only.
    // ------------------------------------------------------------------

    [Test]
    public void Hook_PassesAirAndWater_BitesTheFirstSolidFace()
    {
        // Water 3..6 m out at eye height, the wall's face at 10 m.
        var w = Wall(100).Add(new int3(30, 0, -50), new int3(60, 30, 50), Materials.Water);
        var m = At(w, new float3(0.05f, 0f, 0.05f));
        m.Step(Dt, Fire(90f, 0f, false));
        Assert.IsTrue(m.LastHookFired && m.LastHookHit && m.Hooked, "bit the wall through the water");
        Assert.AreEqual(100, m.HookVoxel.x, "the first solid voxel, not the water");
        Assert.AreEqual(Materials.Stone, w.GetVoxel(m.HookVoxel));
        Assert.AreEqual(10f, m.HookAnchorM.x, 1e-3f, "the anchor is ON the face");
        Assert.AreEqual(m.PositionM.y + 1.6f, m.HookAnchorM.y, 0.05f, "at eye height, straight ahead");
    }

    [Test]
    public void Hook_Misses_BeyondRange_OrOpenSky()
    {
        var c = new PlayerConfig();
        // The face at 26 m: out of the 25 m range.
        var m = At(Wall((int)((c.hookRangeM + 1f) * 10f)), new float3(0.05f, 0f, 0.05f));
        m.Step(Dt, Fire(90f, 0f, true));
        Assert.IsTrue(m.LastHookFired);
        Assert.IsFalse(m.LastHookHit || m.Hooked, "nothing solid within range");

        // The sky (the wall is behind).
        var s = At(Wall(100), new float3(0.05f, 0f, 0.05f));
        s.Step(Dt, Fire(270f, -45f, true));
        Assert.IsFalse(s.Hooked, "open air is not an anchor");

        // Water only: a lake ahead and nothing behind it.
        var lake = new BoxWorld { Floor = false }.Add(new int3(20, -100, -50), new int3(400, 30, 50), Materials.Water);
        var l = At(lake, new float3(0.05f, 3f, 0.05f));
        l.Step(Dt, Fire(90f, 0f, true));
        Assert.IsFalse(l.Hooked, "fluid is never an anchor");

        // Just inside the range: bites.
        var n = At(Wall((int)((c.hookRangeM - 1f) * 10f)), new float3(0.05f, 0f, 0.05f));
        n.Step(Dt, Fire(90f, 0f, false));
        Assert.IsTrue(n.Hooked, "24 m: in range");
    }

    [Test]
    public void Hook_Misses_AnUnloadedChunk()
    {
        // The wall at x = 15 m = voxel 150, chunk x 1; chunk 1 is not loaded.
        var r = new Residency { MaxChunkX = 0 };
        var m = At(Wall(150), new float3(0.05f, 0f, 0.05f), r);
        m.Step(Dt, Fire(90f, 0f, true));
        Assert.IsTrue(m.LastHookFired);
        Assert.IsFalse(m.Hooked, "a chunk that has not streamed in is a miss, never an anchor");
        r.MaxChunkX = int.MaxValue;
        m.Step(Dt, Fire(90f, 0f, true));
        Assert.IsTrue(m.Hooked, "the same shot once it is loaded");
    }

    // ------------------------------------------------------------------
    // Acceptance 2: the pull stops at the wall.
    // ------------------------------------------------------------------

    [Test]
    public void Reel_StopsAtTheWall_NeverInIt()
    {
        var c = new PlayerConfig();
        var m = At(Wall(100), new float3(0.05f, 0f, 0.05f));
        m.Step(Dt, Fire(90f, 0f, true));
        Assert.IsTrue(m.Hooked);
        float top = 0f;
        int i = 0;
        for (; i < 180 && !m.HookArrived; i++)
        {
            m.Step(Dt, Reel());
            Assert.IsTrue(m.Reeling);
            Assert.IsFalse(m.OverlapsSolid(m.PositionM), $"frame {i}: inside the wall");
            // Within the collision skin (VoxelCollision.SkinM, what OverlapsSolid tolerates).
            Assert.LessOrEqual(m.PositionM.x + c.bodyWidthM * 0.5f, 10f + 2e-4f, $"frame {i}: past the face");
            top = math.max(top, math.length(m.VelocityMps));
        }
        Assert.IsTrue(m.HookArrived, "the reel ended");
        Assert.AreEqual(c.hookReelSpeedMps, top, 1e-3f, "reels at hookReelSpeedMps");
        Assert.AreEqual(10f - c.bodyWidthM * 0.5f, m.PositionM.x, 0.002f, "flush with the face");
        Assert.AreEqual(float3.zero, m.VelocityMps, "stopped");
        Assert.IsTrue(m.Hooked, "still hooked");
        Assert.AreEqual(10f / c.hookReelSpeedMps, i * Dt, 0.25f, "~distance / speed");

        // Holding on: it stays there (no gravity while reeling).
        float3 p = m.PositionM;
        for (int k = 0; k < 60; k++) m.Step(Dt, Reel());
        Assert.AreEqual(p.x, m.PositionM.x, 1e-4f);
        Assert.AreEqual(p.y, m.PositionM.y, 1e-4f);
    }

    [Test]
    public void Reel_UpASheerFace_HangsBelowTheAnchor()
    {
        var c = new PlayerConfig();
        // 6 m from the face, aim 45 deg up: the anchor is ~7.6 m up the wall.
        var m = At(Wall(60), new float3(0.05f, 0f, 0.05f));
        m.Step(Dt, Fire(90f, -45f, true));
        Assert.IsTrue(m.Hooked);
        float ay = m.HookAnchorM.y;
        Assert.Greater(ay, 6.5f);
        for (int i = 0; i < 240 && !m.HookArrived; i++)
        {
            m.Step(Dt, Reel());
            Assert.IsFalse(m.OverlapsSolid(m.PositionM), $"frame {i}: inside the wall");
        }
        Assert.IsTrue(m.HookArrived && m.Hooked);
        Assert.IsFalse(m.Grounded, "up the face");
        Assert.AreEqual(6f - c.bodyWidthM * 0.5f, m.PositionM.x, 0.002f, "against the face");
        Assert.LessOrEqual(math.length(m.HookAnchorM - m.RopeAttachM), c.hookMinLengthM + 0.05f, "hands at the anchor");

        // Let go of F: hangs on the rope (falls no further than the rope allows).
        for (int i = 0; i < 120; i++) m.Step(Dt, Look(90f, 0f));
        Assert.IsTrue(m.Hooked, "a hang is not a landing");
        Assert.LessOrEqual(math.length(m.HookAnchorM - m.RopeAttachM), m.RopeLengthM + 0.01f);
        Assert.Greater(m.PositionM.y, ay - 1.35f - c.hookMinLengthM - 0.05f, "held by the rope");
    }

    // ------------------------------------------------------------------
    // Swing.
    // ------------------------------------------------------------------

    /// A stone ceiling 10 m up (rows 100..109), the body at feet (-5, 4) m.
    private static PlayerMotor UnderCeiling(out BoxWorld w)
    {
        w = new BoxWorld().Add(new int3(-400, 100, -400), new int3(400, 109, 400), Materials.Stone);
        var m = At(w, new float3(-5f, 4f, 0.05f));
        // Aim at the ceiling over x = 0: from the eye (-5, 5.6) to (0, 10).
        float pitch = -math.degrees(math.atan2(4.4f, 5f));
        m.Step(Dt, Fire(90f, pitch, false));
        Assert.IsTrue(m.Hooked);
        Assert.AreEqual(0f, m.HookAnchorM.x, 0.05f);
        return m;
    }

    [Test]
    public void Swing_RopeHolds_AndCarriesYouUnderAndUp()
    {
        var m = UnderCeiling(out _);
        float L = m.RopeLengthM;
        float y0 = m.RopeAttachM.y;
        float bottomSpeed = 0f, farX = -10f;
        for (int i = 0; i < 150; i++)
        {
            m.Step(Dt, Look(90f, 0f));
            Assert.IsTrue(m.Hooked && !m.Grounded, $"frame {i}");
            Assert.LessOrEqual(math.length(m.RopeAttachM - m.HookAnchorM), L + 0.01f, $"frame {i}: the rope stretched");
            if (math.abs(m.PositionM.x) < 0.5f) bottomSpeed = math.max(bottomSpeed, math.length(m.VelocityMps));
            farX = math.max(farX, m.PositionM.x);
        }
        // Energy: v at the bottom ~ sqrt(2 g drop) (the drop from the hook point to the arc's foot).
        float drop = y0 - (m.HookAnchorM.y - L);
        Assert.Greater(bottomSpeed, 0.85f * math.sqrt(2f * 22f * drop), "the swing keeps most of its speed");
        Assert.Greater(farX, 3.5f, "and carries the body up the far side");
    }

    [Test]
    public void Space_LetsGo_KeepsMomentum_ThenDoubleJumpAndGlide()
    {
        var c = new PlayerConfig();
        var m = UnderCeiling(out _);
        for (int i = 0; i < 120 && m.PositionM.x < 0f; i++) m.Step(Dt, Look(90f, 0f));
        float vx = m.VelocityMps.x;
        Assert.Greater(vx, 5f);
        var j = Look(90f, 0f);
        j.JumpPressed = true;
        j.UpHeld = true;
        m.Step(Dt, j);
        Assert.IsTrue(m.LastHookReleased && !m.Hooked, "Space lets go");
        Assert.IsTrue(m.LastDoubleJump, "the same press is the air jump");
        Assert.Greater(m.VelocityMps.x, vx - 1f, "momentum kept");
        // Keep Space held: after the apex, a glide.
        var hold = Look(90f, 0f);
        hold.UpHeld = true;
        bool glided = false;
        for (int i = 0; i < 120 && !glided; i++) { m.Step(Dt, hold); glided = m.Gliding; }
        Assert.IsTrue(glided, "air jump spent, falling, Space held: glide");
        Assert.IsFalse(m.Hooked);
    }

    [Test]
    public void SwingLanding_EndsTheHook_AsANormalLanding()
    {
        var c = new PlayerConfig();
        // Feet 2 m up, a wall 20 m out: the rope is far longer than the height.
        var m = At(Wall(200), new float3(0.05f, 2f, 0.05f));
        m.Step(Dt, Fire(90f, -8f, false));
        Assert.IsTrue(m.Hooked);
        float impact = 0f;
        for (int i = 0; i < 120 && m.Hooked; i++) { m.Step(Dt, Look(90f, 0f)); impact = math.max(impact, m.LastImpactDownMps); }
        Assert.IsFalse(m.Hooked, "the landing ended the hook");
        Assert.IsTrue(m.LastHookReleased || m.Grounded);
        Assert.IsTrue(m.Grounded);
        Assert.Greater(impact, 0f, "a real landing, reported to the vitals");
        Assert.AreEqual(0f, PlayerVitals.FallDamage(c, impact), "a 2 m drop does not hurt");
    }

    [Test]
    public void ReelIntoTheGround_FromAbove_DoesNotHurt()
    {
        var c = new PlayerConfig();
        Assert.Less(c.hookReelSpeedMps, math.sqrt(2f * c.gravityMps2 * c.fallSafeHeightM), "reel under the 4 m speed");
        // 15 m up, hook the floor 10 m ahead and below; reel all the way down.
        var m = At(new BoxWorld(), new float3(0.05f, 15f, 0.05f));
        m.Step(Dt, Fire(90f, 50f, true));
        Assert.IsTrue(m.Hooked);
        float impact = 0f;
        for (int i = 0; i < 240 && !m.HookArrived; i++) { m.Step(Dt, Reel()); impact = math.max(impact, m.LastImpactDownMps); }
        Assert.IsTrue(m.Grounded, "reeled onto the floor");
        Assert.AreEqual(0f, PlayerVitals.FallDamage(c, impact));
    }

    // ------------------------------------------------------------------
    // Hand-overs.
    // ------------------------------------------------------------------

    [Test]
    public void Dash_Swim_Climb_DugAnchor_AndLegacyStep_EndTheHook()
    {
        // Dash.
        var d = At(Wall(100), new float3(0.05f, 0f, 0.05f));
        d.Step(Dt, Fire(90f, 0f, false));
        var dash = Look(90f, 0f);
        dash.DashPressed = true;
        d.Step(Dt, dash);
        Assert.IsTrue(d.Dashing && !d.Hooked, "a dash ends the hook");

        // Swim: water from the floor to 2 m; standing in it, the hook does not fire, and a hooked body entering it lets go.
        var ww = Wall(100).Add(new int3(-50, 0, -50), new int3(50, 19, 50), Materials.Water);
        var s = At(ww, new float3(0.05f, 0f, 0.05f));
        s.Step(Dt, Look(90f, 0f));
        Assert.IsTrue(s.Swimming);
        s.Step(Dt, Fire(90f, 0f, true));
        Assert.IsFalse(s.Hooked, "no hook from a swim");
        var s2 = At(ww, new float3(-8f, 0f, 0.05f));
        s2.Step(Dt, Fire(90f, 0f, true));
        Assert.IsTrue(s2.Hooked && !s2.Swimming);
        for (int i = 0; i < 120 && s2.Hooked; i++) s2.Step(Dt, Reel());
        Assert.IsTrue(s2.Swimming && !s2.Hooked, "reeled into the water: swimming takes over");

        // Climb: reel into the face holding W.
        var cl = At(Wall(60), new float3(0.05f, 0f, 0.05f));
        cl.Step(Dt, Fire(90f, -30f, true));
        Assert.IsTrue(cl.Hooked);
        var reelW = Look(90f, 0f, new float2(0f, 1f));
        reelW.HookHeld = true;
        for (int i = 0; i < 240 && !cl.Climbing; i++) cl.Step(Dt, reelW);
        Assert.IsTrue(cl.Climbing && !cl.Hooked, "a cling ends the hook");

        // The anchor dug out.
        var dw = Wall(100);
        var g = At(dw, new float3(0.05f, 0f, 0.05f));
        g.Step(Dt, Fire(90f, 0f, true));
        Assert.IsTrue(g.Hooked);
        g.Step(Dt, Reel());
        dw.Dug.Add(g.HookVoxel);
        g.Step(Dt, Reel());
        Assert.IsTrue(g.LastHookReleased && !g.Hooked, "the anchor voxel is gone");

        // The legacy Step (rigs) never hooks.
        var lg = At(Wall(100), new float3(0.05f, 0f, 0.05f));
        lg.Step(Dt, Fire(90f, 0f, true));
        Assert.IsTrue(lg.Hooked);
        lg.Step(Dt, new float2(1f, 0f), false);
        Assert.IsFalse(lg.Hooked);
        lg.Step(Dt, Look(90f, 0f));
        Assert.IsFalse(lg.Hooked, "and stays off");
    }

    [Test]
    public void FiringAgain_ReAims_AndRangeZero_TurnsItOff()
    {
        var m = At(Wall(100), new float3(0.05f, 0f, 0.05f));
        m.Step(Dt, Fire(90f, 0f, false));
        int3 first = m.HookVoxel;
        m.Step(Dt, Fire(90f, -30f, false));
        Assert.IsTrue(m.Hooked);
        Assert.Greater(m.HookVoxel.y, first.y + 30, "a new anchor, higher up");

        var off = At(Wall(100), new float3(0.05f, 0f, 0.05f));
        off.Config.hookRangeM = 0f;
        off.Step(Dt, Fire(90f, 0f, true));
        Assert.IsFalse(off.Hooked);
    }
}
