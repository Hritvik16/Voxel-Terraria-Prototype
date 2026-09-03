// ==========================================
// Assets/Editor/Phase5aSceneBuilder.cs
//
// Generates Assets/Scenes/Phase 5a Basin.unity -- §13 Phase 5a's scene.
//
// WHY A GENERATOR AND NOT A HAND-BUILT SCENE ASSET: the scene has exactly two
// objects in it and all of its content is produced at runtime by
// Phase5aBasin.BuildBasin(). A generated scene is diffable as code, can be
// rebuilt headlessly, and cannot drift from the component it hosts. Run it
// from the menu, or headlessly with:
//
//   Unity -batchmode -quit -projectPath . \
//         -executeMethod Phase5aSceneBuilder.Generate
//
// (-quit IS correct here: this is -executeMethod, not -runTests. See CLAUDE.md.)

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Phase5aSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Phase 5a Basin.unity";

    [MenuItem("Voxel Engine/Phase 5a/Generate Basin Scene")]
    public static void Generate()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // The overlay is pure IMGUI, so the camera only has to paint the
        // background. No URP renderer feature, no raymarch pass -- the shipped
        // renderer does not come online for fluids until 5b (§13).
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f, 1f);
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        var rigGo = new GameObject("Phase5aBasin");
        rigGo.AddComponent<Phase5aBasin>();

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log(ok
            ? $"[Phase5aSceneBuilder] wrote {ScenePath}"
            : $"[Phase5aSceneBuilder] FAILED to write {ScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
