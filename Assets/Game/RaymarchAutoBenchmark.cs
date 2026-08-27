// Assets/Game/RaymarchAutoBenchmark.cs
//
// v6 - adds LOD cascade on/off comparison configs, and mode-4 (DenseSkip)
// configs, both of which were simply absent before - see chat. This file
// predated the cascade entirely, and was never updated when mode 4 became
// the shipped default either, so the existing sweep could not have answered
// "is the cascade faster or slower" no matter how it was run. No existing
// config was removed, renamed, or reordered - every prior line is byte-for-
// byte unchanged, so this run's numbers for those configs remain directly
// comparable to every prior benchmark folder. Only additions.
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
// STILL TRUE, UNCHANGED BY v5/v6: this harness measures via FrameTimingManager,
// which is a fast in-engine signal for RELATIVE comparisons between configs,
// not a source of truth for an absolute ms figure against any hard gate.
// See run_metadata.txt's own printed reminder of this. Nothing in v5/v6
// changes that; both only make the (still-relative-only) output easier to
// collect, or add configs that were missing.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
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
        public bool useLODCascade;
    }

    // METHODOLOGY CONSTANTS, deliberately NOT [SerializeField] (v8).
    // They were serialized fields; the scene instance had stale values (25
    // samples / 600 attempt cap) silently overriding the code's own defaults
    // (60 / 1800), which is how a ~0.2s-per-config sampling window shipped
    // without anyone choosing it. Same silent-staleness class of bug that
    // already bit the capture rig's variant list. Consts can't go stale.
    //
    // Values raised sharply after a real measurement-validity failure (see
    // chat): the cap-ladder produced a physically impossible result
    // (cap1 - ONE loop iteration per ray - measured SLOWER than uncapped's
    // 400), and gpu_avg came in at a suspiciously constant ~2.6x wall_avg
    // across every config. Leading hypothesis for the inversion is GPU
    // frequency scaling on this fanless machine: a cheap config lets clocks
    // drop, and the next config gets sampled before they ramp back. A 30-
    // frame settle and 0.2s sampling window cannot survive that. These
    // values are a fix ATTEMPT, not a proven remedy - if the cap ladder is
    // still inverted after this run, DVFS was the wrong hypothesis and the
    // next step is isolating one config per process launch, not tuning
    // these numbers further.
    private const int _warmupFrames = 600;          // ~10s, one time, before any measurement
    private const int _settleFrames = 180;          // per-config, ~3s at 60fps (was 30, ~0.5s)
    private const int _targetValidSamples = 240;    // ~4s of samples per config (was 25, ~0.2s)
    private const int _minReliableSamples = 120;
    private const int _maxAttemptFrames = 3600;     // safety ceiling, ~60s worst case per config
    private const float _highVarianceCoefficientThreshold = 0.15f;

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
        var baseline = new Config { label = "Mode1-Reseed_FULL_uncapped", traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false };

        var list = new List<Config>
        {
            baseline,
            new Config { label = "Mode0-LeapSpan_FULL_uncapped",         traversalMode = 0, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false },
            new Config { label = "Mode2-Occupancy_FULL_uncapped",        traversalMode = 2, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false },
            new Config { label = "Mode3-ReseedClosedForm_FULL_uncapped", traversalMode = 3, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false },
            new Config { label = "Mode1-Reseed_STRIPPED_uncapped",       traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = true,  useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false },
            new Config { label = "Mode1-Reseed_MEMORYPROBE",             traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = true,  useLockBufferForAirMip = false, useLODCascade = false },
            new Config { label = "Mode1-Reseed_FULL_AirMipLock",         traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = true,  useLODCascade = false },
        };
        int[] caps = { 1, 4, 8, 16, 32, 64, 128, 400 };
        foreach (int c in caps)
            list.Add(new Config { label = $"Mode1-Reseed_FULL_cap{c}", traversalMode = 1, maxOuterIterations = c, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false });

        // Drift sanity check - MUST be numerically identical to the first
        // entry. If this doesn't match within a few percent, the sweep's
        // clock/thermal state moved during the run and every cross-config
        // comparison in this report inherits that uncertainty.
        list.Add(new Config { label = "Mode1-Reseed_FULL_uncapped_REPEAT_driftcheck", traversalMode = 1, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false });

        // --- v6 additions: mode 4 (DenseSkip) is the actual current shipped
        // default (per Amendment 8.9, RaymarchCaptureRig's own variants, and
        // RaymarchDebugControls' HUD), but was never in this sweep - every
        // config above predates it. Establishing its own no-cascade baseline
        // here, alongside the cascade on/off comparison, so the comparison
        // is against the mode that's actually in use, not mode 1.
        list.Add(new Config { label = "Mode4-DenseSkip_FULL_uncapped_CascadeOFF", traversalMode = 4, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false });

        // --- THE actual question this whole benchmark pass exists to answer:
        // same traversal mode, cascade on. RaymarchFeature computes its own
        // effective max ray distance when this is true (tier2's outer bound,
        // not the old 1280-voxel constant) - see RaymarchFeature.cs's own
        // comment on this - so this config is a genuinely different ray-
        // travel distance from every config above it, not just a flag flip.
        // That's expected and correct, not a confound to control for: it's
        // what the cascade is FOR.
        list.Add(new Config { label = "Mode4-DenseSkip_FULL_uncapped_CascadeON", traversalMode = 4, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = true });

        // Drift check specific to the cascade-on case - the newest, least-
        // measured config in this whole sweep, and the one this run exists
        // to trust. Must be numerically close to the CascadeON entry above;
        // if it isn't, the cascade number itself is drift-corrupted, not
        // just the old baseline configs.
        list.Add(new Config { label = "Mode4-DenseSkip_FULL_uncapped_CascadeON_REPEAT_driftcheck", traversalMode = 4, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = true });

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

        // v8: vsync OFF and no frame-rate cap, so wall_avg is a REAL measure
        // of frame cost rather than a clamp at the refresh interval. This
        // matters more than it used to: gpu_avg is now known to be suspect
        // (see the methodology-constants comment above), so wall_avg is the
        // cross-check, and a vsync-clamped wall_avg would be useless as one.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        // --- v5: one timestamped folder for this entire run, everything lives under it ---
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _runFolder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, timestamp);
        Directory.CreateDirectory(_runFolder);

        _report.AppendLine("=== Raymarch Standalone Benchmark v6 (+cascade configs) / v5 (packaging) / v4 (methodology) ===");
        _report.AppendLine($"Date: {DateTime.Now}");
        _report.AppendLine($"Gate resolution: {RaymarchFeature.LastDispatchResolution}");
        _report.AppendLine($"One-time warmup: {_warmupFrames} frames, per-config settle: {_settleFrames}");
        _report.AppendLine($"Sampling: until {_targetValidSamples} valid readings or {_maxAttemptFrames} attempted frames, whichever first.");
        _report.AppendLine($"Lines with fewer than {_minReliableSamples} valid readings are marked [LOW-CONFIDENCE].");
        _report.AppendLine($"Lines with stddev/mean > {_highVarianceCoefficientThreshold:P0} are marked [HIGH-VARIANCE] - a separate concern from sample count.");
        _report.AppendLine("v4 note: bigger n reduces noise WITHIN a config's own sampling window. It does NOT remove");
        _report.AppendLine("drift ACROSS the whole sweep - the REPEAT_driftcheck lines are still the real cross-sweep");
        _report.AppendLine("error bar (one for the original mode-1 baseline, one new for cascade-on - see v6 note).");
        _report.AppendLine("Read gpu_stddev/min/max as the honest spread, not just gpu_avg.");
        _report.AppendLine();
        _report.AppendLine("v6 note: Mode4-DenseSkip_FULL_uncapped_CascadeOFF vs _CascadeON is the actual comparison");
        _report.AppendLine("this run exists to produce - mode 4 is the current shipped default, and no prior run of");
        _report.AppendLine("this file ever included it or any LOD-cascade-aware config. Every other line in this");
        _report.AppendLine("report predates the cascade and remains directly comparable to prior benchmark folders.");
        _report.AppendLine();
        _report.AppendLine("v7 note: this main sweep runs at WHATEVER POSE the camera is at on launch - normally");
        _report.AppendLine("Phase2Bootstrapper's default spawn. Recorded explicitly below since prior runs of this");
        _report.AppendLine("file never stated which pose produced their numbers - a real traceability gap (see chat).");
        _report.AppendLine("A second, ground-level pose is benchmarked separately near the end of this report.");
        if (Camera.main != null)
        {
            var p = Camera.main.transform.position;
            var r = Camera.main.transform.eulerAngles;
            _report.AppendLine($"Main sweep camera pose: pos=({p.x:F1},{p.y:F1},{p.z:F1}) euler=({r.x:F1},{r.y:F1},{r.z:F1})");
        }
        else
        {
            _report.AppendLine("Main sweep camera pose: Camera.main was null when this was recorded - unknown.");
        }
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
        RaymarchFeature.UseLODCascade = false;
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
            RaymarchFeature.UseLODCascade = cfg.useLODCascade;
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

            // v8: gpu/wall ratio as an inline validity check. GPU time for a
            // frame cannot exceed that frame's wall-clock duration in a
            // non-deeply-pipelined loop, so ratio > ~1.0 means gpu_avg is
            // inflated and must NOT be read as an absolute figure. Flagged
            // per line rather than buried in a header note, because that
            // header note already existed and got ignored anyway (see chat).
            double gpuWallRatio = wallAvg > 0 ? gpuAvg / wallAvg : 0;
            string ratioFlag = gpuWallRatio > 1.2
                ? $"  [GPU/WALL={gpuWallRatio:F2} - gpu_avg INFLATED, use wall_avg for absolute cost]"
                : "";

            string line = $"{cfg.label,-45} gpu_avg={gpuAvg:F2}ms  gpu_stddev={gpuStddev:F2}ms  " +
                          $"gpu_min={(validCount > 0 ? gpuMin : -1):F2}ms  gpu_max={(validCount > 0 ? gpuMax : -1):F2}ms  " +
                          $"wall_avg={wallAvg:F2}ms  (valid={validCount}/{_targetValidSamples} target, attempted={attempted}/{_maxAttemptFrames}, {configElapsedSec:F1}s)" +
                          $"{confidenceFlag}{capFlag}{varianceFlag}{ratioFlag}";
            _report.AppendLine(line);
            Debug.Log("[AutoBenchmark] " + line);
        }

        // --- v7 addition: a SECOND pose, ground-level, reconnecting with
        // Amendment 8.9's own historical worst case (GroundHorizon - "ground
        // level, looking at the skyline, the real gameplay camera"). Added
        // after a direct, fair critique (see chat): every prior run of this
        // file measured only whatever pose Phase2Bootstrapper happens to
        // spawn at (top-down), and "that's close to worst case for the
        // cascade" was stated as a conclusion without ever being tested -
        // it wasn't. This doesn't fix that gap generally, but it closes the
        // single most important instance of it: does the ground-level,
        // actual-gameplay pose look better, worse, or about the same as the
        // top-down one already measured above.
        //
        // Deliberately narrow scope, not a full second 18-config sweep: only
        // the mode-4 cascade off/on comparison plus its driftcheck - the
        // specific comparison this whole benchmark pass exists to produce.
        // A broader multi-pose sweep is future work, not squeezed in here.
        _report.AppendLine();
        _report.AppendLine("=== SECOND POSE: GroundHorizon_Approx (ground level, reconnecting with ===");
        _report.AppendLine("=== Amendment 8.9's own historical worst-case framing - see chat)       ===");
        _report.AppendLine("Only the decisive mode-4 cascade comparison is re-run here, not the full config sweep.");
        _report.AppendLine();

        Camera cam = Camera.main;
        if (cam == null)
        {
            _report.AppendLine("No Camera.main found - cannot reposition for the second pose. Skipping.");
        }
        else
        {
            // Same generation-geometry sourcing convention as RaymarchCaptureRig
            // (kept a SEPARATE local copy rather than cross-referencing that
            // file, to keep this file self-contained - if Phase2Bootstrapper's
            // generation loop bounds change, both files' copies need updating).
            const int GENERATED_CHUNKS_XZ = 22;
            const float CHUNK_SIZE_M = 12.8f;
            const float GENERATED_SPAN_M = GENERATED_CHUNKS_XZ * CHUNK_SIZE_M;

            float groundY = FindGroundSurfaceY(30f, GENERATED_SPAN_M / 2f, 1.5f);
            cam.transform.position = new Vector3(30f, groundY, GENERATED_SPAN_M / 2f);
            cam.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // looking across the long axis of the world

            _report.AppendLine($"Camera repositioned to ({cam.transform.position.x:F1}, {cam.transform.position.y:F1}, {cam.transform.position.z:F1}), looking along +X.");

            for (int i = 0; i < _settleFrames * 3; i++) // extra settle - clipmap/terrain around the new position needs a moment
            {
                FrameTimingManager.CaptureFrameTimings();
                yield return null;
            }

            var secondPoseConfigs = new List<Config>
            {
                new Config { label = "GroundHorizon_Mode4-DenseSkip_CascadeOFF",                  traversalMode = 4, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = false },
                new Config { label = "GroundHorizon_Mode4-DenseSkip_CascadeON",                   traversalMode = 4, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = true },
                new Config { label = "GroundHorizon_Mode4-DenseSkip_CascadeON_REPEAT_driftcheck",  traversalMode = 4, maxOuterIterations = 400, useStrippedKernel = false, useMemoryProbeKernel = false, useLockBufferForAirMip = false, useLODCascade = true },
            };

            foreach (var cfg in secondPoseConfigs)
            {
                RaymarchFeature.TraversalMode = cfg.traversalMode;
                RaymarchFeature.MaxOuterIterations = cfg.maxOuterIterations;
                RaymarchFeature.UseStrippedKernel = cfg.useStrippedKernel;
                RaymarchFeature.UseMemoryProbeKernel = cfg.useMemoryProbeKernel;
                RaymarchFeature.UseLODCascade = cfg.useLODCascade;
                TerrainClipmap.UseLockBufferForAirMip = cfg.useLockBufferForAirMip;

                for (int i = 0; i < _settleFrames; i++)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                }

                double gpuSum2 = 0, gpuSumSq2 = 0, gpuMin2 = double.MaxValue, gpuMax2 = double.MinValue;
                int validCount2 = 0, attempted2 = 0;
                double wallSum2 = 0; // v8 fix: the second pose never recorded wall time, so its
                                     // numbers could only be ESTIMATED from the first pose's
                                     // gpu/wall ratio - and gpu_avg is exactly the figure now
                                     // known to be inflated. Measuring it directly instead.

                while (validCount2 < _targetValidSamples && attempted2 < _maxAttemptFrames)
                {
                    float frameStartRealtime2 = Time.realtimeSinceStartup;

                    FrameTimingManager.CaptureFrameTimings();
                    uint got2 = FrameTimingManager.GetLatestTimings(1, _timings);
                    attempted2++;
                    if (got2 > 0 && _timings[0].gpuFrameTime > 0)
                    {
                        double v2 = _timings[0].gpuFrameTime;
                        gpuSum2 += v2;
                        gpuSumSq2 += v2 * v2;
                        if (v2 < gpuMin2) gpuMin2 = v2;
                        if (v2 > gpuMax2) gpuMax2 = v2;
                        validCount2++;
                    }

                    yield return null;

                    wallSum2 += (Time.realtimeSinceStartup - frameStartRealtime2) * 1000.0;
                }

                double gpuAvg2 = validCount2 > 0 ? gpuSum2 / validCount2 : -1;
                double gpuVariance2 = validCount2 > 1 ? System.Math.Max(0, (gpuSumSq2 / validCount2) - (gpuAvg2 * gpuAvg2)) : 0;
                double gpuStddev2 = System.Math.Sqrt(gpuVariance2);
                double wallAvg2 = attempted2 > 0 ? wallSum2 / attempted2 : -1;
                string confidenceFlag2 = validCount2 >= _minReliableSamples ? "" : "  [LOW-CONFIDENCE]";

                double gpuWallRatio2 = wallAvg2 > 0 ? gpuAvg2 / wallAvg2 : 0;
                string ratioFlag2 = gpuWallRatio2 > 1.2
                    ? $"  [GPU/WALL={gpuWallRatio2:F2} - gpu_avg INFLATED, use wall_avg for absolute cost]"
                    : "";

                string line2 = $"{cfg.label,-45} gpu_avg={gpuAvg2:F2}ms  gpu_stddev={gpuStddev2:F2}ms  " +
                              $"gpu_min={(validCount2 > 0 ? gpuMin2 : -1):F2}ms  gpu_max={(validCount2 > 0 ? gpuMax2 : -1):F2}ms  " +
                              $"wall_avg={wallAvg2:F2}ms  (valid={validCount2}/{_targetValidSamples} target, attempted={attempted2}/{_maxAttemptFrames})" +
                              $"{confidenceFlag2}{ratioFlag2}";
                _report.AppendLine(line2);
                Debug.Log("[AutoBenchmark] " + line2);
            }
        }
        _report.AppendLine();

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

    // Same ground-height-query approach already proven in RaymarchCaptureRig
    // (duplicated here, not shared, to keep this file self-contained). Scans
    // down from above any generated terrain until it finds solid ground,
    // rather than guessing a Y - guessed Y constants have failed twice
    // elsewhere in this project already (see chat).
    private float FindGroundSurfaceY(float worldX, float worldZ, float clearance)
    {
        var store = Phase2Bootstrapper.Store;
        if (store == null)
        {
            Debug.LogWarning("[AutoBenchmark] Phase2Bootstrapper.Store is null - can't query ground height, falling back to Y=2.");
            return 2f;
        }

        const float scanFromY = 15f;
        const float scanToY = -2f;
        const float scanStep = 0.2f;

        for (float y = scanFromY; y >= scanToY; y -= scanStep)
        {
            int3 voxel = CoordMath.WorldToVoxel(new float3(worldX, y, worldZ));
            if (store.GetVoxel(voxel) != 0)
                return y + clearance;
        }

        Debug.LogWarning($"[AutoBenchmark] No ground found scanning ({worldX:F1}, {scanFromY} to {scanToY}, {worldZ:F1}) - falling back to Y=2.");
        return 2f;
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