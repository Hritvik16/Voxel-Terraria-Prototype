// Assets/Game/PlaygroundCapture.LookSweep.cs
//
// -looksweep : OWNER DECISIONS 2-4, SHOWN. The whole day, dawn -> noon -> dusk
// -> night, at three poses, each shot TWICE from the same frame state: A =
// the Brickmap look as it was on 2026-09-24 (lookGrade 0), B = the cinematic
// grade (lookGrade 1). Files are named so a plain sort -- Preview's
// select-all -- runs pose -> time of day -> before/after.
//
// Same conditions as the demo that produced the owner's golden-hour anchor
// (Phase7Demo/2026-09-24_151908/1b_ground_golden_NEW.png): shipped
// LightingConfig defaults, 1920x1080 windowed, shipped gate, rung 0, and the
// anchor's own ground pose. No caption bar, so the pictures are clean.
//
// THE ANCHOR CHECK IS MEASURED, NOT CLAIMED: at every time whose sun is below
// gradeFromDeg the A and B frames are compared pixel by pixel and the count of
// differing pixels is logged. The design says it is 0.
//
// A LOOK, not a measurement: no frame times.
using System;
using System.Collections;
using System.IO;
using UnityEngine;

public partial class PlaygroundCapture
{
    private IEnumerator LookSweep()
    {
        Screen.SetResolution(1920, 1080, false);
        while (Phase4Bootstrapper.Store == null) yield return null;
        _demoCaption = null;
        yield return Frames(300);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "LookSweep", ts);
        Directory.CreateDirectory(_folder);

        LightingConfig.DebugSetActive(new LightingConfig());   // shipped defaults, as the anchor demo
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = 0;
        RaymarchFeature.LookOverride = 1;
        var cam = Camera.main;
        var c = LightingConfig.Active;

        L($"CINEMATIC GRADE DAY SWEEP -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}, " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}");
        L($"A = lookGrade 0 (Brickmap look as of 2026-09-24)   B = lookGrade 1 (cinematic)");
        L($"grade ramp {c.gradeFromDeg}..{c.gradeToDeg} deg   noon exposure {c.gradeNoonExposure}   " +
          $"saturation {c.gradeSaturation}   shoulder knee {c.gradeShoulderKnee}");
        L("");

        // The anchor demo's own poses (PlaygroundCapture.Phase7Demo.cs): the
        // ground pose IS 1b_ground_golden's pose; the crater view is section 2's.
        var poses = new (string tag, Vector3 pos, Vector3 euler)[]
        {
            ("G ground",  new Vector3(1268f, 7.5f, 1268f), new Vector3(2f, 35f, 0f)),
            ("W wide",    new Vector3(1280f, 14f, 1250f),  new Vector3(10f, 0f, 0f)),
            ("H high",    new Vector3(1300f, 24f, 1240f),  new Vector3(26f, -18f, 0f)),
        };
        float golden = SunCycle.GoldenHourHours;
        var times = new (float h, string what)[]
        {
            (SunCycle.HoursForElevation(4.4f, morning: true), "dawn"),
            (7f, "morning"), (8f, "morning"), (10f, "day"), (12f, "noon"),
            (14f, "day"), (16f, "afternoon"), (golden, "golden hour ANCHOR"),
            (17.8f, "dusk"), (19f, "night"), (0f, "midnight"),
        };

        int identicalChecked = 0, identicalFailed = 0;
        foreach (var p in poses)
        {
            cam.transform.position = p.pos;
            cam.transform.rotation = Quaternion.Euler(p.euler);
            yield return Frames(90);
            for (int i = 0; i < times.Length; i++)
            {
                SunCycle.SetHours(times[i].h);
                float el = SunCycle.ElevationDegrees;
                string stem = $"{p.tag} {i + 1:00} {SunCycle.ClockString().Replace(":", "")} " +
                              $"{times[i].what} {el:F0}deg";
                SunCycle.LookGradeOverride = 0;
                yield return Frames(8);
                Color32[] a = null;
                yield return CaptureRaw(stem + " - A before", px => a = px);
                SunCycle.LookGradeOverride = 1;
                yield return Frames(8);
                Color32[] b = null;
                yield return CaptureRaw(stem + " - B after", px => b = px);

                int diff = 0, maxd = 0;
                for (int j = 0; j < a.Length && j < b.Length; j++)
                {
                    int d = Math.Max(Math.Abs(a[j].r - b[j].r), Math.Max(Math.Abs(a[j].g - b[j].g), Math.Abs(a[j].b - b[j].b)));
                    if (d > 0) { diff++; if (d > maxd) maxd = d; }
                }
                bool belowRamp = el <= c.gradeFromDeg;
                string verdict = "";
                if (belowRamp)
                {
                    identicalChecked++;
                    if (diff != 0) identicalFailed++;
                    verdict = diff == 0 ? "   IDENTICAL (below the ramp, as designed)" : "   <-- SHOULD BE IDENTICAL";
                }
                L($"  {stem,-44} grade {SunCycle.LookGradeAmountAt(times[i].h):F2}   " +
                  $"differing px {diff,8} ({100.0 * diff / Math.Max(1, a.Length):F1}%)  max {maxd}{verdict}");
            }
            L("");
        }
        SunCycle.LookGradeOverride = -1;
        L($"ANCHOR CHECK: {identicalChecked - identicalFailed} of {identicalChecked} below-ramp pairs pixel-identical");

        File.WriteAllText(Path.Combine(_folder, "looksweep.txt"), _log.ToString());
        Debug.Log("[looksweep]\n" + _log);
        Application.Quit(0);
    }

    private IEnumerator CaptureRaw(string name, Action<Color32[]> sink)
    {
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            File.WriteAllBytes(Path.Combine(_folder, name + ".png"), tex.EncodeToPNG());
            sink(tex.GetPixels32());
        }
        finally { Destroy(tex); }
    }
}
