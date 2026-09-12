// ==========================================
// Assets/Game/PlaygroundHud.cs
//
// Live debug readout for the Playground dogfood scene.
//
// =========================================================================
// NONE OF THESE NUMBERS ARE EVIDENCE. READ THIS BEFORE QUOTING ANY OF THEM.
// =========================================================================
// This is a feel HUD. It exists so a human flying around can notice "that got
// choppy when I did X", not so anyone can cite a millisecond figure.
//
//   - WALL is Time.unscaledDeltaTime. It is the only figure here with a
//     defensible relationship to reality, and even it is not a benchmark:
//     CLAUDE.md's measurement discipline says the only trusted frame-time
//     source is ./run-acceptance-rig.sh's own printed report, from a release
//     standalone launched outside the Editor. This is a live overlay in a
//     scene with a GUI on top of it.
//
//   - GPU is FrameTimingManager.gpuFrameTime, and AMENDMENT 8.10 MEASURED IT
//     INFLATED BY A NEAR-CONSTANT ~2.6-2.7x ON THIS HARDWARE ("a frame's GPU
//     time cannot exceed that frame's wall clock"). An earlier session used it
//     to produce a result that had to be retracted. It is shown because it is
//     still useful as a RELATIVE signal -- watch it move, do not read its
//     value -- and it is labelled inline so it cannot be quoted innocently.
//
//   - CPU MAIN is cpuMainThreadFrameTime: main-thread WORK, not wall time. On
//     this machine those diverge badly under worker oversubscription --
//     EngineConfig's CHUNK_GEN_WORKER_THREADS note records Unity reporting a
//     healthy ~21 ms while unscaledDeltaTime recorded 1000 ms+, because the
//     main thread was descheduled rather than busy. If WALL and CPU MAIN
//     disagree wildly, that gap IS the finding.
//
// If a number here looks alarming, go reproduce it in the phase rig that owns
// it before treating it as real.

using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Memory;

public class PlaygroundHud : MonoBehaviour
{
    [SerializeField] private bool _visible = true;
    [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

    private FrameTiming[] _timings = new FrameTiming[1];

    // Smoothed for readability, plus a rolling worst so a single hitch does not
    // vanish before you can read it.
    private double _wallSmoothed, _gpuSmoothed, _cpuMainSmoothed;
    private double _wallWorst;
    private float _worstResetTimer;
    private int _frames;

    /// Reads the commit + build time baked in at build time. Cached: this is
    /// drawn every frame and Resources.Load is not free.
    private static string _buildStamp;
    private static string BuildStampLine()
    {
        if (_buildStamp != null) return _buildStamp;
        var ta = Resources.Load<TextAsset>("BuildStamp");
        if (ta == null) { _buildStamp = "(no stamp -- Editor play, or built before BuildStamp existed)"; return _buildStamp; }
        string commit = "?", built = "?";
        foreach (string line in ta.text.Split('\n'))
        {
            string t = line.Trim();
            if (t.StartsWith("commit")) commit = t.Substring(6).Trim();
            else if (t.StartsWith("built")) built = t.Substring(5).Trim();
        }
        _buildStamp = $"{commit}   built {built}";
        return _buildStamp;
    }

    void Update()
    {
        if (Input.GetKeyDown(_toggleKey)) _visible = !_visible;

        double wall = Time.unscaledDeltaTime * 1000.0;
        _wallSmoothed = _frames == 0 ? wall : _wallSmoothed + (wall - _wallSmoothed) * 0.06;
        if (wall > _wallWorst) _wallWorst = wall;
        _frames++;

        // Rolling 3-second worst, so the number reflects "recently" rather than
        // "at any point since launch".
        _worstResetTimer += Time.unscaledDeltaTime;
        if (_worstResetTimer > 3f) { _worstResetTimer = 0f; _wallWorst = wall; }

        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
        {
            if (_timings[0].gpuFrameTime > 0)
                _gpuSmoothed += (_timings[0].gpuFrameTime - _gpuSmoothed) * 0.06;
            if (_timings[0].cpuMainThreadFrameTime > 0)
                _cpuMainSmoothed += (_timings[0].cpuMainThreadFrameTime - _cpuMainSmoothed) * 0.06;
        }
    }

    void OnGUI()
    {
        if (!_visible)
        {
            float uiHidden = Mathf.Max(1f, Screen.height / 900f);
            Matrix4x4 pv = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * uiHidden);
            GUI.Label(new Rect(Screen.width / uiHidden - 150, 8, 140, 22), "F1 — debug HUD");
            GUI.matrix = pv;
            return;
        }

        var sb = new StringBuilder();
        double fps = _wallSmoothed > 0.0001 ? 1000.0 / _wallSmoothed : 0.0;

        sb.AppendLine("<b>PERF — FEEL ONLY, NOT A MEASUREMENT (F1 to hide)</b>");
        sb.AppendLine($"FPS        {fps,7:F1}");
        sb.AppendLine($"WALL       {_wallSmoothed,7:F2} ms   worst/3s {_wallWorst,6:F2}   <- the only honest one here");
        sb.AppendLine($"GPU        {_gpuSmoothed,7:F2} ms   *** INFLATED ~2.6-2.7x on this Mac (Amdt 8.10) — relative signal only ***");
        sb.AppendLine($"CPU MAIN   {_cpuMainSmoothed,7:F2} ms   main-thread WORK, not wall time");
        sb.AppendLine($"render     {RaymarchFeature.LastDispatchResolution.x}x{RaymarchFeature.LastDispatchResolution.y}" +
                      $"   window {Screen.width}x{Screen.height}");

        ChunkStore store = Phase4Bootstrapper.Store;
        BrickDataPool pool = Phase4Bootstrapper.Pool;
        if (store != null && pool != null)
        {
            sb.AppendLine($"chunks     {store.ResidentCount} resident   dense bricks {store.DenseBricksHeld}");
            sb.AppendLine($"brick pool {pool.InUse} / {pool.Capacity}  peak {pool.PeakUsed}  ({store.PoolUtilisation * 100f:F1}%)" +
                          (store.IsUnderPoolPressure ? "   *** LRU VALVE ARMED (§3.6) ***" : ""));
        }

        // PERSISTENCE HEALTH. Both are silent-failure detectors and both should
        // read 0 forever; they are on screen precisely because the failure they
        // catch produced no visible symptom at all for eight days.
        var streamer = Phase4Bootstrapper.Streamer;
        if (streamer != null && (streamer.DeltaSaveFailuresTotal > 0 || streamer.ScratchExhaustionWarnings > 0))
            sb.AppendLine($"<color=#ff5555>*** PERSISTENCE: {streamer.DeltaSaveFailuresTotal} delta " +
                          $"saves FAILED, {streamer.ScratchExhaustionWarnings} scratch leaks. " +
                          "Edits are NOT reaching disk. ***</color>");

        sb.AppendLine("The trusted frame-time source is ./run-acceptance-rig.sh, not this overlay.");

        // BUILD PROVENANCE. On screen because the .app's modified date LIES:
        // Unity writes into an existing bundle in place, so the directory keeps
        // its original mtime while the binary inside is current. That ambiguity
        // once cost a round of doubt about a build that was in fact fresh, and
        // it was only settled by grepping string literals out of the shipped
        // dylib. This line is the cheap answer. See Assets/Editor/BuildStamp.cs.
        sb.AppendLine($"<b>build</b>  {BuildStampLine()}");

        var st = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = true,
            normal = { textColor = Color.white },
        };

        // IMGUI lays out in PHYSICAL pixels; on a Retina backbuffer this panel
        // was microscopic. Scale the whole block by screen height.
        float ui = Mathf.Max(1f, Screen.height / 900f);
        float w = 640f, h = 172f;
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * ui);
        float vw = Screen.width / ui;
        GUI.Box(new Rect(vw - w - 8, 6, w, h), GUIContent.none);
        GUI.Label(new Rect(vw - w, 10, w - 14, h - 8), sb.ToString(), st);
        GUI.matrix = prev;
    }
}
