// Assets/Game/Playground.ClimbDemo.cs
//
// -climbdemo (task 93f1): gameplay step 5's acceptance screenshots. An
// INTERACTIVE run (run-play.sh EXTRA="-climbdemo -playshotquit"), so the HUD
// and the motor are exactly what a player gets; the coroutine only feeds
// MotorInput (Playground._demoInput) and builds a test site through
// EditService ahead of the play start: a stone pad with a 6 m stone wall, a
// 50 deg stone ramp falling away from the camera, and a 4 m packed-ice pillar.
// Noon, clock paused, sun behind the camera (the site lies away from the sun).
//   1  the site                                        (wall ahead, ramp right, ice left)
//   2  half way up the wall, looking up                (CLIMBING + the stamina bar)
//   3  half way up the wall, looking down              (the drop under the climber)
//   4  on top after the mantle, looking out            (stamina refilling: resting)
//   5  sliding down the 50 deg ramp                    (SLIDING; W uphill does nothing)
//   6  climbing the ice pillar                         (slick: slower, faster drain)
// It logs the climb speed, stamina spent, slide speed. Shots: persistentDataPath/ClimbDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator ClimbDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready || (!_poseReady && Time.unscaledTime - t0 < 60f))
            yield return null;
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "ClimbDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Walk;
        SnapPlayerToCamera();
        var m = _player.Motor;
        var c = PlayerConfig.Active;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int2 fw = new int2((int)math.round(math.sin(math.radians(awayYaw))), (int)math.round(math.cos(math.radians(awayYaw))));
        if (fw.x != 0 && fw.y != 0) fw.y = 0;   // an axis, so the wall is a box and the climb is straight
        int2 side = new int2(-fw.y, fw.x);
        float axisYaw = math.degrees(math.atan2(fw.x, fw.y));
        float3 startFeet = m.PositionM;
        int3 sv = CoordMath.WorldToVoxel(startFeet);
        Debug.Log($"[ClimbDemo] start feet {startFeet.x:F1},{startFeet.y:F1},{startFeet.z:F1} m, sun-away yaw {awayYaw:F0} -> axis {axisYaw:F0}; abilities {_moveAbilities}");
        if (!_moveAbilities) { Debug.Log("[ClimbDemo] movement abilities off -- stop"); Application.Quit(); yield break; }

        IEnumerator Idle(int frames, float yaw, float pitch, MotorInput? input = null)
        {
            for (int i = 0; i < frames; i++) { _cam?.SetLook(yaw, pitch); _demoInput = input ?? default(MotorInput); yield return null; }
        }
        // Voxel (k along fw, l along side, y) -> world voxel.
        int3 At(int k, int l, int y) => new int3(sv.x + fw.x * k + side.x * l, y, sv.z + fw.y * k + side.y * l);
        int Box(int k0, int k1, int l0, int l1, int y0, int y1, byte mat)
        {
            int3 a = At(k0, l0, y0), b = At(k1, l1, y1);
            return _edits.SetBox(math.min(a, b), math.max(a, b), mat);
        }
        float3 Feet(float k, float l, int row)
            => new float3((sv.x + fw.x * k + side.x * l) * 0.1f, row * 0.1f + 0.02f, (sv.z + fw.y * k + side.y * l) * 0.1f);

        // ---- The site: k 25..110 ahead, l -60..60 across; a pad whose top row is gy.
        int gy = sv.y - 1;
        for (int k = 25; k <= 110; k += 5)
            for (int l = -60; l <= 60; l += 10)
            {
                int3 p = At(k, l, 0);
                int g = DryTopY(p.x, p.z);
                if (g >= 0) gy = math.max(gy, g);
            }
        int written = Box(-30, 115, -70, 70, gy + 1, gy + 90, Materials.Air);
        written += Box(-30, 115, -70, 70, gy - 15, gy, Materials.Stone);
        // The wall: near face at k = 60, 1 m thick, 4 m wide, 6 m tall.
        const int wallK = 60, wallH = 60;
        written += Box(wallK, wallK + 9, -20, 19, gy + 1, gy + wallH, Materials.Stone);
        // The 50 deg ramp at l 35..64: top (4.9 m) at k = 60, falling along +fw to the pad at k = 100.
        float t50 = math.tan(math.radians(50f));
        for (int k = 60; k < 100; k++)
        {
            int h = (int)math.floor((100 - k) * t50);
            if (h > 0) written += Box(k, k, 35, 64, gy + 1, gy + h, Materials.Stone);
        }
        // The ice pillar at l -55..-36: 2 m wide, 4 m tall, near face at k = 60.
        byte ice = Materials.FromTg2(22);
        written += Box(wallK, wallK + 19, -55, -36, gy + 1, gy + 40, ice);
        Debug.Log($"[ClimbDemo] site pad top row {gy} ({gy * 0.1f:F1} m), wall 6 m at k {wallK}, 50 deg ramp at l 35..64, ice pillar 4 m; {written} voxels written");
        int feetRow = gy + 1;
        // An empty hotbar slot: no cyan place-preview box in the shots.
        for (int s = 0; s < Inventory.HotbarSize; s++) if (_inv.IsEmpty(s)) { _inv.Selected = s; break; }
        m.Teleport(Feet(-15, 0, feetRow));
        yield return Idle(120, axisYaw, -10f);

        // ---- 1. The site.
        yield return DemoShot(dir, "1_site");

        // ---- 2/3. Walk into the wall and hold W.
        m.Teleport(Feet(wallK - 8, 0, feetRow));
        var w = MotorInput.FromLook(new float2(0f, 1f), axisYaw, 0f);
        float climbStartT = -1f, climbStartY = 0f, s0 = 0f;
        bool shot2 = false, mantled = false;
        float topY = (gy + wallH + 1) * 0.1f;
        for (int i = 0; i < 1200; i++)
        {
            _cam?.SetLook(axisYaw, shot2 ? 20f : -60f);
            _demoInput = w;
            yield return null;
            if (m.Climbing && climbStartT < 0f) { climbStartT = Time.time; climbStartY = m.PositionM.y; s0 = m.StaminaS; }
            if (!shot2 && m.Climbing && m.PositionM.y > feetRow * 0.1f + 3f)
            {
                shot2 = true;
                float dt = Time.time - climbStartT;
                Debug.Log($"[ClimbDemo] 2 climbing {MaterialName(m.ClimbFace)} (grip {m.ClimbGrip}): {m.PositionM.y - climbStartY:F2} m in {dt:F2} s " +
                          $"= {(m.PositionM.y - climbStartY) / math.max(dt, 1e-3f):F2} m/s (config {c.climbSpeedMps:F1}); stamina {s0:F2} -> {m.StaminaS:F2} s");
                yield return DemoShot(dir, "2_climbing_wall_up");
                _cam?.SetLook(axisYaw, 70f);
                _demoInput = new MotorInput { WishXZ = w.WishXZ, LookWish = w.LookWish, ForwardXZ = w.ForwardXZ, DownHeld = true };
                yield return null;   // one frame: hold position-ish while the camera turns (Ctrl = down)
                _cam?.SetLook(axisYaw, 70f);
                _demoInput = w;
                yield return null;
                yield return DemoShot(dir, "3_climbing_wall_down");
            }
            if (shot2 && !m.Climbing && m.Grounded && m.PositionM.y > topY - 0.05f) { mantled = true; break; }
        }
        Debug.Log($"[ClimbDemo] mantled {mantled}: feet {m.PositionM.y:F2} m (wall top {topY:F2}), stamina {m.StaminaS:F2} of {c.climbStaminaS:F1} s, " +
                  $"climb {Time.time - climbStartT:F2} s");

        // ---- 4. On top: stand, turn round to look out over the site's near half.
        // Onto the 1 m top until the whole footprint is on it (a rest), not off its far side.
        for (int i = 0; i < 30 && !m.FootprintSupported(); i++) { _cam?.SetLook(axisYaw, 10f); _demoInput = w; yield return null; }
        float sTop = m.StaminaS;
        yield return Idle(12, axisYaw, 35f);   // part refilled, so the bar and "resting" show
        Debug.Log($"[ClimbDemo] 4 on top: resting {m.Resting}, stamina {sTop:F2} -> {m.StaminaS:F2} s after 12 frames (refill {c.climbStaminaRefillPerS:F1}/s)");
        yield return DemoShot(dir, "4_on_top_resting");

        // ---- 5. Half way down the 50 deg ramp, facing downhill (+fw), holding W UPHILL.
        int rk = 72, rl = 50;
        int rh = (int)math.floor((100 - rk) * t50);
        m.Teleport(Feet(rk, rl, gy + rh + 1));
        m.ResolveSpawn();
        float3 r0 = m.PositionM;
        var uphill = MotorInput.FromLook(new float2(0f, -1f), axisYaw, 0f);   // S while facing downhill = walk uphill
        bool shot5 = false;
        float maxSlide = 0f;
        for (int i = 0; i < 240; i++)
        {
            _cam?.SetLook(axisYaw, 55f);
            _demoInput = uphill;
            yield return null;
            if (m.Sliding) maxSlide = math.max(maxSlide, math.length(m.VelocityMps.xz));
            if (!shot5 && m.Sliding && i > 10)
            {
                shot5 = true;
                yield return DemoShot(dir, "5_sliding_50deg");
            }
            if (i > 30 && m.Grounded && !m.Sliding) break;
        }
        float3 r1 = m.PositionM;
        Debug.Log($"[ClimbDemo] 5 50 deg ramp, holding uphill: slid {math.length((r1 - r0).xz):F2} m across and {r0.y - r1.y:F2} m down, " +
                  $"top slide speed {maxSlide:F2} m/s (config {c.slideSpeedMps:F1}); shot {shot5}");

        // ---- 6. The ice pillar.
        m.Teleport(Feet(wallK - 8, -45, feetRow));
        yield return Idle(10, axisYaw, 0f);
        float iceT = -1f, iceY = 0f, iceS = 0f;
        bool shot6 = false;
        for (int i = 0; i < 900; i++)
        {
            _cam?.SetLook(axisYaw, -60f);
            _demoInput = w;
            yield return null;
            if (m.Climbing && iceT < 0f) { iceT = Time.time; iceY = m.PositionM.y; iceS = m.StaminaS; }
            if (!shot6 && m.Climbing && m.PositionM.y > feetRow * 0.1f + 1.5f)
            {
                shot6 = true;
                float dt = Time.time - iceT;
                Debug.Log($"[ClimbDemo] 6 climbing {MaterialName(m.ClimbFace)} (grip {m.ClimbGrip}): {(m.PositionM.y - iceY) / math.max(dt, 1e-3f):F2} m/s " +
                          $"(rock {c.climbSpeedMps:F1} x {c.climbSlickSpeedScale:F1}); stamina {iceS:F2} -> {m.StaminaS:F2} s in {dt:F2} s " +
                          $"(x{c.climbSlickDrainScale:F1} drain)");
                yield return DemoShot(dir, "6_climbing_ice");
                break;
            }
        }
        Debug.Log("[ClimbDemo] done");
        m.Teleport(startFeet);
        yield return Idle(10, axisYaw, 6f);
        Application.Quit();
    }
}
