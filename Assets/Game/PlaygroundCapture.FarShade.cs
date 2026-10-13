// Assets/Game/PlaygroundCapture.FarShade.cs
//
// -farpreview -farshadeshots : TASK 85b (BUGS #9, owner Q48 (a)). Far shading
// (RaymarchFeature.FarShade, Raymarch.compute `_FarShade`) OFF / ON, in ONE launch.
//
// DAY: every kept pose (-poses) twice -- "lit" (sun behind the camera, 25-60 deg
// up) and "backlit" (sun ahead; eye_level_* at noon, as the owner's frames) --
// each raw and through FSR 3, both at -shaderows internal rows (720):
//   <pose>_<lit|backlit>_<raw|fsr3>_<A_off|B_on>.png
// NIGHT (-nightposes, default eye_level_3 + peak_from_2km_ground, at -nighttod
// 0.5 h): torches on the ground 12-28 m (near) and 60-300 m (far, where resident)
// ahead, and a 5 m lava pool 45 m ahead, then the same four shots:
//   <pose>_night_<raw|fsr3>_<A_off|B_on>.png
// Waits are short and fixed (-posewait, 45 s): what is still loading is logged.
// The sheets: tools/farshade85b-sheet.py.

using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;

public partial class PlaygroundCapture
{
    private IEnumerator FarShadeShots(List<(string, Vector3, Quaternion)> poses, Camera cam, FarField far, bool world)
    {
        float wait = ArgFloat("-posewait", 45f);
        int rows = ArgInt("-shaderows", 720);
        var nightKeep = new HashSet<string>((ArgValue("-nightposes") ?? "eye_level_3,peak_from_2km_ground").Split(','));
        float nightHours = ArgFloat("-nighttod", 0.5f);
        bool fs0 = RaymarchFeature.FarShade;
        L($"FAR SHADING (task 85b, -farshadeshots): {poses.Count} poses, wait <= {wait:F0} s each, {rows} internal rows raw and FSR 3; " +
          $"sky x{RaymarchFeature.FarShadeSkyStrength}, haze range {RaymarchFeature.FarShadeHazeRangeM} m, scale height {RaymarchFeature.FarShadeHazeScaleHeightM} m, " +
          $"light's share of haze {RaymarchFeature.FarShadeLightHaze}; shells {(Phase4Bootstrapper.Shells != null ? "ON" : "off")}");
        float pa = (Camera.main != null ? Camera.main.fieldOfView : 60f) * Mathf.Deg2Rad / rows;
        L($"  slope-normal blend: from {0.05f / pa:F0} m (a 0.1 m voxel = 2 px) to {0.1f / pa:F0} m (1 px) at {rows} rows");

        foreach (var (n, pos, rot) in poses)
        {
            yield return FarShadeGoTo(cam, n, pos, rot, wait, far, world);
            Vector3 fwd = rot * Vector3.forward;
            float lit = SunHoursFacing(fwd, -1f);
            float back = n.StartsWith("eye_level") ? SunCycle.NoonHours : SunHoursFacing(fwd, 1f);   // the owner's frames were at noon
            foreach (var (lbl, h) in new[] { ("lit", lit), ("backlit", back) })
            {
                SunCycle.SetHours(h);
                L($"  {n} {lbl}: {h:F2} h, sun elevation {SunCycle.ElevationDegreesAt(h):F0} deg, " +
                  $"sun . view (horizontal) {math.dot(math.normalizesafe(SunCycle.DirectionAt(h).xz), math.normalizesafe(new float2(fwd.x, fwd.z))):F2}");
                yield return FarShadeSet(cam, $"{n}_{lbl}", rows);
            }
        }

        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var driver = FindAnyObjectByType<LightingDriver>();
        if (driver != null) driver.Attach(edits);
        else L("  NO LightingDriver: torches are placed as terrain but light nothing");
        foreach (var (n, pos, rot) in poses)
        {
            if (!nightKeep.Contains(n)) continue;
            yield return FarShadeGoTo(cam, n, pos, rot, wait, far, world);
            Vector3 fwd = rot * Vector3.forward;
            float3 f = math.normalizesafe(new float3(fwd.x, 0f, fwd.z));
            float3 r = new float3(f.z, 0f, -f.x);
            int eyeY = (int)math.floor(pos.y * 10f);
            int near = 0, farN = 0;
            // Far torches must land in RESIDENT chunks (EditService refuses others; a torch's light
            // is registered only there), so several spots out to 300 m are tried and each is logged.
            foreach (var (along, side) in new[] { (12f, -3f), (20f, 4f), (28f, -2f), (60f, 12f), (80f, -10f), (100f, -15f), (120f, 8f),
                                                  (150f, 10f), (200f, -5f), (250f, 0f), (300f, 6f) })
            {
                float3 p = (float3)pos + f * along + r * side;
                int x = (int)math.floor(p.x * 10f), z = (int)math.floor(p.z * 10f);
                int y = FarShadeGroundY(store, x, z, eyeY + 1500, eyeY - 3000);
                bool ok = y >= 0 && edits.TrySetVoxel(new int3(x, y + 1, z), Materials.Torch);
                if (ok) { if (along < 50f) near++; else farN++; }
                L($"    torch {along,4:F0} m ahead, {side,3:F0} m aside: {(ok ? $"at voxel ({x}, {y + 1}, {z})" : y < 0 ? "no ground found" : "refused (not resident)")}");
            }
            float3 pc = (float3)pos + f * 45f;   // tier 0 (a coarse tier's cell may not show a thin pool)
            int cx = (int)math.floor(pc.x * 10f), cz = (int)math.floor(pc.z * 10f);
            int cy = FarShadeGroundY(store, cx, cz, eyeY + 1500, eyeY - 3000);
            int lava = 0;
            if (cy >= 0)
                for (int dx = -25; dx <= 25; dx++)
                for (int dz = -25; dz <= 25; dz++)
                {
                    if (dx * dx + dz * dz > 625) continue;
                    int y = FarShadeGroundY(store, cx + dx, cz + dz, cy + 60, cy - 60);
                    if (y >= 0 && edits.TrySetVoxel(new int3(cx + dx, y, cz + dz), Materials.Lava)) lava++;
                }
            for (int i = 0; i < 60; i++) yield return null;
            SunCycle.SetHours(nightHours);
            L($"  {n} night: {nightHours:F2} h; torches near {near}/3, far (60-300 m) {farN}/8; lava pool 45 m ahead: {lava} voxels; " +
              $"lights registered {(driver != null ? driver.Lights.RegisteredCount : 0)}, lava tiles {(driver != null ? driver.Lights.LavaTileCount : 0)}");
            yield return FarShadeSet(cam, $"{n}_night", rows);
        }
        RaymarchFeature.FarShade = fs0;
        UpscalerSelect.Apply(UpscalerSelect.Kind.None, rows, false);
    }

    /// Move to a pose and give the far ring and the voxel band at most `wait` s.
    private IEnumerator FarShadeGoTo(Camera cam, string n, Vector3 pos, Quaternion rot, float wait, FarField far, bool world)
    {
        cam.transform.position = pos; cam.transform.rotation = rot;
        yield return Frames(30);
        float t0 = Time.realtimeSinceStartup;
        while (far.Busy && Time.realtimeSinceStartup - t0 < wait) yield return null;
        var sm = Phase4Bootstrapper.Streamer;
        while (world && sm != null && sm.LoadDeficit() > 0 && Time.realtimeSinceStartup - t0 < wait) yield return null;
        var sh = Phase4Bootstrapper.Shells;
        while (sh != null && (sh.InFlight > 0 || sh.Pending > 0) && Time.realtimeSinceStartup - t0 < wait) yield return null;
        string shells = "";
        if (sh != null) { int miss = 0; for (int k = sh.FirstShell; k <= sh.LastShell; k++) miss += sh.Missing(k); shells = $", shell chunks missing {miss}"; }
        L($"{n,-24} eye {pos}: waited {Time.realtimeSinceStartup - t0:F0} s, far ring {(far.Busy ? "STILL FILLING" : "ready")}, " +
          $"voxel deficit {(world && sm != null ? sm.LoadDeficit() : 0)}{shells}");
    }

    /// Raw OFF, raw ON, FSR 3 OFF, FSR 3 ON -- all at `rows` internal rows.
    private IEnumerator FarShadeSet(Camera cam, string tag, int rows)
    {
        foreach (bool fsr in new[] { false, true })
        {
            UpscalerSelect.Apply(fsr ? UpscalerSelect.Kind.Fsr3 : UpscalerSelect.Kind.None, rows, false);
            foreach (bool on in new[] { false, true })
            {
                RaymarchFeature.FarShade = on;
                yield return Frames(fsr ? 60 : 12);   // FSR 3 accumulates over still frames
                yield return CaptureTo($"{tag}_{(fsr ? "fsr3" : "raw")}_{(on ? "B_on" : "A_off")}");
            }
        }
    }

    /// The hour whose sun (25-60 deg up) is most behind (sign -1) or ahead of
    /// (sign +1) the view, horizontally.
    private static float SunHoursFacing(Vector3 fwd, float sign)
    {
        float2 f = math.normalizesafe(new float2(fwd.x, fwd.z));
        float best = -9f, bestH = SunCycle.NoonHours;
        for (float h = 6.5f; h <= 17.5f; h += 0.25f)
        {
            float el = SunCycle.ElevationDegreesAt(h);
            if (el < 25f || el > 60f) continue;
            float s = sign * math.dot(math.normalizesafe(SunCycle.DirectionAt(h).xz), f);
            if (s > best) { best = s; bestH = h; }
        }
        return bestH;
    }

    /// The top solid (not fluid) voxel of column (x, z) between yTop and yBottom, or -1.
    private static int FarShadeGroundY(ChunkStore store, int x, int z, int yTop, int yBottom)
    {
        for (int y = yTop; y >= yBottom; y--)
        {
            byte m = store.GetVoxel(new int3(x, y, z));
            if (m != Materials.Air && !MaterialRules.IsFluidMaterial(m)) return y;
        }
        return -1;
    }
}
