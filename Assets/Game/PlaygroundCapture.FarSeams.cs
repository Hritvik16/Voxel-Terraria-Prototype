// Assets/Game/PlaygroundCapture.FarSeams.cs
//
// -farseams : THE OWNER'S "SEAMS AND VISUAL BUGS WHEN FLYING HIGH", PHOTOGRAPHED.
// Requires -farband 1. At 150, 300 and 1000 m it flies a straight line at
// 60 m/s (the demo's Shift speed) and photographs, at the same pose:
//   A  beauty
//   C  band view (debug mode 9): which band drew each pixel
//   B  far band OFF (voxel band alone)      F  LOD tier view (mode 6)
//   G  LOD cascade OFF (tier 0 only)
//   E  voxel-stop view (debug mode 11): why the VOXEL march ended -- blue hit;
//      red = cut short by its distance/iteration cap over a RESIDENT column
//      and the far march then drew nothing there; magenta = cut short and the
//      far march drew terrain from further along the ray; grey = left the
//      window or the content ceiling (legitimate hand-off); green = far hit.
// once SETTLED (ring and streamer idle) and three times IN FLIGHT (motion held
// only for the frames the three shots take; each shot records whether far
// strips or chunk loads were still pending). An artifact present in flight
// but absent settled is a staleness bug; one present in both is geometry.
//
// Diagnostic frames only: no timing here is a measurement.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;
using VoxelEngine.WorldGen;

public partial class PlaygroundCapture
{
    private IEnumerator FarSeams()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        for (int i = 0; i < 300; i++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + "_seams");
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        int gate = ArgInt("-gateres", 960);
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        Playground.DebugHideUi = true;

        var far = FarField.Active;
        var cam = Camera.main;
        var streamer = Phase4Bootstrapper.Streamer;
        L($"FAR BAND SEAMS AT ALTITUDE -- {ts}");
        L($"{Screen.width}x{Screen.height} {(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")} gate {gate}, golden hour pinned");
        if (far == null)
        {
            L("FAR BAND NOT BUILT -- run with -farband 1.");
            File.WriteAllText(Path.Combine(_folder, "farseams.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        L("A beauty | C band (blue voxel, F0 green .. F4 magenta) | E voxel-stop (red = voxel march capped over resident");
        L("terrain AND far missed; magenta = capped, far drew terrain further on; grey = legit hand-off; green far hit)");
        L("");
        L($"{"shot",-26}{"eye",-26}{"pitch",6}{"farBusy",9}{"pending",9}");

        var st = ColumnSampler.CreateState(Phase4Bootstrapper.Meta);
        try
        {
            float cx = st.centerX * 0.1f, cz = st.centerZ * 0.1f;
            var flights = new (float h, float pitch)[] { (150f, 15f), (300f, 20f), (1000f, 50f) };
            foreach (var (h, pitch) in flights)
            {
                Vector3 p = new Vector3(cx - 600f, h, cz - 150f);
                cam.transform.position = p;
                cam.transform.rotation = Quaternion.Euler(pitch, 90f, 0f);
                yield return Frames(120);
                for (int w = 0; w < 1800 && far.Busy; w++) yield return null;
                for (int w = 0; w < 1800 && streamer != null && streamer.PendingLoads > 0; w++) yield return null;
                yield return Frames(20);
                yield return SeamTriple($"h{h:F0}_0_settled", pitch, far, streamer);

                for (int leg = 1; leg <= 3; leg++)
                {
                    // 4 s at 60 m/s between shots: 240 m, several F0 strips.
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 4f)
                    {
                        cam.transform.position += new Vector3(60f * Time.unscaledDeltaTime, 0f, 0f);
                        yield return null;
                    }
                    yield return SeamTriple($"h{h:F0}_{leg}_inflight", pitch, far, streamer);
                }
                // FLICKER PAIR: beauty on consecutive frames while moving. A
                // stable surface changes little between them; sub-pixel point-
                // sampled texels sparkle. tools/farseams-sheet.py reports the
                // mean absolute difference.
                for (int f = 0; f < 2; f++)
                {
                    cam.transform.position += new Vector3(60f * Time.unscaledDeltaTime, 0f, 0f);
                    yield return CaptureTo($"h{h:F0}_flicker{f}");
                }
            }
            // The map view, straight down from 1 km, settled.
            cam.transform.position = new Vector3(cx, 1000f, cz);
            cam.transform.rotation = Quaternion.Euler(89.5f, 0f, 0f);
            yield return Frames(120);
            for (int w = 0; w < 1800 && far.Busy; w++) yield return null;
            for (int w = 0; w < 1800 && streamer != null && streamer.PendingLoads > 0; w++) yield return null;
            yield return Frames(20);
            yield return SeamTriple("h1000_down_settled", 89.5f, far, streamer);
        }
        finally { st.Dispose(); }

        File.WriteAllText(Path.Combine(_folder, "farseams.txt"), _log.ToString());
        Debug.Log("[farseams]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    private IEnumerator SeamTriple(string name, float pitch, FarField far, VoxelEngine.Streaming.StreamManager streamer)
    {
        bool busy = far.Busy;
        int pending = streamer != null ? streamer.PendingLoads : -1;
        yield return CaptureTo($"{name}_A");
        RaymarchFeature.UseDebugViewOverride = true;
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.FarBand;
        yield return null;
        yield return CaptureTo($"{name}_C_band");
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.VoxelStop;
        yield return null;
        yield return CaptureTo($"{name}_E_stop");
        RaymarchFeature.DebugViewOverride = RaymarchFeature.DebugMode.LODTier;
        yield return null;
        yield return CaptureTo($"{name}_F_tier");
        RaymarchFeature.UseDebugViewOverride = false;
        // Which band is WRONG where they disagree: FarBandSeamOracleTests proved
        // the far ring equals the generator column for column, so the far band
        // is the generator's truth. B = voxel band alone (far off); G = the same
        // frame with the LOD cascade off (tier 0 only, which is uploaded per
        // chunk rather than downsampled on a budget).
        FarField.RenderEnabled = false;
        yield return null;
        yield return CaptureTo($"{name}_B_faroff");
        FarField.RenderEnabled = true;
        RaymarchFeature.UseLODCascade = false;
        yield return null;
        yield return CaptureTo($"{name}_G_nocascade");
        RaymarchFeature.UseLODCascade = true;
        yield return null;
        // Pixel fractions (far, capped) are counted from the C and E images by
        // tools/farseams-sheet.py, exactly, rather than from racy counters.
        L($"{name,-26}{cam3(Camera.main.transform.position),-26}{pitch,6:F0}{(busy ? "yes" : "no"),9}{pending,9}");
    }

    private static string cam3(Vector3 v) => $"({v.x:F0},{v.y:F0},{v.z:F0})";
}
