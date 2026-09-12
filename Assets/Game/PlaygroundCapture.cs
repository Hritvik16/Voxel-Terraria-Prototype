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
        for (int i = 0; i < seconds * 60; i++)
        {
            if (moveSpeed > 0f && cam != null)
                cam.transform.position += cam.transform.forward * (moveSpeed * Time.deltaTime);
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
            pg.SendMessage("DebugOpenAllVents", SendMessageOptions.DontRequireReceiver);

            for (int i = 0; i < 120; i++) yield return null;
            yield return Shot("06_arena_pouring", "water/sand/lava falling into generated terrain");
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
