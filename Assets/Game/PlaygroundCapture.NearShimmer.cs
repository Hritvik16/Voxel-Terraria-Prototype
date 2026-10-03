// Assets/Game/PlaygroundCapture.NearShimmer.cs
//
// -nearshimmer (with -world tg2; run-tg2world.sh MODE=shimmer): a "before"
// SHIMMER NUMBER for NEAR terrain at the 960 gate, so TAA (overnight task 06)
// is judged by a number and not by eye. -farshimmer's quarter-pixel nudge
// test, extended to the voxel band (the far-band test itself is unchanged):
//   poses  the tg2 eye-level poses (eye_level_1..3, shared with -farpreview)
//          and slope_walk: a walking-height eye looking across a slope 30-80 m
//          away (voxels are smaller than a pixel beyond ~50 m at 960). The
//          slope candidates come from the band-limited map and are CHECKED
//          against the streamed voxels (real ground under the eye, centre ray
//          30-80 m, little of the frame closer than 5 m) before one is used.
//   frames <pose>_base; the image shifted a QUARTER GATE PIXEL right/left/up/
//          down (xP/xM/yP/yM) by an offset in the camera's projection matrix --
//          the sub-pixel jitter TAA itself applies, the same shift at every
//          depth. (A camera rotation that small never reached the frame: the
//          first run's nudged frames were byte-identical.) Then <pose>_still,
//          the base pose again, unmoved: the NOISE FLOOR (anything that changes
//          by itself, e.g. temporal shadows). Every frame after SETTLE frames.
//   depth  <pose>_depth.f32: one CPU DDA ray through the ChunkStore (what the
//          voxel band draws) per 4x4 gate pixels, the hit distance in metres up
//          to 200 m, -1 = no voxel hit (far band or sky). Row 0 = image top.
//   band   <pose>_band.png: debug view 9, for the sky mask.
// Noon, clock paused: at golden hour the eye-level foreground is near black,
// which hides the very change being measured.
// tools/nearshimmer-report.py turns the folder into the report.
//
// Diagnostic frames only: no timing here is a measurement.
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
    private const int NearShimmerSettle = 20;          // frames at rest before each shot
    private const int NearShimmerDepthStep = 4;        // one depth ray per 4x4 gate pixels
    private const float NearShimmerMaxDepthM = 200f;

    private IEnumerator NearShimmer()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + "_nearshimmer");
        Directory.CreateDirectory(_folder);
        LightingDriver.ClockPaused = true;
        SunCycle.SetHours(12f);
        int gw = ArgInt("-gateres", 960), gh = gw * 9 / 16;
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(gw, gh);
        Playground.DebugHideUi = true;
        Playground.DebugSuspendFluidSim = true;
        var far = FarField.Active;
        var cam = Camera.main;
        var sm = Phase4Bootstrapper.Streamer;
        var store = Phase4Bootstrapper.Store;
        L($"NEAR SHIMMER -- {ts}  (quarter-gate-pixel PROJECTION nudges; gate {gw}x{gh}; noon; " +
          $"preset {(HasFlag("-renderpreset") ? RenderPreset.Current.ToString() : "not set (as shipped)")}; " +
          $"temporal shadows {(RaymarchFeature.TemporalShadows ? "ON" : "OFF")}; shadow level {RaymarchFeature.ShadowQualityLevel}; " +
          $"TAA {(RaymarchFeature.Taa ? $"ON{(RaymarchFeature.TaaChecker ? " + CHECKERBOARD" : "")} (sharpen {RaymarchFeature.TaaSharpenAmount:F2})" : "OFF")}; " +
          $"settle {NearShimmerSettle} frames)");
        if (far == null || !far.UsesTg2 || !Phase4Bootstrapper.IsTg2World)
        {
            L("needs -world tg2");
            File.WriteAllText(Path.Combine(_folder, "nearshimmer.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var map = far.Tg2Map;
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = far.Tg2SeaShift;
        // tg2 voxels -> engine metres (as -farpreview's E).
        Vector3 E(float3 g) => new Vector3((g.x - off.x) * 0.1f, (g.y - seaShift) * 0.1f, (g.z - off.y) * 0.1f);
        float3 F(Vector3 v) => new float3(v.x, v.y, v.z);
        var poses = new List<(string, Vector3, Quaternion)>();
        foreach (var (n, e, a) in Tg2EyeLevelPoses(map))
            poses.Add((n, E(e), Quaternion.LookRotation((E(a) - E(e)).normalized, Vector3.up)));

        // slope_walk: the first map candidate that the streamed voxels confirm.
        // -noslope skips it, so eye_level_1 -> eye_level_2 is the first jump
        // pair, as in the first run (see KNOWN_ISSUES, near-shimmer baseline).
        var cands = HasFlag("-noslope") ? new List<(float3, float3, string)>() : NearSlopeCandidates(map, 8);
        bool slopeOk = false;
        for (int ci = 0; ci < cands.Count && !slopeOk; ci++)
        {
            var (ce, ca, cNote) = cands[ci];
            Vector3 eye = E(ce), at = E(ca);
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
            yield return NearSettle(far, sm);
            float g0 = StoreGroundM(store, eye.x, eye.z, eye.y + 10f), g1 = StoreGroundM(store, at.x, at.z, at.y + 30f);
            if (float.IsNaN(g0) || float.IsNaN(g1)) { L($"slope candidate {ci} rejected: no open ground in the store ({cNote})"); continue; }
            eye.y = g0 + 1.7f; at.y = g1 + 1f;
            var rot = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
            cam.transform.position = eye; cam.transform.rotation = rot;
            yield return Frames(2);
            float dc = NearDepthM(store, F(eye), F(rot * Vector3.forward), NearShimmerMaxDepthM);
            int close = 0;
            for (int gy = 0; gy < 9; gy++)
            for (int gx = 0; gx < 16; gx++)
            {
                Ray ray = cam.ViewportPointToRay(new Vector3((gx + 0.5f) / 16f, (gy + 0.5f) / 9f, 0f));
                float d = NearDepthM(store, F(eye), F(ray.direction), 5f);
                if (d >= 0f) close++;
            }
            if (dc < 30f || dc > 80f || close > 36) { L($"slope candidate {ci} rejected: centre ray {dc:F1} m, {close}/144 rays hit within 5 m ({cNote})"); continue; }
            L($"slope_walk = candidate {ci}: {cNote}; on the voxels: centre ray {dc:F1} m, {close}/144 rays within 5 m");
            poses.Add(("slope_walk", eye, rot));
            slopeOk = true;
        }
        if (!slopeOk) L(HasFlag("-noslope") ? "slope_walk: skipped (-noslope)" : "slope_walk: no candidate confirmed -- pose skipped");

        var nudges = new (string n, float dx, float dy)[] { ("xP", 0.25f, 0f), ("xM", -0.25f, 0f), ("yP", 0f, 0.25f), ("yM", 0f, -0.25f) };
        int dw = gw / NearShimmerDepthStep, dh = gh / NearShimmerDepthStep;
        var depth = new float[dw * dh];
        foreach (var (n, pos, rot) in poses)
        {
            cam.transform.position = pos; cam.transform.rotation = rot;
            cam.ResetProjectionMatrix();
            yield return NearSettle(far, sm);
            int deficit = sm != null ? sm.LoadDeficit() : -1;
            bool inAir = store.GetVoxel(CoordMath.WorldToVoxel(F(pos))) == 0;
            yield return CaptureTo($"{n}_base");
            Matrix4x4 p0 = cam.projectionMatrix;
            foreach (var (dn, dx, dy) in nudges)
            {
                Matrix4x4 p = p0;
                p.m02 += 2f * dx / gw;      // NDC offset of dx gate pixels (as a TAA jitter)
                p.m12 += 2f * dy / gh;
                cam.projectionMatrix = p;
                yield return Frames(NearShimmerSettle);
                yield return CaptureTo($"{n}_{dn}");
            }
            cam.ResetProjectionMatrix();
            yield return Frames(NearShimmerSettle);
            yield return CaptureTo($"{n}_still");
            RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.FarBand;
            RaymarchFeature.UseDebugViewOverride = true;
            yield return Frames(6);
            yield return CaptureTo($"{n}_band");
            RaymarchFeature.UseDebugViewOverride = false;

            // Depth, from the base pose, a few rows a frame (the camera is still).
            int nNear = 0, nMid = 0, nNone = 0;
            float3 o = F(pos);
            for (int r = 0; r < dh; r++)
            {
                for (int x = 0; x < dw; x++)
                {
                    Ray ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / dw, 1f - (r + 0.5f) / dh, 0f));
                    float d = NearDepthM(store, o, F(ray.direction), NearShimmerMaxDepthM);
                    depth[r * dw + x] = d;
                    if (d < 0f) nNone++; else if (d < 50f) nNear++; else nMid++;
                }
                if ((r & 7) == 7) yield return null;
            }
            using (var fs = new BinaryWriter(File.Create(Path.Combine(_folder, $"{n}_depth.f32"))))
                foreach (float d in depth) fs.Write(d);
            var hits = new List<float>();
            foreach (float d in depth) if (d >= 0f) hits.Add(d);
            hits.Sort();
            float tot = 0.01f * depth.Length;
            L($"{n,-12} eye {pos}  {(inAir ? "in air" : "INSIDE ROCK")}  deficit {deficit}  depth {dw}x{dh}: " +
              $"<50 m {nNear / tot:F1}%  50-200 m {nMid / tot:F1}%  no voxel hit {nNone / tot:F1}%  " +
              $"median hit {(hits.Count > 0 ? hits[hits.Count / 2] : -1f):F1} m");
        }
        File.WriteAllText(Path.Combine(_folder, "nearshimmer.txt"), _log.ToString());
        Debug.Log("[nearshimmer]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// After a jump: the far ring refills and the voxel band streams in. Capped
    /// in SECONDS, not frames: at this gate's frame rate 3000 frames ran out
    /// with 763 chunks still to load (first eye_level_3, 2026-10-02).
    private static IEnumerator NearSettle(FarField far, StreamManager sm)
    {
        yield return Frames(30);
        float t0 = Time.realtimeSinceStartup;
        while (far.Busy && Time.realtimeSinceStartup - t0 < 60f) yield return null;
        while (sm != null && sm.LoadDeficit() > 0 && Time.realtimeSinceStartup - t0 < 180f) yield return null;
        yield return Frames(60);
    }

    /// Distance (m) to the first solid voxel along the ray, or -1 within maxM.
    /// The per-voxel walk of RaymarchReference.TracerRaycast without its
    /// 1000-step cap (200 m needs up to ~3500 steps).
    private static float NearDepthM(ChunkStore store, float3 origin, float3 dir, float maxM)
    {
        float3 o = origin * 10f;
        float3 d = math.normalize(dir);
        int3 step = (int3)math.sign(d);
        float3 tDelta = math.abs(1f / d);
        int3 v = CoordMath.WorldToVoxel(origin);
        float3 tMax = new float3(
            step.x > 0 ? (v.x + 1f - o.x) : (o.x - v.x),
            step.y > 0 ? (v.y + 1f - o.y) : (o.y - v.y),
            step.z > 0 ? (v.z + 1f - o.z) : (o.z - v.z)) * tDelta;
        float t = 0f, tEnd = maxM * 10f;
        while (t < tEnd)
        {
            if (store.GetVoxel(v) != 0) return t * 0.1f;
            if (tMax.x < tMax.y && tMax.x < tMax.z) { v.x += step.x; t = tMax.x; tMax.x += tDelta.x; }
            else if (tMax.y < tMax.z) { v.y += step.y; t = tMax.y; tMax.y += tDelta.y; }
            else { v.z += step.z; t = tMax.z; tMax.z += tDelta.z; }
        }
        return -1f;
    }

    /// The real top surface (metres: the first air voxel above solid) under
    /// (x, z), scanning the ChunkStore down from yTopM; NaN if the start is
    /// solid or nothing solid lies within 60 m below it.
    private static float StoreGroundM(ChunkStore store, float xM, float zM, float yTopM)
    {
        int3 v = CoordMath.WorldToVoxel(new float3(xM, yTopM, zM));
        if (store.GetVoxel(v) != 0) return float.NaN;
        for (int i = 0; i < 600; i++, v.y--)
            if (store.GetVoxel(v) != 0) return (v.y + 1) * 0.1f;
        return float.NaN;
    }

    /// Up to `max` walking-height eyes (tg2 voxels) on dry ground whose view in
    /// one of 8 directions is a slope rising 10-40 m between 30 and 80 m away,
    /// with a clear line of sight to its middle (55 m, 1 m up) -- on the
    /// band-limited map, so each is checked on the voxels before use.
    /// Golden-angle spiral from the island centre, one per site: deterministic
    /// per seed. Not an iterator (TgGround holds the map's pointer view).
    private static List<(float3 eye, float3 at, string note)> NearSlopeCandidates(TerrainGen.WorldMap map, int max)
    {
        var list = new List<(float3, float3, string)>();
        var spec = map.Spec;
        float c = spec.worldSizeVoxels * 0.5f;
        for (int i = 0; i < 4000 && list.Count < max; i++)
        {
            float ang = i * 2.39996f, r = 1500f + i * 6f;
            float2 p = new float2(c, c) + new float2(math.cos(ang), math.sin(ang)) * r;
            float g = TgGround(map, p.x, p.y);
            if (g < spec.seaLevelY + 150f) continue;
            float eyeY = g + 17f;
            for (int k = 0; k < 8; k++)
            {
                float2 d = new float2(math.cos(k * 0.785398f), math.sin(k * 0.785398f));
                float2 p30 = p + d * 300f, p55 = p + d * 550f, p80 = p + d * 800f;
                float g30 = TgGround(map, p30.x, p30.y), g55 = TgGround(map, p55.x, p55.y), g80 = TgGround(map, p80.x, p80.y);
                float rise = g80 - g30;
                if (rise < 100f || rise > 400f || g30 < spec.seaLevelY + 50f) continue;
                float tgtY = g55 + 10f;
                bool clear = true;
                for (float t = 20f; t < 550f && clear; t += 20f)
                {
                    float2 pt = p + d * t;
                    if (TgGround(map, pt.x, pt.y) + 5f > eyeY + (tgtY - eyeY) * (t / 550f)) clear = false;
                }
                if (!clear) continue;
                list.Add((new float3(p.x, eyeY, p.y), new float3(p55.x, tgtY, p55.y),
                          $"spiral site {i}, direction {k}, map slope rises {rise * 0.1f:F1} m between 30 and 80 m " +
                          $"({math.degrees(math.atan(rise / 500f)):F0} deg mean)"));
                break;
            }
        }
        return list;
    }
}
