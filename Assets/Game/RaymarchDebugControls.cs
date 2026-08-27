// Assets/Game/RaymarchDebugControls.cs
//
// Amendment 8.7 - runtime debug controls + GPU timing overlay, usable in a BUILT
// player. Drop on any GameObject in the Phase 2 scene.
//
// v3 (this session): T now cycles 0..4 (was 0..2, silently skipping the
// already-shipped closed-form mode 3 and the new dense-skip mode 4). Mode
// names extended to match. No other behavior change.
//
// KEYS (legacy Input Manager):
//   M : toggle the air-mip pyramid ON/OFF (OFF = pure L0 path).
//   H : cycle debug view (Beauty -> StepHeat -> UniformDense -> Normals).
//   T : cycle traversal mode 0 -> 1 -> 2 -> 3 -> 4 -> 0.
//
// OVERLAY shows: air-mip state, debug view, the ACTUAL dispatch resolution,
// and a GPU frame time read from Unity's FrameTimingManager - a real DRIVER
// GPU timestamp, available in standalone builds, NOT the Editor stats panel.

using UnityEngine;

public class RaymarchDebugControls : MonoBehaviour
{
    [SerializeField] private KeyCode _toggleMipKey = KeyCode.M;
    [SerializeField] private KeyCode _cycleViewKey = KeyCode.H;
    [SerializeField] private KeyCode _cycleTraversalModeKey = KeyCode.T;
    [SerializeField] private KeyCode _cycleIterCapKey = KeyCode.O;
    [SerializeField] private KeyCode _toggleAirMipUploadModeKey = KeyCode.L;
    [SerializeField] private KeyCode _toggleStrippedKernelKey = KeyCode.K;

    private static readonly int[] _iterCapSweep = { 1, 2, 4, 8, 16, 32, 64, 128, 400 };
    private int _iterCapIndex = _iterCapSweep.Length - 1;

    private const int TRAVERSAL_MODE_COUNT = 5; // 0..4

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
            RaymarchFeature.TraversalMode = (RaymarchFeature.TraversalMode + 1) % TRAVERSAL_MODE_COUNT;

        if (Input.GetKeyDown(_cycleIterCapKey))
        {
            _iterCapIndex = (_iterCapIndex + 1) % _iterCapSweep.Length;
            RaymarchFeature.MaxOuterIterations = _iterCapSweep[_iterCapIndex];
        }

        if (Input.GetKeyDown(_toggleAirMipUploadModeKey))
            TerrainClipmap.UseLockBufferForAirMip = !TerrainClipmap.UseLockBufferForAirMip;

        if (Input.GetKeyDown(_toggleStrippedKernelKey))
            RaymarchFeature.UseStrippedKernel = !RaymarchFeature.UseStrippedKernel;

        FrameTimingManager.CaptureFrameTimings();
        uint got = FrameTimingManager.GetLatestTimings(1, _timings);
        if (got > 0)
        {
            float gpuMs = (float)_timings[0].gpuFrameTime;
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
            case 3: return "3-ReseedClosedForm";
            case 4: return "4-DenseSkip";
            default: return mode.ToString();
        }
    }
}