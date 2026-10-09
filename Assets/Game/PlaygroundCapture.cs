// Assets/Game/PlaygroundCapture.cs
//
// First-pass screenshot set from the Playground scene. Runs only with
// -playgroundshots, so the scene stays a plain flyable dogfood scene otherwise.
//
// This captures a LOOK, not a measurement. Nothing here is instrumented and
// nothing it produces is evidence about correctness, scale, or performance.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

public partial class PlaygroundCapture : MonoBehaviour
{
    [SerializeField] private string _outputRootFolderName = "PlaygroundShots";
    private readonly StringBuilder _log = new StringBuilder();
    private string _folder;
    private int _shots;

    /// -gputrace: put the CA under REAL, SUSTAINED load and then stay running,
    /// so an external profiler (Instruments "Metal System Trace", driven by
    /// tools/capture-gpu-trace.sh) records a window with all three fluids
    /// active. Same gate condition the screenshot pass uses --
    /// DebugOpenAllVents -- so what is measured is what was looked at.
    ///
    /// THIS MODE NEVER QUITS. xctrace's --time-limit ends the process. If you
    /// run the build with -gputrace by hand, you have to kill it yourself.
    ///
    /// It captures NOTHING itself and reports NO numbers: all timing comes from
    /// the trace. Nothing in this file is a measurement.
    private IEnumerator GpuTraceLoad()
    {
        // Force one encoder per kernel so a capture can tell them apart. This
        // PERTURBS timing (8 command-buffer submissions per tick instead of 1)
        // and is why -gputrace numbers are ratios between kernels, never a
        // budget. See FluidGpuSimulation.SplitDispatchEncodersForCapture.
        VoxelEngine.Simulation.FluidGpuSimulation.SplitDispatchEncodersForCapture = true;

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        // Let streaming settle first, so the recorded window is fluid + raymarch
        // work and not a window fill.
        for (int i = 0; i < 240; i++) yield return null;

        if (pg != null)
        {
            pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
            yield return null;
        }

        Debug.Log("[PlaygroundCapture] -gputrace: arena primed, vents cycling, running until killed.");

        // Vent budgets are finite (raised 2026-09-11 to water 60,000 / sand
        // 20,000 / lava 12,000, emitted as a cube per frame rather than one
        // voxel) and can still drain or back up partway through a 15 s window,
        // leaving the tail of the recording measuring an idle CA. Re-opening
        // them keeps all three materials genuinely in flight for the capture.
        while (true)
        {
            if (pg != null) pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
            for (int i = 0; i < 180; i++) yield return null;   // ~3 s at 60 fps
        }
    }

    // =====================================================================
    // §13 PHASE 5B PERFORMANCE GATE -- wall-clock substitute methodology
    // =====================================================================
    // Same family as RaymarchAutoBenchmark and PHASE_2_COMPLITION's table:
    // RELEASE standalone, wall clock, driftcheck-validated. NOT Instruments --
    // per-kernel GPU attribution was tried and is a confirmed dead end on this
    // toolchain (Unity merges the CA's dispatches into one Metal encoder), and
    // a development build distorts what it measures.
    //
    // ONE CONFIG PER PROCESS LAUNCH, deliberately. Two reasons, both real:
    //   1. RaymarchAutoBenchmark's own header records that back-to-back configs
    //      in one process produced a physically impossible ordering, with GPU
    //      frequency scaling on this fanless machine as the leading hypothesis,
    //      and says the next step is "isolating one config per process launch,
    //      not tuning these numbers further". This is that.
    //   2. Fluid PERSISTS. Once the primed config has poured, the arena is full
    //      of settled fluid, so a later idle config in the same process is not
    //      idle at all. A fresh process is the only way to get a clean idle.
    //
    // gpuFrameTime is NOT read anywhere here -- Amendment 8.10 measured it
    // inflated ~2.6-2.7x on this hardware. Wall clock only.
    private IEnumerator FluidBenchmark()
    {
        // Methodology constants, consts not fields, for the reason
        // RaymarchAutoBenchmark states: serialized copies go stale silently.
        const int warmupFrames = 600;     // ~10 s, once, before anything is measured
        const int settleFrames = 180;     // ~3 s after the config is applied
        const int targetSamples = 240;    // ~4 s of samples
        const int reopenEvery = 180;      // re-open vents ~every 3 s so load is sustained

        string cfg = ArgValue("-fluidbench") ?? "idle";
        bool primed = cfg.StartsWith("primed");

        Screen.SetResolution(1920, 1080, false);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;   // uncapped: a cap would floor the measurement

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;

        // Identical camera pose for every config: TeleportToArena is derived
        // from the arena centre and is deterministic, so idle and primed frame
        // exactly the same view. The delta must not include a pose difference.
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }

        for (int i = 0; i < warmupFrames; i++) yield return null;

        if (primed && pg != null) pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        for (int i = 0; i < settleFrames; i++)
        {
            if (primed && pg != null && i > 0 && i % reopenEvery == 0)
                pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
            yield return null;
        }

        var samples = new List<double>(targetSamples);
        int frame = 0;
        while (samples.Count < targetSamples)
        {
            // Vent budgets are finite and would drain mid-window, leaving the
            // tail measuring an idle CA and understating the delta.
            if (primed && pg != null && frame > 0 && frame % reopenEvery == 0)
                pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
            yield return null;
            samples.Add(Time.unscaledDeltaTime * 1000.0);
            frame++;
        }

        samples.Sort();
        double P(double q)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt((float)(q * (samples.Count - 1))), 0, samples.Count - 1);
            return samples[k];
        }
        double mean = 0; foreach (double v in samples) mean += v; mean /= samples.Count;

        var res = RaymarchFeature.LastDispatchResolution;
        string dir = Path.Combine(Application.persistentDataPath, "PlaygroundFluidBench");
        Directory.CreateDirectory(dir);
        string line = string.Format(CultureInfo.InvariantCulture,
            "{0},{1},{2:F3},{3:F3},{4:F3},{5:F3},{6}x{7},{8}x{9}",
            cfg, samples.Count, P(0.50), P(0.99), samples[samples.Count - 1], mean,
            res.x, res.y, Screen.width, Screen.height);
        File.AppendAllText(Path.Combine(dir, "results.csv"), line + "\n");
        Debug.Log("[FluidBench] " + line);
        yield return null;
        Application.Quit(0);
    }

    // ARGUMENT LOOKUP TAKES THE **LAST** MATCH, NOT THE FIRST.
    //
    // This cost two invalid arms in one session and nearly cost a wrong verdict.
    // Runner scripts naturally pass a default first and the per-arm override
    // second -- `--args -shadowlevel 0 -gateres 960 "$@"` with `"$@"` being
    // `-shadowlevel 3`. First-match lookup silently ignores the override, so the
    // arm runs the DEFAULT while its label, its log line and its report all say
    // otherwise. That produced a confident "shadows cost nothing" reading in §K
    // that §L had to retract, and a gate-520 arm that was really gate 960.
    //
    // Last-match is the conventional CLI rule (later wins) and makes the natural
    // script shape correct by construction. A flag that is silently ignored is
    // worse than one that errors, because it yields a plausible number for a
    // condition that never ran.
    private static string ArgValue(string flag)
    {
        string[] a = Environment.GetCommandLineArgs();
        string found = null;
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) found = a[i + 1];
        return found;
    }

    // =====================================================================
    // -pitchsweep : THE HORIZON FPS INSTRUMENT
    //
    // Reported symptom: frame rate drops when looking at the horizon. This
    // measures that as a curve instead of an impression -- same position,
    // same world, same full Phase 6 stack, only the camera PITCH changing.
    //
    // Why pitch is the right axis: a ray aimed down hits terrain within a few
    // voxels. A ray aimed at the horizon travels nearly horizontally through
    // the air gap above the surface and below _ContentCeilingVoxelY, so
    // neither of the shader's two cheap early exits can fire -- the content
    // ceiling needs the ray to be LEAVING [0,128) in Y, and the window bounds
    // guard needs it to reach the window edge, which for a horizontal ray is
    // the full 204.8-289.6 m. With the cascade on, maxDist is tier 2's outer
    // bound (290 m = 2900 voxels) and MaxOuterIterations is 1024.
    //
    // THAT IS A HYPOTHESIS. This rig exists to make it a measurement, and to
    // catch the case where the peak is somewhere else entirely -- which is why
    // it sweeps the whole range rather than sampling "horizon" and "down".
    //
    // Wall clock only (Time.unscaledDeltaTime). gpuFrameTime is inflated
    // ~2.6-2.7x on this machine (Amdt 8.10) and is not read.
    // =====================================================================
    private IEnumerator PitchSweep()
    {
        // MUST BE FULLSCREEN TO MEAN ANYTHING. Windowed, this same probe
        // returned 26.9 and 122.8 FPS for identical back-to-back runs, and
        // "fluid ON" measured FASTER than "fluid OFF" -- the compositor was
        // dominating, not the workload. Fullscreen owns the display and
        // reproduces to ~3% (66.5 / 68.3 / 68.4 across three runs).
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 300; i++) yield return null;   // streaming settles

        // SEE PAST THE DISPLAY CAP. Fullscreen presentation is locked to the
        // 60 Hz refresh, so at the shipped 960x540 gate every pitch returns
        // ~60 FPS and the curve is flat by construction -- that is the CAP
        // being measured, not the renderer. Forcing a larger gate pushes GPU
        // cost well above one refresh interval, and the RELATIVE cost between
        // pitches (which is the question) survives the scaling.
        int gate = ArgInt("-gateres", 0);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
            for (int i = 0; i < 60; i++) yield return null;
        }
        if (HasFlag("-nocascade")) RaymarchFeature.UseLODCascade = false;
        if (HasFlag("-noairmip")) RaymarchFeature.AirMipEnabled = false;
        if (HasFlag("-nosim")) Playground.DebugSuspendFluidSim = true;

        int holdFrames = ArgInt("-pitchframes", 240);
        bool withFluid = !HasFlag("-nofluid");

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "PitchSweep", ts);
        Directory.CreateDirectory(_folder);

        var cam = Camera.main;
        if (pg != null) pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
        yield return null;
        Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;

        if (withFluid && pg != null)
            pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);

        L($"PITCH SWEEP -- horizon frame-cost curve -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        L($"camera at {pos}, {holdFrames} frames per pitch, fluid {(withFluid ? "ON" : "OFF")}");
        L($"cascade {(RaymarchFeature.UseLODCascade ? "ON" : "OFF")}, " +
          $"maxOuterIterations {RaymarchFeature.MaxOuterIterations}, " +
          $"contentCeilingVoxelY {RaymarchFeature.ContentCeilingVoxelY}");
        L("Wall clock only; gpuFrameTime not read (Amdt 8.10).");
        L("");
        L("  TRUE FPS = frames / elapsed wall time. That is the column that matters;");
        L("  the percentiles are diagnostic only (present-blocking inflates them).");
        L($"  {"pitch",7}{"p50",9}{"p99",9}{"p99-late",11}{"max",9}{"TRUE-FPS",10}");

        // ORDER IS A CONFOUND AND MUST BE BREAKABLE. The vents are open, so
        // fluid accumulates for the whole sweep; a cost that rises with
        // POSITION IN THE SWEEP would look exactly like a cost that rises
        // with pitch. -pitchreverse runs the same poses in the opposite
        // order, and only a peak that survives BOTH orders is about pitch.
        bool reverse = HasFlag("-pitchreverse");
        var pitches = new List<int>();
        for (int q = -80; q <= 40; q += 5) pitches.Add(q);
        if (reverse) pitches.Reverse();
        L($"  order {(reverse ? "REVERSED (+40 -> -80)" : "forward (-80 -> +40)")}");
        L("");

        var rows = new List<string>();
        foreach (int pitch in pitches)
        {
            if (cam != null)
            {
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.Euler(pitch, 90f, 0f);
            }
            // SETTLE LONGER THAN LOOKS NECESSARY, AND SAY WHY. Rotating the
            // camera brings new chunks into view, which can trigger a burst of
            // cascade/clipmap upload. At a 30-frame settle that burst landed
            // INSIDE the measured window and showed up as ~1% of frames --
            // i.e. exactly as p99 -- making a pose-change transient look like
            // a steady-state tail. -pitchsettle sets it so the two can be told
            // apart instead of argued about.
            int settle = ArgInt("-pitchsettle", 120);
            for (int i = 0; i < settle; i++) yield return null;

            var ms = new List<double>(holdFrames);
            float t0 = Time.realtimeSinceStartup;
            for (int i = 0; i < holdFrames; i++)
            {
                yield return null;
                ms.Add(Time.unscaledDeltaTime * 1000.0);
            }
            float elapsed = Time.realtimeSinceStartup - t0;
            double trueFps = holdFrames / elapsed;
            // Also report the window with its first 60 frames dropped, so a
            // residual pose-change transient is visible as a GAP between the
            // two p99s rather than hidden inside one number.
            var tail = ms.GetRange(60, ms.Count - 60);
            tail.Sort();
            double tailP99 = tail[Mathf.Clamp((int)(0.99 * (tail.Count - 1)), 0, tail.Count - 1)];
            ms.Sort();
            double p50 = ms[ms.Count / 2];
            double p99 = ms[Mathf.Clamp((int)(0.99 * (ms.Count - 1)), 0, ms.Count - 1)];
            double mx = ms[ms.Count - 1];
            L($"  {pitch,7}{p50,9:F2}{p99,9:F2}{tailP99,11:F2}{mx,9:F2}{trueFps,10:F1}");
            rows.Add($"{pitch}\t{p50:F3}\t{p99:F3}\t{mx:F3}");
        }

        File.WriteAllText(Path.Combine(_folder, "pitch_sweep.tsv"),
            "pitch\tp50\tp99\tmax\n" + string.Join("\n", rows) + "\n");
        File.WriteAllText(Path.Combine(_folder, "pitch_sweep.txt"), _log.ToString());
        Debug.Log("[PitchSweep]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    // =====================================================================
    // -stutterprobe : ATTRIBUTE THE TAIL, don't just observe it.
    //
    // The pitch sweep established what the tail is NOT: it is not the
    // horizon, not the raymarcher, and not a pose-change transient. p50 sits
    // at 3.8-5.9 ms at every pitch while p99 sits at 47-76 ms at every pitch,
    // with fluid off and the camera static. Roughly 1% of frames take ten
    // times the median.
    //
    // This holds ONE pose and hands the window to FrameGapProbe, which tiles
    // the frame into preUpdate / update / postLate and reports GC generations,
    // upload bytes, SetData calls and streamer ms across the p99 band -- so
    // the answer is an attribution, not another observation.
    // =====================================================================
    private IEnumerator StutterProbe()
    {
        // VALIDITY LEVER. A windowed app on macOS is composited, and an
        // occluded or non-frontmost window has its presentation throttled --
        // which would look exactly like what the first probe found: a FIXED
        // fraction of slow frames, independent of how much GPU work there is.
        // Fullscreen owns the display and removes the compositor from the
        // question, so -fullscreen is how that gets ruled in or out instead
        // of assumed either way.
        bool fullscreen = HasFlag("-fullscreen");
        Screen.SetResolution(1920, 1080, fullscreen);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 300; i++) yield return null;

        // ISOLATION LEVER. The stripped kernel keeps the dispatch, the target
        // texture and the blit identical but makes the ray work trivial. If
        // the tail survives it, the tail is not the raymarch math.
        if (HasFlag("-stripped")) RaymarchFeature.UseStrippedKernel = true;

        // RAY-LENGTH LEVER. If horizon cost is "rays that travel until they
        // run out of window", capping the per-ray iteration count is the most
        // direct thing that can prove it: the cap bounds ray length and
        // nothing else. A large FPS response means ray length IS the cost; a
        // flat response means the cost is elsewhere and the long rays are
        // incidental.
        int maxIter = ArgInt("-maxiter", 0);
        if (maxIter > 0) RaymarchFeature.MaxOuterIterations = maxIter;
        // AIR-MIP LEVER: the mechanism that is SUPPOSED to make long empty
        // rays cheap. If turning it off barely changes horizon cost, it is
        // not doing its job there.
        if (HasFlag("-noairmip")) RaymarchFeature.AirMipEnabled = false;
        if (HasFlag("-nosim")) Playground.DebugSuspendFluidSim = true;
        if (HasFlag("-nocascade")) RaymarchFeature.UseLODCascade = false;

        // ---- PHASE 7 CONTINUATION, STAGE A ----
        // -shadowlevel lets the SAME probe measure §6.5's rungs, so the
        // "is the stutter new?" question and the "which rung?" question are asked
        // with ONE instrument instead of two. Absent => whatever EngineConfig
        // ships, which is also what the pre-Phase-7 binary reports (it has no
        // such flag and no shadow code at all).
        int shadowLevel = ArgInt("-shadowlevel", -1);
        if (shadowLevel >= 0) RaymarchFeature.ShadowQualityLevel = shadowLevel;
        if (HasFlag("-noskyprobe")) RaymarchFeature.UseSkyProbeAmbient = false;
        float shClamp = ArgFloat("-shadowclamp", -1f);
        if (shClamp >= 0f) RaymarchFeature.ShadowMaxDistanceOverrideM = shClamp;
        float r1 = ArgFloat("-rung1dist", -1f);
        if (r1 >= 0f) RaymarchFeature.Rung1DistanceOverrideM = r1;
        int tm = ArgInt("-tonemap", -1);
        if (tm >= 0) RaymarchFeature.TonemapOverride = tm;

        // PIN THE CLOCK. PHASE_7_COMPLETION §A.8 recorded this as a confound:
        // this probe never set the time of day and the Playground clock runs a
        // full day in 120 s, so every arm averaged over a day-night cycle with
        // shadow rays skipped below the horizon. Fine for the before/after
        // comparisons §A used -- every arm shared it -- but useless for any
        // question about what a rung COSTS. -tod pins it; -goldenhour is the
        // shorthand for the worst case §6.5 names.
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        if (HasFlag("-goldenhour")) SunCycle.SetHours(SunCycle.GoldenHourHours);
        float todHours = ArgFloat("-tod", -1f);
        if (todHours >= 0f) SunCycle.SetHours(todHours);
        if (HasFlag("-nolightupload")) { Playground.DebugSuspendLightUpload = true;
            VoxelEngine.Rendering.LightingDriver.SuspendUpload = true; }

        // -turnrate: YAW THE CAMERA CONTINUOUSLY, degrees per second.
        //
        // THIS EXISTS BECAUSE THE REPORTED SYMPTOM IS "STUTTER WHILE TURNING AND
        // WALKING", AND EVERY PROBE IN THIS FILE BEFORE NOW HELD THE CAMERA
        // STILL. A static pose cannot reproduce a symptom whose trigger is camera
        // motion: turning brings new chunks into view, which is what drives
        // clipmap upload, cascade re-downsample and air-mip rebuild -- the CPU
        // work FPS_INVESTIGATION §0.3 already showed can masquerade as a
        // rendering cost. Measuring the still pose and declaring the tail
        // unchanged would have been measuring the wrong thing entirely.
        //
        // NOTE FOR THE THREE-ARM COMPARISON: the pre-Phase-7 binary does NOT
        // have this flag. So the new-vs-old arm uses -movespeed, which exists in
        // both; -turnrate is used only for same-build rung comparisons. Mixing
        // them would compare two different workloads.
        float turnRate = ArgInt("-turnrate", 0);

        int seconds = ArgInt("-stutterseconds", 60);
        int pitch = ArgInt("-stutterpitch", 0);
        bool withFluid = !HasFlag("-nofluid");

        var cam = Camera.main;
        if (pg != null) pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
        yield return null;
        if (cam != null) cam.transform.rotation = Quaternion.Euler(pitch, 90f, 0f);
        if (withFluid && pg != null)
            pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        for (int i = 0; i < 180; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "StutterProbe", ts);
        Directory.CreateDirectory(_folder);

        // ISOLATION LEVER: cut the raymarch's own work without touching
        // anything else. If the tail is GPU raymarch cost, quartering the
        // pixels moves it. If the tail is presentation, it does not.
        int gate = ArgInt("-gateres", 0);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
            for (int i = 0; i < 60; i++) yield return null;
        }

        // MOTION. Every measurement before this one held the camera still,
        // which means no chunk ever streamed -- and "looking at the horizon"
        // in real play is something you do while MOVING. -movespeed flies the
        // camera forward along its own heading so the streamer, the clipmap
        // upload and the cascade are all live while the horizon is on screen.
        float moveSpeed = ArgInt("-movespeed", 0);

        var probe = gameObject.AddComponent<FrameGapProbe>();
        probe.Recording = true;
        var ms = new List<double>();
        float t0 = Time.realtimeSinceStartup;
        float yaw = 90f;   // StutterProbe's fixed heading; -turnrate sweeps it
        for (int i = 0; i < seconds * 60; i++)
        {
            if (moveSpeed > 0f && cam != null)
                cam.transform.position += cam.transform.forward * (moveSpeed * Time.deltaTime);
            if (turnRate > 0f && cam != null)
            {
                yaw += turnRate * Time.deltaTime;
                cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            yield return null;
            ms.Add(Time.unscaledDeltaTime * 1000.0);
        }
        float elapsed = Time.realtimeSinceStartup - t0;
        probe.Recording = false;

        ms.Sort();
        L($"STUTTER PROBE -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}, " +
          $"render {RaymarchFeature.LastDispatchResolution.x}x{RaymarchFeature.LastDispatchResolution.y}" +
          (gate > 0 ? $"  (FORCED gate {gate}x{gate * 9 / 16})" : ""));
        L($"pitch {pitch}, fluid {(withFluid ? "ON" : "OFF")}, {ms.Count} frames, camera STATIC, " +
          $"{(fullscreen ? "FULLSCREEN" : "windowed")}, kernel {(RaymarchFeature.UseStrippedKernel ? "STRIPPED" : "full")}, " +
          $"maxIter {RaymarchFeature.MaxOuterIterations}, airMip {RaymarchFeature.AirMipEnabled}, " +
          $"cascade {RaymarchFeature.UseLODCascade}, move {moveSpeed} m/s");
        L($"frames  p50 {ms[ms.Count / 2]:F2}  p90 {ms[(int)(0.90 * (ms.Count - 1))]:F2}  " +
          $"p99 {ms[(int)(0.99 * (ms.Count - 1))]:F2}  max {ms[ms.Count - 1]:F2} ms");
        L($"frames over 16.6 ms: {ms.FindAll(x => x > 16.6).Count} of {ms.Count} " +
          $"({100.0 * ms.FindAll(x => x > 16.6).Count / ms.Count:F1}%)");

        // THE DISTRIBUTION, NOT THE MEAN. A stutter is a small number of long
        // frames breaking up otherwise-smooth motion; a mean and a p99 both hide
        // that (FPS §0.2: one frame in three lands in the tail BY PACING here,
        // regardless of workload). Counts per 1000 frames at 33 ms and 50 ms are
        // the shape that actually corresponds to "we can feel each frame".
        double per1000 = 1000.0 / ms.Count;
        L($"  >>> LONG FRAMES PER 1000: " +
          $">33ms {ms.FindAll(x => x > 33.0).Count * per1000:F1}   " +
          $">50ms {ms.FindAll(x => x > 50.0).Count * per1000:F1}   " +
          $">100ms {ms.FindAll(x => x > 100.0).Count * per1000:F1}");
        var lb = RaymarchFeature.Lights;
        L($"  shadow rung {(shadowLevel >= 0 ? shadowLevel.ToString() : "default/none")}   " +
          $"turn {turnRate} deg/s   move {moveSpeed} m/s   " +
          $"lightUpload {(Playground.DebugSuspendLightUpload ? "SUSPENDED" : "on")}");
        L($"  shadowClamp {(RaymarchFeature.ShadowMaxDistanceOverrideM >= 0f ? RaymarchFeature.ShadowMaxDistanceOverrideM + " m" : "inherit primary (290 m)")}   " +
          $"rung1dist {(RaymarchFeature.Rung1DistanceOverrideM >= 0f ? RaymarchFeature.Rung1DistanceOverrideM + " m" : "LOD1 (128 m)")}   " +
          $"tonemap {(RaymarchFeature.TonemapOverride >= 0 ? RaymarchFeature.TonemapOverride.ToString() : "config")}");
        // REGISTERED is the number that matters for Stage A's second round:
        // §3.8 sizes the whole light system for "a handful of placed torches",
        // and if this reads in the thousands then something is registering that
        // §3.8 never contemplated.
        L($"  SUN {SunCycle.ClockString()}  {SunCycle.ElevationDegrees:F1} deg  {SunCycle.PhaseName}  " +
          $"(clock {(VoxelEngine.Rendering.LightingDriver.ClockPaused ? "PINNED" : "RUNNING -- see §A.8")})");
        L($"  LIGHTS registered {(lb != null ? lb.RegisteredCount : -1)}   " +
          $"active {(lb != null ? lb.ActiveCount : -1)} / cap {EngineConfig.MAX_ACTIVE_LIGHTS}   " +
          $"SetData calls {(lb != null ? lb.UploadsPerformed : -1)}");
        L("");
        L($"  >>> TRUE SUSTAINED RATE: {ms.Count} frames in {elapsed:F2} s = " +
          $"{ms.Count / elapsed:F1} FPS  (mean frame {1000.0 * elapsed / ms.Count:F2} ms)");
        L("  Percentiles above are NOT the headline. This app presents faster than the");
        L("  display can show, so a third of frames block in present and land in the tail");
        L("  by pacing rather than by cost -- every isolation arm returned EXACTLY 600 of");
        L("  1800. Frames divided by elapsed time is the number that matches what a player");
        L("  sees, and it is the one to hold to 60.");
        L("");
        var sb = new StringBuilder();
        probe.AppendReport(sb, 30.0);
        _log.Append(sb);

        File.WriteAllText(Path.Combine(_folder, "stutter_probe.txt"), _log.ToString());
        Debug.Log("[StutterProbe]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    // =====================================================================
    // -lightingab : AMENDMENT_8_12_PHASE7_GATE.md's SUBSTITUTE GATE
    //
    // §13 Phase 7 asks for "raymarch + shadows <= 9.0 ms, Performance State
    // checked" and a combined "<= 13.0 ms" gate. NEITHER CAN BE RUN AS WRITTEN
    // HERE, for four independent reasons set out in AMENDMENT_8_12 §2: no Xcode
    // (Amdt 8.9 Rule 1), Performance State unreadable (Rule 2), gpuFrameTime
    // inflated ~2.6-2.7x (Amdt 8.10's retracted result), and Unity merging the
    // dispatches into one Metal encoder so per-pass attribution does not exist.
    //
    // So this measures END-TO-END SUSTAINED RATE, which does exist, and reports
    // it as such. NO per-pass ms figure is produced, because producing one would
    // be inventing a number to fill a gap -- which Amendment 8.9 Rule 3 forbids
    // in exactly those words.
    //
    // BUILT ON THIS FILE'S EXISTING LEVERS, NOT A NEW HARNESS. -fullscreen,
    // -gateres, -nofluid, -nosim and -movespeed already exist here and already
    // carry the reasoning for why each is needed; a parallel harness would have
    // to re-derive all of it and would drift.
    // =====================================================================
    private IEnumerator LightingAb()
    {
        // FULLSCREEN IS NOT OPTIONAL. FPS_INVESTIGATION §0.1: identical
        // back-to-back WINDOWED runs of this same binary returned 69.6 and 26.9
        // FPS, and "fluid ON" measured FASTER than "fluid OFF" -- the macOS
        // compositor throttles a windowed surface and dominates everything else.
        // Fullscreen reproduces to 2.9%.
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;   // a cap would floor the measurement

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;

        int gate = ArgInt("-gateres", 0);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        }

        // The rung under test. Absent => whatever EngineConfig ships, which is
        // what arm A (the pre-Phase-7 build) will report, since it has no such
        // flag and no shadow code at all.
        int level = ArgInt("-shadowlevel", -1);
        if (level >= 0) RaymarchFeature.ShadowQualityLevel = level;

        // GOLDEN HOUR, DERIVED FROM THE ARC rather than typed. §13 asks for it
        // to be captured specifically and §6.5 identifies it as the worst case:
        // the longest grazing rays.
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        if (HasFlag("-goldenhour")) SunCycle.SetHours(SunCycle.GoldenHourHours);
        float tod = ArgInt("-tod", -1);
        if (tod >= 0f) SunCycle.SetHours(tod);

        if (HasFlag("-nosim")) Playground.DebugSuspendFluidSim = true;
        if (HasFlag("-noskyprobe")) RaymarchFeature.UseSkyProbeAmbient = false;

        for (int i = 0; i < 300; i++) yield return null;   // streaming settles

        var cam = Camera.main;
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }

        // GROUND HORIZON. Amendment 8.9 established this as "the real gameplay
        // camera" and consistently the MOST EXPENSIVE pose in every config, so
        // it is the pose a budget question should be asked at.
        if (cam != null) cam.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        bool withFluid = !HasFlag("-nofluid");
        if (withFluid && pg != null) pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        for (int i = 0; i < 180; i++) yield return null;   // fluid settles into flight

        int seconds = ArgInt("-abseconds", 60);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "LightingAB", ts);
        Directory.CreateDirectory(_folder);

        var ms = new List<double>(seconds * 70);
        float t0 = Time.realtimeSinceStartup;
        int reopen = 0;
        for (int i = 0; i < seconds * 60; i++)
        {
            // Vent budgets are finite and would drain mid-window, leaving the
            // tail measuring an IDLE CA and understating the combined arm --
            // which is the arm §11.2 moved to this phase specifically to get right.
            if (withFluid && pg != null && ++reopen % 180 == 0)
                pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
            yield return null;
            ms.Add(Time.unscaledDeltaTime * 1000.0);
        }
        float elapsed = Time.realtimeSinceStartup - t0;

        var sorted = new List<double>(ms); sorted.Sort();
        double P(double q) => sorted[Mathf.Clamp((int)(q * (sorted.Count - 1)), 0, sorted.Count - 1)];

        var res = RaymarchFeature.LastDispatchResolution;
        L($"LIGHTING A/B -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}   {Screen.width}x{Screen.height} " +
          $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED -- INVALID, see FPS §0.1")}");
        L($"build {(Debug.isDebugBuild ? "DEVELOPMENT -- NOT COMPARABLE" : "RELEASE")}");
        L($"internal gate {res.x}x{res.y}" + (gate > 0 ? $"  (FORCED {gate})" : "  (shipped)"));
        L($"shadow rung {(level >= 0 ? level.ToString() : "n/a -- arm A, no shadow code")}" +
          $"   k_soft {LightingConfig.Active.kSoft:F3}   skyProbe {RaymarchFeature.UseSkyProbeAmbient}");
        L($"sun {SunCycle.ClockString()}  {SunCycle.ElevationDegrees:F1} deg  {SunCycle.PhaseName}");
        L($"fluid {(withFluid ? "LIVE (§13's true combined gate)" : "OFF")}" +
          $"   lights {(RaymarchFeature.Lights != null ? RaymarchFeature.Lights.ActiveCount : 0)} active");
        L("");
        L($"  >>> TRUE SUSTAINED RATE: {ms.Count} frames in {elapsed:F2} s = " +
          $"{ms.Count / elapsed:F1} FPS   (mean frame {1000.0 * elapsed / ms.Count:F2} ms)");
        L("");
        L($"  diagnostics only, NOT a headline: p50 {P(0.50):F2}  p90 {P(0.90):F2}  " +
          $"p99 {P(0.99):F2}  max {sorted[sorted.Count - 1]:F2} ms");
        L($"  frames over 16.6 ms: {sorted.FindAll(x => x > 16.6).Count} of {sorted.Count}");
        L("  (120 Hz ProMotion: the app out-presents the display, so roughly one frame in three");
        L("   lands in the tail BY PACING rather than by cost -- FPS_INVESTIGATION §0.2 measured");
        L("   exactly 600 of 1800 in four different isolation arms. Do not read the percentiles");
        L("   as cost, and never quote p99 against a budget.)");

        File.WriteAllText(Path.Combine(_folder, "lighting_ab.txt"), _log.ToString());
        Debug.Log("[LightingAB]\n" + _log);

        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_folder, "pose.png"), tex.EncodeToPNG()); }
        finally { Destroy(tex); }

        yield return null;
        Application.Quit(0);
    }


    // =====================================================================
    // -p7stress : STAGE D -- LIGHTING STRESS AND DISTANCE VIEWING
    //
    // Everything §13's gates asserted about lighting is near-field,
    // single-light, or correctness-only. This asks a different question: does
    // it HOLD UP -- many lights, a genuinely dark irregular space, and terrain
    // far enough away to cross into the coarse tiers.
    //
    // IT ASSERTS ALMOST NOTHING, ON PURPOSE. D1 and D3 are quality questions
    // ("does it look right"), and this project's discipline is that a rig does
    // not get to answer those. What it does is produce COMPARABLE evidence --
    // the same view at each tier boundary, the same chamber with 1 vs 3
    // torches, Beauty and sun-visibility side by side -- so the answer can be
    // looked at rather than argued. Where a number IS available (D2's frame
    // cost) it is measured and labelled as such.
    //
    // Runs in the PLAYGROUND scene deliberately: D4 needs live fluid, the
    // player, and the real §8.3 edit path, all of which already exist there.
    // A new scene would have had to re-wire all three.
    // =====================================================================
    private IEnumerator Phase7Stress()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        string sub = ArgValue("-p7stress") ?? "all";
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 300; i++) yield return null;

        int preset = ArgInt("-preset", -1);
        if (preset >= 0) VoxelEngine.Rendering.LightingPresets.Apply(preset);
        int level = ArgInt("-shadowlevel", -1);
        if (level >= 0) RaymarchFeature.ShadowQualityLevel = level;

        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "Phase7Stress", $"{ts}_{sub}");
        Directory.CreateDirectory(_folder);

        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var driver = FindAnyObjectByType<VoxelEngine.Rendering.LightingDriver>();
        if (driver != null) driver.Attach(edits);

        L($"PHASE 7 STAGE D -- lighting stress & distance -- {ts}  sub={sub}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
          $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}");
        L($"preset {(preset >= 0 ? VoxelEngine.Rendering.LightingPresets.All[preset].Name : "as-found")}   " +
          $"shadow rung {RaymarchFeature.ShadowQualityLevel}   " +
          $"N.L on point lights {RaymarchFeature.PointLightNdotL}");
        L("");

        var cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 peak = DeepestNear(store, camVox, 240);
        int surf = SurfaceYAt(store, peak.x, peak.z);
        L($"working from the highest terrain nearby: {peak}, surface y={surf}");
        L("");

        if (sub == "all" || sub == "d1") yield return D1_DarkCave(edits, driver, cam, peak, surf);
        if (sub == "all" || sub == "d2") yield return D2_ManyLights(edits, driver, cam, peak, surf);
        if (sub == "all" || sub == "d3") yield return D3_Distance(cam, peak, surf);
        if (sub == "all" || sub == "d4") yield return D4_Chaos(pg, edits, cam, peak, surf);
        if (sub == "all" || sub == "g1") yield return G1_ColouredLightScale(edits, driver, cam, peak, surf);
        if (sub == "all" || sub == "h1") yield return H1_LavaAsALight(edits, driver, cam, peak, surf);

        File.WriteAllText(Path.Combine(_folder, "stage_d_report.txt"), _log.ToString());
        Debug.Log("[p7stress]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    // ---- D1: does a torch make a dark cave look GOOD, not just pass a gate ----
    private IEnumerator D1_DarkCave(EditService edits, VoxelEngine.Rendering.LightingDriver driver,
                                    Camera cam, int3 peak, int surf)
    {
        L("D1 -- irregular sealed cave, 1 torch then 3. QUALITY, not correctness.");
        L("  Gate 1 already proved the luminance numbers (0.0369 -> 0.5019). That is a");
        L("  measurement, not an opinion about whether it reads as a torch-lit cave.");

        // An IRREGULAR chamber, not the rig's sphere: three overlapping lobes of
        // different radii plus a low side passage. A sphere lights far too
        // evenly to tell you anything about falloff on real geometry.
        int cy = Mathf.Max(16, surf - 34);
        int3 c0 = new int3(peak.x, cy, peak.z);
        int dug = 0;
        dug += Carve(edits, c0, 15);
        dug += Carve(edits, c0 + new int3(17, 3, -6), 11);
        dug += Carve(edits, c0 + new int3(-14, -4, 9), 12);
        for (int t = 0; t < 26; t++)   // a low passage off the main lobe
            dug += Carve(edits, c0 + new int3(-14 - t, -6, 9 + (t / 3)), 4);
        L($"  carved an irregular chamber (3 lobes + a passage): {dug} voxels via EditService");
        for (int i = 0; i < 40; i++) yield return null;

        yield return StressShot(cam, c0 + new int3(0, 2, -16), c0, "D1_00_unlit_wide");
        yield return StressShot(cam, c0 + new int3(10, 0, -8), c0 + new int3(14, 2, -2), "D1_01_unlit_near");

        int3 torch = c0 + new int3(0, -9, 0);
        edits.TrySetVoxel(torch, Materials.Torch);
        for (int i = 0; i < 20; i++) yield return null;
        L($"  torch 1 at {torch}; lights registered = {(driver != null ? driver.Lights.RegisteredCount : -1)}");

        // SEVERAL ANGLES AND DISTANCES, which is the whole point of D1 -- a
        // single hero angle can flatter any lighting model.
        yield return StressShot(cam, torch + new int3(0, 3, -9), torch, "D1_02_one_torch_3m");
        yield return StressShot(cam, torch + new int3(0, 5, -22), torch, "D1_03_one_torch_7m");
        yield return StressShot(cam, torch + new int3(14, 8, -14), torch, "D1_04_one_torch_high_angle");
        yield return StressShot(cam, torch + new int3(-22, 1, 12), torch, "D1_05_one_torch_across_lobe");
        yield return StressShot(cam, torch + new int3(-40, -4, 20), torch, "D1_06_one_torch_from_passage");

        // 2-3 torches placed NATURALLY -- spread through the lobes the way a
        // player lighting a cave would, not at the cap and not in a line. The
        // question is whether overlapping falloffs read as one lit space or as
        // visible seams / double-bright patches.
        int3 t2 = c0 + new int3(16, -5, -5);
        int3 t3 = c0 + new int3(-13, -8, 8);
        edits.TrySetVoxel(t2, Materials.Torch);
        edits.TrySetVoxel(t3, Materials.Torch);
        for (int i = 0; i < 20; i++) yield return null;
        L($"  torches 2 and 3 at {t2}, {t3}; registered = {(driver != null ? driver.Lights.RegisteredCount : -1)}");

        yield return StressShot(cam, c0 + new int3(0, 4, -18), c0, "D1_07_three_torches_wide");
        yield return StressShot(cam, c0 + new int3(8, 0, 0), t2, "D1_08_three_torches_overlap");
        yield return StressShot(cam, c0 + new int3(-6, -4, 4), t3, "D1_09_three_torches_far_lobe");
        L("");
    }

    // ---- D2: many-light stress, visual AND cost ----
    private IEnumerator D2_ManyLights(EditService edits, VoxelEngine.Rendering.LightingDriver driver,
                                      Camera cam, int3 peak, int surf)
    {
        L("D2 -- MAX_ACTIVE_LIGHTS torches in a plausible layout: visual + frame cost.");
        if (driver != null) driver.Lights.Clear();

        // A PLAUSIBLE layout, not an adversarial one: a lit corridor with
        // torches down both walls at even spacing, which is what a player
        // lighting a base actually builds.
        int cy = Mathf.Max(14, surf - 20);
        int3 origin = new int3(peak.x + 60, cy, peak.z);
        int carved = 0;
        for (int t = 0; t < 34; t++)
            carved += Carve(edits, origin + new int3(t * 5, 0, 0), 7);
        L($"  carved a {34 * 5}-voxel corridor: {carved} voxels");
        for (int i = 0; i < 40; i++) yield return null;

        float zeroLights = 0f, fullLights = 0f;
        yield return MeasureHere(cam, origin + new int3(-10, 2, 0), origin + new int3(80, 0, 0),
                                 r => zeroLights = r, "D2_00_corridor_zero_lights");

        int cap = EngineConfig.MAX_ACTIVE_LIGHTS;
        int placed = 0;
        for (int i = 0; i < cap; i++)
        {
            int3 at = origin + new int3(i * 5, 1, (i % 2 == 0) ? -5 : 5);
            if (edits.TrySetVoxel(at, Materials.Torch)) placed++;
        }
        for (int i = 0; i < 30; i++) yield return null;
        L($"  placed {placed} torches down both walls; registered " +
          $"{(driver != null ? driver.Lights.RegisteredCount : -1)}, active " +
          $"{(driver != null ? driver.Lights.ActiveCount : -1)} / cap {cap}");

        yield return MeasureHere(cam, origin + new int3(-10, 2, 0), origin + new int3(80, 0, 0),
                                 r => fullLights = r, "D2_01_corridor_full_cap");
        yield return StressShot(cam, origin + new int3(40, 3, -12), origin + new int3(40, 0, 0), "D2_02_cap_side_on");

        // The VISUAL cap question: place more, walk toward them, and see whether
        // a distant torch visibly un-lighting reads as a design limit or as a bug.
        int extra = 0;
        for (int i = 0; i < 12; i++)
            if (edits.TrySetVoxel(origin + new int3(170 + i * 5, 1, 0), Materials.Torch)) extra++;
        for (int i = 0; i < 30; i++) yield return null;
        L($"  placed {extra} MORE past the cap; registered " +
          $"{(driver != null ? driver.Lights.RegisteredCount : -1)}, active " +
          $"{(driver != null ? driver.Lights.ActiveCount : -1)} -- furthest-first drop (§3.8)");
        yield return StressShot(cam, origin + new int3(-10, 2, 0), origin + new int3(80, 0, 0), "D2_03_over_cap_from_near_end");
        yield return StressShot(cam, origin + new int3(150, 2, 0), origin + new int3(200, 0, 0), "D2_04_over_cap_from_far_end");

        L($"  PERFORMANCE MEASURED (same-session, back-to-back, NOT cooled -- a");
        L($"  diagnostic, not a gate): 0 lights {zeroLights:F2} ms/frame, " +
          $"{cap} lights {fullLights:F2} ms/frame, delta {fullLights - zeroLights:+0.00;-0.00} ms");
        L("");
    }

    // =====================================================================
    // G1 -- COLOURED LIGHTS AT TERRARIA SCALE, and where the real cap is.
    //
    // THE QUESTION IS NOT "does 32 work". §3.8's cap already guarantees 32 is
    // what the shader ever sees. The question is what happens to the CPU side
    // when a player behaves like a Terraria player and places FAR more than the
    // cap -- because everything above the cap still gets registered, still gets
    // distance-scored, and still gets sorted, EVERY FRAME. That is the exact
    // shape of the Stage A stutter (12,000 lava voxels in an O(n^2) sort), and
    // the fix made selection O(n x 32) rather than free. O(n x 32) still grows
    // with n; this finds the n where it starts to matter.
    //
    // MIXED COLOURS, not one tint repeated: the §A upload path skips the
    // GPU SetData when the selected set is byte-identical to what was uploaded
    // last frame. Identical torches make that comparison succeed trivially and
    // would flatter the result. Cycling four tints guarantees the 32 selected
    // lights differ in content as well as position.
    // =====================================================================
    private IEnumerator G1_ColouredLightScale(EditService edits, VoxelEngine.Rendering.LightingDriver driver,
                                              Camera cam, int3 peak, int surf)
    {
        L("G1 -- COLOURED light scaling: 32 / 64 / 128 / 256 / 512 mixed-colour torches.");
        L("  Same emissive strength and range for all four tints, so what varies is colour.");
        if (driver == null) { L("  NO LightingDriver -- skipped, and that is a rig failure, not a result."); yield break; }

        byte[] tints = { Materials.Torch, Materials.TorchBlue, Materials.TorchGreen, Materials.TorchPurple };
        string[] tintNames = { "warm", "blue", "green", "violet" };

        // A big open hall, so the lights are all mutually visible and none is
        // occluded into irrelevance. 512 torches at 2-voxel spacing on a grid.
        int cy = Mathf.Max(14, surf - 26);
        int3 hall = new int3(peak.x + 40, cy, peak.z + 40);
        int carved = 0;
        for (int gx = 0; gx < 8; gx++)
            for (int gz = 0; gz < 8; gz++)
                carved += Carve(edits, hall + new int3(gx * 8 - 28, 0, gz * 8 - 28), 6);
        L($"  carved a hall: {carved} voxels");
        for (int i = 0; i < 60; i++) yield return null;

        int3 eye = hall + new int3(-42, 6, -42);
        int3 look = hall;

        float baseline = 0f;
        yield return MeasureHere(cam, eye, look, r => baseline = r, "G1_00_hall_no_lights");
        L($"  baseline, 0 lights: {baseline:F2} ms/frame");
        L("");
        L($"  {"placed",8}{"registered",12}{"active",8}{"meanMs",9}{"vs base",10}{"uploads/frame",15}");

        int[] counts = { 32, 64, 128, 256, 512 };
        int already = 0;
        var results = new List<(int n, float ms, double upf)>();
        foreach (int target in counts)
        {
            // Place the DIFFERENCE, so this is cumulative and each count is the
            // same world plus more lights -- not five unrelated scenes.
            int placed = 0;
            for (int i = already; i < target; i++)
            {
                // A loose 3D lattice inside the hall, cycling tint per light.
                int gx = i % 16, gz = (i / 16) % 16, gy = i / 256;
                int3 at = hall + new int3(gx * 4 - 30, 1 + gy * 3, gz * 4 - 30);
                if (edits.TrySetVoxel(at, tints[i % tints.Length])) placed++;
            }
            already = target;
            for (int i = 0; i < 45; i++) yield return null;

            float ms = 0f;
            yield return MeasureHere(cam, eye, look, r => ms = r, $"G1_{target:D3}_lights");

            // Uploads per frame over an EXPLICIT, counted window. The §A fix
            // skips the GPU SetData when the selected 32 are byte-identical to
            // last frame's; with a still camera and static lights this should
            // read ~0, and anything else means the byte comparison is failing
            // to converge -- which is the thing worth catching at 512.
            const int uploadWindow = 120;
            long u0 = driver.Lights.UploadsPerformed;
            for (int f = 0; f < uploadWindow; f++) yield return null;
            double perFrame = (driver.Lights.UploadsPerformed - u0) / (double)uploadWindow;

            L($"  {target,8}{driver.Lights.RegisteredCount,12}{driver.Lights.ActiveCount,8}" +
              $"{ms,9:F2}{ms - baseline,10:+0.00;-0.00}{perFrame,15:F2}");
            results.Add((target, ms, perFrame));
        }

        L("");
        L("  Colour blending, looked at rather than asserted:");
        yield return StressShot(cam, hall + new int3(-20, 4, -20), hall, "G1_blend_wide");
        yield return StressShot(cam, hall + new int3(-6, 2, -6), hall + new int3(6, 1, 6), "G1_blend_close");
        for (int i = 0; i < tints.Length; i++)
        {
            // One tint at a time, so the blend shot above can be read against
            // what each colour does on its own.
            driver.Lights.Clear();
            int3 solo = hall + new int3(0, 1, 0);
            edits.TrySetVoxel(solo, tints[i]);
            for (int f = 0; f < 30; f++) yield return null;
            yield return StressShot(cam, hall + new int3(-9, 3, -9), solo, $"G1_solo_{tintNames[i]}");
        }

        L("");
        L("  READ THIS AS: the shader always shades exactly MAX_ACTIVE_LIGHTS (32).");
        L("  Any growth in the column above is CPU-side selection, and it is the");
        L("  number that decides what a real placement cap should be.");
        if (results.Count >= 2)
        {
            var lo = results[0]; var hi = results[results.Count - 1];
            L($"  32 -> {hi.n} lights moved the frame {hi.ms - lo.ms:+0.00;-0.00} ms " +
              $"({lo.ms:F2} -> {hi.ms:F2}).");
        }
        L("  NOT COOLED and not counterbalanced: this is a diagnostic that locates");
        L("  the knee, and the count either side of it gets a cooled arm of its own.");
        L("");
    }

    // =====================================================================
    // H1 -- SHOULD LAVA LIGHT THE CAVE AROUND IT? Re-asked with a number.
    //
    // §A excluded mobile emissives from the §3.8 light buffer because a flowing
    // lava lake registered ~12,000 point lights into an O(n^2) insertion sort,
    // which is what the owner was feeling as stutter. The sort is now partial
    // selection, O(n x MAX_ACTIVE_LIGHTS). The exclusion therefore rests on a
    // premise that no longer holds, and §H's brief is to re-examine it rather
    // than inherit the answer.
    //
    // THE HONEST SHAPE OF THE EXPERIMENT: the same lava lake, measured twice in
    // the same session back to back, differing only in whether its voxels are
    // registered as lights. Both arms shot from the same eye so the visual
    // difference -- the whole reason anyone wants this -- can be looked at.
    //
    // THIS IS A DIAGNOSTIC, NOT A GATE. It locates the cost. Whether the cost
    // is acceptable, and whether the per-region representation §H asks about is
    // worth building, is a design decision and is written up as options.
    // =====================================================================
    private IEnumerator H1_LavaAsALight(EditService edits, VoxelEngine.Rendering.LightingDriver driver,
                                        Camera cam, int3 peak, int surf)
    {
        L("H1 -- LAVA AS A LIGHT SOURCE: the §A exclusion, re-measured now that");
        L("  selection is O(n x 32) rather than O(n^2).");
        if (driver == null) { L("  NO LightingDriver -- skipped; that is a rig failure, not a result."); yield break; }

        driver.Lights.Clear();
        VoxelEngine.Rendering.LightSourceBuffer.AllowMobileEmissives = false;

        // A cavern with a lava lake in the floor, which is the situation the
        // flagged consequence is about: a dark cave that a player expects to be
        // lit orange by the lava in it.
        int cy = Mathf.Max(16, surf - 40);
        int3 cave = new int3(peak.x - 60, cy, peak.z + 20);
        int carved = 0;
        for (int i = 0; i < 5; i++)
            carved += Carve(edits, cave + new int3(i * 10 - 20, 0, 0), 14);
        L($"  carved a cavern: {carved} voxels");
        for (int i = 0; i < 60; i++) yield return null;

        // Fill the floor with lava and REMEMBER where, so the second arm can
        // register exactly the same voxels instead of a different lake.
        var lavaVoxels = new List<int3>();
        for (int dx = -34; dx <= 34; dx++)
            for (int dz = -13; dz <= 13; dz++)
                for (int dy = -12; dy <= -8; dy++)
                {
                    int3 at = cave + new int3(dx, dy, dz);
                    if (edits.TrySetVoxel(at, Materials.Lava)) lavaVoxels.Add(at);
                }
        for (int i = 0; i < 120; i++) yield return null;
        L($"  placed {lavaVoxels.Count} lava voxels " +
          $"(§A's case was ~12,000; this is {lavaVoxels.Count / 1000.0:F1}k)");
        L($"  lights registered with the exclusion IN PLACE: {driver.Lights.RegisteredCount}" +
          "   <- expected 0, and if it is not 0 the exclusion is not doing what §A says");
        L("");

        int3 eye = cave + new int3(-40, 4, 0);
        int3 look = cave + new int3(0, -8, 0);

        float off = 0f;
        yield return MeasureHere(cam, eye, look, r => off = r, "H1_00_lava_NOT_a_light");

        // ARM 2: register every one of them. Direct Register() rather than a
        // re-edit, because TrySetVoxel to the material already there is a no-op
        // and would silently register nothing.
        VoxelEngine.Rendering.LightSourceBuffer.AllowMobileEmissives = true;
        int registered = 0;
        foreach (int3 v in lavaVoxels)
            if (driver.Lights.Register(v, Materials.Lava)) registered++;
        for (int i = 0; i < 60; i++) yield return null;
        L($"  ARM 2: registered {registered} lava voxels as point lights; buffer reports " +
          $"{driver.Lights.RegisteredCount} registered, {driver.Lights.ActiveCount} active " +
          $"(cap {EngineConfig.MAX_ACTIVE_LIGHTS})");

        float on = 0f;
        yield return MeasureHere(cam, eye, look, r => on = r, "H1_01_lava_IS_a_light");

        // A closer pair, because the visual question is about the cave WALL
        // next to the lava, not the lake surface -- the lake is self-lit either
        // way and looks identical in both arms.
        yield return StressShot(cam, cave + new int3(-16, -4, 0), cave + new int3(-16, -2, 12), "H1_02_wall_beside_lava_LIT");
        VoxelEngine.Rendering.LightSourceBuffer.AllowMobileEmissives = false;
        driver.Lights.Clear();
        for (int i = 0; i < 45; i++) yield return null;
        yield return StressShot(cam, cave + new int3(-16, -4, 0), cave + new int3(-16, -2, 12), "H1_03_wall_beside_lava_UNLIT");

        L("");
        L($"  lava NOT a light: {off:F2} ms/frame");
        L($"  lava IS  a light: {on:F2} ms/frame   delta {on - off:+0.00;-0.00} ms " +
          $"for {registered} registered sources");
        if (registered > 0)
            L($"  = {1000f * (on - off) / registered:F4} us per registered lava voxel per frame");
        L("  Same session, back to back, NOT cooled and NOT counterbalanced. It");
        L("  locates the cost; it does not certify it.");
        L("");
    }

    // ---- D3: distant lit terrain, across the tier boundaries ----
    private IEnumerator D3_Distance(Camera cam, int3 peak, int surf)
    {
        L("D3 -- the SAME mountain viewed across the tier 0->1 (64 m) and 1->2 (128 m)");
        L("  boundaries, in Beauty and in sun-visibility, at noon and at golden hour.");
        L("  §8 flagged 'distant shadow speckle' and said it could not be settled without");
        L("  a tier-1/2 oracle, which does not exist (Amdt 8.10 §5.7). This does NOT");
        L("  settle it -- it produces directly comparable frames so it can be looked at.");

        var target = new int3(peak.x, surf, peak.z);
        // Distances chosen to straddle the boundaries rather than sit on them:
        // LODConfig.TIER_OUTER_RANGE_M is {64, 128, 290}.
        float[] dists = { 30f, 55f, 75f, 110f, 150f, 240f };
        foreach (float h in new[] { SunCycle.NoonHours, SunCycle.GoldenHourHours })
        {
            SunCycle.SetHours(h);
            string when = h == SunCycle.NoonHours ? "noon" : "golden";
            foreach (float d in dists)
            {
                int tier = d < 64f ? 0 : (d < 128f ? 1 : 2);
                Vector3 eye = new Vector3(target.x * 0.1f, (target.y + 45) * 0.1f, target.z * 0.1f - d);
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation(
                    (new Vector3(target.x * 0.1f, target.y * 0.1f, target.z * 0.1f) - eye).normalized, Vector3.up);
                for (int i = 0; i < 22; i++) yield return null;

                RaymarchFeature.UseDebugViewOverride = false;
                yield return Shot($"D3_{when}_{d:000}m_tier{tier}_beauty", $"{when}, {d} m, expected tier {tier}");
                RaymarchFeature.UseDebugViewOverride = true;
                RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
                for (int i = 0; i < 8; i++) yield return null;
                yield return Shot($"D3_{when}_{d:000}m_tier{tier}_sunvis", "same view, sun visibility");
                RaymarchFeature.UseDebugViewOverride = false;
                L($"  captured {when} at {d} m (expected tier {tier})");
            }
        }

        // Does the silhouette shadow SWIM as the camera moves at extreme range?
        // Gate 3 asserted alignment from one pose; nobody has looked at whether
        // it stays put under motion, which is how the retracted 8.1 rule
        // announced itself (PHASE_7_COMPLETION §4.3).
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        for (int k = 0; k < 5; k++)
        {
            Vector3 eye = new Vector3(target.x * 0.1f + (k - 2) * 6f,
                                      (target.y + 45) * 0.1f, target.z * 0.1f - 200f);
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation(
                (new Vector3(target.x * 0.1f, target.y * 0.1f, target.z * 0.1f) - eye).normalized, Vector3.up);
            for (int i = 0; i < 18; i++) yield return null;
            yield return Shot($"D3_swim_{k}_200m", $"lateral step {k - 2} at 200 m -- does the shadow stay put?");
        }
        L("  captured a 5-step lateral pan at 200 m: compare D3_swim_0..4 for shadow drift.");
        L("");
    }

    // ---- D4: lighting under combined chaos ----
    private IEnumerator D4_Chaos(Playground pg, EditService edits, Camera cam, int3 peak, int surf)
    {
        L("D4 -- golden hour + live fluid + active digging + torches nearby.");
        L("  §3.10 predicts the shadow and the torch stay correct because both read");
        L("  the authoritative terrain byte. This is the VISUAL spot-check of that");
        L("  under real simultaneous load, not a new correctness proof.");

        SunCycle.SetHours(SunCycle.GoldenHourHours);
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }

        var cv = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                   cam.transform.position.y, cam.transform.position.z));
        int sy = SurfaceYAt(Phase4Bootstrapper.Store, cv.x, cv.z);
        if (sy < 1) sy = surf;
        int3 here = new int3(cv.x, sy, cv.z);

        edits.TrySetVoxel(here + new int3(4, 2, 0), Materials.Torch);
        edits.TrySetVoxel(here + new int3(-4, 2, 3), Materials.Torch);
        for (int i = 0; i < 20; i++) yield return null;

        if (pg != null) pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        for (int i = 0; i < 90; i++) yield return null;

        cam.transform.position = new Vector3(here.x * 0.1f - 3f, (here.y + 14) * 0.1f, here.z * 0.1f - 9f);
        cam.transform.rotation = Quaternion.Euler(22f, 20f, 0f);

        // Dig WHILE fluid moves, capturing as it happens -- the point is frames
        // taken mid-change, not a before/after pair.
        for (int frame = 0; frame < 5; frame++)
        {
            for (int d = 0; d < 6; d++)
                edits.TrySetVoxel(here + new int3(d - 3, 0, 6 + frame), Materials.Air);
            if (pg != null && frame % 2 == 0)
                pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
            for (int i = 0; i < 45; i++) yield return null;
            yield return Shot($"D4_chaos_{frame}", "golden hour, live fluid, mid-dig, 2 torches");
        }
        L("  captured 5 frames during simultaneous fluid flow and digging.");
        L("");
    }

    // ---- helpers ----
    private static int SurfaceYAt(ChunkStore store, int x, int z)
    {
        for (int y = WorldGenConstants.MAX_TERRAIN_HEIGHT + 2; y >= 1; y--)
        {
            byte m = store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }

    private static int3 DeepestNear(ChunkStore store, int3 from, int r)
    {
        int3 best = from; int bestY = -1;
        for (int dz = -r; dz <= r; dz += 10)
        for (int dx = -r; dx <= r; dx += 10)
        {
            int y = SurfaceYAt(store, from.x + dx, from.z + dz);
            if (y > bestY) { bestY = y; best = new int3(from.x + dx, y, from.z + dz); }
        }
        return best;
    }

    private static int Carve(EditService edits, int3 centre, int radius)
    {
        int r2 = radius * radius, n = 0;
        for (int dz = -radius; dz <= radius; dz++)
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
            if (dx * dx + dy * dy + dz * dz <= r2 &&
                edits.TrySetVoxel(centre + new int3(dx, dy, dz), Materials.Air)) n++;
        return n;
    }

    private IEnumerator StressShot(Camera cam, int3 eyeVox, int3 lookVox, string name)
    {
        var eye = new Vector3(eyeVox.x * 0.1f, eyeVox.y * 0.1f, eyeVox.z * 0.1f);
        var at = new Vector3(lookVox.x * 0.1f, lookVox.y * 0.1f, lookVox.z * 0.1f);
        cam.transform.position = eye;
        if ((at - eye).sqrMagnitude > 1e-4f)
            cam.transform.rotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
        for (int i = 0; i < 14; i++) yield return null;
        yield return Shot(name, $"eye {eyeVox} -> {lookVox}");
    }

    /// Holds a pose and returns mean ms/frame. SAME-SESSION BACK-TO-BACK, NOT
    /// COOLED -- labelled as a diagnostic everywhere it is reported, because a
    /// 300 s cooldown per arm inside a screenshot rig would put this run past
    /// an hour for two numbers.
    private IEnumerator MeasureHere(Camera cam, int3 eyeVox, int3 lookVox,
                                    Action<float> sink, string shotName)
    {
        yield return StressShot(cam, eyeVox, lookVox, shotName);
        var ms = new List<double>();
        for (int i = 0; i < 300; i++) { yield return null; ms.Add(Time.unscaledDeltaTime * 1000.0); }
        double mean = 0; foreach (double v in ms) mean += v;
        sink((float)(mean / ms.Count));
    }

    // =====================================================================
    // -shadowstats : STAGE F's INSTRUMENTATION, BEFORE ANY GUESS
    //
    // Rung 1 measures indistinguishable from rung 0. §E.2 offered a hypothesis
    // (the half-res branch rarely engages, because most hit pixels at the test
    // pose are nearer than the 128 m LOD1 boundary). This settles it: it counts,
    // frame-wide, how many pixels hit, how many are beyond LOD1, how many rays
    // are ACTUALLY TRACED, and how many took a reused value.
    //
    // Two outcomes and they point at different bugs:
    //   - branch rarely engages  -> the THRESHOLD or the pose is wrong, and the
    //     mechanism is untested rather than broken;
    //   - branch engages often but traced-ray count does not fall -> the
    //     "half-res" path is not actually skipping work;
    //   - branch engages, traced count DOES fall, and time does not -> the work
    //     is being skipped per-LANE but not per-WAVE, which on a SIMD GPU saves
    //     nothing because a wave costs the max over its lanes.
    // The third is the one a ray-count-only test would miss, which is why this
    // reports counts AND the arm is paired with a timing arm.
    // =====================================================================
    private IEnumerator ShadowStats()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;

        int gate = ArgInt("-gateres", 960);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);

        for (int i = 0; i < 300; i++) yield return null;
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }
        var cam = Camera.main;
        if (cam != null) cam.transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // GroundHorizon, Stage E's pose

        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        if (!HasFlag("-nofluid") && pg != null)
            pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        for (int i = 0; i < 120; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "ShadowStats", ts);
        Directory.CreateDirectory(_folder);

        L($"STAGE F -- SHADOW BRANCH ENGAGEMENT -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  gate {gate}x{gate * 9 / 16}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}");
        L($"pose GroundHorizon (Stage E's), fluid {(HasFlag("-nofluid") ? "OFF" : "LIVE")}");
        L($"LOD1 boundary = {LODConfig.TIER_OUTER_RANGE_M[1]} m (the threshold rung 1 keys off)");
        L("");
        L($"  {"time",-14}{"rung",5}{"hit",10}{"beyondLOD1",12}{"traced",10}{"reused",10}  {"%beyond",9}{"%traced/hit",12}");

        RaymarchFeature.CollectShadowStats = true;
        foreach (float hours in new[] { SunCycle.GoldenHourHours, SunCycle.NoonHours })
        {
            SunCycle.SetHours(hours);
            foreach (int rung in new[] { 0, 1 })
            {
                RaymarchFeature.ShadowQualityLevel = rung;
                for (int i = 0; i < 30; i++) yield return null;
                RaymarchFeature.ResetShadowStats();
                yield return null;          // exactly one frame accumulates
                yield return new WaitForEndOfFrame();
                uint[] st = RaymarchFeature.ReadShadowStats();
                float hit = Mathf.Max(1u, st[0]);
                L($"  {SunCycle.ClockString() + " " + SunCycle.PhaseName,-14}{rung,5}" +
                  $"{st[0],10}{st[1],12}{st[2],10}{st[3],10}  " +
                  $"{100f * st[1] / hit,8:F1}%{100f * st[2] / hit,11:F1}%");
            }
        }
        RaymarchFeature.CollectShadowStats = false;

        L("");
        L("HOW TO READ THIS:");
        L("  %beyond   -- share of hit pixels past the LOD1 boundary. This is the");
        L("               ONLY population rung 1 can help. If it is small, rung 1");
        L("               cannot save much no matter how well it works.");
        L("  traced    -- rays actually cast. Rung 1 should cast ~1/4 as many as");
        L("               rung 0 over the 'beyond' population. If it does, the");
        L("               mechanism works and any missing speedup is SIMD lockstep");
        L("               (a wave costs the max over its lanes, not the sum).");
        File.WriteAllText(Path.Combine(_folder, "shadow_stats.txt"), _log.ToString());
        Debug.Log("[shadowstats]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    // =====================================================================
    // -daysweep : STAGE F.5 -- THE FULL DAY CYCLE, not just golden hour.
    //
    // §6.5 PREDICTS golden hour is the worst case ("the longest grazing rays"),
    // and every budget measurement in this project has taken that on trust and
    // sampled golden hour alone. It has never been swept and checked.
    //
    // DESIGN, AND THE COMPROMISE STATED UP FRONT: 12 sample points x a 300 s
    // cooldown each would be an hour per rung. This sweeps all 12 in ONE process
    // launch, back-to-back, with a settle between points -- so the points are
    // comparable TO EACH OTHER (same thermal state, same session, same pose)
    // and identify the WORST POINT relatively, which is the question. The
    // absolute number at that worst point is then re-measured in a separate
    // COOLED arm. Relative sweep to locate, cooled arm to quantify.
    //
    // The clock is PINNED per point (§A.8's confound): each sample is a fixed
    // time of day, not an average over one.
    // =====================================================================
    private IEnumerator DaySweep()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;

        int gate = ArgInt("-gateres", 960);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        int rung = ArgInt("-shadowlevel", 0);
        RaymarchFeature.ShadowQualityLevel = rung;

        for (int i = 0; i < 300; i++) yield return null;
        var cam = Camera.main;
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;

        bool withFluid = !HasFlag("-nofluid");
        if (withFluid && pg != null) pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        for (int i = 0; i < 150; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "DaySweep", $"{ts}_rung{rung}");
        Directory.CreateDirectory(_folder);

        L($"STAGE F.5 -- FULL DAY-CYCLE SWEEP -- rung {rung} -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  gate {gate}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  fluid {(withFluid ? "LIVE" : "OFF")}");
        L($"Back-to-back, ONE process, pose RESET per point, fly {ArgInt("-movespeed", 12)} m/s.");
        L("Points are comparable to");
        L("EACH OTHER and locate the worst case; the absolute figure at the worst");
        L("point is re-measured in a separate COOLED arm. Not a cooled gate itself.");
        L("");
        L($"  {"hour",6}{"elev",8}  {"phase",-15}{"meanMs",9}{"FPS",8}{">33/1000",10}");

        int samples = ArgInt("-daysamples", 12);
        int hold = ArgInt("-dayframes", 240);
        float worstMs = 0f; float worstHour = -1f; string worstPhase = "";
        var rows = new List<string>();

        // MOVE THE CAMERA THE WAY §F.2's ARMS DID -- fly forward at 12 m/s, so the
        // streamer, the clipmap upload and the cascade are all live. But RESET THE
        // POSE AT EVERY POINT: 12 x 300 frames of continuous flight is ~720 m of
        // travel, which would put the late points somewhere else in the world
        // entirely and let a change of PLACE masquerade as a change of TIME. Each
        // point therefore flies the same path from the same start.
        float moveSpeed = ArgInt("-movespeed", 12);

        for (int k = 0; k < samples; k++)
        {
            float hours = k * (24f / samples);
            SunCycle.SetHours(hours);
            if (pg != null) pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
            yield return null;
            if (cam != null) cam.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            for (int i = 0; i < 90; i++) yield return null;    // settle pose, clock, streaming

            var ms = new List<double>(hold);
            float t0 = Time.realtimeSinceStartup;
            for (int i = 0; i < hold; i++)
            {
                if (moveSpeed > 0f && cam != null)
                    cam.transform.position += cam.transform.forward * (moveSpeed * Time.deltaTime);
                yield return null;
                ms.Add(Time.unscaledDeltaTime * 1000.0);
            }
            float elapsed = Time.realtimeSinceStartup - t0;

            float mean = (float)(1000.0 * elapsed / ms.Count);
            float fps = ms.Count / elapsed;
            int over33 = ms.FindAll(x => x > 33.0).Count;
            L($"  {SunCycle.ClockString(),6}{SunCycle.ElevationDegrees,8:F1}  " +
              $"{SunCycle.PhaseName,-15}{mean,9:F2}{fps,8:F1}{1000.0 * over33 / ms.Count,10:F1}");
            rows.Add($"{hours}	{SunCycle.ElevationDegrees:F1}	{mean:F3}	{fps:F1}");
            if (mean > worstMs) { worstMs = mean; worstHour = hours; worstPhase = SunCycle.PhaseName; }
        }

        L("");
        L($"  >>> WORST POINT IN THE CYCLE: {worstHour:F1}h ({worstPhase}) at {worstMs:F2} ms " +
          $"= {1000f / worstMs:F1} FPS");
        L($"  >>> golden hour is {SunCycle.GoldenHourHours:F2}h -- §6.5 predicts it is the worst case.");
        L("      If the worst point above is NOT near it, that prediction is wrong for this");
        L("      content and the assumption every prior budget capture rests on needs revisiting.");
        File.WriteAllText(Path.Combine(_folder, "day_sweep.txt"), _log.ToString());
        File.WriteAllText(Path.Combine(_folder, "day_sweep.tsv"),
            "hours\televation\tmeanMs\tfps\n" + string.Join("\n", rows) + "\n");
        Debug.Log("[daysweep]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// Last match wins -- see the note on ArgValue.
    private static float ArgFloat(string flag, float dflt)
    {
        string v = ArgValue(flag);
        return v == null ? dflt : float.Parse(v, CultureInfo.InvariantCulture);
    }

    /// Last match wins -- see the note on ArgValue.
    private static int ArgInt(string flag, int dflt)
    {
        string v = ArgValue(flag);
        return v == null ? dflt : int.Parse(v, CultureInfo.InvariantCulture);
    }

    private static bool HasFlag(string f)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, f, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    IEnumerator Start()
    {
        if (HasFlag("-pitchsweep")) { yield return PitchSweep(); yield break; }
        if (HasFlag("-stutterprobe")) { yield return StutterProbe(); yield break; }
        if (HasFlag("-gputrace")) { yield return GpuTraceLoad(); yield break; }
        if (HasFlag("-fluidbench")) { yield return FluidBenchmark(); yield break; }
        if (HasFlag("-lightingab")) { yield return LightingAb(); yield break; }
        if (HasFlag("-p7stress")) { yield return Phase7Stress(); yield break; }
        if (HasFlag("-shadowstats")) { yield return ShadowStats(); yield break; }
        if (HasFlag("-daysweep")) { yield return DaySweep(); yield break; }
        if (HasFlag("-presetshots")) { yield return PresetShots(); yield break; }
        if (HasFlag("-sustained")) { yield return Sustained(); yield break; }
        if (HasFlag("-phase7demo")) { yield return Phase7Demo(); yield break; }
        if (HasFlag("-roofskipab")) { yield return RoofSkipAb(); yield break; }
        if (HasFlag("-upscaleab")) { yield return UpscaleAb(); yield break; }
        if (HasFlag("-lavalight")) { yield return LavaLightShots(); yield break; }
        if (HasFlag("-looksweep")) { yield return LookSweep(); yield break; }
        if (HasFlag("-farvisual")) { yield return FarVisual(); yield break; }
        if (HasFlag("-farpreview")) { yield return FarPreview(); yield break; }
        if (HasFlag("-flyfast")) { yield return FlyFast(); yield break; }
        if (HasFlag("-farseams")) { yield return FarSeams(); yield break; }
        if (HasFlag("-lodpop")) { yield return LodPop(); yield break; }
        if (HasFlag("-farshimmer")) { yield return FarShimmer(); yield break; }
        if (HasFlag("-nearshimmer")) { yield return NearShimmer(); yield break; }
        if (HasFlag("-taarisk")) { yield return TaaRisk(); yield break; }
        if (HasFlag("-fluidprobe")) { yield return FluidProbe(); yield break; }
        if (HasFlag("-poolprobe")) { yield return PoolProbe(); yield break; }
        if (HasFlag("-streamprobe")) { yield return StreamProbe(); yield break; }
        if (HasFlag("-flysweep")) { yield return FlySweep(); yield break; }
        if (HasFlag("-fluidroster")) { yield return FluidRoster(); yield break; }
        if (HasFlag("-devmap")) { yield return DevMapShots(); yield break; }
        if (!HasFlag("-playgroundshots")) yield break;
        Screen.SetResolution(1920, 1080, false);

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 240; i++) yield return null;   // let streaming settle

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, ts);
        Directory.CreateDirectory(_folder);
        L($"Playground first-pass look — {ts}");
        L($"device {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        L("Dogfood scene. Not a diagnostic scene, not a scale test, not a perf measurement.");
        L("");

        var cam = Camera.main;

        // ---- Terrain-only, several angles ----
        // Poses are around the island centre (~1280 m for sizeClass 1), not the
        // origin -- see the camera note in Phase5aSceneBuilder.GeneratePlayground.
        yield return Pose(cam, new Vector3(1280f, 14f, 1250f), new Vector3(10f, 0f, 0f), "01_terrain_wide");
        yield return Pose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(2f, 35f, 0f), "02_terrain_ground");
        yield return Pose(cam, new Vector3(1300f, 24f, 1240f), new Vector3(26f, -18f, 0f), "03_terrain_high");
        yield return Pose(cam, new Vector3(1280f, 5.5f, 1310f), new Vector3(-1f, 180f, 0f), "04_terrain_coast");

        // ---- Terrain + the fluid arena ----
        if (pg != null)
        {
            pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
            yield return null;

            // ELEVATED VANTAGE FOR THE FLUID SHOTS, added 2026-09-11.
            // TeleportToArena puts the camera 2.6 m above the basin centre,
            // which was fine when the vents emitted 310 voxels in total. At
            // the raised budgets the basin floods PAST that point and the
            // capture came back as a flat blue screen -- the camera was
            // underwater. The numbers were right (92,000 written, 202,107
            // live) and the picture showed none of it.
            var ac = Camera.main;
            if (ac != null)
            {
                Vector3 basePos = ac.transform.position;
                ac.transform.position = new Vector3(basePos.x - 5.5f, basePos.y + 7.5f,
                                                    basePos.z - 5.5f);
                ac.transform.rotation = Quaternion.Euler(34f, 45f, 0f);
            }
            yield return null;
            yield return Shot("05_arena_before", "the natural basin the arena sits in, before fluid");

            // 1/2/3 select a brush now; vents are opened explicitly.
            // THE BURST IS THE SCALE SHOWCASE, so the capture has to show it
            // rather than only the vents' trickle.
            pg.SendMessage("StartChaosBurst", SendMessageOptions.DontRequireReceiver);
            pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);

            for (int i = 0; i < 120; i++) yield return null;
            yield return Shot("06_arena_pouring", "CHAOS BURST mid-pour: ~51,000 voxels of water/sand/lava");
            for (int i = 0; i < 400; i++) yield return null;
            yield return Shot("07_arena_settling", "fluid finding the shape of the natural ground");
            for (int i = 0; i < 700; i++) yield return null;
            yield return Shot("08_arena_settled", "at rest on natural (non-flat) geometry");

            var c = Camera.main;
            c.transform.position += c.transform.forward * 2.2f + Vector3.up * 0.6f;
            yield return Shot("09_arena_close", "close on the fluid/terrain boundary");
        }

        L($"screenshots: {_shots}");
        File.WriteAllText(Path.Combine(_folder, "look_report.txt"), _log.ToString());
        Debug.Log("[PlaygroundCapture]\n" + _log);
        yield return null;
        Application.Quit(_shots >= 9 ? 0 : 1);
    }

    private IEnumerator Pose(Camera cam, Vector3 pos, Vector3 euler, string name)
    {
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Euler(euler);
        for (int i = 0; i < 90; i++) yield return null;   // let streaming catch up
        yield return Shot(name, $"pos {pos} euler {euler}");
    }

    // =====================================================================
    // -presetshots : STAGE J -- THE OFFLINE REVIEW PACKAGE
    //
    // The owner's note on the presets was "not a fan of any of them", and Stage
    // J's first job was to find out whether that was a preset problem or
    // something underneath all five. It was underneath: there was NO TONEMAPPING
    // ANYWHERE in this renderer -- the final colour was a bare saturate(), so
    // anything brighter than 1.0 clipped flat and every preset was being judged
    // through a hard clamp. §6.1 defers "palette/mood/post" to Phases 2 and 7,
    // so it is in scope, and it is now implemented (Reinhard and ACES, behind
    // LightingConfig.tonemap).
    //
    // THIS MODE DOES NOT PICK A WINNER AND MUST NOT. It produces the same nine
    // scenes under every preset, plus a three-way tonemap comparison on the
    // three scenes where tonemapping actually changes the picture, so the choice
    // can be made by looking rather than by reading someone's adjectives.
    //
    // The nine scenes are fixed and identical across presets by construction --
    // same eye, same target, same clock -- because a preset comparison in which
    // the camera moved is not a comparison.
    // =====================================================================
    private struct ShotScene
    {
        public string Name;
        public float Hours;
        public int3 Eye, Look;
        public string Why;
    }

    private IEnumerator PresetShots()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 300; i++) yield return null;

        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);

        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var driver = FindAnyObjectByType<VoxelEngine.Rendering.LightingDriver>();
        if (driver != null) driver.Attach(edits);
        var cam = Camera.main;

        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 peak = DeepestNear(store, camVox, 240);
        int surf = SurfaceYAt(store, peak.x, peak.z);

        // A cave with a torch in it, built ONCE and shared by every preset, so
        // scene 6/7 are the same room in all of them.
        int3 cave = new int3(peak.x - 50, Mathf.Max(16, surf - 45), peak.z + 15);
        for (int i = 0; i < 4; i++) Carve(edits, cave + new int3(i * 9 - 14, 0, 0), 11);
        int3 torchAt = cave + new int3(0, -8, 0);
        edits.TrySetVoxel(torchAt, Materials.Torch);
        for (int i = 0; i < 90; i++) yield return null;

        int3 top = new int3(peak.x, surf, peak.z);
        int3 sky = new int3(peak.x, surf + 55, peak.z - 90);
        var scenes = new ShotScene[]
        {
            new ShotScene { Name = "1_noon_surface",    Hours = 12f, Eye = sky, Look = top,
                Why = "the flat midday case -- where highlights clip first" },
            new ShotScene { Name = "2_golden_surface",  Hours = SunCycle.GoldenHourHours, Eye = sky, Look = top,
                Why = "long shadows and the warmest sun colour the preset defines" },
            new ShotScene { Name = "3_dusk",            Hours = 18f, Eye = sky, Look = top,
                Why = "sun at the horizon -- ambient is doing nearly all the work" },
            new ShotScene { Name = "4_midnight",        Hours = 0f,  Eye = sky, Look = top,
                Why = "the night floor: is it moody, or is it unreadably black?" },
            new ShotScene { Name = "5_snow_noon",       Hours = 12f,
                Eye = top + new int3(-35, 30, -35), Look = top,
                Why = "THE CLIPPING TEST. Snow at noon is the brightest surface in the game" },
            new ShotScene { Name = "6_cave_dark",       Hours = 12f,
                Eye = cave + new int3(-26, 2, 0), Look = cave + new int3(4, -2, 0),
                Why = "§6.5 requires caves be dark. Is this dark, or is it black?" },
            new ShotScene { Name = "7_cave_torchlit",   Hours = 12f,
                Eye = cave + new int3(-14, 1, 0), Look = torchAt,
                Why = "one torch, the falloff, and whether the far wall still reads" },
            new ShotScene { Name = "8_distant_terrain", Hours = SunCycle.GoldenHourHours,
                Eye = top + new int3(0, 45, -200), Look = top,
                Why = "200 m out, past the tier 1->2 boundary -- aerial and shadow at range" },
            new ShotScene { Name = "9_shadow_edge",     Hours = SunCycle.GoldenHourHours,
                Eye = top + new int3(-14, 8, -14), Look = top,
                Why = "close on a shadow edge: C.10's penumbra, or a hard step?" },
        };

        // The three scenes where tonemapping is the whole question.
        var tmScenes = new[] { "1_noon_surface", "5_snow_noon", "7_cave_torchlit" };
        var tmModes = new[] { (0, "None"), (1, "Reinhard"), (2, "ACES") };

        string root = Path.Combine(Application.persistentDataPath, "Presets");
        Directory.CreateDirectory(root);
        var presets = VoxelEngine.Rendering.LightingPresets.All;
        var topIndex = new StringBuilder();
        topIndex.AppendLine("# Phase 7 preset review package");
        topIndex.AppendLine();
        topIndex.AppendLine("Nine fixed scenes, shot identically under every preset. **No preset is");
        topIndex.AppendLine("recommended here and none is marked as a favourite** -- that is the owner's");
        topIndex.AppendLine("call and the whole reason this package exists.");
        topIndex.AppendLine();
        topIndex.AppendLine("Every folder also has a `tonemap/` set. Read that FIRST if you are deciding");
        topIndex.AppendLine("anything: until this stage there was no tonemapping in the renderer at all,");
        topIndex.AppendLine("so every earlier judgement of these presets was made through a hard clamp.");
        topIndex.AppendLine();
        topIndex.AppendLine($"Shot at {Screen.width}x{Screen.height}, shadow rung {RaymarchFeature.ShadowQualityLevel}, " +
                            $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")} build.");
        topIndex.AppendLine();

        for (int pi = 0; pi < presets.Length; pi++)
        {
            string name = presets[pi].Name;
            VoxelEngine.Rendering.LightingPresets.Apply(pi);
            for (int i = 0; i < 20; i++) yield return null;    // let §8.1's poll pick the file up

            _folder = Path.Combine(root, name);
            Directory.CreateDirectory(_folder);
            var idx = new StringBuilder();
            idx.AppendLine($"# {name}");
            idx.AppendLine();
            idx.AppendLine($"> {presets[pi].Note}");
            idx.AppendLine();
            var cfg = LightingConfig.Active;
            idx.AppendLine($"`sunIntensity` {cfg.sunIntensity:F2} | `ambientIntensity` {cfg.ambientIntensity:F2} | " +
                           $"`kSoft` {cfg.kSoft:F3} | `tonemap` {cfg.tonemap} | `exposure` {cfg.exposure:F2}");
            idx.AppendLine();
            idx.AppendLine("| # | image | time | what to look at |");
            idx.AppendLine("|---|---|---|---|");

            foreach (var sc in scenes)
            {
                SunCycle.SetHours(sc.Hours);
                for (int i = 0; i < 20; i++) yield return null;
                yield return StressShot(cam, sc.Eye, sc.Look, sc.Name);
                idx.AppendLine($"| {sc.Name[0]} | `{sc.Name}.png` | {SunCycle.ClockString()} | {sc.Why} |");
            }

            // ---- the tonemap axis ----
            string tmDir = Path.Combine(_folder, "tonemap");
            Directory.CreateDirectory(tmDir);
            string savedFolder = _folder;
            _folder = tmDir;
            foreach (string want in tmScenes)
            {
                foreach (var sc in scenes)
                {
                    if (sc.Name != want) continue;
                    SunCycle.SetHours(sc.Hours);
                    foreach (var (mode, label) in tmModes)
                    {
                        RaymarchFeature.TonemapOverride = mode;
                        for (int i = 0; i < 16; i++) yield return null;
                        yield return StressShot(cam, sc.Eye, sc.Look, $"{sc.Name}_tm{label}");
                    }
                    RaymarchFeature.TonemapOverride = -1;
                }
            }
            _folder = savedFolder;

            idx.AppendLine();
            idx.AppendLine("## tonemap/");
            idx.AppendLine();
            idx.AppendLine("The same three scenes under each tone curve. `None` is what every");
            idx.AppendLine("screenshot before Stage J looked like -- a bare `saturate()`, so anything");
            idx.AppendLine("over 1.0 clips to flat white and the surface detail in it is gone.");
            idx.AppendLine();
            idx.AppendLine("| scene | None (pre-Phase-7 clamp) | Reinhard | ACES |");
            idx.AppendLine("|---|---|---|---|");
            foreach (string want in tmScenes)
                idx.AppendLine($"| {want} | `tonemap/{want}_tmNone.png` | `tonemap/{want}_tmReinhard.png` | " +
                               $"`tonemap/{want}_tmACES.png` |");
            File.WriteAllText(Path.Combine(savedFolder, "INDEX.md"), idx.ToString());
            topIndex.AppendLine($"- **{name}/** — {presets[pi].Note}");
        }

        File.WriteAllText(Path.Combine(root, "INDEX.md"), topIndex.ToString());
        Debug.Log("[presetshots] wrote " + root);
        yield return null;
        Application.Quit(0);
    }

    // =====================================================================
    // -sustained : STAGE K -- THE MEASUREMENT EVERY PRIOR ONE SHOULD HAVE BEEN
    //
    // THE PROBLEM THIS EXISTS TO SOLVE: the owner sees ~30 FPS at golden hour in
    // real play. Last session measured 12.29 ms / 81 FPS at golden hour, cooled,
    // counterbalanced, fullscreen, release, live fluid, moving camera. That is a
    // 2.7x discrepancy between a careful measurement and the actual experience,
    // and until it is explained NO frame-time number in this project's history
    // can be called representative.
    //
    // THE SUSPECTED FLAW IN ALL PRIOR MEASUREMENTS: every single one was ~30
    // seconds of capture after a 300 s cooldown. That protocol was adopted to
    // remove thermal variance between ARMS -- and it does, which is why the A/B
    // comparisons in this document are still sound. But it also guarantees that
    // every arm reports the first 30 seconds of a cold machine, which on a
    // FANLESS M1 Air is the best case and nothing like a play session. §F.5
    // already caught the edge of this: its rung-3 control, doing identical work
    // at every sample, drifted +2.5 ms over ~7 minutes.
    //
    // SO THIS MODE DELIBERATELY DOES THE OPPOSITE OF EVERY OTHER PROBE HERE:
    // one continuous run of 15-20 minutes, NO cooldown pause anywhere in it,
    // reporting mean frame time in rolling 30 s windows for the whole duration.
    // The output is a CURVE, not a number. If it is flat, the cooled numbers
    // were representative and the discrepancy is something else (see K4). If it
    // climbs and settles, the settled value is the honest frame rate and every
    // headline in this document is a best case.
    //
    // THE CAMERA ORBITS rather than flying straight: 20 minutes at 12 m/s is
    // 14.4 km, which leaves the island entirely and would measure empty sky.
    // A circle keeps terrain in frame and the streamer genuinely working for
    // the whole run, which is the load being tested.
    // =====================================================================

    /// -motion walk: eye Y (metres) 1.6 m above the TerrainGen ground or still
    /// water at (xM, zM). Voxel-exact column (texel 1); carved tops (cave mouths)
    /// read as the uncarved top, which keeps the eye out of rock. Returns
    /// `fallbackY` on worlds without a TerrainGen source.
    private static float WalkEyeY(float xM, float zM, float fallbackY)
    {
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        if (src == null) return fallbackY;
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var col = TerrainGen.ShapeField.Sample(in spec, in view, in o, xM * 10f, zM * 10f, 1);
        int top = math.max(math.max(col.height, col.water), spec.seaLevelY);
        return (top + 1) * 0.1f + 1.6f;
    }

    private IEnumerator Sustained()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;

        int gate = ArgInt("-gateres", 960);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        }
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        string upscaleNote = ApplyUpscaleArgs();

        // -lavaslots N: decision 8's A/B arm. Lava's share of §3.8's cap; 0
        // reproduces pre-decision-8 behaviour exactly (lava lights nothing).
        int lavaSlots = ArgInt("-lavaslots", -1);
        if (lavaSlots >= 0) LightingConfig.Active.lavaLightMaxSlots = lavaSlots;
        var lightDriver = FindAnyObjectByType<VoxelEngine.Rendering.LightingDriver>();

        // -maxqueued N: DIAGNOSTIC SEAM for decision 11, not a setting. How many
        // frames the CPU may run ahead of the GPU; if the fluid readback's
        // latency is the GPU frame queue, it moves with this.
        int maxQueued = ArgInt("-maxqueued", -1);
        if (maxQueued >= 0) QualitySettings.maxQueuedFrames = maxQueued;

        // -lookgrade 0|1: decisions 2-4's A/B arm. MEASURE IT WITH -tod 12: the
        // grade's amount is 0 at the default golden-hour pin, so an arm there
        // would compare the shipped frame with itself.
        int lookGrade = ArgInt("-lookgrade", -1);
        if (lookGrade >= 0) SunCycle.LookGradeOverride = lookGrade;

        // -emissivebleed 0|1: faces next to lava/torches pick up their glow.
        int emissiveBleed = ArgInt("-emissivebleed", -1);
        if (emissiveBleed >= 0) RaymarchFeature.EmissiveBleedOverride = emissiveBleed > 0 ? 1 : 0;

        // The shadow-distance clamp, re-exposed here because §F.4 "disproved" it
        // AT THE WRONG POSE. That sweep ran inside the arena pit, where almost
        // nothing is sunlit and almost no shadow ray is traced, so a 12x change
        // in how far rays MAY travel moved nothing -- as it must, when the rays
        // are not running. At the open-ground pose the shadow ray costs ~9-12 ms
        // and §6.5's grazing-ray reasoning finally has something to bite on.
        float shClampM = ArgFloat("-shadowclamp", -1f);
        if (shClampM >= 0f) RaymarchFeature.ShadowMaxDistanceOverrideM = shClampM;

        // Shadow LOD coarsening by ray travel. The cost lever this session is
        // actually testing; 0 reproduces shipped behaviour exactly.
        float travelLod = ArgFloat("-shadowtravellod", -1f);
        if (travelLod >= 0f) RaymarchFeature.ShadowTravelLodOverride = travelLod;

        float ambient = ArgFloat("-ambient", -1f);
        if (ambient >= 0f) LightingConfig.Active.ambientIntensity = ambient;
        int matVar = ArgInt("-matvar", -1);
        if (matVar >= 0) RaymarchFeature.MaterialVariationOverride = matVar;

        // K4b: the overlays are part of what a player actually has on screen.
        // Default here is HUD ON, because the question is what the OWNER sees,
        // not what a clean capture pose sees. -nohud is the isolation arm.
        bool hud = !HasFlag("-nohud");
        Playground.DebugHideUi = !hud;

        for (int i = 0; i < 300; i++) yield return null;
        if (pg != null) { pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver); yield return null; }

        var cam = Camera.main;
        Vector3 centre = cam != null ? cam.transform.position : Vector3.zero;
        // FAR BAND arms (DESIGN_LARGE_WORLD §7 item 1): the same orbit, lifted
        // and tilted. -orbitheight is metres ADDED to the arena pose; -orbitpitch
        // tilts the view down (degrees). Both default 0 = the historical orbit.
        float orbitHeight = ArgFloat("-orbitheight", 0f);
        float orbitPitch = ArgFloat("-orbitpitch", 0f);
        centre.y += orbitHeight;

        float tod = ArgFloat("-tod", -1f);
        if (tod >= 0f) SunCycle.SetHours(tod);
        else SunCycle.SetHours(SunCycle.GoldenHourHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;

        bool withFluid = !HasFlag("-nofluid");
        if (withFluid && pg != null) pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);
        for (int i = 0; i < 180; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        string tag = ArgValue("-tag") ?? "K1";
        _folder = Path.Combine(Application.persistentDataPath, "Sustained", $"{ts}_{tag}");
        Directory.CreateDirectory(_folder);

        int totalSeconds = ArgInt("-minutes", 20) * 60;
        const float windowSeconds = 30f;
        float speed = ArgInt("-movespeed", 12);
        float radiusM = ArgInt("-orbitradius", 60);
        string motion = ArgValue("-motion") ?? (speed > 0f ? "orbit" : "static");

        // -chaos N: fire the G-key CHAOS BURST N seconds in (STAGE L). Placed on
        // the same clock as everything else so the window it lands in is
        // identifiable in the curve rather than inferred.
        int chaosAt = ArgInt("-chaos", -1);
        bool chaosFired = false;

        L($"STAGE K/L -- SUSTAINED-DURATION PROBE -- {tag} -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
          $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  {(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}");
        L($"gate {(gate > 0 ? gate + "x" + (gate * 9 / 16) : "as-shipped")}   " +
          $"shadow rung {RaymarchFeature.ShadowQualityLevel}   HUD {(hud ? "ON" : "OFF")}   " +
          $"fluid {(withFluid ? "LIVE" : "OFF")}   motion {motion} ({radiusM} m at {speed} m/s)   " +
          $"shadowClamp {(shClampM >= 0f ? shClampM + " m" : "inherit 290 m")}   " +
          $"travelLOD {RaymarchFeature.ShadowTravelLodOverride}   " +
          $"ambient {LightingConfig.Active.ambientIntensity:F2}");
        L($"clock PINNED at {SunCycle.ClockString()}  sun {SunCycle.ElevationDegrees:F1} deg ({SunCycle.PhaseName})");
        L($"upscale {upscaleNote ?? (HasFlag("-renderpreset") ? $"RenderPreset {RenderPreset.Current} ({RenderPreset.InternalWidth(RenderPreset.Current)} px wide internal, temporal shadows {(RaymarchFeature.TemporalShadows ? "ON" : "OFF")})" : "not set (as shipped)")}");
        L($"lava light share {LightingConfig.Active.lavaLightMaxSlots} of {EngineConfig.MAX_ACTIVE_LIGHTS} slots   " +
          $"look grade {(SunCycle.LookGradeOverride >= 0 ? SunCycle.LookGradeOverride : LightingConfig.Active.lookGrade)} " +
          $"(amount {SunCycle.LookGradeAmountAt(SunCycle.Hours):F2} at this hour)   maxQueuedFrames {QualitySettings.maxQueuedFrames}");
        L($"shadow roof skip {(RaymarchFeature.ShadowRoofSkip ? "ON" : "OFF")}   look {(RaymarchFeature.LookOverride >= 0 ? RaymarchFeature.LookOverride : LightingConfig.Active.look)}   emissive bleed {(RaymarchFeature.EmissiveBleedOverride >= 0 ? RaymarchFeature.EmissiveBleedOverride : LightingConfig.Active.emissiveBleed)}");
        var farBand = VoxelEngine.Rendering.FarField.Active;
        L($"FAR BAND {(farBand != null ? "ON" : "OFF")}" +
          (farBand != null ? $"  far shadows {(farBand.ShadowsOn ? "ON" : "OFF")}  haze {farBand.HazeRangeMetres:F0} m  " +
                             $"step caps {farBand.PrimaryStepCap}/{farBand.ShadowStepCap}  initial fill {farBand.InitialFillMs:F0} ms" : "") +
          $"   orbit height +{orbitHeight:F0} m  pitch {orbitPitch:F0} deg");
        L($"DURATION {totalSeconds / 60} min CONTINUOUS -- no cooldown, no pause. This is the");
        L("point: every other probe in this project reports the first 30 s of a cold machine.");
        if (chaosAt >= 0) L($"CHAOS BURST scheduled at t+{chaosAt}s");
        L("");
        // WHAT IS ACTUALLY IN FRAME. The K5 decomposition found a 3.6x gap
        // between two poses 60 m apart, which is not explainable by streaming
        // (the orbit never leaves the window) -- so the next question is
        // literally "what is on screen", and guessing at that is how the last
        // four sessions each lost a round. Shoot it instead.
        bool wantShot = !HasFlag("-noshot");
        if (wantShot)
        {
            Playground.DebugHideUi = true;
            yield return Shot("00_start_pose", $"{motion}: eye {cam.transform.position} " +
                                               $"fwd {cam.transform.forward}");
            Playground.DebugHideUi = !hud;
        }

        L($"  {"t(min)",8}{"meanMs",9}{"FPS",8}{">33/1000",10}{"vs first",10}  note");

        var rows = new List<string>();
        var windowMs = new List<double>(2048);
        float windowStart = Time.realtimeSinceStartup;
        float runStart = windowStart;
        int windowFrames = 0;
        float firstWindowMs = -1f;
        float lastWindowMs = -1f;
        float worstWindowMs = 0f, worstAtMin = 0f;
        float angle = 0f;
        long fluidTicksAtStart = pg != null ? pg.DebugFluidTicks : -1;
        var rb = pg != null ? pg.DebugReadback : null;
        long rbTicks0 = rb?.TicksLanded ?? 0, rbLat0 = rb?.LandLatencyFramesTotal ?? 0,
             rbDrain0 = rb?.DrainFramesTotal ?? 0, rbGap0 = rb?.IssueGapFramesTotal ?? 0,
             rbGapN0 = rb?.IssueGapsCounted ?? 0, rbOps0 = rb?.OpsInLandedBatchesTotal ?? 0;
        long[] rbHist0 = rb != null ? (long[])rb.LandLatencyHistogram.Clone() : null;
        int frames0 = Time.frameCount;
        long lightUploadsAtStart = lightDriver != null ? lightDriver.Lights.UploadsPerformed : -1;
        // -frametiming (2026-09-26): FrameTimingManager's CPU-main / GPU split,
        // so a floor that does not scale with pixels can be called CPU or GPU.
        // Off by default: every historical arm ran without the capture call.
        bool ftm = HasFlag("-frametiming");
        // -framegap (2026-09-26): attach FrameGapProbe for the whole run --
        // per-frame attribution of the >33 ms tail (engine frame start / our
        // Update / render+present, GC, streamer, far band, chunks admitted).
        FrameGapProbe gap = null;
        if (HasFlag("-framegap")) { gap = gameObject.AddComponent<FrameGapProbe>(); gap.Recording = true; }
        var ftmT = new FrameTiming[1];
        var ftmCpu = new List<double>(4096); var ftmGpu = new List<double>(4096);

        while (Time.realtimeSinceStartup - runStart < totalSeconds)
        {
            float elapsed = Time.realtimeSinceStartup - runStart;

            if (chaosAt >= 0 && !chaosFired && elapsed >= chaosAt)
            {
                chaosFired = true;
                if (pg != null) pg.SendMessage("StartChaosBurst", SendMessageOptions.DontRequireReceiver);
                L($"  >>> CHAOS BURST fired at t+{elapsed:F0}s");
            }

            // -motion DECOMPOSES WHAT "A MOVING CAMERA" ACTUALLY COSTS.
            //   orbit    translate + rotate (a player walking a circuit)
            //   straight translate only, fixed heading (what every prior probe did)
            //   spin     rotate only, no translation (a player looking around)
            //   static   neither
            // The four together separate translation cost from rotation cost,
            // which is the whole question §K4 is trying to answer.
            if (cam != null)
            {
                if (motion == "orbit" && speed > 0f)
                {
                    angle += (speed / Mathf.Max(1f, radiusM)) * Time.deltaTime;
                    cam.transform.position = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radiusM;
                    var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    cam.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(orbitPitch, 0f, 0f);
                }
                else if (motion == "walk" && speed > 0f)
                {
                    // -motion walk (2026-10-01): the orbit's circle, but at a
                    // walking player's eye -- 1.6 m above the generated ground
                    // (or the water surface) under the camera -- so the frame is
                    // what a player on foot sees. tg2 only: other worlds have no
                    // TerrainGen ground to sample and keep the orbit's height.
                    angle += (speed / Mathf.Max(1f, radiusM)) * Time.deltaTime;
                    Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radiusM;
                    p.y = WalkEyeY(p.x, p.z, p.y);
                    cam.transform.position = p;
                    var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    cam.transform.rotation = Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(orbitPitch, 0f, 0f);
                }
                else if (motion == "straight" && speed > 0f)
                {
                    cam.transform.position += cam.transform.forward * (speed * Time.deltaTime);
                }
                else if (motion == "spin")
                {
                    // Same ANGULAR rate as the orbit, so spin and orbit differ in
                    // translation alone rather than in how fast the view sweeps.
                    angle += (speed / Mathf.Max(1f, radiusM)) * Time.deltaTime;
                    cam.transform.rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
                }
            }

            yield return null;
            windowMs.Add(Time.unscaledDeltaTime * 1000.0);
            windowFrames++;
            if (ftm)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, ftmT) > 0)
                { ftmCpu.Add(ftmT[0].cpuMainThreadFrameTime); ftmGpu.Add(ftmT[0].gpuFrameTime); }
            }

            float wElapsed = Time.realtimeSinceStartup - windowStart;
            if (wElapsed >= windowSeconds)
            {
                float mean = (float)(1000.0 * wElapsed / windowFrames);
                float fps = windowFrames / wElapsed;
                int over33 = windowMs.FindAll(x => x > 33.0).Count;
                if (firstWindowMs < 0f) firstWindowMs = mean;
                lastWindowMs = mean;
                if (mean > worstWindowMs) { worstWindowMs = mean; worstAtMin = (Time.realtimeSinceStartup - runStart) / 60f; }

                string note = (chaosFired && elapsed - chaosAt < 90f) ? "  <- chaos burst active" : "";
                L($"  {(Time.realtimeSinceStartup - runStart) / 60f,8:F1}{mean,9:F2}{fps,8:F1}" +
                  $"{1000.0 * over33 / windowMs.Count,10:F1}{mean - firstWindowMs,10:+0.00;-0.00}{note}");
                int resident = Phase4Bootstrapper.Store != null ? Phase4Bootstrapper.Store.ResidentCount : -1;
                rows.Add($"{(Time.realtimeSinceStartup - runStart) / 60f:F3}\t{mean:F3}\t{fps:F2}\t{over33}\t{windowMs.Count}\t{resident}");

                windowMs.Clear(); windowFrames = 0; windowStart = Time.realtimeSinceStartup;
            }
        }

        // -memledger (2026-10-01): who owns the memory, by engine object
        // (MemoryLedger.cs). Taken after the timed loop so the walk's own cost
        // never lands in a measured window.
        if (HasFlag("-memledger"))
        {
            L("");
            L("  --- MEMORY LEDGER (end of run; GPU = Metal buffers, native = NativeArray, managed = arrays) ---");
            foreach (var line in MemoryLedger.Report()) L(line);
        }

        if (wantShot)
        {
            Playground.DebugHideUi = true;
            yield return Shot("99_end_pose", $"{motion}: eye {cam.transform.position}");
        }

        // SHADOW-RAY STEP COUNT, averaged over a short window. The decisive
        // counter for whether a shadow cost lever is actually engaging.
        if (RaymarchFeature.ShadowStatsBuffer != null)
        {
            RaymarchFeature.CollectShadowStats = true;
            RaymarchFeature.ResetShadowStats();
            const int statFrames = 60;
            for (int f = 0; f < statFrames; f++) yield return null;
            uint[] st = RaymarchFeature.ReadShadowStats();
            RaymarchFeature.CollectShadowStats = false;
            if (st != null && st.Length >= 5)
            {
                double tracedPf = st[2] / (double)statFrames;
                double stepsPf = st[4] / (double)statFrames;
                L("");
                L($"  SHADOW RAYS traced/frame {tracedPf:N0}   steps/frame {stepsPf:N0}   " +
                  $"steps per ray {(tracedPf > 0 ? stepsPf / tracedPf : 0):F1}");
                if (st.Length >= 8 && farBand != null)
                {
                    double farPf = st[5] / (double)statFrames, farStepsPf = st[6] / (double)statFrames, farHitPf = st[7] / (double)statFrames;
                    L($"  FAR RAYS/frame {farPf:N0}   far hits/frame {farHitPf:N0}   far steps/frame {farStepsPf:N0}   " +
                      $"steps per far ray {(farPf > 0 ? farStepsPf / farPf : 0):F1}   " +
                      $"ring: {farBand.StripsApplied} strips, {farBand.FullRefills} refills, {farBand.BytesUploaded / 1048576.0:F1} MB up");
                }
            }
        }

        if (pg != null && fluidTicksAtStart >= 0)
        {
            float secs = Time.realtimeSinceStartup - runStart;
            L("");
            L($"  fluid CA ticks/s {(pg.DebugFluidTicks - fluidTicksAtStart) / Math.Max(1f, secs):F1}   " +
              "(one tick per readback round-trip; this is fluid SPEED)");
        }
        if (rb != null)
        {
            long t = Math.Max(1, rb.TicksLanded - rbTicks0);
            long gn = Math.Max(1, rb.IssueGapsCounted - rbGapN0);
            float secs = Time.realtimeSinceStartup - runStart;
            L($"  TICK PERIOD (decision 11): {(Time.frameCount - frames0) / (float)t:F2} frames/tick over {t} ticks " +
              $"at {(Time.frameCount - frames0) / Math.Max(1f, secs):F1} FPS   maxQueuedFrames {QualitySettings.maxQueuedFrames}");
            L($"    issue -> landed   {(rb.LandLatencyFramesTotal - rbLat0) / (float)t:F2} frames  (readback latency)");
            L($"    landed -> drained {(rb.DrainFramesTotal - rbDrain0) / (float)t:F2} frames  (1 = applied the frame it landed; >1 = 1024-op budget carried it)");
            L($"    drained -> issue  {(rb.IssueGapFramesTotal - rbGap0) / (float)gn:F2} frames  (1 = CanIssue checked before PumpAndApply)");
            L($"    ops per tick      {(rb.OpsInLandedBatchesTotal - rbOps0) / (float)t:F0}   budget-limited frames {rb.BudgetLimitedFrames}");
            var sb = new StringBuilder("    latency histogram (frames:count)");
            for (int i = 0; i < rb.LandLatencyHistogram.Length; i++)
            {
                long c = rb.LandLatencyHistogram[i] - rbHist0[i];
                if (c > 0) sb.Append($"  {i}{(i == rb.LandLatencyHistogram.Length - 1 ? "+" : "")}:{c}");
            }
            L(sb.ToString());
        }
        if (lightDriver != null)
        {
            var lb = lightDriver.Lights;
            float secs = Time.realtimeSinceStartup - runStart;
            L($"  lights at end: {lb.ActiveCount} active ({lb.LavaActiveCount} lava aggregates), " +
              $"{lb.RegisteredCount} point lights, {lb.LavaVoxelCount} lava voxels in {lb.LavaTileCount} tiles");
            L($"  light-buffer uploads/s {(lb.UploadsPerformed - lightUploadsAtStart) / Math.Max(1f, secs):F2}   " +
              "(SetData on change only; each one is a potential GPU sync -- PHASE_7 §A)");
        }
        L("");
        if (gap != null)
        {
            gap.Recording = false;
            var gsb = new StringBuilder();
            gap.AppendReport(gsb, 33.0);
            L(gsb.ToString());
        }
        if (ftm && ftmCpu.Count > 0)
        {
            ftmCpu.Sort(); ftmGpu.Sort();
            L($"  FrameTimingManager ({ftmCpu.Count} frames): cpu main p50 {ftmCpu[ftmCpu.Count / 2]:F2} ms   " +
              $"gpu p50 {ftmGpu[ftmGpu.Count / 2]:F2} ms (GPU inflated ~2.6x on this Mac, Amdt 8.10 -- relative only)");
        }
        L($"  FIRST 30 s window: {firstWindowMs:F2} ms = {1000f / firstWindowMs:F1} FPS   <- what a cooled short arm reports");
        L($"  LAST  30 s window: {lastWindowMs:F2} ms = {1000f / lastWindowMs:F1} FPS   <- what sustained play gets");
        L($"  WORST 30 s window: {worstWindowMs:F2} ms = {1000f / worstWindowMs:F1} FPS at t+{worstAtMin:F1} min");
        if (firstWindowMs > 0f)
            L($"  DEGRADATION first -> last: {lastWindowMs - firstWindowMs:+0.00;-0.00} ms " +
              $"({100f * (lastWindowMs - firstWindowMs) / firstWindowMs:+0.0;-0.0}%)");
        L("");
        L("  Raw 30 s windows are in sustained.tsv -- the CURVE is the result, not the endpoint.");

        File.WriteAllText(Path.Combine(_folder, "sustained.txt"), _log.ToString());
        File.WriteAllText(Path.Combine(_folder, "sustained.tsv"),
            "minutes\tmeanMs\tfps\tframesOver33\tframesInWindow\tresidentChunks\n" + string.Join("\n", rows) + "\n");
        Debug.Log("[sustained]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    private IEnumerator Shot(string name, string caption)
    {
        for (int i = 0; i < 6; i++) yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_folder, name + ".png"), t.EncodeToPNG()); }
        finally { Destroy(t); }
        _shots++;
        L($"{name}.png — {caption}");
    }

    private void L(string s) => _log.AppendLine(s);
}
