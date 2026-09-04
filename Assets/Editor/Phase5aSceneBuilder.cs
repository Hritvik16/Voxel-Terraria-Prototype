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



    public const string Phase5bScenePath = "Assets/Scenes/Phase 5b Basin.unity";

    [MenuItem("Voxel Engine/Phase 5b/Generate Basin Scene")]
    public static void GeneratePhase5b()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // The basin spans world voxels 0..63 => 0..6.4 m (0.1 m voxels). Sit the
        // camera back and above it so the raymarcher has real work: §13's gate
        // config is fluid CA + PRIMARY RAYMARCH together, and a camera staring at
        // nothing would measure the wrong thing.
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 1f);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        camGo.transform.position = new Vector3(3.2f, 2.6f, -3.5f);
        camGo.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

        var basinGo = new GameObject("Phase5bBasin");
        var basin = basinGo.AddComponent<Phase5bBasin>();
        var rigGo = new GameObject("Phase5bValidationRig");
        var rig = rigGo.AddComponent<Phase5bValidationRig>();

        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");

        var bo = new SerializedObject(basin);
        bo.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        bo.FindProperty("_slotCapacity").intValue = 65536;
        bo.FindProperty("_maxOpsPerFrame").intValue = 65536;
        bo.ApplyModifiedPropertiesWithoutUndo();

        var ro = new SerializedObject(rig);
        ro.FindProperty("_maxTicksPerScenario").intValue = 1200;
        ro.FindProperty("_quietTicksForRest").intValue = 12;
        ro.FindProperty("_perfWarmupTicks").intValue = 60;
        ro.FindProperty("_perfSampleTicks").intValue = 300;
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase5bValidation";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase5bScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase5bScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase5bScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase5bScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string ClaimStressScenePath = "Assets/Scenes/Phase 5b ClaimStress.unity";

    /// §7.3's Metal claim verification scene. Deliberately trivial: a camera and
    /// one component. It touches no terrain, so a failure in it can only be the
    /// contended plain store itself.
    [MenuItem("Voxel Engine/Phase 5b/Generate Claim Stress Scene")]
    public static void GenerateClaimStress()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f, 1f);
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        var go = new GameObject("Phase5bClaimStress");
        var comp = go.AddComponent<Phase5bClaimStress>();

        // Same lesson as the basin scene: never rely on AddComponent picking up
        // C# field initializers under -executeMethod. Set everything explicitly.
        var so = new SerializedObject(comp);
        so.FindProperty("_destCount").intValue = 4096;
        so.FindProperty("_sourcesPerDest").intValue = 512;
        so.FindProperty("_passes").intValue = 200;
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidClaimStress.compute");
        if (shader == null) throw new InvalidOperationException("FluidClaimStress.compute not found");
        so.FindProperty("_stress").objectReferenceValue = shader;
        so.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(ClaimStressScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, ClaimStressScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {ClaimStressScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {ClaimStressScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

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
