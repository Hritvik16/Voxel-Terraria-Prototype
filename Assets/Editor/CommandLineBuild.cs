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