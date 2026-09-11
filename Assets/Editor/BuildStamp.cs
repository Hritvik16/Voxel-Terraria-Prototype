// Assets/Editor/BuildStamp.cs
//
// STAMPS EVERY STANDALONE BUILD WITH ITS GIT COMMIT AND BUILD TIME.
//
// WHY THIS EXISTS. "Is the app on disk actually built from the code I just
// wrote?" had no cheap answer, and the obvious check -- the .app's modified
// date -- LIES. Unity writes into an existing .app in place, so the bundle
// directory keeps whatever mtime it had when first created. Builds/Playground.app
// showed Sep 5 while the GameAssembly.dylib inside it was from Sep 10. That
// ambiguity cost a round of doubt over a build that was in fact current, and
// the only way it was settled was by grepping HUD string literals out of the
// shipped binary.
//
// Two outputs, deliberately:
//   Assets/Resources/BuildStamp.txt  -- ships INSIDE the player, so a running
//                                       build can show its own provenance in
//                                       the HUD. This is the one that cannot
//                                       be faked by a stale file on disk.
//   <output>.app.buildinfo.txt       -- sits beside the bundle, so provenance
//                                       is readable without launching it.
//
// IPreprocessBuildWithReport / IPostprocessBuildWithReport rather than a call
// in each of the 22 build methods in CommandLineBuild: a hook you have to
// remember to call is a hook that gets missed on build number 23.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class BuildStamp : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public const string ResourcePath = "Assets/Resources/BuildStamp.txt";
    /// Name without extension, for Resources.Load at runtime.
    public const string ResourceName = "BuildStamp";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        string stamp = Compose();
        Directory.CreateDirectory(Path.GetDirectoryName(ResourcePath));
        File.WriteAllText(ResourcePath, stamp);
        AssetDatabase.ImportAsset(ResourcePath, ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"[BuildStamp] {stamp.Replace('\n', ' ')}");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        try
        {
            string outPath = report.summary.outputPath;
            if (string.IsNullOrEmpty(outPath)) return;
            File.WriteAllText(outPath + ".buildinfo.txt", Compose() + "\n" +
                              $"output    {outPath}\n" +
                              $"result    {report.summary.result}\n");
        }
        catch (Exception e)
        {
            // Never fail a build over provenance bookkeeping.
            Debug.LogWarning($"[BuildStamp] could not write buildinfo: {e.Message}");
        }
    }

    private static string Compose()
    {
        string commit = Git("rev-parse --short HEAD");
        string dirty = string.IsNullOrEmpty(Git("status --porcelain")) ? "clean" : "DIRTY";
        string subject = Git("log -1 --format=%s");
        return $"commit    {commit} ({dirty})\n" +
               $"subject   {subject}\n" +
               $"built     {DateTime.Now.ToString("u", CultureInfo.InvariantCulture)}\n" +
               $"unity     {Application.unityVersion}";
    }

    private static string Git(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                return outp;
            }
        }
        catch { return "(git unavailable)"; }
    }
}
