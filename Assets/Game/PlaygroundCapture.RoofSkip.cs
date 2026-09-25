// Assets/Game/PlaygroundCapture.RoofSkip.cs
//
// -roofskipab : THE SHADOW ROOF SKIP, VISUAL + COUNTER A/B.
//
// The roof skip (Raymarch.compute's HeightMip block) claims to be EXACT: it
// only skips space a climbing sun ray provably cannot hit, so it should change
// cost and not a single pixel. This mode tests that claim instead of trusting
// it: at each pose it renders the same frame with the skip OFF then ON and
// compares the beauty image and the sun-visibility view pixel by pixel, and
// reads the shadow-ray step counters for both.
//
// Frame time is NOT concluded here -- a few hundred frames per pose is a
// diagnostic. The FPS claim comes from `-sustained` runs, counterbalanced.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

public partial class PlaygroundCapture
{
    private IEnumerator RoofSkipAb()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "RoofSkipAB", ts);
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        Playground.DebugHideUi = true;

        var store = Phase4Bootstrapper.Store;
        var cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 peak = DeepestNear(store, camVox, 240);
        int surf = SurfaceYAt(store, peak.x, peak.z);
        int3 top = new int3(peak.x, surf, peak.z);

        L($"SHADOW ROOF SKIP A/B -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
          $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  {(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  " +
          $"shadow rung {RaymarchFeature.ShadowQualityLevel}");
        L("Per pose: same frame rendered with the skip OFF then ON. 'differing px' counts pixels whose");
        L("colour differs by more than 1/255 in any channel. The claim under test is ZERO.");
        L("");
        L($"{"pose",-26}{"time",7}{"sun",7}{"beauty px",11}{"sunvis px",11}{"steps/ray off",15}{"on",8}{"ms off",9}{"ms on",8}");

        float golden = SunCycle.GoldenHourHours;
        float low = SunCycle.HoursForElevation(2.9f, morning: false);
        var poses = new List<(string name, float hours, Action pose)>
        {
            ("ground_golden",  golden, () => SetPose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(2f, 35f, 0f))),
            ("ground_sun2.9",  low,    () => SetPose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(2f, 35f, 0f))),
            ("ground_noon",    12f,    () => SetPose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(2f, 35f, 0f))),
            ("wide_golden",    golden, () => SetPose(cam, new Vector3(1280f, 14f, 1250f), new Vector3(10f, 0f, 0f))),
            ("high_golden",    golden, () => SetPose(cam, new Vector3(1300f, 24f, 1240f), new Vector3(26f, -18f, 0f))),
            ("toward_sun",     golden, () => SetPose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(4f, 90f, 0f))),
            ("away_from_sun",  golden, () => SetPose(cam, new Vector3(1268f, 7.5f, 1268f), new Vector3(4f, 270f, 0f))),
            ("shadow_edge",    golden, () => LookAtVox(cam, top + new int3(-14, 8, -14), top)),
            ("peak_dawn",      SunCycle.HoursForElevation(4f, morning: true),
                                       () => LookAtVox(cam, top + new int3(-35, 30, -35), top)),
        };

        long totalBeauty = 0, totalSunvis = 0;
        double sumStepsOff = 0, sumStepsOn = 0, sumMsOff = 0, sumMsOn = 0;
        foreach (var (name, hours, pose) in poses)
        {
            SunCycle.SetHours(hours);
            pose();
            yield return Frames(60);

            var r = new Dictionary<bool, (Color32[] beauty, Color32[] sunvis, double steps, double ms)>();
            foreach (bool on in new[] { false, true })
            {
                RaymarchFeature.ShadowRoofSkip = on;
                RaymarchFeature.UseDebugViewOverride = false;
                yield return Frames(8);
                double ms = 0; for (int i = 0; i < 120; i++) { yield return null; ms += Time.unscaledDeltaTime * 1000.0; }
                ms /= 120.0;

                RaymarchFeature.CollectShadowStats = true;
                RaymarchFeature.ResetShadowStats();
                yield return Frames(30);
                uint[] st = RaymarchFeature.ReadShadowStats();
                RaymarchFeature.CollectShadowStats = false;
                double steps = (st != null && st.Length >= 5 && st[2] > 0) ? st[4] / (double)st[2] : 0;

                yield return new WaitForEndOfFrame();
                Color32[] beauty = Grab(out Texture2D bt);
                File.WriteAllBytes(Path.Combine(_folder, $"{name}_{(on ? "ON" : "OFF")}.png"), bt.EncodeToPNG());
                Destroy(bt);

                RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.SunVis;
                RaymarchFeature.UseDebugViewOverride = true;
                yield return Frames(6);
                yield return new WaitForEndOfFrame();
                Color32[] sunvis = Grab(out Texture2D sv);
                Destroy(sv);
                RaymarchFeature.UseDebugViewOverride = false;

                r[on] = (beauty, sunvis, steps, ms);
            }

            int db = CountDiff(r[false].beauty, r[true].beauty, name);
            int ds = CountDiff(r[false].sunvis, r[true].sunvis, null);
            totalBeauty += db; totalSunvis += ds;
            sumStepsOff += r[false].steps; sumStepsOn += r[true].steps;
            sumMsOff += r[false].ms; sumMsOn += r[true].ms;
            L($"{name,-26}{SunCycle.ClockString(),7}{SunCycle.ElevationDegrees,6:F1}°{db,11}{ds,11}" +
              $"{r[false].steps,15:F1}{r[true].steps,8:F1}{r[false].ms,9:F2}{r[true].ms,8:F2}");
        }

        int n = poses.Count;
        L("");
        L($"TOTAL differing pixels: beauty {totalBeauty}, sun visibility {totalSunvis}  (claim: 0 and 0)");
        L($"MEAN shadow steps per traced ray: off {sumStepsOff / n:F1}  on {sumStepsOn / n:F1}  " +
          $"({100.0 * (sumStepsOn - sumStepsOff) / Math.Max(1e-9, sumStepsOff):+0.0;-0.0}%)");
        L($"MEAN ms/frame over these poses (DIAGNOSTIC, back-to-back, not a budget figure): " +
          $"off {sumMsOff / n:F2}  on {sumMsOn / n:F2}");
        L("Any pose with differing pixels has <pose>_DIFF.png: magenta where the two frames differ.");

        File.WriteAllText(Path.Combine(_folder, "roofskip_ab.txt"), _log.ToString());
        Debug.Log("[roofskipab]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    private static void SetPose(Camera cam, Vector3 pos, Vector3 euler)
    {
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Euler(euler);
    }

    private static void LookAtVox(Camera cam, int3 eyeVox, int3 lookVox)
    {
        var eye = new Vector3(eyeVox.x * 0.1f, eyeVox.y * 0.1f, eyeVox.z * 0.1f);
        var at = new Vector3(lookVox.x * 0.1f, lookVox.y * 0.1f, lookVox.z * 0.1f);
        cam.transform.position = eye;
        cam.transform.rotation = Quaternion.LookRotation((at - eye).normalized, Vector3.up);
    }

    private static Color32[] Grab(out Texture2D tex)
    {
        tex = ScreenCapture.CaptureScreenshotAsTexture();
        return tex.GetPixels32();
    }

    /// Pixels differing by more than 1/255 in any channel. With a name, also
    /// writes <name>_DIFF.png (magenta = differs) when anything differs.
    private int CountDiff(Color32[] a, Color32[] b, string name)
    {
        if (a.Length != b.Length) return -1;
        int n = 0;
        var mask = name != null ? new Color32[a.Length] : null;
        for (int i = 0; i < a.Length; i++)
        {
            bool d = Math.Abs(a[i].r - b[i].r) > 1 || Math.Abs(a[i].g - b[i].g) > 1 || Math.Abs(a[i].b - b[i].b) > 1;
            if (d) n++;
            if (mask != null) mask[i] = d ? new Color32(255, 0, 255, 255) : new Color32((byte)(a[i].r / 3), (byte)(a[i].g / 3), (byte)(a[i].b / 3), 255);
        }
        if (n > 0 && mask != null)
        {
            var t = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            if (t.width * t.height == mask.Length)
            {
                t.SetPixels32(mask); t.Apply();
                File.WriteAllBytes(Path.Combine(_folder, name + "_DIFF.png"), t.EncodeToPNG());
            }
            Destroy(t);
        }
        return n;
    }
}
