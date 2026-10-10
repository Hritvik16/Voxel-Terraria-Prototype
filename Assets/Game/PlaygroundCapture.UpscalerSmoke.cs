// Assets/Game/PlaygroundCapture.UpscalerSmoke.cs
//
// -upscalersmoke : TASK 47, a SMOKE test of every -upscaler x -renderrows path.
// One pose (tg2 spawn, 11:00, sun clock paused), 1920x1080 windowed. For each
// upscaler (none, taa, fsr1, stp, fsr3) at 540 and 720 rows: apply it in
// process through UpscalerSelect.Apply (the code the flags use), let temporal
// history settle, save the frame. Then each upscaler at dyn for 600 frames,
// recording the rows it settles on. A frame that is black or garbage fails
// that path; the log gives mean RGB and the dark-pixel share for each.
//
// A LOOK, not a measurement: no frame times are claimed (dyn's smoothed ms is
// logged only to show why it settled where it did). Task 48 compares.
using System;
using System.Collections;
using System.IO;
using UnityEngine;

public partial class PlaygroundCapture
{
    private IEnumerator UpscalerSmoke()
    {
        Screen.SetResolution(1920, 1080, false);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        _demoCaption = null;
        yield return Frames(300);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "UpscalerSmoke", ts);
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        SunCycle.SetHours(11f);
        Playground.DebugHideUi = true;
        var cam = Camera.main;
        Vector3 pos = Phase4Bootstrapper.IsTg2World
            ? Phase4Bootstrapper.Tg2Spawn(Phase4Bootstrapper.Streamer.TerrainGenSource)
            : cam.transform.position;
        SetPose(cam, pos, new Vector3(8f, 30f, 0f));
        yield return Frames(400);          // stream the pose in

        L($"UPSCALER SMOKE -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})  screen {Screen.width}x{Screen.height}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  world {(Phase4Bootstrapper.IsTg2World ? "tg2" : "default")}");
        L($"pose {pos} euler (8, 30, 0), 11:00, clock paused.  dark = share of pixels with max(r,g,b) < 8");
        L("");
        L($"{"config",-12}{"dispatch",12}{"screen",12}{"meanRGB",18}{"dark",8}  file  (traced = raymarch dispatches / frames; must be ~all)");

        var kinds = new[] { UpscalerSelect.Kind.None, UpscalerSelect.Kind.Taa, UpscalerSelect.Kind.Fsr1,
                            UpscalerSelect.Kind.Stp, UpscalerSelect.Kind.Fsr3 };
        foreach (var k in kinds)
            foreach (int rows in new[] { 540, 720 })
            {
                UpscalerSelect.Apply(k, rows, false);
                int f0 = Time.frameCount, d0 = RaymarchFeature.PrimaryDispatchCount;
                yield return Frames(90);   // temporal history converges on a still pose
                yield return SmokeShot($"{UpscalerSelect.Name(k)}_{rows}",
                                       $"  traced {RaymarchFeature.PrimaryDispatchCount - d0}/{Time.frameCount - f0} frames");
            }

        // DIAGNOSTIC, not an upscaler: URP's own bilinear filter with post on, to
        // tell a tone change made by URP's post chain from one made by FSR 1 / STP.
        {
            UpscalerSelect.Apply(UpscalerSelect.Kind.Fsr1, 540, false);
            var urp = (QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline)
                      as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null) urp.upscalingFilter = UnityEngine.Rendering.Universal.UpscalingFilterSelection.Linear;
            yield return Frames(90);
            yield return SmokeShot("diag_urppost_linear_540");
        }

        L("");
        L("DYN (600 frames each, from 540 rows; target 16.7 ms, dead band 15-18 ms)");
        foreach (var k in kinds)
        {
            UpscalerSelect.Apply(k, 540, true);
            int f0 = Time.frameCount, d0 = RaymarchFeature.PrimaryDispatchCount;
            yield return Frames(600);
            int frames = Time.frameCount - f0, disp = RaymarchFeature.PrimaryDispatchCount - d0;
            yield return SmokeShot($"{UpscalerSelect.Name(k)}_dyn");
            L($"  {UpscalerSelect.Name(k)}: settled at {UpscalerSelect.Rows} rows " +
              $"(stable {UpscalerSelect.DynStableFrames} frames, {UpscalerSelect.DynChanges} steps, smoothed {UpscalerSelect.DynSmoothedMs:F1} ms; " +
              $"raymarch dispatches {disp} in {frames} frames)");
        }

        L("");
        L($"FSR 3: {RaymarchFeature.Fsr3Status}");
        L($"STP inputs: {RaymarchFeature.StpDepthTargetNote}");
        File.WriteAllText(Path.Combine(_folder, "upscalersmoke.txt"), _log.ToString());
        Debug.Log(_log.ToString());
        Application.Quit();
    }

    private IEnumerator SmokeShot(string name, string note = "")
    {
        yield return new WaitForEndOfFrame();
        Color32[] px = Grab(out Texture2D tex);
        File.WriteAllBytes(Path.Combine(_folder, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        double r = 0, g = 0, b = 0; long dark = 0;
        for (int i = 0; i < px.Length; i++)
        {
            r += px[i].r; g += px[i].g; b += px[i].b;
            if (Math.Max(px[i].r, Math.Max(px[i].g, px[i].b)) < 8) dark++;
        }
        int n = Math.Max(1, px.Length);
        var d = RaymarchFeature.LastDispatchResolution;
        L($"{name,-12}{d.x + "x" + d.y,12}{Screen.width + "x" + Screen.height,12}" +
          $"{$"({r / n:F0},{g / n:F0},{b / n:F0})",18}{100.0 * dark / n,7:F1}%  {name}.png{note}");
    }
}
