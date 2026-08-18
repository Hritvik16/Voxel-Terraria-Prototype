// Assets/Game/RaymarchAutoBenchmark.cs
//
// v5 - packaging/collection only. Does NOT change what v4 measures or how
// (still FrameTimingManager, still the same config sweep) - see the v4
// header comment below for that history. This pass answers a different
// question: "build, walk away, come back to one thing to send" - v4 already
// wrote one .txt to persistentDataPath and quit; that's fine for reading on
// this machine but awkward to hand off - no run metadata (which machine,
// which build, which parameters), no player log, one bare file to go find
// and attach by hand.
//
// Changes from v4:
//  - All output for one run now goes into its own timestamped folder under
//    <persistentDataPath>/RaymarchBenchmarks/<yyyy-MM-dd_HHmmss>/, not loose
//    in persistentDataPath directly. Re-running never overwrites a prior
//    run's data - every run gets its own folder.
//  - New run_metadata.txt written alongside the report: Unity version,
//    platform, graphics device name/type/memory, OS, processor, the gate
//    resolution actually used, and every sampling parameter (warmup/settle/
//    target/cap/variance-threshold) - so a report can be read on its own
//    later without cross-referencing whatever this .cs file looked like at
//    the time it ran.
//  - The player log (Application.consoleLogPath - wherever Unity is
//    actually writing it on this machine) is copied into the run folder too.
//    Driver/graphics warnings that never make it into the benchmark's own
//    Debug.Log calls still end up in that file, and it's easy to forget to
//    grab separately.
//  - Best-effort zip of the whole run folder to a single .zip beside it,
//    via System.IO.Compression. Wrapped in try/catch: if the build's API
//    Compatibility Level doesn't have System.IO.Compression.FileSystem
//    available, this silently no-ops and leaves the folder unzipped rather
//    than failing the run - the raw folder is still there either way.
//  - Best-effort Finder reveal of the run folder (macOS only, guarded by
//    UNITY_STANDALONE_OSX) right before quitting, so there's no need to go
//    hunting for persistentDataPath by hand.
//  - Final Debug.Log line prints the exact folder (and zip, if it worked)
//    path in full, so it's the last thing visible in the console/log even
//    if Finder-reveal or zipping didn't fire for some reason.
//  - No change to sampling methodology, config list, or the drift-check
//    line - still v4 underneath. See the ORIGINAL v4 NOTE below.
//
// ORIGINAL v4 NOTE (unchanged, kept verbatim for continuity):
// v4 - responds to a real finding from v3's own output: even with sample-
// until-valid (v3) fixing the n=0-1 validity crisis, the SAME config
// ("Mode1-Reseed_FULL_uncapped") measured 26.64ms in one full sweep and
// 35.77ms in the next, with that sweep's own REPEAT drift-check landing at
// 22.96ms - a 36% spread. Bigger n reduces noise WITHIN one config's brief
// sampling window; it does NOT remove drift ACROSS a multi-minute sweep, if
// the GPU's clock/thermal state is genuinely moving during that sweep. Both
// effects are probably present. v4 addresses the part more samples CAN fix
// (within-window noise) and, just as importantly, now REPORTS stddev/min/max
// per config instead of only a mean, so the residual spread is visible
// rather than hidden behind a bigger, falsely-precise-looking number. The
// sweep's own drift-check line remains the honest cross-sweep error bar -
// v4 doesn't try to eliminate that, just to stop obscuring it.
//
// STILL TRUE, UNCHANGED BY v5: this harness measures via FrameTimingManager,
// which is a fast in-engine signal for RELATIVE comparisons between configs,
// not a source of truth for an absolute ms figure against any hard gate.
// See run_metadata.txt's own printed reminder of this. Nothing in v5 changes
// that; v5 only makes the (still-relative-only) output easier to collect.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class RaymarchAutoBenchmark : MonoBehaviour
{
    private struct Config
    {
        public string label;
        public int traversalMode;
        public int maxOuterIterations;
        public bool useStrippedKernel;
        public bool useMemoryProbeKernel;
        public bool useLockBufferForAirMip;
    }

    [SerializeField] private int _warmupFrames = 600; // ~10s @ 60fps, ONE TIME, before any measurement
    [SerializeField] private int _settleFrames = 30;   // per-config, after the one-time warmup

    [Header("Sample-until-valid, with spread reporting (v4)")]
    [Tooltip("Stop sampling this config once this many VALID gpuFrameTime readings are collected.")]
    [SerializeField] private int _targetValidSamples = 60;
    [Tooltip("Below this many valid readings, the line is flagged LOW-CONFIDENCE in the report.")]
    [SerializeField] private int _minReliableSamples = 30;
    [Tooltip("Give up on this config after this many ATTEMPTED frames regardless of how many were valid " +
             "(safety cap - bounds worst-case time under heavy load on a fanless machine).")]
    [SerializeField] private int _maxAttemptFrames = 1800;
    [Tooltip("Flag a line HIGH-VARIANCE if stddev/mean exceeds this fraction, even when it has plenty of samples.")]
    [SerializeField] private float _highVarianceCoefficientThreshold = 0.15f;

    [Header("Output collection (v5)")]
    [Tooltip("Subfolder of Application.persistentDataPath that every run's own timestamped folder is created under.")]
    [SerializeField] private string _outputRootFolderName = "RaymarchBenchmarks";
    [Tooltip("Best-effort: zip the run folder to a single .zip beside it when done. Safe no-op if unavailable on this build.")]
    [SerializeField] private bool _zipWhenDone = true;
    [Tooltip("Best-effort, macOS standalone only: reveal the run folder in Finder when done.")]
    [SerializeField] private bool _revealInFinderWhenDone = true;
    [Tooltip("Copy Application.consoleLogPath (the player log) into the run folder when done, if it exists and is readable.")]
    [SerializeField] private bool _copyPlayerLogWhenDone = true;

    [SerializeField] private bool _runOnStart = true;
    [SerializeField] private bool _quitWhenDone = true;

    private FrameTiming[] _timings = new FrameTiming[1];
    private StringBuilder _report = new StringBuilder();
    private string _runFolder;

    private List<Config> BuildConfigs()
    {
        var baseline = new Config { label = "Mode1-Reseed_FULL_uncapped", traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false };

        var list = new List<Config>
        {
            baseline,
            new Config { label = "Mode0-LeapSpan_FULL_uncapped",         traversalMode = 0, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false },
            new Config { label = "Mode2-Occupancy_FULL_uncapped",        traversalMode = 2, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false },
            new Config { label = "Mode3-ReseedClosedForm_FULL_uncapped", traversalMode = 3, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false },
            new Config { label = "Mode1-Reseed_STRIPPED_uncapped",       traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = true,  useMemoryProbeKernel = false, useLockBufferForAirMip = false },
            new Config { label = "Mode1-Reseed_MEMORYPROBE",             traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = true,  useLockBufferForAirMip = false },
            new Config { label = "Mode1-Reseed_FULL_AirMipLock",         traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = true  },
        };
        int[] caps = { 1, 4, 8, 16, 32, 64, 128, 400 };
        foreach (int c in caps)
            list.Add(new Config { label = $"Mode1-Reseed_FULL_cap{c}", traversalMode = 1, maxOuterIterations = c, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false });

        // Drift sanity check - MUST be numerically identical to the first
        // entry. If this doesn't match within a few percent, the sweep's
        // clock/thermal state moved during the run and every cross-config
        // comparison in this report inherits that uncertainty.
        list.Add(new Config { label = "Mode1-Reseed_FULL_uncapped_REPEAT_driftcheck", traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false });

        return list;
    }

    void Start()
    {
        if (_runOnStart) StartCoroutine(RunAll());
    }

    [ContextMenu("Run Benchmark Now")]
    public void RunNow() => StartCoroutine(RunAll());

    private IEnumerator RunAll()
    {
        yield return new WaitForSeconds(1.5f); // let terrain/clipmap fully settle first

        // --- v5: one timestamped folder for this entire run, everything lives under it ---
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _runFolder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, timestamp);
        Directory.CreateDirectory(_runFolder);

        _report.AppendLine("=== Raymarch Standalone Benchmark v5 (packaging) / v4 (methodology) ===");
        _report.AppendLine($"Date: {DateTime.Now}");
        _report.AppendLine($"Gate resolution: {RaymarchFeature.LastDispatchResolution}");
        _report.AppendLine($"One-time warmup: {_warmupFrames} frames, per-config settle: {_settleFrames}");
        _report.AppendLine($"Sampling: until {_targetValidSamples} valid readings or {_maxAttemptFrames} attempted frames, whichever first.");
        _report.AppendLine($"Lines with fewer than {_minReliableSamples} valid readings are marked [LOW-CONFIDENCE].");
        _report.AppendLine($"Lines with stddev/mean > {_highVarianceCoefficientThreshold:P0} are marked [HIGH-VARIANCE] - a separate concern from sample count.");
        _report.AppendLine("v4 note: bigger n reduces noise WITHIN a config's own sampling window. It does NOT remove");
        _report.AppendLine("drift ACROSS the whole sweep - the REPEAT_driftcheck line at the end is still the real");
        _report.AppendLine("cross-sweep error bar. Read gpu_stddev/min/max as the honest spread, not just gpu_avg.");
        _report.AppendLine();
        _report.AppendLine("REMINDER (unchanged since v4, restated here since this file now travels on its own):");
        _report.AppendLine("this is a FrameTimingManager-based measurement. Treat it as a RELATIVE signal between");
        _report.AppendLine("configs in this same sweep, not as an absolute ms figure against any hard gate. See");
        _report.AppendLine("run_metadata.txt in this same folder for the machine/build this run came from.");
        _report.AppendLine();

        // --- ONE-TIME WARMUP at a fixed representative config, before any
        // measurement. Unchanged since v2 - this part wasn't the problem. ---
        RaymarchFeature.TraversalMode = 1;
        RaymarchFeature.MaxOuterIterations = 400;
        RaymarchFeature.UseStrippedKernel = false;
        RaymarchFeature.UseMemoryProbeKernel = false;
        TerrainClipmap.UseLockBufferForAirMip = false;
        _report.AppendLine($"[Warming up {_warmupFrames} frames before first measurement...]");
        Debug.Log($"[AutoBenchmark] Warming up {_warmupFrames} frames...");
        for (int i = 0; i < _warmupFrames; i++)
        {
            FrameTimingManager.CaptureFrameTimings();
            yield return null;
        }
        _report.AppendLine();

        foreach (var cfg in BuildConfigs())
        {
            RaymarchFeature.TraversalMode = cfg.traversalMode;
            RaymarchFeature.MaxOuterIterations = cfg.maxOuterIterations;
            RaymarchFeature.UseStrippedKernel = cfg.useStrippedKernel;
            RaymarchFeature.UseMemoryProbeKernel = cfg.useMemoryProbeKernel;
            TerrainClipmap.UseLockBufferForAirMip = cfg.useLockBufferForAirMip;

            for (int i = 0; i < _settleFrames; i++)
            {
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
            }

            double gpuSum = 0;
            double gpuSumSq = 0;
            double gpuMin = double.MaxValue;
            double gpuMax = double.MinValue;
            int validCount = 0;
            int attempted = 0;
            double wallSum = 0;
            float configStartRealtime = Time.realtimeSinceStartup;

            while (validCount < _targetValidSamples && attempted < _maxAttemptFrames)
            {
                float frameStartRealtime = Time.realtimeSinceStartup;

                FrameTimingManager.CaptureFrameTimings();
                uint got = FrameTimingManager.GetLatestTimings(1, _timings);
                attempted++;
                if (got > 0 && _timings[0].gpuFrameTime > 0)
                {
                    double v = _timings[0].gpuFrameTime;
                    gpuSum += v;
                    gpuSumSq += v * v;
                    if (v < gpuMin) gpuMin = v;
                    if (v > gpuMax) gpuMax = v;
                    validCount++;
                }

                yield return null;

                wallSum += (Time.realtimeSinceStartup - frameStartRealtime) * 1000.0;
            }

            double gpuAvg = validCount > 0 ? gpuSum / validCount : -1;
            double gpuVariance = validCount > 1 ? System.Math.Max(0, (gpuSumSq / validCount) - (gpuAvg * gpuAvg)) : 0;
            double gpuStddev = System.Math.Sqrt(gpuVariance);
            double wallAvg = attempted > 0 ? wallSum / attempted : -1;
            float configElapsedSec = Time.realtimeSinceStartup - configStartRealtime;

            bool reliable = validCount >= _minReliableSamples;
            bool hitAttemptCap = attempted >= _maxAttemptFrames && validCount < _targetValidSamples;
            double coefficientOfVariation = gpuAvg > 0 ? gpuStddev / gpuAvg : 0;
            bool highVariance = validCount >= 5 && coefficientOfVariation > _highVarianceCoefficientThreshold;

            string confidenceFlag = reliable ? "" : "  [LOW-CONFIDENCE]";
            string capFlag = hitAttemptCap ? "  [HIT ATTEMPT CAP]" : "";
            string varianceFlag = highVariance ? $"  [HIGH-VARIANCE cv={coefficientOfVariation:P0}]" : "";

            string line = $"{cfg.label,-45} gpu_avg={gpuAvg:F2}ms  gpu_stddev={gpuStddev:F2}ms  " +
                          $"gpu_min={(validCount > 0 ? gpuMin : -1):F2}ms  gpu_max={(validCount > 0 ? gpuMax : -1):F2}ms  " +
                          $"wall_avg={wallAvg:F2}ms  (valid={validCount}/{_targetValidSamples} target, attempted={attempted}/{_maxAttemptFrames}, {configElapsedSec:F1}s)" +
                          $"{confidenceFlag}{capFlag}{varianceFlag}";
            _report.AppendLine(line);
            Debug.Log("[AutoBenchmark] " + line);
        }

        // --- v5: everything below is packaging. Sampling is done above, unchanged from v4. ---

        string reportPath = Path.Combine(_runFolder, "raymarch_benchmark.txt");
        File.WriteAllText(reportPath, _report.ToString());
        Debug.Log($"[AutoBenchmark] Report written to: {reportPath}");

        WriteRunMetadata();
        if (_copyPlayerLogWhenDone) CopyPlayerLog();

        string zipPath = null;
        if (_zipWhenDone) zipPath = TryZipRunFolder();

        Debug.Log("[AutoBenchmark] ===== FULL REPORT =====\n" + _report.ToString());
        Debug.Log($"[AutoBenchmark] DONE. Run folder: {_runFolder}");
        if (!string.IsNullOrEmpty(zipPath))
            Debug.Log($"[AutoBenchmark] Zipped to: {zipPath}  <- send this one file");
        else
            Debug.Log($"[AutoBenchmark] Zipping unavailable or failed this run - send the folder above (zip it by hand if needed).");

#if UNITY_STANDALONE_OSX
        if (_revealInFinderWhenDone)
        {
            try { System.Diagnostics.Process.Start("open", $"-R \"{_runFolder}\""); }
            catch (Exception e) { Debug.LogWarning($"[AutoBenchmark] Could not reveal run folder in Finder: {e.Message}"); }
        }
#endif

        if (_quitWhenDone)
        {
            yield return new WaitForSeconds(2f); // give zip/Finder-reveal a moment to actually finish
            Application.Quit();
        }
    }

    private void WriteRunMetadata()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Run metadata ===");
        sb.AppendLine($"Date: {DateTime.Now}");
        sb.AppendLine($"Unity version: {Application.unityVersion}");
        sb.AppendLine($"Platform: {Application.platform}");
        sb.AppendLine($"Product / version: {Application.productName} {Application.version}");
        sb.AppendLine();
        sb.AppendLine("--- Graphics ---");
        sb.AppendLine($"Device name: {SystemInfo.graphicsDeviceName}");
        sb.AppendLine($"Device type (API): {SystemInfo.graphicsDeviceType}");
        sb.AppendLine($"Device vendor: {SystemInfo.graphicsDeviceVendor}");
        sb.AppendLine($"Graphics memory (MB): {SystemInfo.graphicsMemorySize}");
        sb.AppendLine($"Shader level: {SystemInfo.graphicsShaderLevel}");
        sb.AppendLine();
        sb.AppendLine("--- System ---");
        sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
        sb.AppendLine($"Processor: {SystemInfo.processorType}");
        sb.AppendLine($"Processor count: {SystemInfo.processorCount}");
        sb.AppendLine($"System memory (MB): {SystemInfo.systemMemorySize}");
        sb.AppendLine($"Device model: {SystemInfo.deviceModel}");
        sb.AppendLine();
        sb.AppendLine("--- Gate / dispatch ---");
        sb.AppendLine($"Gate resolution (actual, this run): {RaymarchFeature.LastDispatchResolution}");
        sb.AppendLine();
        sb.AppendLine("--- Sampling parameters, this run ---");
        sb.AppendLine($"warmupFrames: {_warmupFrames}");
        sb.AppendLine($"settleFrames (per config): {_settleFrames}");
        sb.AppendLine($"targetValidSamples: {_targetValidSamples}");
        sb.AppendLine($"minReliableSamples: {_minReliableSamples}");
        sb.AppendLine($"maxAttemptFrames: {_maxAttemptFrames}");
        sb.AppendLine($"highVarianceCoefficientThreshold: {_highVarianceCoefficientThreshold:P0}");
        sb.AppendLine();
        sb.AppendLine("--- Reminder ---");
        sb.AppendLine("This data is from FrameTimingManager (Unity, in-engine). It is a relative signal between");
        sb.AppendLine("the configs run in THIS sweep, run on THIS machine, at THIS point in time - not a validated");
        sb.AppendLine("absolute figure against any external gate. Per the project's own §10.2 (\"Native Profiling");
        sb.AppendLine("(Xcode) - The Measurement Rule\"), an authoritative absolute number requires a separate");
        sb.AppendLine("Xcode Instruments (Metal System Trace) capture, not this harness.");

        string path = Path.Combine(_runFolder, "run_metadata.txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[AutoBenchmark] Metadata written to: {path}");
    }

    private void CopyPlayerLog()
    {
        try
        {
            string src = Application.consoleLogPath;
            if (!string.IsNullOrEmpty(src) && File.Exists(src))
            {
                string dst = Path.Combine(_runFolder, "player_log.txt");
                File.Copy(src, dst, overwrite: true);
                Debug.Log($"[AutoBenchmark] Player log copied to: {dst}");
            }
            else
            {
                Debug.LogWarning($"[AutoBenchmark] Player log not found at expected path (\"{src}\") - skipping copy.");
            }
        }
        catch (Exception e)
        {
            // Non-fatal by design - a missing/locked log file should never abort the run or the report.
            Debug.LogWarning($"[AutoBenchmark] Could not copy player log: {e.Message}");
        }
    }

    // Best-effort. Returns the zip path on success, null on any failure (missing API, permissions,
    // whatever) - callers must treat null as "leave the raw folder as the deliverable," never as fatal.
    private string TryZipRunFolder()
    {
        try
        {
            string zipPath = _runFolder + ".zip";
            if (File.Exists(zipPath)) File.Delete(zipPath);
            System.IO.Compression.ZipFile.CreateFromDirectory(
                _runFolder, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: true);
            return zipPath;
        }
        catch (Exception e)
        {
            // Most likely cause: this build's API Compatibility Level doesn't expose
            // System.IO.Compression.FileSystem. That's fine - the folder itself is still complete.
            Debug.LogWarning($"[AutoBenchmark] Zip step unavailable/failed ({e.GetType().Name}: {e.Message}) - " +
                              "the run folder is still complete, just not zipped. Send the folder as-is.");
            return null;
        }
    }
}