// Assets/Game/Playground.GlideDemo.cs
//
// -glidedemo (task 93g1): gameplay step 6's acceptance screenshots, "a summit
// glide". An INTERACTIVE run (run-play.sh EXTRA="-glidedemo -playshotquit"),
// so the HUD and the motor are exactly what a player gets; the coroutine only
// picks the launch point and feeds MotorInput (Playground._demoInput).
// The launch point is real terrain: within 220 m of the play start, the dry
// map column with the longest glide lane AWAY from the sun (ground staying
// under a 5:1 glide line with 2 m to spare, sampled every 10 m to 150 m out),
// ties to the bigger drop. Noon, clock paused, sun behind the camera.
//   1  on the summit, looking out along the lane
//   2  ~2.5 s into the glide, looking ahead      (GLIDING + speed / sink in the HUD)
//   3  ~6 s in, looking down                     (the height under the glider)
//   4  landed                                    (health bar untouched: no fall damage)
// It logs the glide time, distance, drop, ratio, the landing impact and the
// fall damage it would do, and any frame the glider was held by terrain not
// streamed in yet. Shots: persistentDataPath/GlideDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator GlideDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready || (!_poseReady && Time.unscaledTime - t0 < 60f))
            yield return null;
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "GlideDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        if (src == null || !_moveAbilities) { Debug.Log("[GlideDemo] needs a tg2 world and movement abilities -- stop"); Application.Quit(); yield break; }
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var c = PlayerConfig.Active;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        Debug.Log($"[GlideDemo] start {f}, sun-away yaw {awayYaw:F0}, glide {c.glideSpeedMps:F1} m/s / sink {c.glideSinkMps:F1} m/s");

        // ---- the search (voxels; a glide drops 1 voxel per ratio voxels) ----
        int Surf(float x, float z)
        {
            var s = TerrainGen.ShapeField.Sample(in spec, in view, in o, x, z, 1);
            int top = s.height;
            if (s.water != TerrainGen.ColumnSample.NO_WATER_Y) top = math.max(top, s.water);
            return math.max(top, spec.seaLevelY);
        }
        float ratio = c.glideSpeedMps / math.max(0.1f, c.glideSinkMps);
        int bestLane = -1, bestDrop = 0; int3 site = f; float siteYaw = awayYaw;
        int rows = 0;
        for (int dz = -2200; dz <= 2200; dz += 60)
        {
            for (int dx = -2200; dx <= 2200; dx += 60)
            {
                if (dx * dx + dz * dz > 2200 * 2200) continue;
                float x = f.x + dx, z = f.z + dz;
                var s0 = TerrainGen.ShapeField.Sample(in spec, in view, in o, x, z, 1);
                if (s0.water != TerrainGen.ColumnSample.NO_WATER_Y && s0.water >= s0.height) continue;
                if (s0.height <= spec.seaLevelY + 20) continue;
                for (int h = -1; h <= 1; h++)
                {
                    float yw = awayYaw + h * 25f, yr = math.radians(yw);
                    float2 fw = new float2(math.sin(yr), math.cos(yr));
                    int lane = 0;
                    for (int q = 1; q <= 15; q++)
                    {
                        float d = q * 100f;
                        int g = Surf(x + fw.x * d, z + fw.y * d);
                        if (g + 20 > s0.height + 6 - d / ratio) break;
                        lane = q;
                    }
                    int dr = s0.height - Surf(x + fw.x * 300f, z + fw.y * 300f);
                    if (lane > bestLane || (lane == bestLane && dr > bestDrop))
                    { bestLane = lane; bestDrop = dr; site = new int3((int)x, s0.height, (int)z); siteYaw = yw; }
                }
            }
            if ((++rows & 3) == 0) yield return null;
        }
        Debug.Log($"[GlideDemo] launch site {site} ({math.length(new float2(site.x - f.x, site.z - f.z)) * 0.1f:F0} m from the start), " +
                  $"{(site.y - spec.seaLevelY) * 0.1f:F1} m above sea, lane yaw {siteYaw:F0}: clear for {bestLane * 10} m, {bestDrop * 0.1f:F1} m drop 30 m out");

        // ---- go there: fly the camera over it, wait for the column, stand on it ----
        var camT = Camera.main.transform;
        _mode = MoveMode.Fly;
        Vector3 over = new Vector3(site.x * 0.1f + 0.05f, site.y * 0.1f + 6f, site.z * 0.1f + 0.05f);
        float tw = Time.unscaledTime;
        int top = -1;
        while (Time.unscaledTime - tw < 90f && (top = DryTopY(site.x, site.z)) < 0) { camT.position = over; _cam?.SetLook(siteYaw, 20f); yield return null; }
        for (int k = 0; k < 120; k++) { camT.position = over; _cam?.SetLook(siteYaw, 20f); yield return null; }
        top = DryTopY(site.x, site.z);
        if (top < 0) { Debug.Log($"[GlideDemo] the site never streamed in ({Time.unscaledTime - tw:F0} s) -- stop"); Application.Quit(); yield break; }
        _mode = MoveMode.Walk;
        var m = _player.Motor;
        m.Teleport(new float3(site.x * 0.1f + 0.05f, (top + 1) * 0.1f + 0.02f, site.z * 0.1f + 0.05f));
        m.ResolveSpawn();
        for (int s = 0; s < Inventory.HotbarSize; s++) if (_inv.IsEmpty(s)) { _inv.Selected = s; break; }

        IEnumerator Idle(int frames, float yaw, float pitch, MotorInput? input = null)
        {
            for (int i = 0; i < frames; i++) { _cam?.SetLook(yaw, pitch); _demoInput = input ?? default(MotorInput); yield return null; }
        }
        _status = "glide demo: a summit within 220 m of the start";
        yield return Idle(150, siteYaw, 12f);
        Debug.Log($"[GlideDemo] on the summit: feet {m.PositionM.y:F2} m (top row {top}, ShapeField {site.y}), grounded {m.Grounded}");

        // ---- 1. The summit.
        yield return DemoShot(dir, "1_summit");

        // ---- 2-4. Run, jump, double jump at the apex, hold Space (and W).
        MotorInput In(bool jump, bool hold, float pitch)
        {
            var i = MotorInput.FromLook(new float2(0f, 1f), siteYaw, pitch);
            i.JumpPressed = jump; i.UpHeld = hold;
            return i;
        }
        for (int i = 0; i < 20; i++) { _cam?.SetLook(siteYaw, 8f); _demoInput = In(false, false, 8f); yield return null; }   // a few steps of run-up
        _cam?.SetLook(siteYaw, 8f); _demoInput = In(true, true, 8f); yield return null;                                       // ground jump
        float3 jumpAt = m.PositionM;
        int guard = 0;
        while (m.VelocityMps.y > 0f && guard++ < 120) { _cam?.SetLook(siteYaw, 8f); _demoInput = In(false, true, 8f); yield return null; }
        _cam?.SetLook(siteYaw, 8f); _demoInput = In(true, true, 8f); yield return null;                                       // double jump
        bool dj = m.AirJumpsUsed >= c.airJumps;
        float hp0 = _vitals != null ? _vitals.Health : -1f;
        float glideT = 0f, impact = 0f, airT = 0f;
        float3 glideFrom = default; bool glideStarted = false; int heldFrames = 0;
        bool shot2 = false, shot3 = false;
        for (int i = 0; i < 60 * 60 && !(m.Grounded && airT > 0.3f) && !m.Swimming; i++)
        {
            float pitch = glideT > 4f ? 55f : 8f;
            _cam?.SetLook(siteYaw, pitch);
            _demoInput = In(false, true, pitch);
            yield return null;
            airT += Time.deltaTime;
            impact = math.max(impact, m.LastImpactDownMps);
            if (m.LastBlockedByNonResident) heldFrames++;
            if (m.Gliding)
            {
                if (!glideStarted) { glideStarted = true; glideFrom = m.PositionM; }
                glideT += Time.deltaTime;
            }
            if (!shot2 && glideT >= 2.5f)
            {
                shot2 = true;
                Debug.Log($"[GlideDemo] 2 gliding: {math.length(m.VelocityMps.xz):F2} m/s forward, {-m.VelocityMps.y:F2} m/s down, {m.PositionM.y - jumpAt.y:F1} m vs the summit");
                yield return DemoShot(dir, "2_gliding");
            }
            if (!shot3 && glideT >= 6f)
            {
                shot3 = true;
                int g = Surf(m.PositionM.x * 10f, m.PositionM.z * 10f);
                Debug.Log($"[GlideDemo] 3 looking down: ~{m.PositionM.y - g * 0.1f:F1} m above the ground (map surface)");
                yield return DemoShot(dir, "3_looking_down");
            }
        }
        float3 end = m.PositionM;
        int3 under = CoordMath.WorldToVoxel(end);
        for (int k = 0; k < 4 && _store.GetVoxel(under) == Materials.Air; k++) under.y--;
        float run = math.length((end - glideFrom).xz), drop = glideFrom.y - end.y;
        Debug.Log($"[GlideDemo] landed {(m.Swimming ? "in " + MaterialName(m.SwimFluid) : "on " + MaterialName(_store.GetVoxel(under)))} " +
                  $"after {airT:F1} s in the air ({glideT:F1} s gliding, double jump {dj}): {run:F1} m across, {drop:F1} m down, ratio {(drop > 0.01f ? run / drop : 0f):F2}; " +
                  $"impact {impact:F2} m/s -> fall damage {PlayerVitals.FallDamage(c, impact):F0} (a free fall from {jumpAt.y - end.y:F1} m: {PlayerVitals.FallDamage(c, math.sqrt(2f * c.gravityMps2 * math.max(0f, jumpAt.y - end.y))):F0}); " +
                  $"health {hp0:F0} -> {(_vitals != null ? _vitals.Health : -1f):F0}; frames held by unstreamed terrain {heldFrames}");
        yield return Idle(45, siteYaw, 18f);
        // ---- 4. Landed.
        yield return DemoShot(dir, "4_landed");
        Debug.Log("[GlideDemo] done");
        Application.Quit();
    }
}
