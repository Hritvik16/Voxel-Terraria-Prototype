// Assets/Game/PlaygroundCapture.LookCross.cs
//
// -lookreview -lookcross 1: TASK 127, comparison stills that show SHAPE (owner,
// 2026-10-23, Q75: "they both look flat"; Q78: "Can i get a birds eye view?").
// Capture-only: no product behaviour, no default changes. Replaces the look
// review's poses and arms with its own (the -lookposes filter still applies).
//   LIGHT   one fixed time for every shot: the afternoon hour with the sun
//           -lookcrosssun D deg up (default 40), and every heading -lookcrossoff A
//           deg (default 75) left or right of the sun's azimuth: CROSS-LIGHT, so
//           slopes facing the sun and slopes facing away differ.
//   POSES   (generator-based, so the same in every launch of the same world)
//           x_high:  the look review's high column (highest within 800 m, +20 m),
//                    the cross heading with the most land 2.5-4.5 km out, -4 deg.
//           x_ridge: walk height (circles of 150 m and 500 m), the cross heading
//                    whose ground rises nearest 1.5 km ahead (0.6-3 km), +1 deg.
//           x_coast: walk height (circle + centre), free >= 3 km, the cross heading
//                    with the most SEA 2.5-4.5 km out (land nearer preferred), 0 deg.
//           x_bird800:  800 m over the centre's ground, pitched so a flat plane
//                    3.3 km out is the frame's centre row (-13.6 deg), the cross
//                    heading whose 3.3 km arc (+-45 deg) mixes land and sea most.
//           x_bird2300: 2300 m over it, -45 deg (3.3 km slant = the centre row).
//           xb_<k> (with -lookbiome 1): the task 122 border points, camera
//                    -lookcrossbh H m (default 700) over the ground, -60 deg, the
//                    cross heading nearest the border's tangent, border point at
//                    the centre row.
//   ARM     fsr3 at the launch's rows (-lookrows), 60 frames settle; the shells get
//           -lookwait s (xb_ poses: at most -lookcrossbwait, default 90) and what is
//           still missing is LOGGED. Before the first pose the shells fill round the
//           centre at walk height for up to -lookcrossprefill s (default 240; 0 = off). The log gives, per
//           pose, the rows where a flat plane at ground level is 1.0 / 2.2 / 3.3 /
//           4.4 km away (for the crops: tools/cross127-sheet.py).
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
    private static bool SeaAt(float xM, float zM)
    {
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        if (src == null) return false;
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, xM * 10f, zM * 10f, 1);
        return col.height < spec.seaLevelY || col.water > col.height;
    }

    /// Share of sea along `h` between a and b metres (100 m steps).
    private static float SeaShare(Vector3 eye, float2 h, float a, float b)
    {
        int n = 0, s = 0;
        for (float t = a; t <= b; t += 100f, n++) if (SeaAt(eye.x + h.x * t, eye.z + h.y * t)) s++;
        return n > 0 ? (float)s / n : 0f;
    }

    private IEnumerator LookCross(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, string fsr, float wait,
                                  HashSet<string> onlyPoses, bool biomeLook)
    {
        float elev = ArgFloat("-lookcrosssun", 40f), off = ArgFloat("-lookcrossoff", 75f), bh = ArgFloat("-lookcrossbh", 700f);
        float bwait = ArgFloat("-lookcrossbwait", 90f), prefill = ArgFloat("-lookcrossprefill", 240f);
        float hours = SunCycle.HoursForElevation(elev, false);
        SunCycle.SetHours(hours);
        float3 s3 = SunCycle.DirectionAt(hours);
        float2 toSun = math.normalizesafe(new float2(s3.x, s3.z));
        float sunYaw = math.degrees(math.atan2(toSun.x, toSun.y));
        float2 Rot(float2 v, float deg) { float a = math.radians(deg), c = math.cos(a), s = math.sin(a); return new float2(v.x * c + v.y * s, -v.x * s + v.y * c); }
        float Yaw(float2 h) => math.degrees(math.atan2(h.x, h.y));
        Quaternion Look(float2 h, float pitchDeg) => Quaternion.Euler(-pitchDeg, Yaw(h), 0f);
        var cross = new List<float2>();
        foreach (float sg in new[] { -1f, 1f }) foreach (float d in new[] { off - 15f, off, off + 15f })
            cross.Add(Rot(toSun, sg * math.clamp(d, 0f, 180f)));
        L($"LOOK CROSS (task 127): {hours:F2} h, sun elevation {SunCycle.ElevationDegreesAt(hours):F1} deg, sun azimuth (yaw) {sunYaw:F0} deg; " +
          $"headings {off - 15f:F0}/{off:F0}/{off + 15f:F0} deg either side of it; arm {fsr}; shell wait cap {wait:F0} s");

        Vector3 centre = cam.transform.position;
        const float radius = 150f;
        Vector3 CircleEye(float a, float r) { Vector3 e = centre + new Vector3(math.cos(a), 0f, math.sin(a)) * r; e.y = WalkEyeY(e.x, e.z, e.y); return e; }
        var poses = new List<(string n, Vector3 eye, Quaternion rot, string note)>();

        // x_high
        Vector3 hi = centre; hi.y = -1e9f;
        for (float dz = -800f; dz <= 800f; dz += 40f)
        for (float dx = -800f; dx <= 800f; dx += 40f)
        {
            if (dx * dx + dz * dz > 800f * 800f) continue;
            float y = WalkEyeY(centre.x + dx, centre.z + dz, -1e9f);
            if (y > hi.y) hi = new Vector3(centre.x + dx, y, centre.z + dz);
        }
        hi.y += 20f;
        {
            float2 best = cross[0]; float bs = -1f;
            foreach (var h in cross)
            {
                float fd = CostFreeDistanceM(hi, h, 3000f), land = 1f - SeaShare(hi, h, 2500f, 4500f);
                float sc = (fd >= 3000f ? 1f : fd / 3000f) + land;
                if (sc > bs) { bs = sc; best = h; }
            }
            poses.Add(("x_high", hi, Look(best, -4f), $"highest column within 800 m + 20 m; land 2.5-4.5 km {1f - SeaShare(hi, best, 2500f, 4500f):P0}, " +
                       $"free {CostFreeDistanceM(hi, best, 3000f):F0} m"));
        }
        // x_ridge and x_coast
        {
            float bestR = float.MaxValue, bestC = -1f; Vector3 rE = centre, cE = centre; float2 rH = cross[0], cH = cross[0]; float rD = 0f;
            for (int k = -1; k < 32; k++)
            {
                Vector3 e = k < 0 ? new Vector3(centre.x, WalkEyeY(centre.x, centre.z, centre.y), centre.z) : CircleEye(k * math.PI / 8f, k < 16 ? radius : 500f);
                foreach (var h in cross)
                {
                    float fd = CostFreeDistanceM(e, h, 4000f);
                    if (k >= 0 && fd >= 600f && fd <= 3000f && math.abs(fd - 1500f) < bestR) { bestR = math.abs(fd - 1500f); rE = e; rH = h; rD = fd; }
                    if (k >= 16 || fd < 3000f) continue;
                    float sc = SeaShare(e, h, 2500f, 4500f) + 0.25f * (1f - SeaShare(e, h, 300f, 1500f));
                    if (sc > bestC) { bestC = sc; cE = e; cH = h; }
                }
            }
            if (bestR < float.MaxValue) poses.Add(("x_ridge", rE, Look(rH, 1f), $"ground rises {rD:F0} m ahead"));
            else L("x_ridge: no cross-lit view with ground rising 0.6-3 km ahead -- skipped");
            if (bestC >= 0f) poses.Add(("x_coast", cE, Look(cH, 0f), $"sea 2.5-4.5 km {SeaShare(cE, cH, 2500f, 4500f):P0}, land 0.3-1.5 km {1f - SeaShare(cE, cH, 300f, 1500f):P0}"));
            else L("x_coast: no cross-lit view free for 3 km -- skipped");
        }
        // x_bird800 / x_bird2300: the 3.3 km ring
        {
            float g = WalkEyeY(centre.x, centre.z, centre.y) - 1.6f;
            Vector3 c0 = new Vector3(centre.x, g, centre.z);
            float2 best = cross[0]; float bs = -1f;
            foreach (var h in cross)
            {
                int n = 0, sea = 0;
                for (float a = -45f; a <= 45f; a += 5f, n++)
                {
                    float2 d = Rot(h, a);
                    if (SeaAt(c0.x + d.x * 3300f, c0.z + d.y * 3300f)) sea++;
                }
                float sc = math.min(sea, n - sea) / (float)n;
                if (sc > bs) { bs = sc; best = h; }
            }
            poses.Add(("x_bird800", c0 + Vector3.up * 800f, Look(best, -math.degrees(math.atan2(800f, 3300f))),
                       $"800 m over the centre's ground, the 3.3 km ring at the centre row; arc +-45 deg sea/land mix {bs:P0}"));
            poses.Add(("x_bird2300", c0 + Vector3.up * 2300f, Look(best, -45f), "2300 m over the centre's ground, -45 deg"));
        }
        // xb_<k>: the task 122 borders from above
        if (biomeLook)
        {
            var bp = new List<(string n, Vector3 eye, Quaternion rot, string note)>();
            LookBorderPoses(centre, toSun, bp, (h, p) => Look(h, p));
            for (int k = 0; k < bp.Count; k++)
            {
                Vector3 f3 = bp[k].rot * Vector3.forward; float2 tan = math.normalizesafe(new float2(f3.x, f3.z));
                float2 at = new float2(bp[k].eye.x, bp[k].eye.z) + tan * 150f;
                float2 best = cross[0]; float bd = -2f;
                foreach (var h in cross) { float d = math.abs(math.dot(h, tan)); if (d > bd) { bd = d; best = h; } }
                float g = WalkEyeY(at.x, at.y, centre.y) - 1.6f;
                float back = bh / math.tan(math.radians(60f));
                float2 e2 = at - best * back;
                poses.Add(($"xb_{k}", new Vector3(e2.x, g + bh, e2.y), Look(best, -60f),
                           $"{bp[k].note.Split(new[] { " at " }, StringSplitOptions.None)[0]} at ({at.x:F0}, {at.y:F0}) m (centre of frame), {bh:F0} m over it, -60 deg"));
            }
        }

        // Prefill: the shells past ~1 km are shared by every walk-height pose (all within ~500 m of the
        // centre) and fill last; without this, the first poses drew 2-4.4 km with the far band (s6/s7 empty).
        if (sh != null && prefill > 0f)
        {
            Vector3 pe = new Vector3(centre.x, WalkEyeY(centre.x, centre.z, centre.y), centre.z);
            Phase4Bootstrapper.DebugStreamCentre = pe;
            cam.transform.position = pe;
            yield return NearSettle(far, sm);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < prefill)
            {
                int missing = 0;
                for (int k = sh.FirstShell; k <= sh.LastShell; k++) if (!sh.ShellFull(k)) missing += sh.Missing(k);
                if (missing == 0 && sh.InFlight == 0 && sh.Pending == 0) break;
                yield return null;
            }
            L($"  [prefill] centre at walk height, waited {Time.realtimeSinceStartup - t0:F0} s (cap {prefill:F0} s): {ShellCountsLine(sh)}");
        }
        foreach (var (n, eye, rot, note) in poses)
        {
            if (onlyPoses != null && !onlyPoses.Contains(n)) continue;
            float pitch = -Mathf.DeltaAngle(0f, rot.eulerAngles.x), yaw = rot.eulerAngles.y;
            float rel = Mathf.DeltaAngle(sunYaw, yaw);
            L("");
            L($"== {n}: eye {eye:F1}, yaw {yaw:F0} pitch {pitch:F1} deg ({rel:+0;-0} deg from the sun's azimuth, sun {(rel > 0 ? "on the left" : "on the right")}); {note}");
            float gnd = WalkEyeY(eye.x, eye.z, eye.y) - 1.6f, hgt = math.max(eye.y - gnd, 1f);
            var rowsTxt = new List<string>();
            foreach (float dm in new[] { 1000f, 2200f, 3300f, 4400f })
            {
                // Row (0 = top, output rows) where a flat plane at this ground level is dm metres out (horizontal), centre column.
                float ang = math.degrees(math.atan2(hgt, dm)) + pitch;   // degrees below the view axis
                float row = (float)(Screen.height * 0.5 + Math.Tan(ang * Math.PI / 180.0) / Math.Tan(cam.fieldOfView * 0.5 * Math.PI / 180.0) * Screen.height * 0.5);
                rowsTxt.Add($"{dm / 1000f:F1} km row {row:F0}");
            }
            L($"  flat plane {hgt:F0} m below the eye: {string.Join(", ", rowsTxt)} (of {Screen.height})");
            Phase4Bootstrapper.DebugStreamCentre = eye;
            cam.transform.position = eye; cam.transform.rotation = rot;
            ApplyCompareArm(fsr);
            cam.ResetProjectionMatrix();
            yield return NearSettle(far, sm);
            if (sh != null)
            {
                float t0 = Time.realtimeSinceStartup, cap = n.StartsWith("xb_") ? Math.Min(wait, bwait) : wait;
                while (Time.realtimeSinceStartup - t0 < cap)
                {
                    int missing = 0;
                    for (int k = sh.FirstShell; k <= sh.LastShell; k++) if (!sh.ShellFull(k)) missing += sh.Missing(k);
                    if (missing == 0 && sh.InFlight == 0 && sh.Pending == 0) break;
                    yield return null;
                }
                L($"  [shells] waited {Time.realtimeSinceStartup - t0:F0} s (cap {cap:F0} s): {ShellCountsLine(sh)}");
            }
            cam.transform.position = eye; cam.transform.rotation = rot;
            yield return Frames(60);
            yield return CaptureTo($"{n}_{fsr}");
            Phase4Bootstrapper.DebugStreamCentre = null;
            File.WriteAllText(Path.Combine(_folder, "lookreview.txt"), _log.ToString());
        }
        L("LOOK CROSS done");
    }
}
