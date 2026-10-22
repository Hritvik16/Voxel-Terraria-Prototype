// Assets/Game/Playground.GrappleDemo.cs
//
// -grappledemo (task 100): gameplay step 7's acceptance screenshots. An
// INTERACTIVE run (run-play.sh EXTRA="-grappledemo -playshotquit"), so the HUD
// and the motor are exactly what a player gets; the coroutine only feeds
// MotorInput (Playground._demoInput) and builds a test site through
// EditService ahead of the play start: a stone pad, a 15 m SHEER stone face
// (no ledge, no perch) 12 m ahead, and a floating stone beam 10 m up for the
// swing. Noon, clock paused, sun behind the camera (the site lies away from it).
//   1  the site                                   (the face ahead, the beam over it)
//   2  aiming 9 m up the face from 11 m           (the in-range ring on the crosshair)
//   3  reeling in, half way                       (the rope, REELING in the HUD)
//   4  at the anchor, looking along the face, down (hooked 9 m up a sheer face, the drop)
//   5  swinging under the beam                    (SWINGING at the bottom of the arc, looking up the rope)
// It logs the anchor, reel time, rope lengths, swing speed. Shots: persistentDataPath/GrappleDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator GrappleDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready || (!_poseReady && Time.unscaledTime - t0 < 60f))
            yield return null;
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "GrappleDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Walk;
        SnapPlayerToCamera();
        var m = _player.Motor;
        var c = PlayerConfig.Active;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int2 fw = new int2((int)math.round(math.sin(math.radians(awayYaw))), (int)math.round(math.cos(math.radians(awayYaw))));
        if (fw.x != 0 && fw.y != 0) fw.y = 0;   // an axis, so the face is a box face
        int2 side = new int2(-fw.y, fw.x);
        float axisYaw = math.degrees(math.atan2(fw.x, fw.y));
        float3 startFeet = m.PositionM;
        int3 sv = CoordMath.WorldToVoxel(startFeet);
        Debug.Log($"[GrappleDemo] start feet {startFeet.x:F1},{startFeet.y:F1},{startFeet.z:F1} m, sun-away yaw {awayYaw:F0} -> axis {axisYaw:F0}; abilities {_moveAbilities}");
        if (!_moveAbilities) { Debug.Log("[GrappleDemo] movement abilities off -- stop"); Application.Quit(); yield break; }

        IEnumerator Idle(int frames, float yaw, float pitch, MotorInput? input = null)
        {
            for (int i = 0; i < frames; i++) { _cam?.SetLook(yaw, pitch); _demoInput = input ?? default(MotorInput); yield return null; }
        }
        int3 At(int k, int l, int y) => new int3(sv.x + fw.x * k + side.x * l, y, sv.z + fw.y * k + side.y * l);
        int Box(int k0, int k1, int l0, int l1, int y0, int y1, byte mat)
        {
            int3 a = At(k0, l0, y0), b = At(k1, l1, y1);
            return _edits.SetBox(math.min(a, b), math.max(a, b), mat);
        }
        float3 Feet(float k, float l, int row)
            => new float3((sv.x + fw.x * k + side.x * l) * 0.1f, row * 0.1f + 0.02f, (sv.z + fw.y * k + side.y * l) * 0.1f);
        // Yaw/pitch from the eye to a world point (Unity: pitch > 0 looks down).
        float PitchTo(float3 target)
        {
            float3 d = target - m.HookOriginM;
            return -math.degrees(math.atan2(d.y, math.length(d.xz)));
        }
        MotorInput Aim(float pitch, bool fire, bool hold, float2 move = default)
        {
            var i = MotorInput.FromLook(move, axisYaw, pitch);
            i.HookPressed = fire;
            i.HookHeld = hold;
            return i;
        }

        // ---- The site: k -30..175 along fw, l -70..70 across; a pad whose top row is gy.
        int gy = sv.y - 1;
        for (int k = 0; k <= 170; k += 10)
            for (int l = -60; l <= 60; l += 15)
            {
                int3 p = At(k, l, 0);
                int g = DryTopY(p.x, p.z);
                if (g >= 0) gy = math.max(gy, g);
            }
        int written = Box(-30, 175, -70, 70, gy + 1, gy + 170, Materials.Air);
        written += Box(-30, 175, -70, 70, gy - 15, gy, Materials.Stone);
        // The sheer face: near face at k = 120, 3 m thick, 8 m wide, 15 m tall.
        const int faceK = 120, faceH = 150;
        written += Box(faceK, faceK + 29, -40, 39, gy + 1, gy + faceH, Materials.Stone);
        // The beam: 10..11 m up, k 45..60, 4 m wide (a test overhang, floating).
        const int beamK0 = 45, beamK1 = 60, beamY = 100;
        written += Box(beamK0, beamK1, -20, 19, gy + beamY, gy + beamY + 9, Materials.Stone);
        Debug.Log($"[GrappleDemo] site pad top row {gy} ({gy * 0.1f:F1} m), sheer face 15 m at k {faceK}, beam {beamY * 0.1f:F0} m up at k {beamK0}..{beamK1}; {written} voxels written");
        int feetRow = gy + 1;
        float groundY = feetRow * 0.1f;
        for (int s = 0; s < Inventory.HotbarSize; s++) if (_inv.IsEmpty(s)) { _inv.Selected = s; break; }
        m.Teleport(Feet(-25, 0, feetRow));
        yield return Idle(120, axisYaw, -22f);

        // ---- 1. The site.
        yield return DemoShot(dir, "1_site");

        // ---- 2. Aim 9 m up the face from 11 m out.
        m.Teleport(Feet(faceK - 110, 0, feetRow));
        float3 target = Feet(faceK, 0, feetRow + 90);
        yield return Idle(20, axisYaw, PitchTo(target));
        float aimPitch = PitchTo(target);
        yield return Idle(5, axisYaw, aimPitch);
        Debug.Log($"[GrappleDemo] 2 aim pitch {aimPitch:F1} deg, ring {(_hookAimHit ? "ON" : "off")} at {_hookAimDist:F1} m");
        yield return DemoShot(dir, "2_aim_in_range");

        // ---- 3/4. Fire and hold F.
        _cam?.SetLook(axisYaw, aimPitch);
        _demoInput = Aim(aimPitch, true, true);
        yield return null;
        Debug.Log($"[GrappleDemo] fired: hit {m.LastHookHit}, hooked {m.Hooked}, anchor {m.HookAnchorM.x:F2},{m.HookAnchorM.y:F2},{m.HookAnchorM.z:F2} " +
                  $"({m.HookAnchorM.y - groundY:F2} m up), rope {m.RopeLengthM:F2} m");
        float reelT0 = Time.time, startDist = math.length(m.HookAnchorM - m.RopeAttachM);
        bool shot3 = false;
        for (int i = 0; i < 400 && m.Hooked && !m.HookArrived; i++)
        {
            float lookAt = PitchTo(m.HookAnchorM);
            _cam?.SetLook(axisYaw, lookAt);
            _demoInput = Aim(lookAt, false, true);
            yield return null;
            if (!shot3 && math.length(m.HookAnchorM - m.RopeAttachM) < startDist * 0.5f)
            {
                shot3 = true;
                // Look a little below the anchor so the rope runs up the frame.
                _cam?.SetLook(axisYaw, PitchTo(m.HookAnchorM) + 12f);
                Debug.Log($"[GrappleDemo] 3 reeling: {math.length(m.HookAnchorM - m.RopeAttachM):F2} m to go, speed {math.length(m.VelocityMps):F2} m/s (config {c.hookReelSpeedMps:F1})");
                yield return DemoShot(dir, "3_reeling");
            }
        }
        Debug.Log($"[GrappleDemo] reel done: arrived {m.HookArrived}, hooked {m.Hooked}, {Time.time - reelT0:F2} s, feet {m.PositionM.y - groundY:F2} m up, " +
                  $"hands {math.length(m.HookAnchorM - m.RopeAttachM):F2} m from the anchor, overlaps {m.OverlapsSolid(m.PositionM)}");
        // Hold on and look along the face and down: the drop under the hook.
        yield return Idle(20, axisYaw + 70f, 48f, Aim(48f, false, true));
        Debug.Log($"[GrappleDemo] 4 hanging: hooked {m.Hooked}, feet {m.PositionM.y - groundY:F2} m up the sheer face");
        yield return DemoShot(dir, "4_hooked_on_sheer_face");

        // ---- 5. The swing under the beam.
        m.Teleport(Feet(beamK0 - 40, 0, feetRow));
        yield return Idle(15, axisYaw, -30f);
        float3 beamUnder = Feet((beamK0 + beamK1) * 0.5f, 0, feetRow);
        beamUnder.y = (gy + beamY) * 0.1f - 0.05f;
        float bp = PitchTo(beamUnder);
        _cam?.SetLook(axisYaw, bp);
        _demoInput = Aim(bp, true, true);
        yield return null;
        Debug.Log($"[GrappleDemo] 5 beam shot: hooked {m.Hooked}, anchor {m.HookAnchorM.y - groundY:F2} m up, rope {m.RopeLengthM:F2} m");
        // Reel the rope in to 5.5 m (off the ground), then let go of F: the swing (W pumps).
        bool shot5 = false;
        float topSpeed = 0f;
        float anchorK = math.dot(m.HookAnchorM.xz - startFeet.xz, new float2(fw.x, fw.y));
        bool reel = true;
        for (int i = 0; i < 400 && m.Hooked; i++)
        {
            if (m.RopeLengthM <= 5.5f) reel = false;
            _cam?.SetLook(axisYaw, 5f);
            _demoInput = Aim(5f, false, reel, new float2(0f, 1f));
            yield return null;
            topSpeed = math.max(topSpeed, math.length(m.VelocityMps));
            float k = math.dot(m.PositionM.xz - startFeet.xz, new float2(fw.x, fw.y));
            if (!shot5 && !reel && m.Hooked && !m.Grounded && k > anchorK - 1.5f)
            {
                shot5 = true;
                // Look up and ahead: the beam, the rope, the arc's far side.
                _cam?.SetLook(axisYaw, -38f);
                Debug.Log($"[GrappleDemo] 5 swinging: {k - anchorK:F2} m from under the anchor, speed {math.length(m.VelocityMps):F2} m/s, " +
                          $"feet {m.PositionM.y - groundY:F2} m up, rope {m.RopeLengthM:F2} m");
                yield return DemoShot(dir, "5_swinging_under_beam");
            }
        }
        Debug.Log($"[GrappleDemo] swing done: shot {shot5}, top speed {topSpeed:F2} m/s, hooked {m.Hooked}, grounded {m.Grounded}");
        Debug.Log("[GrappleDemo] done");
        m.Teleport(startFeet);
        yield return Idle(10, axisYaw, 6f);
        Application.Quit();
    }
}
