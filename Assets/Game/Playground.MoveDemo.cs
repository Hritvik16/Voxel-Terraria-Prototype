// Assets/Game/Playground.MoveDemo.cs
//
// -movedemo (task 65): gameplay step 2's acceptance screenshots. An
// INTERACTIVE run (run-play.sh EXTRA="-movedemo"), so the HUD and the motor
// path are exactly what a player gets; the coroutine only feeds MotorInput in
// place of the keys (Playground._demoInput). Noon, clock paused, sun behind
// the camera. Then quits.
//   1  floating at a lake's surface           (swim, idle float)
//   2  diving toward the lake bed             (W while looking 55 deg down)
//   3  mid-double-jump over a 3.8 m trench    (carved through EditService)
// It logs the top speeds it saw (walk, swim, dash) and where the jump landed.
// Shots: persistentDataPath/MoveDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator MoveDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        _showHelp = false;
        _demoHideState = true;
        _mode = MoveMode.Walk;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "MoveDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        var m = _player.Motor;
        var c = m.Config;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int3 f = CoordMath.WorldToVoxel(m.PositionM);
        Debug.Log($"[MoveDemo] start {f}, away from sun yaw {awayYaw:F0}, abilities {_moveAbilities}");

        // ---- 1/2. A lake 3-9 m deep, inside water for 6 m each way. ----
        int3 lake = default;
        int lakeTop = -1, lakeBed = -1;
        for (int r = 0; r <= 2400 && lakeTop < 0; r += 40)
        {
            for (int dz = -r; dz <= r && lakeTop < 0; dz += 40)
            for (int dx = -r; dx <= r && lakeTop < 0; dx += 40)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int x = f.x + dx, z = f.z + dz;
                if (!WaterColumn(x, z, out int top, out int bed) || top - bed < 30 || top - bed > 90) continue;
                if (!WaterColumn(x + 60, z, out _, out _) || !WaterColumn(x - 60, z, out _, out _) ||
                    !WaterColumn(x, z + 60, out _, out _) || !WaterColumn(x, z - 60, out _, out _)) continue;
                lake = new int3(x, top, z); lakeTop = top; lakeBed = bed;
            }
            yield return null;
        }
        if (lakeTop < 0) Debug.Log("[MoveDemo] no lake 3-9 m deep within 240 m -- swim shots skipped");
        else
        {
            m.Teleport(new float3(lake.x * 0.1f + 0.05f, (lakeTop + 1) * 0.1f - c.swimFloatHeightM, lake.z * 0.1f + 0.05f));
            _cam?.SetLook(awayYaw, 8f);
            for (int i = 0; i < 120; i++) { _demoInput = default(MotorInput); yield return null; }
            Debug.Log($"[MoveDemo] lake {lake} surface y {(lakeTop + 1) * 0.1f:F1} m, bed {(lakeBed + 1) * 0.1f:F1} m; " +
                      $"floating: swimming {m.Swimming} in {MaterialName(m.SwimFluid)}, feet {m.PositionM.y:F2} m, vy {m.VelocityMps.y:F3}");
            yield return DemoShot(dir, "1_swim_surface");

            float maxSwim = 0f;
            float bedTopM = (lakeBed + 1) * 0.1f;
            for (int i = 0; i < 240 && m.PositionM.y > bedTopM + 0.9f; i++)
            {
                _cam?.SetLook(awayYaw, 55f);
                _demoInput = MotorInput.FromLook(new float2(0f, 1f), awayYaw, 55f);
                yield return null;
                maxSwim = math.max(maxSwim, math.length(m.VelocityMps));
            }
            for (int i = 0; i < 8; i++)
            {
                _cam?.SetLook(awayYaw, 35f);
                _demoInput = MotorInput.FromLook(new float2(0f, 1f), awayYaw, 35f);
                yield return null;
                maxSwim = math.max(maxSwim, math.length(m.VelocityMps));
            }
            Debug.Log($"[MoveDemo] dive: feet {m.PositionM.y:F2} m (bed top {bedTopM:F1}), swimming {m.Swimming}, " +
                      $"top swim speed {maxSwim:F2} m/s (cap {m.SwimCapMps(Materials.Water):F2})");
            yield return DemoShot(dir, "2_dive_lake_bed");
        }

        // ---- 3. Double jump over a trench. Flat dry ground along the axis
        // nearest to straight away from the sun, carved 3.8 m wide.
        float axisYaw = math.round(awayYaw / 90f) * 90f;
        float ar = math.radians(axisYaw);
        int2 fw = new int2((int)math.round(math.sin(ar)), (int)math.round(math.cos(ar)));
        int2 side = new int2(fw.y, -fw.x);
        int3 stand = default;
        bool found = false;
        for (int r = 0; r <= 1200 && !found; r += 30)
        {
            for (int dz = -r; dz <= r && !found; dz += 30)
            for (int dx = -r; dx <= r && !found; dx += 30)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int x = f.x + dx, z = f.z + dz, sy = DryTopY(x, z);
                if (sy < 0) continue;
                if (FlatAlong(x, z, sy, fw, side, 10) && FlatAlong(x, z, sy, fw, side, 1))
                { stand = new int3(x, sy, z); found = true; }
            }
            yield return null;
        }
        if (!found) { Debug.Log("[MoveDemo] no flat 10 m run within 120 m -- jump shot skipped"); Application.Quit(); yield break; }

        const int gapStart = 20, gapWidth = 38;
        int3 a = stand + new int3(fw.x * gapStart - side.x * 20, -24, fw.y * gapStart - side.y * 20);
        int3 b = stand + new int3(fw.x * (gapStart + gapWidth - 1) + side.x * 20, 3, fw.y * (gapStart + gapWidth - 1) + side.y * 20);
        int carved = _edits.SetBox(a, b, Materials.Air);
        float2 c0 = new float2(stand.x * 0.1f + 0.05f, stand.z * 0.1f + 0.05f);
        float sNear = (gapStart - 0.5f) * 0.1f, sFar = (gapStart + gapWidth - 0.5f) * 0.1f;
        Debug.Log($"[MoveDemo] stand {stand} axis yaw {axisYaw:F0}; trench {sNear:F2}..{sFar:F2} m ahead, 2.4 m deep, {carved} voxels carved");

        m.Teleport(new float3(c0.x, (stand.y + 1) * 0.1f + 0.02f, c0.y));
        m.ResolveSpawn();
        _cam?.SetLook(axisYaw, 20f);
        for (int i = 0; i < 60; i++) { _demoInput = default(MotorInput); yield return null; }

        float2 fwm = new float2(fw.x, fw.y);
        float S() => math.dot(m.PositionM.xz - c0, fwm);
        bool jumped = false, doubled = false;
        float maxWalk = 0f, jumpS = 0f, djS = 0f, djY = 0f, groundY = (stand.y + 1) * 0.1f;
        int afterDj = -1;
        for (int i = 0; i < 400; i++)
        {
            var input = MotorInput.FromLook(new float2(0f, 1f), axisYaw, afterDj >= 0 ? 32f : 20f);
            if (!jumped && m.Grounded && S() >= sNear - 0.2f) { input.JumpPressed = true; jumped = true; jumpS = S(); }
            else if (jumped && !doubled && !m.Grounded && m.VelocityMps.y <= 0.3f)
            { input.JumpPressed = true; doubled = true; djS = S(); djY = m.PositionM.y; afterDj = 0; }
            _cam?.SetLook(axisYaw, afterDj >= 0 ? 32f : 20f);
            _demoInput = input;
            yield return null;
            if (!jumped) maxWalk = math.max(maxWalk, math.length(m.VelocityMps.xz));
            if (afterDj >= 0 && ++afterDj == 4) yield return DemoShot(dir, "3_double_jump_over_gap");
            if (doubled && afterDj > 4 && m.Grounded) break;
        }
        float landS = S();
        bool farSide = landS > sFar - 0.3f && m.PositionM.y > groundY - 0.35f;
        Debug.Log($"[MoveDemo] jump at {jumpS:F2} m, double jump at {djS:F2} m ({djY - groundY:F2} m up), " +
                  $"landed at {landS:F2} m, feet {m.PositionM.y - groundY:+0.00;-0.00} m vs ground -> {(farSide ? "FAR SIDE (cleared)" : "IN THE TRENCH")}; walk top {maxWalk:F2} m/s");

        // A dash on the far side: peak speed and distance.
        for (int i = 0; i < 60; i++) { _demoInput = default(MotorInput); yield return null; }
        float3 d0 = m.PositionM;
        float maxDash = 0f;
        var dash = MotorInput.FromLook(float2.zero, axisYaw, 20f);
        dash.DashPressed = true;
        _demoInput = dash;
        yield return null;
        while (m.Dashing) { maxDash = math.max(maxDash, math.length(m.VelocityMps.xz)); _demoInput = default(MotorInput); yield return null; }
        Debug.Log($"[MoveDemo] dash: peak {maxDash:F2} m/s, {math.length((m.PositionM - d0).xz):F2} m in the dash, cooldown {m.DashCooldownS:F2} s left; " +
                  $"fall terminal {c.terminalSpeedMps:F0} m/s (config)");
        Debug.Log("[MoveDemo] done");
        Application.Quit();
    }

    /// Water from the top of the loaded band down: the top water voxel and the
    /// solid voxel under it. False for dry, other fluids or unloaded columns.
    private bool WaterColumn(int x, int z, out int surfaceY, out int bedY)
    {
        surfaceY = bedY = -1;
        int yTop = RaymarchFeature.ContentCeilingVoxelY - 1, yBot = RaymarchFeature.ContentFloorVoxelY;
        for (int y = yTop; y >= yBot; y--)
        {
            int3 v = new int3(x, y, z);
            if (!_store.IsResident(CoordMath.VoxelToChunk(v))) return false;
            byte mat = _store.GetVoxel(v);
            if (mat == Materials.Air) continue;
            if (mat != Materials.Water) return false;
            surfaceY = y;
            for (int d = y - 1; d >= yBot; d--)
            {
                byte under = _store.GetVoxel(new int3(x, d, z));
                if (under == Materials.Water) continue;
                bedY = d;
                return !MaterialRules.IsFluidMaterial(under) && under != Materials.Air;
            }
            return false;
        }
        return false;
    }

    /// Dry ground within 2 voxels of `sy` from 2.5 m behind to 7 m ahead along
    /// `fw`, 2 m either side, sampled every `step` voxels.
    private bool FlatAlong(int x, int z, int sy, int2 fw, int2 side, int step)
    {
        for (int k = -25; k <= 70; k += step)
        for (int l = -20; l <= 20; l += 10)
        {
            int h = DryTopY(x + fw.x * k + side.x * l, z + fw.y * k + side.y * l);
            if (h < 0 || math.abs(h - sy) > 2) return false;
        }
        return true;
    }
}
