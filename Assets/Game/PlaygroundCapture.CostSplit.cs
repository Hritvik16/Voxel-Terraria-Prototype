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
//   FULL    play's frame: FSR 3 pinned at 540 rows (task 112: -renderrows N
//           pins N instead, clamped to UpscalerSelect's 540-720; "dyn" or no
//           flag = 540, so older arms are unchanged), play HUD + minimap shown,
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
//             ups_none   UpscalerSelect none at the same rows (540: gate 960x540, plain blit)
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
//   TASK 168: the pyramid build is incremental (RaymarchFeature.RoofInc.cs) only with -roofinc 1; roof_on
//                       draws CSMainFarShadeLeanRoof (diet 2 + roof skip; run line `kernel diet2+roof`).
//     roof_full         roof skip ON with the FULL rebuild every frame (task 59's build)
//   TASK 168c: sundefer_on / sundefer_off  the sun-ray shell marches handed to CSSunDeferLean (RaymarchFeature.SunDefer;
//                       FULL keeps the launch's -sundefer); run line `kernel diet2+sun`. -costsdpair (with -coststillraw):
//                       raw / raw2, then _raw_sdflip (flipped, 10 frames later) and _raw_sdback; tools/roof168.py pair <stills> sd
//     roof_off          roof skip OFF (for a launch with -roofskip 1)
//     -costroofpair     with -coststillraw: raw / raw2 (as launched), then _raw_roofflip (roof skip flipped,
//                       10 frames later) and _raw_roofback (flipped back): the identity check without the
//                       minute between two arms' stills (the sea is simulated fluid). tools/roof168.py pair <stills>
//                       Each roof run line is followed by the pyramid counters (full / incremental / idle,
//                       -roofverify's checks and bad words).
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
//   TASK 93b: shells_off / fshade_off arms and the -shells 1 fill wait + tint still
//            (PlaygroundCapture.ShellsPerf.cs).
//   TASK 93b3 (diagnostic): farprim_off  the far march runs 0 steps (FarField.PrimaryStepCap 0: no far
//                       hit, so no far shadow ray), residency still bound -- unlike far_off, whose dummy
//                       FarResident also cuts the shells' tier-0 hand-over (ShellTier0Holds/Missing)
//            fshadow_off  far sun-shadow rays off (FarField.ShadowsOn false)
//            tiers_farprim  shells_off + farprim_off (tiers 1-2 drawn, no far march)
//   TASK 93b4 (diagnostic): shellshadow_off  the shell-hit sun-shadow march skipped (sunVis 1)
//            shellmarch_off  the primary shell march AND the far march after it skipped (misses draw
//                       sky): with shellshadow_off, what the shell march costs and what is left over
//                       (RaymarchFeature.DebugShellSkip; far-shading kernel only, so -farshade 1)
//            shellcache_off  ShellMarch's one-entry resolve / page caches off (the pre-fix reads; same image)
//   TASK 93b5 (diagnostic): shellmask_off  ShellMarch's air-brick mask off (the pre-93b5 iterations; same
//                       image); the counters line adds the empty groups crossed in one leap (ShadowStats 60)
//                       (= the shell march as before 93b5: mask off keeps the old leap)
//            shellleap_new  mask off but the exit-plane leap (ShellLeap, which the mask turns on): what the
//                       leap itself costs; its raw stills = FULL's (exact) and differ from shellmask_off's
//                       by the new t's LSBs
//            (the mask needs -shellmask 1: FULL then = mask on)
//   TASK 99: skyexit_on / skyexit_off  the shells' sky-ray exit on / off (RaymarchFeature.ShellSkyExit;
//                       FULL keeps the launch's -shellskyexit, ON by default); same image either way (exact); the counters
//                       line adds SKY rays (no shell or far hit) and their shell iterations (ShadowStats 62-64)
//   TASK 116: colleap_on / colleap_off  the shells' column sky leap on / off (RaymarchFeature.ShellColumnLeap;
//                       FULL keeps the launch's -shellcolleap, OFF by default); the counters line adds the column
//                       leaps (ShadowStats 65-66); raw stills ON vs OFF are the identity check
//   TASK 132: prepass_on / prepass_off  the shell pre-pass on / off (RaymarchFeature.ShellPrepass; FULL keeps the
//                       launch's -shellprepass, ON by default since task 132); same image either way (exact): raw stills ON vs OFF are the identity check
//   TASK 133: nearsh_on / nearsh_off  near (tier-0) sun rays through the shells on / off (RaymarchFeature.NearShadowShells;
//                       FULL keeps the launch's -nearshadowshells); the [nearsh] counters line gives the rays handed on,
//                       their shell iterations and how many the shells blocked (ShadowStats 77-79)
//   TASK 134: diet_on / diet_off  the lean play kernels / their *Diag twins, the code before task 134
//                       (RaymarchFeature.ShellKernelDiet; FULL keeps the launch's -shelldiet, ON by default); exact: raw
//                       stills ON vs OFF are the identity check. The run line says which kernel drew (lean / diag).
//   TASK 137: holeleap_on / holeleap_off  the shells' hole leap on / off (RaymarchFeature.ShellHoleLeap; FULL keeps the
//                       launch's -holeleap, OFF by default); exact: raw stills ON vs OFF are the identity check. The [nearsh]
//                       line adds the near rays' tier-0 chunk leaps and how many ran inside the hole box (ShadowStats 80-81)
//   TASK 153: heightbound_on / heightbound_off  the shells' column height leap on / off (RaymarchFeature.ShellHeightBound;
//                       FULL keeps the launch's -shellheightbound, OFF by default); shadow rays only, raw stills ON vs OFF
//                       are the identity check; the [nearsh] line's shell iterations per near ray show what it cut.
//   TASK 161: diet2_on / diet2_off  the diet-2 far-shading kernels (VE_DIET2) / task 134's lean kernels (RaymarchFeature.KernelDiet2;
//                       FULL keeps the launch's -diet2); the run line ends `kernel diet2` when they drew (arms that leave a
//                       folded setting, e.g. roof_on / shells_off / reuse arms that change nothing folded, say which ran).
//                       skyprobe_off: the vertical sky probe (UseSkyProbeAmbient) off -- capture only, a look change.
//   TASK 138: skytilt_on / skytilt2 / skytilt_off  the tilted sky probe 1 / 2 (side faces only) / 0 (RaymarchFeature.SkyProbeTilt;
//                       FULL keeps the launch's -skytilt). A LOOK change: the stills differ by design.
//   TASK 118: clampN (e.g. clamp128)  the voxel sun-shadow ray (TraceShadowRay) clamped at N m
//                       (RaymarchFeature.ShadowMaxDistanceOverrideM; FULL keeps the launch's -shadowclamp,
//                       inherit = the primary ray's 290 m); the counters line adds that ray's traced rays and
//                       steps (ShadowStats 2 / 4). Shell-hit and far-band shadows have no distance clamp.
//   TASK 124: reuseN (e.g. reuse8)  temporal sun-shadow reuse, a tile traces one frame in N
//                       (RaymarchFeature.ShadowReusePeriod; FULL keeps the launch's -shadowreuse, 4 by
//                       default); the [sun] counters line shows the period and the rays traced.
//   TASK 130 (diagnostic, PERF_SHELL_MARCH_PLAN E1-E3): shellsplit  shellmarch_off's skip, and the primary pass hands
//                       each pixel's shell-march input (tTier0) to CSShellProbe, a small kernel that runs the same
//                       ShellMarch alone after it (RaymarchFeature.DebugShellSkip 2 + 32); shellsplit - shellmarch_off =
//                       the march outside the big kernel. shellsplit2  the probe marches every pixel twice, the second
//                       on warm caches (+ 64). Misses draw sky in both: timing only. The counters line of these arms
//                       comes from the probe (same rays: its iterations must equal FULL's), plus a [simd] line (E3):
//                       per 32-lane SIMD (8x4 half-tile), efficiency sum / (32 x max) of shell iterations, ShadowStats 67-76
//   TASK 151: -costpaths torch  night, 32 torches round a still pose; arms tl_K_I_R (the three C.11 knobs) and
//                       tl_none (PlaygroundCapture.TorchCost.cs)
//            -coststillraw  each arm's first still also shoots _raw.png / _raw2.png (10 frames apart) with no
//                       upscaler (no jitter), for byte-identity checks between arms
//            the counters line also gives shell march iterations by ray kind (ShadowStats 48-55)
//            every arm's first-pass still also logs the far counters (ShadowStats 5-7, 40-47, 30
//            frames, before the timing window) and shoots the far step heat, the far-start view
//            and the shell march heat (_farheat / _farstart / _shellheat.png).
//   TASK 168b (30 fps ceiling check): stacked arms skysun_off (sky probe + sun), sunshells_off (sun + shells),
//            ceiling (sky probe + sun + shells; FSR 3 kept). Every run line and table row also gives p95 and
//            the share of frames > 33 ms (the owner's E4 tail criterion).
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
        bool torchPaths = ArgValue("-costpaths") == "torch";            // task 151: night, 32 torches
        if (torchPaths) hours = ArgFloat("-nighttod", 0.5f);
        string armsArg = ArgValue("-costarms");
        string[] costArms = string.IsNullOrEmpty(armsArg) ? CostArms : armsArg.Split(',');
        SunCycle.SetHours(hours);
        float3 sun = SunCycle.DirectionAt(hours);
        int baseShadow = RaymarchFeature.ShadowQualityLevel, baseTorch = RaymarchFeature.TorchShadowsOverride;
        L("");
        L($"COST SPLIT (task 57): {hours:F2} h, sun elevation {SunCycle.ElevationDegreesAt(hours):F0} deg; FULL = fsr3 {CostRows} rows pinned, " +
          $"HUD + minimap on, fluid on; roof skip {(RaymarchFeature.ShadowRoofSkip ? "ON" : "OFF")}; shadow res {(RaymarchFeature.ShadowHalfRes ? "HALF" : "full")}; shadow rung {baseShadow}, torch rays override {baseTorch} (config {LightingConfig.Active.torchShadowRays}), " +
          $"temporal shadows {(RaymarchFeature.TemporalShadows ? $"ON (1 in {RaymarchFeature.ShadowReusePeriod})" : "OFF")}, far shadows {(far.ShadowsOn ? "ON" : "OFF")}, cascade {RaymarchFeature.UseLODCascade}");
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
        else if (torchPaths) yield return CostTorchScene(centre, hours, far, sm, cam, pathList);
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
            // Task 93b: with -shells 1, let the shells fill here first (PlaygroundCapture.ShellsPerf.cs).
            yield return CostShellWait(path, cam, Path.Combine(stills, $"{pname}_shelltint.png"));
            if (torchPaths && ArgInt("-tfl3", 0) != 0)   // task 165: the incremental torch flood probe INSTEAD of the timed arms
            {
                yield return TorchFloodL3(cam, stills);
                File.WriteAllText(Path.Combine(root, "costsplit.txt"), _log.ToString());
                yield break;
            }

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
                L($"  {runs.Count,2} {arm,-9} pass {pass + 1}  frames {r.ms.Count,4}  p50 {Pct(r.ms, 0.5f),5:F1}  p95 {Pct(r.ms, 0.95f),6:F1}  p99 {Pct(r.ms, 0.99f),6:F1} ms  >33ms {Over33(r.ms),5:F1}%  " +
                  $"cpu main p50 {Pct(r.cpuMain, 0.5f),5:F1} (present wait {Pct(r.presentWait, 0.5f),5:F1})  render thr {Pct(r.render, 0.5f),5:F1}  " +
                  $"gpu* {Pct(r.gpu, 0.5f),5:F1}  deficit frames {r.deficitFrames}  far busy {r.farBusyFrames}  resident {store.ResidentCount}  " +
                  $"kernel {(RaymarchFeature.ShellDiagDrawnLastFrame ? "diag" : RaymarchFeature.Diet2RoofDrawnLastFrame ? "diet2+roof" : RaymarchFeature.SunDeferDrawnLastFrame ? "diet2+sun" : RaymarchFeature.Diet2DrawnLastFrame ? "diet2" : "lean")}");
                if (RaymarchFeature.ShadowRoofSkip || RaymarchFeature.DebugRoofBuildOnly) L($"     {RaymarchFeature.RoofIncSummary()}");   // task 168
                File.WriteAllText(Path.Combine(root, "costsplit.txt"), _log.ToString());
            }
            CostApply("full");
            // Task 151: lava stills under each torch arm's knobs (PlaygroundCapture.TorchCost.cs).
            if (torchPaths) { var la = new List<string> { "full" }; foreach (string a in costArms) if (a.StartsWith("tl_") && a != "tl_none") la.Add(a); yield return CostTorchLava(cam, stills, la.ToArray()); }
            Phase4Bootstrapper.DebugStreamCentre = null;

            // ---- the table ----
            var fulls = runs.FindAll(x => x.arm == "full");
            var fp50 = fulls.ConvertAll(x => Pct(x.ms, 0.5f));
            float fMin = float.MaxValue, fMax = float.MinValue;
            foreach (float v in fp50) { fMin = math.min(fMin, v); fMax = math.max(fMax, v); }
            var allFull = new List<float>(); foreach (var x in fulls) allFull.AddRange(x.ms);
            var t = tables;
            t.AppendLine($"{pname}");
            t.AppendLine("arm        p50    p95    p99 ms  >33ms%   pass1/pass2 p50   FULL before/after p50   saved p50 ms   noise (|FULL b - a|)   cpu main p50 / present wait   gpu* p50");
            t.AppendLine($"full     {Pct(allFull, 0.5f),6:F1} {Pct(allFull, 0.95f),6:F1} {Pct(allFull, 0.99f),6:F1}  {Over33(allFull),6:F1}    {fulls.Count} runs: {string.Join(" ", fp50.ConvertAll(v => v.ToString("F1")))}   " +
                         $"(range {fMax - fMin:F1})   cpu {Pct(Pool(fulls, x => x.cpuMain), 0.5f):F1} / {Pct(Pool(fulls, x => x.presentWait), 0.5f):F1}   gpu* {Pct(Pool(fulls, x => x.gpu), 0.5f):F1}");
            for (int i = 0; i < costArms.Length; i++)
            {
                string arm = costArms[i];
                CostRun x1 = runs[1 + 3 * i], x2 = runs[2 + 3 * i], before = runs[3 * i], after = runs[3 + 3 * i];
                var both = new List<float>(x1.ms); both.AddRange(x2.ms);
                float fb = Pct(before.ms, 0.5f), fa = Pct(after.ms, 0.5f), xp = Pct(both, 0.5f);
                var xs = new List<CostRun> { x1, x2 };
                t.AppendLine($"{arm,-9}{xp,6:F1} {Pct(both, 0.95f),6:F1} {Pct(both, 0.99f),6:F1}  {Over33(both),6:F1}    {Pct(x1.ms, 0.5f),6:F1} / {Pct(x2.ms, 0.5f),-6:F1}    {fb,6:F1} / {fa,-6:F1}          " +
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

    /// Task 168b: the share of frames over 33 ms (the owner's E4 tail criterion), in %.
    private static float Over33(List<float> ms) => ms.Count == 0 ? 0f : 100f * ms.FindAll(x => x > 33f).Count / ms.Count;

    private static List<float> Pool(List<CostRun> rs, System.Func<CostRun, List<float>> f)
    {
        var a = new List<float>(); foreach (var r in rs) a.AddRange(f(r)); return a;
    }

    /// Task 112: the rows every arm is pinned at. `-renderrows N` (last match, like
    /// every Arg*) clamped as UpscalerSelect.Apply does (task 161: from CaptureMinRows, so 432 is
    /// reachable); "dyn", a bad value or no flag = 540, the value every arm before task 112 ran at.
    private static readonly int CostRows =
        int.TryParse(ArgValue("-renderrows"), out int rows) ? Mathf.Clamp(rows, UpscalerSelect.CaptureMinRows, UpscalerSelect.MaxRows) : 540;

    /// FULL (play's frame at CostRows rows) with exactly one thing switched off.
    private static void CostApply(string arm)
    {
        UpscalerSelect.Apply(arm == "ups_none" || arm == "tiny_gate" ? UpscalerSelect.Kind.None : UpscalerSelect.Kind.Fsr3, CostRows, false);
        // tiny_gate: a DIAGNOSTIC, not one thing off -- no upscaler and a 256x144
        // march (7% of 540's pixels): what is left is the CPU + fixed-cost floor
        if (arm == "tiny_gate") RaymarchFeature.CustomGateResolution = new Vector2Int(256, 144);
        RaymarchFeature.ShadowQualityLevel = (arm == "sun_off" || arm == "skysun_off" || arm == "sunshells_off" || arm == "ceiling") ? 3 : arm == "halfres" ? 1 : _costBaseShadow;
        RaymarchFeature.Rung1DistanceOverrideM = arm == "halfres" ? 0.001f : _costBaseRung1;
        RaymarchFeature.TorchShadowsOverride = arm == "torch_off" ? 0 : _costBaseTorch;
        RaymarchFeature.ShadowRoofSkip = arm == "roof_on" || arm == "roof_full" || (arm != "roof_off" && _costBaseRoof);
        RaymarchFeature.RoofIncremental = arm != "roof_full" && _costBaseRoofInc;   // task 168
        RaymarchFeature.SunDefer = arm == "sundefer_on" || (arm != "sundefer_off" && _costBaseSunDefer);   // task 168c
        RaymarchFeature.DebugRoofBuildOnly = arm == "roof_build";
        RaymarchFeature.ShadowHalfRes = arm == "half" || _costBaseHalf;
        FarField.RenderEnabled = arm != "far_off";
        // Task 93b3: farprim_off / fshadow_off / tiers_farprim (see the header).
        if (FarField.Active != null)
        {
            if (_costBaseFarSteps < 0) { _costBaseFarSteps = FarField.Active.PrimaryStepCap; _costBaseFarShadowOn = FarField.Active.ShadowsOn; }
            FarField.Active.PrimaryStepCap = (arm == "farprim_off" || arm == "tiers_farprim") ? 0 : _costBaseFarSteps;
            FarField.Active.ShadowsOn = arm != "fshadow_off" && _costBaseFarShadowOn;
        }
        // Task 93b: shells_off = tiers 1-2 draw instead of the shells (shell streaming keeps
        // running; since task 93d only with -keeptiers 1, else tier 0 + far band); fshade_off = far shading off, FULL keeps the launch's -farshade.
        RaymarchFeature.DrawShells = arm != "shells_off" && arm != "tiers_farprim" && arm != "sunshells_off" && arm != "ceiling";
        RaymarchFeature.FarShade = arm != "fshade_off" && _costBaseFarShade;
        // Task 99: the sky-ray exit (see the header).
        RaymarchFeature.ShellSkyExit = arm == "skyexit_on" || (arm != "skyexit_off" && _costBaseSkyExit);
        // Task 116: the column sky leap (see the header).
        RaymarchFeature.ShellColumnLeap = arm == "colleap_on" || (arm != "colleap_off" && _costBaseColLeap);
        // Task 132: the shell pre-pass (see the header).
        RaymarchFeature.ShellPrepass = arm == "prepass_on" || (arm != "prepass_off" && _costBasePrepass);
        // Task 133: near sun rays through the shells (see the header).
        RaymarchFeature.NearShadowShells = arm == "nearsh_on" || (arm != "nearsh_off" && _costBaseNearSh);
        // Task 134: the kernel diet (see the header).
        RaymarchFeature.ShellKernelDiet = arm == "diet_on" || (arm != "diet_off" && _costBaseDiet);
        // Task 161: kernel diet 2 (diet2_on / diet2_off) and the vertical sky probe off (skyprobe_off; capture only).
        RaymarchFeature.KernelDiet2 = arm == "diet2_on" || (arm != "diet2_off" && _costBaseDiet2);
        RaymarchFeature.UseSkyProbeAmbient = arm != "skyprobe_off" && arm != "skysun_off" && arm != "ceiling" && _costBaseSkyProbe;   // task 168b: stacked arms
        // Task 137: the hole leap (see the header).
        RaymarchFeature.ShellHoleLeap = arm == "holeleap_on" || (arm != "holeleap_off" && _costBaseHoleLeap);
        RaymarchFeature.ShellHeightBound = arm == "heightbound_on" || (arm != "heightbound_off" && _costBaseHeightBound);   // task 153
        // Task 138: the tilted sky probe (see the header).
        RaymarchFeature.SkyProbeTilt = arm == "skytilt_on" ? 1 : arm == "skytilt2" ? 2 : arm == "skytilt_off" ? 0 : _costBaseSkyTilt;
        // Task 93b4 (diagnostic): see the header.
        RaymarchFeature.DebugShellSkip = arm == "shellshadow_off" ? 1 : arm == "shellmarch_off" ? 2 : arm == "shellcache_off" ? 4 : arm == "shellmask_off" ? 8 : arm == "shellleap_new" ? 24 : arm == "shellsplit" ? 34 : arm == "shellsplit2" ? 98 : 0;
        // Task 118: clampN = the voxel sun-shadow clamp at N m (see the header).
        RaymarchFeature.ShadowMaxDistanceOverrideM = CostClampM(arm, out float clampM) ? clampM : _costBaseClamp;
        // Task 124: reuseN = temporal sun-shadow reuse one frame in N (see the header).
        RaymarchFeature.ShadowReusePeriod = CostReuseN(arm, out int reuseN) ? reuseN : _costBaseReuse;
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
        // Task 151: tl_K_I_R / tl_none torch arms (PlaygroundCapture.TorchCost.cs); no-op without -costpaths torch.
        CostTorchApply(arm);
    }

    private static int _costBaseShadow = -1, _costBaseTorch = -1;
    private static bool _costBaseRoof;
    private static bool _costBaseSunDefer;   // task 168c
    private static bool _costBaseRoofInc = true;   // task 168: the launch's -roofinc
    private static float _costBaseRung1;
    private static bool _costBaseHalf, _costBaseSee, _costBaseFarShade, _costBaseSkyExit, _costBaseColLeap, _costBasePrepass, _costBaseNearSh, _costBaseDiet, _costBaseHoleLeap, _costBaseHeightBound, _costBaseDiet2, _costBaseSkyProbe;
    private static int _costBaseSkyTilt;
    private static int _costBaseFarSteps = -1;
    private static bool _costBaseFarShadowOn;
    private static float _costBaseClamp = -1f;
    private static int _costBaseReuse = 4;

    /// Task 124: "reuse8" -> 8 (1, 2, 4, 8 or 16).
    private static bool CostReuseN(string arm, out int n)
    {
        n = 0;
        return arm.StartsWith("reuse") && int.TryParse(arm.Substring(5), out n) && RaymarchFeature.ValidReusePeriod(n) == n;
    }

    /// Task 118: "clamp128" -> 128 m.
    private static bool CostClampM(string arm, out float m)
    {
        m = -1f;
        return arm.StartsWith("clamp") && float.TryParse(arm.Substring(5), System.Globalization.NumberStyles.Float,
                                                         System.Globalization.CultureInfo.InvariantCulture, out m) && m > 0f;
    }

    /// Task 93b3 (diagnostic): 30 frames of the far / shell-end counters at the
    /// still pose (not in any timing window). Counters 40-47 are written by the
    /// far-shading kernel only (CSMainFarShade; -farshade 1).
    private IEnumerator CostFarCounters(string arm)
    {
        if (RaymarchFeature.ShadowStatsBuffer == null) yield break;
        RaymarchFeature.CollectShadowStats = true;
        RaymarchFeature.ResetShadowStats();
        const int n = 30;
        yield return Frames(n);
        uint[] st = RaymarchFeature.ReadShadowStats();
        RaymarchFeature.CollectShadowStats = false;
        if (st == null || st.Length < 48) yield break;
        // Task 118: the voxel sun-shadow rays (TraceShadowRay: ShadowStats 2 traced, 4 their steps) under this arm's clamp.
        double vR = st[2] / (double)n, vS = st[4] / (double)n;
        L($"    [sun {arm}] reuse 1 in {RaymarchFeature.ShadowReusePeriod}{(RaymarchFeature.TemporalShadows ? "" : " (temporal shadows OFF)")}; clamp {(RaymarchFeature.ShadowMaxDistanceOverrideM >= 0f ? RaymarchFeature.ShadowMaxDistanceOverrideM + " m" : "inherit")} (primary ray's {RaymarchFeature.LastTierBoundsM[2]:F0} m); " +
          $"voxel shadow rays traced/frame {vR:N0}, steps/frame {vS:N0} ({(vR > 0 ? vS / vR : 0):F1}/ray)");
        double fr =st[5] / (double)n, prim = st[44] / (double)n, shR = st[45] / (double)n, shS = st[46] / (double)n;
        double fromShells = (st[40] + st[41] + st[42]) / (double)n;
        L($"    [far {arm}] far rays/frame {fr:N0} (from shells: past-last {st[40] / (double)n:N0}, absent {st[41] / (double)n:N0}, cap / sky exit {st[42] / (double)n:N0}; " +
          $"FarStartT {st[43] / (double)n:N0}), far hits/frame {st[7] / (double)n:N0}; PRIMARY steps/frame {prim:N0} ({(fr > 0 ? prim / fr : 0):F1}/ray); " +
          $"SHADOW rays/frame {shR:N0}, steps/frame {shS:N0} ({(shR > 0 ? shS / shR : 0):F1}/ray); mean far start {(fr > 0 ? st[47] / (double)n / fr : 0):F0} m");
        if (st.Length < 56) yield break;
        // Task 93b4: shell march iterations by ray kind, per frame and per ray, and per pixel.
        double px = st[55] / (double)n, hR = st[48] / (double)n, hI = st[49] / (double)n, mR = st[50] / (double)n, mI = st[51] / (double)n;
        double sR = st[52] / (double)n, sI = st[53] / (double)n, l0 = st[54] / (double)n;
        L($"    [shell {arm}] pixels/frame {px:N0}; PRIMARY hit rays {hR:N0} ({(px > 0 ? 100 * hR / px : 0):F1}%) iters {hI:N0} ({(hR > 0 ? hI / hR : 0):F1}/ray); " +
          $"PRIMARY no-hit rays {mR:N0} ({(px > 0 ? 100 * mR / px : 0):F1}%) iters {mI:N0} ({(mR > 0 ? mI / mR : 0):F1}/ray); tier-0 leaps {l0:N0} ({(hI + mI > 0 ? 100 * l0 / (hI + mI) : 0):F0}% of primary iters); " +
          $"SHADOW rays {sR:N0} iters {sI:N0} ({(sR > 0 ? sI / sR : 0):F1}/ray); all shell iters/pixel {(px > 0 ? (hI + mI + sI) / px : 0):F2}");
        if (st.Length < 60) yield break;
        double pi = hI + mI, lc = st[56] / (double)n, lb = st[57] / (double)n, dr = st[58] / (double)n, pr = st[59] / (double)n;
        L($"    [shell {arm}] primary iters by step: tier-0 leap {(pi > 0 ? 100 * l0 / pi : 0):F0}%, air chunk {(pi > 0 ? 100 * lc / pi : 0):F0}%, air brick {(pi > 0 ? 100 * lb / pi : 0):F0}%, " +
          $"cell / end {(pi > 0 ? 100 * (pi - l0 - lc - lb) / pi : 0):F0}%; reads per primary iter: directory {(pi > 0 ? dr / pi : 0):F2}, page {(pi > 0 ? pr / pi : 0):F2}");
        if (st.Length < 62) yield break;
        // Task 93b5: the empty brick groups the air-brick mask crossed in one leap each (primary rays).
        double mg = st[60] / (double)n;
        L($"    [shell {arm}] air-brick mask: empty groups crossed in one leap {mg:N0}/frame ({(pi > 0 ? 100 * mg / pi : 0):F0}% of primary iters)");
        if (st.Length < 65) yield break;
        // Task 99: SKY rays (the shell march ran; no shell hit, no far hit) and the sky-ray exit.
        double kR = st[62] / (double)n, kI = st[63] / (double)n, kX = st[64] / (double)n;
        L($"    [shell {arm}] sky rays {kR:N0}/frame ({(px > 0 ? 100 * kR / px : 0):F1}% of pixels), shell iters {kI:N0} ({(kR > 0 ? kI / kR : 0):F1}/sky ray); " +
          $"ended by the sky exit {kX:N0} ({(kR > 0 ? 100 * kX / kR : 0):F0}%); exit {(RaymarchFeature.ShellSkyExit ? "ON" : "OFF")}, shell top y {(VoxelEngine.Mirror.ShellStreamer.Active != null ? VoxelEngine.Mirror.ShellStreamer.Active.SolidTopY / 10f : float.NaN):F1} m, " +
          $"far max h {(FarField.Active != null ? FarField.Active.MaxHeightVoxels / 10f : float.NaN):F1} m");
        if (st.Length < 67) yield break;
        // Task 116: the column sky leaps (primary rays): one leap each over a group column or a whole chunk.
        double cl = st[65] / (double)n, cw = st[66] / (double)n;
        L($"    [shell {arm}] column sky leaps {cl:N0}/frame ({(pi > 0 ? 100 * cl / pi : 0):F0}% of primary iters), whole chunk {cw:N0}; " +
          $"leap {(RaymarchFeature.ShellColumnLeap ? "ON" : "OFF")}; pre-pass {(RaymarchFeature.ShellPrepassDrawnLastFrame ? "ON" : "OFF")} " +
          $"({RaymarchFeature.ShellPrepassBytes / (1024.0 * 1024.0):F1} MB scratch)");
        if (st.Length >= 80)
        {
            // Task 133: near (tier-0) sun rays that left the window / band ceiling unblocked and went on through the shells.
            double nR = st[77] / (double)n, nI = st[78] / (double)n, nB = st[79] / (double)n;
            L($"    [nearsh {arm}] {(RaymarchFeature.NearShadowShells ? "ON" : "OFF")}: near sun rays handed on to the shells {nR:N0}/frame " +
              $"({(vR > 0 ? 100 * nR / vR : 0):F0}% of traced), shell iters {nI:N0} ({(nR > 0 ? nI / nR : 0):F1}/ray), blocked by the shells {nB:N0} ({(nR > 0 ? 100 * nB / nR : 0):F1}%)");
            if (st.Length >= 82)
            {
                // Task 137: of those iterations, the tier-0 chunk leaps (tier 0's band and the hole) and the ones inside the hole box.
                double nL = st[80] / (double)n, nH = st[81] / (double)n;
                L($"    [nearsh {arm}] tier-0 chunk leaps {nL:N0}/frame ({(nI > 0 ? 100 * nL / nI : 0):F0}% of their iters, {(nR > 0 ? nL / nR : 0):F1}/ray), " +
                  $"of them in the hole box {nH:N0} ({(nL > 0 ? 100 * nH / nL : 0):F0}%); hole leap {(RaymarchFeature.ShellHoleLeap ? "ON" : "OFF")}; " +
                  $"height bound {(RaymarchFeature.ShellHeightBound ? "ON" : "OFF")}");
            }
        }
        if (st.Length < 77 || st[67] == 0) yield break;
        // Task 130 (E3): per 32-lane SIMD that had any shell ray (the probe's, shellsplit arms only).
        double simd = st[67], lanes = st[68], mxs = st[69], act = st[70];
        L($"    [simd {arm}] SIMDs with shell rays {simd / n:N0}/frame; efficiency sum/(32 x max) {lanes / (32.0 * mxs):F3} (iteration-weighted), " +
          $"active lanes/SIMD {act / simd:F1} of 32, efficiency over active lanes {st[71] / simd / 1000.0:F3} (SIMD mean), " +
          $"longest lane {mxs / simd:F1} iters vs mean lane {lanes / act:F1}; step kinds per SIMD {st[72] / simd:F2} of 4 (tier-0 leap, air chunk, air brick, cell), " +
          $"end kinds {st[73] / simd:F2} of 5; SIMDs by efficiency <0.35 {100.0 * st[74] / simd:F0}%, 0.35-0.6 {100.0 * st[75] / simd:F0}%, >=0.6 {100.0 * st[76] / simd:F0}%");
    }

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
        if (_costBaseShadow < 0) { _costBaseShadow = RaymarchFeature.ShadowQualityLevel; _costBaseTorch = RaymarchFeature.TorchShadowsOverride; _costBaseRoof = RaymarchFeature.ShadowRoofSkip; _costBaseSunDefer = RaymarchFeature.SunDefer; _costBaseRoofInc = RaymarchFeature.RoofIncremental; _costBaseRung1 = RaymarchFeature.Rung1DistanceOverrideM; _costBaseHalf = RaymarchFeature.ShadowHalfRes; _costBaseSee = RaymarchFeature.SeeThroughLiquids; _costBaseFarShade = RaymarchFeature.FarShade; _costBaseSkyExit = RaymarchFeature.ShellSkyExit; _costBaseColLeap = RaymarchFeature.ShellColumnLeap; _costBasePrepass = RaymarchFeature.ShellPrepass; _costBaseNearSh = RaymarchFeature.NearShadowShells; _costBaseDiet = RaymarchFeature.ShellKernelDiet; _costBaseHoleLeap = RaymarchFeature.ShellHoleLeap; _costBaseHeightBound = RaymarchFeature.ShellHeightBound; _costBaseDiet2 = RaymarchFeature.KernelDiet2; _costBaseSkyProbe = RaymarchFeature.UseSkyProbeAmbient; _costBaseSkyTilt = RaymarchFeature.SkyProbeTilt; _costBaseClamp = RaymarchFeature.ShadowMaxDistanceOverrideM; _costBaseReuse = RaymarchFeature.ShadowReusePeriod; }
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
            // Task 93b3: far step heat, far-start view, shell march heat; then the counters.
            foreach (var (mode, suffix) in new[] { (RaymarchFeature.DebugMode.FarHeat, "_farheat.png"),
                                                   (RaymarchFeature.DebugMode.FarStart, "_farstart.png"),
                                                   (RaymarchFeature.DebugMode.ShellHeat, "_shellheat.png") })
            {
                RaymarchFeature.DebugViewOverride = mode;
                yield return Frames(6);
                ScreenCapture.CaptureScreenshot(still.Replace(".png", suffix)); yield return Frames(2);
            }
            RaymarchFeature.UseDebugViewOverride = false;
            // Task 93b4: no-upscaler stills (no jitter) for byte-identity between arms.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-coststillraw") >= 0)
            {
                UpscalerSelect.Apply(UpscalerSelect.Kind.None, CostRows, false);
                bool hideUi2 = Playground.DebugHideUi, mapOn2 = Minimap.Visible;
                Playground.DebugHideUi = true; Minimap.Visible = false;
                yield return Frames(20);
                ScreenCapture.CaptureScreenshot(still.Replace(".png", "_raw.png")); yield return Frames(10);
                ScreenCapture.CaptureScreenshot(still.Replace(".png", "_raw2.png")); yield return Frames(2);
                // Task 161: -costd2pair -- diet 2 OFF then ON again, 10 frames apart, right after raw / raw2 (both
                // as launched): raw vs raw2 is the control for anything that changed in between (shells arriving).
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-costd2pair") >= 0)
                {
                    bool d2 = RaymarchFeature.KernelDiet2;
                    foreach (var (on, suffix) in new[] { (!d2, "_raw_d2flip.png"), (d2, "_raw_d2back.png") })
                    {
                        RaymarchFeature.KernelDiet2 = on;
                        yield return Frames(10);
                        ScreenCapture.CaptureScreenshot(still.Replace(".png", suffix)); yield return Frames(2);
                    }
                    L($"  [d2pair] {Path.GetFileName(still)}: raw / raw2 / d2back diet2 {(d2 ? "ON" : "OFF")}, d2flip {(d2 ? "OFF" : "ON")}; last frame kernel {(RaymarchFeature.Diet2DrawnLastFrame ? "diet2" : "lean")}");
                }
                // Task 168: -costroofpair -- the same with the shadow roof skip flipped (raw2 / roofflip / roofback).
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-costroofpair") >= 0)
                {
                    bool rf = RaymarchFeature.ShadowRoofSkip;
                    foreach (var (on, suffix) in new[] { (!rf, "_raw_roofflip.png"), (rf, "_raw_roofback.png") })
                    {
                        RaymarchFeature.ShadowRoofSkip = on;
                        yield return Frames(10);
                        ScreenCapture.CaptureScreenshot(still.Replace(".png", suffix)); yield return Frames(2);
                    }
                    L($"  [roofpair] {Path.GetFileName(still)}: raw / raw2 / roofback roof skip {(rf ? "ON" : "OFF")}, roofflip {(rf ? "OFF" : "ON")}; last frame kernel {(RaymarchFeature.Diet2RoofDrawnLastFrame ? "diet2+roof" : RaymarchFeature.Diet2DrawnLastFrame ? "diet2" : "lean")}");
                }
                // Task 168c: -costsdpair -- the same with the sun defer flipped (raw2 / sdflip / sdback).
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-costsdpair") >= 0)
                {
                    bool sd = RaymarchFeature.SunDefer;
                    var kinds = new List<string>();
                    foreach (var (on, suffix) in new[] { (!sd, "_raw_sdflip.png"), (sd, "_raw_sdback.png") })
                    {
                        RaymarchFeature.SunDefer = on;
                        yield return Frames(10);
                        kinds.Add(RaymarchFeature.SunDeferDrawnLastFrame ? "diet2+sun" : RaymarchFeature.Diet2DrawnLastFrame ? "diet2" : "lean");
                        ScreenCapture.CaptureScreenshot(still.Replace(".png", suffix)); yield return Frames(2);
                    }
                    L($"  [sdpair] {Path.GetFileName(still)}: raw / raw2 / sdback sun defer {(sd ? "ON" : "OFF")}, sdflip {(sd ? "OFF" : "ON")}; kernels flip {kinds[0]}, back {kinds[1]}");
                }
                Playground.DebugHideUi = hideUi2; Minimap.Visible = mapOn2;
                CostApply(r.arm);
            }
            yield return CostFarCounters(r.arm);
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
