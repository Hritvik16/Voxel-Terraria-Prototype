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

public class PlaygroundCapture : MonoBehaviour
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

    private static string ArgValue(string flag)
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
        return null;
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
        L($"  shadow rung {(shadowLevel >= 0 ? shadowLevel.ToString() : "default/none")}   " +
          $"turn {turnRate} deg/s   move {moveSpeed} m/s   " +
          $"lightUpload {(Playground.DebugSuspendLightUpload ? "SUSPENDED" : "on")}");
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

    private static int ArgInt(string flag, int dflt)
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (string.Equals(a[i], flag, StringComparison.OrdinalIgnoreCase))
                return int.Parse(a[i + 1], CultureInfo.InvariantCulture);
        return dflt;
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
