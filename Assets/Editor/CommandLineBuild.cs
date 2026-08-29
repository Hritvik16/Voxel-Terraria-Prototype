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
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CommandLineBuild
{
    private const string SCENE_PATH = "Assets/Scenes/Phase 4 Streaming.unity";
    private const string OUTPUT_PATH = "Builds/Phase4Acceptance.app";

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
    }
}