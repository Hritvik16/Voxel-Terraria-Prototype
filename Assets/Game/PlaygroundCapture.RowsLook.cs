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
                                      string armA, string armB, float wait, HashSet<string> onlyPoses)
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
                yield return LookRowsShoot("cliff", cam, sh, eye, rot, armA, armB, wait);
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
                yield return LookRowsShoot("torch", cam, sh, paths[0].pos[0], paths[0].rot[0], armA, armB, wait);
            }
            SunCycle.SetHours(noon);
        }
        Phase4Bootstrapper.DebugStreamCentre = null;
    }

    /// Shell wait (capped, missing counts logged), then each arm after 60 frames, same frame state.
    private IEnumerator LookRowsShoot(string n, Camera cam, ShellStreamer sh, Vector3 eye, Quaternion rot, string armA, string armB, float wait)
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
        foreach (string arm in new[] { armA, armB })
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
