// Assets/Editor/CommandLineBuild.cs
//
// Invoked from the command line as:
//   Unity -batchmode -quit -executeMethod CommandLineBuild.BuildPhase4Standalone
//
// Builds a RELEASE (non-development) macOS standalone player containing ONLY
// the Phase 4 Streaming scene, so the built app launches straight into
// Phase4Bootstrapper + Phase4AcceptanceRig with no menu, no scene picker,
// nothing to click.
//
// RELEASE, NOT DEVELOPMENT: a Development Build carries its own profiling
// overhead — a smaller version of the same inflation that made Editor
// Play-mode numbers untrustworthy. For frame time to mean what it says,
// this has to stay a plain Release build. Do not add BuildOptions.Development
// even for debugging; add temporary Debug.Log calls instead and re-build.
//
// Scene list is passed EXPLICITLY here rather than read from Build Settings'
// checkbox list, so the build is reproducible from the command line and
// can't silently pick up whatever happened to be checked in the Editor.
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CommandLineBuild
{
    private const string SCENE_PATH = "Assets/Scenes/Phase 4 Streaming.unity";
    private const string OUTPUT_PATH = "Builds/Phase4Acceptance.app";

    private const string PHASE5A_SCENE_PATH = "Assets/Scenes/Phase 5a Basin.unity";
    private const string PHASE5A_OUTPUT_PATH = "Builds/Phase5aAcceptance.app";

    /// Phase 5a's basin acceptance rig, same shape as BuildPhase4Standalone.
    /// RELEASE, not Development, for the same reason stated at the top of this
    /// file -- and additionally so Debug.isDebugBuild reads false in the rig's
    /// own report, where it is printed as evidence of which build produced the
    /// screenshots.
    private const string PLAYGROUND_SCENE_PATH = "Assets/Scenes/Playground.unity";
    private const string PLAYGROUND_OUTPUT_PATH = "Builds/Playground.app";

    public static void BuildPlaygroundStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PLAYGROUND_SCENE_PATH },
            locationPathName = PLAYGROUND_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] playground result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PLAYGROUND_OUTPUT_PATH);
    }

    private const string FLUID_ACTIVITY_SCENE_PATH = "Assets/Scenes/Fluid Activity.unity";
    private const string FLUID_ACTIVITY_OUTPUT_PATH = "Builds/FluidActivity.app";

    public static void BuildFluidActivityStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { FLUID_ACTIVITY_SCENE_PATH },
            locationPathName = FLUID_ACTIVITY_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] fluid activity result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(FLUID_ACTIVITY_OUTPUT_PATH);
    }

    private const string PLAYTEST_BUGS_SCENE_PATH = "Assets/Scenes/Playtest Bugs.unity";
    private const string PLAYTEST_BUGS_OUTPUT_PATH = "Builds/PlaytestBugs.app";

    /// STEP 0's two-bug diagnostic. Release build for consistency with every
    /// other rig here; it reports no timings, so the build type is not load-
    /// bearing for its conclusions.
    public static void BuildPlaytestBugsStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PLAYTEST_BUGS_SCENE_PATH },
            locationPathName = PLAYTEST_BUGS_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] playtest bugs result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PLAYTEST_BUGS_OUTPUT_PATH);
    }

    private const string PHASE6_SANDBOX_SCENE_PATH = "Assets/Scenes/Phase 6 Sandbox.unity";
    private const string PHASE6_SANDBOX_OUTPUT_PATH = "Builds/Phase6Sandbox.app";

    /// §13 Phase 6's INTEGRATED acceptance rig. RELEASE build -- step 8 reports
    /// frame time, and a development build would make those figures meaningless.
    public static void BuildPhase6SandboxStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE6_SANDBOX_SCENE_PATH },
            locationPathName = PHASE6_SANDBOX_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] phase6 sandbox result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE6_SANDBOX_OUTPUT_PATH);
    }

    private const string PHASE6_EDIT_SCENE_PATH = "Assets/Scenes/Phase 6 Edit.unity";
    private const string PHASE6_EDIT_OUTPUT_PATH = "Builds/Phase6Edit.app";

    /// §13 Phase 6 file 3's acceptance rig. Scene path and output path must
    /// agree with run-phase6-edit.sh.
    public static void BuildPhase6EditStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE6_EDIT_SCENE_PATH },
            locationPathName = PHASE6_EDIT_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] phase6 edit result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE6_EDIT_OUTPUT_PATH);
    }

    private const string PHASE6_CCD_SCENE_PATH = "Assets/Scenes/Phase 6 CCD.unity";
    private const string PHASE6_CCD_OUTPUT_PATH = "Builds/Phase6Ccd.app";

    /// §13 Phase 6 file 2's acceptance rig. Scene path and output path must
    /// agree with run-phase6-ccd.sh.
    public static void BuildPhase6CcdStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE6_CCD_SCENE_PATH },
            locationPathName = PHASE6_CCD_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] phase6 ccd result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE6_CCD_OUTPUT_PATH);
    }

    private const string PHASE6_PLAYER_SCENE_PATH = "Assets/Scenes/Phase 6 Player.unity";
    private const string PHASE6_PLAYER_OUTPUT_PATH = "Builds/Phase6Player.app";

    /// §13 Phase 6 file 1's acceptance rig. Scene path and output path must
    /// agree with run-phase6-player.sh.
    public static void BuildPhase6PlayerStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE6_PLAYER_SCENE_PATH },
            locationPathName = PHASE6_PLAYER_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] phase6 player result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE6_PLAYER_OUTPUT_PATH);
    }

    private const string PHASE6_BRUSHGUARD_SCENE_PATH = "Assets/Scenes/Phase 6 Brush Guard.unity";
    private const string PHASE6_BRUSHGUARD_OUTPUT_PATH = "Builds/Phase6BrushGuard.app";

    /// The brush-guard end-to-end rig. RELEASE build, same as every other rig:
    /// the scene and the output path must agree with run-phase6-brushguard.sh,
    /// which is the mistake run-phase5d-rig.sh actually shipped with (it tested
    /// for a path nothing ever wrote and called a successful build a failure).
    public static void BuildPhase6BrushGuardStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE6_BRUSHGUARD_SCENE_PATH },
            locationPathName = PHASE6_BRUSHGUARD_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] phase6 brushguard result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE6_BRUSHGUARD_OUTPUT_PATH);
    }

    private const string PLAYGROUND_TRACE_OUTPUT_PATH = "Builds/PlaygroundTrace.app";

    /// DEVELOPMENT build of the Playground, for GPU capture ONLY.
    ///
    /// WHY A SEPARATE, NON-RELEASE BUILD EXISTS AT ALL. Metal debug groups are
    /// what makes a captured trace show "VE.FluidCA.CSIntent" instead of an
    /// anonymous compute encoder, and Unity emits them from
    /// CommandBuffer.BeginSample -- which is a PROFILER marker and is compiled
    /// out of a non-development player. Measured, not assumed: a release build
    /// captured with Instruments' Metal System Trace contained zero occurrences
    /// of any "VE." label anywhere in the trace bundle, and every encoder was
    /// labelled "Command Buffer 0". BuildOptions.Development is what turns the
    /// markers back on.
    ///
    /// THIS BUILD MUST NEVER BE USED FOR FRAME TIME. It carries development
    /// overhead by construction. It exists to attribute GPU work BETWEEN
    /// kernels, which is a ratio, not a budget. ./run-acceptance-rig.sh remains
    /// the only trusted frame-time source (CLAUDE.md), and it builds release.
    /// The output path is deliberately different from Builds/Playground.app so
    /// the two can never be confused.
    public static void BuildPlaygroundTraceStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PLAYGROUND_SCENE_PATH },
            locationPathName = PLAYGROUND_TRACE_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            // Development enables profiler markers -> Metal debug groups.
            // AllowDebugging is NOT set: a script debugger would change timing
            // far more than the markers do.
            options = BuildOptions.Development,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] playgroundTrace result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PLAYGROUND_TRACE_OUTPUT_PATH);
    }

    private const string PHASE5BDEMO_SCENE_PATH = "Assets/Scenes/Phase 5b Demo.unity";
    private const string PHASE5BDEMO_OUTPUT_PATH = "Builds/Phase5bDemo.app";

    /// The demo/playable build. RELEASE for the same reasons as every other
    /// build here; it is also the one a human launches and flies around in.
    public static void BuildPhase5bDemoStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE5BDEMO_SCENE_PATH },
            locationPathName = PHASE5BDEMO_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] phase5bdemo result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE5BDEMO_OUTPUT_PATH);
    }

    private const string PHASE5D_SCENE_PATH = "Assets/Scenes/Phase 5d Stream Fluid.unity";
    private const string PHASE5D_OUTPUT_PATH = "Builds/Phase5dStreamFluid.app";

    public static void BuildPhase5dStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE5D_SCENE_PATH },
            locationPathName = PHASE5D_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[CommandLineBuild] phase5d result={report.summary.result} " +
                  $"errors={report.summary.totalErrors} outputPath={report.summary.outputPath}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE5D_OUTPUT_PATH);
    }

    private const string PHASE5C_SCENE_PATH = "Assets/Scenes/Phase 5c Edit Stress.unity";
    private const string PHASE5C_OUTPUT_PATH = "Builds/Phase5cEditStress.app";

    public static void BuildPhase5cStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE5C_SCENE_PATH },
            locationPathName = PHASE5C_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"[CommandLineBuild] phase5c result={summary.result} errors={summary.totalErrors} " +
                  $"outputPath={summary.outputPath}");
        if (summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE5C_OUTPUT_PATH);
    }

    private const string PHASE5B_SCENE_PATH = "Assets/Scenes/Phase 5b Basin.unity";
    private const string PHASE5B_OUTPUT_PATH = "Builds/Phase5bValidation.app";

    /// Phase 5b's GPU-port validation rig. RELEASE, not Development: a
    /// Development build carries profiling overhead, and this rig takes a
    /// wall-clock frame-time reading (provisional, but not deliberately spoiled).
    public static void BuildPhase5bStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE5B_SCENE_PATH },
            locationPathName = PHASE5B_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"[CommandLineBuild] phase5b result={summary.result} errors={summary.totalErrors} " +
                  $"outputPath={summary.outputPath}");
        if (summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        DisableAppNap(PHASE5B_OUTPUT_PATH);
    }

    public static void BuildPhase5aStandalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { PHASE5A_SCENE_PATH },
            locationPathName = PHASE5A_OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"[CommandLineBuild] phase5a result={summary.result} " +
                  $"errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                  $"outputPath={summary.outputPath} sizeBytes={summary.totalSize}");

        if (summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);

        DisableAppNap(PHASE5A_OUTPUT_PATH);
    }

    public static void BuildPhase4Standalone()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { SCENE_PATH },
            locationPathName = OUTPUT_PATH,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None, // no Development, no AutoRunPlayer
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        Debug.Log($"[CommandLineBuild] result={summary.result} " +
                  $"errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                  $"outputPath={summary.outputPath} sizeBytes={summary.totalSize}");

        // Non-zero exit lets the driver shell script tell "build failed" from
        // "build succeeded" without scraping the log for a magic string.
        if (summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);

        DisableAppNap(OUTPUT_PATH);
    }

    /// Writes NSAppSleepDisabled into the built app's Info.plist.
    ///
    /// WHY THIS IS A BUILD STEP AND NOT A README LINE: macOS App Nap throttles
    /// and deschedules an app that is not frontmost and looks idle. The
    /// acceptance rig runs unattended, so the player IS that app, and the
    /// throttling lands directly in the numbers the rig exists to produce --
    /// measured Gate C frame p99 2316ms with App Nap active vs 961ms with it
    /// disabled, and FrameTimingManager sample validity 62% vs 93%, same build,
    /// same world, everything else identical.
    ///
    /// The workaround used while diagnosing this was
    ///     defaults write <bundleid> NSAppSleepDisabled -bool YES
    /// which lives in one user's preferences on one Mac. Every future build on
    /// every other machine -- and CI -- would have silently gone back to
    /// producing throttled numbers, with nothing in the repo to explain why the
    /// figures disagreed. Baking it into the bundle makes the property travel
    /// with the artifact.
    ///
    /// Plain text insertion rather than XML parsing on purpose: Info.plist
    /// carries a DOCTYPE, and XDocument's default DtdProcessing throws on it
    /// while Ignore silently drops the declaration on save. Unity generates this
    /// file, so the shape is predictable, and a targeted insert after the opening
    /// <dict> leaves every other byte untouched.
    private static void DisableAppNap(string appPath)
    {
        string plistPath = Path.Combine(appPath, "Contents", "Info.plist");
        if (!File.Exists(plistPath))
        {
            Debug.LogWarning($"[CommandLineBuild] No Info.plist at {plistPath}; App Nap NOT disabled.");
            return;
        }

        string text = File.ReadAllText(plistPath);
        if (text.Contains("NSAppSleepDisabled"))
        {
            Debug.Log("[CommandLineBuild] NSAppSleepDisabled already present.");
            return;
        }

        const string marker = "<dict>";
        int at = text.IndexOf(marker, System.StringComparison.Ordinal);
        if (at < 0)
        {
            Debug.LogWarning("[CommandLineBuild] Info.plist has no <dict>; App Nap NOT disabled.");
            return;
        }

        at += marker.Length;
        text = text.Insert(at, "\n\t<key>NSAppSleepDisabled</key>\n\t<true/>");
        File.WriteAllText(plistPath, text);
        Debug.Log($"[CommandLineBuild] NSAppSleepDisabled=true written into {plistPath}");
    }
}