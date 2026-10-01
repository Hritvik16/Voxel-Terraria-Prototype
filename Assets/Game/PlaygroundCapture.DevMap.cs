// Assets/Game/PlaygroundCapture.DevMap.cs
//
// -devmap : photographs the dev map (DevMap.cs) the way the owner will see it,
// overlays ON, so the page reporting it shows the real thing. Views: the whole
// island oblique and top-down, each major landmark close, and the underground
// slice at five depths (shallow, the cavern layer, deep caverns, the
// underworld's lava sea), then the LOCAL slice (2.5 m per pixel) at three. Logs the slice's preparation and per-depth times and
// the marker counts.
//
// Run it with -farband 1 -farsource tg2 for the new island (run-devmap.sh), or
// -farband 1 alone for the legacy one. Frame times are not recorded: this is a
// dev view, not a budgeted one.
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using VoxelEngine.Rendering;

public partial class PlaygroundCapture
{
    private IEnumerator DevMapShots()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        yield return Frames(200);
        for (int w = 0; w < 1200 && DevMap.Instance == null; w++) yield return null;

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "DevMap", ts);
        Directory.CreateDirectory(_folder);
        var dm = DevMap.Instance;
        if (dm == null)
        {
            L("NO DEV MAP (Playground never finished Start -- no basin found?)");
            File.WriteAllText(Path.Combine(_folder, "devmap.txt"), _log.ToString());
            Application.Quit(2); yield break;
        }
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        dm.OpenMap();
        var far = FarField.Active;
        L($"DEV MAP -- {ts}");
        L($"world {(dm.IsTg2 ? "sizeClass 2 (TerrainGen)" : "legacy")}, far band {(far != null ? "built" : "NOT BUILT")}, {dm.MarkerCount} markers");

        Vector3 c = dm.IslandCentre;
        float R = dm.IslandRadiusM;
        IEnumerator Shot(string name, Vector3 target, float yaw, float pitch, float dist, float hours)
        {
            SunCycle.SetHours(hours);
            dm.SetView(target, yaw, pitch, dist);
            yield return Frames(20);
            for (int w = 0; w < 1500 && far != null && far.Busy; w++) yield return null;
            yield return Frames(20);
            yield return CaptureTo(name);
            L($"shot {name}");
        }

        yield return Shot("01_island_oblique_golden", c, 30f, 42f, R * 1.9f, SunCycle.GoldenHourHours);
        yield return Shot("02_island_topdown_noon", c, 0f, 89.5f, R * 2.3f, 12f);
        int i = 0;
        foreach (var m in dm.Markers)
        {
            if (!m.major) continue;
            string safe = m.name.Split(' ')[0].ToLowerInvariant();
            yield return Shot($"{3 + i:00}_{safe}", m.pos, 30f + 70f * i, 35f, 900f, SunCycle.GoldenHourHours);
            i++;
        }

        if (dm.IsTg2)
        {
            // Wait for the column preparation (one-off), then each depth.
            float t0 = Time.realtimeSinceStartup;
            while (dm.SlicePrepFraction < 1f && Time.realtimeSinceStartup - t0 < 180f) yield return null;
            L($"slice: {DevMap.SLICE_N}^2 columns prepared in {dm.SlicePrepMs:F0} ms (worker thread), {dm.SliceVoxelsPerPixel * 0.1f:F0} m per pixel");
            var spec = FarField.Active.Tg2Spec;
            var depths = new (string, int)[]
            {
                ("minus20m", spec.seaLevelY - 200), ("minus100m", spec.seaLevelY - 1000),
                ("minus200m_caverns", spec.seaLevelY - 2000), ("minus330m_deep", spec.seaLevelY - 3300),
                ("minus440m_underworld", spec.seaLevelY - 4400),
            };
            int k = 0;
            foreach (var (name, y) in depths)
            {
                dm.SetSliceY(y);
                float t1 = Time.realtimeSinceStartup;
                while (!dm.SliceShowing(y) && Time.realtimeSinceStartup - t1 < 60f) yield return null;
                L($"slice {name}: Y {y} ({(y - spec.seaLevelY) * 0.1f:F0} m) rendered in {dm.SliceLastRenderMs:F0} ms, shown={dm.SliceShowing(y)}");
                yield return Shot($"{20 + k:00}_slice_{name}", c, 30f, 42f, R * 1.9f, SunCycle.GoldenHourHours);
                k++;
            }

            // The LOCAL slice (Z): 800 m around the target at 2.5 m per pixel,
            // where tunnels and caverns resolve instead of speckling.
            dm.SetView(c, 30f, 42f, 1600f);
            foreach (var (name, y) in new[] { ("minus60m", spec.seaLevelY - 600), ("minus200m_caverns", spec.seaLevelY - 2000), ("minus440m_underworld", spec.seaLevelY - 4400) })
            {
                dm.SetSliceY(y);
                if (!dm.SliceLocal) dm.StartSlice(true);
                float t2 = Time.realtimeSinceStartup;
                while (dm.SlicePrepFraction < 1f && Time.realtimeSinceStartup - t2 < 120f) yield return null;
                while (!dm.SliceShowing(y) && Time.realtimeSinceStartup - t2 < 180f) yield return null;
                L($"local slice {name}: prepared in {dm.SlicePrepMs:F0} ms, {dm.SliceVoxelsPerPixel * 0.1f:F1} m per pixel, shown={dm.SliceShowing(y)}");
                yield return Shot($"{30 + k:00}_local_slice_{name}", c, 30f, 42f, 1600f, SunCycle.GoldenHourHours);
                k++;
            }
        }
        dm.CloseMap();
        L($"folder {_folder}");
        File.WriteAllText(Path.Combine(_folder, "devmap.txt"), _log.ToString());
        Debug.Log("[devmap]\n" + _log);
        yield return null;
        Application.Quit(0);
    }
}
