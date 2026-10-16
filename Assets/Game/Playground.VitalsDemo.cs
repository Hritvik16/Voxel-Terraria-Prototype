// Assets/Game/Playground.VitalsDemo.cs
//
// -vitalsdemo (task 93d1): gameplay step 9a's acceptance screenshots. An
// INTERACTIVE Survival run (run-play.sh EXTRA="-vitalsdemo -playshotquit"), so the
// HUD, the motor and PlayerVitals are exactly what a player gets; the
// coroutine only feeds MotorInput (Playground._demoInput) and builds two
// tanks through EditService. Noon, clock paused, sun behind the camera.
//   1  at the play start, full health                  (the health bar)
//   2  just after a 12 m fall                          (about half gone, red flash)
//   3  on the bed of a 2.5 m water tank, 12 s under    (the AIR bar draining)
//   4  air gone, drowning                              ("drowning!", health falling)
//   5  wading in a lava tank                           (health falling fast)
//   6  dead in the lava                                (the soft death screen)
//   7  back at the play start                          (full bars again)
// Acid is logged only (1 s in an acid tank). Shots: persistentDataPath/VitalsDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator VitalsDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready || (!_poseReady && Time.unscaledTime - t0 < 60f))
            yield return null;
        SetGameMode(true);
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "VitalsDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        _mode = MoveMode.Walk;
        SnapPlayerToCamera();
        var m = _player.Motor;
        var c = PlayerConfig.Active;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int2 fw = new int2((int)math.round(math.sin(math.radians(awayYaw))), (int)math.round(math.cos(math.radians(awayYaw))));
        if (fw.x != 0 && fw.y != 0) fw.y = 0;   // an axis, so the tanks are boxes
        int2 side = new int2(-fw.y, fw.x);
        float axisYaw = math.degrees(math.atan2(fw.x, fw.y));
        float3 startFeet = m.PositionM;
        Debug.Log($"[VitalsDemo] start feet {startFeet.x:F1},{startFeet.y:F1},{startFeet.z:F1} m, play start {(Phase4Bootstrapper.PlayStartActive ? Phase4Bootstrapper.PlayStartM.ToString() : "none")}, " +
                  $"sun-away yaw {awayYaw:F0} -> axis {axisYaw:F0}; survival {SurvivalMode}, abilities {_moveAbilities}");

        IEnumerator Idle(int frames, float pitch, MotorInput? input = null)
        {
            for (int i = 0; i < frames; i++) { _cam?.SetLook(axisYaw, pitch); _demoInput = input ?? default(MotorInput); yield return null; }
        }

        // ---- 1. Full health at the start.
        yield return Idle(90, 6f);
        Debug.Log($"[VitalsDemo] 1 health {_vitals?.Health:F1}, air {_vitals?.OxygenS:F1} s, vitals {(_vitals != null ? "on" : "OFF")}");
        yield return DemoShot(dir, "1_full_health");
        if (_vitals == null) { Debug.Log("[VitalsDemo] no vitals (not Survival walk?) -- stop"); Application.Quit(); yield break; }

        // ---- 2. A 12 m fall onto the start.
        float hp0 = _vitals.Health;
        m.Teleport(startFeet + new float3(0f, 12f, 0f));
        float impact = 0f;
        int land = -1;
        for (int i = 0; i < 300; i++)
        {
            _cam?.SetLook(axisYaw, 10f);
            _demoInput = default(MotorInput);
            yield return null;
            impact = math.max(impact, m.LastImpactDownMps);
            if (land < 0 && impact > 0f) land = i;
            if (land >= 0 && i - land == 3) yield return DemoShot(dir, "2_after_fall_12m");
            if (land >= 0 && i - land > 4) break;
        }
        Debug.Log($"[VitalsDemo] 2 fell 12 m: impact {impact:F2} m/s (= {PlayerVitals.FallHeightM(c, impact):F2} m), " +
                  $"health {hp0:F1} -> {_vitals.Health:F1} (curve says {PlayerVitals.FallDamage(c, impact):F1} damage)");

        // ---- 3/4. A water tank 6 m ahead: inner 2 m x 2 m, 2.5 m of water.
        int3 sv = CoordMath.WorldToVoxel(startFeet);
        int3 TankAt(int k, int l)
        {
            int x = sv.x + fw.x * k + side.x * l, z = sv.z + fw.y * k + side.y * l;
            int g = DryTopY(x, z);
            return new int3(x, g < 0 ? sv.y - 1 : g, z);
        }
        int3 wc = TankAt(60, 0);
        int gy = wc.y;
        for (int k = -12; k <= 12; k += 4) for (int l = -12; l <= 12; l += 4) gy = math.max(gy, TankAt(60 + k, l).y);
        int wDepth = 25;
        int written = _edits.SetBox(new int3(wc.x - 12, gy - 1, wc.z - 12), new int3(wc.x + 11, gy + wDepth + 2, wc.z + 11), Materials.Stone);
        written += _edits.SetBox(new int3(wc.x - 10, gy, wc.z - 10), new int3(wc.x + 9, gy + wDepth + 2, wc.z + 9), Materials.Air);
        written += _edits.SetBox(new int3(wc.x - 10, gy, wc.z - 10), new int3(wc.x + 9, gy + wDepth - 1, wc.z + 9), Materials.Water);
        Debug.Log($"[VitalsDemo] water tank at {wc} floor y {gy}, {wDepth * 0.1f:F1} m deep; {written} voxels written");
        yield return Idle(30, 6f);
        _vitals.Health = c.maxHealth;
        m.Teleport(new float3(wc.x * 0.1f, gy * 0.1f + 0.02f, wc.z * 0.1f));
        var down = MotorInput.FromLook(float2.zero, axisYaw, 0f);
        down.DownHeld = true;
        float tUnder = 0f;
        bool shot3 = false;
        for (int i = 0; i < 3000 && _vitals.OxygenS > 0f; i++)
        {
            _cam?.SetLook(axisYaw, -30f);   // up at the surface from below
            _demoInput = down;
            yield return null;
            tUnder += math.min(Time.deltaTime, 0.1f);
            if (!shot3 && _vitals.OxygenS <= c.oxygenSeconds * 0.45f)
            {
                shot3 = true;
                Debug.Log($"[VitalsDemo] 3 under water: feet {m.PositionM.y - gy * 0.1f:F2} m over the bed, swimming {m.Swimming}, air {_vitals.OxygenS:F1} s, health {_vitals.Health:F1}");
                yield return DemoShot(dir, "3_under_water_air");
            }
        }
        float hpAtEmpty = _vitals.Health;
        for (int i = 0; i < 400 && _vitals.Health > c.maxHealth * 0.6f && !_vitals.Dead; i++)
        { _cam?.SetLook(axisYaw, -30f); _demoInput = down; yield return null; }
        Debug.Log($"[VitalsDemo] 4 air empty after ~{tUnder:F1} s under (config {c.oxygenSeconds:F0} s); health {hpAtEmpty:F1} -> {_vitals.Health:F1} drowning, cause {_vitals.LastCause}");
        yield return DemoShot(dir, "4_drowning");
        // Up and out; the breath refills.
        m.Teleport(startFeet);
        yield return Idle(60, 6f);
        Debug.Log($"[VitalsDemo] out: air {_vitals.OxygenS:F1} s after 1 s in air (refill {c.oxygenRefillPerS:F0} s/s)");

        // ---- Acid (log only): a 1 m pool, 1 s.
        int3 ac = TankAt(30, 40);
        int ay = ac.y;
        _edits.SetBox(new int3(ac.x - 8, ay - 1, ac.z - 8), new int3(ac.x + 7, ay + 12, ac.z + 7), Materials.Stone);
        _edits.SetBox(new int3(ac.x - 6, ay, ac.z - 6), new int3(ac.x + 5, ay + 12, ac.z + 5), Materials.Air);
        _edits.SetBox(new int3(ac.x - 6, ay, ac.z - 6), new int3(ac.x + 5, ay + 9, ac.z + 5), Materials.Acid);
        yield return Idle(20, 6f);
        _vitals.Health = c.maxHealth;
        m.Teleport(new float3(ac.x * 0.1f, ay * 0.1f + 0.02f, ac.z * 0.1f));
        float ta = Time.unscaledTime;
        while (Time.unscaledTime - ta < 1f) { _demoInput = default(MotorInput); yield return null; }
        Debug.Log($"[VitalsDemo] acid 1 s: health {c.maxHealth:F0} -> {_vitals.Health:F1} (config {c.acidDamagePerS:F0}/s), cause {_vitals.LastCause}");
        m.Teleport(startFeet);
        yield return Idle(20, 6f);

        // ---- 5/6. A lava tank 6 m to the side: 0.8 m of lava to wade in.
        int3 lc = TankAt(30, -40);
        int ly = lc.y;
        for (int k = -10; k <= 10; k += 5) for (int l = -10; l <= 10; l += 5) ly = math.max(ly, TankAt(30 + k, -40 + l).y);
        _edits.SetBox(new int3(lc.x - 12, ly - 1, lc.z - 12), new int3(lc.x + 11, ly + 10, lc.z + 11), Materials.Stone);
        _edits.SetBox(new int3(lc.x - 10, ly, lc.z - 10), new int3(lc.x + 9, ly + 10, lc.z + 9), Materials.Air);
        _edits.SetBox(new int3(lc.x - 10, ly, lc.z - 10), new int3(lc.x + 9, ly + 7, lc.z + 9), Materials.Lava);
        yield return Idle(30, 6f);
        _vitals.Health = c.maxHealth;
        // Stand at the tank's back wall, looking across the lava with the sun behind.
        m.Teleport(new float3((lc.x - fw.x * 8) * 0.1f, ly * 0.1f + 0.02f, (lc.z - fw.y * 8) * 0.1f));
        bool shot5 = false;
        int deadFrames = -1;
        for (int i = 0; i < 900; i++)
        {
            _cam?.SetLook(axisYaw, 30f);
            _demoInput = default(MotorInput);
            yield return null;
            if (!shot5 && _vitals.Health <= c.maxHealth * 0.6f)
            {
                shot5 = true;
                Debug.Log($"[VitalsDemo] 5 in lava: health {_vitals.Health:F1}, cause {_vitals.LastCause}, feet {m.PositionM.y - ly * 0.1f:F2} m over the bed");
                yield return DemoShot(dir, "5_lava");
            }
            if (_vitals.Dead && deadFrames < 0) deadFrames = 0;
            if (deadFrames >= 0 && ++deadFrames == 30)
            {
                Debug.Log($"[VitalsDemo] 6 dead: cause {_vitals.LastCause}, deaths {_vitals.Deaths}, status \"{_status}\"");
                yield return DemoShot(dir, "6_you_died");
                break;
            }
        }

        // ---- 7. The soft respawn: wait for it, then for the start pose.
        int inv = InventoryTotal();
        float tr = Time.unscaledTime;
        while (Time.unscaledTime - tr < 60f && (_vitals.Dead || _mode != MoveMode.Walk)) yield return null;
        yield return Idle(120, 6f);
        float3 back = m.PositionM;
        float3 ps = Phase4Bootstrapper.PlayStartActive ? (float3)(Vector3)Phase4Bootstrapper.PlayStartM : startFeet;
        Debug.Log($"[VitalsDemo] 7 respawned after {Time.unscaledTime - tr:F1} s: feet {back.x:F1},{back.y:F1},{back.z:F1} m, " +
                  $"{math.length((back - ps).xz):F1} m from the play start (xz), {math.length((back - startFeet).xz):F1} m from the demo's start feet; " +
                  $"health {_vitals.Health:F0}, air {_vitals.OxygenS:F0} s, dead {_vitals.Dead}, inventory {inv} -> {InventoryTotal()} voxels");
        yield return DemoShot(dir, "7_respawned_at_start");
        Debug.Log("[VitalsDemo] done");
        Application.Quit();
    }
}
