// Assets/Game/PlaygroundCapture.LodRef.cs
//
// -lookreview -lodref 1: TASK 129, does the game keep the 0.1 m detail at distance D?
// (owner 2026-10-23: "for terrain 200m away, I want to build that terrain at 0.1 and
// look at it from 200 m away and compare that with the actual game view").
// Capture-only: no product behaviour, no default changes, no timing.
//   SCENES  (generator-based, identical across launches of one world; -lodrefscenes
//           filters) ridge: the steepest cross-lit face within 1.2 km of the centre;
//           coast: a shore point with sea behind the camera and land ahead; plain:
//           the flattest land. Each has a target point P and one heading h for every D.
//   POSES   for each D in -lodrefd (default 50,100,200,400,800,1600,3300,6600 m) the
//           eye is D from P along -h, raised by the scene's depression angle (the
//           smallest from its start value up that sees P from every D over the
//           generator heightfield), looking at P.
//   LIGHT   task 127's: the afternoon hour with the sun 40 deg up, h 75 (+-15) deg off
//           the sun's azimuth.
//   PASSES  per pose, camera fixed:
//           GAME  everything as shipped, centred on the camera (shells get -lodrefwait s,
//                 the launch's first pose -lodreffirstwait s).
//           REF   the same frame, but the 0.1 m window (CPU store + GPU mirror, the
//                 normal tier 0: 204.8 m x 204.8 m x the 7-layer band) is centred on P
//                 (Phase4Bootstrapper.RefTier0Centre) and tier 0 may march 409.6 m past
//                 its entry face (RaymarchFeature.MaxRayDistance). The shells and the far
//                 band stay on the camera. REF pixels are only the ones tier 0 drew
//                 (tools/lodref129.py masks the rest out with debug view 14).
//           ARMS  native (1080 rows, no upscaler, no TAA), hit (debug view 14 at
//                 native), fsr540 / fsr720 (FSR 3 fixed at the play range's ends),
//                 taa (our TAA resolve at native rows). -lodrefarms filters.
//   OUTPUT  <scene>_<D>_<game|ref>_<arm>.png, lodref_poses.json (cameras, the REF box,
//           the shells), lookreview.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private struct RefCol { public float topM; public bool land; }

    private static RefCol RefColumn(float xM, float zM)
    {
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        if (src == null) return new RefCol { topM = 0f, land = false };
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, xM * 10f, zM * 10f, 1);
        bool land = col.height >= spec.seaLevelY && col.water <= col.height;
        int top = math.max(math.max(col.height, col.water), spec.seaLevelY);
        return new RefCol { topM = (top + 1) * 0.1f, land = land };
    }

    private static float RefSeaM()
    {
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        return src != null ? (src.Spec.seaLevelY + 1) * 0.1f : 0f;
    }

    /// The segment eye -> P clears the heightfield by 1 m, except its last 15 m.
    private static bool RefLineClear(float3 eye, float3 p)
    {
        float3 d = p - eye; float len = math.length(d);
        int n = Math.Max(20, (int)(len / 8f));
        float tEnd = 1f - 15f / math.max(len, 16f);
        for (int i = 1; i < n; i++)
        {
            float t = tEnd * i / n;
            float3 q = eye + d * t;
            if (RefColumn(q.x, q.z).topM > q.y - 1f) return false;
        }
        return true;
    }

    private IEnumerator LodRef(FarField far, Camera cam, StreamManager sm, ShellStreamer sh)
    {
        float elev = ArgFloat("-lookcrosssun", 40f), off = ArgFloat("-lookcrossoff", 75f);
        float wait = ArgFloat("-lodrefwait", 90f), firstWait = ArgFloat("-lodreffirstwait", 240f), refWait = ArgFloat("-lodrefrefwait", 120f);
        int settle = ArgInt("-lodrefframes", 60);
        float refMaxRay = ArgFloat("-lodrefmaxray", 4096f);
        string sceneArg = ArgValue("-lodrefscenes"), armArg = ArgValue("-lodrefarms"), dArg = ArgValue("-lodrefd");
        var scenesOn = string.IsNullOrEmpty(sceneArg) ? null : new HashSet<string>(sceneArg.Split(','));
        var arms = new List<string>(string.IsNullOrEmpty(armArg) ? new[] { "native", "hit", "fsr540", "fsr720", "taa" } : armArg.Split(','));
        var dists = new List<float>();
        foreach (var s in (string.IsNullOrEmpty(dArg) ? "50,100,200,400,800,1600,3300,6600" : dArg).Split(','))
            dists.Add(float.Parse(s, CultureInfo.InvariantCulture));

        float hours = SunCycle.HoursForElevation(elev, false);
        SunCycle.SetHours(hours);
        float3 s3 = SunCycle.DirectionAt(hours);
        float2 toSun = math.normalizesafe(new float2(s3.x, s3.z));
        float sunYaw = math.degrees(math.atan2(toSun.x, toSun.y));
        float2 Rot(float2 v, float deg) { float a = math.radians(deg), c = math.cos(a), s = math.sin(a); return new float2(v.x * c + v.y * s, -v.x * s + v.y * c); }
        float AngleDeg(float2 a, float2 b) => math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(a), math.normalizesafe(b)), -1f, 1f)));
        var cross = new List<float2>();
        foreach (float sg in new[] { -1f, 1f }) foreach (float d in new[] { off - 15f, off, off + 15f })
            cross.Add(Rot(toSun, sg * math.clamp(d, 0f, 180f)));
        L($"LOD REF (task 129): {hours:F2} h, sun elevation {SunCycle.ElevationDegreesAt(hours):F1} deg, yaw {sunYaw:F0} deg; " +
          $"distances {string.Join(",", dists)} m; arms {string.Join(",", arms)}; shell wait {wait:F0} s, REF fill cap {refWait:F0} s, " +
          $"{settle} frames per arm; camera vFOV {cam.fieldOfView:F1} deg, {Screen.width}x{Screen.height}");

        Vector3 c0 = cam.transform.position;
        float seaM = RefSeaM();
        // ---- the three scenes ----
        var scenes = new List<(string n, float3 p, float2 h, float theta, string note)>();
        {
            // ridge: steepest relief over +-40 m whose uphill heading is cross-lit
            float bestS = -1f; float3 bp = 0; float2 bh = 0; float bSlope = 0f;
            for (float dz = -1200f; dz <= 1200f; dz += 20f)
            for (float dx = -1200f; dx <= 1200f; dx += 20f)
            {
                if (dx * dx + dz * dz > 1200f * 1200f) continue;
                float x = c0.x + dx, z = c0.z + dz;
                var cc = RefColumn(x, z); if (!cc.land) continue;
                float gx = (RefColumn(x + 40f, z).topM - RefColumn(x - 40f, z).topM) / 80f;
                float gz = (RefColumn(x, z + 40f).topM - RefColumn(x, z - 40f).topM) / 80f;
                float2 g = new float2(gx, gz); float gl = math.length(g);
                if (gl < 0.5f) continue;
                float2 up = g / gl; float bestA = 180f;
                foreach (var hc in cross) bestA = math.min(bestA, AngleDeg(up, hc));
                if (bestA > 20f) continue;
                if (gl > bestS) { bestS = gl; bp = new float3(x, cc.topM, z); bh = up; bSlope = math.degrees(math.atan(gl)); }
            }
            if (bestS > 0f) scenes.Add(("ridge", bp, bh, 8f, $"relief slope {bSlope:F0} deg over +-40 m, camera looks uphill into the face"));
            else L("ridge: no cross-lit face steeper than 27 deg within 1.2 km -- skipped");
        }
        {
            // coast: a shore point (land 0.5-4 m over the sea) with sea behind the camera for 1.5 km and land ahead
            float bestS = -1f; float3 bp = 0; float2 bh = 0;
            for (float dz = -2000f; dz <= 2000f; dz += 20f)
            for (float dx = -2000f; dx <= 2000f; dx += 20f)
            {
                if (dx * dx + dz * dz > 2000f * 2000f) continue;
                float x = c0.x + dx, z = c0.z + dz;
                var cc = RefColumn(x, z);
                if (!cc.land || cc.topM < seaM + 0.5f || cc.topM > seaM + 4f) continue;
                foreach (var hc in cross)
                {
                    if (RefColumn(x - hc.x * 40f, z - hc.y * 40f).land) continue;
                    int n = 0, sea = 0, land = 0;
                    for (float t = 100f; t <= 1500f; t += 100f, n++) if (!RefColumn(x - hc.x * t, z - hc.y * t).land) sea++;
                    for (float t = 20f; t <= 200f; t += 20f) if (RefColumn(x + hc.x * t, z + hc.y * t).land) land++;
                    float sc = sea / (float)n + land / 10f;
                    if (sc > bestS) { bestS = sc; bp = new float3(x, cc.topM, z); bh = hc; }
                }
            }
            if (bestS > 0f) scenes.Add(("coast", bp, bh, 10f, $"shore point, sea behind the camera / land ahead score {bestS:F2} (2 = best)"));
            else L("coast: no cross-lit shore within 2 km -- skipped");
        }
        {
            // plain: the flattest land (relief over +-40 m), not near water, nearest the centre on ties
            float bestS = float.MaxValue; float3 bp = 0;
            for (float dz = -1200f; dz <= 1200f; dz += 20f)
            for (float dx = -1200f; dx <= 1200f; dx += 20f)
            {
                if (dx * dx + dz * dz > 1200f * 1200f) continue;
                float x = c0.x + dx, z = c0.z + dz;
                var cc = RefColumn(x, z); if (!cc.land || cc.topM < seaM + 3f) continue;
                float mx = cc.topM, mn = cc.topM; bool wet = false;
                for (int k = 0; k < 8 && !wet; k++)
                {
                    float a = k * math.PI / 4f;
                    var q = RefColumn(x + math.cos(a) * 40f, z + math.sin(a) * 40f);
                    var q2 = RefColumn(x + math.cos(a) * 100f, z + math.sin(a) * 100f);
                    wet = !q.land || !q2.land;
                    mx = math.max(mx, q.topM); mn = math.min(mn, q.topM);
                }
                if (wet) continue;
                float sc = (mx - mn) + math.sqrt(dx * dx + dz * dz) * 0.0005f;
                if (sc < bestS) { bestS = sc; bp = new float3(x, cc.topM, z); }
            }
            if (bestS < float.MaxValue) scenes.Add(("plain", bp, cross[1], 15f, $"relief {bestS:F1} m over +-40 m"));
            else L("plain: no dry flat land within 1.2 km -- skipped");
        }

        var json = new StringBuilder();
        json.Append("{\"screen\":[").Append(Screen.width).Append(',').Append(Screen.height).Append("],\"vfov\":")
            .Append(F(cam.fieldOfView)).Append(",\"sunYaw\":").Append(F(sunYaw)).Append(",\"shells\":[");
        if (sh != null)
            for (int k = 0; k < sh.Layout.ShellCount; k++)
                json.Append(k > 0 ? "," : "").Append($"{{\"voxel\":{F(sh.Layout[k].VoxelM)},\"inner\":{F(sh.Layout[k].InnerM)},\"outer\":{F(sh.Layout[k].OuterM)}}}");
        json.Append("],\"poses\":[");
        bool firstPose = true;

        foreach (var (sn, p, h, theta0, note) in scenes)
        {
            if (scenesOn != null && !scenesOn.Contains(sn)) continue;
            // the smallest depression angle from theta0 up that sees P from every D
            float theta = theta0;
            Vector3 EyeAt(float D, float th) { float a = math.radians(th); return new Vector3(p.x - h.x * D * math.cos(a), p.y + D * math.sin(a), p.z - h.y * D * math.cos(a)); }
            for (; theta <= 45f; theta += 3f)
            {
                bool ok = true;
                foreach (float D in dists)
                {
                    Vector3 e = EyeAt(D, theta);
                    if (e.y < RefColumn(e.x, e.z).topM + 3f || !RefLineClear(e, p)) { ok = false; break; }
                }
                if (ok) break;
            }
            float yawH = math.degrees(math.atan2(h.x, h.y));
            L("");
            L($"=== scene {sn}: P ({p.x:F1}, {p.y:F1}, {p.z:F1}) m, heading yaw {yawH:F0} deg ({Mathf.DeltaAngle(sunYaw, yawH):+0;-0} deg from the sun), " +
              $"depression {theta:F0} deg{(theta > 45f ? " (NOT CLEAR from every D)" : "")}; {note}");
            foreach (float D in dists)
            {
                Vector3 eye = EyeAt(D, theta);
                Quaternion rot = Quaternion.LookRotation(((Vector3)p - eye).normalized, Vector3.up);
                string tag = $"{sn}_{D:F0}";
                L($"== {tag}: eye {eye:F1}, {(RefLineClear(eye, p) ? "line of sight clear" : "LINE OF SIGHT BLOCKED")}");

                // GAME: as shipped
                Phase4Bootstrapper.RefTier0Centre = null;
                Phase4Bootstrapper.DebugStreamCentre = eye;
                cam.transform.position = eye; cam.transform.rotation = rot;
                yield return NearSettle(far, sm);
                if (sh != null)
                {
                    float t0 = Time.realtimeSinceStartup, cap = firstPose ? firstWait : wait;   // the first pose fills every shell
                    while (Time.realtimeSinceStartup - t0 < cap)
                    {
                        int missing = 0;
                        for (int k = sh.FirstShell; k <= sh.LastShell; k++) if (!sh.ShellFull(k)) missing += sh.Missing(k);
                        if (missing == 0 && sh.InFlight == 0 && sh.Pending == 0) break;
                        yield return null;
                    }
                    L($"  [shells] waited {Time.realtimeSinceStartup - t0:F0} s (cap {cap:F0} s): {ShellCountsLine(sh)}");
                }
                string gameBox = BoxJson();
                foreach (string arm in arms) yield return LodRefArm(cam, eye, rot, arm, $"{tag}_game_{arm}", settle);

                // REF: the 0.1 m window round P
                Phase4Bootstrapper.RefTier0Centre = (Vector3)p + Vector3.up * 1.6f;
                float tr = Time.realtimeSinceStartup;
                yield return Frames(30);
                while (sm != null && sm.LoadDeficit() > 0 && Time.realtimeSinceStartup - tr < refWait) yield return null;
                yield return Frames(30);
                L($"  [ref] 0.1 m window round P after {Time.realtimeSinceStartup - tr:F0} s (cap {refWait:F0} s), load deficit " +
                  $"{(sm != null ? sm.LoadDeficit() : -1)}, {(Phase4Bootstrapper.Store != null ? Phase4Bootstrapper.Store.ResidentCount : 0)} resident chunks; box {BoxJson()}");
                string refBox = BoxJson();
                float maxRay0 = RaymarchFeature.MaxRayDistance;
                RaymarchFeature.MaxRayDistance = refMaxRay;
                foreach (string arm in arms) yield return LodRefArm(cam, eye, rot, arm, $"{tag}_ref_{arm}", settle);
                RaymarchFeature.MaxRayDistance = maxRay0;
                Phase4Bootstrapper.RefTier0Centre = null;

                Transform tf = cam.transform;
                json.Append(firstPose ? "" : ",").Append('{')
                    .Append($"\"tag\":\"{tag}\",\"scene\":\"{sn}\",\"D\":{F(D)},\"theta\":{F(theta)},")
                    .Append($"\"eye\":[{F(eye.x)},{F(eye.y)},{F(eye.z)}],\"P\":[{F(p.x)},{F(p.y)},{F(p.z)}],")
                    .Append($"\"fwd\":[{F(tf.forward.x)},{F(tf.forward.y)},{F(tf.forward.z)}],")
                    .Append($"\"right\":[{F(tf.right.x)},{F(tf.right.y)},{F(tf.right.z)}],")
                    .Append($"\"up\":[{F(tf.up.x)},{F(tf.up.y)},{F(tf.up.z)}],")
                    .Append($"\"refBox\":{refBox},\"gameBox\":{gameBox},\"shellLine\":\"{(sh != null ? ShellCountsLine(sh) : "")}\"}}");
                firstPose = false;
                File.WriteAllText(Path.Combine(_folder, "lodref_poses.json"), json.ToString() + "]}");
                File.WriteAllText(Path.Combine(_folder, "lookreview.txt"), _log.ToString());
            }
        }
        Phase4Bootstrapper.RefTier0Centre = null;
        Phase4Bootstrapper.DebugStreamCentre = null;
        RaymarchFeature.UseDebugViewOverride = false;
        File.WriteAllText(Path.Combine(_folder, "lodref_poses.json"), json.ToString() + "]}");
        L("LOD REF done");
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// The tier-0 window the shader marches, in voxels: the GPU mirror's XZ box and the loaded layers.
    private static string BoxJson()
    {
        var clip = Phase4Bootstrapper.Clipmap;
        if (clip == null) return "null";
        int3 lo = clip.WindowOriginBricks * 8, dims = clip.WindowDimsChunks * 128;
        return $"{{\"lo\":[{lo.x},{RaymarchFeature.ContentFloorVoxelY},{lo.z}],\"hi\":[{lo.x + dims.x},{RaymarchFeature.ContentCeilingVoxelY},{lo.z + dims.z}]}}";
    }

    private IEnumerator LodRefArm(Camera cam, Vector3 eye, Quaternion rot, string arm, string name, int settle)
    {
        RaymarchFeature.UseDebugViewOverride = false;
        switch (arm)
        {
            case "fsr540": ApplyCompareArm("fsr3_540"); break;
            case "fsr720": ApplyCompareArm("fsr3_720"); break;
            case "taa": ApplyCompareArm("native"); RaymarchFeature.Taa = true; break;
            case "hit":
                ApplyCompareArm("native");
                RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.HitInfo;
                RaymarchFeature.UseDebugViewOverride = true;
                break;
            default: ApplyCompareArm("native"); break;
        }
        cam.ResetProjectionMatrix();
        cam.transform.position = eye; cam.transform.rotation = rot;
        yield return Frames(arm == "hit" ? 5 : settle);
        yield return CaptureTo(name);
        RaymarchFeature.UseDebugViewOverride = false;
        RaymarchFeature.Taa = false;
    }
}
