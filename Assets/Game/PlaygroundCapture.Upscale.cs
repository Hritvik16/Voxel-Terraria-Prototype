// Assets/Game/PlaygroundCapture.Upscale.cs
//
// -upscaleab : WHAT DOES UPSCALING ACTUALLY LOOK LIKE, AND WHAT DOES IT COST?
//
// This project has never upscaled with anything but a nearest-neighbour blit
// (RaymarchFeature's Blitter.BlitTexture(..., bilinear: false)). This mode tests
// URP's OUT-OF-THE-BOX filters -- nearest, bilinear, FSR 1.0, STP -- at several
// internal resolutions, with nothing custom: our pass renders at URP's render
// scale (GateResMode.Native follows cameraTargetDescriptor) and URP's own final
// pass upscales. The only wiring is RaymarchFeature.UseCameraDataProjection for
// the STP arms, so rays carry URP's sub-pixel jitter.
//
// Every frame is scored against a NATIVE 1920x1080 render of the same pose:
//   MAE   mean absolute error, 0-255 per channel (lower = closer to native)
//   PSNR  in dB (higher = closer)
//   EDGE  gradient energy / native's (1.00 = as sharp as native; <1 = softer;
//         >1 = over-sharpened or aliased)
// plus a MOTION test: the camera glides into pose P1 over 40 frames and the
// arrival frame is scored against the static native P1 -- temporal ghosting
// (STP) shows up here and nowhere else.
//
// Frame times printed here are DIAGNOSTIC (240 frames, back-to-back). The FPS
// claim comes from -sustained arms driven by -upscale / -internalw.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public partial class PlaygroundCapture
{
    private struct UpscaleConfig
    {
        public string Name;
        public bool ShippedGate;          // our own forced gate + nearest blit (today's path)
        public int InternalW;             // for URP render-scale configs
        public UpscalingFilterSelection Filter;
        public bool PostProcessing;
    }

    /// Applies an upscale configuration. Also used by -sustained via -upscale.
    private static void ApplyUpscale(UpscaleConfig c)
    {
        var urp = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        var cam = Camera.main;
        var acd = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
        if (c.ShippedGate)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
            if (urp != null) { urp.renderScale = 1f; urp.upscalingFilter = UpscalingFilterSelection.Auto; }
        }
        else
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.Native;
            if (urp != null)
            {
                urp.renderScale = c.InternalW / 1920f;   // the capture resolution; Screen.width can lag a SetResolution by a frame
                urp.upscalingFilter = c.Filter;
            }
        }
        bool stp = !c.ShippedGate && c.Filter == UpscalingFilterSelection.STP;
        RaymarchFeature.UseCameraDataProjection = stp;
        if (acd != null)
        {
            acd.renderPostProcessing = c.PostProcessing || stp;
            acd.antialiasing = stp ? AntialiasingMode.TemporalAntiAliasing : AntialiasingMode.None;
        }
    }

    /// -upscale <point|linear|fsr|stp|shipped> -internalw <W>, for -sustained.
    private static string ApplyUpscaleArgs()
    {
        string f = ArgValue("-upscale");
        if (f == null) return null;
        var c = new UpscaleConfig { InternalW = ArgInt("-internalw", 960), PostProcessing = true };
        switch (f.ToLowerInvariant())
        {
            case "shipped": c.ShippedGate = true; c.PostProcessing = false; break;
            case "point":   c.Filter = UpscalingFilterSelection.Point; break;
            case "linear":  c.Filter = UpscalingFilterSelection.Linear; break;
            case "fsr":     c.Filter = UpscalingFilterSelection.FSR; break;
            case "stp":     c.Filter = UpscalingFilterSelection.STP; break;
            default: return "unknown -upscale " + f;
        }
        ApplyUpscale(c);
        return c.ShippedGate ? "shipped (gate 960x540, nearest blit)" : $"URP {c.Filter} from {c.InternalW}x{c.InternalW * 9 / 16}";
    }

    private IEnumerator UpscaleAb()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "UpscaleAB", ts);
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = 0;
        Playground.DebugHideUi = true;

        var store = Phase4Bootstrapper.Store;
        var cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x, cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 peak = DeepestNear(store, camVox, 240);
        int3 top = new int3(peak.x, SurfaceYAt(store, peak.x, peak.z), peak.z);
        float golden = SunCycle.GoldenHourHours;

        var poses = new (string name, float hours, Action set)[]
        {
            ("P1_ground_golden", golden, () => SetPose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(2f, 35f, 0f))),
            ("P2_wide_noon",     12f,    () => SetPose(cam, new Vector3(1280f, 14f, 1250f), new Vector3(10f, 0f, 0f))),
            ("P3_close_golden",  golden, () => LookAtVox(cam, top + new int3(-14, 8, -14), top)),
            ("P4_high_noon",     12f,    () => SetPose(cam, new Vector3(1300f, 24f, 1240f), new Vector3(26f, -18f, 0f))),
        };

        var configs = new List<UpscaleConfig>
        {
            new UpscaleConfig { Name = "REF_native1920", InternalW = 1920, Filter = UpscalingFilterSelection.Point },
            new UpscaleConfig { Name = "REF_native1920_pp", InternalW = 1920, Filter = UpscalingFilterSelection.Point, PostProcessing = true },
            new UpscaleConfig { Name = "shipped_gate960_nearest", ShippedGate = true },
        };
        foreach (int w in new[] { 960, 640, 560, 480 })
            foreach (var f in new[] { UpscalingFilterSelection.Point, UpscalingFilterSelection.Linear,
                                      UpscalingFilterSelection.FSR, UpscalingFilterSelection.STP })
                configs.Add(new UpscaleConfig { Name = $"{f}_{w}", InternalW = w, Filter = f, PostProcessing = true });

        L($"UPSCALER A/B -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  screen {Screen.width}x{Screen.height} " +
          $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  {(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  shadow rung 0");
        L("Scores vs native 1920x1080 at the same pose. MAE lower=better, PSNR higher=better,");
        L("EDGE 1.00 = native sharpness. MOTION = MAE of a frame arriving at P1 mid-glide vs static native P1.");
        L("ms = DIAGNOSTIC orbit mean at golden hour (240 frames), not a budget figure.");
        L("");
        L($"{"config",-26}{"MAE",7}{"PSNR",7}{"EDGE",7}{"MOTION",8}{"ms",8}   per-pose MAE");

        var reference = new Dictionary<string, Color32[]>();
        Color32[] refMotionTarget = null;

        foreach (var c in configs)
        {
            ApplyUpscale(c);
            yield return Frames(30);
            double maeSum = 0, psnrSum = 0, edgeSum = 0; int scored = 0;
            var perPose = new List<string>();

            foreach (var (pname, hours, set) in poses)
            {
                SunCycle.SetHours(hours);
                set();
                yield return Frames(40);                  // STP: let the history converge on a still pose
                yield return new WaitForEndOfFrame();
                Color32[] px = Grab(out Texture2D tex);
                File.WriteAllBytes(Path.Combine(_folder, $"{c.Name}__{pname}.png"), tex.EncodeToPNG());
                Destroy(tex);

                if (c.Name == "REF_native1920") { reference[pname] = px; if (pname.StartsWith("P1")) refMotionTarget = px; continue; }
                if (!reference.TryGetValue(pname, out var r) || r.Length != px.Length) continue;
                Score(px, r, out double mae, out double psnr, out double edge);
                maeSum += mae; psnrSum += psnr; edgeSum += edge; scored++;
                perPose.Add($"{mae:F1}");
            }

            // MOTION: glide 4 m sideways into P1 over 40 frames, capture on arrival.
            string motion = "-";
            if (refMotionTarget != null && c.Name != "REF_native1920")
            {
                SunCycle.SetHours(golden);
                var p1 = new Vector3(1268f, 7.5f, 1268f);
                var rot = Quaternion.Euler(2f, 35f, 0f);
                var start = p1 - rot * Vector3.right * 4f;
                for (int i = 0; i <= 40; i++)
                {
                    cam.transform.SetPositionAndRotation(Vector3.Lerp(start, p1, i / 40f),
                        Quaternion.Slerp(Quaternion.Euler(2f, 25f, 0f), rot, i / 40f));
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                Color32[] mv = Grab(out Texture2D mt);
                File.WriteAllBytes(Path.Combine(_folder, $"{c.Name}__P1_MOTION.png"), mt.EncodeToPNG());
                Destroy(mt);
                if (mv.Length == refMotionTarget.Length) { Score(mv, refMotionTarget, out double mm, out _, out _); motion = $"{mm:F1}"; }
            }

            // DIAGNOSTIC cost: orbit at golden hour.
            SunCycle.SetHours(golden);
            Vector3 centre = new Vector3(1268f, 7.5f, 1268f);
            double ms = 0; float ang = 0f;
            for (int i = 0; i < 240; i++)
            {
                ang += 0.2f * Time.deltaTime;
                cam.transform.position = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 60f;
                cam.transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)), Vector3.up);
                yield return null;
                if (i >= 30) ms += Time.unscaledDeltaTime * 1000.0;
            }
            ms /= 210.0;

            if (c.Name == "REF_native1920")
                L($"{c.Name,-26}{"(ref)",7}{"",7}{"",7}{"",8}{ms,8:F2}");
            else
                L($"{c.Name,-26}{maeSum / Math.Max(1, scored),7:F2}{psnrSum / Math.Max(1, scored),7:F1}" +
                  $"{edgeSum / Math.Max(1, scored),7:F2}{motion,8}{ms,8:F2}   {string.Join(" ", perPose)}");
        }

        L("");
        L("REF_native1920_pp vs REF_native1920 is the check that turning camera post-processing ON");
        L("(required by URP for every filter but bilinear) does not itself change the picture: its MAE");
        L("should be ~0. If it is not, the default volume is altering the image and every URP row carries it.");
        File.WriteAllText(Path.Combine(_folder, "upscale_ab.txt"), _log.ToString());
        Debug.Log("[upscaleab]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// MAE (0-255), PSNR (dB) and edge-energy ratio of `a` against reference `r`.
    private static void Score(Color32[] a, Color32[] r, out double mae, out double psnr, out double edge)
    {
        int w = Screen.width, h = Screen.height;
        double absSum = 0, sqSum = 0, ea = 0, er = 0;
        int cropBottom = 0;   // no caption bar in this mode
        for (int y = cropBottom; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            int dr = a[i].r - r[i].r, dg = a[i].g - r[i].g, db = a[i].b - r[i].b;
            absSum += Math.Abs(dr) + Math.Abs(dg) + Math.Abs(db);
            sqSum += dr * dr + dg * dg + db * db;
            if (x + 1 < w && y + 1 < h)
            {
                ea += Math.Abs(Luma(a[i + 1]) - Luma(a[i])) + Math.Abs(Luma(a[i + w]) - Luma(a[i]));
                er += Math.Abs(Luma(r[i + 1]) - Luma(r[i])) + Math.Abs(Luma(r[i + w]) - Luma(r[i]));
            }
        }
        double n = 3.0 * w * (h - cropBottom);
        mae = absSum / n;
        double mse = sqSum / n;
        psnr = mse <= 1e-9 ? 99.0 : 10.0 * Math.Log10(255.0 * 255.0 / mse);
        edge = er > 0 ? ea / er : 0;
    }

    private static double Luma(Color32 c) => 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
}
