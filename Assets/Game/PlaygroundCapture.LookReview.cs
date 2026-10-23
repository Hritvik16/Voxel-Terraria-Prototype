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
//   TASK 120 (capture-only): -lookab 1 shoots every fsr3 / native arm twice in
//           the same frame state, <name> (task 120's cube look as flagged) and
//           <name>_old (CubeBounce / CubeFilter / CellHash off = task 85b's look);
//           -lookgolden 1 adds, at EVERY pose, fsr3 at 17 h turned away from the
//           sun (<pose>_golden_<fsr3>, with _old under -lookab).
//   TASK 118 (capture-only): -lookclamp M (with -lookgolden 1) adds, at every
//           pose, golden hour with the sun behind (away) and beside (side) the
//           camera: fsr3 / none / sun-visibility view, the launch's sun-shadow
//           clamp (_full) vs M m (_clamp<M>), in the same frame state.
//   TASK 124 (capture-only): -lookreuse N (with -lookgolden 1) adds, at every
//           pose, golden hour with the camera still: temporal sun-shadow reuse one
//           frame in N (_reuse<N>) vs the launch's period vs every frame (_reuse1,
//           the reference), none / sun-visibility view: STILL sun behind (away)
//           and beside (side); SUN side, the sun moving as in a 120 s day for 60
//           frames (the same sun sequence per period); EDIT side, a 1 x 1 x 4 m
//           stone pillar placed 10 m ahead, frames 1-8 after the edit (_f1.._f8),
//           then the original voxels restored.
//   TASK 121 (capture-only): -looksky 1 adds, at every pose, fsr3 at noon (the
//           pose's heading), golden hour 17 h away from and toward the sun, sunset
//           (sun 2 deg up) and dusk (3 deg down) toward the sun:
//           <pose>_sky_<time>, and <pose>_sky_<time>_old with the sky look off
//           (SkyLook / SkyHaze false = before task 121), in the same frame state.
//   TASK 122 (capture-only): -lookbiome 1 adds poses border_0..2: up to three
//           land points within 2 km where the RECORD biome changes (25 m grid),
//           one per biome pair, >= 500 m apart; eye 35 m over the ground 150 m
//           back ALONG the border (sun behind where it can be), pitched down 12
//           deg, so the border runs into the frame. Every pose also gets
//           <pose>_<fsr3>_notint (the per-biome tint off, same frame state). The
//           generator's border blend is per launch: compare with -biomeblend 0.
//   TASK 125 (capture-only): -lookedge 1 adds pose edge_x (the high pose's height,
//           2 km from the world's +x edge, looking +x) and, at every pose, fsr3 at
//           noon looking +x / -x / +z / +x+z (pitch as the pose) and golden hour
//           17 h away from / toward the sun: <pose>_edge_<tag>, and _old with the
//           edge fade off (EdgeFade false = task 121's fade inside the haze), same
//           frame state. Each logs the rows (1080 output, centre column) where a flat
//           sea would end at the WORLD's edge and at the far band's square.
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
    /// Task 122: -lookbiome's border poses (see the header).
    private void LookBorderPoses(Vector3 centre, float2 shz, List<(string n, Vector3 eye, Quaternion rot, string note)> poses,
                                 Func<float2, float, Quaternion> look)
    {
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        if (src == null) { L("lookbiome: no tg2 source -- no border poses"); return; }
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        string[] names = { "forest", "desert", "snow", "jungle", "savanna", "badlands" };
        var found = new List<(float2 at, float2 tangent, int a, int b, float dist)>();
        for (float dz = -2000f; dz <= 2000f; dz += 25f)
        for (float dx = -2000f; dx <= 2000f; dx += 25f)
        {
            float2 pm = new float2(centre.x + dx, centre.z + dz), pv = pm * 10f;
            byte b0 = TerrainGen.ShapeField.Biome(in o, pv);
            byte bx = TerrainGen.ShapeField.Biome(in o, pv + new float2(250f, 0f));
            byte bz = TerrainGen.ShapeField.Biome(in o, pv + new float2(0f, 250f));
            if (b0 == bx && b0 == bz) continue;
            byte other = b0 != bx ? bx : bz;
            int a = Math.Min((int)b0, other), b = Math.Max((int)b0, other);
            float dist = math.length(new float2(dx, dz));
            var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, pv.x, pv.y, 1);
            if (col.height < spec.seaLevelY + 30 || col.water > col.height) continue;
            float2 n = b0 != bx ? new float2(1f, 0f) : new float2(0f, 1f);
            if (b0 != bx && b0 != bz) n = math.normalize(new float2(1f, 1f));
            float2 t = new float2(-n.y, n.x);
            if (math.dot(t, -shz) < 0f) t = -t;
            int same = found.FindIndex(f => f.a == a && f.b == b);
            if (same >= 0) { if (dist < found[same].dist) found[same] = (pm, t, a, b, dist); continue; }
            found.Add((pm, t, a, b, dist));
        }
        found.Sort((p, q) => p.dist.CompareTo(q.dist));
        int k = 0;
        foreach (var f in found)
        {
            if (k >= 3) break;
            bool near = false;
            for (int i = 0; i < k; i++)
            {
                var prev = poses[poses.Count - k + i];
                if (math.distance(new float2(prev.eye.x, prev.eye.z), f.at - f.tangent * 150f) < 500f) near = true;
            }
            if (near) continue;
            float2 e2 = f.at - f.tangent * 150f;
            float ground = WalkEyeY(e2.x, e2.y, centre.y) - 1.6f;
            var eye = new Vector3(e2.x, ground + 35f, e2.y);
            poses.Add(($"border_{k}", eye, look(f.tangent, -12f),
                       $"{names[f.a]} | {names[f.b]} border at ({f.at.x:F0}, {f.at.y:F0}) m, {f.dist:F0} m from the centre, looking along it"));
            k++;
        }
        L($"lookbiome: {found.Count} biome pairs with a land border within 2 km; {k} border poses");
    }

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
        bool ab = ArgInt("-lookab", 0) != 0, goldenAll = ArgInt("-lookgolden", 0) != 0;   // task 120
        float clampM = ArgFloat("-lookclamp", 0f);   // task 118
        int reuseN = ArgInt("-lookreuse", 0);        // task 124
        bool skyAB = ArgInt("-looksky", 0) != 0;      // task 121
        bool biomeLook = ArgInt("-lookbiome", 0) != 0;   // task 122
        bool edgeAB = ArgInt("-lookedge", 0) != 0;       // task 125
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
            var spec = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
            if (edgeAB && spec != null)
            {
                float wM = spec.Spec.worldSizeVoxels * 0.1f;
                poses.Add(("edge_x", new Vector3(wM - 2000f, hi.y, hi.z), Look(new float2(1f, 0f), -4f), $"the high pose's height, 2 km from the world's +x edge ({wM:F0} m)"));
            }
        }
        if (biomeLook) LookBorderPoses(centre, shz, poses, Look);

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
                yield return CaptureCubeAB($"{n}_{arm}", ab && arm != none);
                var d = RaymarchFeature.LastDispatchResolution;
                L($"  {n,-10} {arm,-9} dispatch {d.x}x{d.y}");
            }
            ApplyCompareArm(fsr);
            RaymarchFeature.ShellTint = true;
            yield return Frames(30);
            yield return CaptureTo($"{n}_{fsr}_tint");
            RaymarchFeature.ShellTint = false;
            if (goldenAll)
            {
                const float golden = 17f;
                SunCycle.SetHours(golden);
                float3 gs = SunCycle.DirectionAt(golden);
                ApplyCompareArm(fsr);
                cam.ResetProjectionMatrix();
                cam.transform.position = eye; cam.transform.rotation = Look(-math.normalizesafe(new float2(gs.x, gs.z)), -Mathf.DeltaAngle(0f, rot.eulerAngles.x));
                yield return Frames(60);
                yield return CaptureCubeAB($"{n}_golden_{fsr}", ab);
                L($"  golden (task 120): {golden:F1} h, sun elevation {SunCycle.ElevationDegreesAt(golden):F0} deg, turned away from the sun");
                if (clampM > 0f) yield return LookClampAB(n, cam, eye, -Mathf.DeltaAngle(0f, rot.eulerAngles.x), -math.normalizesafe(new float2(gs.x, gs.z)), fsr, none, clampM);
                if (reuseN > 0) yield return LookReuseAB(n, cam, store, eye, -Mathf.DeltaAngle(0f, rot.eulerAngles.x), -math.normalizesafe(new float2(gs.x, gs.z)), none, reuseN);
                SunCycle.SetHours(noon);
                cam.transform.rotation = rot;
            }
            if (skyAB) yield return LookSkyAB(n, cam, eye, rot, fsr, noon);
            if (edgeAB) yield return LookEdgeAB(n, cam, eye, rot, fsr, noon);
            if (biomeLook)
            {
                ApplyCompareArm(fsr);
                cam.ResetProjectionMatrix();
                cam.transform.position = eye; cam.transform.rotation = rot;
                bool tint = RaymarchFeature.BiomeTint;
                RaymarchFeature.BiomeTint = false;
                yield return Frames(60);
                yield return CaptureTo($"{n}_{fsr}_notint");
                RaymarchFeature.BiomeTint = tint;
                L($"  biome (task 122): {n}_{fsr}_notint = per-biome tint off; launch's border blend " +
                  $"{(Phase4Bootstrapper.Streamer?.TerrainGenSource != null && Phase4Bootstrapper.Streamer.TerrainGenSource.Spec.BiomeBlend ? "ON" : "OFF")}");
            }
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

    /// Task 118: at golden hour, sun behind (away) and beside (side) the camera: each of the
    /// fsr3 / none arms and the sun-visibility view, with the launch's sun-shadow clamp
    /// (<pose>_golden_<tag>_<arm>_full) and clamped at M m (..._clamp<M>), 60 frames each.
    private IEnumerator LookClampAB(string n, Camera cam, Vector3 eye, float pitch, float2 away, string fsr, string none, float m)
    {
        float baseClamp = RaymarchFeature.ShadowMaxDistanceOverrideM;
        string cm = $"clamp{m:F0}";
        foreach (var (tag, h) in new[] { ("away", away), ("side", new float2(away.y, -away.x)) })
        {
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.Euler(-pitch, math.degrees(math.atan2(h.x, h.y)), 0f);
            foreach (string arm in new[] { fsr, none, "sunvis" })
            {
                ApplyCompareArm(arm == "sunvis" ? none : arm);
                cam.ResetProjectionMatrix();
                RaymarchFeature.UseDebugViewOverride = arm == "sunvis";
                RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
                foreach (var (label, v) in new[] { ("full", baseClamp), (cm, m) })
                {
                    RaymarchFeature.ShadowMaxDistanceOverrideM = v;
                    yield return Frames(60);
                    yield return CaptureTo($"{n}_golden_{tag}_{arm}_{label}");
                }
                RaymarchFeature.UseDebugViewOverride = false;
            }
        }
        RaymarchFeature.ShadowMaxDistanceOverrideM = baseClamp;
        L($"  golden clamp A/B (task 118): away + side, {fsr} / {none} / sunvis, full ({(baseClamp >= 0f ? baseClamp + " m" : "inherit " + RaymarchFeature.LastTierBoundsM[2].ToString("F0") + " m")}) vs {m:F0} m");
    }

    /// Task 124: temporal sun-shadow reuse (see the header). Every arm runs once per period, every
    /// period from the same state, so a still that differs from _reuse1 is the reuse's own lag.
    private IEnumerator LookReuseAB(string n, Camera cam, ChunkStore store, Vector3 eye, float pitch, float2 away, string none, int m)
    {
        int basePeriod = RaymarchFeature.ShadowReusePeriod;
        var periods = new List<int> { 1 };
        foreach (int p in new[] { basePeriod, RaymarchFeature.ValidReusePeriod(m) }) if (!periods.Contains(p)) periods.Add(p);
        float2 side = new float2(away.y, -away.x);
        float h0 = SunCycle.Hours;
        float dh = 24f / (120f * 30f);           // a 120 s day at 30 fps: 0.1 deg of hour angle a frame
        Quaternion Face(float2 h) => Quaternion.Euler(-pitch, math.degrees(math.atan2(h.x, h.y)), 0f);
        string[] arms = { none, "sunvis" };
        void Arm(string arm)
        {
            ApplyCompareArm(none);
            cam.ResetProjectionMatrix();
            RaymarchFeature.UseDebugViewOverride = arm == "sunvis";
            RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
        }

        // STILL
        foreach (var (tag, h) in new[] { ("away", away), ("side", side) })
        {
            cam.transform.position = eye; cam.transform.rotation = Face(h);
            foreach (string arm in arms)
            {
                Arm(arm);
                foreach (int p in periods)
                {
                    RaymarchFeature.ShadowReusePeriod = p;
                    yield return Frames(60);
                    yield return CaptureTo($"{n}_golden_{tag}_{arm}_reuse{p}");
                }
            }
        }

        // SUN: 60 settle frames at h0 - 60 dh, then 60 frames moving to h0, captured on the last.
        cam.transform.position = eye; cam.transform.rotation = Face(side);
        foreach (string arm in arms)
        {
            Arm(arm);
            foreach (int p in periods)
            {
                RaymarchFeature.ShadowReusePeriod = p;
                SunCycle.SetHours(h0 - 60f * dh);
                yield return Frames(60);
                for (int i = 1; i <= 60; i++) { SunCycle.SetHours(h0 - 60f * dh + i * dh); yield return null; }
                yield return CaptureTo($"{n}_golden_sun_{arm}_reuse{p}");
            }
        }
        SunCycle.SetHours(h0);

        // EDIT: a stone pillar 10 m ahead on the side heading; the voxels it replaces are restored after each period.
        var edits = FindAnyObjectByType<Playground>()?.DebugEdits;
        Vector3 fwd = new Vector3(side.x, 0f, side.y);
        Vector3 at = eye + fwd * 10f;
        float g = StoreGroundM(store, at.x, at.z, eye.y + 15f);
        if (edits == null || float.IsNaN(g))
            L($"  reuse A/B (task 124): EDIT skipped ({(edits == null ? "no EditService" : "no open ground 10 m ahead")})");
        else
        {
            int3 lo = CoordMath.WorldToVoxel(new float3(at.x, g, at.z)) + new int3(-5, 0, -5), hi = lo + new int3(9, 39, 9);
            var saved = new List<(int3 v, byte m)>(4000);
            foreach (string arm in arms)
            {
                Arm(arm);
                foreach (int p in periods)
                {
                    RaymarchFeature.ShadowReusePeriod = p;
                    yield return Frames(60);
                    saved.Clear();
                    for (int y = lo.y; y <= hi.y; y++)
                        for (int z = lo.z; z <= hi.z; z++)
                            for (int x = lo.x; x <= hi.x; x++) { var v = new int3(x, y, z); saved.Add((v, store.GetVoxel(v))); }
                    int placed = edits.SetBox(lo, hi, Materials.Stone);
                    int f0 = Time.frameCount;
                    for (int k = 1; k <= 8; k++) yield return CaptureTo($"{n}_golden_edit_{arm}_reuse{p}_f{k}");
                    if (arm == none && p == 1) L($"  reuse EDIT: pillar {lo}..{hi} ({placed} voxels set, ground {g:F1} m), 8 shots over frames {f0}-{Time.frameCount}");
                    foreach (var (v, mat) in saved) edits.TrySetVoxel(v, mat);
                }
            }
            yield return Frames(60);
        }
        RaymarchFeature.UseDebugViewOverride = false;
        RaymarchFeature.ShadowReusePeriod = basePeriod;
        L($"  golden reuse A/B (task 124): periods {string.Join("/", periods)} (launch {basePeriod}); still away + side, sun side ({dh * 60f:F2} h over 60 frames " +
          $"to {h0:F2} h, elevation {SunCycle.ElevationDegreesAt(h0 - 60f * dh):F1} -> {SunCycle.ElevationDegreesAt(h0):F1} deg), edit side; {none} / sunvis");
    }

    /// Task 121: the sky look, new vs old (_old: SkyLook and SkyHaze off), 60 frames each, at noon
    /// (the pose's own heading), golden hour 17 h (sun behind = away, and toward it), sunset (sun 2 deg
    /// up) and dusk (3 deg down), both toward the sun. The pose's pitch, +3 deg toward the sun.
    private IEnumerator LookSkyAB(string n, Camera cam, Vector3 eye, Quaternion rot, string fsr, float noon)
    {
        bool sky = RaymarchFeature.SkyLook, haze = RaymarchFeature.SkyHaze;
        float pitch = -Mathf.DeltaAngle(0f, rot.eulerAngles.x);
        float golden = 17f, sunset = SunCycle.HoursForElevation(2f, false), dusk = SunCycle.HoursForElevation(-3f, false);
        ApplyCompareArm(fsr);
        cam.ResetProjectionMatrix();
        var times = new (string tag, float h, int dir)[] { ("noon", noon, 0), ("golden_away", golden, -1), ("golden_toward", golden, 1),
                                                          ("sunset_toward", sunset, 1), ("dusk_toward", dusk, 1) };
        foreach (var (tag, h, dir) in times)
        {
            SunCycle.SetHours(h);
            float3 s = SunCycle.DirectionAt(h);
            float2 toSun = math.normalizesafe(new float2(s.x, s.z));
            cam.transform.position = eye;
            cam.transform.rotation = dir == 0 ? rot
                : Quaternion.Euler(-(pitch + (dir > 0 ? 3f : 0f)), math.degrees(math.atan2(dir * toSun.x, dir * toSun.y)), 0f);
            foreach (bool old in new[] { false, true })
            {
                RaymarchFeature.SkyLook = sky && !old; RaymarchFeature.SkyHaze = haze && !old;
                yield return Frames(60);
                yield return CaptureTo($"{n}_sky_{tag}" + (old ? "_old" : ""));
            }
            RaymarchFeature.SkyLook = sky; RaymarchFeature.SkyHaze = haze;
            L($"  sky (task 121) {tag}: {h:F2} h, sun elevation {SunCycle.ElevationDegreesAt(h):F1} deg, rim {SunCycle.LookSkyRimAt(h)}");
        }
        SunCycle.SetHours(noon);
        cam.transform.rotation = rot;
    }

    /// Task 125: the sea/sky line. Noon at four headings + golden hour away / toward, edge fade on vs off.
    private IEnumerator LookEdgeAB(string n, Camera cam, Vector3 eye, Quaternion rot, string fsr, float noon)
    {
        bool edgeFade = RaymarchFeature.EdgeFade;
        float pitch = -Mathf.DeltaAngle(0f, rot.eulerAngles.x);
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        float wM = src != null ? src.Spec.worldSizeVoxels * 0.1f : 0f, seaM = src != null ? src.Spec.seaLevelY * 0.1f : 0f;
        const float bandM = 440f * 128f * 0.1f;   // Raymarch.compute FAR_USE_RADIUS * (8 << (FAR_LEVELS - 1)) voxels
        const float golden = 17f;
        float3 gs = SunCycle.DirectionAt(golden);
        float2 toSun = math.normalizesafe(new float2(gs.x, gs.z));
        ApplyCompareArm(fsr);
        cam.ResetProjectionMatrix();
        var arms = new (string tag, float h, float2 head)[] {
            ("px", noon, new float2(1f, 0f)), ("nx", noon, new float2(-1f, 0f)), ("pz", noon, new float2(0f, 1f)),
            ("diag", noon, math.normalize(new float2(1f, 1f))), ("golden_away", golden, -toSun), ("golden_toward", golden, toSun) };
        foreach (var (tag, h, head) in arms)
        {
            SunCycle.SetHours(h);
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.Euler(-pitch, math.degrees(math.atan2(head.x, head.y)), 0f);
            foreach (bool old in new[] { false, true })
            {
                RaymarchFeature.EdgeFade = edgeFade && !old;
                yield return Frames(60);
                yield return CaptureTo($"{n}_edge_{tag}" + (old ? "_old" : ""));
            }
            RaymarchFeature.EdgeFade = edgeFade;
            // Where a flat sea would end along this heading: the world's rectangle [0, wM]^2 vs the far band's square.
            float dWorld = float.MaxValue;
            if (head.x > 1e-4f) dWorld = math.min(dWorld, (wM - eye.x) / head.x); else if (head.x < -1e-4f) dWorld = math.min(dWorld, eye.x / -head.x);
            if (head.y > 1e-4f) dWorld = math.min(dWorld, (wM - eye.z) / head.y); else if (head.y < -1e-4f) dWorld = math.min(dWorld, eye.z / -head.y);
            float dBand = bandM / math.max(math.abs(head.x), math.abs(head.y));
            float f = 540f / math.tan(math.radians(cam.fieldOfView * 0.5f));
            string Row(float d) => d <= 0f || d == float.MaxValue ? "-" : $"{540f + f * math.tan(math.atan((eye.y - seaM) / d) - math.radians(-pitch)):F0}";
            L($"  edge (task 125) {tag}: {h:F2} h, heading ({head.x:F2}, {head.y:F2}); a flat sea {eye.y - seaM:F0} m below ends at the world's edge " +
              $"{dWorld:F0} m (row {Row(dWorld)} of 1080) / the far band's square {dBand:F0} m (row {Row(dBand)})");
        }
        SunCycle.SetHours(noon);
        cam.transform.rotation = rot;
    }

    /// Task 120: <name>, then (if old) <name>_old with the cube look off after 60 settle frames.
    private IEnumerator CaptureCubeAB(string name, bool old)
    {
        yield return CaptureTo(name);
        if (!old) yield break;
        bool b = RaymarchFeature.CubeBounce, f = RaymarchFeature.CubeFilter, c = RaymarchFeature.CellHash;
        RaymarchFeature.CubeBounce = RaymarchFeature.CubeFilter = RaymarchFeature.CellHash = false;
        yield return Frames(60);
        yield return CaptureTo(name + "_old");
        RaymarchFeature.CubeBounce = b; RaymarchFeature.CubeFilter = f; RaymarchFeature.CellHash = c;
        yield return Frames(60);
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
