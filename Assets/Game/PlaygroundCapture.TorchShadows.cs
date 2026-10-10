// Assets/Game/PlaygroundCapture.TorchShadows.cs
//
// -torchshadowshots : TASK 46 (engine plan step 2, owner Q5 (a)), SHOWN. Torch
// shadow rays (LightingConfig.torchShadowRays) and emissive bleed ON by default.
//
// Every pose is shot twice from the same eye in the same process:
//   _A_before : RaymarchFeature.TorchShadowsOverride = 0, EmissiveBleedOverride = 0
//               (exactly what -torchshadows 0 -emissivebleed 0 select),
//   _B_after  : both overrides -1, i.e. the shipped defaults.
// Same scene, same lights, so the pair differs only in those two switches.
//
// Each pose is built in its own site, and the light buffer is cleared before
// the next site is built, so one pose's torches never light another's.
// A LOOK, not a measurement -- no frame times (plan step 7 measures cost).
using System;
using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Rendering;

public partial class PlaygroundCapture
{
    private IEnumerator TorchShadowShots()
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
        _folder = Path.Combine(Application.persistentDataPath, "TorchShadows", ts);
        Directory.CreateDirectory(_folder);

        var store = Phase4Bootstrapper.Store;
        var edits = new EditService();
        edits.AttachWorld(store, store, store, Phase4Bootstrapper.Clipmap);
        var driver = FindAnyObjectByType<LightingDriver>();
        if (driver == null) { L("NO LightingDriver -- nothing to show"); yield break; }
        driver.Attach(edits);
        driver.Lights.Clear();

        L($"TASK 46 -- TORCH SHADOW RAYS + EMISSIVE BLEED -- {ts}");
        L($"device {SystemInfo.graphicsDeviceName}  {Screen.width}x{Screen.height}  " +
          $"{(Debug.isDebugBuild ? "DEVELOPMENT" : "RELEASE")}");
        L($"after = config defaults: torchShadowRays {LightingConfig.Active.torchShadowRays}, " +
          $"emissiveBleed {LightingConfig.Active.emissiveBleed}; before = 0 / 0");
        L("");

        var cam = Camera.main;
        int3 camVox = CoordMath.WorldToVoxel(new float3(cam.transform.position.x,
                                                        cam.transform.position.y,
                                                        cam.transform.position.z));
        int3 peak = DeepestNear(store, camVox, 240);
        SunCycle.SetHours(SunCycle.NoonHours);

        // ---- 1. A sealed room split by a 2-voxel stone wall, torch on one side ----
        int3 room = SiteUnder(store, peak, 45);
        int carved = CarveBox(edits, room, new int3(-20, -6, -10), new int3(20, 10, 10));
        int wall = 0;
        for (int x = 0; x <= 1; x++)
        for (int dy = -6; dy <= 10; dy++)
        for (int dz = -10; dz <= 10; dz++)
            if (edits.TrySetVoxel(room + new int3(x, dy, dz), Materials.Stone)) wall++;
        int3 torchA = room + new int3(-6, -6, 0);
        edits.TrySetVoxel(torchA, Materials.Torch);
        for (int i = 0; i < 60; i++) yield return null;
        L($"1. room at {room} (roof {RoofAbove(store, room, 10)} voxels): {carved} carved, {wall} wall voxels, torch {torchA}");
        yield return TorchPair(cam, room + new int3(-17, 3, 0), room + new int3(0, -3, 0), "1a_wall_torch_side");
        // Its back to the wall: point lights take N.L, so only far-side faces
        // turned TOWARD the torch (the end wall, the floor) can show a leak.
        yield return TorchPair(cam, room + new int3(3, 3, 0), room + new int3(20, -4, 0), "1b_wall_far_side");

        // ---- 2. A cave with two pillars, lit by 4 torches on its floor ----
        driver.Lights.Clear();
        int3 cave = SiteUnder(store, peak + new int3(0, 0, 160), 45);
        carved = 0;
        for (int i = 0; i < 3; i++) carved += Carve(edits, cave + new int3(i * 20 - 20, 0, 0), 14);
        int pillar = 0;
        foreach (int3 p in new[] { new int3(-10, 0, -2), new int3(10, 0, 3) })
            for (int dy = -14; dy <= 14; dy++)
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (edits.TrySetVoxel(cave + p + new int3(dx, dy, dz), Materials.Stone)) pillar++;
        int torches = 0;
        foreach (int3 t in new[] { new int3(-26, 0, 0), new int3(-4, 0, -8), new int3(18, 0, 6), new int3(2, 0, 9) })
            if (edits.TrySetVoxel(FloorAt(store, cave + t), Materials.Torch)) torches++;
        for (int i = 0; i < 60; i++) yield return null;
        L($"2. cave at {cave} (roof {RoofAbove(store, cave, 14)} voxels): {carved} carved, {pillar} pillar voxels, {torches} torches");
        yield return TorchPair(cam, cave + new int3(-30, 4, 0), cave + new int3(20, -8, 0), "2_cave_four_torches");

        // ---- 3. The lava cavern (the -lavalight cavern) with 8 wall torches ----
        driver.Lights.Clear();
        int3 lavaCave = SiteUnder(store, peak + new int3(0, 0, -200), 40);
        carved = 0;
        for (int i = 0; i < 5; i++) carved += Carve(edits, lavaCave + new int3(i * 10 - 20, 0, 0), 14);
        for (int i = 0; i < 60; i++) yield return null;
        int lava = 0;
        for (int dx = -34; dx <= 34; dx++)
        for (int dz = -13; dz <= 13; dz++)
        for (int dy = -12; dy <= -8; dy++)
            if (edits.TrySetVoxel(lavaCave + new int3(dx, dy, dz), Materials.Lava)) lava++;
        torches = 0;
        for (int i = 0; i < 8; i++)
        {
            int side = (i & 1) == 0 ? 1 : -1;
            if (edits.TrySetVoxel(WallAt(store, lavaCave + new int3(-28 + i * 8, 2, 0), side), Materials.Torch)) torches++;
        }
        for (int i = 0; i < 90; i++) yield return null;
        L($"3. lava cavern at {lavaCave} (roof {RoofAbove(store, lavaCave, 14)} voxels): {carved} carved, " +
          $"{lava} lava voxels in {driver.Lights.LavaTileCount} tiles, {torches} wall torches");
        yield return TorchPair(cam, lavaCave + new int3(-30, 4, 0), lavaCave + new int3(20, -8, 0), "3_lava_cavern_torches");

        // ---- 4. A lava pool on open ground at night (the -lavalight pool) ----
        driver.Lights.Clear();
        int3 pool = new int3(peak.x + 40, 0, peak.z - 30);
        pool.y = SurfaceYAt(store, pool.x, pool.z);
        int poolN = 0;
        for (int dx = -12; dx <= 12; dx++)
        for (int dz = -12; dz <= 12; dz++)
        {
            if (dx * dx + dz * dz > 144) continue;
            int3 c = new int3(pool.x + dx, SurfaceYAt(store, pool.x + dx, pool.z + dz), pool.z + dz);
            if (edits.TrySetVoxel(c, Materials.Lava)) poolN++;
        }
        for (int i = 0; i < 60; i++) yield return null;
        SunCycle.SetHours(0.5f);
        L($"4. night pool at {pool}: {poolN} lava voxels, {driver.Lights.LavaTileCount} tiles");
        yield return TorchPair(cam, pool + new int3(-60, 30, -60), pool, "4_surface_lava_pool_night");

        RaymarchFeature.TorchShadowsOverride = -1;
        RaymarchFeature.EmissiveBleedOverride = -1;
        File.WriteAllText(Path.Combine(_folder, "torchshadows.txt"), _log.ToString());
        Debug.Log("[torchshadows]\n" + _log);
        Application.Quit();
    }

    private IEnumerator TorchPair(Camera cam, int3 eye, int3 look, string name)
    {
        byte at = Phase4Bootstrapper.Store.GetVoxel(eye);
        if (at != Materials.Air) L($"  WARNING: {name} eye {eye} is in material {at}, not air");
        RaymarchFeature.TorchShadowsOverride = 0;
        RaymarchFeature.EmissiveBleedOverride = 0;
        yield return StressShot(cam, eye, look, name + "_A_before");
        RaymarchFeature.TorchShadowsOverride = -1;
        RaymarchFeature.EmissiveBleedOverride = -1;
        yield return StressShot(cam, eye, look, name + "_B_after");
    }

    /// A site `depth` voxels under the surface at (x, z), never below y = 20.
    private static int3 SiteUnder(ChunkStore store, int3 xz, int depth)
    {
        int surf = SurfaceYAt(store, xz.x, xz.z);
        return new int3(xz.x, Mathf.Max(20, surf - depth), xz.z);
    }

    private static int CarveBox(EditService edits, int3 c, int3 lo, int3 hi)
    {
        int n = 0;
        for (int dx = lo.x; dx <= hi.x; dx++)
        for (int dy = lo.y; dy <= hi.y; dy++)
        for (int dz = lo.z; dz <= hi.z; dz++)
            if (edits.TrySetVoxel(c + new int3(dx, dy, dz), Materials.Air)) n++;
        return n;
    }

    /// Rock between the top of a cavity (centre.y + up) and the surface.
    private static int RoofAbove(ChunkStore store, int3 centre, int up)
        => SurfaceYAt(store, centre.x, centre.z) - (centre.y + up);

    /// The lowest air voxel above solid ground, scanning down from `from`.
    private static int3 FloorAt(ChunkStore store, int3 from)
    {
        int3 p = from;
        for (int i = 0; i < 40 && store.GetVoxel(p - new int3(0, 1, 0)) == Materials.Air; i++)
            p.y--;
        return p;
    }

    /// The last air voxel before solid rock, walking from `from` along +z or -z.
    private static int3 WallAt(ChunkStore store, int3 from, int side)
    {
        int3 p = from;
        for (int i = 0; i < 40 && store.GetVoxel(p + new int3(0, 0, side)) == Materials.Air; i++)
            p.z += side;
        return p;
    }
}
