// Assets/Game/PlaygroundCapture.CliffShadow.cs
//
// -lookreview -cliffshadow 1: TASK 131, the Q83 picture. Tier-0 terrain (the 0.1 m
// window round the player) takes no sun shadow from terrain outside that window:
// its shadow ray counts as lit at the window edge / the band ceiling, and only
// shell hits march the shells (Raymarch.compute TraceShadowRay; the shell sun ray
// after it). This finds cliff feet where that should show and takes the shots.
// Capture-only: no product behaviour, no default changes, no timing.
//   SEARCH  a 10 m generator heightfield (RefColumn) of +-cliffr m (default 2500)
//           round the start; for each sun elevation in -cliffsun (default 30,25,20,
//           afternoon) a dry, gentle cell Q is a spot when its ground is shadowed by
//           terrain 160-350 m toward the sun that stands >= 60 m above Q, and the
//           ground from Q toward the sun is shadowed (by terrain >= 140 m from Q),
//           dry and seen from Q's 1.6 m eye for >= 150 m. Best two, >= 800 m apart.
//   SHOTS   per spot, at its sun:
//           A  eye 1.6 m over Q, looking toward the sun/cliff (play look fsr3_dyn,
//              native 1080, debug view 14 = who drew each pixel);
//           C  250 m above the ground, looking straight down (screen up = toward
//              the sun); ONLY the 0.1 m window stays on A's eye
//              (Phase4Bootstrapper.RefTier0Centre, as task 129's REF), shells and
//              far band follow the camera; play look + view 14;
//           B  300 m further back (away from the sun), eye raised until it sees
//              Q's ground, looking at the same ground; play look.
//   OUTPUT  cliff<N>_<A|B|C>_<arm>.png, cliffshadow.json (cameras, the window box,
//           C's screen pixels of Q and of the window corners), lookreview.txt.
//           tools/cliffshadow131.py builds the side-by-side sheets.
//   TASK 133: -cliffbefore 1 also shoots A (play, native) and C (play) with
//           RaymarchFeature.NearShadowShells forced OFF first (<name>0: play0, native0),
//           then the launch's setting: the before/after pair in one launch.
//   TASK 134: -cliffbefore 2 does the same with RaymarchFeature.ShellKernelDiet
//           forced OFF instead (the far-shading *Diag kernels, the code before 134).
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
    private IEnumerator CliffShadow(FarField far, Camera cam, StreamManager sm, ShellStreamer sh)
    {
        float R = ArgFloat("-cliffr", 2500f), step = 10f;
        float firstWait = ArgFloat("-cliffwait0", 120f), wait = ArgFloat("-cliffwait", 60f);
        int settle = ArgInt("-cliffframes", 90);
        string sunArg = ArgValue("-cliffsun");
        var elevs = new List<float>();
        foreach (var s in (string.IsNullOrEmpty(sunArg) ? "30,25,20" : sunArg).Split(','))
            elevs.Add(float.Parse(s, CultureInfo.InvariantCulture));

        // ---- the heightfield ----
        Vector3 c0 = cam.transform.position;
        int n = (int)(2f * R / step) + 1;
        float x0 = c0.x - R, z0 = c0.z - R;
        var H = new float[n * n]; var land = new bool[n * n];
        float tg = Time.realtimeSinceStartup;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                var c = RefColumn(x0 + i * step, z0 + j * step);
                H[j * n + i] = c.topM; land[j * n + i] = c.land;
            }
            if ((j & 31) == 31) yield return null;
        }
        float HAt(float x, float z)
        {
            int i = (int)math.round((x - x0) / step), j = (int)math.round((z - z0) / step);
            return (i < 0 || j < 0 || i >= n || j >= n) ? -1f : H[j * n + i];
        }
        bool LandAt(float x, float z)
        {
            int i = (int)math.round((x - x0) / step), j = (int)math.round((z - z0) / step);
            return i >= 0 && j >= 0 && i < n && j < n && land[j * n + i];
        }
        L("");
        L($"CLIFF SHADOW (task 131): heightfield {n}x{n} at {step:F0} m round ({c0.x:F0}, {c0.z:F0}) in {Time.realtimeSinceStartup - tg:F1} s; sun elevations {string.Join(",", elevs)} deg");

        // ---- spots ----
        var spots = new List<(float3 q, float2 toSun, float elev, float hours, float tb, float cliffH, float stretch, float score)>();
        foreach (float elev in elevs)
        {
            float hours = SunCycle.HoursForElevation(elev, false);
            float3 s3 = SunCycle.DirectionAt(hours);
            float2 ts = math.normalizesafe(new float2(s3.x, s3.z));
            float tanE = math.tan(math.radians(elev));
            // first blocker distance toward the sun (-1 = lit) and the tallest terrain on the ray, per cell
            var tb = new float[n * n]; var top = new float[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i; float g = H[k], x = x0 + i * step, z = z0 + j * step;
                tb[k] = -1f; top[k] = 0f;
                for (float t = step; t <= 600f; t += step)
                {
                    float h = HAt(x + ts.x * t, z + ts.y * t);
                    if (h < 0f) break;
                    top[k] = math.max(top[k], h - g);
                    if (tb[k] < 0f && h > g + t * tanE + 0.5f) tb[k] = t;
                }
            }
            // how often: dry land whose own ground is shadowed by terrain >= 110 m away (would be lit wrongly)
            int nLand = 0, nShadow = 0, nWrong = 0;
            for (int k = 0; k < n * n; k++)
            {
                if (!land[k]) continue;
                nLand++;
                if (tb[k] > 0f) { nShadow++; if (tb[k] >= 110f) nWrong++; }
            }
            L($"  sun {elev:F0} deg ({hours:F2} h, toward-sun yaw {math.degrees(math.atan2(ts.x, ts.y)):F0} deg): dry land cells {nLand}, " +
              $"shadowed {100f * nShadow / math.max(1, nLand):F1}%, shadowed by terrain >= 110 m away (tier 0 would draw them LIT) {100f * nWrong / math.max(1, nLand):F1}%");
            int found = 0;
            for (int j = 2; j < n - 2; j++)
            for (int i = 2; i < n - 2; i++)
            {
                int k = j * n + i;
                if (!land[k] || tb[k] < 160f || tb[k] > 350f || top[k] < 60f) continue;
                float g = H[k];
                if (math.abs(H[k + 2] - H[k - 2]) > 6f || math.abs(H[k + 2 * n] - H[k - 2 * n]) > 6f) continue;
                float x = x0 + i * step, z = z0 + j * step;
                float3 eye = new float3(x, g + 1.6f, z);
                float stretch = 0f;
                for (float d = 0f; d <= 300f; d += step)
                {
                    float px = x + ts.x * d, pz = z + ts.y * d;
                    int pi = (int)math.round((px - x0) / step), pj = (int)math.round((pz - z0) / step);
                    if (pi < 0 || pj < 0 || pi >= n || pj >= n) break;
                    int pk = pj * n + pi;
                    if (!land[pk] || tb[pk] < 0f || d + tb[pk] < 140f) break;
                    bool seen = true;
                    for (float u = step; u < d - 5f && seen; u += step)
                        if (HAt(x + ts.x * u, z + ts.y * u) > eye.y + (H[pk] + 0.3f - eye.y) * (u / d)) seen = false;
                    if (!seen && d < 150f) break;
                    stretch = d;
                }
                if (stretch < 150f) continue;
                found++;
                float sc = math.min(stretch, 250f) + 2f * elev - math.distance(new float2(x, z), new float2(c0.x, c0.z)) * 0.002f;
                spots.Add((new float3(x, g, z), ts, elev, hours, tb[k], top[k], stretch, sc));
            }
            L($"    candidate spots: {found}");
        }
        spots.Sort((a, b) => b.score.CompareTo(a.score));
        var chosen = new List<(float3 q, float2 toSun, float elev, float hours, float tb, float cliffH, float stretch, float score)>();
        foreach (var s in spots)
        {
            bool far800 = true;
            foreach (var c in chosen) if (math.distance(s.q.xz, c.q.xz) < 800f) far800 = false;
            if (far800) chosen.Add(s);
            if (chosen.Count >= ArgInt("-cliffspots", 2)) break;
        }
        if (chosen.Count == 0) { L("  NO SPOT FOUND: no cliff foot meets the rule in this area at these sun elevations"); yield break; }

        var json = new StringBuilder("{\"screen\":[" + Screen.width + "," + Screen.height + "],\"vfov\":" + F(cam.fieldOfView) + ",\"spots\":[");
        string V3(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
        float maxRay0 = RaymarchFeature.MaxRayDistance;
        for (int si = 0; si < chosen.Count; si++)
        {
            var s = chosen[si];
            string tag = $"cliff{si + 1}";
            SunCycle.SetHours(s.hours);
            float g = RefColumn(s.q.x, s.q.z).topM;
            Vector3 eyeA = new Vector3(s.q.x, g + 1.6f, s.q.z);
            Vector3 fwdH = new Vector3(s.toSun.x, 0f, s.toSun.y);
            float cliffAng = math.degrees(math.atan2(s.cliffH - 1.6f, s.tb));
            float pitch = math.clamp(cliffAng - 20f, -12f, 5f);
            Quaternion rotA = Quaternion.LookRotation(fwdH, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
            L("");
            L($"=== {tag}: Q ({s.q.x:F1}, {g:F1}, {s.q.z:F1}) m, sun {s.elev:F0} deg at {s.hours:F2} h, toward-sun yaw {math.degrees(math.atan2(s.toSun.x, s.toSun.y)):F0} deg; " +
              $"Q's shadow caster {s.tb:F0} m toward the sun, terrain up to {s.cliffH:F0} m above Q; shadowed ground seen from the eye for {s.stretch:F0} m; " +
              $"cliff top {cliffAng:F1} deg up from the eye, camera pitch {pitch:F1} deg");

            // ---- A: standing in the shadow ----
            Phase4Bootstrapper.RefTier0Centre = null;
            Phase4Bootstrapper.DebugStreamCentre = eyeA;
            cam.transform.position = eyeA; cam.transform.rotation = rotA;
            yield return NearSettle(far, sm);
            yield return CliffShellWait(sh, si == 0 ? firstWait : wait);
            string boxA = BoxJson();
            {
                var clip = Phase4Bootstrapper.Clipmap;
                int3 lo = clip.WindowOriginBricks * 8, dims = clip.WindowDimsChunks * 128;
                // distance from Q to the window edge along the view (toward the sun), and the band ceiling over Q
                float tEdge = float.MaxValue;
                if (s.toSun.x > 1e-4f) tEdge = math.min(tEdge, ((lo.x + dims.x) * 0.1f - eyeA.x) / s.toSun.x);
                if (s.toSun.x < -1e-4f) tEdge = math.min(tEdge, (lo.x * 0.1f - eyeA.x) / s.toSun.x);
                if (s.toSun.y > 1e-4f) tEdge = math.min(tEdge, ((lo.z + dims.z) * 0.1f - eyeA.z) / s.toSun.y);
                if (s.toSun.y < -1e-4f) tEdge = math.min(tEdge, (lo.z * 0.1f - eyeA.z) / s.toSun.y);
                L($"  [window] box {boxA}: edge {tEdge:F0} m from Q toward the sun, band ceiling {RaymarchFeature.ContentCeilingVoxelY * 0.1f - g:F0} m above Q's ground");
            }
            bool before = ArgInt("-cliffbefore", 0) >= 1;
            foreach (string arm in before ? new[] { "play0", "native0", "play", "native", "hit" } : new[] { "play", "native", "hit" })
                yield return CliffArm(cam, eyeA, rotA, arm, $"{tag}_A_{arm}", settle);

            // ---- C: from above, the 0.1 m window kept on A's eye ----
            float fwdOff = math.clamp(s.tb * 0.4f, 40f, 120f);
            Vector3 eyeC = new Vector3(s.q.x + s.toSun.x * fwdOff, g + 250f, s.q.z + s.toSun.y * fwdOff);
            Quaternion rotC = Quaternion.LookRotation(Vector3.down, fwdH);
            Phase4Bootstrapper.DebugStreamCentre = null;
            Phase4Bootstrapper.RefTier0Centre = eyeA;
            RaymarchFeature.MaxRayDistance = 4096f;   // as task 129's REF: tier 0 entered from far above
            cam.transform.position = eyeC; cam.transform.rotation = rotC;
            yield return Frames(30);
            yield return CliffShellWait(sh, wait);
            string boxC = BoxJson();
            foreach (string arm in before ? new[] { "play0", "play", "hit" } : new[] { "play", "hit" })
                yield return CliffArm(cam, eyeC, rotC, arm, $"{tag}_C_{arm}", settle);
            cam.transform.position = eyeC; cam.transform.rotation = rotC;
            Vector3 qPx = cam.WorldToScreenPoint(new Vector3(s.q.x, g, s.q.z));
            var clipC = Phase4Bootstrapper.Clipmap;
            int3 cLo = clipC.WindowOriginBricks * 8, cDims = clipC.WindowDimsChunks * 128;
            var corners = new StringBuilder();
            for (int c = 0; c < 4; c++)
            {
                float cx = ((c == 1 || c == 2) ? cLo.x + cDims.x : cLo.x) * 0.1f, cz = (c >= 2 ? cLo.z + cDims.z : cLo.z) * 0.1f;
                Vector3 p = cam.WorldToScreenPoint(new Vector3(cx, g, cz));
                corners.Append(c > 0 ? "," : "").Append($"[{F(p.x)},{F(Screen.height - p.y)}]");
            }
            RaymarchFeature.MaxRayDistance = maxRay0;
            Phase4Bootstrapper.RefTier0Centre = null;

            // ---- B: 300 m further back ----
            Vector3 b0 = new Vector3(s.q.x - s.toSun.x * 300f, 0f, s.q.z - s.toSun.y * 300f);
            float gb = RefColumn(b0.x, b0.z).topM;
            Vector3 eyeB = new Vector3(b0.x, gb + 1.6f, b0.z);
            float3 tgt = new float3(s.q.x + s.toSun.x * 60f, RefColumn(s.q.x + s.toSun.x * 60f, s.q.z + s.toSun.y * 60f).topM, s.q.z + s.toSun.y * 60f);
            float3[] seeIt = { new float3(s.q.x, g + 0.5f, s.q.z), tgt + new float3(0, 0.5f, 0),
                               new float3(s.q.x + s.toSun.x * 100f, RefColumn(s.q.x + s.toSun.x * 100f, s.q.z + s.toSun.y * 100f).topM + 0.5f, s.q.z + s.toSun.y * 100f) };
            float raised = 0f;
            for (; raised <= 150f; raised += 2f)
            {
                eyeB.y = gb + 1.6f + raised;
                bool ok = true;
                foreach (var p in seeIt) if (!RefLineClear(eyeB, p)) { ok = false; break; }
                if (ok) break;
            }
            Quaternion rotB = Quaternion.LookRotation(((Vector3)tgt - eyeB).normalized, Vector3.up) * Quaternion.Euler(-6f, 0f, 0f);
            L($"  [B] eye {eyeB:F1}: {raised:F0} m over the ground there to see Q's ground ({(raised > 150f ? "NOT CLEAR" : "clear")}), looking at the ground 60 m from Q toward the sun");
            Phase4Bootstrapper.DebugStreamCentre = eyeB;
            cam.transform.position = eyeB; cam.transform.rotation = rotB;
            yield return NearSettle(far, sm);
            yield return CliffShellWait(sh, wait);
            string boxB = BoxJson();
            foreach (string arm in new[] { "play", "hit" }) yield return CliffArm(cam, eyeB, rotB, arm, $"{tag}_B_{arm}", settle);

            json.Append(si > 0 ? "," : "").Append('{')
                .Append($"\"tag\":\"{tag}\",\"elev\":{F(s.elev)},\"hours\":{F(s.hours)},\"toSun\":[{F(s.toSun.x)},{F(s.toSun.y)}],")
                .Append($"\"Q\":[{F(s.q.x)},{F(g)},{F(s.q.z)}],\"casterM\":{F(s.tb)},\"cliffH\":{F(s.cliffH)},\"stretch\":{F(s.stretch)},")
                .Append($"\"A\":{{\"eye\":{V3(eyeA)},\"pitch\":{F(pitch)},\"box\":{boxA}}},")
                .Append($"\"B\":{{\"eye\":{V3(eyeB)},\"box\":{boxB}}},")
                .Append($"\"C\":{{\"eye\":{V3(eyeC)},\"box\":{boxC},\"qPx\":[{F(qPx.x)},{F(Screen.height - qPx.y)}],\"windowPx\":[{corners}]}}}}");
            File.WriteAllText(Path.Combine(_folder, "cliffshadow.json"), json.ToString() + "]}");
            File.WriteAllText(Path.Combine(_folder, "lookreview.txt"), _log.ToString());
        }
        Phase4Bootstrapper.DebugStreamCentre = null;
        Phase4Bootstrapper.RefTier0Centre = null;
        RaymarchFeature.UseDebugViewOverride = false;
        File.WriteAllText(Path.Combine(_folder, "cliffshadow.json"), json.ToString() + "]}");
        L("CLIFF SHADOW done");
    }

    private IEnumerator CliffShellWait(ShellStreamer sh, float cap)
    {
        if (sh == null) yield break;
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < cap)
        {
            int missing = 0;
            for (int k = sh.FirstShell; k <= sh.LastShell; k++) if (!sh.ShellFull(k)) missing += sh.Missing(k);
            if (missing == 0 && sh.InFlight == 0 && sh.Pending == 0) break;
            yield return null;
        }
        L($"  [shells] waited {Time.realtimeSinceStartup - t0:F0} s (cap {cap:F0} s): {ShellCountsLine(sh)}");
    }

    /// play = FSR 3 dynamic (what ./run-play.sh starts with); native = 1080 rows, no upscaler; hit = debug view 14 at native.
    private IEnumerator CliffArm(Camera cam, Vector3 eye, Quaternion rot, string arm, string name, int settle)
    {
        RaymarchFeature.UseDebugViewOverride = false;
        // Task 133: a trailing 0 = the same arm with near sun rays through the shells forced OFF (the "before").
        // Task 134: with -cliffbefore 2 it is the kernel diet that is forced OFF.
        bool nearSh0 = RaymarchFeature.NearShadowShells, diet0 = RaymarchFeature.ShellKernelDiet;
        if (arm.EndsWith("0"))
        {
            if (ArgInt("-cliffbefore", 0) == 2) RaymarchFeature.ShellKernelDiet = false;
            else RaymarchFeature.NearShadowShells = false;
            arm = arm.Substring(0, arm.Length - 1);
        }
        if (arm == "play") ApplyCompareArm("fsr3_dyn");
        else ApplyCompareArm("native");
        if (arm == "hit")
        {
            RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.HitInfo;
            RaymarchFeature.UseDebugViewOverride = true;
        }
        cam.ResetProjectionMatrix();
        cam.transform.position = eye; cam.transform.rotation = rot;
        yield return Frames(arm == "hit" ? 5 : settle);
        yield return CaptureTo(name);
        RaymarchFeature.UseDebugViewOverride = false;
        RaymarchFeature.NearShadowShells = nearSh0;
        RaymarchFeature.ShellKernelDiet = diet0;
    }
}
