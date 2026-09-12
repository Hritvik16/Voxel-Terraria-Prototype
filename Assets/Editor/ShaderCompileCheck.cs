// Assets/Editor/ShaderCompileCheck.cs
//
// Forces a compute-shader compile and reports messages, so a shader error
// cannot hide behind a green C# test run. The EditMode suite does not compile
// compute shaders, which means "PASS 218 FAIL 0" says nothing whatsoever about
// FluidCA.compute -- exactly the kind of false green this project's rules exist
// to prevent.
//
//   Unity -batchmode -quit -projectPath . -executeMethod ShaderCompileCheck.CheckAll
using UnityEditor;
using UnityEngine;

public static class ShaderCompileCheck
{
    [MenuItem("Voxel Engine/Check Compute Shaders")]
    public static void CheckAll()
    {
        int totalErrors = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ComputeShader"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (cs == null) { Debug.LogError($"[ShaderCheck] could not load {path}"); totalErrors++; continue; }

            int n = ShaderUtil.GetComputeShaderMessageCount(cs);
            var msgs = ShaderUtil.GetComputeShaderMessages(cs);
            int errs = 0;
            for (int i = 0; i < n && msgs != null && i < msgs.Length; i++)
            {
                var m = msgs[i];
                string line = $"[ShaderCheck] {path}: {m.severity} {m.message} {m.messageDetails} (line {m.line})";
                if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) { Debug.LogError(line); errs++; }
                else Debug.LogWarning(line);
            }
            totalErrors += errs;
            Debug.Log($"[ShaderCheck] {path}: {(errs == 0 ? "OK" : errs + " ERROR(S)")} ({n} message(s))");
        }
        Debug.Log($"[ShaderCheck] DONE totalErrors={totalErrors}");
        if (totalErrors > 0 && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
