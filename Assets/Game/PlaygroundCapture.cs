// Assets/Game/PlaygroundCapture.cs
//
// First-pass screenshot set from the Playground scene. Runs only with
// -playgroundshots, so the scene stays a plain flyable dogfood scene otherwise.
//
// This captures a LOOK, not a measurement. Nothing here is instrumented and
// nothing it produces is evidence about correctness, scale, or performance.
using System;
using System.Collections;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

public class PlaygroundCapture : MonoBehaviour
{
    [SerializeField] private string _outputRootFolderName = "PlaygroundShots";
    private readonly StringBuilder _log = new StringBuilder();
    private string _folder;
    private int _shots;

    private static bool HasFlag(string f)
    {
        foreach (string a in Environment.GetCommandLineArgs())
            if (string.Equals(a, f, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    IEnumerator Start()
    {
        if (!HasFlag("-playgroundshots")) yield break;
        Screen.SetResolution(1920, 1080, false);

        var pg = FindAnyObjectByType<Playground>();
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 240; i++) yield return null;   // let streaming settle

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, _outputRootFolderName, ts);
        Directory.CreateDirectory(_folder);
        L($"Playground first-pass look — {ts}");
        L($"device {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
        L("Dogfood scene. Not a diagnostic scene, not a scale test, not a perf measurement.");
        L("");

        var cam = Camera.main;

        // ---- Terrain-only, several angles ----
        // Poses are around the island centre (~1280 m for sizeClass 1), not the
        // origin -- see the camera note in Phase5aSceneBuilder.GeneratePlayground.
        yield return Pose(cam, new Vector3(1280f, 14f, 1250f), new Vector3(10f, 0f, 0f), "01_terrain_wide");
        yield return Pose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(2f, 35f, 0f), "02_terrain_ground");
        yield return Pose(cam, new Vector3(1300f, 24f, 1240f), new Vector3(26f, -18f, 0f), "03_terrain_high");
        yield return Pose(cam, new Vector3(1280f, 5.5f, 1310f), new Vector3(-1f, 180f, 0f), "04_terrain_coast");

        // ---- Terrain + the fluid arena ----
        if (pg != null)
        {
            pg.SendMessage("TeleportToArena", SendMessageOptions.DontRequireReceiver);
            yield return null;
            yield return Shot("05_arena_before", "the natural basin the arena sits in, before fluid");

            foreach (KeyCode k in new[] { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 })
                pg.SendMessage("DebugRunKey", k, SendMessageOptions.DontRequireReceiver);

            for (int i = 0; i < 120; i++) yield return null;
            yield return Shot("06_arena_pouring", "water/sand/lava falling into generated terrain");
            for (int i = 0; i < 400; i++) yield return null;
            yield return Shot("07_arena_settling", "fluid finding the shape of the natural ground");
            for (int i = 0; i < 700; i++) yield return null;
            yield return Shot("08_arena_settled", "at rest on natural (non-flat) geometry");

            var c = Camera.main;
            c.transform.position += c.transform.forward * 2.2f + Vector3.up * 0.6f;
            yield return Shot("09_arena_close", "close on the fluid/terrain boundary");
        }

        L($"screenshots: {_shots}");
        File.WriteAllText(Path.Combine(_folder, "look_report.txt"), _log.ToString());
        Debug.Log("[PlaygroundCapture]\n" + _log);
        yield return null;
        Application.Quit(_shots >= 9 ? 0 : 1);
    }

    private IEnumerator Pose(Camera cam, Vector3 pos, Vector3 euler, string name)
    {
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Euler(euler);
        for (int i = 0; i < 90; i++) yield return null;   // let streaming catch up
        yield return Shot(name, $"pos {pos} euler {euler}");
    }

    private IEnumerator Shot(string name, string caption)
    {
        for (int i = 0; i < 6; i++) yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_folder, name + ".png"), t.EncodeToPNG()); }
        finally { Destroy(t); }
        _shots++;
        L($"{name}.png — {caption}");
    }

    private void L(string s) => _log.AppendLine(s);
}
