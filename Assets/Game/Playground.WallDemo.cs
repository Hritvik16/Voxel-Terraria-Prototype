// Assets/Game/Playground.WallDemo.cs
//
// -walldemo (task 103): gameplay step 8's acceptance screenshots. An
// INTERACTIVE run (run-play.sh EXTRA="-walldemo"), so the HUD and the motor are
// exactly what a player gets; the coroutine only feeds MotorInput
// (Playground._demoInput) and builds a test site through EditService ahead of
// the play start: a stone pad, a 9 m long, 4 m tall stone wall on the left of
// a run lane, and a 1.7 m wide, 8 m tall two-wall chimney on the right.
// Noon, clock paused, sun behind the camera (the site lies away from the sun).
//   1  the site                                   (run wall left, chimney right)
//   2  wall-running, half way along               (WALL-RUN in the HUD, the stamina bar)
//   3  just after the wall-jump off its end        (pushed off the wall, looking back at it over the left shoulder)
//   4  bouncing up the chimney, at a wall, looking up (both walls, the "wall-jump" hint)
//   5  at the highest bounce, looking down        (the drop, stamina spent)
// It logs the run's time / length / height change and the chimney's bounces and height.
// Shots: persistentDataPath/WallDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator WallDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready || (!_poseReady && Time.unscaledTime - t0 < 60f))
            yield return null;
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "WallDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Walk;
        SnapPlayerToCamera();
        var m = _player.Motor;
        var c = PlayerConfig.Active;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int2 fw = new int2((int)math.round(math.sin(math.radians(awayYaw))), (int)math.round(math.cos(math.radians(awayYaw))));
        if (fw.x != 0 && fw.y != 0) fw.y = 0;   // an axis: the walls are boxes
        int2 side = new int2(-fw.y, fw.x);
        float axisYaw = math.degrees(math.atan2(fw.x, fw.y));
        float3 startFeet = m.PositionM;
        int3 sv = CoordMath.WorldToVoxel(startFeet);
        Debug.Log($"[WallDemo] start feet {startFeet.x:F1},{startFeet.y:F1},{startFeet.z:F1} m, sun-away yaw {awayYaw:F0} -> axis {axisYaw:F0}; abilities {_moveAbilities}");
        if (!_moveAbilities) { Debug.Log("[WallDemo] movement abilities off -- stop"); Application.Quit(); yield break; }

        IEnumerator Idle(int frames, float yaw, float pitch)
        {
            for (int i = 0; i < frames; i++) { _cam?.SetLook(yaw, pitch); _demoInput = default(MotorInput); yield return null; }
        }
        int3 At(int k, int l, int y) => new int3(sv.x + fw.x * k + side.x * l, y, sv.z + fw.y * k + side.y * l);
        int Box(int k0, int k1, int l0, int l1, int y0, int y1, byte mat)
        {
            int3 a = At(k0, l0, y0), b = At(k1, l1, y1);
            return _edits.SetBox(math.min(a, b), math.max(a, b), mat);
        }
        float3 Feet(float k, float l, int row)
            => new float3((sv.x + fw.x * k + side.x * l) * 0.1f, row * 0.1f + 0.02f, (sv.z + fw.y * k + side.y * l) * 0.1f);
        // Keys as a world-space wish (k along fw, l along side), the camera's look kept for the shot.
        MotorInput Wish(float k, float l, float yaw, bool jump = false)
        {
            var i = MotorInput.FromLook(float2.zero, yaw, 0f);
            float2 w = new float2(fw.x, fw.y) * k + new float2(side.x, side.y) * l;
            float wl = math.length(w);
            i.WishXZ = wl > 1f ? w / wl : w;
            i.JumpPressed = jump;
            return i;
        }

        // ---- The site: k -30..130 ahead, l -75..45 across; a pad whose top row is gy.
        int gy = sv.y - 1;
        for (int k = 0; k <= 130; k += 10)
            for (int l = -70; l <= 40; l += 10)
            {
                int3 p = At(k, l, 0);
                int g = DryTopY(p.x, p.z);
                if (g >= 0) gy = math.max(gy, g);
            }
        int written = Box(-30, 135, -80, 50, gy + 1, gy + 110, Materials.Air);
        written += Box(-30, 135, -80, 50, gy - 15, gy, Materials.Stone);
        // The run wall: near face at l = 12, k 30..119 (9 m), 4 m tall, 1 m thick.
        const int runL = 12, runK0 = 30, runK1 = 119;
        written += Box(runK0, runK1, runL, runL + 9, gy + 1, gy + 40, Materials.Stone);
        // The chimney: k 70..89, walls l -40..-31 and l -67..-58: 1.7 m of air between (l -57..-41), 8 m tall.
        const int chK0 = 70, chK1 = 89;
        written += Box(chK0, chK1, -40, -31, gy + 1, gy + 80, Materials.Stone);
        written += Box(chK0, chK1, -67, -58, gy + 1, gy + 80, Materials.Stone);
        Debug.Log($"[WallDemo] site pad top row {gy} ({gy * 0.1f:F1} m), run wall 9 m x 4 m at l {runL}, chimney 1.7 m x 8 m at k {chK0}..{chK1}; {written} voxels written");
        int feetRow = gy + 1;
        for (int s = 0; s < Inventory.HotbarSize; s++) if (_inv.IsEmpty(s)) { _inv.Selected = s; break; }
        m.Teleport(Feet(-20, -10, feetRow));
        yield return Idle(120, axisYaw, -8f);

        // ---- 1. The site.
        yield return DemoShot(dir, "1_site");

        // ---- 2/3. Run up the lane beside the wall, jump, wall-run, wall-jump near its end.
        // The body's edge 6 cm off the face: centre 3.6 voxels short of it (the
        // near face is voxel runL's low edge when side > 0, its high edge when side < 0).
        int sideAxis = side.x != 0 ? 0 : 2, sideSign = side.x != 0 ? side.x : side.y;
        float laneL = (sideSign > 0 ? runL : runL - 1) - 3.6f;
        float KOf(float3 p) => math.dot(p.xz * 10f - new float2(sv.x, sv.z), new float2(fw.x, fw.y));
        m.Teleport(Feet(runK0 - 25, laneL, feetRow));
        yield return Idle(10, axisYaw, 0f);
        float runT = 0f, runY0 = 0f;
        float3 runP0 = float3.zero;
        bool shot2 = false, jumped = false, started = false;
        for (int i = 0; i < 600; i++)
        {
            bool jump = !started && m.Grounded && KOf(m.PositionM) >= runK0 + 5;
            bool kick = started && m.WallRunning && m.WallRunTimeS > 0.8f;
            _cam?.SetLook(axisYaw, 0f);
            _demoInput = Wish(1f, 0f, axisYaw, jump || kick);
            yield return null;
            if (m.LastWallRunStarted) { started = true; runP0 = m.PositionM; runY0 = m.PositionM.y; runT = Time.time; }
            if (!shot2 && m.WallRunning && m.WallRunTimeS > 0.5f)
            {
                shot2 = true;
                Debug.Log($"[WallDemo] 2 wall-running {m.WallRunTimeS:F2} s: {math.length((m.PositionM - runP0).xz):F2} m along, " +
                          $"height {m.PositionM.y - runY0:+0.00;-0.00} m, speed {math.length(m.VelocityMps.xz):F2} m/s (config {c.wallRunSpeedMps:F1}), stamina {m.StaminaS:F2} s");
                yield return DemoShot(dir, "2_wallrun");
            }
            if (m.LastWallJump)
            {
                jumped = true;
                Debug.Log($"[WallDemo] 3 wall-jump after {Time.time - runT:F2} s of run, {math.length((m.PositionM - runP0).xz):F2} m along, " +
                          $"height {m.PositionM.y - runY0:+0.00;-0.00} m; velocity {m.VelocityMps.x:F1},{m.VelocityMps.y:F1},{m.VelocityMps.z:F1}");
                for (int f = 0; f < 12; f++) { _cam?.SetLook(axisYaw - 150f, 8f); _demoInput = Wish(1f, 0f, axisYaw); yield return null; }
                yield return DemoShot(dir, "3_walljump");
                break;
            }
            if (started && !m.WallRunning && m.Grounded) break;
        }
        Debug.Log($"[WallDemo] run started {started}, shot2 {shot2}, wall-jumped {jumped}");

        // ---- 4/5. The chimney: bounce between its walls.
        float midL = -49f;                          // the chimney's centre line
        m.Teleport(Feet((chK0 + chK1) * 0.5f, midL, feetRow));
        yield return Idle(20, axisYaw, 0f);
        float s0 = m.StaminaS, bottom = m.PositionM.y, top = bottom;
        int jumps = 0, bounceDir = 1;                // +1 = towards +side (l up)
        bool shot4 = false;
        for (int i = 0; i < 900; i++)
        {
            bool first = i == 0;
            bool at = !m.Grounded && m.WallBeside(sideAxis, sideSign * bounceDir, out _);
            _cam?.SetLook(axisYaw, -35f);
            if (!shot4 && at && jumps >= 3)
            {
                // Against a wall, before the press: the HUD's "wall-jump" hint is up.
                shot4 = true;
                yield return DemoShot(dir, "4_chimney_up");
            }
            _demoInput = Wish(0f, bounceDir, axisYaw, first || at);
            yield return null;
            if (m.LastWallJump) { jumps++; bounceDir = -bounceDir; }
            top = math.max(top, m.PositionM.y);
            if (m.StaminaS <= 0f && m.VelocityMps.y <= 0f) break;
            if (i > 30 && m.Grounded) break;
        }
        Debug.Log($"[WallDemo] 4 chimney: {jumps} wall-jumps, {top - bottom:F2} m up, stamina {s0:F2} -> {m.StaminaS:F2} s ({c.wallJumpStaminaS:F1} per jump)");
        for (int f = 0; f < 3; f++) { _cam?.SetLook(axisYaw, 75f); _demoInput = default(MotorInput); yield return null; }
        yield return DemoShot(dir, "5_chimney_top_down");

        Debug.Log("[WallDemo] done");
        m.Teleport(startFeet);
        yield return Idle(10, axisYaw, 6f);
        Application.Quit();
    }
}
