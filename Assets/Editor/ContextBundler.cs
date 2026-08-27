using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Linq;

// CHANGE: was scoped to a single hardcoded folder ("Assets/CoreEngine"), which meant
// Assets/Game (RaymarchAutoBenchmark.cs, Phase2Bootstrapper.cs, the debug/camera rigs,
// etc.) and Assets/ContentModules (EngineConfig.cs) never made it into the dump - had
// to be pasted by hand every time. Now scans the whole "Assets" folder recursively, so
// every .cs/.compute/.hlsl file in the project is included automatically, with no
// per-folder list to maintain going forward. Scenes/Settings/InputActions etc. are
// still naturally excluded, since the extension filter below only ever matches source
// files - there was never a need to enumerate folders for that part.
//
// Also now fires on BOTH play-mode transitions (was ExitingEditMode only, i.e. only
// when pressing Play) - added EnteredEditMode too, so stopping play mode also
// refreshes the dump. Source files can't actually change *during* a play session
// (Unity forces a domain reload on any script recompile), so this is redundancy for
// convenience, not a correctness fix - whichever transition you remember to wait for,
// the file is current.
[InitializeOnLoad]
public static class ContextBundler
{
    // The name of the file to generate in the root of your Unity project
    private const string OutputFileName = "Codebase_Context.txt";

    // Scans this whole tree, recursively. "Assets" covers CoreEngine, Game,
    // ContentModules, Editor - everything - without needing to list each one.
    private static readonly string[] TargetDirectories = new string[]
    {
        "Assets"
    };

    // Any path (relative to project root, forward-slash form) containing one of these
    // as a path segment is skipped even though it's under Assets/ and has a matching
    // extension. Empty by default - add to this if a vendored/third-party folder ever
    // needs excluding, rather than narrowing TargetDirectories back down.
    private static readonly string[] ExcludeContains = new string[]
    {
        // e.g. "Assets/ThirdParty",
    };

    private static readonly string[] TrackedExtensions = new string[] { ".cs", ".compute", ".hlsl" };

    static ContextBundler()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode)
        {
            GenerateContextFile();
        }
    }

    [MenuItem("Voxel Engine/Generate Context")]
    public static void GenerateContextFile()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputPath = Path.Combine(projectRoot, OutputFileName);
        StringBuilder sb = new StringBuilder();

        var collected = new System.Collections.Generic.List<string>();

        foreach (string relativeDir in TargetDirectories)
        {
            string absoluteDir = Path.Combine(projectRoot, relativeDir);
            if (!Directory.Exists(absoluteDir)) continue;

            string[] files = Directory.GetFiles(absoluteDir, "*.*", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                if (!TrackedExtensions.Any(ext => file.EndsWith(ext))) continue;

                string relPath = file.Replace(projectRoot + Path.DirectorySeparatorChar, "").Replace('\\', '/');

                if (ExcludeContains.Any(ex => relPath.Contains(ex))) continue;

                collected.Add(relPath);
            }
        }

        // Deterministic order - same input tree always produces the same file, byte
        // for byte, which makes two dumps diff-able and avoids reordering noise
        // between runs that didn't actually change any code.
        collected.Sort(System.StringComparer.Ordinal);

        sb.AppendLine("// --- M1 VOXEL ENGINE STABLE CONTEXT ---");
        sb.AppendLine($"// Generated: {System.DateTime.Now}");
        sb.AppendLine($"// Files included: {collected.Count}");
        sb.AppendLine();

        foreach (string relPath in collected)
        {
            string absPath = Path.Combine(projectRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
            sb.AppendLine($"// ==========================================");
            sb.AppendLine($"// FILE: {relPath}");
            sb.AppendLine($"// ==========================================");
            sb.AppendLine(File.ReadAllText(absPath));
            sb.AppendLine();
        }

        File.WriteAllText(outputPath, sb.ToString());
        Debug.Log($"[Context Bundler] Engine context updated. {collected.Count} files, {new FileInfo(outputPath).Length / 1024} KB -> {outputPath}");
    }
}