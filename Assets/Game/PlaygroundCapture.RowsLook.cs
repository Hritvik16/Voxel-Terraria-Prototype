// Assets/Game/PlaygroundCapture.RowsLook.cs
//
// -lookreview -lookrowsab N (run-look-review.sh EXTRA=): TASK 156, PERF_30FPS_PLAN step 3 /
// owner Q1. Stills of FSR 3 at 540 internal rows vs N (e.g. 432), so the owner sees the
// softer picture before deciding. CAPTURE-ONLY: N is pinned through
// UpscalerSelect.CaptureMinRows, which play cannot reach. A LOOK, not a measurement.
// LookReview's own poses (walk_near / walk_open / high, -lookposes) shoot <pose>_fsr3_540 and
// <pose>_fsr3_N in the same frame state (clock paused, fluid suspended, 60 frames settle per
// arm); this file adds, after them:
//   cliff   of -farpreview's cliff feet (FindCliffFeet), the nearest to the start pose whose
//           look direction has the sun behind (heading . -sun >= 0.3); eye 1.7 m over the
//           store's ground, looking 70% up the cliff. Noon.
//   torch   task 151's torch scene (CostTorchScene: 32 torches on 1.5 m posts round the start
//           pose, the launch's lighting knobs), night (-nighttod, default 0.5 h), its first
//           frame's pose (12 deg down). Last, because it writes torch voxels.
// Sheets: python3 -I tools/rows156.py <LookReview dir> Screenshots/156
// TASK 162: -lookladder native,720,648,540,432 shoots any list of arms the same way (LadderArms);
// sheets: python3 -I tools/ladder162.py <LookReview dir> Screenshots/162
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator LookRowsExtra(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, Vector3 centre, float2 shz,
                                      string armA, string[] arms, float wait, HashSet<string> onlyPoses)
    {
        var store = Phase4Bootstrapper.Store;
        float noon = SunCycle.NoonHours;
        SunCycle.SetHours(noon);

        if ((onlyPoses == null || onlyPoses.Contains("cliff")) && far != null && far.Tg2Map != null)
        {
            int2 off = far.Tg2OffsetVoxels; int seaShift = far.Tg2SeaShift;
            Vector3 E(float3 g) => new Vector3((g.x - off.x) * 0.1f, (g.y - seaShift) * 0.1f, (g.z - off.y) * 0.1f);
            Vector3 bestEye = default, bestAt = default; float bestD = float.MaxValue; int cands = 0;
            foreach (var (e, a) in FindCliffFeet(far.Tg2Map, 2000))
            {
                Vector3 eye = E(e), at = E(a);
                float2 h = math.normalizesafe(new float2(at.x - eye.x, at.z - eye.z));
                if (math.dot(h, -shz) < 0.3f) continue;
                cands++;
                float d = math.distance(new float2(eye.x, eye.z), new float2(centre.x, centre.z));
                if (d < bestD) { bestD = d; bestEye = eye; bestAt = at; }
            }
            if (bestD < float.MaxValue)
            {
                Vector3 eye = bestEye;
                Phase4Bootstrapper.DebugStreamCentre = eye;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((bestAt - eye).normalized, Vector3.up));
                ApplyCompareArm(armA);
                yield return NearSettle(far, sm);
                float g = StoreGroundM(store, eye.x, eye.z, eye.y + 10f);
                if (!float.IsNaN(g)) eye.y = g + 1.7f;
                var rot = Quaternion.LookRotation((bestAt - eye).normalized, Vector3.up);
                L("");
                L($"== cliff: eye {eye:F1} (store ground {(float.IsNaN(g) ? "NOT FOUND" : g.ToString("F1"))}), looking at {bestAt:F1}; " +
                  $"nearest of {cands} sun-behind cliff feet, {bestD:F0} m from the start pose");
                yield return LookRowsShoot("cliff", cam, sh, eye, rot, arms, wait);
            }
            else L("cliff: no cliff foot with the sun behind -- skipped");
        }

        if (onlyPoses == null || onlyPoses.Contains("torch"))
        {
            float night = ArgFloat("-nighttod", 0.5f);
            SunCycle.SetHours(night);
            var paths = new List<ClipPath>();
            L("");
            yield return CostTorchScene(centre, night, far, sm, cam, paths);
            if (paths.Count > 0)
            {
                L($"== torch: {paths[0].note}; sun elevation {SunCycle.ElevationDegreesAt(night):F0} deg");
                yield return LookRowsShoot("torch", cam, sh, paths[0].pos[0], paths[0].rot[0], arms, wait);
            }
            SunCycle.SetHours(noon);
        }
        Phase4Bootstrapper.DebugStreamCentre = null;
    }

    /// Task 162: "native,720,540" -> native, fsr3_720, fsr3_540 (ApplyCompareArm names).
    /// Task 166: "432_s0" -> fsr3_432_s0 (a variant, Fsr3ArmVariant); "none432" -> none_432 (the raw
    /// internal image, bilinear to the output).
    private static string[] LadderArms(string list)
    {
        var parts = list.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string t = parts[i].Trim();
            parts[i] = t == "native" ? "native" : t.StartsWith("none") ? $"none_{int.Parse(t.Substring(4))}" : $"fsr3_{t}";
        }
        return parts;
    }

    /// Task 166 (look, capture-only): FSR 3 arm variants, "_"-separated tokens after the rows.
    /// "" = play's settings (RCAS on, 0.8; no debug view). s0 = RCAS off; sNN = RCAS on, sharpness NN/10;
    /// dbg = FSR 3's debug view grid; jf = ray jitter sign flipped; j0 = no jitter;
    /// scNN = cut FSR 3's auto shading change by NN/10 (sc10 = ignore it); rm = our reactive mask (task 174; rm1 / rm2 = only its light-change / new-voxel test).
    private static void Fsr3ArmVariant(string v)
    {
        RaymarchFeature.Fsr3Sharpen = true;
        RaymarchFeature.Fsr3Sharpness = 0.8f;
        RaymarchFeature.Fsr3DebugView = false;
        RaymarchFeature.Fsr3RayJitterSign = 1f;
        RaymarchFeature.Fsr3ZeroJitter = false;
        RaymarchFeature.Fsr3ShadingChangeCut = 0f;
        RaymarchFeature.Fsr3Reactive = false;
        RaymarchFeature.Fsr3ReactiveParts = 3;
        if (string.IsNullOrEmpty(v)) return;
        foreach (string t in v.Split('_'))
        {
            if (t == "dbg") RaymarchFeature.Fsr3DebugView = true;
            else if (t == "jf") RaymarchFeature.Fsr3RayJitterSign = -1f;
            else if (t == "j0") RaymarchFeature.Fsr3ZeroJitter = true;
            else if (t == "rm") { RaymarchFeature.Fsr3Reactive = true; RaymarchFeature.Fsr3ReactiveParts = 3; }
            else if (t == "rm1" || t == "rm2") { RaymarchFeature.Fsr3Reactive = true; RaymarchFeature.Fsr3ReactiveParts = t[2] - '0'; }
            else if (t.Length > 2 && t.StartsWith("sc") && int.TryParse(t.Substring(2), out int sc)) RaymarchFeature.Fsr3ShadingChangeCut = Mathf.Clamp01(sc / 10f);
            else if (t == "s0") RaymarchFeature.Fsr3Sharpen = false;
            else if (t.Length > 1 && t[0] == 's' && int.TryParse(t.Substring(1), out int sn)) RaymarchFeature.Fsr3Sharpness = Mathf.Clamp01(sn / 10f);
            else Debug.LogError($"[LookReview] unknown FSR 3 arm variant '{t}' in '{v}'");
        }
    }

    /// Shell wait (capped, missing counts logged), then each arm after 60 frames, same frame state.
    private IEnumerator LookRowsShoot(string n, Camera cam, ShellStreamer sh, Vector3 eye, Quaternion rot, string[] arms, float wait)
    {
        Phase4Bootstrapper.DebugStreamCentre = eye;
        cam.transform.SetPositionAndRotation(eye, rot);
        if (sh != null)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < wait)
            {
                int missing = 0;
                for (int k = sh.FirstShell; k <= sh.LastShell; k++) if (!sh.ShellFull(k)) missing += sh.Missing(k);
                if (missing == 0 && sh.InFlight == 0 && sh.Pending == 0) break;
                yield return null;
            }
            L($"  [shells] waited {Time.realtimeSinceStartup - t0:F0} s (cap {wait:F0} s): {ShellCountsLine(sh)}");
        }
        foreach (string arm in arms)
        {
            ApplyCompareArm(arm);
            cam.ResetProjectionMatrix();
            cam.transform.SetPositionAndRotation(eye, rot);
            yield return Frames(60);
            yield return CaptureTo($"{n}_{arm}");
            var d = RaymarchFeature.LastDispatchResolution;
            L($"  {n,-10} {arm,-9} dispatch {d.x}x{d.y}");
        }
    }
}
