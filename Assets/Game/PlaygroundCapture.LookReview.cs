// Assets/Game/PlaygroundCapture.LookReview.cs
//
// -upscalerclips -lookreview (with -world tg2; run-look-review.sh): TASK 114,
// the visual look review (owner, 2026-10-22: "the game visually just does not
// look good"). A LOOK, not a measurement: no frame time is claimed. One launch
// per -quality preset (the shells are sized at start-up only), task 51's
// harness (fullscreen 1920x1080, clock paused, fluid suspended, HUD + minimap
// hidden), noon unless stated.
//   POSES   walk_near / walk_open: the start frames of task 57's walk arcs
//           (CostSplit: MODE=walk's 150 m circle round the start pose, eye
//           1.6 m over the generated ground, level look along the tangent).
//           ridge: of the 16 circle points x 16 headings with the sun behind
//           (heading . -sun >= 0), the level view whose ground rises nearest
//           250 m ahead (100-500 m), pitched up 2 deg.
//           high: the highest column within 800 m of the centre (40 m grid),
//           eye 20 m over it, the most open heading with the sun behind or
//           beside, pitched down 4 deg (shells AND the far band in frame).
//           Streaming is pinned at each pose (DebugStreamCentre); the shells get
//           -lookwait s (default 45) and what is still missing is LOGGED.
//   ARMS    per pose: fsr3_<rows> (play's upscaler at the preset's rows,
//           UpscalerSelect-clamped: Low/Medium 540, High 720), none_<rows> (same
//           rows, plain blit: render aliasing without the upscaler), native
//           (1920x1080 march, no AA: the ground truth), and fsr3 + ShellTint.
//           60 frames settle per arm. The fsr3 arm logs per-shell voxel px at
//           the dispatch rows (ShadowStats 8-11: hits / >1 px / max px).
//   MOTION  walk_open and ridge, fsr3 and none: 4 CONSECUTIVE frames each of
//           still (camera fixed), walk (0.05 m/frame forward = 1.5 m/s at
//           30 fps) and turn (0.2 deg/frame), after 60 lead frames of the same
//           motion. tools/look-review-diff.py makes the difference images.
//   GOLDEN  High only: walk_open at 17 h, fsr3 + none, its own heading and
//           turned away from the 17 h sun.
//   TASK 117 (capture-only): -lookrows N pins the fsr3/none arms at N rows
//           (UpscalerSelect-clamped) instead of the preset's (High vs Medium at
//           540 both); -lookposes a,b,.. only these poses; -lookbursts 0 skips
//           the motion bursts and golden hour.
// Output: <persistentDataPath>/LookReview/<ts>_<preset>/ (PNGs + lookreview.txt).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator LookReview(FarField far, Camera cam, StreamManager sm, ChunkStore store)
    {
        var sh = ShellStreamer.Active;
        var layout = sh != null ? sh.Layout : null;
        string preset = layout != null ? layout.Preset.ToString().ToLowerInvariant() : "noshells";
        string root = Path.Combine(Application.persistentDataPath, "LookReview", DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_" + preset);
        Directory.CreateDirectory(root);
        _folder = root;
        Minimap.Visible = false;
        float wait = ArgFloat("-lookwait", 45f);
        int rows = layout != null && layout.SizingRows >= UpscalerSelect.MaxRows ? UpscalerSelect.MaxRows : UpscalerSelect.MinRows;
        int rowsArg = ArgInt("-lookrows", 0);
        if (rowsArg > 0) rows = Math.Max(UpscalerSelect.MinRows, Math.Min(UpscalerSelect.MaxRows, rowsArg));
        string posesArg = ArgValue("-lookposes");
        var onlyPoses = string.IsNullOrEmpty(posesArg) ? null : new HashSet<string>(posesArg.Split(','));
        bool bursts = ArgInt("-lookbursts", 1) != 0;
        string fsr = $"fsr3_{rows}", none = $"none_{rows}";
        float noon = SunCycle.NoonHours;
        SunCycle.SetHours(noon);
        float3 sun = SunCycle.DirectionAt(noon);
        L("");
        L($"LOOK REVIEW (task 114): preset {preset}, sizing rows {(layout != null ? layout.SizingRows : 0)}, render rows {rows} (UpscalerSelect clamps " +
          $"{UpscalerSelect.MinRows}-{UpscalerSelect.MaxRows}), camera vFOV {cam.fieldOfView:F1} deg, noon {noon:F2} h (sun elevation " +
          $"{SunCycle.ElevationDegreesAt(noon):F0} deg), shell wait cap {wait:F0} s");
        if (layout != null)
        {
            // Rule A: voxel px at each shell's inner and outer edge, at the render rows and at the output's 1080.
            double c = rows / (cam.fieldOfView * Math.PI / 180.0), c1080 = 1080.0 / (cam.fieldOfView * Math.PI / 180.0);
            for (int k = 0; k < layout.ShellCount; k++)
            {
                var s = layout[k];
                string inner = s.InnerM > 0f ? $"{s.VoxelM * c / s.InnerM:F2}" : "inf";
                L($"  shell {k}: voxel {s.VoxelM:F1} m, {s.InnerM,6:F0}-{s.OuterM,6:F0} m; voxel px at {rows} rows inner {inner} -> outer " +
                  $"{s.VoxelM * c / s.OuterM:F2} (at 1080 output: outer {s.VoxelM * c1080 / s.OuterM:F2})");
            }
        }

        // ---- poses (fixed before any arm) ----
        const float radius = 150f;
        Vector3 centre = cam.transform.position;
        float2 shz = math.normalizesafe(new float2(sun.x, sun.z));
        Vector3 CircleEye(float a)
        {
            Vector3 e = centre + new Vector3(math.cos(a), 0f, math.sin(a)) * radius;
            e.y = WalkEyeY(e.x, e.z, e.y);
            return e;
        }
        float2 Tangent(float a) => new float2(-math.sin(a), math.cos(a));
        Quaternion Look(float2 h, float pitchDeg) =>
            Quaternion.Euler(-pitchDeg, math.degrees(math.atan2(h.x, h.y)), 0f);
        var poses = new List<(string n, Vector3 eye, Quaternion rot, string note)>();
        float aNear = math.atan2(shz.x, -shz.y);
        poses.Add(("walk_near", CircleEye(aNear), Look(Tangent(aNear), 0f), $"circle angle {math.degrees(aNear):F0} deg, heading = -sun"));
        float aOpen = 0f, bestOpen = -1f;
        float bestRidge = float.MaxValue; Vector3 ridgeEye = centre; float2 ridgeHead = new float2(0, 1); float ridgeD = 0f;
        for (int k = 0; k < 16; k++)
        {
            float a = k * math.PI / 8f;
            Vector3 e = CircleEye(a);
            float2 head = Tangent(a);
            if (math.dot(head, -shz) >= -0.2f)
            {
                var d = new List<float>();
                for (int j = -2; j <= 2; j++)
                {
                    float yaw = math.atan2(head.x, head.y) + math.radians(15f * j);
                    d.Add(CostFreeDistanceM(e, new float2(math.sin(yaw), math.cos(yaw)), 1500f));
                }
                float open = Pct(d, 0.5f);
                if (open > bestOpen) { bestOpen = open; aOpen = a; }
            }
            for (int j = 0; j < 16; j++)
            {
                float yaw = j * math.PI / 8f;
                float2 h = new float2(math.sin(yaw), math.cos(yaw));
                if (math.dot(h, -shz) < 0f) continue;
                float fd = CostFreeDistanceM(e, h, 600f);
                if (fd < 100f || fd > 500f) continue;
                if (math.abs(fd - 250f) < bestRidge) { bestRidge = math.abs(fd - 250f); ridgeEye = e; ridgeHead = h; ridgeD = fd; }
            }
        }
        poses.Add(("walk_open", CircleEye(aOpen), Look(Tangent(aOpen), 0f), $"circle angle {math.degrees(aOpen):F0} deg, median free distance {bestOpen:F0} m"));
        if (bestRidge < float.MaxValue) poses.Add(("ridge", ridgeEye, Look(ridgeHead, 2f), $"ground rises {ridgeD:F0} m ahead"));
        else L("ridge: no level view with ground rising 100-500 m ahead and the sun behind -- skipped");
        {
            Vector3 hi = centre; hi.y = -1e9f;
            for (float dz = -800f; dz <= 800f; dz += 40f)
            for (float dx = -800f; dx <= 800f; dx += 40f)
            {
                if (dx * dx + dz * dz > 800f * 800f) continue;
                float y = WalkEyeY(centre.x + dx, centre.z + dz, -1e9f);
                if (y > hi.y) hi = new Vector3(centre.x + dx, y, centre.z + dz);
            }
            hi.y += 20f;
            float2 bestH = new float2(0, 1); float bestF = -1f;
            for (int j = 0; j < 16; j++)
            {
                float yaw = j * math.PI / 8f;
                float2 h = new float2(math.sin(yaw), math.cos(yaw));
                if (math.dot(h, -shz) < -0.2f) continue;
                float fd = CostFreeDistanceM(hi, h, 3000f);
                if (fd > bestF) { bestF = fd; bestH = h; }
            }
            poses.Add(("high", hi, Look(bestH, -4f), $"highest column within 800 m + 20 m, free distance {bestF:F0} m"));
        }

        foreach (var (n, eye, rot, note) in poses)
        {
            if (onlyPoses != null && !onlyPoses.Contains(n)) continue;
            L("");
            L($"== {n}: eye {eye:F1}, yaw {rot.eulerAngles.y:F0} pitch {-Mathf.DeltaAngle(0f, rot.eulerAngles.x):F0} deg; {note}");
            Phase4Bootstrapper.DebugStreamCentre = eye;
            cam.transform.position = eye; cam.transform.rotation = rot;
            ApplyCompareArm(fsr);
            yield return NearSettle(far, sm);
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
            foreach (string arm in new[] { fsr, none, "native" })
            {
                ApplyCompareArm(arm);
                cam.ResetProjectionMatrix();
                cam.transform.position = eye; cam.transform.rotation = rot;
                yield return Frames(60);
                if (arm == fsr && sh != null) yield return LookShellPx(sh, n);
                yield return CaptureTo($"{n}_{arm}");
                var d = RaymarchFeature.LastDispatchResolution;
                L($"  {n,-10} {arm,-9} dispatch {d.x}x{d.y}");
            }
            ApplyCompareArm(fsr);
            RaymarchFeature.ShellTint = true;
            yield return Frames(30);
            yield return CaptureTo($"{n}_{fsr}_tint");
            RaymarchFeature.ShellTint = false;
            if (bursts && (n == "walk_open" || n == "ridge"))
            {
                Vector3 fwd = rot * Vector3.forward; fwd.y = 0f; fwd = fwd.normalized;
                foreach (string arm in new[] { fsr, none })
                {
                    ApplyCompareArm(arm);
                    cam.ResetProjectionMatrix();
                    yield return LookBurst($"{n}_{arm}_still", cam, eye, rot, Vector3.zero, 0f);
                    yield return LookBurst($"{n}_{arm}_walk", cam, eye, rot, fwd * 0.05f, 0f);
                    yield return LookBurst($"{n}_{arm}_turn", cam, eye, rot, Vector3.zero, 0.2f);
                }
                L($"  {n} motion bursts: still / walk 0.05 m/frame / turn 0.2 deg/frame, 60 lead + 4 consecutive frames, {fsr} and {none}");
            }
            if (bursts && n == "walk_open" && layout != null && layout.Preset == QualityPreset.High)
            {
                const float golden = 17f;
                SunCycle.SetHours(golden);
                float3 gs = SunCycle.DirectionAt(golden);
                float2 away = -math.normalizesafe(new float2(gs.x, gs.z));
                L($"  golden: {golden:F1} h, sun elevation {SunCycle.ElevationDegreesAt(golden):F0} deg");
                foreach (var (tag, r) in new[] { ("own", rot), ("away", Look(away, 0f)) })
                foreach (string arm in new[] { fsr, none })
                {
                    ApplyCompareArm(arm);
                    cam.ResetProjectionMatrix();
                    cam.transform.position = eye; cam.transform.rotation = r;
                    yield return Frames(60);
                    yield return CaptureTo($"{n}_golden_{tag}_{arm}");
                }
                SunCycle.SetHours(noon);
            }
            Phase4Bootstrapper.DebugStreamCentre = null;
            File.WriteAllText(Path.Combine(root, "lookreview.txt"), _log.ToString());
        }
        ApplyCompareArm(fsr);
        L("");
        L($"LOOK REVIEW done: {root}");
        File.WriteAllText(Path.Combine(root, "lookreview.txt"), _log.ToString());
    }

    /// Per-shell voxel px at the dispatch rows for one frame (ShadowStats 8-11,
    /// the stats ShellPoseShots logs): hits / hits over 1 px / max px.
    private IEnumerator LookShellPx(ShellStreamer sh, string pose)
    {
        RaymarchFeature.CollectShadowStats = true;
        RaymarchFeature.ResetShadowStats();
        yield return Frames(1);
        uint[] s = RaymarchFeature.ReadShadowStats();
        RaymarchFeature.CollectShadowStats = false;
        var sb = new System.Text.StringBuilder($"  [px] {pose,-10} hits / >1 px / max px per shell (0 = tier 0 + cascade):");
        for (int k = 0; k < sh.Layout.ShellCount && 11 + 4 * k < s.Length; k++)
        {
            uint hits = s[8 + 4 * k];
            sb.Append(hits == 0 ? $"  s{k} -" : $"  s{k} {hits}/{s[9 + 4 * k]}/{s[10 + 4 * k] / 1000f:F2}");
        }
        L(sb.ToString());
    }

    /// 60 lead frames of the motion, then 4 consecutive frames captured (kept
    /// in memory, encoded after, so the capture does not hold a frame up).
    private IEnumerator LookBurst(string name, Camera cam, Vector3 p0, Quaternion r0, Vector3 dPos, float dYawDeg)
    {
        const int lead = 60, n = 4;
        var shots = new List<Texture2D>(n);
        for (int i = 0; i < lead + n; i++)
        {
            cam.transform.position = p0 + dPos * i;
            cam.transform.rotation = Quaternion.Euler(0f, dYawDeg * i, 0f) * r0;
            yield return new WaitForEndOfFrame();
            if (i >= lead) shots.Add(ScreenCapture.CaptureScreenshotAsTexture());
        }
        for (int i = 0; i < shots.Count; i++)
        {
            try { File.WriteAllBytes(Path.Combine(_folder, $"{name}_{i}.png"), shots[i].EncodeToPNG()); }
            finally { Destroy(shots[i]); }
        }
        cam.transform.position = p0; cam.transform.rotation = r0;
    }
}
