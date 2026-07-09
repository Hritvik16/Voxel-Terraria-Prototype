using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

[InitializeOnLoad]
public static class ContextBundler
{
    // The name of the file to generate in the root of your Unity project
    private const string OutputFileName = "Codebase_Context.txt";

    private static readonly string[] TargetDirectories = new string[]
    {
        "Assets/CoreEngine" 
    };

    static ContextBundler()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
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
        sb.AppendLine("// --- M1 VOXEL ENGINE STABLE CONTEXT ---");
        sb.AppendLine($"// Generated: {System.DateTime.Now}");
        sb.AppendLine();

        foreach (string relativeDir in TargetDirectories)
        {
            string absoluteDir = Path.Combine(projectRoot, relativeDir);
            if (!Directory.Exists(absoluteDir)) continue;

            string[] files = Directory.GetFiles(absoluteDir, "*.*", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                if (file.EndsWith(".cs") || file.EndsWith(".compute") || file.EndsWith(".hlsl"))
                {
                    sb.AppendLine($"// ==========================================");
                    sb.AppendLine($"// FILE: {file.Replace(projectRoot + Path.DirectorySeparatorChar, "")}");
                    sb.AppendLine($"// ==========================================");
                    sb.AppendLine(File.ReadAllText(file));
                    sb.AppendLine();
                }
            }
        }

        File.WriteAllText(outputPath, sb.ToString());
        Debug.Log($"[Context Bundler] Engine context updated.");
    }
}