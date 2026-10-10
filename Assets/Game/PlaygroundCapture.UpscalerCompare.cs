// Assets/Game/PlaygroundCapture.UpscalerCompare.cs
//
// -upscalercompare (with -world tg2; run-upscaler-compare.sh): TASK 48, the
// side-by-side of every working -upscaler from task 47, so the owner can pick.
// One launch: each pose is streamed in ONCE and every arm is shot there, in
// process, through UpscalerSelect.Apply (the code the flags use).
//   arms   native (1920x1080 raymarch, no AA: the ground truth), taa / fsr1 /
//          stp / fsr3 at 540 and 720 rows, and (stills only) each at dyn.
//          The shimmer pass adds none_540 = the pre-TAA baseline's own path
//          (960x540 gate, bilinear), to check this run against that baseline.
//   SHIMMER  first, before any edit: -nearshimmer's protocol and poses
//          (eye_level_1..3 + slope_walk, noon), per arm: base, four projection
//          nudges of a quarter 960x540 pixel (the SAME screen motion for every
//          arm, = the baseline's), still. Sky mask (debug view 9) and the CPU
//          depth (240x135) once per pose, copied into every arm's folder, so
//          tools/nearshimmer-report.py reads each shimmer/<arm>/ unchanged.
//   STILLS  11:00 unless night: spawn_day (task 47's smoke pose), near_cliff
//          (a cliff foot, -farpreview's search), horizon (eye_level_3 looking
//          at the peak), slope_detail (slope_walk: tg2 has NO vegetation -- the
//          world tree is off -- so the sub-pixel grass slope stands in for a
//          forest edge), fluid_pools (a water and a lava pool dug at
//          eye_level_1, fluid sim suspended so every arm sees the same fluid),
//          night_torches (8 torches round a spot at eye_level_2, 23:00).
// Clock paused, fluid sim suspended for the whole run. A LOOK, not a
// measurement: no frame times are claimed (dyn's smoothed ms is logged only to
// show why it settled where it did).
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
    private static readonly string[] CompareStillArms =
        { "native", "taa_540", "fsr1_540", "stp_540", "fsr3_540", "taa_720", "fsr1_720", "stp_720", "fsr3_720",
          "taa_dyn", "fsr1_dyn", "stp_dyn", "fsr3_dyn" };
    private static readonly string[] CompareShimmerArms =
        { "native", "none_540", "taa_540", "fsr1_540", "stp_540", "fsr3_540", "taa_720", "fsr1_720", "stp_720", "fsr3_720" };

    private IEnumerator UpscalerCompare()
    {
        Screen.SetResolution(1920, 1080, false);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        _demoCaption = null;
        yield return Frames(300);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string root = Path.Combine(Application.persistentDataPath, "UpscalerCompare", ts);
        Directory.CreateDirectory(root);
        _folder = root;
        LightingDriver.ClockPaused = true;
        Playground.DebugHideUi = true;
        Playground.DebugSuspendFluidSim = true;
        var far = FarField.Active;
        var cam = Camera.main;
        var sm = Phase4Bootstrapper.Streamer;
        var store = Phase4Bootstrapper.Store;
        L($"UPSCALER COMPARE -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})  screen {Screen.width}x{Screen.height}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  preset {(HasFlag("-renderpreset") ? RenderPreset.Current.ToString() : "not set")}  " +
          $"temporal shadows {(RaymarchFeature.TemporalShadows ? "ON" : "OFF")}");
        if (far == null || !far.UsesTg2 || !Phase4Bootstrapper.IsTg2World || pg == null)
        {
            L("needs the Playground with -world tg2");
            File.WriteAllText(Path.Combine(root, "upscalercompare.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var map = far.Tg2Map;
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = far.Tg2SeaShift;
        Vector3 E(float3 g) => new Vector3((g.x - off.x) * 0.1f, (g.y - seaShift) * 0.1f, (g.z - off.y) * 0.1f);
        float3 F(Vector3 v) => new float3(v.x, v.y, v.z);
        var eyes = new List<(string n, Vector3 eye, Vector3 at)>();
        foreach (var (n, e, a) in Tg2EyeLevelPoses(map)) eyes.Add((n, E(e), E(a)));

        // ---- slope_walk: -nearshimmer's first candidate the voxels confirm ----
        var shimmerPoses = new List<(string, Vector3, Quaternion)>();
        foreach (var (n, e, a) in eyes) shimmerPoses.Add((n, e, Quaternion.LookRotation((a - e).normalized, Vector3.up)));
        var cands = NearSlopeCandidates(map, 8);
        (Vector3 eye, Quaternion rot)? slope = null;
        for (int ci = 0; ci < cands.Count && slope == null; ci++)
        {
            var (ce, ca, cNote) = cands[ci];
            Vector3 eye = E(ce), at = E(ca);
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
            yield return NearSettle(far, sm);
            float g0 = StoreGroundM(store, eye.x, eye.z, eye.y + 10f), g1 = StoreGroundM(store, at.x, at.z, at.y + 30f);
            if (float.IsNaN(g0) || float.IsNaN(g1)) { L($"slope candidate {ci} rejected: no open ground ({cNote})"); continue; }
            eye.y = g0 + 1.7f; at.y = g1 + 1f;
            var rot = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
            float dc = NearDepthM(store, F(eye), F(rot * Vector3.forward), NearShimmerMaxDepthM);
            if (dc < 30f || dc > 80f) { L($"slope candidate {ci} rejected: centre ray {dc:F1} m ({cNote})"); continue; }
            L($"slope_walk = candidate {ci}: {cNote}; centre ray {dc:F1} m");
            slope = (eye, rot);
        }
        if (slope != null) shimmerPoses.Add(("slope_walk", slope.Value.eye, slope.Value.rot));
        else L("slope_walk: no candidate confirmed -- skipped (shimmer and the slope_detail still)");

        // ================= SHIMMER (no edits yet) =================
        SunCycle.SetHours(12f);
        L("");
        L($"SHIMMER: noon; nudge = a quarter 960x540 pixel in the projection for every arm; settle {NearShimmerSettle} frames per nudge, 90 per arm");
        const int dw = 960 / NearShimmerDepthStep, dh = 540 / NearShimmerDepthStep;
        var depth = new float[dw * dh];
        var nudges = new (string n, float dx, float dy)[] { ("xP", 0.25f, 0f), ("xM", -0.25f, 0f), ("yP", 0f, 0.25f), ("yM", 0f, -0.25f) };
        foreach (var (n, pos, rot) in shimmerPoses)
        {
            ApplyCompareArm("native");
            cam.transform.position = pos; cam.transform.rotation = rot;
            cam.ResetProjectionMatrix();
            yield return NearSettle(far, sm);
            bool inAir = store.GetVoxel(CoordMath.WorldToVoxel(F(pos))) == 0;
            // sky mask once per pose (debug view 9, native: the report's sky colours)
            _folder = Path.Combine(root, "shimmer", "_shared");
            Directory.CreateDirectory(_folder);
            RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.FarBand;
            RaymarchFeature.UseDebugViewOverride = true;
            yield return Frames(6);
            yield return CaptureTo($"{n}_band");
            RaymarchFeature.UseDebugViewOverride = false;
            float3 o = F(pos);
            for (int r = 0; r < dh; r++)
            {
                for (int x = 0; x < dw; x++)
                {
                    Ray ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / dw, 1f - (r + 0.5f) / dh, 0f));
                    depth[r * dw + x] = NearDepthM(store, o, F(ray.direction), NearShimmerMaxDepthM);
                }
                if ((r & 7) == 7) yield return null;
            }
            byte[] depthBytes = new byte[depth.Length * 4];
            Buffer.BlockCopy(depth, 0, depthBytes, 0, depthBytes.Length);
            byte[] bandBytes = File.ReadAllBytes(Path.Combine(_folder, $"{n}_band.png"));
            L($"{n,-12} eye {pos}  {(inAir ? "in air" : "INSIDE ROCK")}  deficit {(sm != null ? sm.LoadDeficit() : -1)}");

            foreach (string arm in CompareShimmerArms)
            {
                _folder = Path.Combine(root, "shimmer", arm);
                Directory.CreateDirectory(_folder);
                File.WriteAllBytes(Path.Combine(_folder, $"{n}_depth.f32"), depthBytes);
                File.WriteAllBytes(Path.Combine(_folder, $"{n}_band.png"), bandBytes);
                ApplyCompareArm(arm);
                cam.ResetProjectionMatrix();
                int f0 = Time.frameCount, d0 = RaymarchFeature.PrimaryDispatchCount;
                yield return Frames(90);
                yield return CaptureTo($"{n}_base");
                Matrix4x4 p0 = cam.projectionMatrix;
                foreach (var (dn, dx, dy) in nudges)
                {
                    Matrix4x4 p = p0;
                    p.m02 += 2f * dx / 960f;
                    p.m12 += 2f * dy / 540f;
                    cam.projectionMatrix = p;
                    yield return Frames(NearShimmerSettle);
                    yield return CaptureTo($"{n}_{dn}");
                }
                cam.ResetProjectionMatrix();
                yield return Frames(NearShimmerSettle);
                yield return CaptureTo($"{n}_still");
                var d = RaymarchFeature.LastDispatchResolution;
                L($"  {arm,-10} dispatch {d.x}x{d.y}  traced {RaymarchFeature.PrimaryDispatchCount - d0}/{Time.frameCount - f0} frames");
            }
        }

        // ================= STILLS =================
        _folder = Path.Combine(root, "stills");
        Directory.CreateDirectory(_folder);
        L("");
        L($"STILLS (1920x1080; mean RGB; dyn = 240 frames from 540 rows, target {UpscalerSelect.DynTargetMs} ms)");
        var stillPoses = new List<string>();

        // spawn_day: task 47's smoke pose
        SunCycle.SetHours(11f);
        ApplyCompareArm("native");
        SetPose(cam, Phase4Bootstrapper.Tg2Spawn(sm.TerrainGenSource), new Vector3(8f, 30f, 0f));
        yield return NearSettle(far, sm);
        yield return CompareStillArmsAt("spawn_day", stillPoses);

        // near_cliff: the first cliff foot (-farpreview's search), eye checked on the voxels
        if (FindCliffFoot(map, out float3 cfEye, out float3 cfAt))
        {
            Vector3 eye = E(cfEye), at = E(cfAt);
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
            yield return NearSettle(far, sm);
            float g = StoreGroundM(store, eye.x, eye.z, eye.y + 10f);
            if (!float.IsNaN(g)) eye.y = g + 1.7f;
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
            yield return Frames(60);
            L($"near_cliff: eye {eye:F1} (store ground {(float.IsNaN(g) ? "NOT FOUND" : g.ToString("F1"))}), looking at {at:F1}, " +
              $"centre ray {NearDepthM(store, F(eye), F(cam.transform.forward), NearShimmerMaxDepthM):F1} m");
            yield return CompareStillArmsAt("near_cliff", stillPoses);
        }
        else L("near_cliff: no cliff foot found -- skipped");

        // horizon: eye_level_3 looking at the peak
        {
            var (n, e, a) = eyes[eyes.Count - 1];
            cam.transform.position = e;
            cam.transform.rotation = Quaternion.LookRotation((a - e).normalized, Vector3.up);
            yield return NearSettle(far, sm);
            L($"horizon = {n}: eye {e:F1}");
            yield return CompareStillArmsAt("horizon", stillPoses);
        }

        // slope_detail: slope_walk at 11:00
        if (slope != null)
        {
            cam.transform.position = slope.Value.eye; cam.transform.rotation = slope.Value.rot;
            yield return NearSettle(far, sm);
            yield return CompareStillArmsAt("slope_detail", stillPoses);
        }

        // fluid_pools: water and lava pools dug at eye_level_1 (sim suspended)
        {
            var (_, e, a) = eyes[0];
            Vector3 fwd = a - e; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            cam.transform.position = e;
            cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            yield return NearSettle(far, sm);
            float gE = StoreGroundM(store, e.x, e.z, e.y + 10f);
            if (!float.IsNaN(gE)) e.y = gE + 1.7f;
            Vector3 c = e + fwd * 9f;
            float gC = StoreGroundM(store, c.x, c.z, e.y + 15f);
            c.y = float.IsNaN(gC) ? e.y - 1.7f : gC;
            var pools = new (string m, byte mat, Vector3 at)[] { ("water", Materials.Water, c - right * 2.2f), ("lava", Materials.Lava, c + right * 2.2f) };
            foreach (var (m, mat, at) in pools)
            {
                int3 pc = CoordMath.WorldToVoxel(F(at));
                int dug = pg.DebugEdits.SetBox(pc + new int3(-14, -8, -14), pc + new int3(14, 30, 14), Materials.Air);
                int fill = pg.DebugEdits.SetBox(pc + new int3(-14, -8, -14), pc + new int3(14, -3, 14), mat);
                L($"fluid_pools: {m} pool at {pc}: {dug} voxels cleared, {fill} {m}");
            }
            cam.transform.position = e;
            cam.transform.rotation = Quaternion.LookRotation((c + Vector3.down * 0.5f - e).normalized, Vector3.up);
            yield return Frames(120);
            yield return CompareStillArmsAt("fluid_pools", stillPoses);
        }

        // night_torches: 8 torches in a 3 m ring at eye_level_2, 23:00
        {
            var (_, e, a) = eyes[Math.Min(1, eyes.Count - 1)];
            Vector3 fwd = a - e; fwd.y = 0f; fwd.Normalize();
            cam.transform.position = e;
            cam.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            yield return NearSettle(far, sm);
            float gE = StoreGroundM(store, e.x, e.z, e.y + 10f);
            if (!float.IsNaN(gE)) e.y = gE + 1.7f;
            Vector3 c = e + fwd * 10f;
            float gC = StoreGroundM(store, c.x, c.z, e.y + 15f);
            c.y = float.IsNaN(gC) ? e.y - 1.7f : gC;
            int placed = 0;
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI / 4f;
                Vector3 p = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 3f + Vector3.up * 2f;
                if (pg.DebugEdits.TrySetVoxel(FloorAt(store, CoordMath.WorldToVoxel(F(p))), Materials.Torch)) placed++;
            }
            SunCycle.SetHours(23f);
            cam.transform.position = e;
            cam.transform.rotation = Quaternion.LookRotation((c - e).normalized, Vector3.up);
            L($"night_torches: {placed}/8 torches round {c:F1}, 23:00");
            yield return Frames(120);
            yield return CompareStillArmsAt("night_torches", stillPoses);
        }

        L("");
        L($"still poses: {string.Join(" ", stillPoses)}");
        L($"FSR 3: {RaymarchFeature.Fsr3Status}");
        File.WriteAllText(Path.Combine(root, "upscalercompare.txt"), _log.ToString());
        Debug.Log("[upscalercompare]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// One pose, every still arm: settle the temporal history, shoot, log.
    private IEnumerator CompareStillArmsAt(string pose, List<string> done)
    {
        done.Add(pose);
        foreach (string arm in CompareStillArms)
        {
            ApplyCompareArm(arm);
            int f0 = Time.frameCount, d0 = RaymarchFeature.PrimaryDispatchCount;
            yield return Frames(arm.EndsWith("_dyn") ? 240 : 90);
            yield return new WaitForEndOfFrame();
            Color32[] px = Grab(out Texture2D tex);
            File.WriteAllBytes(Path.Combine(_folder, $"{pose}__{arm}.png"), tex.EncodeToPNG());
            Destroy(tex);
            double r = 0, g = 0, b = 0;
            foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
            int nPx = Math.Max(1, px.Length);
            var d = RaymarchFeature.LastDispatchResolution;
            string dyn = UpscalerSelect.Dynamic
                ? $"  dyn settled {UpscalerSelect.Rows} rows ({UpscalerSelect.DynChanges} steps, smoothed {UpscalerSelect.DynSmoothedMs:F0} ms)" : "";
            L($"{pose,-14}{arm,-10} dispatch {d.x}x{d.y}  traced {RaymarchFeature.PrimaryDispatchCount - d0}/{Time.frameCount - f0}  " +
              $"meanRGB ({r / nPx:F0},{g / nPx:F0},{b / nPx:F0}){dyn}");
        }
    }

    /// "native" = the raymarch at the full 1920x1080 target, no AA, no post;
    /// otherwise "<upscaler>_<540|720|dyn>" through UpscalerSelect.Apply.
    private static void ApplyCompareArm(string arm)
    {
        if (arm == "native")
        {
            UpscalerSelect.Apply(UpscalerSelect.Kind.None, UpscalerSelect.MaxRows, false);
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.Native;
            RaymarchFeature.Taa = false;
            return;
        }
        int us = arm.IndexOf('_');
        UpscalerSelect.TryParseKind(arm.Substring(0, us), out var k);
        string rows = arm.Substring(us + 1);
        UpscalerSelect.Apply(k, rows == "dyn" ? UpscalerSelect.MinRows : int.Parse(rows), rows == "dyn");
    }

    /// The first cliff foot of -farpreview's search (tg2 voxels: eye 1.7 m over
    /// the exact ground, look-at 70% up the cliff). Not an iterator (map.View).
    private static bool FindCliffFoot(TerrainGen.WorldMap map, out float3 eye, out float3 at)
    {
        var feet = FindCliffFeet(map, 1);
        eye = feet.Count > 0 ? feet[0].eye : float3.zero;
        at = feet.Count > 0 ? feet[0].at : float3.zero;
        return feet.Count > 0;
    }

    /// The first `max` cliff feet of -farpreview's search, in its scan order.
    private static List<(float3 eye, float3 at)> FindCliffFeet(TerrainGen.WorldMap map, int max)
    {
        var feet = new List<(float3, float3)>();
        var spec = map.Spec;
        var o = TerrainGen.Offsets.For(spec);
        int nC = spec.mapCells, cellV = spec.mapCellVoxels;
        for (int gz = nC / 4; gz < 3 * nC / 4; gz += 5)
        for (int gx = nC / 4; gx < 3 * nC / 4; gx += 5)
        {
            int i0 = gz * nC + gx;
            float h0 = map.Height[i0];
            if (h0 < 300f || (map.Water != null && map.Water[i0] > h0)) continue;
            float bh = h0; int bx = gx, bz = gz;
            for (int dz = -7; dz <= 7; dz++)
            for (int dx = -7; dx <= 7; dx++)
            {
                float h = map.Height[(gz + dz) * nC + gx + dx];
                if (h > bh) { bh = h; bx = gx + dx; bz = gz + dz; }
            }
            if (bh - h0 < 600f) continue;
            float2 sp = new float2((gx + 0.5f) * cellV, (gz + 0.5f) * cellV);
            var exact = TerrainGen.ShapeField.Sample(in spec, map.View, in o, sp.x, sp.y, 1);
            float g = math.max(math.max(exact.height, exact.water), spec.seaLevelY) + 1;
            feet.Add((new float3(sp.x, g + 17f, sp.y),
                      new float3((bx + 0.5f) * cellV, spec.seaLevelY + h0 + 0.7f * (bh - h0), (bz + 0.5f) * cellV)));
            if (feet.Count >= max) return feet;
        }
        return feet;
    }
}
