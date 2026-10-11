// Assets/Game/Playground.MoveDemo.cs
//
// -movedemo (task 65): gameplay step 2's acceptance screenshots. An
// INTERACTIVE run (run-play.sh EXTRA="-movedemo"), so the HUD and the motor
// path are exactly what a player gets; the coroutine only feeds MotorInput in
// place of the keys (Playground._demoInput). Noon, clock paused, sun behind
// the camera. Then quits.
//   1  floating at a lake's surface           (swim, idle float)
//   2  diving toward the lake bed             (W while looking 55 deg down)
//   2a/2b/2c  task 66b: eye 1 m and 6 m under, then looking up at the surface
//      (+ 2b_under_6m_view_off: the same pose with RaymarchFeature.UnderwaterView off)
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

        // ---- 1/2. A lake 3-9 m deep, water 6 m each way: from the terrain
        // MAP (ShapeField; the loaded window rarely holds one), nearest first,
        // then teleport and let the window stream in around it.
        int3 lake = default;
        int lakeTop = -1, lakeBed = -1;
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        if (src != null)
        {
            var spec = src.Spec; var view = src.Map.View; var o = src.O;
            bool Lake(int x, int z, out int w, out int h)
            {
                var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
                w = col.water; h = col.height;
                return col.water != TerrainGen.ColumnSample.NO_WATER_Y && col.waterKind == 0 && col.water > col.height;
            }
            for (int r = 0; r <= 20000 && lakeTop < 0; r += 80)
            {
                for (int dz = -r; dz <= r && lakeTop < 0; dz += 80)
                for (int dx = -r; dx <= r && lakeTop < 0; dx += 80)
                {
                    if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                    int x = f.x + dx, z = f.z + dz;
                    if (!Lake(x, z, out int w, out int h) || w - h < 30 || w - h > 90) continue;
                    if (!Lake(x + 60, z, out _, out _) || !Lake(x - 60, z, out _, out _) ||
                        !Lake(x, z + 60, out _, out _) || !Lake(x, z - 60, out _, out _)) continue;
                    lake = new int3(x, w, z); lakeTop = w; lakeBed = h;
                }
                if ((r & 1023) == 0) yield return null;
            }
        }
        if (lakeTop >= 0)
        {
            Debug.Log($"[MoveDemo] map lake at {lake}, {math.length((float2)(lake.xz - f.xz)) * 0.1f:F0} m away; streaming it in");
            m.Teleport(new float3(lake.x * 0.1f + 0.05f, (lakeTop + 1) * 0.1f - c.swimFloatHeightM, lake.z * 0.1f + 0.05f));
            float tw = Time.unscaledTime;
            while (Time.unscaledTime - tw < 40f && !WaterColumn(lake.x, lake.z, out _, out _))
            { _demoInput = default(MotorInput); yield return null; }
            for (int i = 0; i < 300; i++) { _demoInput = default(MotorInput); yield return null; }   // the window around it
            if (!WaterColumn(lake.x, lake.z, out lakeTop, out lakeBed))
            { Debug.Log($"[MoveDemo] lake column {lake} not water in the loaded world after 45 s"); lakeTop = -1; }
        }
        if (lakeTop < 0) Debug.Log("[MoveDemo] no lake 3-9 m deep within 2 km on the map -- swim shots skipped");
        else
        {
            m.Teleport(new float3(lake.x * 0.1f + 0.05f, (lakeTop + 1) * 0.1f - c.swimFloatHeightM, lake.z * 0.1f + 0.05f));
            _cam?.SetLook(awayYaw, 8f);
            for (int i = 0; i < 120; i++) { _demoInput = default(MotorInput); yield return null; }
            Debug.Log($"[MoveDemo] lake {lake} surface y {(lakeTop + 1) * 0.1f:F1} m, bed {(lakeBed + 1) * 0.1f:F1} m; " +
                      $"floating: swimming {m.Swimming} in {MaterialName(m.SwimFluid)}, feet {m.PositionM.y:F2} m, vy {m.VelocityMps.y:F3}");
            yield return DemoShot(dir, "1_swim_surface");

            // Dive toward the deepest water (8 headings, 1-8 m out), preferring
            // headings with the sun behind; log the descent.
            float diveYaw = awayYaw, bestScore = float.MinValue;
            for (int h = 0; h < 8; h++)
            {
                float yawH = awayYaw + h * 45f, yr = math.radians(yawH);
                float score = math.abs(Mathf.DeltaAngle(yawH, awayYaw)) <= 90f ? 20f : 0f;
                for (int dm = 10; dm <= 80; dm += 10)
                {
                    int x = lake.x + (int)math.round(math.sin(yr) * dm), z = lake.z + (int)math.round(math.cos(yr) * dm);
                    score += WaterColumn(x, z, out int wTop, out int wBed) ? wTop - wBed : -50f;
                }
                if (score > bestScore) { bestScore = score; diveYaw = yawH; }
            }
            float maxSwim = 0f, surfM = (lakeTop + 1) * 0.1f;
            var trace = new System.Text.StringBuilder();
            int3 here = default;
            for (int i = 0; i < 150; i++)
            {
                here = CoordMath.WorldToVoxel(m.PositionM);
                if (WaterColumn(here.x, here.z, out _, out int bedHere) && m.PositionM.y < (bedHere + 1) * 0.1f + 0.6f) break;
                _cam?.SetLook(diveYaw, 55f);
                _demoInput = MotorInput.FromLook(new float2(0f, 1f), diveYaw, 55f);
                yield return null;
                if (m.Swimming) maxSwim = math.max(maxSwim, math.length(m.VelocityMps));
                if (i % 15 == 0) trace.Append($" t{i / 60f:F2}s y{m.PositionM.y - surfM:+0.00;-0.00} v{math.length(m.VelocityMps):F2}{(m.Swimming ? "" : " DRY")}");
            }
            for (int i = 0; i < 8; i++)
            {
                _cam?.SetLook(diveYaw, 35f);
                _demoInput = MotorInput.FromLook(new float2(0f, 1f), diveYaw, 35f);
                yield return null;
                if (m.Swimming) maxSwim = math.max(maxSwim, math.length(m.VelocityMps));
            }
            float deepest = m.PositionM.y - surfM;
            Debug.Log($"[MoveDemo] dive heading {diveYaw:F0}: feet {deepest:+0.00;-0.00} m vs the surface, swimming {m.Swimming}, " +
                      $"top swim speed {maxSwim:F2} m/s (cap {m.SwimCapMps(Materials.Water):F2}); trace (feet vs surface):{trace}");
            yield return DemoShot(dir, "2_dive_lake_bed");

            // Task 66b: the view from under the water. The eye is held at a set
            // depth (re-teleported every frame: an idle swimmer floats up) while
            // TAA / FSR 3 settle; the same 6 m pose with the view OFF is the old
            // black frame for comparison.
            float eyeOff = Camera.main != null ? Camera.main.transform.position.y - m.PositionM.y : 1.6f;
            float bedM = WaterColumn(here.x, here.z, out _, out int bedU) ? (bedU + 1) * 0.1f : surfM - 9f;
            float3 divePose = m.PositionM;   // x, z held for the shots; the whole pose restored after
            float deep6 = math.min(6f, surfM - bedM - 0.4f);
            float[] depths = { 1f, deep6, deep6 }, pitches = { 35f, 35f, -60f };
            string[] names = { "2a_under_1m", "2b_under_6m", "2c_look_up_surface" };
            for (int s = 0; s < 3; s++)
            {
                float depth = depths[s], pitch = pitches[s];
                float3 feet = new float3(divePose.x, surfM - depth - eyeOff, divePose.z);
                for (int i = 0; i < 40; i++)
                {
                    m.Teleport(feet);
                    _cam?.SetLook(diveYaw, pitch);
                    _demoInput = default(MotorInput);
                    yield return null;
                }
                float eyeY = Camera.main != null ? Camera.main.transform.position.y : feet.y + eyeOff;
                Debug.Log($"[MoveDemo] 66b {names[s]}: eye {surfM - eyeY:F2} m under the surface (bed {surfM - bedM:F1} m), pitch {pitch:F0}, " +
                          $"swimming {m.Swimming}");
                yield return DemoShot(dir, names[s]);
                if (s == 1)
                {
                    RaymarchFeature.UnderwaterView = false;
                    for (int i = 0; i < 20; i++) { m.Teleport(feet); _demoInput = default(MotorInput); yield return null; }
                    yield return DemoShot(dir, "2b_under_6m_view_off");
                    RaymarchFeature.UnderwaterView = true;
                }
            }
            m.Teleport(divePose);   // back to the dive pose (the 6 m eye put the feet in the bed) for the release below

            // Let go: an idle swimmer floats back to the water line.
            float tUp = 0f;
            for (int i = 0; i < 900; i++)
            {
                _demoInput = default(MotorInput);
                yield return null;
                tUp += math.min(Time.deltaTime, 1f / 30f);
                if (math.abs(m.PositionM.y + c.swimFloatHeightM - surfM) < 0.03f && math.abs(m.VelocityMps.y) < 0.05f) break;
            }
            Debug.Log($"[MoveDemo] released at {deepest:+0.00;-0.00} m: back at the water line after {tUp:F1} s (float point {m.PositionM.y + c.swimFloatHeightM - surfM:+0.000;-0.000} m), swimming {m.Swimming}");
        }

        // ---- 3. Double jump over a gap: two stone runways (built through
        // EditService, 0.3 m thick, 4 m above the highest ground under them)
        // with a 3.8 m gap -- too wide for one jump (~2.9 m), not for two
        // (~4.6 m). Along the axis nearest to straight away from the sun.
        f = CoordMath.WorldToVoxel(m.PositionM);
        float axisYaw = math.round(awayYaw / 90f) * 90f;
        float ar = math.radians(axisYaw);
        int2 fw = new int2((int)math.round(math.sin(ar)), (int)math.round(math.cos(ar)));
        int2 side = new int2(fw.y, -fw.x);
        const int gapStart = 30, gapWidth = 38, runEnd = 100;
        int groundMax = int.MinValue;
        for (int k = -25; k <= runEnd; k += 5)
        for (int l = -20; l <= 20; l += 5)
        {
            int x = f.x + fw.x * k + side.x * l, z = f.z + fw.y * k + side.y * l;
            int h = DryTopY(x, z);
            if (h < 0 && WaterColumn(x, z, out int wt, out _)) h = wt;
            if (h < 0) h = TopAnyY(x, z);
            groundMax = math.max(groundMax, h);
        }
        int3 stand = new int3(f.x, groundMax + 40, f.z);   // the runway's top voxel, 4 m up
        int3 a1 = Course(f, fw, side, -25, -20, stand.y - 2), b1 = Course(f, fw, side, gapStart - 1, 20, stand.y);
        int3 a2 = Course(f, fw, side, gapStart + gapWidth, -20, stand.y - 2), b2 = Course(f, fw, side, runEnd, 20, stand.y);
        int built = _edits.SetBox(a1, b1, Materials.Stone) + _edits.SetBox(a2, b2, Materials.Stone);
        float2 c0 = new float2(stand.x * 0.1f + 0.05f, stand.z * 0.1f + 0.05f);
        float sNear = (gapStart - 0.5f) * 0.1f, sFar = (gapStart + gapWidth - 0.5f) * 0.1f;
        Debug.Log($"[MoveDemo] runway top y {(stand.y + 1) * 0.1f:F1} m ({(stand.y - groundMax) * 0.1f:F1} m over the highest ground), axis yaw {axisYaw:F0}; " +
                  $"gap {sNear:F2}..{sFar:F2} m ahead; {built} voxels built");

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
            var input = MotorInput.FromLook(new float2(0f, 1f), axisYaw, afterDj >= 0 ? 45f : 20f);
            if (!jumped && m.Grounded && S() >= sNear - 0.2f) { input.JumpPressed = true; jumped = true; jumpS = S(); }
            else if (jumped && !doubled && !m.Grounded && m.VelocityMps.y <= 0.3f)
            { input.JumpPressed = true; doubled = true; djS = S(); djY = m.PositionM.y; afterDj = 0; }
            _cam?.SetLook(axisYaw, afterDj >= 0 ? 45f : 20f);
            _demoInput = input;
            yield return null;
            if (!jumped) maxWalk = math.max(maxWalk, math.length(m.VelocityMps.xz));
            if (afterDj >= 0 && ++afterDj == 4) yield return DemoShot(dir, "3_double_jump_over_gap");
            if (doubled && afterDj > 4 && m.Grounded) break;
        }
        float landS = S();
        bool farSide = landS > sFar - 0.3f && m.PositionM.y > groundY - 0.05f;
        Debug.Log($"[MoveDemo] jump at {jumpS:F2} m, double jump at {djS:F2} m ({djY - groundY:F2} m up), " +
                  $"landed at {landS:F2} m, feet {m.PositionM.y - groundY:+0.00;-0.00} m vs ground -> {(farSide ? "FAR RUNWAY (cleared)" : "MISSED")}; walk top {maxWalk:F2} m/s");

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

    /// Topmost non-air voxel of the loaded column, or the band floor.
    private int TopAnyY(int x, int z)
    {
        int yTop = RaymarchFeature.ContentCeilingVoxelY - 1, yBot = RaymarchFeature.ContentFloorVoxelY;
        for (int y = yTop; y >= yBot; y--)
            if (_store.GetVoxel(new int3(x, y, z)) != Materials.Air) return y;
        return yBot;
    }

    /// The voxel `k` ahead along `fw` and `l` to the side of `f`, at height `y`.
    private static int3 Course(int3 f, int2 fw, int2 side, int k, int l, int y)
        => new int3(f.x + fw.x * k + side.x * l, y, f.z + fw.y * k + side.y * l);
}
