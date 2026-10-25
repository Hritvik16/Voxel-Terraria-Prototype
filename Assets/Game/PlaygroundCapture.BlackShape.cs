// Assets/Game/PlaygroundCapture.BlackShape.cs
//
// -lookreview -cliffshadow 1 -blackshape N: TASK 136, capture + diagnosis only (no fix,
// no default or look change). Tasks 131 / 135 saw a pure-black jagged shape on the near
// rock at cliff2's A view. This re-shoots spot N (default 2) of task 131's search:
//   A      the same eye / rotation / sun as CliffShadow's A: play, native, hit (view 14),
//          sun (view 7, sunVis), nrm (view 3, normals), nosky (native with the vertical
//          sky probe OFF, RaymarchFeature.UseSkyProbeAmbient), then noon native + sun.
//   close  15 m nearer the black spot T (the CPU hit of pixel -bspx, default 1780,520).
//   side   ~40 m from T, about 50 deg off A's line.
//   other  a second overhang (-bsother x,y,z m, default the stone arches left of cliff2's
//          view) from 28 m on its shadow side.
// Per pose a CPU probe on the ChunkStore (the tier-0 truth the GPU mirror is written from):
// for pixels down a column and along a row through T it logs the hit voxel, its face, what
// stands straight above the face cell (the sky probe's ray) and toward the sun, inside the
// 0.1 m window box -- the two rays that zero ambient and sun in Raymarch.compute's shading.
using System.Collections;
using System.Globalization;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Mirror;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private IEnumerator BlackShape(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, string tag,
                                   float3 q, float2 toSun, float hours, float tb, float cliffH, int settle, float wait)
    {
        // ---- A: exactly CliffShadow's A ----
        SunCycle.SetHours(hours);
        float g = RefColumn(q.x, q.z).topM;
        Vector3 eyeA = new Vector3(q.x, g + 1.6f, q.z);
        Vector3 fwdH = new Vector3(toSun.x, 0f, toSun.y);
        float cliffAng = math.degrees(math.atan2(cliffH - 1.6f, tb));
        float pitch = math.clamp(cliffAng - 20f, -12f, 5f);
        Quaternion rotA = Quaternion.LookRotation(fwdH, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
        L("");
        L($"BLACK SHAPE (task 136): {tag} A eye {eyeA:F1}, pitch {pitch:F1} deg, sun {hours:F2} h");
        Phase4Bootstrapper.RefTier0Centre = null;
        Phase4Bootstrapper.DebugStreamCentre = eyeA;
        cam.transform.position = eyeA; cam.transform.rotation = rotA;
        yield return NearSettle(far, sm);
        yield return CliffShellWait(sh, wait);

        int px = 1780, py = 520;
        string pxArg = ArgValue("-bspx");
        if (!string.IsNullOrEmpty(pxArg)) { var a = pxArg.Split(','); px = int.Parse(a[0]); py = int.Parse(a[1]); }
        cam.ResetProjectionMatrix();
        bool tOk = BsCpuPixel(cam, px, py, out Vector3 T, out _);
        L($"  T = CPU hit of pixel ({px},{py}): {(tOk ? T.ToString("F1") : "NO HIT")}");
        BsProbeLines(cam, "A", px, py);
        foreach (string arm in new[] { "play", "native", "hit", "sun", "nrm", "nosky" })
            yield return BsArm(cam, eyeA, rotA, arm, $"bs_A_{arm}", settle);
        SunCycle.SetHours(12f);
        L("  noon (12.00 h):");
        BsProbeLines(cam, "A noon", px, py);
        foreach (string arm in new[] { "native", "sun" })
            yield return BsArm(cam, eyeA, rotA, arm, $"bs_Anoon_{arm}", settle);
        SunCycle.SetHours(hours);
        if (!tOk) T = eyeA + rotA * Vector3.forward * 88f;

        // ---- close: 15 m nearer T ----
        Vector3 d = T - eyeA; d.y = 0f; d.Normalize();
        Vector3 e = eyeA + d * 15f; e.y = RefColumn(e.x, e.z).topM + 1.6f;
        yield return BsPose(far, cam, sm, sh, "close", e, T, settle, wait,
                            new[] { "native", "hit", "sun", "nrm", "nosky" });

        // ---- side: ~40 m from T, the side whose ground is nearer T's height ----
        Vector3 perp = new Vector3(d.z, 0f, -d.x);
        Vector3 best = Vector3.zero; float bestErr = float.MaxValue;
        foreach (float sgn in new[] { 1f, -1f })
        {
            Vector3 c = T - d * 25f + perp * (30f * sgn);
            c.y = RefColumn(c.x, c.z).topM + 1.6f;
            float err = math.abs(c.y - T.y);
            L($"  side candidate {(sgn > 0 ? "+" : "-")}: eye {c:F1}, {err:F0} m from T's height");
            if (err < bestErr) { bestErr = err; best = c; }
        }
        yield return BsPose(far, cam, sm, sh, "side", best, T, settle, wait, new[] { "native", "hit", "nrm" });

        // ---- other: a second overhang, from its shadow side ----
        Vector3 o = new Vector3(3455f, 576f, 5150f);
        string oArg = ArgValue("-bsother");
        if (!string.IsNullOrEmpty(oArg))
        {
            var a = oArg.Split(',');
            o = new Vector3(float.Parse(a[0], CultureInfo.InvariantCulture), float.Parse(a[1], CultureInfo.InvariantCulture),
                            float.Parse(a[2], CultureInfo.InvariantCulture));
        }
        Vector3 eo = o - fwdH * 28f; eo.y = RefColumn(eo.x, eo.z).topM + 1.6f;
        yield return BsPose(far, cam, sm, sh, "other", eo, o, settle, wait, new[] { "native", "hit", "sun", "nrm", "nosky" });

        Phase4Bootstrapper.DebugStreamCentre = null;
        Phase4Bootstrapper.RefTier0Centre = null;
        RaymarchFeature.UseDebugViewOverride = false;
        L("BLACK SHAPE done");
    }

    private IEnumerator BsPose(FarField far, Camera cam, StreamManager sm, ShellStreamer sh, string name,
                               Vector3 eye, Vector3 look, int settle, float wait, string[] arms)
    {
        Quaternion rot = Quaternion.LookRotation((look - eye).normalized, Vector3.up);
        L($"  [{name}] eye {eye:F1} looking at {look:F1} ({Vector3.Distance(eye, look):F0} m)");
        Phase4Bootstrapper.DebugStreamCentre = eye;
        cam.transform.position = eye; cam.transform.rotation = rot;
        yield return NearSettle(far, sm);
        yield return CliffShellWait(sh, wait);
        cam.ResetProjectionMatrix();
        BsProbeLines(cam, name, Screen.width / 2, Screen.height / 2);
        foreach (string arm in arms) yield return BsArm(cam, eye, rot, arm, $"bs_{name}_{arm}", settle);
    }

    /// play = FSR 3 dynamic; native = 1080 rows; hit / sun / nrm = debug views 14 / 7 / 3 at native;
    /// nosky = native with the vertical sky probe off (ambient as if open sky).
    private IEnumerator BsArm(Camera cam, Vector3 eye, Quaternion rot, string arm, string name, int settle)
    {
        RaymarchFeature.UseDebugViewOverride = false;
        bool sky0 = RaymarchFeature.UseSkyProbeAmbient;
        ApplyCompareArm(arm == "play" ? "fsr3_dyn" : "native");
        bool dbg = arm == "hit" || arm == "sun" || arm == "nrm";
        if (dbg)
        {
            RaymarchFeature.DebugViewOverride = arm == "hit" ? RaymarchFeature.DebugMode.HitInfo
                                              : arm == "sun" ? RaymarchFeature.DebugMode.SunVis : RaymarchFeature.DebugMode.Normals;
            RaymarchFeature.UseDebugViewOverride = true;
        }
        if (arm == "nosky") RaymarchFeature.UseSkyProbeAmbient = false;
        cam.ResetProjectionMatrix();
        cam.transform.position = eye; cam.transform.rotation = rot;
        yield return Frames(arm == "hit" || arm == "nrm" ? 5 : settle);
        yield return CaptureTo(name);
        RaymarchFeature.UseDebugViewOverride = false;
        RaymarchFeature.UseSkyProbeAmbient = sky0;
    }

    /// CPU probe lines down column px and along row py (screen pixels, y down).
    private void BsProbeLines(Camera cam, string pose, int px, int py)
    {
        L($"  [cpu {pose}] pixel: hit m | voxel | face | mat | up (sky probe) | toward sun   (window-bounded, ChunkStore)");
        for (int y = math.max(0, py - 240); y <= math.min(Screen.height - 1, py + 90); y += 30) BsCpuPixel(cam, px, y, out _, out string s1, true);
        for (int x = math.max(0, px - 300); x <= math.min(Screen.width - 1, px + 120); x += 60) if (x != px) BsCpuPixel(cam, x, py, out _, out string s2, true);
    }

    private bool BsCpuPixel(Camera cam, int x, int y, out Vector3 hitM, out string line, bool log = false)
    {
        hitM = Vector3.zero; line = "";
        var st = Phase4Bootstrapper.Store; var clip = Phase4Bootstrapper.Clipmap;
        if (st == null || clip == null) return false;
        int3 lo = clip.WindowOriginBricks * 8, hi = lo + clip.WindowDimsChunks * 128;
        Ray r = cam.ViewportPointToRay(new Vector3((x + 0.5f) / Screen.width, 1f - (y + 0.5f) / Screen.height, 0f));
        float3 o = (float3)r.origin * 10f, dir = math.normalize((float3)r.direction);
        if (!BsDda(st, o, dir, 3000f, lo, hi, out int3 v, out int3 n, out float t))
        {
            line = $"    ({x},{y}): no tier-0 hit within the window";
            if (log) L(line);
            return false;
        }
        hitM = (Vector3)((o + dir * t) * 0.1f);
        byte mat = st.GetVoxel(v);
        int3 f = v + n;
        // sky probe: straight up from the face cell to the window top
        string up = "open to the window top";
        for (int yy = f.y; yy < hi.y; yy++)
            if (st.GetVoxel(new int3(f.x, yy, f.z)) != 0) { up = $"BLOCKED {(yy - f.y) * 0.1f:F1} m up"; break; }
        float3 sd = math.normalize(SunCycle.Direction);
        string sun = sd.y <= 0f ? "night" : (BsDda(st, (float3)f + 0.5f, sd, 4000f, lo, hi, out _, out _, out float ts)
            ? $"BLOCKED at {ts * 0.1f:F1} m" : "lit (left the window)");
        line = $"    ({x},{y}): {t * 0.1f:F1} m | {v.x},{v.y},{v.z} (y {v.y * 0.1f:F1} m) | n {n.x},{n.y},{n.z} | {mat} | {up} | {sun}";
        if (log) L(line);
        return true;
    }

    /// Per-voxel DDA in voxel units, inside the window box [lo, hi).
    private static bool BsDda(ChunkStore st, float3 o, float3 d, float maxT, int3 lo, int3 hi,
                              out int3 v, out int3 n, out float t)
    {
        v = (int3)math.floor(o); n = int3.zero; t = 0f;
        int3 step = (int3)math.sign(d);
        float3 tDelta = math.abs(1f / d);
        float3 tMax = new float3(
            d.x > 0 ? (v.x + 1 - o.x) : (o.x - v.x),
            d.y > 0 ? (v.y + 1 - o.y) : (o.y - v.y),
            d.z > 0 ? (v.z + 1 - o.z) : (o.z - v.z)) * tDelta;
        for (int i = 0; i < 20000 && t <= maxT; i++)
        {
            if (math.any(v < lo) || math.any(v >= hi)) { if (i > 0) return false; }
            else if (st.GetVoxel(v) != 0) return true;
            if (tMax.x < tMax.y && tMax.x < tMax.z) { t = tMax.x; v.x += step.x; tMax.x += tDelta.x; n = new int3(-step.x, 0, 0); }
            else if (tMax.y < tMax.z) { t = tMax.y; v.y += step.y; tMax.y += tDelta.y; n = new int3(0, -step.y, 0); }
            else { t = tMax.z; v.z += step.z; tMax.z += tDelta.z; n = new int3(0, 0, -step.z); }
        }
        return false;
    }
}
