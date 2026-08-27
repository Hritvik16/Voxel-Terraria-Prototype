// Assets/Game/RaymarchCaptureRig.cs
//
// v2 - "hit play, never touch this again" version. Changes from v1:
//
//  - Poses are no longer PLACEHOLDER_* stand-ins. They're computed from the
//    ACTUAL generation geometry (see GENERATED_CHUNKS_XZ / CHUNK_SIZE_M
//    below - both sourced, not guessed) so distances relative to the tier
//    boundaries are known in advance rather than discovered after a run.
//    TopDownAerial_Center reuses Phase2Bootstrapper's own default camera
//    spawn (52,84,52)/(90,0,0) - its own comments say that position was
//    picked specifically for top-down worst-case capture, so it's very
//    likely at or near the real deleted TopDownAerial pose, not a guess.
//    The other four are new, derived from the generated-area math to
//    deliberately exercise tier0-only, tier0->1, and tier1->2 crossings.
//
//  - NEW: per-capture LOD TIER COVERAGE. For every Cascade_LODTierView
//    capture, this rig reads the actual rendered pixels back (not just a
//    single debug pixel) and classifies each one against the three known
//    tier colors (via Color.gamma, so it's comparing against the exact
//    on-screen encoding, not a guessed byte value). Reported as a percentage
//    breakdown per capture, so "did tier 2 ever actually show up" is a
//    number in the report, not something that has to be re-derived by
//    eyeballing a screenshot.
//
//  - NEW: a SUMMARY block at the end of the report, aggregating tier
//    coverage across every capture in the run - one place to check whether
//    a run actually reached tier 2 at all before concluding anything from it.
//
//  - Screenshot capture switched from ScreenCapture.CaptureScreenshot(path)
//    (async, writes 1+ frames later, was the reason v1 needed two blind
//    yield-null frames of margin after capturing) to
//    ScreenCapture.CaptureScreenshotAsTexture() + manual EncodeToPNG() +
//    File.WriteAllBytes (synchronous). This is what makes the pixel
//    histogram possible in the first place, and is also just more reliable
//    timing-wise as a side effect.
//
// STILL NOT A PERFORMANCE BENCHMARK. quickGpuMs is still a single rough
// sample, still clearly labeled as such. Still not Xcode, per Rule 1 -
// nothing about that changed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

public class RaymarchCaptureRig : MonoBehaviour
{
    // --- Generation geometry this rig's poses are derived from ---
    // SOURCED, not guessed: Phase2Bootstrapper.Start() currently loops
    // cx,cz in [0,8) generating an 8x8 chunk patch; chunk size 12.8m is
    // ARCHITECTURE_v8.6.md §2.3 (16 bricks/edge x 0.8m/brick). If
    // Phase2Bootstrapper's generation loop bounds ever change, these two
    // constants are the only things that need updating for every pose below
    // to stay correctly targeted.
    private const int GENERATED_CHUNKS_XZ = 22;
    private const float CHUNK_SIZE_M = 12.8f;
    private const float GENERATED_SPAN_M = GENERATED_CHUNKS_XZ * CHUNK_SIZE_M; // 102.4m

    [Serializable]
    public struct CapturePose
    {
        public string label;
        public Vector3 position;
        public Vector3 eulerAngles;
        // NEW: when true, `position.y` is ignored and replaced at run time
        // with an actual queried ground surface height at (position.x,
        // position.z) plus groundClearance. Replaces hardcoded Y guesses,
        // which broke twice: once for GroundEdge at the old 8x8 world, again
        // for GroundCenter/GroundDiagonal_Reverse after the 22x22 bump moved
        // where "center" and "corner" physically land in the noise field.
        // A guessed constant is only ever right by luck; this can't be wrong
        // in the same way, because it's not a guess.
        public bool queryGroundHeight;
        public float groundClearance;
        [TextArea] public string purpose; // why this pose exists, shown in the report
    }

    [Serializable]
    public struct CaptureVariant
    {
        public string label;
        public bool useLODCascade;
        public RaymarchFeature.DebugMode debugMode;
        public int traversalMode;
    }

    // Version-gated rebuild. Replaces the earlier "is the list empty" /
    // "does any pose still say PLACEHOLDER" heuristics, which were each only
    // narrow enough to catch ONE specific kind of staleness - the variants
    // list went stale silently last run because neither heuristic covered
    // "count is non-zero but incomplete." Bump this any time BuildPoses() or
    // BuildVariants() changes and Awake()/Reset() will unconditionally
    // rebuild both from scratch, no label-matching guesswork needed.
    private const int RIG_VERSION = 5;
    [SerializeField] private int _builtFromVersion = 0;

    // Built in Awake()/Reset() from the constants above, NOT hand-typed
    // literals scattered through an Inspector list - see BuildPoses(). Still
    // exposed as a normal serialized list (visible/inspectable, and CAN be
    // hand-edited if ever wanted), but the version check above means a code
    // update always wins over stale Inspector data - no "go fix it by hand"
    // step required.
    [SerializeField] private List<CapturePose> _poses = new List<CapturePose>();
    [SerializeField] private List<CaptureVariant> _variants = new List<CaptureVariant>();

    [Header("Timing")]
    [Tooltip("Frames to wait after moving the camera / changing config before screenshotting - lets the clipmap/render settle.")]
    [SerializeField] private int _settleFrames = 30;
    [Tooltip("Frames sampled for the quick (non-rigorous) GPU ms reading, after settling.")]
    [SerializeField] private int _quickSampleFrames = 20;

    [Header("Output collection")]
    [SerializeField] private string _outputRootFolderName = "RaymarchCaptures";
    [SerializeField] private bool _zipWhenDone = true;
    [SerializeField] private bool _revealInFinderWhenDone = true;
    [SerializeField] private bool _copyPlayerLogWhenDone = true;

    [SerializeField] private bool _runOnStart = true;
    [SerializeField] private bool _quitWhenDone = true;

    private Camera _cam;
    private StringBuilder _report = new StringBuilder();
    private string _runFolder;
    private FrameTiming[] _timings = new FrameTiming[1];

    // Tier coverage aggregated across the WHOLE run, for the summary block.
    private bool _tier0SeenAnywhere = false;
    private bool _tier1SeenAnywhere = false;
    private bool _tier2SeenAnywhere = false;
    private bool _anyLODTierCaptureRun = false;

    private void Reset()
    {
        BuildPoses();
        BuildVariants();
        _builtFromVersion = RIG_VERSION;
    }

    private void Awake()
    {
        // Version-gated: if this GameObject has any serialized data from
        // before RIG_VERSION, rebuild BOTH lists unconditionally, no partial-
        // staleness guessing. This is what "just hit Play, no Inspector step,
        // ever" actually requires - a count check or a label check each only
        // catch one specific stale shape and silently miss others (this is
        // exactly how the variants list went stale last run: non-zero count,
        // so the old "count == 0" check never fired).
        if (_builtFromVersion != RIG_VERSION)
        {
            BuildPoses();
            BuildVariants();
            _builtFromVersion = RIG_VERSION;
        }
    }

    private void BuildPoses()
    {
        _poses = new List<CapturePose>
        {
            new CapturePose
            {
                label = "TopDownAerial_Center",
                position = new Vector3(52.0f, 84.0f, 52.0f),
                eulerAngles = new Vector3(90f, 0f, 0f),
                queryGroundHeight = false, // intentionally absolute, not ground-relative
                purpose = "Matches Phase2Bootstrapper's own default camera spawn (top-down, worst-case air-walk framing per its own comments). Height alone puts most of the frame past the 64m tier0->1 boundary - confirmed in prior runs to land entirely in tier 1, none in tier 0 or 2."
            },
            new CapturePose
            {
                label = "GroundCenter_Forward",
                position = new Vector3(GENERATED_SPAN_M / 2f, 0f, GENERATED_SPAN_M / 2f), // Y ignored, see queryGroundHeight
                eulerAngles = Vector3.zero,
                queryGroundHeight = true,
                groundClearance = 1.5f,
                purpose = "Ground level, center of the generated patch, looking forward. Nearby geometry stays inside tier0's 64m range from this position - exercises the tier0 dense-hit path (mode 4 micro-stepping) with cascade fully idle, the regression check against the pre-LOD baseline."
            },
            new CapturePose
            {
                label = "GroundDiagonal_CornerToCorner",
                position = new Vector3(5f, 0f, 5f), // Y ignored, see queryGroundHeight
                eulerAngles = new Vector3(0f, 45f, 0f),
                queryGroundHeight = true,
                groundClearance = 1.5f,
                purpose = $"Near one corner of the generated {GENERATED_SPAN_M:F0}m patch, looking toward the opposite corner (~{GENERATED_SPAN_M * 1.41f:F0}m diagonal). Designed to cross BOTH the 64m (tier0->1) and 128m (tier1->2) boundaries - the only pose in this set expected to ever show tier 2."
            },
            new CapturePose
            {
                label = "GroundDiagonal_ReverseCorner",
                position = new Vector3(GENERATED_SPAN_M - 5f, 0f, GENERATED_SPAN_M - 5f), // Y ignored
                eulerAngles = new Vector3(0f, 225f, 0f),
                queryGroundHeight = true,
                groundClearance = 1.5f,
                purpose = "Same diagonal as GroundDiagonal_CornerToCorner, shot from the opposite corner looking back. Symmetry check - if tier coverage looks very different from the forward diagonal, that's a real asymmetry worth investigating, not expected noise."
            },
            new CapturePose
            {
                label = "GroundEdge_LookOutOfBounds",
                // Previously a hardcoded Y (2m, then 15m after the first
                // "embedded in ground" failure) - both were guesses, and
                // guesses are only ever right by luck. Now queries actual
                // ground height at run time, so this can't fail this way
                // again regardless of where the generated area's edge lands.
                position = new Vector3(GENERATED_SPAN_M / 2f, 0f, GENERATED_SPAN_M - 3f), // Y ignored
                eulerAngles = new Vector3(15f, 0f, 0f), // slight downward pitch
                queryGroundHeight = true,
                groundClearance = 5f, // extra clearance - this pose is deliberately near a boundary/relief-prone edge
                purpose = "Near the edge of generated terrain, looking past it into unloaded chunks (read as uniform air by every cascade tier). Checks that rays traveling through unloaded space don't do anything degenerate with cascade on - should just fade to background, no artifacts. Height now queried at runtime rather than guessed (see in-code comment) after guessed constants failed twice."
            },
            new CapturePose
            {
                label = "HighAerial_TierTwoReach",
                // Purpose-built to reach tier 2, since neither ground-level
                // diagonal pose did (terrain relief occludes the sightline
                // well before 128m - see the run this pose was added after).
                // Math, and its one unverified assumption, spelled out:
                // corner slant distance = H / cos(halfDiagonalFOV). Using
                // Amendment 8.9's own cited 60-deg vFOV assumption (the same
                // one behind the spec's 128m tier-boundary figure) at a
                // 960x540 (16:9) aspect gives a half-diagonal FOV of ~49.6deg,
                // cos(49.6deg)=0.649, so corner slant = H/0.649 = 1.54*H.
                // At H=95m: center~95m (tier1, margin below 128), corner~
                // 146m (tier2, margin above 128). CONFIRMED, not just
                // predicted, as of the run that first showed tier2=9.7% for
                // this pose - the FOV assumption held up in practice.
                position = new Vector3(GENERATED_SPAN_M / 2f, 95.0f, GENERATED_SPAN_M / 2f),
                eulerAngles = new Vector3(90f, 0f, 0f),
                queryGroundHeight = false, // intentionally absolute height, not ground-relative
                purpose = "Purpose-built to reach tier 2: top-down from 95m. CONFIRMED (not just predicted) to reach tier 2 as of the run that first showed tier2=9.7% for this pose."
            },
            new CapturePose
            {
                label = "GroundHorizon_Approx",
                // EXACT same transform used in the RaymarchAutoBenchmark run
                // that found this is both the most expensive baseline pose
                // AND the pose with the worst cascade overhead (+50.9%, vs
                // +25.1% at TopDownAerial - see chat). This pose exists
                // specifically to visually diagnose WHERE that cost goes,
                // via the already-existing StepHeat/UniformDenseView
                // variants, rather than guessing a third shader fix blind.
                position = new Vector3(30f, 0f, 140.8f), // Y ignored, see queryGroundHeight
                eulerAngles = new Vector3(0f, 90f, 0f),
                queryGroundHeight = true,
                groundClearance = 1.5f,
                purpose = "Matches RaymarchAutoBenchmark's GroundHorizon_Approx pose exactly - ground level, looking along the long axis of the world. Measured as both the most expensive baseline (cascade off) AND the worst cascade overhead (+50.9%) of any pose tested. This capture is for diagnosing WHERE that cost concentrates (StepHeat/UniformDenseView), not re-measuring the ms figures - those come from the benchmark, not this rig."
            },
        };
    }

    private void BuildVariants()
    {
        _variants = new List<CaptureVariant>
        {
            new CaptureVariant { label = "Baseline_CascadeOff_Beauty",         useLODCascade = false, debugMode = RaymarchFeature.DebugMode.Beauty,       traversalMode = 4 },
            // NEW: cascade-off StepHeat/UniformDenseView. Every diagnostic
            // view before this only existed with the cascade ON, which was
            // fine for diagnosing cascade overhead specifically, but useless
            // for the CURRENT question (Amendment 8.10 §6.2): isolating
            // baseline tier-0-only cost, cascade fully out of the picture.
            new CaptureVariant { label = "Baseline_CascadeOff_StepHeat",       useLODCascade = false, debugMode = RaymarchFeature.DebugMode.StepHeat,     traversalMode = 4 },
            new CaptureVariant { label = "Baseline_CascadeOff_UniformDenseView", useLODCascade = false, debugMode = RaymarchFeature.DebugMode.UniformDense, traversalMode = 4 },
            new CaptureVariant { label = "Cascade_Beauty",                    useLODCascade = true,  debugMode = RaymarchFeature.DebugMode.Beauty,       traversalMode = 4 },
            new CaptureVariant { label = "Cascade_LODTierView",               useLODCascade = true,  debugMode = RaymarchFeature.DebugMode.LODTier,      traversalMode = 4 },
            new CaptureVariant { label = "Cascade_UniformDenseView",          useLODCascade = true,  debugMode = RaymarchFeature.DebugMode.UniformDense, traversalMode = 4 },
            new CaptureVariant { label = "Cascade_StepHeat",                  useLODCascade = true,  debugMode = RaymarchFeature.DebugMode.StepHeat,     traversalMode = 4 },
        };
    }

    // Scans straight down from a height guaranteed above any generated
    // terrain (chunks are only ever generated at cy=0, i.e. within one
    // 12.8m-tall chunk layer, per Phase2Bootstrapper - see its generation
    // loop) until it finds the first solid voxel, returning that height plus
    // clearance. Replaces every hardcoded ground-level Y in this file.
    // Returns a fallback if Phase2Bootstrapper.Store isn't ready yet or no
    // ground is found in range - logged loudly, not silently, since a silent
    // fallback here is exactly how the last two "embedded in ground" bugs
    // went unnoticed until a screenshot was eyeballed.
    private float FindGroundSurfaceY(float worldX, float worldZ, float clearance)
    {
        var store = Phase2Bootstrapper.Store;
        if (store == null)
        {
            Debug.LogWarning("[CaptureRig] Phase2Bootstrapper.Store is null - can't query ground height, falling back to Y=2. This pose may be embedded in terrain.");
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

        Debug.LogWarning($"[CaptureRig] No ground found scanning ({worldX:F1}, {scanFromY} to {scanToY}, {worldZ:F1}) - falling back to Y=2. Check GENERATED_CHUNKS_XZ covers this XZ.");
        return 2f;
    }

    void Start()
    {
        _cam = Camera.main;
        if (_runOnStart) StartCoroutine(RunAll());
    }

    [ContextMenu("Run Capture Sweep Now")]
    public void RunNow() => StartCoroutine(RunAll());

    [ContextMenu("Reset Poses/Variants To Defaults")]
    public void ResetToDefaults()
    {
        BuildPoses();
        BuildVariants();
    }

    private IEnumerator RunAll()
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null)
        {
            Debug.LogError("[CaptureRig] No Camera.main found - cannot move the camera to any pose. Aborting.");
            yield break;
        }
        if (_poses.Count == 0) BuildPoses();
        if (_variants.Count == 0) BuildVariants();

        yield return new WaitForSeconds(1.5f);

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _runFolder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, timestamp);
        Directory.CreateDirectory(_runFolder);

        _report.AppendLine("=== Raymarch Capture Rig v2 - visual/correctness sweep ===");
        _report.AppendLine($"Date: {DateTime.Now}");
        _report.AppendLine($"Gate resolution: {RaymarchFeature.LastDispatchResolution}");
        _report.AppendLine($"Generated terrain assumed: {GENERATED_CHUNKS_XZ}x{GENERATED_CHUNKS_XZ} chunks, {GENERATED_SPAN_M:F1}m span " +
            "(from Phase2Bootstrapper's current generation loop - update GENERATED_CHUNKS_XZ in this file if that loop changes).");
        _report.AppendLine($"Poses: {_poses.Count}, Variants: {_variants.Count}, Total captures: {_poses.Count * _variants.Count}");
        _report.AppendLine();
        _report.AppendLine("REMINDER: quickGpuMs below is a SINGLE QUICK SAMPLE over " +
            $"{_quickSampleFrames} frames, NOT RaymarchAutoBenchmark's rigorous sample-until-valid " +
            "methodology. Treat it as a rough sanity signal only - if a number here needs to be trusted, " +
            "re-run it through RaymarchAutoBenchmark instead.");
        _report.AppendLine();
        _report.AppendLine("--- Pose purposes ---");
        foreach (var p in _poses)
            _report.AppendLine($"{p.label}: {p.purpose}");
        _report.AppendLine();

        foreach (var pose in _poses)
        {
            Vector3 resolvedPosition = pose.position;
            if (pose.queryGroundHeight)
            {
                float groundY = FindGroundSurfaceY(pose.position.x, pose.position.z, pose.groundClearance);
                resolvedPosition = new Vector3(pose.position.x, groundY, pose.position.z);
            }
            _cam.transform.position = resolvedPosition;
            _cam.transform.rotation = Quaternion.Euler(pose.eulerAngles);

            foreach (var variant in _variants)
            {
                RaymarchFeature.UseLODCascade = variant.useLODCascade;
                RaymarchFeature.UseDebugViewOverride = true;
                RaymarchFeature.DebugViewOverride = variant.debugMode;
                RaymarchFeature.TraversalMode = variant.traversalMode;

                Vector2Int dispatchRes = RaymarchFeature.LastDispatchResolution;
                Vector2Int debugPixel = new Vector2Int(Mathf.Max(0, dispatchRes.x / 2), Mathf.Max(0, dispatchRes.y / 2));
                RaymarchFeature.DebugPixel = debugPixel;

                for (int i = 0; i < _settleFrames; i++)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    yield return null;
                }

                double gpuSum = 0;
                int validCount = 0;
                for (int i = 0; i < _quickSampleFrames; i++)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    uint got = FrameTimingManager.GetLatestTimings(1, _timings);
                    if (got > 0 && _timings[0].gpuFrameTime > 0)
                    {
                        gpuSum += _timings[0].gpuFrameTime;
                        validCount++;
                    }
                    yield return null;
                }
                double quickGpuMs = validCount > 0 ? gpuSum / validCount : -1;

                yield return new WaitForEndOfFrame();

                string fileSafeLabel = SanitizeForFilename($"{pose.label}__{variant.label}");
                string tierCoverageText = "";

                // Synchronous capture-to-texture. Also enables the tier
                // histogram below, which the old async CaptureScreenshot(path)
                // approach could not have supported without a separate readback.
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                try
                {
                    byte[] png = shot.EncodeToPNG();
                    string screenshotPath = Path.Combine(_runFolder, fileSafeLabel + ".png");
                    File.WriteAllBytes(screenshotPath, png);

                    if (variant.debugMode == RaymarchFeature.DebugMode.LODTier)
                    {
                        _anyLODTierCaptureRun = true;
                        tierCoverageText = ComputeTierCoverage(shot);
                    }
                }
                finally
                {
                    // CaptureScreenshotAsTexture textures are not auto-managed -
                    // must destroy explicitly or every capture leaks a full-res
                    // texture for the rest of the run.
                    UnityEngine.Object.Destroy(shot);
                }

                string debugLine = ReadDebugBufferLine(debugPixel);

                string line = $"{pose.label,-28} / {variant.label,-24} pos=({resolvedPosition.x:F1},{resolvedPosition.y:F1},{resolvedPosition.z:F1}) dispatch={dispatchRes.x}x{dispatchRes.y} " +
                              $"quickGpuMs={quickGpuMs:F2}  {debugLine}" +
                              (string.IsNullOrEmpty(tierCoverageText) ? "" : $"  {tierCoverageText}") +
                              $"  screenshot={fileSafeLabel}.png";
                _report.AppendLine(line);
                Debug.Log("[CaptureRig] " + line);
            }
        }

        _report.AppendLine();
        _report.AppendLine("--- SUMMARY: LOD tier coverage across this entire run ---");
        if (!_anyLODTierCaptureRun)
        {
            _report.AppendLine("No Cascade_LODTierView captures ran this sweep - nothing to summarize.");
        }
        else
        {
            _report.AppendLine($"Tier 0 (blue)  seen in at least one capture: {(_tier0SeenAnywhere ? "YES" : "no")}");
            _report.AppendLine($"Tier 1 (green) seen in at least one capture: {(_tier1SeenAnywhere ? "YES" : "no")}");
            _report.AppendLine($"Tier 2 (red)   seen in at least one capture: {(_tier2SeenAnywhere ? "YES" : "no")}");
            if (!_tier2SeenAnywhere)
                _report.AppendLine("Tier 2 was NEVER observed this run - the coarse-brick dense/uniform path for the " +
                    "outermost tier remains visually unverified. If GroundDiagonal_CornerToCorner still didn't reach " +
                    "it, the generated terrain patch may need to be larger than the current " +
                    $"{GENERATED_SPAN_M:F0}m span for any pose to ever reach the 128m boundary with margin.");
        }

        // --- Automated pixel diagnostic scan (Amendment 8.10 §6.2 follow-up) ---
        // Fully automated per direct request - no manual pixel-picking, no
        // ContextMenu clicks. Scans a whole vertical strip through the hot
        // band GroundHorizon_Approx's cascade-off StepHeat capture showed,
        // reading the FULL debug buffer breakdown (not just the abbreviated
        // steps/denseMicroSteps this rig's normal per-capture line uses) at
        // every sampled row, so the exact shape of the cost - not just its
        // rough visual location - is in the report as numbers.
        yield return StartCoroutine(RunPixelDiagnosticScan());

        string reportPath = Path.Combine(_runFolder, "capture_report.txt");
        File.WriteAllText(reportPath, _report.ToString());
        Debug.Log($"[CaptureRig] Report written to: {reportPath}");

        WriteRunMetadata();
        if (_copyPlayerLogWhenDone) CopyPlayerLog();

        string zipPath = null;
        if (_zipWhenDone) zipPath = TryZipRunFolder();

        Debug.Log("[CaptureRig] ===== FULL REPORT =====\n" + _report.ToString());
        Debug.Log($"[CaptureRig] DONE. Run folder: {_runFolder}");
        if (!string.IsNullOrEmpty(zipPath))
            Debug.Log($"[CaptureRig] Zipped to: {zipPath}  <- send this one file");
        else
            Debug.Log("[CaptureRig] Zipping unavailable or failed this run - send the folder above (zip it by hand if needed).");

#if UNITY_STANDALONE_OSX
        if (_revealInFinderWhenDone)
        {
            try { System.Diagnostics.Process.Start("open", $"-R \"{_runFolder}\""); }
            catch (Exception e) { Debug.LogWarning($"[CaptureRig] Could not reveal run folder in Finder: {e.Message}"); }
        }
#endif

        if (_quitWhenDone)
        {
            yield return new WaitForSeconds(2f);
            Application.Quit();
        }
    }

    // Classifies every pixel in the captured frame against the three known
    // LOD tier colors from Raymarch.compute's DebugMode==6 branch, compared
    // via Color.gamma so the comparison is against the actual on-screen
    // (gamma-encoded) bytes, not a hand-guessed byte value. Generous
    // per-channel tolerance absorbs compression/blending noise. Any pixel
    // not close to one of the three known colors (background, HUD overlay,
    // text) is counted separately and excluded from the tier percentages.
    private string ComputeTierCoverage(Texture2D shot)
    {
        Color tier0 = new Color(0.1f, 0.3f, 0.9f).gamma;
        Color tier1 = new Color(0.15f, 0.85f, 0.2f).gamma;
        Color tier2 = new Color(0.9f, 0.15f, 0.15f).gamma;
        const float tolerance = 0.16f; // per-channel, 0-1 range (~40/255)

        Color32[] pixels = shot.GetPixels32();
        long t0 = 0, t1 = 0, t2 = 0, other = 0;

        foreach (Color32 p in pixels)
        {
            Color c = p;
            if (ColorClose(c, tier0, tolerance)) t0++;
            else if (ColorClose(c, tier1, tolerance)) t1++;
            else if (ColorClose(c, tier2, tolerance)) t2++;
            else other++;
        }

        long total = pixels.Length;
        float pct0 = 100f * t0 / total;
        float pct1 = 100f * t1 / total;
        float pct2 = 100f * t2 / total;
        float pctOther = 100f * other / total;

        if (t0 > 0) _tier0SeenAnywhere = true;
        if (t1 > 0) _tier1SeenAnywhere = true;
        if (t2 > 0) _tier2SeenAnywhere = true;

        return $"tierCoverage[tier0={pct0:F1}% tier1={pct1:F1}% tier2={pct2:F1}% other/hud={pctOther:F1}%]";
    }

    private static bool ColorClose(Color a, Color b, float tolerance)
    {
        return Mathf.Abs(a.r - b.r) < tolerance
            && Mathf.Abs(a.g - b.g) < tolerance
            && Mathf.Abs(a.b - b.b) < tolerance;
    }

    private string ReadDebugBufferLine(Vector2Int debugPixel)
    {
        if (RaymarchFeature.DebugBuffer == null)
            return "debugBuffer=null (feature has not rendered a frame yet)";

        float[] data = new float[128];
        RaymarchFeature.DebugBuffer.GetData(data);

        int totalOuterSteps = Mathf.RoundToInt(data[7]);
        int denseMicroSteps = Mathf.RoundToInt(data[13]);
        int tierAtHit = Mathf.RoundToInt(data[17]);
        int gpuUseLODCascade = Mathf.RoundToInt(data[18]);

        bool allZero = totalOuterSteps == 0 && data[0] == 0f && data[1] == 0f && data[2] == 0f;
        if (allZero)
            return $"debugPixel=({debugPixel.x},{debugPixel.y}) WARNING: all-zero read - pixel likely outside dispatch bounds this frame";

        return $"steps={totalOuterSteps} denseMicroSteps={denseMicroSteps} tierAtHit={tierAtHit} gpuUseLODCascade={gpuUseLODCascade}";
    }

    // Fully automated vertical pixel scan through the hot band found at
    // GroundHorizon_Approx (Amendment 8.10 §6.2 follow-up). Cascade OFF,
    // isolating tier-0-only cost specifically (that's the current open
    // question - the cascade itself already measured near-zero overhead
    // here). Reads the FULL debug buffer at every sampled row - not just the
    // abbreviated steps/denseMicroSteps this rig's normal capture line uses
    // - so the exact shape of where cost concentrates is in the report as
    // real numbers, not inferred from a color bucket.
    private IEnumerator RunPixelDiagnosticScan()
    {
        if (_cam == null) { _report.AppendLine("No camera - pixel diagnostic scan skipped."); yield break; }

        // Same pose as the GroundHorizon_Approx entry in _poses, computed
        // fresh here rather than looked up, so this scan works even if that
        // pose entry is ever renamed or reordered.
        float groundY = FindGroundSurfaceY(30f, GENERATED_SPAN_M / 2f, 1.5f);
        Vector3 scanPos = new Vector3(30f, groundY, GENERATED_SPAN_M / 2f);
        Quaternion scanRot = Quaternion.Euler(0f, 90f, 0f);

        // Run BOTH cascade states at the SAME pixels, so a hit distance or
        // tier at one pixel is directly comparable to the other - a single
        // cascade-off-only scan can't answer "what does cascade actually do
        // to this specific expensive pixel," which is exactly the question
        // that came up after the first version of this scan (see chat: a
        // cascade-off hit at 86.5m looked like it should engage tier 1, but
        // the same pose's LODTierView showed tier1=0.0% - those two numbers
        // were never actually comparable, since LODTierView used cascade ON
        // and this scan's first version used cascade OFF).
        yield return StartCoroutine(RunPixelScanPass(scanPos, scanRot, useCascade: false));
        yield return StartCoroutine(RunPixelScanPass(scanPos, scanRot, useCascade: true));
    }

    private IEnumerator RunPixelScanPass(Vector3 pos, Quaternion rot, bool useCascade)
    {
        _report.AppendLine();
        _report.AppendLine($"=== AUTOMATED PIXEL DIAGNOSTIC SCAN: GroundHorizon_Approx, cascade {(useCascade ? "ON" : "OFF")} ===");
        _report.AppendLine("Vertical strip at x=480 (frame center), y=100..350 step 10, covering the hot band and margin.");
        _report.AppendLine();

        _cam.transform.position = pos;
        _cam.transform.rotation = rot;

        RaymarchFeature.UseLODCascade = useCascade;
        RaymarchFeature.TraversalMode = 4;
        RaymarchFeature.UseDebugViewOverride = true;
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.Beauty; // irrelevant to the buffer read, kept simple

        for (int i = 0; i < _settleFrames * 3; i++)
        {
            FrameTimingManager.CaptureFrameTimings();
            yield return null;
        }

        _report.AppendLine($"{"pixelY",6} {"steps",6} {"denseMicroSteps",16} {"mipProbeCalls",14} {"exitIters",10} {"nonExitIters",13} {"chainLeaps",11} {"hitDistM",10} {"tierAtHit",10}");

        for (int py = 100; py <= 350; py += 10)
        {
            RaymarchFeature.DebugPixel = new Vector2Int(480, py);

            // Debug write only takes effect on the NEXT dispatch after
            // DebugPixel changes (same caveat RaymarchGpuDebugReadback's own
            // ContextMenu already documents) - wait two frames to be safe.
            yield return null;
            yield return null;

            if (RaymarchFeature.DebugBuffer == null)
            {
                _report.AppendLine($"{py,6}  DebugBuffer null - skipped");
                continue;
            }

            float[] data = new float[128];
            RaymarchFeature.DebugBuffer.GetData(data);

            int steps = Mathf.RoundToInt(data[7]);
            int denseMicroSteps = Mathf.RoundToInt(data[13]);
            int mipProbeCalls = Mathf.RoundToInt(data[14]);
            int exitIters = Mathf.RoundToInt(data[9]);
            int nonExitIters = Mathf.RoundToInt(data[10]);
            int chainLeaps = Mathf.RoundToInt(data[12]);
            float hitDistMeters = data[19] / 10f; // raw voxel units (m*10) -> meters
            int tierAtHit = Mathf.RoundToInt(data[17]);

            bool allZero = steps == 0 && data[0] == 0f && data[1] == 0f && data[2] == 0f;
            string line = allZero
                ? $"{py,6}  ALL-ZERO (pixel outside dispatch bounds this frame)"
                : $"{py,6} {steps,6} {denseMicroSteps,16} {mipProbeCalls,14} {exitIters,10} {nonExitIters,13} {chainLeaps,11} {hitDistMeters,10:F1} {tierAtHit,10}";

            _report.AppendLine(line);
            Debug.Log($"[CaptureRig][PixelScan cascade={useCascade}] " + line);
        }

        _report.AppendLine();
        _report.AppendLine("Columns: pixelY (dispatch-res, x fixed at 480) | steps = total outer iterations |");
        _report.AppendLine("denseMicroSteps = outer iterations spent single-stepping inside a dense brick |");
        _report.AppendLine("mipProbeCalls = outer loop passes (== steps, kept for direct comparison to");
        _report.AppendLine("RaymarchGpuDebugReadback's own field naming) | exitIters/nonExitIters = tier-0");
        _report.AppendLine("LeapSpan-family axis work | chainLeaps = tier-0 mode-2-style chained leaps (0 for mode 4) |");
        _report.AppendLine("hitDistM = distance to hit (or maxDist if no hit), in METERS | tierAtHit = which tier");
        _report.AppendLine("resolved the hit (0/1/2, meaningless when cascade is off - always 0 then).");
    }

    private static string SanitizeForFilename(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    private void WriteRunMetadata()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Run metadata (RaymarchCaptureRig v2) ===");
        sb.AppendLine($"Date: {DateTime.Now}");
        sb.AppendLine($"Unity version: {Application.unityVersion}");
        sb.AppendLine($"Platform: {Application.platform}");
        sb.AppendLine();
        sb.AppendLine("--- Graphics ---");
        sb.AppendLine($"Device name: {SystemInfo.graphicsDeviceName}");
        sb.AppendLine($"Device type (API): {SystemInfo.graphicsDeviceType}");
        sb.AppendLine($"Graphics memory (MB): {SystemInfo.graphicsMemorySize}");
        sb.AppendLine();
        sb.AppendLine("--- System ---");
        sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
        sb.AppendLine($"Processor: {SystemInfo.processorType}");
        sb.AppendLine($"Device model: {SystemInfo.deviceModel}");
        sb.AppendLine();
        sb.AppendLine("--- Gate / dispatch ---");
        sb.AppendLine($"Gate resolution (actual, this run): {RaymarchFeature.LastDispatchResolution}");
        sb.AppendLine();
        sb.AppendLine("--- Reminder ---");
        sb.AppendLine("This tool is for VISUAL/CORRECTNESS verification, not performance. quickGpuMs and the HUD's");
        sb.AppendLine("own GPU frame reading are both single rough samples, especially unreliable in the Editor.");
        sb.AppendLine("For any ms figure that needs to be trusted, use RaymarchAutoBenchmark in a standalone build.");

        string path = Path.Combine(_runFolder, "run_metadata.txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[CaptureRig] Metadata written to: {path}");
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
                Debug.Log($"[CaptureRig] Player log copied to: {dst}");
            }
            else
            {
                Debug.LogWarning($"[CaptureRig] Player log not found at expected path (\"{src}\") - skipping copy.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CaptureRig] Could not copy player log: {e.Message}");
        }
    }

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
            Debug.LogWarning($"[CaptureRig] Zip step unavailable/failed ({e.GetType().Name}: {e.Message}) - " +
                              "the run folder is still complete, just not zipped. Send the folder as-is.");
            return null;
        }
    }
}