// Assets/Game/PlaygroundCapture.FarBand.cs
//
// -farvisual : THE FAR BAND, SHOWN. DESIGN_LARGE_WORLD_AND_TERRAIN_OVERHAUL.md
// §7 items 1-2 and §2.3, Step 1's visual half.
//
// Requires -farband 1 (the ring is built at startup by Phase4Bootstrapper). At
// each pose x time of day it renders the SAME frame with the far band OFF then
// ON (FarField.RenderEnabled -- one process, one ring, only the march gated),
// plus the band view (which band drew each pixel) and the far step heat. It
// also verifies every pose's camera is in AIR before shooting: a camera inside
// rock "measures" a black frame (CLAUDE.md known issue, found three times).
//
// Frame times here are DIAGNOSTIC, back-to-back in one process -- NOT a budget
// figure. The FPS claim comes from -sustained arms, counterbalanced, in their
// own processes (run-farband.sh).
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
    private IEnumerator FarVisual()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(300);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts);
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = ArgInt("-shadowlevel", 0);
        int gate = ArgInt("-gateres", 960);
        if (gate > 0)
        {
            RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
            RaymarchFeature.CustomGateResolution = new Vector2Int(gate, gate * 9 / 16);
        }
        Playground.DebugHideUi = true;

        var far = FarField.Active;
        var cam = Camera.main;
        float fov0 = cam.fieldOfView;

        L($"FAR BAND VISUAL A/B -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
          $"{(Screen.fullScreen ? "FULLSCREEN" : "WINDOWED")}  {(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}  " +
          $"gate {gate}  shadow rung {RaymarchFeature.ShadowQualityLevel}");
        if (far == null)
        {
            L("FAR BAND NOT BUILT -- run with -farband 1. Nothing to compare.");
            File.WriteAllText(Path.Combine(_folder, "farvisual.txt"), _log.ToString());
            Application.Quit(2);
            yield break;
        }
        L($"far ring: {FarField.LEVELS} levels x {FarField.EDGE}^2, initial fill {far.InitialFillMs:F0} ms, " +
          $"max top {far.MaxHeightVoxels} vox, GPU {far.GpuBytes / 1048576f:F1} MB, far shadows {(far.ShadowsOn ? "ON" : "OFF")}, " +
          $"haze range {far.HazeRangeMetres:F0} m, step caps {far.PrimaryStepCap}/{far.ShadowStepCap}");
        L("Per pose: OFF = shipped frame (sky past the window). ON = far band. band = which band drew each");
        L("pixel (blue voxel window; F0 green, F1 yellow, F2 orange, F3 red, F4 magenta). heat = far steps.");
        L("ms columns are DIAGNOSTIC (back-to-back, one process), not a budget figure.");
        L("");
        L($"{"pose",-24}{"time",7}{"sun",7}{"air?",6}{"ms off",8}{"ms on",8}{"far px%",9}{"far hit%",10}{"steps/ray",11}");

        var st = ColumnSampler.CreateState(Phase4Bootstrapper.Meta);
        try
        {
            Vector3 c = new Vector3(1268f, 0f, 1268f);
            float g = GroundMetres(in st, c.x, c.z);
            float golden = SunCycle.GoldenHourHours;
            float dawn = SunCycle.HoursForElevation(6f, morning: true);
            var times = new (string name, float h)[] { ("dawn", dawn), ("noon", 12f), ("golden", golden), ("night", 22f) };

            var poses = new List<(string name, Vector3 pos, Vector3 euler, float fov, bool allTimes)>
            {
                ("ground_seam",     new Vector3(c.x, g + 2.0f, c.z),   new Vector3(1.5f, 35f, 0f),  fov0, true),
                ("seam_zoom_20m",   new Vector3(c.x, g + 20f, c.z),    new Vector3(4.2f, 35f, 0f),  14f,  true),
                ("ground_south",    new Vector3(c.x, g + 2.0f, c.z),   new Vector3(1.5f, 200f, 0f), fov0, false),
                ("hill_150m",       new Vector3(c.x, 150f, c.z),        new Vector3(12f, 35f, 0f),   fov0, true),
                ("high_300m",       new Vector3(c.x, 300f, c.z),        new Vector3(15f, 35f, 0f),   fov0, true),
                ("map_1000m",       new Vector3(c.x, 1000f, c.z),       new Vector3(89.5f, 0f, 0f),  fov0, false),
                ("map_3000m",       new Vector3(c.x, 3000f, c.z),       new Vector3(89.5f, 0f, 0f),  fov0, false),
                ("coast_from_sea",  new Vector3(c.x, 30f, c.z - 1250f), new Vector3(3f, 0f, 0f),     fov0, false),
            };

            foreach (var (pname, pos, euler, fov, allTimes) in poses)
            {
                foreach (var (tname, hours) in times)
                {
                    if (!allTimes && tname != "golden") continue;
                    SunCycle.SetHours(hours);
                    cam.fieldOfView = fov;
                    SetPose(cam, pos, euler);
                    yield return Frames(90);   // streaming and far strips settle
                    for (int w = 0; w < 1200 && far.Busy; w++) yield return null;
                    yield return Frames(10);

                    bool air = CameraInAir(in st, cam.transform.position);
                    string tag = $"{pname}_{tname}";

                    FarField.RenderEnabled = false;
                    double msOff = 0; yield return Frames(8);
                    for (int i = 0; i < 60; i++) { yield return null; msOff += Time.unscaledDeltaTime * 1000.0; }
                    msOff /= 60.0;
                    yield return CaptureTo($"{tag}_A_off");

                    FarField.RenderEnabled = true;
                    double msOn = 0; yield return Frames(8);
                    for (int i = 0; i < 60; i++) { yield return null; msOn += Time.unscaledDeltaTime * 1000.0; }
                    msOn /= 60.0;
                    RaymarchFeature.CollectShadowStats = true;
                    RaymarchFeature.ResetShadowStats();
                    yield return Frames(10);
                    uint[] s = RaymarchFeature.ReadShadowStats();
                    RaymarchFeature.CollectShadowStats = false;
                    yield return CaptureTo($"{tag}_B_on");

                    if (tname == "golden" || pname == "ground_seam")
                    {
                        RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)9;
                        RaymarchFeature.UseDebugViewOverride = true;
                        yield return Frames(6);
                        yield return CaptureTo($"{tag}_C_band");
                        RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)10;
                        yield return Frames(6);
                        yield return CaptureTo($"{tag}_D_heat");
                        RaymarchFeature.UseDebugViewOverride = false;
                    }

                    double pix = (double)RaymarchFeature.LastDispatchResolution.x * RaymarchFeature.LastDispatchResolution.y * 10.0;
                    double farRays = s.Length > 7 ? s[5] : 0, farSteps = s.Length > 7 ? s[6] : 0, farHits = s.Length > 7 ? s[7] : 0;
                    L($"{pname,-24}{tname,7}{SunCycle.ElevationDegrees,6:F1}°{(air ? "yes" : "NO!"),6}{msOff,8:F2}{msOn,8:F2}" +
                      $"{100.0 * farRays / Math.Max(1.0, pix),9:F1}{100.0 * farHits / Math.Max(1.0, farRays),10:F1}" +
                      $"{farSteps / Math.Max(1.0, farRays),11:F1}");
                }
            }
            cam.fieldOfView = fov0;

            L("");
            L($"far ring upkeep over the run: {far.StripsApplied} strips, {far.FullRefills} full refills, " +
              $"{far.ColumnsSampled:N0} columns sampled, {far.BytesUploaded / 1048576.0:F1} MB uploaded, last strip job {far.LastStripJobMs:F1} ms");
        }
        finally { st.Dispose(); }

        File.WriteAllText(Path.Combine(_folder, "farvisual.txt"), _log.ToString());
        Debug.Log("[farvisual]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    // =====================================================================
    // -farpreview (with -farband 1 -farsource tg2): the Step 2 generator's
    // sizeClass 2 island, drawn by the far band alone. A LOOK at the new world
    // at scale -- 600 m peak, 6 km island, 6.5 km horizon -- not a measurement.
    // Far band only means 0.8 m blocks at the closest range; the voxel detail
    // field D is not in these frames.
    // =====================================================================
    private IEnumerator FarPreview()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(200);
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "FarBand", ts + "_preview");
        Directory.CreateDirectory(_folder);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        RaymarchFeature.ShadowQualityLevel = 0;
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(ArgInt("-gateres", 960), ArgInt("-gateres", 960) * 9 / 16);
        Playground.DebugHideUi = true;
        var far = FarField.Active;
        var cam = Camera.main;
        if (far == null || !far.FarOnly || far.Tg2Map == null)
        {
            L("PREVIEW NEEDS -farband 1 -farsource tg2");
            File.WriteAllText(Path.Combine(_folder, "farpreview.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        var spec = far.Tg2Spec; var map = far.Tg2Map; var o = TerrainGen.Offsets.For(spec.seed);
        int2 off = far.Tg2OffsetVoxels;
        int seaShift = spec.seaLevelY - WorldGenConstants.SEA_LEVEL_VOXEL_Y;
        // tg2 voxel (x, z) -> engine metres; tg2 absolute Y -> engine metres.
        Vector3 E(float gx, float gy, float gz) => new Vector3((gx - off.x) * 0.1f, (gy - seaShift) * 0.1f, (gz - off.y) * 0.1f);
        float Ground(float gx, float gz) => TgGround(map, gx, gz);

        L($"SIZECLASS 2 PREVIEW (TerrainGen via the far band) -- {ts}");
        L($"seed {spec.seed}: world {spec.worldSizeVoxels / 10000f:F0} km, sea at Y {spec.seaLevelY}, peak site {map.PeakPosition}, " +
          $"max top {far.MaxHeightVoxels} (engine voxels), ring initial fill {far.InitialFillMs:F0} ms");
        float c = spec.worldSizeVoxels * 0.5f;
        float2 pk = map.PeakPosition;
        float pkTop = Ground(pk.x, pk.y);
        float2 toC = math.normalizesafe(new float2(c, c) - pk, new float2(1, 0));
        float2 from2k = pk + toC * 20000f;
        var poses = new List<(string, Vector3, Quaternion)>();
        void Look(string n, Vector3 eye, Vector3 at) => poses.Add((n, eye, Quaternion.LookRotation((at - eye).normalized, Vector3.up)));
        Look("peak_from_2km_ground", E(from2k.x, Ground(from2k.x, from2k.y) + 25f, from2k.y), E(pk.x, pkTop - 400f, pk.y));
        Look("summit_view", E(pk.x - toC.x * 300f, pkTop + 250f, pk.y - toC.y * 300f), E(pk.x + toC.x * 40000f, spec.seaLevelY, pk.y + toC.y * 40000f));
        Look("glide_400m", E(c - 16000f, spec.seaLevelY + 4000f, c - 16000f), E(pk.x, pkTop - 800f, pk.y));
        Look("coast_from_sea_30m", E(c, spec.seaLevelY + 300f, c - 36000f), E(c, spec.seaLevelY + 800f, c));
        Look("oblique_3000m", E(c, spec.seaLevelY + 30000f, c - 22000f), E(c, spec.seaLevelY, c + 6000f));
        poses.Add(("map_7000m", E(c, spec.seaLevelY + 70000f, c), Quaternion.Euler(89.5f, 0f, 0f)));

        foreach (var (n, pos, rot) in poses)
        {
            foreach (float hours in new[] { SunCycle.GoldenHourHours, 12f })
            {
                SunCycle.SetHours(hours);
                cam.transform.position = pos; cam.transform.rotation = rot;
                yield return Frames(30);
                for (int w = 0; w < 1500 && far.Busy; w++) yield return null;   // ring refills after the jump
                yield return Frames(30);
                double ms = 0; for (int i = 0; i < 60; i++) { yield return null; ms += Time.unscaledDeltaTime * 1000.0; }
                string tn = hours == 12f ? "noon" : "golden";
                yield return CaptureTo($"{n}_{tn}");
                if (hours != 12f)
                {
                    RaymarchFeature.DebugViewOverride = (RaymarchFeature.DebugMode)9;
                    RaymarchFeature.UseDebugViewOverride = true;
                    yield return Frames(6);
                    yield return CaptureTo($"{n}_{tn}_band");
                    RaymarchFeature.UseDebugViewOverride = false;
                }
                L($"{n,-24}{tn,7}  eye {pos}  {ms / 60.0:F2} ms (diagnostic)");
            }
        }
        L($"ring upkeep: {far.StripsApplied} strips, {far.FullRefills} refills, {far.BytesUploaded / 1048576.0:F1} MB uploaded");
        File.WriteAllText(Path.Combine(_folder, "farpreview.txt"), _log.ToString());
        Debug.Log("[farpreview]\n" + _log);
        yield return null;
        Application.Quit(0);
    }

    /// Top-face Y (tg2 absolute voxels) of the sizeClass 2 column, or sea level.
    /// Not an iterator, so it may hold the map's pointer view.
    private static float TgGround(TerrainGen.WorldMap map, float gx, float gz)
    {
        var spec = map.Spec;
        var view = map.View;
        var o = TerrainGen.Offsets.For(spec.seed);
        return math.max(TerrainGen.ShapeField.Sample(in spec, in view, in o, gx, gz, 8).height + 1, spec.seaLevelY + 1);
    }

    private IEnumerator CaptureTo(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D t = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(_folder, name + ".png"), t.EncodeToPNG()); }
        finally { Destroy(t); }
    }

    /// Top of the generated column in METRES (first air above it), or sea level.
    private static float GroundMetres(in ColumnSampler.State st, float xM, float zM)
    {
        ColumnSampler.SampleColumn(in st, (int)math.floor(xM * 10f), (int)math.floor(zM * 10f), out int h, out _);
        return math.max(h + 1, WorldGenConstants.SEA_LEVEL_VOXEL_Y + 1) * 0.1f;
    }

    /// True if the camera's voxel is above the generated column there. Cheap and
    /// generator-based (caves aside, which only ever ADD air).
    private static bool CameraInAir(in ColumnSampler.State st, Vector3 p)
    {
        ColumnSampler.SampleColumn(in st, (int)math.floor(p.x * 10f), (int)math.floor(p.z * 10f), out int h, out _);
        return p.y * 10f > h + 1;
    }
}
