// Assets/Game/PlaygroundCapture.ShadowDots.cs
//
// -shadowdots (with -world tg2 -renderpreset quality; run-shadow-dots.sh): TASK 56
// (engine plan step 8, BUGS #1). The owner saw "shadow acne -- dots on the
// terrain" on grass slopes and lake-shore sand at a low sun, playing on the
// Quality preset (960x540, Full soft shadows, TAA off). Play also turns on
// temporal shadow reuse (RenderPreset.Apply: -tshadow defaults to 1), and play's
// default since task 52 is FSR 3, which keeps it on.
//
// Clock paused, fluid suspended; fly mode (Playground's capture-flag list).
// Poses: from the MAP (see the pose search below): two open sun-facing slopes
// and one flat near sea level where the golden-hour sun reaches the ground,
// looking 60 deg off the sun, pitched 22
// deg down. Each is shot at golden hour (6 deg) and at 12 deg. Arms per view:
//   q_ts1 / q_ts0     Quality preset, temporal shadows on / off
//   _still / _move    camera still, or strafing 0.02 m/frame while the frame is
//                     taken (the owner was walking)
//   beauty_*          the shipped image
//   sunvis_*          debug view 7 (sunVis in grey), compared voxel-for-voxel
//                     with the CPU oracle (ShadowRayReference.Trace, the
//                     reference definition of C.9/C.10) at the same pixels
//   fsr3_ts1/ts0_move play's default upscaler, beauty only
// plus one noon pose (the first view) at q_ts1/q_ts0 so a before/after
// shows nothing else changed. -shadowdotstag names the run (before/after).
//
// The oracle pass casts a CPU primary ray through every 3rd gate pixel centre
// (320x180 samples), takes the hit voxel and face, and traces the shadow ray on
// the ChunkStore. GPU "dark" = the 2x2 screen block under that gate pixel < 0.2,
// "lit" > 0.8 (display-encoded grey); CPU dark = sunVis < 0.05, lit > 0.5.
// falseDark = GPU dark where the oracle says lit: the dots, if they are a bug.
// A LOOK plus a count, not a measurement: no frame times.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private const int ShadowDotsStep = 3;          // oracle sample spacing, gate pixels
    private const float ShadowDotsStrafe = 0.02f;  // m per frame while "moving"

    private IEnumerator ShadowDots()
    {
        Screen.SetResolution(1920, 1080, false);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        _demoCaption = null;
        yield return Frames(300);
        string tag = ArgValue("-shadowdotstag") ?? "run";
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string root = Path.Combine(Application.persistentDataPath, "ShadowDots", ts + "_" + tag);
        Directory.CreateDirectory(root);
        _folder = root;
        LightingDriver.ClockPaused = true;
        Playground.DebugHideUi = true;
        Playground.DebugSuspendFluidSim = true;
        var far = FarField.Active;
        var cam = Camera.main;
        var sm = Phase4Bootstrapper.Streamer;
        var store = Phase4Bootstrapper.Store;
        L($"SHADOW DOTS ({tag}) -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  screen {Screen.width}x{Screen.height}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  kSoft {LightingConfig.Active.kSoft}  " +
          $"shadow level {RaymarchFeature.ShadowQualityLevel}  travelLOD {LightingConfig.Active.shadowTravelLod}");
        if (far == null || !far.UsesTg2 || !Phase4Bootstrapper.IsTg2World || pg == null)
        {
            L("needs the Playground with -world tg2");
            File.WriteAllText(Path.Combine(root, "shadowdots.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var map = far.Tg2Map;
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = far.Tg2SeaShift;
        Vector3 spawn = Phase4Bootstrapper.Tg2Spawn(sm.TerrainGenSource);

        // ---- poses: from the MAP, where a low sun actually reaches the ground ----
        // Every 40 m over the world: the horizon toward the golden-hour sun
        // (map heights every 20 m out to 1.5 km) must stay under 4 deg. Of those:
        // two slopes of 15-35 deg facing the sun (the owner's grass slopes) and one
        // flat (< 6 deg) within 4 m of sea level (the lake-shore sand), nearest the
        // spawn first, >= 300 m apart. The camera looks toward the sun's side,
        // 60 deg off it, 22 deg down. Ground re-snapped on the ChunkStore later.
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        float3 sdG = SunCycle.Direction;
        float2 sunH = math.normalize(new float2(sdG.x, sdG.z));
        float sunYaw = Mathf.Atan2(sdG.x, sdG.z) * Mathf.Rad2Deg;
        var spec = map.Spec;
        float seaM = (spec.seaLevelY + 1 - seaShift) * 0.1f;
        float GroundAt(float xM, float zM) => (TgGround(map, xM * 10f + off.x, zM * 10f + off.y) - seaShift) * 0.1f;
        float worldM = spec.worldSizeVoxels * 0.1f;
        float x0 = -off.x * 0.1f, z0 = -off.y * 0.1f;
        var slopes = new List<(Vector3 p, float slope, float dist)>();
        var flats = new List<(Vector3 p, float slope, float dist)>();
        int scanned = 0;
        for (float z = z0 + 200f; z < z0 + worldM - 200f; z += 40f)
        {
            for (float x = x0 + 200f; x < x0 + worldM - 200f; x += 40f)
            {
                scanned++;
                float h = GroundAt(x, z);
                if (h <= seaM + 0.05f) continue;                       // sea
                float hx = GroundAt(x + 3f, z) - GroundAt(x - 3f, z);
                float hz = GroundAt(x, z + 3f) - GroundAt(x, z - 3f);
                float2 grad = new float2(hx, hz) / 6f;
                float slope = math.degrees(math.atan(math.length(grad)));
                bool isSlope = slope >= 15f && slope <= 35f && math.dot(-math.normalizesafe(grad), sunH) > 0.5f;
                bool isFlat = slope < 6f && h - seaM < 4f;
                if (!isSlope && !isFlat) continue;
                bool open = true;
                for (float s = 20f; s <= 1500f && open; s += 20f)
                    if (GroundAt(x + sunH.x * s, z + sunH.y * s) - h > s * 0.07f) open = false;   // tan 4 deg
                if (!open) continue;
                var c = (new Vector3(x, h, z), slope, Vector2.Distance(new Vector2(x, z), new Vector2(spawn.x, spawn.z)));
                if (isSlope) slopes.Add(c); else flats.Add(c);
            }
            if (((int)z & 255) < 40) yield return null;
        }
        slopes.Sort((a, b) => a.dist.CompareTo(b.dist));
        flats.Sort((a, b) => a.dist.CompareTo(b.dist));
        var picks = new List<(string n, Vector3 p)>();
        foreach (var c in slopes)
        {
            if (picks.Count >= 2) break;
            if (picks.Count == 1 && Vector3.Distance(picks[0].p, c.p) < 300f) continue;
            picks.Add(($"slope{picks.Count + 1}", c.p));
        }
        if (flats.Count > 0) picks.Add(("shore", flats[0].p));
        L($"pose search at golden hour (sun yaw {sunYaw:F0}): {scanned} map points, {slopes.Count} open sun-facing slopes, {flats.Count} open flats near sea level");

        var poses = new List<(string n, Vector3 eye, float yaw)>();
        foreach (var (pn, p) in picks)
        {
            Vector3 eye = p + Vector3.up * 1.7f;
            float yaw = sunYaw + 60f;
            cam.transform.position = eye + Vector3.up * 20f;
            cam.transform.rotation = Quaternion.Euler(22f, yaw, 0f);
            float t0 = Time.realtimeSinceStartup;
            yield return NearSettle(far, sm);
            while (sm != null && sm.LoadDeficit() > 0 && Time.realtimeSinceStartup - t0 < 480f) yield return Frames(30);
            yield return Frames(60);
            L($"  {pn}: settled in {Time.realtimeSinceStartup - t0:F0} s, deficit {(sm != null ? sm.LoadDeficit() : -1)}");
            float g = StoreGroundM(store, p.x, p.z, p.y + 25f);
            if (float.IsNaN(g)) { L($"  {pn}: no ChunkStore ground at {p:F1} -- skipped"); continue; }
            eye.y = g + 1.7f;
            byte mat = store.GetVoxel(CoordMath.WorldToVoxel(new float3(p.x, g - 0.05f, p.z)));
            L($"  {pn}: eye {eye:F1} (map ground {p.y:F1}, store {g:F1}), ground material {mat}, yaw {yaw:F0}");
            poses.Add(($"{pn}_m{mat}", eye, yaw));
        }
        if (poses.Count == 0) { L("no sunlit pose found"); File.WriteAllText(Path.Combine(root, "shadowdots.txt"), _log.ToString()); Application.Quit(3); yield break; }

        // ---- every pose x sun elevation x arm ----
        L("");
        L("oracle columns: compared = gate samples with a GPU and a CPU verdict; falseDark = GPU dark, oracle lit; falseLit = GPU lit, oracle dark");
        foreach (float elev in new[] { SunCycle.GoldenHourElevationDegrees, 12f })
        {
            SunCycle.SetHours(elev == SunCycle.GoldenHourElevationDegrees ? SunCycle.GoldenHourHours : SunCycle.HoursForElevation(elev, morning: false));
            foreach (var (pn, eye0, yaw) in poses)
            {
                string name = $"e{elev:F0}_{pn}";
                Quaternion rot = Quaternion.Euler(22f, yaw, 0f);
                RenderPreset.Apply(RenderPreset.Kind.Quality);
                Vector3 eye = eye0;
                cam.transform.position = eye; cam.transform.rotation = rot;
                float t1 = Time.realtimeSinceStartup;
                yield return NearSettle(far, sm);
                while (sm != null && sm.LoadDeficit() > 0 && Time.realtimeSinceStartup - t1 < 480f) yield return Frames(30);
                float gs = StoreGroundM(store, eye.x, eye.z, eye.y + 20f);
                if (!float.IsNaN(gs)) eye.y = gs + 1.7f;
                bool inAir = store.GetVoxel(CoordMath.WorldToVoxel(new float3(eye.x, eye.y, eye.z))) == 0;
                L($"     re-snapped after settling: ground {(float.IsNaN(gs) ? "NOT FOUND" : gs.ToString("F1"))}, eye {(inAir ? "in air" : "INSIDE ROCK")}");
                cam.transform.position = eye;
                yield return Frames(30);
                L($"---- {name}: sun {SunCycle.ElevationDegrees:F1} deg, eye {eye:F1} yaw {yaw:F0}  deficit {(sm != null ? sm.LoadDeficit() : -1)}");
                foreach (bool tsOn in new[] { true, false })
                {
                    string arm = tsOn ? "q_ts1" : "q_ts0";
                    RenderPreset.Apply(RenderPreset.Kind.Quality);
                    RaymarchFeature.TemporalShadows = tsOn;
                    foreach (bool move in new[] { false, true })
                    {
                        string m = move ? "move" : "still";
                        RaymarchFeature.UseDebugViewOverride = false;
                        yield return ShadowDotsShot(cam, eye, rot, move, $"{name}_beauty_{arm}_{m}", null, null);
                        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
                        RaymarchFeature.UseDebugViewOverride = true;
                        yield return ShadowDotsShot(cam, eye, rot, move, $"{name}_sunvis_{arm}_{m}", store, $"{arm}_{m}");
                        RaymarchFeature.UseDebugViewOverride = false;
                    }
                }
                foreach (bool tsOn in new[] { true, false })
                {
                    UpscalerSelect.Apply(UpscalerSelect.Kind.Fsr3, 540, false);
                    RaymarchFeature.TemporalShadows = tsOn;
                    yield return ShadowDotsShot(cam, eye, rot, true, $"{name}_beauty_fsr3_ts{(tsOn ? 1 : 0)}_move", null, null);
                }
                RenderPreset.Apply(RenderPreset.Kind.Quality);
            }
        }

        // ---- noon control: nothing else may change ----
        SunCycle.SetHours(SunCycle.NoonHours);
        var sp = poses[0].eye;
        { cam.transform.position = sp; float t2 = Time.realtimeSinceStartup; yield return NearSettle(far, sm);
          while (sm != null && sm.LoadDeficit() > 0 && Time.realtimeSinceStartup - t2 < 480f) yield return Frames(30);
          float gn = StoreGroundM(store, sp.x, sp.z, sp.y + 20f); if (!float.IsNaN(gn)) sp.y = gn + 1.7f; }
        var sr = Quaternion.Euler(22f, poses[0].yaw, 0f);
        L("");
        L($"---- noon control ({poses[0].n}), elevation {SunCycle.ElevationDegrees:F1} deg");
        foreach (bool tsOn in new[] { true, false })
        {
            RenderPreset.Apply(RenderPreset.Kind.Quality);
            RaymarchFeature.TemporalShadows = tsOn;
            cam.transform.position = sp; cam.transform.rotation = sr;
            yield return Frames(60);
            yield return ShadowDotsShot(cam, sp, sr, false, $"noon_beauty_q_ts{(tsOn ? 1 : 0)}_still", null, null);
            RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
            RaymarchFeature.UseDebugViewOverride = true;
            yield return ShadowDotsShot(cam, sp, sr, false, $"noon_sunvis_q_ts{(tsOn ? 1 : 0)}_still", store, $"noon_ts{(tsOn ? 1 : 0)}");
            RaymarchFeature.UseDebugViewOverride = false;
        }

        File.WriteAllText(Path.Combine(root, "shadowdots.txt"), _log.ToString());
        Debug.Log("[shadowdots]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// One shot. Still: 40 frames at the pose. Moving: start 0.8 m to the left
    /// and strafe right ShadowDotsStrafe per frame, the frame being shot still
    /// moving (it lands back on the pose). With `store`, compare to the oracle.
    private IEnumerator ShadowDotsShot(Camera cam, Vector3 eye, Quaternion rot, bool move, string file,
                                       ChunkStore store, string label)
    {
        Vector3 right = rot * Vector3.right;
        cam.transform.rotation = rot;
        bool stats = store == null;     // beauty shots: count one frame's shadow rays
        uint[] st = null;
        if (move)
        {
            int n = 40;
            for (int f = n; f > 0; f--)
            {
                cam.transform.position = eye - right * (ShadowDotsStrafe * f);
                if (stats && f == 1) { RaymarchFeature.CollectShadowStats = true; RaymarchFeature.ResetShadowStats(); }
                yield return null;
                if (stats && f == 1) { yield return new WaitForEndOfFrame(); st = RaymarchFeature.ReadShadowStats(); RaymarchFeature.CollectShadowStats = false; }
            }
            cam.transform.position = eye;
        }
        else
        {
            cam.transform.position = eye;
            yield return Frames(40);
            if (stats)
            {
                RaymarchFeature.CollectShadowStats = true; RaymarchFeature.ResetShadowStats();
                yield return null;
                yield return new WaitForEndOfFrame();
                st = RaymarchFeature.ReadShadowStats(); RaymarchFeature.CollectShadowStats = false;
            }
        }
        if (st != null)
            L($"  rays {file.Substring(file.IndexOf("_beauty_") + 8),-16} hit {st[0],7}  traced {st[2],7}  ({100f * st[2] / Mathf.Max(1u, st[0]):F1}% of hit pixels traced this frame)");
        yield return new WaitForEndOfFrame();
        Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(_folder, file + ".png"), t.EncodeToPNG());
        if (store != null)
        {
            // The camera holds still while the CPU works, but the shot is already taken.
            Vector3 camPos = cam.transform.position;
            Quaternion camRot = cam.transform.rotation;
            yield return ShadowOracleCompare(store, cam, camPos, camRot, t, file, label);
        }
        Destroy(t);
        cam.transform.position = eye;
    }

    private IEnumerator ShadowOracleCompare(ChunkStore store, Camera cam, Vector3 camPos, Quaternion camRot, Texture2D shot,
                                            string file, string label)
    {
        int gw = 960 / ShadowDotsStep, gh = 540 / ShadowDotsStep;
        var img = new Texture2D(gw, gh, TextureFormat.RGB24, false);
        float3 sunDir = SunCycle.Direction;
        float kSoft = LightingConfig.Active.kSoft;
        float3 camM = new float3(camPos.x, camPos.y, camPos.z);
        int compared = 0, falseDark = 0, falseLit = 0, litBoth = 0, darkBoth = 0, sky = 0;
        var examples = new List<string>();
        float sx = Screen.width / 960f, sy = Screen.height / 540f;
        for (int j = 0; j < gh; j++)
        {
            cam.transform.position = camPos; cam.transform.rotation = camRot;
            for (int i = 0; i < gw; i++)
            {
                int gx = i * ShadowDotsStep + 1, gy = j * ShadowDotsStep + 1;
                Ray ray = cam.ViewportPointToRay(new Vector3((gx + 0.5f) / 960f, (gy + 0.5f) / 540f, 0f));
                Color c = new Color(0f, 0f, 0f);
                if (!CpuPrimaryHit(store, camM, new float3(ray.direction.x, ray.direction.y, ray.direction.z), 120f,
                                   out int3 hv, out int3 hn))
                { sky++; img.SetPixel(i, j, c); continue; }
                var r = ShadowRayReference.Trace(store, hv, hn, sunDir, camM, kSoft);
                // N.L <= 0 faces: the shader still traces them; the oracle's ray starts
                // in front of the face too, so both see the same thing.
                int px = (int)(gx * sx), py = (int)(gy * sy);
                float g = 0.25f * (shot.GetPixel(px, py).r + shot.GetPixel(px + 1, py).r +
                                   shot.GetPixel(px, py + 1).r + shot.GetPixel(px + 1, py + 1).r);
                bool gDark = g < 0.2f, gLit = g > 0.8f;
                bool cDark = r.sunVis < 0.05f, cLit = r.sunVis > 0.5f;
                if ((gDark || gLit) && (cDark || cLit))
                {
                    compared++;
                    if (gDark && cLit)
                    {
                        falseDark++; c = new Color(1f, 0f, 0f);
                        if (examples.Count < 6) examples.Add($"gate ({gx},{gy}) voxel {hv} face {hn} gpu {g:F2} oracle {r.sunVis:F2}");
                    }
                    else if (gLit && cDark)
                    {
                        falseLit++; c = new Color(0f, 0.4f, 1f);
                        if (examples.Count < 6) examples.Add($"gate ({gx},{gy}) voxel {hv} face {hn} gpu {g:F2} oracle {r.sunVis:F2} (false LIT)");
                    }
                    else if (gLit) { litBoth++; c = new Color(0.75f, 0.75f, 0.75f); }
                    else { darkBoth++; c = new Color(0.25f, 0.25f, 0.25f); }
                }
                else c = new Color(0.9f, 0.8f, 0.1f);   // a penumbra on either side: not judged
                img.SetPixel(i, j, c);
            }
            if ((j & 7) == 7) yield return null;
        }
        img.Apply();
        File.WriteAllBytes(Path.Combine(_folder, file + "_oracle.png"), img.EncodeToPNG());
        Destroy(img);
        float pct = compared > 0 ? 100f * falseDark / compared : 0f;
        L($"  oracle {label,-12} compared {compared,6}  litBoth {litBoth,6}  darkBoth {darkBoth,6}  " +
          $"falseDark {falseDark,5} ({pct:F2}%)  falseLit {falseLit,5}  sky {sky}");
        foreach (string e in examples) L($"      {e}");
    }

    /// CPU primary ray: the first solid voxel and the face it was entered by.
    private static bool CpuPrimaryHit(ChunkStore store, float3 originM, float3 dir, float maxM,
                                      out int3 v, out int3 n)
    {
        float3 o = originM * 10f;
        float3 d = math.normalize(dir);
        int3 step = (int3)math.sign(d);
        float3 tDelta = math.abs(1f / d);
        v = CoordMath.WorldToVoxel(originM);
        n = int3.zero;
        float3 tMax = new float3(
            step.x > 0 ? (v.x + 1f - o.x) : (o.x - v.x),
            step.y > 0 ? (v.y + 1f - o.y) : (o.y - v.y),
            step.z > 0 ? (v.z + 1f - o.z) : (o.z - v.z)) * tDelta;
        float t = 0f, tEnd = maxM * 10f;
        while (t < tEnd)
        {
            if (store.GetVoxel(v) != 0) return true;
            if (tMax.x < tMax.y && tMax.x < tMax.z) { v.x += step.x; t = tMax.x; tMax.x += tDelta.x; n = new int3(-step.x, 0, 0); }
            else if (tMax.y < tMax.z) { v.y += step.y; t = tMax.y; tMax.y += tDelta.y; n = new int3(0, -step.y, 0); }
            else { v.z += step.z; t = tMax.z; tMax.z += tDelta.z; n = new int3(0, 0, -step.z); }
        }
        return false;
    }
}
