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

        // Vent budgets are finite (water 160 / sand 90 / lava 60) and would
        // drain and settle partway through a 15 s window, leaving the tail of
        // the recording measuring an idle CA. Re-opening them keeps all three
        // materials genuinely in flight for the whole capture.
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

    private static bool HasFlag(string f)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, f, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    IEnumerator Start()
    {
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
