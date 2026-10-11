// Assets/Game/Playground.RiverDemo.cs
//
// -riverdemo (task 73): screenshots of how generated rivers look today, for
// RIVERS_WATERFALLS_PLAN.md. An INTERACTIVE run (run-play.sh EXTRA="-riverdemo"),
// fly camera, noon with the clock paused, sun behind the camera. Then quits.
// The sites come from the terrain MAP (WorldMap.Water / WaterClass, 8 m cells),
// nearest the spawn first:
//   A  a river reach on a slope   (river cell -> river neighbour 2-6 m lower)
//   B  a river entering a lake    (river cell beside a lake cell, higher)
//   C  a waterfall                (river cell -> river/lake neighbour >= 10 m lower; else the biggest drop)
//   D  a river mouth at the sea   (river cell beside a sea cell: BUGS #7's slab)
// Each site: a near shot (~12 m) and a mid shot (~100 m); A also a far-band
// shot (~800 m). With -riverlook also on the command line, every pose is shot
// twice: _off (today) and _on (RaymarchFeature.RiverLook, the task 73
// shading-only prototype). Near shots also log a wall census of the loaded
// world in a 24 m box: water top faces vs water side faces facing air.
// Shots: persistentDataPath/RiverDemo/.

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator RiverDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        bool both = HasArg("-riverlook");
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "RiverDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        if (src == null) { Debug.Log("[RiverDemo] not a tg2 world -- nothing to show"); Application.Quit(); yield break; }
        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        var map = src.Map;
        int n = spec.mapCells, cv = spec.mapCellVoxels, sea = spec.seaLevelY;
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        int fcx = f.x / cv, fcz = f.z / cv;
        Debug.Log($"[RiverDemo] start {f}, away-from-sun yaw {awayYaw:F0}, map {n} cells of {cv} voxels, sea y {sea}, riverlook A/B {both}");

        // ---- the search: map cells in rings round the spawn ----
        byte RIVER = TerrainGen.Hydrology.RIVER, LAKE = TerrainGen.Hydrology.LAKE;
        int WY(int i) => sea + (int)math.floor(map.Water[i]);
        int2[] n8 = { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1), new int2(1, 1), new int2(-1, 1), new int2(1, -1), new int2(-1, -1) };
        int[] siteI = { -1, -1, -1, -1 }, siteJ = { -1, -1, -1, -1 };
        int bestFallI = -1, bestFallJ = -1, bestFall = 0;
        for (int r = 1; r <= 400 && (siteI[0] < 0 || siteI[1] < 0 || siteI[2] < 0 || siteI[3] < 0); r++)
        {
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int cx = fcx + dx, cz = fcz + dz;
                if (cx < 1 || cz < 1 || cx >= n - 1 || cz >= n - 1) continue;
                int i = cz * n + cx;
                if (map.WaterClass[i] != RIVER) continue;
                for (int k = 0; k < 8; k++)
                {
                    int j = (cz + n8[k].y) * n + cx + n8[k].x;
                    byte cj = map.WaterClass[j];
                    bool seaJ = map.Height[j] <= 0f;
                    if (k < 4 && seaJ && siteI[3] < 0) { siteI[3] = i; siteJ[3] = j; }
                    if (k < 4 && cj == LAKE && WY(i) > WY(j) && siteI[1] < 0) { siteI[1] = i; siteJ[1] = j; }
                    if (cj != RIVER && cj != LAKE) continue;
                    int drop = WY(i) - WY(j);
                    if (cj == RIVER && k < 4 && drop >= 20 && drop <= 60 && map.Height[i] > 50f && siteI[0] < 0) { siteI[0] = i; siteJ[0] = j; }
                    if (drop >= 100 && siteI[2] < 0) { siteI[2] = i; siteJ[2] = j; }
                    if (drop > bestFall && r <= 250) { bestFall = drop; bestFallI = i; bestFallJ = j; }
                }
            }
            if ((r & 7) == 0) yield return null;
        }
        if (siteI[2] < 0 && bestFallI >= 0) { siteI[2] = bestFallI; siteJ[2] = bestFallJ; }
        string[] names = { "A_slope_reach", "B_river_into_lake", "C_waterfall", "D_river_mouth_sea" };
        for (int s = 0; s < 4; s++)
        {
            if (siteI[s] < 0) { Debug.Log($"[RiverDemo] site {names[s]}: none found within 3.2 km"); continue; }
            int i = siteI[s], j = siteJ[s];
            Debug.Log($"[RiverDemo] site {names[s]}: cell ({i % n},{i / n}) -> ({j % n},{j / n}), water y {WY(i)} -> " +
                      (map.Height[j] <= 0f ? $"sea {sea}" : $"{WY(j)} ({TerrainGen.Hydrology.IsLake(j, map.WaterClass)} lake)") +
                      $", drop {(WY(i) - (map.Height[j] <= 0f ? sea : WY(j))) * 0.1f:F1} m, ground {map.Height[i] * 0.1f:F1} m, flow {map.Flow[i]:F0} cells, " +
                      $"{math.length(new float2(i % n - fcx, i / n - fcz)) * cv * 0.1f:F0} m from the spawn");
        }

        // ---- the shots ----
        _mode = MoveMode.Fly;
        DebugHideUi = true;   // no HUD, hotbar or minimap in the shots
        var camT = Camera.main.transform;
        int SurfY(float x, float z, bool groundOnly = false)
        {
            var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, x, z, 1);
            int top = c.height;
            if (!groundOnly && c.water != TerrainGen.ColumnSample.NO_WATER_Y) top = math.max(top, c.water);
            return math.max(top, sea);
        }
        for (int s = 0; s < 4; s++)
        {
            if (siteI[s] < 0) continue;
            int i = siteI[s], j = siteJ[s];
            float2 ci = new float2(i % n + 0.5f, i / n + 0.5f) * cv, cj = new float2(j % n + 0.5f, j / n + 0.5f) * cv;
            float2 pxz = (ci + cj) * 0.5f;                     // the step between the two cells
            int lowY = map.Height[j] <= 0f ? sea : WY(j);
            int hiY = WY(i);
            float3 tgt = new float3(pxz.x, s == 2 ? (lowY + hiY) >> 1 : lowY + 2, pxz.y);
            float2 fd = math.normalizesafe(cj - ci);           // downstream
            float yawLookUp = math.degrees(math.atan2(-fd.x, -fd.y)), yawLookDown = math.degrees(math.atan2(fd.x, fd.y));
            var poses = new List<(string tag, float dist, float up)> { ("near", 120f, 50f), ("mid", 1000f, 300f) };
            if (s == 0) { poses.Add(("far", 8000f, 1500f)); poses.Add(("far2km", 20000f, 4000f)); }
            bool censusDone = false;
            foreach (var (tag, dist, up) in poses)
            {
                // Heading: near shots look along the river first (downstream camera
                // looking up it, then upstream looking down it) -- a valley's sides
                // block every other near view; mid/far shots put the sun behind
                // first. The first heading whose sight line clears the ground wins.
                var yaws = new List<float>();
                if (tag == "near") { yaws.Add(yawLookUp); yaws.Add(yawLookDown); }
                for (int h = 0; h < 8; h++) yaws.Add(awayYaw + ((h & 1) == 0 ? 1 : -1) * ((h + 1) >> 1) * 30f);
                if (tag != "near") { yaws.Add(yawLookUp); yaws.Add(yawLookDown); }
                float3 cam = default; float yaw = awayYaw; bool ok = false;
                for (int lift = 0; lift <= 8 && !ok; lift++)
                for (int h = 0; h < yaws.Count && !ok; h++)
                {
                    float yw = yaws[h];
                    float yr = math.radians(yw);
                    float2 fw = new float2(math.sin(yr), math.cos(yr));
                    float2 cxz = pxz - fw * dist;
                    float cy = tgt.y + up * (1f + lift * 0.5f);
                    if (cy < SurfY(cxz.x, cxz.y) + 25) continue;
                    bool clear = true;
                    // Near: the river's own water slabs are what we came to see, so
                    // only GROUND blocks, and the last quarter is not checked.
                    bool nearPose = tag == "near";
                    for (int q = 1; q < (nearPose ? 12 : 16) && clear; q++)
                    {
                        float t = q / 16f;
                        float2 p = math.lerp(cxz, pxz, t);
                        if (SurfY(p.x, p.y, nearPose) > math.lerp(cy, tgt.y, t) + 3) clear = false;
                    }
                    if (!clear) continue;
                    cam = new float3(cxz.x, cy, cxz.y); yaw = yw; ok = true;
                }
                if (!ok) { Debug.Log($"[RiverDemo] {names[s]} {tag}: no clear sight line, skipped"); continue; }
                float3 camM = cam * 0.1f, tgtM = (tgt + 0.5f) * 0.1f, look = tgtM - camM;
                float pitch = -math.degrees(math.atan2(look.y, math.length(look.xz)));
                camT.position = camM;
                _cam?.SetLook(math.degrees(math.atan2(look.x, look.z)), pitch);
                int3 tv = new int3((int)tgt.x, lowY, (int)tgt.z);
                float tw = Time.unscaledTime;
                bool near = !tag.StartsWith("far");
                while (near && Time.unscaledTime - tw < 75f && !(_store.IsResident(CoordMath.VoxelToChunk(tv)) && _store.GetVoxel(tv) != Materials.Air))
                { camT.position = camM; yield return null; }
                for (int k = 0; k < (near ? 180 : 300); k++) { camT.position = camM; yield return null; }
                Debug.Log($"[RiverDemo] {names[s]} {tag}: camera {(int3)cam} yaw {math.degrees(math.atan2(look.x, look.z)):F0} (sun-away {awayYaw:F0}) pitch {pitch:F0}, " +
                          $"{math.length(look):F0} m to the target {tv}, loaded voxel there: {MaterialName(_store.GetVoxel(tv))}");
                if (!censusDone && near)
                {
                    censusDone = true;
                    int tops = 0, sides = 0, tallest = 0;
                    for (int z = tv.z - 120; z < tv.z + 120; z++)
                    {
                        for (int x = tv.x - 120; x < tv.x + 120; x++)
                        {
                            int run = 0;
                            for (int y = tv.y + 120; y >= tv.y - 120; y--)
                            {
                                int3 v = new int3(x, y, z);
                                if (_store.GetVoxel(v) != Materials.Water) { run = 0; continue; }
                                if (_store.GetVoxel(v + new int3(0, 1, 0)) == Materials.Air) tops++;
                                int fa = 0;
                                if (_store.GetVoxel(v + new int3(1, 0, 0)) == Materials.Air) fa++;
                                if (_store.GetVoxel(v + new int3(-1, 0, 0)) == Materials.Air) fa++;
                                if (_store.GetVoxel(v + new int3(0, 0, 1)) == Materials.Air) fa++;
                                if (_store.GetVoxel(v + new int3(0, 0, -1)) == Materials.Air) fa++;
                                sides += fa;
                                run = fa > 0 ? run + 1 : 0;
                                tallest = math.max(tallest, run);
                            }
                        }
                        if ((z & 15) == 0) { camT.position = camM; yield return null; }
                    }
                    Debug.Log($"[RiverDemo] {names[s]} wall census (24 x 24 m, +-12 m): water top faces {tops}, side faces facing air {sides} " +
                              $"({(tops > 0 ? (float)sides / tops : 0f):F2} per top face), tallest exposed water wall {tallest * 0.1f:F1} m");
                }
                string baseName = $"{(char)('a' + s)}_{names[s]}_{tag}";
                if (!both) { yield return DemoShot(dir, baseName); continue; }
                RaymarchFeature.RiverLook = false;
                for (int k = 0; k < 8; k++) { camT.position = camM; yield return null; }
                yield return DemoShot(dir, baseName + "_off");
                RaymarchFeature.RiverLook = true;
                for (int k = 0; k < 8; k++) { camT.position = camM; yield return null; }
                yield return DemoShot(dir, baseName + "_on");
                RaymarchFeature.RiverLook = false;
            }
        }
        Debug.Log("[RiverDemo] done");
        Application.Quit();
    }
}
