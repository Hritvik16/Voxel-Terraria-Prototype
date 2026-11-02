// Assets/Game/UpscalerSelect.cs
//
// TASK 47 (engine plan step 3; owner Q3 "compare all three resolutions", Q4
// "plug in ready-made upscalers"). The raymarch renders at a lower internal
// resolution and a selectable upscaler outputs the screen (1080p):
//
//   -upscaler none   bilinear stretch of the gate (today's shipped blit)
//             taa    our TAA (task 06) at the internal size + the same bilinear stretch
//             fsr1   URP 17.3's FidelityFX Super Resolution 1.0 filter (spatial)
//             stp    URP 17.3's Spatial-Temporal Post-processing (temporal); the
//                    raymarch writes URP's depth + motion vectors for it
//             fsr3   AMD FSR 3.1 upscaler, MIT Unity port (Assets/ThirdParty/FidelityFX.FSR)
//   -renderrows 540 | 720 | dyn     internal rows (width follows the screen's
//                    aspect: 960x540, 1280x720 at 16:9). dyn moves between 540
//                    and 720 in 36-row steps on smoothed frame time (target
//                    16.7 ms) and logs every step.
//
// TASK 156 (capture-only): a PINNED -renderrows N (not dyn) clamps to
// CaptureMinRows-MaxRows (360-720), so a capture can shoot e.g. 432 rows. Play
// cannot reach it: PlaySettings only passes 540 / 720 / dyn, and the dynamic
// controller still moves within MinRows-MaxRows.
//
// TASK 159 (owner Q100 (a), 2026-10-31; PERF_30FPS_PLAN step 3): PLAY's dyn
// range is 432-540 rows at a ~30 ms target (dead band 27-33 ms), so the rows
// drop only on views heavier than 30 fps and stay at 540 on light ones. Every
// run takes the OLD range (540-720 at 16.7 ms) unless it is a PlaySettings run
// (Playground.Start) or passes -dynfloor N: N >= 540 = the old range, N in
// 360-539 = N-540 at 30 ms. tools/play-flags.sh passes -dynfloor 432, so
// MODE=playwalk follows play; `EXTRA="-dynfloor 540"` gives the old controller.
// Either flag alone turns this on (defaults: none, 540). Last match wins, like
// PlaygroundCapture. NEITHER FLAG GIVEN: capture/rig runs keep RenderPreset /
// the shipped gate; an interactive run (task 52, owner Q38 (a)) starts with
// fsr3 dyn unless it passes -renderpreset, -taa or -checker (Playground.Start).
// RenderPreset.Apply (F3) hands control back (Deactivate).
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class UpscalerSelect
{
    public enum Kind { None, Taa, Fsr1, Stp, Fsr3 }
    public const int MinRows = 540, MaxRows = 720, DynStep = 36;
    /// Task 156: the floor for a pinned (non-dyn) row count; capture runs only.
    public const int CaptureMinRows = 360;
    /// Task 166: the ceiling for a pinned row count; capture runs only (1080 = FSR 3
    /// as anti-aliasing at the output size, no upscale). Play's paths clamp to MaxRows first.
    public const int CaptureMaxRows = 1080;
    /// The dyn controller's range and target (task 159): UseDynRange.
    public static int DynFloor { get; private set; } = MinRows;
    public static int DynCeil { get; private set; } = MaxRows;
    public static float DynTargetMs { get; private set; } = 16.7f;
    /// Task 159: play's dyn floor (owner Q100 (a)).
    public const int PlayDynFloor = 432;
    public const float PlayDynTargetMs = 30f;

    public static bool Active { get; private set; }
    public static Kind Current { get; private set; } = Kind.None;
    public static int Rows { get; private set; } = MinRows;
    public static bool Dynamic { get; private set; }
    /// Frames since the dynamic controller last moved the rows.
    public static int DynStableFrames { get; private set; }
    public static int DynChanges { get; private set; }
    public static float DynSmoothedMs { get; private set; }

    private static readonly bool ArgTaa = int.TryParse(ArgValue("-taa"), out int t) && t != 0;
    private static VolumeProfile _empty;
    private static int _appliedScreenH;

    public static string Name(Kind k) => k.ToString().ToLowerInvariant();

    /// False when neither -upscaler nor -renderrows is on the command line.
    public static bool FromArgs(out Kind kind, out int rows, out bool dyn)
    {
        kind = Kind.None; rows = MinRows; dyn = false;
        string u = ArgValue("-upscaler"), r = ArgValue("-renderrows");
        if (u == null && r == null) return false;
        if (u != null && !TryParseKind(u, out kind))
            Debug.LogError($"[UpscalerSelect] unknown -upscaler {u} (none|taa|fsr1|stp|fsr3); using none");
        if (r != null)
        {
            if (r.Equals("dyn", StringComparison.OrdinalIgnoreCase)) dyn = true;
            else if (int.TryParse(r, out int n)) rows = Mathf.Clamp(n, CaptureMinRows, MaxRows);
            else Debug.LogError($"[UpscalerSelect] unknown -renderrows {r} ({CaptureMinRows}-{MaxRows}|dyn); using 540");
        }
        return true;
    }

    /// Task 166 (candidate fixes, OFF unless given; play's defaults unchanged): -fsr3sharp N = RCAS sharpness
    /// N/10 (0 = RCAS off; default 8 = 0.8), -fsr3sc N = cut FSR 3's auto shading change by N/10 (default 0).
    /// Called by Playground.Start for every run (play included), before the upscaler is applied.
    public static void Fsr3KnobsFromArgs()
    {
        if (int.TryParse(ArgValue("-fsr3sharp"), out int sh))
        {
            RaymarchFeature.Fsr3Sharpen = sh > 0;
            RaymarchFeature.Fsr3Sharpness = Mathf.Clamp01(sh / 10f);
        }
        if (int.TryParse(ArgValue("-fsr3sc"), out int sc)) RaymarchFeature.Fsr3ShadingChangeCut = Mathf.Clamp01(sc / 10f);
    }

    /// Task 159: floor >= MinRows -> the old range (540-720, 16.7 ms); below ->
    /// floor-540 at 30 ms. Takes effect at the next Apply.
    public static void UseDynRange(int floor)
    {
        if (floor >= MinRows) { DynFloor = MinRows; DynCeil = MaxRows; DynTargetMs = 16.7f; }
        else { DynFloor = Mathf.Max(CaptureMinRows, floor); DynCeil = MinRows; DynTargetMs = PlayDynTargetMs; }
    }

    /// Task 159: `-dynfloor N` if given (last match), else `defaultFloor`.
    public static void UseDynRangeFromArgs(int defaultFloor) =>
        UseDynRange(int.TryParse(ArgValue("-dynfloor"), out int f) ? f : defaultFloor);

    public static string DynRangeLabel => $"{DynFloor}-{DynCeil}";

    public static bool TryParseKind(string s, out Kind k)
    {
        switch (s.ToLowerInvariant())
        {
            case "none": k = Kind.None; return true;
            case "taa": k = Kind.Taa; return true;
            case "fsr1": k = Kind.Fsr1; return true;
            case "stp": k = Kind.Stp; return true;
            case "fsr3": k = Kind.Fsr3; return true;
            default: k = Kind.None; return false;
        }
    }

    public static void Apply(Kind k, int rows, bool dyn)
    {
        Active = true;
        Current = k;
        Dynamic = dyn;
        DynHeld = false;
        DynStableFrames = 0; DynChanges = 0; DynSmoothedMs = 0f;
        Rows = dyn ? Mathf.Min(MinRows, DynCeil) : Mathf.Clamp(rows, CaptureMinRows, CaptureMaxRows);
        ApplyRows();
        Debug.Log($"[UpscalerSelect] {Describe()}");
    }

    /// Task 51's real-rate clips: the dynamic controller frozen at `rows` (the
    /// rows it ran at when timed), so capture-slowed frames cannot walk it down.
    /// Cleared by Apply.
    public static bool DynHeld { get; private set; }
    public static void HoldDynRows(int rows)
    {
        Rows = Mathf.Clamp(rows, DynFloor, DynCeil);
        DynHeld = true;
        ApplyRows();
    }

    public static string Describe() =>
        $"upscaler {Name(Current)}, internal {InternalWidth(Rows)}x{Rows}{(Dynamic ? $" (dyn {DynRangeLabel}, target {DynTargetMs:F1} ms)" : "")} -> {Screen.width}x{Screen.height}";

    /// Hands the frame back to RenderPreset (called from RenderPreset.Apply).
    public static void Deactivate()
    {
        if (!Active) return;
        Active = false;
        RaymarchFeature.UpscaleInputs = RaymarchFeature.UpscalerInputMode.Off;
        RaymarchFeature.UseCameraDataProjection = false;
        RaymarchFeature.Fsr3MaxRenderSize = Vector2Int.zero;
        RaymarchFeature.Taa = ArgTaa || RaymarchFeature.TaaChecker;
    }

    /// Internal width for a row count at the screen's aspect, rounded to even.
    public static int InternalWidth(int rows)
    {
        float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
        return Mathf.Max(2, Mathf.RoundToInt(rows * aspect * 0.5f) * 2);
    }

    /// Every frame: the dynamic controller, and the render scale follows a
    /// screen resize (fsr1/stp scale URP's target, a fraction of the screen).
    public static void Tick()
    {
        if (!Active) return;
        if (Dynamic && !DynHeld)
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            DynSmoothedMs = DynSmoothedMs <= 0f ? ms : Mathf.Lerp(DynSmoothedMs, ms, 0.05f);
            DynStableFrames++;
            // 30 frames between steps so the smoothed time sees the new size;
            // a dead band round the target so it settles: 15-18 ms round the
            // old 16.7 ms, 27-33 ms round play's 30 ms (task 159; one 36-row
            // step is ~13% of the pixels, inside the +-10% band).
            if (DynStableFrames >= 30)
            {
                bool old = DynTargetMs < 20f;
                float hi = old ? 18f : DynTargetMs * 1.1f, lo = old ? 15f : DynTargetMs * 0.9f;
                int next = Rows;
                if (DynSmoothedMs > hi && Rows > DynFloor) next = Mathf.Max(DynFloor, Rows - DynStep);
                else if (DynSmoothedMs < lo && Rows < DynCeil) next = Mathf.Min(DynCeil, Rows + DynStep);
                if (next != Rows)
                {
                    Rows = next; DynStableFrames = 0; DynChanges++;
                    ApplyRows();
                    Debug.Log($"[UpscalerSelect] dyn -> {Rows} rows (smoothed {DynSmoothedMs:F1} ms)");
                }
            }
        }
        if (Screen.height > 0 && Screen.height != _appliedScreenH) ApplyRows();
    }

    private static void ApplyRows()
    {
        _appliedScreenH = Screen.height;
        int rows = Rows, w = InternalWidth(rows);
        var urp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        var cam = Camera.main;
        var acd = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
        Kind k = Current;
        bool urpScaled = k == Kind.Fsr1 || k == Kind.Stp;

        RaymarchFeature.UpscaleInputs = k == Kind.Stp ? RaymarchFeature.UpscalerInputMode.Stp
                                      : k == Kind.Fsr3 ? RaymarchFeature.UpscalerInputMode.Fsr3
                                      : RaymarchFeature.UpscalerInputMode.Off;
        RaymarchFeature.UseCameraDataProjection = k == Kind.Stp;
        RaymarchFeature.Fsr3MaxRenderSize = k == Kind.Fsr3 && Dynamic ? new Vector2Int(InternalWidth(MaxRows), MaxRows) : Vector2Int.zero;
        // Our TAA resolve: on for taa; left as the command line says for none
        // and fsr1; STP / FSR 3 replace it (RaymarchFeature skips the resolve).
        RaymarchFeature.Taa = k == Kind.Taa || ArgTaa || RaymarchFeature.TaaChecker;

        if (urpScaled)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.Native;
            if (urp != null)
            {
                urp.renderScale = rows / (float)Mathf.Max(1, Screen.height > 0 ? Screen.height : 1080);
                urp.upscalingFilter = k == Kind.Stp ? UpscalingFilterSelection.STP : UpscalingFilterSelection.FSR;
                if (_empty == null) _empty = ScriptableObject.CreateInstance<VolumeProfile>();
                urp.volumeProfile = _empty;      // the upscaler is the only post step (RenderPreset.cs)
            }
            if (acd != null)
            {
                acd.renderPostProcessing = true;
                acd.antialiasing = k == Kind.Stp ? AntialiasingMode.TemporalAntiAliasing : AntialiasingMode.None;
            }
        }
        else
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(w, rows);
            if (urp != null) { urp.renderScale = 1f; urp.upscalingFilter = UpscalingFilterSelection.Auto; }
            if (acd != null) { acd.renderPostProcessing = false; acd.antialiasing = AntialiasingMode.None; }
        }
    }

    private static string ArgValue(string flag)
    {
        string v = null;
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == flag) v = a[i + 1];
        return v;
    }
}
