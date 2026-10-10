// Assets/Game/RenderPreset.cs
//
// How the game turns its internal render resolution into the 1080p screen
// (owner, 2026-09-26: "performance should be 60 fps", "ideally 60 fps at
// 1080p"). Measured on the in-air orbit (run-perf60.sh): the ray-marched frame
// scales with pixel count, and the sun's shadow rays are ~2/3 of it, so the
// lever that reaches 60 is rendering fewer pixels and upscaling well.
//
//   Quality      the shipped path: gate 960x540, nearest blit (every historical
//                measurement in this repo was taken here)
//   Balanced     URP FSR 1.0 to the screen from 640x360
//   Performance  URP FSR 1.0 to the screen from 480x270
//
// Every preset also turns on TEMPORAL SHADOW REUSE (RaymarchFeature.
// TemporalShadows; `-tshadow 0` keeps it off). Measured 2026-09-26, in-air orbit,
// far band + far shadows, live fluid, 1080p fullscreen RELEASE, 3 min arms
// (Logs/perf60_20260926_014432.txt):
//   shipped 960, no temporal        55.2 ms  18.1 FPS
//   960 + temporal shadows          31.4 ms  31.9 FPS
//   FSR 640 + temporal              20.0 ms  49.9 FPS   (Balanced)
//   FSR 480 + temporal              15.6 ms  64.1 FPS   (Performance: the 60 FPS target)
// The 480 image is visibly softer than 960 (fine lines on snow, far hills);
// F3 in the Playground cycles the presets so the trade can be judged live.
//
// FSR runs in URP's post chain, so the camera's post-processing must be ON --
// and the PC pipeline asset's default volume profile (SampleSceneProfile:
// Bloom, Vignette, Neutral tonemapping) then tints the whole frame grey
// (Screenshots/Upscale/00c). The ray-marcher already does its own display
// transform, so a preset that upscales swaps in an EMPTY volume profile:
// FSR becomes the only post step.
//
// `-renderpreset quality|balanced|performance` overrides (last match wins, as
// PlaygroundCapture's args do); PlaygroundCapture's -upscale still overrides
// both for measurement arms.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class RenderPreset
{
    public enum Kind { Quality, Balanced, Performance }

    /// The Playground's default. 2026-10-01, owner (game-design brief): 960x540
    /// is the MINIMUM internal resolution, so Quality; the work is to make it
    /// reach 60 FPS. Measured on the tg2 walk: 960 31.5-33.9 ms vs 480
    /// 12.0-12.2 ms (CLAUDE.md known issues). Performance/Balanced stay
    /// reachable with F3 and -renderpreset for measurement.
    public static Kind Default = Kind.Quality;
    public static Kind Current { get; private set; } = Kind.Quality;
    private static VolumeProfile _empty;
    private static bool _applied;
    private static int _appliedWidth;

    /// Call every frame: the internal width is a FIXED pixel count, so the
    /// render scale must follow the screen (a window resize, or a fullscreen
    /// switch that lands a frame after Start).
    public static void Tick()
    {
        if (_applied && Current != Kind.Quality && !UpscalerSelect.Active && Screen.width > 0 && Screen.width != _appliedWidth) Apply(Current);
    }

    public static Kind FromArgs(Kind fallback)
    {
        string v = null;
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-renderpreset") v = a[i + 1];
        if (v == null) return fallback;
        switch (v.ToLowerInvariant())
        {
            case "quality": return Kind.Quality;
            case "balanced": return Kind.Balanced;
            case "performance": return Kind.Performance;
            default: return fallback;
        }
    }

    private static int ArgInt(string flag, int fallback)
    {
        int v = fallback;
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == flag && int.TryParse(a[i + 1], out int p)) v = p;
        return v;
    }

    public static int InternalWidth(Kind k) => k == Kind.Balanced ? 640 : k == Kind.Performance ? 480 : 960;

    public static void Apply(Kind k)
    {
        UpscalerSelect.Deactivate();    // F3 takes the frame back from -upscaler (task 47)
        Current = k;
        _applied = true;
        _appliedWidth = Screen.width;
        RaymarchFeature.TemporalShadows = ArgInt("-tshadow", 1) != 0;
        var urp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        var cam = Camera.main;
        var acd = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
        if (k == Kind.Quality)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
            if (urp != null) { urp.renderScale = 1f; urp.upscalingFilter = UpscalingFilterSelection.Auto; }
            if (acd != null) acd.renderPostProcessing = false;
            return;
        }
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.Native;
        if (urp != null)
        {
            urp.renderScale = InternalWidth(k) / (float)Mathf.Max(1, Screen.width > 0 ? Screen.width : 1920);
            urp.upscalingFilter = UpscalingFilterSelection.FSR;
            if (_empty == null) _empty = ScriptableObject.CreateInstance<VolumeProfile>();
            urp.volumeProfile = _empty;          // no Bloom / Vignette / tonemap tint: FSR only
        }
        if (acd != null) { acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.None; }
    }
}
