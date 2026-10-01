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
    [SerializeField] private bool _visible = false;   // STAGE I -- detail behind F1
    /// The always-on strip. Separately toggleable so a screenshot pass can
    /// clear the frame completely; `DebugHideUi` still wins over both.
    [SerializeField] private bool _minimalVisible = true;
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
    /// §6.5's four rungs, named rather than numbered on screen. A bare "2" on a
    /// HUD tells a player nothing about what they are looking at.
    private static string ShadowRungName(int level)
    {
        switch (level)
        {
            case 0: return "0 FULL (soft penumbra, C.10)";
            case 1: return "1 HALF-RES beyond LOD1";
            case 2: return "2 HARD only";
            case 3: return "3 OFF (flat N.L + ambient)";
            default: return level.ToString();
        }
    }

    /// The same four rungs said in PLAIN LANGUAGE, for the always-on strip.
    ///
    /// The names above are correct and cite the spec, which is what you want on
    /// a diagnostic panel you opened deliberately. They are the wrong register
    /// for a line that is on screen all the time while someone is judging how
    /// the game LOOKS: "HALF-RES beyond LOD1" asks the reader to know what LOD1
    /// is before they can tell whether the picture in front of them is the good
    /// one. These say what you would SEE.
    internal static string ShadowRungPlain(int level)
    {
        switch (level)
        {
            case 0: return "Full (soft edges)";
            case 1: return "Cheaper far away";
            case 2: return "Hard edges only";
            case 3: return "Off";
            default: return "rung " + level;
        }
    }

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
        if (Playground.DebugHideUi || DevMap.IsOpen) return;
        if (_minimalVisible) DrawMinimalStrip();
        if (!_visible) return;

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

        // ---- §10.3 item 7, Phase 7: "sun angle/time" ----
        // Plus the two Phase 7 numbers a player needs to interpret what they are
        // looking at: how many of their torches are actually being drawn, and
        // which §6.5 rung is in force. Without the first, the §3.8 cap is
        // invisible and "my torch did nothing" has no explanation on screen.
        sb.AppendLine();
        sb.AppendLine($"<b>SUN</b>        {SunCycle.ClockString()}   {SunCycle.ElevationDegrees,6:F1} deg   " +
                      $"{SunCycle.PhaseName}{(VoxelEngine.Rendering.LightingDriver.ClockPaused ? "   <color=#ffd479>[PAUSED]</color>" : "")}");

        var lights = RaymarchFeature.Lights;
        if (lights != null)
        {
            string capNote = lights.CapIsBinding
                ? $"   <color=#ffd479>CAP BINDING - {lights.RegisteredCount - lights.ActiveCount} " +
                  "furthest not drawn (§3.8, furthest-first)</color>"
                : "";
            sb.AppendLine($"<b>LIGHTS</b>     {lights.ActiveCount} active / {lights.RegisteredCount} placed   " +
                          $"cap {EngineConfig.MAX_ACTIVE_LIGHTS}{capNote}");
        }
        var pg = FindAnyObjectByType<Playground>();
        if (pg != null)
            sb.AppendLine($"<b>PRESET</b>     {pg.CurrentPresetName}   <color=#9fb6c9>[ / ] to cycle</color>");
        sb.AppendLine($"<b>SHADOWS</b>    {ShadowRungName(RaymarchFeature.ShadowQualityLevel)}   " +
                      $"k_soft {LightingConfig.Active.kSoft:F3}" +
                      (LightingConfig.LastError != null
                        ? $"   <color=#ff8888>LightingConfig: {LightingConfig.LastError}</color>"
                        : ""));
        sb.AppendLine();
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
        // HEIGHT GROWN FOR PHASE 7's THREE LINES (SUN / LIGHTS / SHADOWS).
        // The first Playground capture with those lines in place showed the box
        // clipping mid-word through LIGHTS, so the §3.8 cap readout -- the whole
        // point of which is to make the degradation VISIBLE -- was itself
        // invisible. A HUD line that is cut off is worse than one that is
        // absent, because it looks like it is there.
        float w = 740f, h = 292f;   // +1 line for Stage C's PRESET readout
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * ui);
        float vw = Screen.width / ui;
        // Sit BELOW the always-on strip rather than on top of it. Both anchor to
        // the same corner on purpose (F1 must not make the strip move), which
        // means the detail panel has to yield the strip's 26 px + gap.
        float topY = _minimalVisible ? 36f : 6f;
        GUI.Box(new Rect(vw - w - 8, topY, w, h), GUIContent.none);
        GUI.Label(new Rect(vw - w, topY + 4f, w - 14, h - 8), sb.ToString(), st);
        GUI.matrix = prev;
    }

    /// STAGE I -- THE ALWAYS-ON LINE.
    ///
    /// The owner's note was that the two things you must know to judge a
    /// screenshot -- which preset and which shadow rung produced it -- were
    /// only visible inside a debug panel, so every judgement made with that
    /// panel closed was made without them. This line is the fix: it is the
    /// smallest set of facts that makes the picture INTERPRETABLE, and nothing
    /// else. Frame time is here too because "which rung can I afford" is the
    /// question the rungs exist to answer, and a number you have to open a
    /// panel to see is a number you will not check while flying around.
    ///
    /// The FPS here is a smoothed on-screen readout and is NOT evidence --
    /// CLAUDE.md is explicit that the only trusted frame-time source is the
    /// rig's own report from a release standalone. It is a live indicator, and
    /// the strip says so rather than letting the number look authoritative.
    private void DrawMinimalStrip()
    {
        var pg = FindAnyObjectByType<Playground>();
        string preset = pg != null ? pg.CurrentPresetName : "-";
        // The as-found name is "custom (LightingConfig.json as found)", which is
        // longer than this line has room for. A clipped HUD line is worse than
        // an absent one -- it looks like it is there -- so truncate explicitly
        // rather than letting the box cut it mid-word.
        if (preset.Length > 22) preset = preset.Substring(0, 21) + "\u2026";

        double ms = _wallSmoothed;
        double fps = ms > 0.0 ? 1000.0 / ms : 0.0;

        // Colour the frame time against the 16.67 ms frame, so the strip
        // answers "am I inside budget" without arithmetic.
        string fpsCol = ms <= 16.67 ? "#8fe08f" : ms <= 22.0 ? "#ffd479" : "#ff8a8a";

        string text =
            $"<b>PRESET</b> {preset}   <b>SHADOWS</b> {ShadowRungPlain(RaymarchFeature.ShadowQualityLevel)}   " +
            $"<color={fpsCol}><b>{fps:F0} fps</b> ({ms:F1} ms)</color>" +
            $"   <color=#7f93a3>F1 detail</color>";

        var st = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            richText = true,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,          // one line, always -- wrapping is what clipped it
            clipping = TextClipping.Overflow,
            normal = { textColor = Color.white },
        };

        float ui = Mathf.Max(1f, Screen.height / 900f);
        Matrix4x4 prev = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * ui);
        float vw = Screen.width / ui;
        // Width measured from the text itself rather than guessed: the preset
        // name is variable-length and a fixed box is what clipped the first
        // attempt. Bounded so it cannot run off the left of the screen.
        float w = Mathf.Clamp(st.CalcSize(new GUIContent(text)).x + 24f, 320f, vw - 16f);
        const float h = 24f;
        // Top-right, where the detail panel also lives, so opening F1 does not
        // make the strip jump to somewhere else on screen.
        var r = new Rect(vw - w - 8, 6, w, h);
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x + 10, r.y, r.width - 16, r.height), text, st);
        GUI.matrix = prev;
    }
}
