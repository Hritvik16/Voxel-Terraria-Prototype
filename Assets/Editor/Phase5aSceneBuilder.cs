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

using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Phase5aSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Phase 5a Basin.unity";

    /// Writes serialized values through SerializedObject rather than through the
    /// managed fields, so it works even when the managed type is stale (see the
    /// note at the call site). Throws on an unknown name so a renamed field
    /// fails the generator loudly instead of silently leaving a zero behind.
    private static void Configure(Component target, (string name, object value)[] values)
    {
        var so = new SerializedObject(target);
        foreach (var (name, value) in values)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop == null)
                throw new InvalidOperationException(
                    $"{target.GetType().Name} has no serialized field '{name}' — " +
                    "it was renamed or removed; update Phase5aSceneBuilder.");
            switch (value)
            {
                case int i:    prop.intValue = i; break;
                case float f:  prop.floatValue = f; break;
                case bool b:   prop.boolValue = b; break;
                case string s: prop.stringValue = s; break;
                default: throw new InvalidOperationException($"unhandled type for '{name}'");
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

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
        var basin = rigGo.AddComponent<Phase5aBasin>();

        // The acceptance rig lives in the same scene but stays dormant unless
        // launched with -phase5arig (or in batchmode), so opening this scene in
        // the Editor still gives a human the plain clickable basin.
        var acceptanceGo = new GameObject("Phase5aAcceptanceRig");
        var acceptance = acceptanceGo.AddComponent<Phase5aAcceptanceRig>();

        // EVERY serialized value IS SET EXPLICITLY HERE. Do not go back to
        // relying on the C# field initializers in Phase5aBasin.
        //
        // WHY: AddComponent only picks those up when the managed assembly is
        // loaded and current, and this method runs under -executeMethod right
        // after a script edit, when it may not be. That is not hypothetical --
        // it happened: a regeneration immediately following an edit to
        // Phase5aBasin.cs wrote a scene with _bricksX/_bricksY/_bricksZ all 0,
        // and every scenario in the standalone run then died with
        //     ArgumentException: bricksX must be a positive power of two (got 0)
        // while the rig still reported "RESULT: every captured frame balanced"
        // off zero captured frames. A generator whose output depends on
        // compile timing is not a generator, so the values are written here.
        Configure(basin, new (string, object)[]
        {
            ("_bricksX", 8), ("_bricksY", 4), ("_bricksZ", 8),
            ("_ticksPerSecond", 20f), ("_paused", false), ("_sourceBudget", 250),
        });
        Configure(acceptance, new (string, object)[]
        {
            ("_quietTicksForRest", 12), ("_maxTicksPerScenario", 1500),
            ("_captureWidth", 1600), ("_captureHeight", 900),
            ("_outputRootFolderName", "Phase5aAcceptance"),
        });

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log(ok
            ? $"[Phase5aSceneBuilder] wrote {ScenePath}"
            : $"[Phase5aSceneBuilder] FAILED to write {ScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
