// Assets/Game/PlaygroundCapture.TaaRisk.cs
//
// -taarisk (with -world tg2; overnight task 07): TAA's risks, FRAME BY FRAME.
// Run once with -taa 0 and once with -taa 1 (same binary); every camera step
// is per FRAME, not per second, so frame k of one run has the same pose as
// frame k of the other and the two can be laid side by side
// (tools/taarisk-report.py). Captures stall a frame; per-frame steps make that
// harmless (motion steps model 30 fps: the slower the frame, the larger the
// per-frame motion TAA must reject).
//   edit   eye_level_1's eye looking at the ground ~10 m ahead; 90 still
//          frames; a 1.5 m sphere dug; edit_pre, edit_f0..f5 (consecutive),
//          edit_settled 60 frames later. The hole must be clean in the first
//          frame that shows it.
//   fluid  same view: a water column poured into the hole, a lava column 3 m to
//          the right; three runs of 8 consecutive frames (at once, +45, +180).
//   spin   eye_level_2: yaw 1.5 deg/frame (45 deg/s at 30 fps) and 6 deg/frame
//          (180 deg/s), 8 consecutive frames after 16 of motion, then 4 frames
//          after the turn stops.
//   fly    from eye_level_2 toward its view, 60 m/s at 30 fps = 2 m/frame,
//          ~15 m above the ground, pitched 12 deg down; 8 consecutive frames
//          after 24 of flight.
//   light  eye_level_2's view; the sun stepped 0.2 deg/frame (under the 0.25 deg
//          history reset), 0.5 and 0.9 (under the old ~1 deg reset), and 2, 8 consecutive frames
//          after 16 of motion, straddling golden hour.
// Without -world (the default Playground world) only edit and fluid run, at
// the fluid arena (TaaRiskArena). Noon (except light), clock paused, 960x540 gate. Diagnostic frames only: no
// timing here is a measurement.
using System;
using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator TaaRisk()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string arm = RaymarchFeature.Taa ? (RaymarchFeature.TaaChecker ? "chk" : "on") : "off";
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + "_taarisk_" + arm);
        Directory.CreateDirectory(_folder);
        LightingDriver.ClockPaused = true;
        SunCycle.SetHours(12f);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
        Playground.DebugHideUi = true;
        var far = FarField.Active;
        var cam = Camera.main;
        var sm = Phase4Bootstrapper.Streamer;
        var store = Phase4Bootstrapper.Store;
        L($"TAA RISK FRAMES -- {ts}  TAA {(RaymarchFeature.Taa ? $"ON{(RaymarchFeature.TaaChecker ? " + CHECKERBOARD" : "")} (sharpen {RaymarchFeature.TaaSharpenAmount:F2})" : "OFF")}; " +
          $"gate 960x540; preset {(HasFlag("-renderpreset") ? RenderPreset.Current.ToString() : "not set")}");
        if (pg != null && !Phase4Bootstrapper.IsTg2World)
        {
            yield return TaaRiskArena(pg, store);
            yield break;
        }
        if (far == null || !far.UsesTg2 || pg == null)
        {
            L("needs the Playground (tg2 for spin/fly/light, the default world for edit/fluid)");
            File.WriteAllText(Path.Combine(_folder, "taarisk.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var map = far.Tg2Map;
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = far.Tg2SeaShift;
        Vector3 E(float3 g) => new Vector3((g.x - off.x) * 0.1f, (g.y - seaShift) * 0.1f, (g.z - off.y) * 0.1f);
        var poses = Tg2EyeLevelPoses(map);
        Vector3 eye1 = E(poses[0].eye), at1 = E(poses[0].at);
        Vector3 eye2 = E(poses[1].eye), at2 = E(poses[1].at);

        // ---- edit ----
        Vector3 fwd = at1 - eye1; fwd.y = 0f; fwd.Normalize();
        cam.transform.position = eye1;
        cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        yield return NearSettle(far, sm);
        float gEye = StoreGroundM(store, eye1.x, eye1.z, eye1.y + 10f);
        if (!float.IsNaN(gEye)) eye1.y = gEye + 1.7f;
        Vector3 target = Vector3.zero; bool found = false;
        foreach (float d in new[] { 10f, 13f, 16f, 8f, 20f })
        {
            Vector3 p = eye1 + fwd * d;
            float g = StoreGroundM(store, p.x, p.z, eye1.y + 15f);
            if (float.IsNaN(g) || g > eye1.y - 0.5f) continue;
            target = new Vector3(p.x, g, p.z); found = true; break;
        }
        if (!found) { target = eye1 + fwd * 10f; target.y = eye1.y - 1.7f; L("edit: no open ground found ahead; using a guess"); }
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Vector3 look = target + right * 1.5f + Vector3.up * 0.5f;
        cam.transform.position = eye1;
        cam.transform.rotation = Quaternion.LookRotation((look - eye1).normalized, Vector3.up);
        L($"edit/fluid view: eye {eye1:F1}, ground target {target:F1} ({Vector3.Distance(eye1, target):F1} m)");
        yield return NearSettle(far, sm);
        yield return Frames(90);
        yield return TaaRiskShot("edit_pre");
        int3 hole = CoordMath.WorldToVoxel(new float3(target.x, target.y - 0.4f, target.z));
        int dug = pg.DebugEdits.SetSphere(hole, 15, Materials.Air);
        L($"dug {dug} voxels at {hole} on frame {Time.frameCount}");
        for (int k = 0; k <= 5; k++) yield return TaaRiskShot($"edit_f{k}");
        yield return Frames(60);
        yield return TaaRiskShot("edit_settled");

        // ---- fluid ----
        int3 wLo = hole + new int3(-3, 20, -3), wHi = hole + new int3(3, 40, 3);
        int3 lc = CoordMath.WorldToVoxel(new float3(target.x + right.x * 3f, target.y + 1.5f, target.z + right.z * 3f));
        int nW = pg.DebugEdits.SetBox(wLo, wHi, Materials.Water);
        int nL = pg.DebugEdits.SetBox(lc + new int3(-3, 0, -3), lc + new int3(3, 15, 3), Materials.Lava);
        L($"fluid: water {nW} voxels over the hole, lava {nL} voxels at {lc}, on frame {Time.frameCount}");
        for (int k = 0; k < 8; k++) yield return TaaRiskShot($"fluid_a{k}");
        yield return Frames(45);
        for (int k = 0; k < 8; k++) yield return TaaRiskShot($"fluid_b{k}");
        yield return Frames(180);
        for (int k = 0; k < 8; k++) yield return TaaRiskShot($"fluid_c{k}");

        // ---- spin ----
        foreach (var (name, deg) in new[] { ("spin_slow", 1.5f), ("spin_fast", 6f) })
        {
            cam.transform.position = eye2;
            Quaternion r0 = Quaternion.LookRotation((at2 - eye2).normalized, Vector3.up);
            cam.transform.rotation = r0;
            yield return NearSettle(far, sm);
            yield return Frames(60);
            yield return TaaRiskShot($"{name}_still");
            Vector3 eul = r0.eulerAngles;
            for (int k = 0; k < 24; k++)
            {
                cam.transform.rotation = Quaternion.Euler(eul.x, eul.y + deg * (k + 1), 0f);
                if (k >= 16) yield return TaaRiskShot($"{name}_m{k - 16}"); else yield return null;
            }
            for (int k = 0; k < 4; k++) yield return TaaRiskShot($"{name}_stop{k}");
        }

        // ---- fly ----
        {
            Vector3 f2 = at2 - eye2; f2.y = 0f; f2.Normalize();
            Vector3 p = eye2; p.y = WalkEyeY(p.x, p.z, p.y) + 13.4f;
            cam.transform.position = p;
            cam.transform.rotation = Quaternion.LookRotation(f2, Vector3.up) * Quaternion.Euler(12f, 0f, 0f);
            yield return NearSettle(far, sm);
            for (int k = 0; k < 32; k++)
            {
                p += f2 * 2f;
                p.y = math.lerp(p.y, WalkEyeY(p.x, p.z, p.y) + 13.4f, 0.15f);
                cam.transform.position = p;
                if (k >= 24) yield return TaaRiskShot($"fly_m{k - 24}"); else yield return null;
            }
            L($"fly: ended at {p:F1}");
        }

        // ---- light ----
        cam.transform.position = eye2;
        cam.transform.rotation = Quaternion.LookRotation((at2 - eye2).normalized, Vector3.up);
        float h0 = SunCycle.GoldenHourHours - 0.75f;   // only where the rate is measured
        float3 sA = SunCycle.DirectionAt(h0), sB = SunCycle.DirectionAt(h0 + 0.05f);
        float degPerHour = math.degrees(math.acos(math.clamp(math.dot(math.normalize(sA), math.normalize(sB)), -1f, 1f))) / 0.05f;
        L($"light: start {h0:F2} h, sun moves {degPerHour:F2} deg per hour there");
        foreach (var (name, deg) in new[] { ("light_02", 0.2f), ("light_05", 0.5f), ("light_09", 0.9f), ("light_20", 2f) })
        {
            // the 8 captured frames (16..23 of 24) straddle golden hour
            float dh = deg / math.max(0.01f, degPerHour);
            float h = SunCycle.GoldenHourHours - 20f * dh;
            SunCycle.SetHours(h);
            yield return NearSettle(far, sm);
            yield return Frames(60);
            yield return TaaRiskShot($"{name}_still");
            for (int k = 0; k < 24; k++)
            {
                h += dh; SunCycle.SetHours(h);
                if (k >= 16) yield return TaaRiskShot($"{name}_m{k - 16}"); else yield return null;
            }
            L($"{name}: {dh:F4} h/frame, ended at {h:F2} h");
        }

        L($"shots: {_shots}");
        File.WriteAllText(Path.Combine(_folder, "taarisk.txt"), _log.ToString());
        Debug.Log("[PlaygroundCapture]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// Default world (no -world): edit and fluid at the Playground's fluid arena
    /// (in tg2, fluid placed ~2-4 m above the ground at y ~695 m did not move in
    /// 225 frames -- not investigated here). -playgroundshots' elevated arena
    /// vantage; a 2 m sphere dug in the basin floor; then the CHAOS BURST plus
    /// the vents (water, sand and lava pouring), three runs of 8 frames.
    private IEnumerator TaaRiskArena(Playground pg, ChunkStore store)
    {
        var cam = Camera.main;
        pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
        yield return null;
        Vector3 b = cam.transform.position;
        cam.transform.position = new Vector3(b.x - 5.5f, b.y + 7.5f, b.z - 5.5f);
        cam.transform.rotation = Quaternion.Euler(34f, 45f, 0f);
        yield return Frames(240);
        // basin floor under the view's centre ray (CPU DDA through the store)
        Vector3 eye = cam.transform.position, dir = cam.transform.forward;
        float d = NearDepthM(store, new float3(eye.x, eye.y, eye.z), new float3(dir.x, dir.y, dir.z), 60f);
        int3 ac = pg.DebugArenaCentre;
        int3 hole = d > 0f ? CoordMath.WorldToVoxel(new float3(eye.x + dir.x * d, eye.y + dir.y * d, eye.z + dir.z * d)) : ac;
        L($"arena: centre {ac}, eye {eye:F1}, view centre hits ground at {d:F1} m -> dig at {hole}");
        yield return Frames(90);
        yield return TaaRiskShot("edit_pre");
        int dug = pg.DebugEdits.SetSphere(hole, 20, Materials.Air);
        L($"dug {dug} voxels at {hole} on frame {Time.frameCount}");
        for (int k = 0; k <= 5; k++) yield return TaaRiskShot($"edit_f{k}");
        yield return Frames(60);
        yield return TaaRiskShot("edit_settled");

        pg.SendMessage("StartChaosBurst", SendMessageOptions.DontRequireReceiver);
        pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        L($"fluid: chaos burst + vents opened on frame {Time.frameCount}");
        yield return Frames(30);
        for (int k = 0; k < 8; k++) yield return TaaRiskShot($"fluid_a{k}");
        yield return Frames(90);
        for (int k = 0; k < 8; k++) yield return TaaRiskShot($"fluid_b{k}");
        yield return Frames(300);
        for (int k = 0; k < 8; k++) yield return TaaRiskShot($"fluid_c{k}");

        L($"shots: {_shots}");
        File.WriteAllText(Path.Combine(_folder, "taarisk.txt"), _log.ToString());
        Debug.Log("[PlaygroundCapture]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    private IEnumerator TaaRiskShot(string name)
    {
        yield return CaptureTo(name);
        _shots++;
        Vector3 p = Camera.main.transform.position;
        L($"{name}.png  frame {Time.frameCount}  cam {p:F2}  yaw {Camera.main.transform.eulerAngles.y:F1}  sun {SunCycle.Hours:F3} h");
    }
}
