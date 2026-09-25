// Assets/Game/PlaygroundCapture.LavaLight.cs
//
// -lavalight : OWNER DECISION 8, SHOWN. Lava lights its surroundings through
// one aggregate point light per 32-voxel tile (LightSourceBuffer's LAVA
// AGGREGATE block), capped at LightingConfig.lavaLightMaxSlots so placed
// torches keep their share of §3.8's global cap.
//
// Every pose is shot twice from the same eye, lava lighting ON (the shipped
// share) and OFF (share 0 -- exactly what shipped before decision 8), so the
// pair differs in that one thing. The OFF arm is a config value, not a code
// path: lava stays registered in its aggregates, it just gets no slots.
//
// Lava here is written through an EditService with NO fluid simulation
// attached, so it does not flow: the ON and OFF shots see the same pool.
// A LOOK, not a measurement -- no frame times here; ./run-lavalight-ab.sh's
// sustained arms are the frame-time evidence.
using System;
using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;

public partial class PlaygroundCapture
{
    private IEnumerator LavaLightShots()
    {
        Screen.SetResolution(1920, 1080, !HasFlag("-windowed"));
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        while (Phase4Bootstrapper.Store == null) yield return null;
        RaymarchFeature.GateModeOverride = RaymarchFeature.GateResMode.ForcedCustom;
        RaymarchFeature.CustomGateResolution = new Vector2Int(960, 540);
        for (int i = 0; i < 300; i++) yield return null;

        LightingDriver.ClockPaused = true;
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        _folder = Path.Combine(Application.persistentDataPath, "LavaLight", ts);
        Directory.CreateDirectory(_folder);

        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var driver = FindAnyObjectByType<LightingDriver>();
        if (driver == null) { L("NO LightingDriver -- nothing to show"); yield break; }
        driver.Attach(edits);
        driver.Lights.Clear();
        int shippedSlots = LightingConfig.Active.lavaLightMaxSlots;

        L($"DECISION 8 -- LAVA AS A LIGHT, one aggregate per 32-voxel tile -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}");
        L($"lava share {shippedSlots} of {EngineConfig.MAX_ACTIVE_LIGHTS} slots   " +
          $"intensity {LightingConfig.Active.lavaLightIntensity}   fullCount {LightingConfig.Active.lavaLightFullCount}   " +
          $"rangeScale {LightingConfig.Active.lavaLightRangeScale}   lift {LightingConfig.Active.lavaLightLiftM} m");
        L("");

        var cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 peak = DeepestNear(store, camVox, 240);
        int surf = SurfaceYAt(store, peak.x, peak.z);

        // ---- 1. The §H.1 cavern: sealed, a lava lake in its floor ----
        SunCycle.SetHours(SunCycle.NoonHours);
        int cy = Mathf.Max(16, surf - 40);
        int3 cave = new int3(peak.x - 60, cy, peak.z + 20);
        int carved = 0;
        for (int i = 0; i < 5; i++) carved += Carve(edits, cave + new int3(i * 10 - 20, 0, 0), 14);
        for (int i = 0; i < 60; i++) yield return null;
        int lava = 0;
        for (int dx = -34; dx <= 34; dx++)
        for (int dz = -13; dz <= 13; dz++)
        for (int dy = -12; dy <= -8; dy++)
            if (edits.TrySetVoxel(cave + new int3(dx, dy, dz), Materials.Lava)) lava++;
        for (int i = 0; i < 90; i++) yield return null;
        L($"cavern at {cave}: {carved} voxels carved, {lava} lava voxels placed");
        L($"  -> {driver.Lights.LavaVoxelCount} lava voxels in {driver.Lights.LavaTileCount} tile aggregates; " +
          $"{driver.Lights.RegisteredCount} point lights registered (expected 0: lava is never per-voxel)");

        // THE WIDE EYE MUST BE INSIDE THE CAVERN. §H.1's arm used cave + (-40, 4, 0),
        // but the five carved spheres only reach x = -34 -- that eye was in rock
        // and its frame was black in both arms (first run of this mode,
        // 2026-09-24_211658). -30 is 10.8 voxels from the nearest sphere centre.
        int3 wideEye = cave + new int3(-30, 4, 0);
        yield return LavaPair(cam, wideEye, cave + new int3(20, -8, 0), "1_cavern_wide", shippedSlots);
        yield return LavaPair(cam, cave + new int3(-16, -4, 0), cave + new int3(-16, -2, 12), "2_wall_beside_lava", shippedSlots);
        yield return LavaPair(cam, cave + new int3(30, 2, -6), cave + new int3(0, -6, 10), "3_cavern_from_the_far_end", shippedSlots);

        // ---- 2. Starvation: more torches than the cap, lava right beside them ----
        int placed = 0;
        for (int i = 0; i < EngineConfig.MAX_ACTIVE_LIGHTS + 8; i++)
            if (edits.TrySetVoxel(cave + new int3(-30 + (i % 20) * 3, 6, -8 + (i / 20) * 16), Materials.Torch)) placed++;
        LightingConfig.Active.lavaLightMaxSlots = shippedSlots;
        for (int i = 0; i < 30; i++) yield return null;
        yield return StressShot(cam, wideEye, cave + new int3(20, -4, 0), "4_torches_AND_lava_ON");
        int active = driver.Lights.ActiveCount, lavaActive = driver.Lights.LavaActiveCount;
        L("");
        L($"STARVATION CHECK: {placed} torches placed + lava in {driver.Lights.LavaTileCount} tiles, player beside both");
        L($"  active {active} of cap {EngineConfig.MAX_ACTIVE_LIGHTS}: torches {active - lavaActive}, lava {lavaActive} " +
          $"(torches are guaranteed {EngineConfig.MAX_ACTIVE_LIGHTS - shippedSlots})");

        // ---- 3. A lava pool on open ground at night ----
        driver.Lights.Clear();
        int3 pool = new int3(peak.x + 40, 0, peak.z - 30);
        int py = SurfaceYAt(store, pool.x, pool.z);
        pool.y = py;
        int poolN = 0;
        for (int dx = -12; dx <= 12; dx++)
        for (int dz = -12; dz <= 12; dz++)
        {
            if (dx * dx + dz * dz > 144) continue;
            int3 c = new int3(pool.x + dx, SurfaceYAt(store, pool.x + dx, pool.z + dz), pool.z + dz);
            if (edits.TrySetVoxel(c, Materials.Lava)) poolN++;
        }
        for (int i = 0; i < 60; i++) yield return null;
        L("");
        L($"night pool at {pool}: {poolN} lava voxels on the surface, {driver.Lights.LavaTileCount} tiles");
        SunCycle.SetHours(0.5f);
        yield return LavaPair(cam, pool + new int3(-60, 30, -60), pool, "5_surface_pool_at_night", shippedSlots);
        SunCycle.SetHours(SunCycle.GoldenHourHours);
        yield return LavaPair(cam, pool + new int3(-60, 30, -60), pool, "6_surface_pool_golden_hour", shippedSlots);

        LightingConfig.Active.lavaLightMaxSlots = shippedSlots;
        File.WriteAllText(Path.Combine(_folder, "lavalight.txt"), _log.ToString());
        Debug.Log("[lavalight]\n" + _log);
        Application.Quit();
    }

    private IEnumerator LavaPair(Camera cam, int3 eye, int3 look, string name, int shippedSlots)
    {
        LightingConfig.Active.lavaLightMaxSlots = shippedSlots;
        yield return StressShot(cam, eye, look, name + "_A_lava_light_ON");
        LightingConfig.Active.lavaLightMaxSlots = 0;
        yield return StressShot(cam, eye, look, name + "_B_lava_light_OFF");
        LightingConfig.Active.lavaLightMaxSlots = shippedSlots;
    }
}
