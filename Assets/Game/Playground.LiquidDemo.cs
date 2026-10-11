// Assets/Game/Playground.LiquidDemo.cs
//
// -liquiddemo (task 75): see-through liquids, before/after screenshots. An
// INTERACTIVE run (run-play.sh EXTRA="-liquiddemo"), fly camera, noon with the
// clock paused, sun behind the camera, no HUD. Every pose is shot twice:
// _off (RaymarchFeature.SeeThroughLiquids false = today) and _on. Then quits.
//   pools   six test pools on a stone plinth near the start (EditService, the
//           one CPU write path): water, acid, honey, sludge, oil, lava. Each
//           pool's bed steps down along +z: 0.2, 0.4, 0.7, 1.1, 1.6, 2.2 m of
//           fluid, with white (snow) stripes across the bed every 0.8 m so the
//           clarity reads as "which stripes still show". Shots: each pool from
//           ~3 m, all six from ~15 m.
//   lake    the nearest map LAKE (5 x 5 lake cells, >= 3 m deep): its shore
//           looking down into the shallows, then its centre from ~25 m, ~150 m
//           and ~800 m (near / mid / far band) -- the clarity must not change
//           with distance.
// Shots: persistentDataPath/LiquidDemo/.

using System.Collections;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator LiquidDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "LiquidDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        float2 sxz = math.normalizesafe(SunCycle.Direction.xz);
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        Debug.Log($"[LiquidDemo] start {f}, sun dir {SunCycle.Direction}, setting was {RaymarchFeature.SeeThroughLiquids}");
        _mode = MoveMode.Fly;
        DebugHideUi = true;
        DebugSuspendFluidSim = true;   // the pools hold still (acid would eat the stone, lava would meet nothing)
        var camT = Camera.main.transform;

        // Off then on at one pose (camera held there; 30 frames each so FSR 3's history settles).
        IEnumerator Pair(Vector3 camM, Vector3 atM, string name)
        {
            Vector3 look = atM - camM;
            camT.position = camM;
            _cam?.SetLook(math.degrees(math.atan2(look.x, look.z)), -math.degrees(math.atan2(look.y, new Vector2(look.x, look.z).magnitude)));
            foreach (bool on in new[] { false, true })
            {
                RaymarchFeature.SeeThroughLiquids = on;
                for (int k = 0; k < 30; k++) { camT.position = camM; yield return null; }
                yield return DemoShot(dir, name + (on ? "_on" : "_off"));
            }
        }
        IEnumerator WaitResident(int3 v, Vector3 camM, float maxS)
        {
            float tw = Time.unscaledTime;
            while (Time.unscaledTime - tw < maxS && !_store.IsResident(CoordMath.VoxelToChunk(v))) { camT.position = camM; yield return null; }
            for (int k = 0; k < 120; k++) { camT.position = camM; yield return null; }
        }

        // ---- 1. the pools ----
        // Plinth corner: 4 m ahead of the start on the side AWAY from the sun, so
        // a camera between the start and the pools has the sun behind it.
        byte[] fluids = { Materials.Water, Materials.Acid, Materials.Honey, Materials.Sludge, Materials.Oil, Materials.Lava };
        string[] fnames = { "water", "acid", "honey", "sludge", "oil", "lava" };
        int[] depths = { 2, 4, 7, 11, 16, 22 };
        const int PW = 31, PL = 64, PH = 26, GAP = 34;
        int3 b = new int3(f.x - 3 * GAP + (int)math.round(-sxz.x * 40f), 0, f.z - PL / 2 + (int)math.round(-sxz.y * 40f));
        int top = int.MinValue;
        for (int z = b.z; z < b.z + PL; z += 4)
        for (int x = b.x; x < b.x + 6 * GAP; x += 4)
            top = math.max(top, SurfaceYAt(x, z));
        b.y = top + 5;
        Vector3 plinthCam = new Vector3((b.x + 3 * GAP) * 0.1f, (b.y + PH) * 0.1f, (b.z + PL / 2) * 0.1f);
        yield return WaitResident(b, plinthCam + Vector3.up * 5f, 30f);
        int written = 0;
        for (int p = 0; p < 6; p++)
        {
            int x0 = b.x + p * GAP;
            written += _edits.SetBox(new int3(x0, b.y, b.z), new int3(x0 + PW - 1, b.y + PH - 1, b.z + PL - 1), Materials.Stone);
            int surf = b.y + PH - 2;   // top fluid voxel; the rim is one voxel above it
            for (int s = 0; s < 6; s++)
            {
                int z0 = b.z + 2 + s * 10, z1 = z0 + 9;
                written += _edits.SetBox(new int3(x0 + 2, surf + 1, z0), new int3(x0 + PW - 3, surf + 1, z1), Materials.Air);
                written += _edits.SetBox(new int3(x0 + 2, surf - depths[s] + 1, z0), new int3(x0 + PW - 3, surf, z1), fluids[p]);
                for (int sx = x0 + 2; sx <= x0 + PW - 3; sx += 8)
                    written += _edits.SetBox(new int3(sx, surf - depths[s], z0), new int3(math.min(sx + 3, x0 + PW - 3), surf - depths[s], z1), Materials.Snow);
            }
        }
        Debug.Log($"[LiquidDemo] pools at {b} (6 x {PW} x {PL} voxels, fluid top y {b.y + PH - 2}), {written} voxels written, depths 0.2-2.2 m along +z");
        for (int k = 0; k < 90; k++) yield return null;
        // Camera on the sun side of the plinth (toward -z if the sun is at -z).
        float side = sxz.y >= 0 ? 1f : -1f;   // +1: the sun is toward +z, so stand at +z and look -z
        float poolTopM = (b.y + PH - 1) * 0.1f;
        for (int p = 0; p < 6; p++)
        {
            float cx = (b.x + p * GAP + PW * 0.5f) * 0.1f, cz = (b.z + PL * 0.5f) * 0.1f;
            var at = new Vector3(cx, poolTopM - 0.5f, cz);
            var cam = new Vector3(cx, poolTopM + 3f, cz + side * 3.2f);
            yield return Pair(cam, at, $"pool_{p + 1}_{fnames[p]}_3m");
        }
        {
            var at = new Vector3((b.x + 3 * GAP) * 0.1f, poolTopM - 0.5f, (b.z + PL * 0.5f) * 0.1f);
            var cam = at + new Vector3(0f, 15f, side * 12f);
            yield return Pair(cam, at, "pools_all_15m");
        }

        // ---- 2. a lake: shore, then near / mid / far ----
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        if (src == null) { Debug.Log("[LiquidDemo] not a tg2 world -- no lake shots"); Finish(); yield break; }
        var spec = src.Spec; var view = src.Map.View; var o = src.O; var map = src.Map;
        int n = spec.mapCells, cv = spec.mapCellVoxels, sea = spec.seaLevelY;
        TerrainGen.ColumnSample Col(float x, float z) => TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
        int Depth(float x, float z)
        {
            var c = Col(x, z);
            return (c.water == TerrainGen.ColumnSample.NO_WATER_Y || c.waterKind != 0) ? 0 : math.max(0, c.water - c.height);
        }
        int SurfY(float x, float z)
        {
            var c = Col(x, z);
            int t = c.height;
            if (c.water != TerrainGen.ColumnSample.NO_WATER_Y) t = math.max(t, c.water);
            return math.max(t, sea);
        }
        if (!FindDemoLake(f.xz, sxz, out float2 lake, out float2 edge, out int waterTop))
        { Debug.Log("[LiquidDemo] no lake (5 x 5 lake cells, >= 3 m deep) within 3.2 km"); Finish(); yield break; }
        float wM = (waterTop + 1) * 0.1f;
        // The shallows: back from the edge into the lake until 0.8 m deep.
        float2 shallow = edge;
        for (int k = 0; k < 400 && Depth(shallow.x, shallow.y) < 8; k++) shallow -= sxz * 2f;
        Debug.Log($"[LiquidDemo] lake centre {lake} water y {waterTop}, depth {Depth(lake.x, lake.y) * 0.1f:F1} m; edge {edge}; " +
                  $"shallows {shallow} ({math.length(shallow - edge) * 0.1f:F1} m from the edge, {Depth(shallow.x, shallow.y) * 0.1f:F1} m deep); " +
                  $"{math.length(lake - (float2)f.xz) * 0.1f:F0} m from the start");
        float2 shoreXZ = edge + sxz * 30f;
        var shoreCam = new Vector3(shoreXZ.x * 0.1f, (SurfY(shoreXZ.x, shoreXZ.y) + 1) * 0.1f + 2.2f, shoreXZ.y * 0.1f);
        var shAt = new Vector3(shallow.x * 0.1f, wM, shallow.y * 0.1f) + (Vector3)(new float3(-sxz.x, 0f, -sxz.y) * 2f);
        yield return WaitResident(new int3((int)shallow.x, waterTop, (int)shallow.y), shoreCam, 60f);
        Debug.Log($"[LiquidDemo] loaded voxel at the shallows: {MaterialName(_store.GetVoxel(new int3((int)shallow.x, waterTop, (int)shallow.y)))}");
        yield return Pair(shoreCam, shAt, "lake_1_shore_shallows");
        // Near / mid / far: the lake centre from 25 m, 150 m and 800 m. Headings:
        // away from the sun first, then +-30 deg steps; the first whose sight
        // line clears the generated ground (and whose camera is 5 m above it) wins.
        var tAt = new Vector3(lake.x * 0.1f, wM, lake.y * 0.1f);
        float awayYaw = math.degrees(math.atan2(-sxz.x, -sxz.y));
        foreach (var (tag, dist, up) in new[] { ("2_near", 25f, 10f), ("3_mid", 150f, 30f), ("4_far800m", 800f, 120f) })
        {
            Vector3 cam = default; bool ok = false;
            for (int lift = 0; lift <= 6 && !ok; lift++)
            for (int h = 0; h < 12 && !ok; h++)
            {
                float yr = math.radians(awayYaw + ((h & 1) == 0 ? 1 : -1) * ((h + 1) >> 1) * 30f);
                float2 fw = new float2(math.sin(yr), math.cos(yr));
                float2 cxz = lake - fw * dist * 10f;
                float cy = waterTop + up * 10f * (1f + lift * 0.5f);
                if (cy < SurfY(cxz.x, cxz.y) + 50) continue;
                bool clear = true;
                for (int q = 1; q < 32 && clear; q++)
                {
                    float t = q / 32f;
                    float2 pq = math.lerp(cxz, lake, t);
                    if (SurfY(pq.x, pq.y) > math.lerp(cy, waterTop, t) + 3) clear = false;
                }
                if (!clear) continue;
                cam = new Vector3(cxz.x * 0.1f, cy * 0.1f, cxz.y * 0.1f); ok = true;
            }
            if (!ok) { Debug.Log($"[LiquidDemo] lake {tag}: no clear sight line, skipped"); continue; }
            camT.position = cam;
            for (int k = 0; k < (dist > 200f ? 300 : 180); k++) { camT.position = cam; yield return null; }
            Debug.Log($"[LiquidDemo] lake {tag}: camera {cam}, {Vector3.Distance(cam, tAt):F0} m to the lake centre");
            yield return Pair(cam, tAt, $"lake_{tag}");
        }
        Finish();

        void Finish()
        {
            RaymarchFeature.SeeThroughLiquids = true;
            Debug.Log("[LiquidDemo] done");
            Application.Quit();
        }
    }

    /// Task 75 (-liquiddemo and the cost split's -costpaths lake): the nearest
    /// real LAKE to `fromXZ` (voxels) on the terrain map -- a lake cell whose
    /// 5 x 5 cells (40 m) are all lake (the map's water class, not a river's
    /// slab), >= 3 m deep at its centre -- and its edge toward `sunXZ`: the last
    /// water column walking from the centre toward the sun. False: none within
    /// 3.2 km, or not a tg2 world.
    internal static bool FindDemoLake(int2 fromXZ, float2 sunXZ, out float2 lake, out float2 edge, out int waterTop)
    {
        lake = edge = default; waterTop = 0;
        var src = Phase4Bootstrapper.Streamer != null ? Phase4Bootstrapper.Streamer.TerrainGenSource : null;
        if (src == null) return false;
        var spec = src.Spec; var view = src.Map.View; var o = src.O; var map = src.Map;
        int n = spec.mapCells, cv = spec.mapCellVoxels;
        int Depth(float x, float z)
        {
            var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
            return (c.water == TerrainGen.ColumnSample.NO_WATER_Y || c.waterKind != 0) ? 0 : math.max(0, c.water - c.height);
        }
        byte LAKE = TerrainGen.Hydrology.LAKE;
        int fcx = fromXZ.x / cv, fcz = fromXZ.y / cv;
        for (int r = 0; r <= 400; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
            int cx = fcx + dx, cz = fcz + dz;
            if (cx < 2 || cz < 2 || cx >= n - 2 || cz >= n - 2 || map.WaterClass[cz * n + cx] != LAKE) continue;
            bool all = true;
            for (int j = -2; j <= 2 && all; j++)
            for (int i = -2; i <= 2 && all; i++)
                all = map.WaterClass[(cz + j) * n + cx + i] == LAKE;
            if (!all) continue;
            float2 p = new float2(cx + 0.5f, cz + 0.5f) * cv;
            if (Depth(p.x, p.y) < 30) continue;
            lake = p;
            waterTop = TerrainGen.ShapeField.Sample(in spec, in view, in o, p.x + 0.5f, p.y + 0.5f, 1).water;
            edge = lake;
            for (int k = 0; k < 4000 && Depth(edge.x + sunXZ.x * 2f, edge.y + sunXZ.y * 2f) > 0; k++) edge += sunXZ * 2f;
            return true;
        }
        return false;
    }

    /// Top solid-or-fluid voxel y of the loaded world at (x, z), scanning down
    /// from 60 m above the start; the start's y if nothing is loaded there.
    private int SurfaceYAt(int x, int z)
    {
        int y0 = CoordMath.WorldToVoxel(_player.Motor.PositionM).y;
        for (int y = y0 + 600; y > y0 - 600; y--)
            if (_store.GetVoxel(new int3(x, y, z)) != Materials.Air) return y;
        return y0;
    }
}
