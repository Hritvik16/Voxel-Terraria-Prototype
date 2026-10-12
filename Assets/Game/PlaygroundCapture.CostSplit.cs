// Assets/Game/PlaygroundCapture.CostSplit.cs
//
// -upscalerclips -costsplit (with -world tg2; run-cost-split.sh): TASK 57,
// engine plan step 7 (frame-time milestone 1). Where do a play frame's ms go?
// One launch, task 51's harness (fullscreen 1920x1080, quality preset, far
// shadows, noon, clock paused):
//   PATHS   walk_near + walk_open (see CostSplit); each an 8 s piece of MODE=walk's circle (radius 150 m round the start
//           pose, 5 m/s, eye 1.6 m over the generated ground, level look along
//           the tangent), started where the tangent faces AWAY from the sun so
//           the sun is behind the camera. Played forward and back by REAL time
//           (Time.unscaledDeltaTime). Streaming PINNED at its middle pose
//           (Phase4Bootstrapper.DebugStreamCentre), as task 51: nothing streams
//           while timing.
//   FULL    play's frame: FSR 3 pinned at 540 rows, play HUD + minimap shown,
//           fluid simulating, every pass on.
//   ARMS    ONE thing off each, all through existing static switches:
//             sun_off    RaymarchFeature.ShadowQualityLevel = 3 (voxel AND
//                        far-band sun shadows; the rung "off")
//             torch_off  RaymarchFeature.TorchShadowsOverride = 0 (task 46 rays)
//             far_off    FarField.RenderEnabled = false (march only; the ring
//                        keeps updating)
//             tiers_off  RaymarchFeature.UseLODCascade = false (tier 0 only,
//                        rays end at MaxRayDistance; the CPU tier builds keep
//                        running)
//             fluid_off  Playground.DebugSuspendFluidSim = true
//             ups_none   UpscalerSelect none at 540 rows (gate 960x540, plain blit)
//             hud_off    Playground.DebugHideUi + minimap hidden
//           plus tiny_gate, a DIAGNOSTIC: no upscaler, 256x144 march -- the
//           CPU + fixed-cost floor (cpuMainThreadFrameTime tracks wall time on
//           Metal and its present wait reads 0, so it cannot say CPU vs GPU).
//           Order F, then per arm X X F (each X pair bracketed by two FULL
//           runs = ABBA); each run 3 s settle + CostTimed s timed, no capture.
//           saved = mean(bracketing FULL p50) - X p50 (both X passes pooled).
//   CPU     FrameTimingManager per frame: cpuMainThreadFrameTime, its present
//           wait, cpuRenderThreadFrameTime, gpuFrameTime (INFLATED ~2.6x on this
//           Mac, Amendment 8.10 -- relative only).
//   STILLS  one per arm (first pass, before its timing window) into <run>/stills/.
//   TASK 59 (capture-only flags, run-cost-split.sh EXTRA=):
//     -costarms a,b,..  only these arms (repeats allowed: "roof_on,roof_on" =
//                       F X X F X X F); default the eight above
//     -costsun DEG      sun DEG above the horizon, evening side
//                       (SunCycle.HoursForElevation) instead of noon
//     roof_on           an arm that turns the shadow roof skip ON
//                       (RaymarchFeature.ShadowRoofSkip); FULL keeps the launch's
//                       value (OFF unless -roofskip 1), so saved = OFF - ON
//     roof_build        DIAGNOSTIC: the pyramid is built every frame but the
//                       march ignores it (RaymarchFeature.DebugRoofBuildOnly):
//                       the build's own cost
//   TASK 61 (engine step 8b): halfres  shadow rung 1's 2x2 quad reuse at ALL
//                       distances (ShadowQualityLevel 1, Rung1DistanceOverrideM
//                       ~0): one lane of each quad traces the sun ray
//                       (step A: no edge test)
//                     half  the shipped switch: RaymarchFeature.ShadowHalfRes
//                       (edge-aware half-res at all distances, rung unchanged);
//                       FULL keeps the launch's value (`-shadowres`)
//   TASK 75: see_on  see-through liquids ON (RaymarchFeature.SeeThroughLiquids);
//                       FULL keeps the launch's value (OFF unless -seethrough 1)
//            -costpaths lake  two still poses instead of the walk arcs: lake_shore
//                       (over a map lake, sun behind) and lake_away (same eye, facing the sun)
//   TASK 75b: see_noblend / see_nofar / see_nomarch  DIAGNOSTIC: see_on minus one part
//                       (RaymarchFeature.DebugSeeSkip): the voxel end blend, the far band's
//                       blend, the march through the fluid (the blend runs on the surface)
//                       (the candidates' own arms -- step cap 64, cut 5%, coarser tier by path --
//                       were measured once, Logs/costsplit_2026-10-11_224010.txt, and removed)
//            heat / see_heat / see_tier  DIAGNOSTIC stills: step heat off / on, LOD tier view on
//            -costpaths lake also times lake_wall: task 75's first pose (seed 42, fixed
//                       eye), which faced a wall of water (~60% of the frame) in 75's run
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VoxelEngine.Rendering;
using VoxelEngine.Streaming;

public partial class PlaygroundCapture
{
    private const float CostSettle = 3f;
    private static readonly string[] CostArms = { "sun_off", "torch_off", "far_off", "tiers_off", "fluid_off", "ups_none", "hud_off", "tiny_gate" };

    private sealed class CostRun
    {
        public string arm; public int pass;
        public readonly List<float> ms = new List<float>(1024);
        public readonly List<float> cpuMain = new List<float>(1024), presentWait = new List<float>(1024),
                                    render = new List<float>(1024), gpu = new List<float>(1024);
        public int deficitFrames, farBusyFrames;
    }

    private IEnumerator CostSplit(string root, Playground pg, FarField far, Camera cam, StreamManager sm, ChunkStore store,
                                  UniversalAdditionalCameraData acd)
    {
        float timed = ArgFloat("-costtimed", 20f);
        float sunDeg = ArgFloat("-costsun", -1f);
        float hours = sunDeg > 0f ? SunCycle.HoursForElevation(sunDeg, morning: false) : SunCycle.NoonHours;
        string armsArg = ArgValue("-costarms");
        string[] costArms = string.IsNullOrEmpty(armsArg) ? CostArms : armsArg.Split(',');
        SunCycle.SetHours(hours);
        float3 sun = SunCycle.DirectionAt(hours);
        int baseShadow = RaymarchFeature.ShadowQualityLevel, baseTorch = RaymarchFeature.TorchShadowsOverride;
        L("");
        L($"COST SPLIT (task 57): {hours:F2} h, sun elevation {SunCycle.ElevationDegreesAt(hours):F0} deg; FULL = fsr3 540 rows pinned, " +
          $"HUD + minimap on, fluid on; roof skip {(RaymarchFeature.ShadowRoofSkip ? "ON" : "OFF")}; shadow res {(RaymarchFeature.ShadowHalfRes ? "HALF" : "full")}; shadow rung {baseShadow}, torch rays override {baseTorch} (config {LightingConfig.Active.torchShadowRays}), " +
          $"temporal shadows {(RaymarchFeature.TemporalShadows ? "ON" : "OFF")}, far shadows {(far.ShadowsOn ? "ON" : "OFF")}, cascade {RaymarchFeature.UseLODCascade}");
        L($"order: F, then per arm X X F; each run {CostSettle:F0} s settle + {timed:F0} s timed, real time, no capture");

        // ---- the paths: 8 s arcs of MODE=walk's circle ----
        // walk_near: the arc whose heading is straight away from the sun (the
        //   first smoke run's only path; it faces a canyon wall 10-30 m away,
        //   the cost task 51 measured on walk_cliff).
        // walk_open: the one of 16 points on the circle whose level view is the
        //   most open (median free distance of 5 level rays, -30..+30 deg, over
        //   the generated ground), sun behind or beside (heading . -sun >= -0.2).
        // The two bracket what a walk sees; MODE=walk's whole circle mixes them.
        const float radius = 150f, speed = 5f;
        Vector3 centre = cam.transform.position;
        float2 sh = math.normalizesafe(new float2(sun.x, sun.z));
        float aNear = math.atan2(sh.x, -sh.y);          // tangent (-sin a, cos a) = -sun
        float aOpen = 0f, bestOpen = -1f;
        var cands = new StringBuilder();
        for (int k = 0; k < 16; k++)
        {
            float a = k * math.PI / 8f;
            float2 head = new float2(-math.sin(a), math.cos(a));
            if (math.dot(head, -sh) < -0.2f) continue;
            Vector3 e = centre + new Vector3(math.cos(a), 0f, math.sin(a)) * radius;
            e.y = WalkEyeY(e.x, e.z, e.y);
            var d = new List<float>();
            for (int j = -2; j <= 2; j++)
            {
                float yaw = math.atan2(head.x, head.y) + math.radians(15f * j);
                d.Add(CostFreeDistanceM(e, new float2(math.sin(yaw), math.cos(yaw)), 1500f));
            }
            float open = Pct(d, 0.5f);
            cands.Append($"{math.degrees(a):F0}:{open:F0}m ");
            if (open > bestOpen) { bestOpen = open; aOpen = a; }
        }
        L($"walk_open: arc start candidates (angle on the circle: median free distance): {cands}-> {math.degrees(aOpen):F0} deg");

        string stills = Path.Combine(root, "stills");
        Directory.CreateDirectory(stills);
        var tables = new StringBuilder();
        // TASK 75: -costpaths lake -- two STILL poses instead of the walk arcs: a
        // lake shore looking out over the water (sun behind), and the same spot
        // turned to face the sun (no water in view; check its still).
        var pathList = new List<ClipPath>();
        if (ArgValue("-costpaths") == "lake") pathList.AddRange(CostLakePaths(centre, sun, hours));
        else
        foreach (var (pname0, a0) in new[] { ("walk_near", aNear), ("walk_open", aOpen) })
        {
            var path = new ClipPath { name = pname0, hours = hours, moving = true, lead = 0,
                                      pos = new Vector3[(int)(RatePathSeconds * 30f) + 1],
                                      rot = new Quaternion[(int)(RatePathSeconds * 30f) + 1] };
            for (int f = 0; f < path.pos.Length; f++)
            {
                float a = a0 + speed * (f / 30f) / radius;
                Vector3 p = centre + new Vector3(math.cos(a), 0f, math.sin(a)) * radius;
                p.y = WalkEyeY(p.x, p.z, p.y);
                path.pos[f] = p;
                path.rot[f] = Quaternion.LookRotation(new Vector3(-math.sin(a), 0f, math.cos(a)), Vector3.up);
            }
            path.note = $"MODE=walk circle centre {centre:F0} r {radius:F0} m, arc from {math.degrees(a0):F0} deg, {speed:F0} m/s, " +
                        $"heading yaw {path.rot[0].eulerAngles.y:F0} -> {path.rot[path.rot.Length - 1].eulerAngles.y:F0} (sun yaw {math.degrees(math.atan2(sun.x, sun.z)):F0})";
            pathList.Add(path);
        }
        foreach (var path in pathList)
        {
            string pname = path.name;
            L("");
            L($"== {pname}: {path.note}");
            RatePose(path, RatePathSeconds * 0.5f, out Vector3 mid, out _);
            Phase4Bootstrapper.DebugStreamCentre = mid;
            cam.transform.position = mid; cam.transform.rotation = path.rot[0];
            yield return NearSettle(far, sm);
            L($"  streaming pinned at the middle pose {mid:F1}: resident {store.ResidentCount}, deficit {sm.LoadDeficit()}");

            var runs = new List<CostRun>();
            var order = new List<string> { "full" };
            foreach (string a in costArms) { order.Add(a); order.Add(a); order.Add("full"); }
            var seen = new Dictionary<string, int>();
            foreach (string arm in order)
            {
                int pass = seen.TryGetValue(arm, out int n) ? n : 0;
                seen[arm] = pass + 1;
                var r = new CostRun { arm = arm, pass = pass };
                yield return CostTime(path, r, timed, pass == 0 ? Path.Combine(stills, $"{pname}_{arm}.png") : null, cam, acd, sm, far);
                runs.Add(r);
                L($"  {runs.Count,2} {arm,-9} pass {pass + 1}  frames {r.ms.Count,4}  p50 {Pct(r.ms, 0.5f),5:F1}  p99 {Pct(r.ms, 0.99f),6:F1} ms  " +
                  $"cpu main p50 {Pct(r.cpuMain, 0.5f),5:F1} (present wait {Pct(r.presentWait, 0.5f),5:F1})  render thr {Pct(r.render, 0.5f),5:F1}  " +
                  $"gpu* {Pct(r.gpu, 0.5f),5:F1}  deficit frames {r.deficitFrames}  far busy {r.farBusyFrames}  resident {store.ResidentCount}");
                File.WriteAllText(Path.Combine(root, "costsplit.txt"), _log.ToString());
            }
            CostApply("full");
            Phase4Bootstrapper.DebugStreamCentre = null;

            // ---- the table ----
            var fulls = runs.FindAll(x => x.arm == "full");
            var fp50 = fulls.ConvertAll(x => Pct(x.ms, 0.5f));
            float fMin = float.MaxValue, fMax = float.MinValue;
            foreach (float v in fp50) { fMin = math.min(fMin, v); fMax = math.max(fMax, v); }
            var allFull = new List<float>(); foreach (var x in fulls) allFull.AddRange(x.ms);
            var t = tables;
            t.AppendLine($"{pname}");
            t.AppendLine("arm        p50    p99 ms   pass1/pass2 p50   FULL before/after p50   saved p50 ms   noise (|FULL b - a|)   cpu main p50 / present wait   gpu* p50");
            t.AppendLine($"full     {Pct(allFull, 0.5f),6:F1} {Pct(allFull, 0.99f),6:F1}    {fulls.Count} runs: {string.Join(" ", fp50.ConvertAll(v => v.ToString("F1")))}   " +
                         $"(range {fMax - fMin:F1})   cpu {Pct(Pool(fulls, x => x.cpuMain), 0.5f):F1} / {Pct(Pool(fulls, x => x.presentWait), 0.5f):F1}   gpu* {Pct(Pool(fulls, x => x.gpu), 0.5f):F1}");
            for (int i = 0; i < costArms.Length; i++)
            {
                string arm = costArms[i];
                CostRun x1 = runs[1 + 3 * i], x2 = runs[2 + 3 * i], before = runs[3 * i], after = runs[3 + 3 * i];
                var both = new List<float>(x1.ms); both.AddRange(x2.ms);
                float fb = Pct(before.ms, 0.5f), fa = Pct(after.ms, 0.5f), xp = Pct(both, 0.5f);
                var xs = new List<CostRun> { x1, x2 };
                t.AppendLine($"{arm,-9}{xp,6:F1} {Pct(both, 0.99f),6:F1}    {Pct(x1.ms, 0.5f),6:F1} / {Pct(x2.ms, 0.5f),-6:F1}    {fb,6:F1} / {fa,-6:F1}          " +
                             $"{0.5f * (fb + fa) - xp,7:+0.0;-0.0}        {math.abs(fb - fa),5:F1}                 " +
                             $"{Pct(Pool(xs, x => x.cpuMain), 0.5f),5:F1} / {Pct(Pool(xs, x => x.presentWait), 0.5f),5:F1}          {Pct(Pool(xs, x => x.gpu), 0.5f),5:F1}");
            }
            t.AppendLine();
        }
        L("");
        L("COST SPLIT TABLES (gpu* = FrameTimingManager gpuFrameTime, inflated ~2.6x on this Mac: relative only)");
        L(tables.ToString());
        File.WriteAllText(Path.Combine(root, "costsplit.txt"), _log.ToString());
    }

    /// Metres along a level ray from the eye before the generated ground
    /// (WalkEyeY's column top) rises above it; maxM if never.
    private static float CostFreeDistanceM(Vector3 eye, float2 dir, float maxM)
    {
        for (float t = 4f; t < maxM; t += 4f)
        {
            float x = eye.x + dir.x * t, z = eye.z + dir.y * t;
            if (WalkEyeY(x, z, -1e9f) - 1.6f > eye.y) return t;
        }
        return maxM;
    }

    private static List<float> Pool(List<CostRun> rs, System.Func<CostRun, List<float>> f)
    {
        var a = new List<float>(); foreach (var r in rs) a.AddRange(f(r)); return a;
    }

    /// FULL (play's frame at 540 rows) with exactly one thing switched off.
    private static void CostApply(string arm)
    {
        UpscalerSelect.Apply(arm == "ups_none" || arm == "tiny_gate" ? UpscalerSelect.Kind.None : UpscalerSelect.Kind.Fsr3, 540, false);
        // tiny_gate: a DIAGNOSTIC, not one thing off -- no upscaler and a 256x144
        // march (7% of 540's pixels): what is left is the CPU + fixed-cost floor
        if (arm == "tiny_gate") RaymarchFeature.CustomGateResolution = new Vector2Int(256, 144);
        RaymarchFeature.ShadowQualityLevel = arm == "sun_off" ? 3 : arm == "halfres" ? 1 : _costBaseShadow;
        RaymarchFeature.Rung1DistanceOverrideM = arm == "halfres" ? 0.001f : _costBaseRung1;
        RaymarchFeature.TorchShadowsOverride = arm == "torch_off" ? 0 : _costBaseTorch;
        RaymarchFeature.ShadowRoofSkip = arm == "roof_on" || _costBaseRoof;
        RaymarchFeature.DebugRoofBuildOnly = arm == "roof_build";
        RaymarchFeature.ShadowHalfRes = arm == "half" || _costBaseHalf;
        FarField.RenderEnabled = arm != "far_off";
        RaymarchFeature.UseLODCascade = arm != "tiers_off";
        // Task 75: see_on turns see-through liquids ON; FULL keeps the launch's
        // value (OFF unless -seethrough 1), so saved = OFF - ON (negative = its cost).
        RaymarchFeature.SeeThroughLiquids = arm.StartsWith("see_") || _costBaseSee;
        // Task 75b: see_on minus one part (diagnostic; FULL and every other arm skip nothing).
        RaymarchFeature.DebugSeeSkip = arm == "see_noblend" ? 1 : arm == "see_nofar" ? 2 : arm == "see_nomarch" ? 4 : 0;
        // Task 75b: heat / see_heat show the step-count heat view (stills only; their ms are not frames
        // of the product), see_tier the LOD tier view with see-through on.
        RaymarchFeature.UseDebugViewOverride = arm == "heat" || arm == "see_heat" || arm == "see_tier";
        RaymarchFeature.DebugViewOverride = arm == "see_tier" ? RaymarchFeature.DebugMode.LODTier : RaymarchFeature.DebugMode.StepHeat;
        Playground.DebugSuspendFluidSim = arm == "fluid_off";
        Playground.DebugHideUi = arm == "hud_off";
        Minimap.Visible = arm != "hud_off";
        Minimap.ForceShow = arm != "hud_off";      // capture runs hide it otherwise
    }

    private static int _costBaseShadow = -1, _costBaseTorch = -1;
    private static bool _costBaseRoof;
    private static float _costBaseRung1;
    private static bool _costBaseHalf, _costBaseSee;

    /// Task 75: the lake poses, at -liquiddemo's lake (Playground.FindDemoLake:
    /// the nearest map LAKE, 40 m across, >= 3 m deep) and its edge toward the
    /// sun. The eye stands 3 m past the edge, 1.6 m over the ground or the water
    /// level, whichever is higher (a generated lake can stand above the land next
    /// to it; the first try put the eye under such a wall). lake_shore looks back
    /// over the water (sun behind), 8 deg down; lake_away is the same eye facing
    /// the sun, level.
    private List<ClipPath> CostLakePaths(Vector3 centre, float3 sun, float hours)
    {
        var list = new List<ClipPath>();
        float2 toSun = math.normalizesafe(new float2(sun.x, sun.z));
        if (!Playground.FindDemoLake(new int2((int)(centre.x * 10f), (int)(centre.z * 10f)), toSun, out float2 lake, out float2 edge, out int waterTop))
        { L("  -costpaths lake: no lake (or not a tg2 world)"); return list; }
        float2 shore = edge + toSun * 30f;
        var eye = new Vector3(shore.x * 0.1f, 0f, shore.y * 0.1f);
        eye.y = math.max(WalkEyeY(eye.x, eye.z, centre.y), (waterTop + 1) * 0.1f + 1.6f);
        var look = new Vector3(-toSun.x, 0f, -toSun.y);
        foreach (var (name, rot) in new[] { ("lake_shore", Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(8f, 0f, 0f)),
                                            ("lake_away", Quaternion.LookRotation(-look, Vector3.up)) })
        {
            int n = (int)(RatePathSeconds * 30f) + 1;
            var path = new ClipPath { name = name, hours = hours, moving = false, lead = 0, pos = new Vector3[n], rot = new Quaternion[n] };
            for (int f = 0; f < n; f++) { path.pos[f] = eye; path.rot[f] = rot; }
            path.note = $"still pose, eye {eye:F1} (1.6 m over the ground), lake point {lake * 0.1f} m, " +
                        $"lake centre {math.length(shore - lake) * 0.1f:F0} m away, water level {(waterTop + 1) * 0.1f:F1} m, yaw {rot.eulerAngles.y:F0}, pitch {rot.eulerAngles.x:F0}";
            list.Add(path);
        }
        // Task 75b: task 75's first lake_shore pose (Logs/costsplit_2026-10-11_135318.txt),
        // fixed: the eye stood under a lake standing above the land and faced its wall.
        {
            var eyeW = new Vector3(4145.2f, 653.1f, 4303.0f);
            var rotW = Quaternion.Euler(8f, 180f, 0f);
            int n = (int)(RatePathSeconds * 30f) + 1;
            var path = new ClipPath { name = "lake_wall", hours = hours, moving = false, lead = 0, pos = new Vector3[n], rot = new Quaternion[n] };
            for (int f = 0; f < n; f++) { path.pos[f] = eyeW; path.rot[f] = rotW; }
            path.note = $"still pose, eye {eyeW:F1} (task 75's first pose, fixed; seed 42), yaw 180, pitch 8";
            list.Add(path);
        }
        return list;
    }

    /// One timed run: settle at pose 0 with the arm applied, history reset, a
    /// still (first pass only), then the path by real time; the frames after
    /// CostSettle s are kept. Fluid off again afterwards (the waits).
    private IEnumerator CostTime(ClipPath path, CostRun r, float timed, string still, Camera cam,
                                 UniversalAdditionalCameraData acd, StreamManager sm, FarField far)
    {
        if (_costBaseShadow < 0) { _costBaseShadow = RaymarchFeature.ShadowQualityLevel; _costBaseTorch = RaymarchFeature.TorchShadowsOverride; _costBaseRoof = RaymarchFeature.ShadowRoofSkip; _costBaseRung1 = RaymarchFeature.Rung1DistanceOverrideM; _costBaseHalf = RaymarchFeature.ShadowHalfRes; _costBaseSee = RaymarchFeature.SeeThroughLiquids; }
        cam.transform.position = path.pos[0]; cam.transform.rotation = path.rot[0];
        Playground.DebugSuspendFluidSim = true;
        yield return NearSettle(far, sm);
        CostApply(r.arm);
        acd.resetHistory = true;
        yield return Frames(30);
        if (still != null)
        {
            ScreenCapture.CaptureScreenshot(still); yield return Frames(2);
            // Task 61: the same frame without HUD or minimap (owner stills),
            // then the arm's own HUD state back.
            bool hideUi = Playground.DebugHideUi, mapOn = Minimap.Visible;
            Playground.DebugHideUi = true; Minimap.Visible = false;
            yield return Frames(3);
            ScreenCapture.CaptureScreenshot(still.Replace(".png", "_clean.png")); yield return Frames(2);
            Playground.DebugHideUi = hideUi; Minimap.Visible = mapOn;
            // Task 61: the sun-visibility view at the same pose (the half-res
            // edge check diffs these between arms), then back to the beauty.
            RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
            RaymarchFeature.UseDebugViewOverride = true;
            yield return Frames(10);
            ScreenCapture.CaptureScreenshot(still.Replace(".png", "_sunvis.png")); yield return Frames(2);
            RaymarchFeature.UseDebugViewOverride = false;
            acd.resetHistory = true;
            yield return Frames(10);
        }
        var ft = new FrameTiming[1];
        float t = 0f;
        while (t < CostSettle + timed)
        {
            float s = t % (2f * RatePathSeconds);
            RatePose(path, s < RatePathSeconds ? s : 2f * RatePathSeconds - s, out Vector3 p, out Quaternion q);
            cam.transform.SetPositionAndRotation(p, q);
            yield return null;
            float dt = Time.unscaledDeltaTime;
            t += dt;
            FrameTimingManager.CaptureFrameTimings();
            if (t <= CostSettle) continue;
            r.ms.Add(dt * 1000f);
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
            {
                r.cpuMain.Add((float)ft[0].cpuMainThreadFrameTime);
                r.presentWait.Add((float)ft[0].cpuMainThreadPresentWaitTime);
                r.render.Add((float)ft[0].cpuRenderThreadFrameTime);
                r.gpu.Add((float)ft[0].gpuFrameTime);
            }
            if (sm.LoadDeficit() > 0) r.deficitFrames++;
            if (far.Busy) r.farBusyFrames++;
        }
        Playground.DebugSuspendFluidSim = true;
    }
}
