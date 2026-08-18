// Assets/Game/RaymarchDebugControls.cs
//
// Amendment 8.7 - runtime debug controls + GPU timing overlay, usable in a BUILT
// player. Drop on any GameObject in the Phase 2 scene.
//
// KEYS (legacy Input Manager):
//   M : toggle the air-mip pyramid ON/OFF (OFF = pure L0 path).
//   H : cycle debug view (Beauty -> StepHeat -> UniformDense -> Normals).
//
// OVERLAY shows: air-mip state, debug view, the ACTUAL dispatch resolution
// (so you can confirm the gate 1920x1080 is really what's being rendered, not
// the Retina backing store), and a GPU frame time read from Unity's
// FrameTimingManager - a real DRIVER GPU timestamp, available in standalone
// builds, NOT the Editor stats panel. Since the raymarch compute dominates this
// scene's GPU work (dispatch + a cheap upscale blit), GPU frame time is
// effectively the raymarch cost and is the right number to compare against the
// 8 ms Phase 2 budget.
//
// WHY THIS IS GATE-VALID (not the thing the doc says to distrust): the doc
// distrusts Unity's EDITOR performance readouts because they carry editor
// overhead. FrameTimingManager.gpuFrameTime is the GPU's own hardware timer,
// surfaced in a standalone build without the Instruments capture layer that was
// inflating the earlier numbers. Cross-check it against ONE Instruments GPU
// capture to confirm agreement, then trust it for fast A/B iteration.
//
// FrameTimingManager needs enabling and has a few-frames latency; if it returns
// 0 the overlay says "GPU timing unavailable" and you fall back to Instruments.

using UnityEngine;

public class RaymarchDebugControls : MonoBehaviour
{
    [SerializeField] private KeyCode _toggleMipKey = KeyCode.M;
    [SerializeField] private KeyCode _cycleViewKey = KeyCode.H;
    [SerializeField] private KeyCode _cycleTraversalModeKey = KeyCode.T;
    [SerializeField] private KeyCode _cycleIterCapKey = KeyCode.O;
    [SerializeField] private KeyCode _toggleAirMipUploadModeKey = KeyCode.L;
    [SerializeField] private KeyCode _toggleStrippedKernelKey = KeyCode.K;

    // Sweep values for the iteration-cap diagnostic. 400 = uncapped (matches
    // the shader's own default), so cycling wraps back to "off" cleanly.
    private static readonly int[] _iterCapSweep = { 1, 2, 4, 8, 16, 32, 64, 128, 400 };
    private int _iterCapIndex = _iterCapSweep.Length - 1; // start uncapped

    [Tooltip("Force 1920x1080 window on start. NOTE: the actual RAY resolution is " +
             "clamped separately in RaymarchFeature (_forceGateResolution); this " +
             "just sizes the window. The overlay shows the real dispatch res.")]
    [SerializeField] private bool _force1080pWindowOnStart = true;

    private float _smoothedGpuMs = 0f;
    private FrameTiming[] _timings = new FrameTiming[1];

    void Start()
    {
        if (_force1080pWindowOnStart)
            Screen.SetResolution(1920, 1080, Screen.fullScreenMode);
    }

    void Update()
    {
        if (Input.GetKeyDown(_toggleMipKey))
            RaymarchFeature.AirMipEnabled = !RaymarchFeature.AirMipEnabled;

        if (Input.GetKeyDown(_cycleViewKey))
        {
            RaymarchFeature.UseDebugViewOverride = true;
            int next = ((int)RaymarchFeature.DebugViewOverride + 1) % 4;
            RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)next;
        }

        if (Input.GetKeyDown(_cycleTraversalModeKey))
            RaymarchFeature.TraversalMode = (RaymarchFeature.TraversalMode + 1) % 3;

        if (Input.GetKeyDown(_cycleIterCapKey))
        {
            _iterCapIndex = (_iterCapIndex + 1) % _iterCapSweep.Length;
            RaymarchFeature.MaxOuterIterations = _iterCapSweep[_iterCapIndex];
        }

        if (Input.GetKeyDown(_toggleAirMipUploadModeKey))
            TerrainClipmap.UseLockBufferForAirMip = !TerrainClipmap.UseLockBufferForAirMip;

        if (Input.GetKeyDown(_toggleStrippedKernelKey))
            RaymarchFeature.UseStrippedKernel = !RaymarchFeature.UseStrippedKernel;

        // Pull the latest GPU frame time from the driver. Must call
        // CaptureFrameTimings each frame; GetLatestTimings returns recent frames.
        FrameTimingManager.CaptureFrameTimings();
        uint got = FrameTimingManager.GetLatestTimings(1, _timings);
        if (got > 0)
        {
            float gpuMs = (float)_timings[0].gpuFrameTime; // milliseconds
            if (gpuMs > 0f)
                _smoothedGpuMs = _smoothedGpuMs <= 0f ? gpuMs : Mathf.Lerp(_smoothedGpuMs, gpuMs, 0.1f);
        }
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        string mipState = RaymarchFeature.AirMipEnabled ? "ON" : "OFF (L0 only)";
        Vector2Int res = RaymarchFeature.LastDispatchResolution;
        string gpuLine = _smoothedGpuMs > 0f
            ? $"GPU frame: {_smoothedGpuMs:F2} ms"
            : "GPU timing unavailable (use Instruments)";

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(8, 8, 440, 268), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(18, 12, 480, 28),
            $"AIR-MIP: {mipState}   [{_toggleMipKey}]", style);
        GUI.Label(new Rect(18, 40, 480, 28),
            $"VIEW: {RaymarchFeature.DebugViewOverride}   [{_cycleViewKey}]", style);
        GUI.Label(new Rect(18, 68, 480, 28),
            $"DISPATCH: {res.x} x {res.y}", style);
        GUI.Label(new Rect(18, 96, 480, 28),
            $"MODE: {TraversalModeName(RaymarchFeature.TraversalMode)}   [{_cycleTraversalModeKey}]", style);
        GUI.Label(new Rect(18, 124, 480, 28),
            $"ITER CAP: {(RaymarchFeature.MaxOuterIterations >= 400 ? "off" : RaymarchFeature.MaxOuterIterations.ToString())}   [{_cycleIterCapKey}]", style);
        GUI.Label(new Rect(18, 152, 480, 28),
            $"AIRMIP UPLOAD: {(TerrainClipmap.UseLockBufferForAirMip ? "LockBufferForWrite" : "SetData")}   [{_toggleAirMipUploadModeKey}]", style);
        GUI.Label(new Rect(18, 180, 480, 28),
            $"KERNEL: {(RaymarchFeature.UseStrippedKernel ? "STRIPPED (mode1/Beauty only)" : "FULL")}   [{_toggleStrippedKernelKey}]", style);

       // GPU line in a distinct colour - THIS is the gate-relevant number.
        GUIStyle gpuStyle = new GUIStyle(style)
        { normal = { textColor = _smoothedGpuMs > 0f ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.6f, 0.3f) } };
        GUI.Label(new Rect(18, 208, 480, 28), gpuLine, gpuStyle);

        GUI.Label(new Rect(18, 240, 480, 24),
            "GPU frame time = driver GPU timer (standalone, gate-valid)",
            new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } });
    }

    private static string TraversalModeName(int mode)
    {
        switch (mode)
        {
            case 0: return "0-LeapSpan";
            case 1: return "1-Reseed";
            case 2: return "2-OccupancyChain";
            default: return mode.ToString();
        }
    }
}