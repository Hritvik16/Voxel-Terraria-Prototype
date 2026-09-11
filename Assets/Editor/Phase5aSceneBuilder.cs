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





    public const string PlaygroundScenePath = "Assets/Scenes/Playground.unity";

    /// The dogfood scene: REAL Phase 3 generation + Phase 4 streaming via
    /// Phase4Bootstrapper, plus a flycam and one fixed fluid arena.
    [MenuItem("Voxel Engine/Playground/Generate Scene")]
    public static void GeneratePlayground()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);   // matches RaymarchFeature.SkyColor
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        // ON THE ISLAND, not at the origin. Phase4Bootstrapper's own spawn is
        // 140.8 m, which was correct for sizeClass 0; sizeClass 1 (Content.cs,
        // §D.2) moved the island centre to ~1280 m and left that spawn in deep
        // ocean. A dogfood scene that opens on featureless sea is useless.
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);
        camGo.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
        // PlaygroundFlyCamera, not SimpleFlyCamera: mouse-capture look, which
        // the rig scenes deliberately do not have (see that file's header).
        camGo.AddComponent<PlaygroundFlyCamera>();

        // The real world. Every serialized value set explicitly -- AddComponent
        // does not reliably pick up C# field initializers under -executeMethod.
        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);   // a feel scene starts clean
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false); // this scene frames its own camera
        bo.ApplyModifiedPropertiesWithoutUndo();

        var pgGo = new GameObject("Playground");
        var pg = pgGo.AddComponent<Playground>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var po = new SerializedObject(pg);
        po.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        po.FindProperty("_arenaEdge").intValue = 64;
        po.FindProperty("_slotCapacity").intValue = 8192;
        po.FindProperty("_maxOpsPerFrame").intValue = 8192;
        // Tiny on purpose -- see Playground's header note 2. Not a scale test.
        po.FindProperty("_waterBudget").intValue = 160;
        po.FindProperty("_sandBudget").intValue = 90;
        po.FindProperty("_lavaBudget").intValue = 60;
        po.FindProperty("_outputRootFolderName").stringValue = "PlaygroundShots";
        po.ApplyModifiedPropertiesWithoutUndo();

        // THE PLAYER (Phase 6). Playground drives it via DebugStep and owns its
        // input, so mouse capture and look stay in ONE place (PlaygroundFlyCamera)
        // whichever movement mode is active -- two components both locking the
        // cursor and both writing the camera transform fight each other.
        var playerGo = new GameObject("Player");
        playerGo.transform.position = camGo.transform.position;
        var pc = playerGo.AddComponent<PlayerController>();
        var pco = new SerializedObject(pc);
        var pcCam = pco.FindProperty("_camera");
        if (pcCam != null) pcCam.objectReferenceValue = cam;
        var pcSpawn = pco.FindProperty("_spawnM");
        if (pcSpawn != null) pcSpawn.vector3Value = camGo.transform.position;
        SetBoolIfPresent(pco, "_resolveSpawnUpward", true);
        // Playground owns capture; PlayerController must not also grab the cursor.
        SetBoolIfPresent(pco, "_captureMouse", false);
        SetBoolIfPresent(pco, "_hotReload", true);   // §8.1's live tuning loop
        pco.ApplyModifiedPropertiesWithoutUndo();

        po.Update();
        var pgPlayer = po.FindProperty("_player");
        if (pgPlayer != null) pgPlayer.objectReferenceValue = pc;
        po.ApplyModifiedPropertiesWithoutUndo();

        // Live debug readout. Its own object so the dogfood scene keeps the
        // perf overlay separable from the gameplay toys.
        var hudGo = new GameObject("PlaygroundHud");
        var hud = hudGo.AddComponent<PlaygroundHud>();
        var ho = new SerializedObject(hud);
        var visProp = ho.FindProperty("_visible");
        if (visProp != null) visProp.boolValue = true;
        ho.ApplyModifiedPropertiesWithoutUndo();

        var capGo = new GameObject("PlaygroundCapture");
        var cap = capGo.AddComponent<PlaygroundCapture>();
        var co = new SerializedObject(cap);
        co.FindProperty("_outputRootFolderName").stringValue = "PlaygroundShots";
        co.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(PlaygroundScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, PlaygroundScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {PlaygroundScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {PlaygroundScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    private static void SetIfPresent(SerializedObject so, string name, int v)
    { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
    private static void SetBoolIfPresent(SerializedObject so, string name, bool v)
    { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }

    public const string FluidActivityScenePath = "Assets/Scenes/Fluid Activity.unity";

    /// STEP 0 diagnostic scene for "fluid stops simulating". Real world, a
    /// Playground-shaped fluid arena (including its 8192 slot capacity), and a
    /// rig that pours into it while logging slot-allocation counters.
    [MenuItem("Voxel Engine/Diagnostics/Generate Fluid Activity Scene")]
    public static void GenerateFluidActivity()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("FluidActivityRig");
        var rig = rigGo.AddComponent<FluidActivityRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        // PLAYGROUND'S NUMBER, deliberately: 8192 is what the reported symptom
        // was produced against, and the diagnosis is about that value.
        ro.FindProperty("_slotCapacity").intValue = 8192;
        ro.FindProperty("_maxOpsPerFrame").intValue = 8192;
        ro.FindProperty("_outputRootFolderName").stringValue = "FluidActivity";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(FluidActivityScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, FluidActivityScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {FluidActivityScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {FluidActivityScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase6SandboxScenePath = "Assets/Scenes/Phase 6 Sandbox.unity";

    /// §13 Phase 6's INTEGRATED acceptance scene. Everything live at once: the
    /// real world, a PlayerController, a fluid arena, and the rig that drives
    /// the whole §13 acceptance list in one run.
    [MenuItem("Voxel Engine/Phase 6/Generate Sandbox Scene")]
    public static void GeneratePhase6Sandbox()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 68f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(1280f, 14f, 1268f);
        var pc = playerGo.AddComponent<PlayerController>();
        var pco = new SerializedObject(pc);
        var camProp = pco.FindProperty("_camera");
        if (camProp != null) camProp.objectReferenceValue = cam;
        var spawnProp = pco.FindProperty("_spawnM");
        if (spawnProp != null) spawnProp.vector3Value = new Vector3(1280f, 40f, 1268f);
        SetBoolIfPresent(pco, "_resolveSpawnUpward", true);
        SetBoolIfPresent(pco, "_captureMouse", false);
        SetBoolIfPresent(pco, "_hotReload", true);
        pco.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("Phase6SandboxRig");
        var rig = rigGo.AddComponent<Phase6SandboxRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_player").objectReferenceValue = pc;
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_maxOpsPerFrame").intValue = 65536;
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase6Sandbox";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase6SandboxScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase6SandboxScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase6SandboxScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase6SandboxScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase6EditScenePath = "Assets/Scenes/Phase 6 Edit.unity";

    /// §13 Phase 6 file 3's acceptance scene. Real world, a fluid arena for the
    /// wake-scan step, and a camera the rig aims itself.
    [MenuItem("Voxel Engine/Phase 6/Generate Edit Scene")]
    public static void GeneratePhase6Edit()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("Phase6EditRig");
        var rig = rigGo.AddComponent<Phase6EditRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_maxOpsPerFrame").intValue = 65536;
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase6Edit";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase6EditScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase6EditScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase6EditScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase6EditScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase6CcdScenePath = "Assets/Scenes/Phase 6 CCD.unity";

    /// §13 Phase 6 file 2's acceptance scene. Real world, a free camera the rig
    /// aims itself, and no player: SweptCCD is exercised directly, so nothing
    /// else may be moving bodies around while it is measured.
    [MenuItem("Voxel Engine/Phase 6/Generate CCD Scene")]
    public static void GeneratePhase6Ccd()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1270f, 12f, 1272f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("Phase6CcdRig");
        var rig = rigGo.AddComponent<Phase6CcdRig>();
        var ro = new SerializedObject(rig);
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase6Ccd";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase6CcdScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase6CcdScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase6CcdScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase6CcdScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase6PlayerScenePath = "Assets/Scenes/Phase 6 Player.unity";

    /// §13 Phase 6 file 1's acceptance scene. Real Phase 3 generation and Phase
    /// 4 streaming (Phase4Bootstrapper), a PlayerController, and the rig that
    /// drives it. No flycam and no HUD: the rig owns the camera through the
    /// player, and nothing else may move it.
    [MenuItem("Voxel Engine/Phase 6/Generate Player Scene")]
    public static void GeneratePhase6Player()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 14f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(1280f, 14f, 1268f);
        var pc = playerGo.AddComponent<PlayerController>();
        var pco = new SerializedObject(pc);
        var camProp = pco.FindProperty("_camera");
        if (camProp != null) camProp.objectReferenceValue = cam;
        var spawnProp = pco.FindProperty("_spawnM");
        if (spawnProp != null) spawnProp.vector3Value = new Vector3(1280f, 40f, 1268f);
        SetBoolIfPresent(pco, "_resolveSpawnUpward", true);
        // No mouse capture: this is a scripted run with no human at the keyboard,
        // and a locked cursor in a batch-launched player is just a nuisance.
        SetBoolIfPresent(pco, "_captureMouse", false);
        SetBoolIfPresent(pco, "_hotReload", true);   // step 5 depends on it
        pco.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("Phase6PlayerRig");
        var rig = rigGo.AddComponent<Phase6PlayerRig>();
        var ro = new SerializedObject(rig);
        ro.FindProperty("_player").objectReferenceValue = pc;
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase6Player";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase6PlayerScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase6PlayerScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase6PlayerScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase6PlayerScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase6BrushGuardScenePath = "Assets/Scenes/Phase 6 Brush Guard.unity";

    /// The brush-guard end-to-end rig scene. Deliberately the PLAYGROUND setup
    /// -- the real Phase4Bootstrapper world and the real Playground component --
    /// with the rig driving it instead of a human. Two things the dogfood scene
    /// has are omitted on purpose:
    ///   PlaygroundFlyCamera  the rig owns the camera; a flycam would fight it
    ///   PlaygroundCapture    its screenshot pass would race the rig's own
    /// Playground.HandleMouse is additionally inert here regardless, because it
    /// is gated on the flycam having captured the mouse.
    [MenuItem("Voxel Engine/Phase 6/Generate Brush Guard Scene")]
    public static void GeneratePhase6BrushGuard()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        // Same island spawn as the Playground scene, so the basin the arena
        // lands in is the same one a human would see. The rig repositions from
        // here; it never teleports far enough to matter (the arena is 6.4 m
        // across, so "outside" is metres away, not chunks).
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);
        camGo.transform.rotation = Quaternion.Euler(14f, 0f, 0f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var pgGo = new GameObject("Playground");
        var pg = pgGo.AddComponent<Playground>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var po = new SerializedObject(pg);
        po.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        po.FindProperty("_arenaEdge").intValue = 64;
        po.FindProperty("_slotCapacity").intValue = 8192;
        po.FindProperty("_maxOpsPerFrame").intValue = 8192;
        // Vents off: the rig places every voxel it reasons about itself, so a
        // background vent dribbling water into the arena would contaminate the
        // conservation check in step 4.
        po.FindProperty("_waterBudget").intValue = 0;
        po.FindProperty("_sandBudget").intValue = 0;
        po.FindProperty("_lavaBudget").intValue = 0;
        po.FindProperty("_outputRootFolderName").stringValue = "Phase6BrushGuard";
        po.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("Phase6BrushGuard");
        var rig = rigGo.AddComponent<Phase6BrushGuard>();
        var ro = new SerializedObject(rig);
        ro.FindProperty("_playground").objectReferenceValue = pg;
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase6BrushGuard";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase6BrushGuardScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase6BrushGuardScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase6BrushGuardScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase6BrushGuardScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase5bDemoScenePath = "Assets/Scenes/Phase 5b Demo.unity";

    /// The demo / playable scene. One scene, two modes -- see Phase5bDemo.
    [MenuItem("Voxel Engine/Phase 5b/Generate Demo Scene")]
    public static void GeneratePhase5bDemo()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Framed deliberately: high and back on the -Z/-X corner, looking down
        // the diagonal so the shelf, the spillway and the catch basin are all in
        // frame at once. The world spans 0..12.8 m; voxels are 0.1 m.
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.13f, 0.19f, 1f);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        // INSIDE the world, not outside it. The first framing sat at negative
        // X/Z, outside the clipmap window, where every ray starts out of bounds
        // and is killed -- which renders as a flat fill and looks like the fluid
        // is missing when nothing is wrong with the fluid at all.
        // The arena spans voxels 2..125 => 0.2..12.5 m; this stands in the
        // near corner at head height looking down the diagonal.
        camGo.transform.position = new Vector3(1.8f, 3.6f, 1.8f);
        camGo.transform.rotation = Quaternion.Euler(20f, 45f, 0f);
        camGo.AddComponent<SimpleFlyCamera>();

        var go = new GameObject("Phase5bDemo");
        var demo = go.AddComponent<Phase5bDemo>();

        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");

        var so = new SerializedObject(demo);
        so.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        so.FindProperty("_slotCapacity").intValue = 131072;
        so.FindProperty("_maxOpsPerFrame").intValue = 32768;
        so.FindProperty("_outputRootFolderName").stringValue = "Phase5bDemo";
        so.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase5bDemoScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase5bDemoScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase5bDemoScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase5bDemoScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase5dScenePath = "Assets/Scenes/Phase 5d Stream Fluid.unity";

    /// Streaming x fluid interaction rig (Phase 5d). DIAGNOSTIC scene: the real
    /// Phase 4 streaming stack plus a live fluid CA, scripted camera, minimal
    /// rendering. Unlike every other fluid scene this one has a REAL
    /// StreamManager with a non-zero load radius, because chunks actually
    /// streaming in and out is the whole point.
    [MenuItem("Voxel/Generate Phase 5d Stream+Fluid Scene")]
    public static void GeneratePhase5d()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 1f);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        // The rig drives this camera from a script; the starting pose is
        // overwritten in Start(). No flycam component: no human input.
        camGo.transform.position = new Vector3(128f, 14f, 128f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        // 0 = use the engine default radius. The rig reads the resulting
        // LoadRadiusChunks back and derives its camera distances from it, so
        // this scene does not hardcode how far "far" is.
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("Phase5dStreamFluid");
        var rig = rigGo.AddComponent<Phase5dStreamFluid>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_maxOpsPerFrame").intValue = 65536;
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase5dStreamFluid";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase5dScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase5dScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase5dScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase5dScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase5cScenePath = "Assets/Scenes/Phase 5c Edit Stress.unity";

    /// Edit-path stress rig (Phase 5c). Same basin shape as 5b so a human
    /// comparing screenshots is looking at the same world; the difference is
    /// that this one drives PLAYER-shaped edits and runs every case in both
    /// frame orderings. See Phase5cEditStress.
    [MenuItem("Voxel/Generate Phase 5c Edit Stress Scene")]
    public static void GeneratePhase5c()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f, 1f);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        // INSIDE the clipmap window. The first version of this scene copied 5b's
        // camera, which stands at NEGATIVE Z -- outside the window, where every
        // ray starts out of bounds and is killed, so the capture came out as a
        // flat pale fill that looks exactly like "the fluid is missing" when
        // nothing is wrong with the fluid at all (§6.2 lesson, and it caught me
        // once here despite being written down two lines above).
        // The basin spans voxels 0..63 => 0..6.4 m at 0.1 m/voxel. This stands
        // inside the near corner at head height, looking down the diagonal
        // toward the pour column at voxel (26,*,32).
        camGo.transform.position = new Vector3(0.9f, 1.9f, 0.9f);
        camGo.transform.rotation = Quaternion.Euler(22f, 45f, 0f);

        var go = new GameObject("Phase5cEditStress");
        var rig = go.AddComponent<Phase5cEditStress>();

        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");

        var so = new SerializedObject(rig);
        so.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        so.FindProperty("_slotCapacity").intValue = 65536;
        so.FindProperty("_maxOpsPerFrame").intValue = 65536;
        so.FindProperty("_outputRootFolderName").stringValue = "Phase5cEditStress";
        so.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase5cScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase5cScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase5cScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase5cScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

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
        ro.FindProperty("_maxTicksPerScenario").intValue = 4000;
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

    public const string PlaytestBugsScenePath = "Assets/Scenes/Playtest Bugs.unity";

    /// STEP 0 diagnostic scene for the two bugs found by playtesting: a
    /// floating cluster near the fluid arena, and no-fall-on-Tab at height.
    /// Deliberately the SAME shape as the Playground (64^3 arena, 8192 slots,
    /// a 128-voxel demo activity radius), because both bugs were seen there and
    /// a diagnosis against different numbers would be diagnosing a different
    /// scene.
    [MenuItem("Voxel Engine/Diagnostics/Generate Playtest Bugs Scene")]
    public static void GeneratePlaytestBugs()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 4000f;      // B1 flies to 2000 m; a 600 m far plane
                                       // would clip the sky and confuse the shot
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(1280f, 14f, 1268f);
        var pc = playerGo.AddComponent<PlayerController>();
        var pco = new SerializedObject(pc);
        var camProp = pco.FindProperty("_camera");
        if (camProp != null) camProp.objectReferenceValue = cam;
        var spawnProp = pco.FindProperty("_spawnM");
        if (spawnProp != null) spawnProp.vector3Value = new Vector3(1280f, 40f, 1268f);
        SetBoolIfPresent(pco, "_resolveSpawnUpward", true);
        SetBoolIfPresent(pco, "_captureMouse", false);
        SetBoolIfPresent(pco, "_hotReload", false);
        pco.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("PlaytestBugsRig");
        var rig = rigGo.AddComponent<PlaytestBugsRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_player").objectReferenceValue = pc;
        // PLAYGROUND'S DEMO VALUE, on purpose -- see the rig header.
        ro.FindProperty("_activeRadiusVoxels").intValue = 128;
        ro.FindProperty("_outputRootFolderName").stringValue = "PlaytestBugs";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(PlaytestBugsScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, PlaytestBugsScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {PlaytestBugsScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {PlaytestBugsScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string FluidScaleScenePath = "Assets/Scenes/Fluid Scale.unity";

    /// §7 slot/memory scale data. Counter-only: no timings are taken, so the
    /// scene needs no camera framing beyond a valid Main Camera.
    [MenuItem("Voxel Engine/Diagnostics/Generate Fluid Scale Scene")]
    public static void GenerateFluidScale()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("FluidScaleRig");
        var rig = rigGo.AddComponent<FluidScaleRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        // Big enough that 32,000 live voxels is a test of the ALLOCATOR rather
        // than a test of an artificially small capacity.
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_outputRootFolderName").stringValue = "FluidScale";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(FluidScaleScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, FluidScaleScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {FluidScaleScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {FluidScaleScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string FluidTiledScenePath = "Assets/Scenes/Fluid Tiled.unity";

    /// §7.2's sparse tiled active set -- acceptance. Real world, a tiled CA at
    /// the SHIPPED radius, and an explosion-scatter stress.
    [MenuItem("Voxel Engine/Diagnostics/Generate Fluid Tiled Scene")]
    public static void GenerateFluidTiled()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("FluidTiledRig");
        var rig = rigGo.AddComponent<FluidTiledRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_tilePoolCap").intValue = 512;
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_outputRootFolderName").stringValue = "FluidTiled";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(FluidTiledScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, FluidTiledScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {FluidTiledScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {FluidTiledScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string FluidABScenePath = "Assets/Scenes/Fluid AB.unity";

    /// The dense-vs-tiled wall-clock A/B (Assets/Game/FluidABBenchmark.cs).
    ///
    /// Deliberately the SAME scene shape as GenerateFluidTiled -- same camera
    /// pose, same bootstrapper settings, same world seed. The A/B's whole
    /// claim is that only the addressing path differs between two runs, and
    /// that claim starts with the scene.
    public static void GenerateFluidAB()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 65f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("FluidABBenchmark");
        var rig = rigGo.AddComponent<FluidABBenchmark>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_tilePoolCap").intValue = 512;
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_maxOpsPerFrame").intValue = 65536;
        ro.FindProperty("_outputRootFolderName").stringValue = "FluidAB";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(FluidABScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, FluidABScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {FluidABScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {FluidABScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase4FluidScenePath = "Assets/Scenes/Phase 4 Streaming Fluid.unity";

    /// STEP 3's combined-load scene: the Phase 4 acceptance scene, byte-for-byte,
    /// PLUS the one thing it lacks -- a reference to FluidCA.compute.
    ///
    /// IT IS CLONED, NOT REBUILT, and that is the whole point. Reconstructing a
    /// Phase 4 scene by hand would risk a different camera pose, a different
    /// bootstrapper setting, a different load radius -- and then the combined-load
    /// numbers could not be compared against the terrain-only ones at all, which
    /// is the only reason to run them. Opening the real scene and saving it under
    /// a new name guarantees everything else is identical.
    ///
    /// The ORIGINAL scene is never modified. The terrain-only build does not even
    /// contain FluidCA.compute, so every Phase 4 figure on record stays comparable.
    public static void GeneratePhase4FluidScene()
    {
        const string src = "Assets/Scenes/Phase 4 Streaming.unity";
        var scene = EditorSceneManager.OpenScene(src, OpenSceneMode.Single);

        var rig = UnityEngine.Object.FindAnyObjectByType<Phase4AcceptanceRig>();
        if (rig == null) throw new InvalidOperationException($"no Phase4AcceptanceRig in {src}");

        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");

        var ro = new SerializedObject(rig);
        var prop = ro.FindProperty("_fluidCA");
        if (prop == null) throw new InvalidOperationException("Phase4AcceptanceRig has no _fluidCA field");
        prop.objectReferenceValue = fluidCA;
        ro.ApplyModifiedPropertiesWithoutUndo();

        bool ok = EditorSceneManager.SaveScene(scene, Phase4FluidScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase4FluidScenePath} (clone of {src} + FluidCA)"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase4FluidScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string Phase6CombinedScenePath = "Assets/Scenes/Phase 6 Combined.unity";

    /// Every Phase 6 system at once, on the TILED fluid substrate.
    ///
    /// Cloned in shape from GeneratePhase6Sandbox deliberately: same camera,
    /// same bootstrapper settings, same player spawn. The combined rig's
    /// numbers are only comparable to the sandbox's if the scene is.
    public static void GeneratePhase6Combined()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 68f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(1280f, 14f, 1268f);
        var pc = playerGo.AddComponent<PlayerController>();
        var pco = new SerializedObject(pc);
        var camProp = pco.FindProperty("_camera");
        if (camProp != null) camProp.objectReferenceValue = cam;
        var spawnProp = pco.FindProperty("_spawnM");
        if (spawnProp != null) spawnProp.vector3Value = new Vector3(1280f, 40f, 1268f);
        SetBoolIfPresent(pco, "_resolveSpawnUpward", true);
        SetBoolIfPresent(pco, "_captureMouse", false);
        SetBoolIfPresent(pco, "_hotReload", true);
        pco.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("Phase6CombinedRig");
        var rig = rigGo.AddComponent<Phase6CombinedRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_player").objectReferenceValue = pc;
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_maxOpsPerFrame").intValue = 65536;
        ro.FindProperty("_tilePoolCap").intValue = 512;
        ro.FindProperty("_secondsOfActivity").floatValue = 75f;
        ro.FindProperty("_outputRootFolderName").stringValue = "Phase6Combined";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(Phase6CombinedScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, Phase6CombinedScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {Phase6CombinedScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {Phase6CombinedScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string FluidChaosScenePath = "Assets/Scenes/Fluid Chaos.unity";

    /// The large-scale chaos ladder. Same scene shape as the combined rig so
    /// the two are comparable.
    public static void GenerateFluidChaos()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 68f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(1280f, 14f, 1268f);
        var pc = playerGo.AddComponent<PlayerController>();
        var pco = new SerializedObject(pc);
        var camProp = pco.FindProperty("_camera");
        if (camProp != null) camProp.objectReferenceValue = cam;
        var spawnProp = pco.FindProperty("_spawnM");
        if (spawnProp != null) spawnProp.vector3Value = new Vector3(1280f, 40f, 1268f);
        SetBoolIfPresent(pco, "_resolveSpawnUpward", true);
        SetBoolIfPresent(pco, "_captureMouse", false);
        SetBoolIfPresent(pco, "_hotReload", true);
        pco.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("FluidChaosRig");
        var rig = rigGo.AddComponent<FluidChaosRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        ro.FindProperty("_player").objectReferenceValue = pc;
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_slotCapacity").intValue = 500000;
        ro.FindProperty("_maxOpsPerFrame").intValue = 65536;
        ro.FindProperty("_tilePoolCap").intValue = 512;
        
        ro.FindProperty("_outputRootFolderName").stringValue = "FluidChaos";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(FluidChaosScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, FluidChaosScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {FluidChaosScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {FluidChaosScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }

    public const string FluidStaggerScenePath = "Assets/Scenes/Fluid Stagger.unity";

    /// Diagnosis-only rig for the staggered-drop effect.
    public static void GenerateFluidStagger()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        cam.fieldOfView = 68f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 600f;
        camGo.transform.position = new Vector3(1280f, 9.5f, 1268f);

        var bootGo = new GameObject("Phase4Bootstrapper");
        var boot = bootGo.AddComponent<Phase4Bootstrapper>();
        var bo = new SerializedObject(boot);
        SetIfPresent(bo, "_loadRadiusChunks", 0);
        SetBoolIfPresent(bo, "_fillWindowOnStart", true);
        SetBoolIfPresent(bo, "_clearDeltasOnStart", true);
        SetBoolIfPresent(bo, "_overrideCameraOnStart", false);
        bo.ApplyModifiedPropertiesWithoutUndo();

        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(1280f, 14f, 1268f);
        var pc = playerGo.AddComponent<PlayerController>();
        var pco = new SerializedObject(pc);
        var camProp = pco.FindProperty("_camera");
        if (camProp != null) camProp.objectReferenceValue = cam;
        var spawnProp = pco.FindProperty("_spawnM");
        if (spawnProp != null) spawnProp.vector3Value = new Vector3(1280f, 40f, 1268f);
        SetBoolIfPresent(pco, "_resolveSpawnUpward", true);
        SetBoolIfPresent(pco, "_captureMouse", false);
        SetBoolIfPresent(pco, "_hotReload", true);
        pco.ApplyModifiedPropertiesWithoutUndo();

        var rigGo = new GameObject("FluidStaggerRig");
        var rig = rigGo.AddComponent<FluidStaggerRig>();
        var fluidCA = AssetDatabase.LoadAssetAtPath<ComputeShader>(
            "Assets/CoreEngine/Simulation/FluidCA.compute");
        if (fluidCA == null) throw new InvalidOperationException("FluidCA.compute not found");
        var ro = new SerializedObject(rig);
        // FluidStaggerRig drives no player -- it pours once and observes.
        ro.FindProperty("_fluidCA").objectReferenceValue = fluidCA;
        ro.FindProperty("_slotCapacity").intValue = 65536;
        ro.FindProperty("_maxOpsPerFrame").intValue = 65536;
        ro.FindProperty("_tilePoolCap").intValue = 512;
        
        ro.FindProperty("_outputRootFolderName").stringValue = "FluidStagger";
        ro.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(Path.GetDirectoryName(FluidStaggerScenePath));
        bool ok = EditorSceneManager.SaveScene(scene, FluidStaggerScenePath);
        Debug.Log(ok ? $"[Phase5aSceneBuilder] wrote {FluidStaggerScenePath}"
                     : $"[Phase5aSceneBuilder] FAILED to write {FluidStaggerScenePath}");
        if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
