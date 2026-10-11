// Assets/Game/Playground.OreDemo.cs
//
// -oredemo (task 66): gameplay step 3's acceptance screenshots. An INTERACTIVE
// Survival run (run-play.sh EXTRA="-oredemo"), so the HUD, the dig path and the
// inventory are exactly what a player gets. Then quits.
//   1  a tunnel wall with an ore deposit in it   (fly camera in the tunnel, one torch)
//   2  the hotbar holding the dug ore            (SurvivalDig, the LMB path)
//   3  the E panel with it
//   4  the dev map's CLOSE slice at the deposit  (O, Z x3: one voxel per pixel)
// The deposit is found on the TerrainGen side (DetailField.Material, the same
// function the chunks are made from), nearest the spawn first: an ore voxel with
// tunnel air beside it horizontally and room for the camera. The loaded world
// then confirms it (ChunkStore.GetVoxel). Shots: persistentDataPath/OreDemo/.

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator OreDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        SetGameMode(true);
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "OreDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        if (src == null) { Debug.Log("[OreDemo] not a tg2 world -- nothing to show"); Application.Quit(); yield break; }

        var spec = src.Spec; var view = src.Map.View; var o = src.O;
        byte[] plane = TerrainGen.OreTable.DensityPlane(src.Map.Record, spec.mapCells);
        var cols = new Dictionary<long, (TerrainGen.ColumnSample c, TerrainGen.DetailField.ColumnDetail cd, float st)>();
        (TerrainGen.ColumnSample c, TerrainGen.DetailField.ColumnDetail cd, float st) Col(int x, int z)
        {
            long key = ((long)x << 32) | (uint)z;
            if (cols.TryGetValue(key, out var e)) return e;
            var c = TerrainGen.ShapeField.Sample(in spec, in view, in o, x + 0.5f, z + 0.5f, 1);
            float st = TerrainGen.ShapeField.MaxStep(in spec, in view, in o, x + 0.5f, z + 0.5f, 1, c.height);
            var cd = TerrainGen.DetailField.PrepareColumn(in spec, in o, x, z);
            if (cd.oreMul != 0) cd.oreMul = plane[TerrainGen.Ores.CellIndex(x, z, spec.mapCells, spec.mapCellVoxels)];
            if (cols.Count > 20000) cols.Clear();
            cols[key] = (c, cd, st);
            return (c, cd, st);
        }
        byte Mat(int3 v)
        {
            var e = Col(v.x, v.z);
            return TerrainGen.DetailField.Material(in spec, in o, in e.c, in e.cd, e.st, v.x, v.y, v.z);
        }

        // ---- the search: 32^3 ore cells in rings around the spawn ----
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        int3[] dirs = { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 0, 1), new int3(0, 0, -1) };
        bool found = false;
        int3 ore = default, dOut = default;
        int airRun = 0, cellsSeen = 0, deposits = 0;
        int fcx = f.x >> TerrainGen.Ores.CELL_SHIFT, fcz = f.z >> TerrainGen.Ores.CELL_SHIFT;
        for (int r = 0; r <= 90 && !found; r++)
        {
            for (int dz = -r; dz <= r && !found; dz++)
            for (int dx = -r; dx <= r && !found; dx++)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int cx = fcx + dx, cz = fcz + dz;
                var top = Col((cx << 5) + 16, (cz << 5) + 16);
                if (top.c.height < spec.seaLevelY) continue;                       // land only
                for (int cy = (top.c.height - 450) >> 5; cy <= (top.c.height - 80) >> 5 && !found; cy++)
                {
                    cellsSeen++;
                    int layer = TerrainGen.Ores.Layer(in spec, (cy << 5) + 16, top.cd.deepOffset, top.cd.cavernOffset, top.cd.underOffset);
                    int chance = TerrainGen.Ores.CellChance(layer, top.c.biome, top.cd.oreMul);
                    if ((int)(TerrainGen.Ores.Hash(spec.seed, cx, cy, cz) & 0xFFFFu) >= chance) continue;
                    deposits++;
                    for (int b = 0; b < 8 && !found; b++)
                    {
                        int3 v = new int3((cx << 5) + 8 + ((b & 1) << 3) + 3, (cy << 5) + 8 + (((b >> 1) & 1) << 3) + 3, (cz << 5) + 8 + (((b >> 2) & 1) << 3) + 3);
                        if (!TerrainGen.Ores.IsOre(Mat(v))) continue;
                        foreach (var d in dirs)
                        {
                            int k = 1;
                            while (k <= 8 && TerrainGen.Ores.IsOre(Mat(v + d * k))) k++;
                            if (k > 8 || Mat(v + d * k) != TerrainGen.GenMat.Air) continue;
                            // Room for the camera: air 1.2 m out from the wall at eye level.
                            int n = 0;
                            while (n < 30 && Mat(v + d * (k + n)) == TerrainGen.GenMat.Air && Mat(v + d * (k + n) + new int3(0, 3, 0)) == TerrainGen.GenMat.Air) n++;
                            if (n < 14) continue;
                            ore = v + d * (k - 1); dOut = d; airRun = n; found = true;
                            break;
                        }
                    }
                }
            }
            if ((r & 1) == 0) yield return null;
        }
        Debug.Log($"[OreDemo] search: {cellsSeen} cells, {deposits} deposits, " +
                  (found ? $"exposed ore at {ore} ({TerrainGen.Ores.OreOf(TerrainGen.Ores.LayerOf(Mat(ore)))}: {Items.Name(Materials.FromTg2(Mat(ore)))}), " +
                           $"{math.length((float2)(ore.xz - f.xz)) * 0.1f:F0} m from the spawn, {(Col(ore.x, ore.z).c.height - ore.y) * 0.1f:F1} m under the top, air {airRun * 0.1f:F1} m out along {dOut}"
                         : "NO exposed deposit within ~290 m"));
        if (!found) { Application.Quit(); yield break; }

        // ---- 1. the tunnel wall: fly camera 1.3-2.2 m out, a little above, looking at it ----
        byte oreEngine = Materials.FromTg2(Mat(ore));
        int back = math.min(airRun - 1, 22);
        int3 camV = ore + dOut * back + new int3(0, 2, 0);
        _mode = MoveMode.Fly;
        var camT = Camera.main.transform;
        float3 camM = new float3(camV.x + 0.5f, camV.y + 0.5f, camV.z + 0.5f) * 0.1f;
        float3 tgtM = new float3(ore.x + 0.5f, ore.y + 0.5f, ore.z + 0.5f) * 0.1f;
        float3 look = tgtM - camM;
        // A little off-axis so the wall reads as a wall, not a flat colour.
        float yaw = math.degrees(math.atan2(look.x, look.z)) + 12f;
        float pitch = -math.degrees(math.atan2(look.y, math.length(look.xz)));
        camT.position = camM;
        _cam?.SetLook(yaw, pitch);
        float tw = Time.unscaledTime;
        int3 oreChunk = CoordMath.VoxelToChunk(ore);
        while (Time.unscaledTime - tw < 90f && !(_store.IsResident(oreChunk) && _store.GetVoxel(ore) == oreEngine))
        { camT.position = camM; yield return null; }
        bool loaded = _store.GetVoxel(ore) == oreEngine;
        // Two torches between the camera and the wall (TrySetVoxel registers the
        // point light; SetBox does not): one up and to one side, one low on the other.
        int3 side = new int3(dOut.z, 0, -dOut.x);
        int lit = 0;
        int3[] torches =
        {
            ore + dOut * math.max(4, back / 2) + new int3(0, 7, 0) + side * 5,
            ore + dOut * math.max(4, back / 2) + new int3(0, 3, 0),
            ore + dOut * 4 + new int3(0, -3, 0) - side * 7,
            ore + dOut * 4 - side * 4,
        };
        foreach (var t in torches)
        {
            if (lit >= 2) break;
            if (_store.GetVoxel(t) == Materials.Air && _edits.TrySetVoxel(t, Materials.Torch)) lit++;
        }
        for (int i = 0; i < 150; i++) { camT.position = camM; yield return null; }
        Debug.Log($"[OreDemo] camera {camV} ({back * 0.1f:F1} m out), loaded world says {Items.Name(_store.GetVoxel(ore))} at the ore voxel (expected {Items.Name(oreEngine)}, match {loaded}); {lit} torch(es) lit, lights registered {_edits.LightsRegistered}");
        yield return DemoShot(dir, "1_cave_wall_ore");

        // ---- 2/3. dig the deposit through the Survival path; the hotbar and the E panel ----
        int dug = 0, credited = 0;
        for (int pass = 0; pass < 12; pass++)
        {
            int3 hit = default; bool any = false;
            for (int y = -8; y <= 8 && !any; y++)
            for (int z = -8; z <= 8 && !any; z++)
            for (int x = -8; x <= 8 && !any; x++)
            {
                int3 v = ore + new int3(x, y, z);
                if (_store.GetVoxel(v) == oreEngine) { hit = v; any = true; }
            }
            if (!any) break;
            var res = SurvivalDig(hit, 3, 150);
            dug += res.Removed; credited += res.Credited;
            yield return null;
        }
        int slot = -1;
        for (int i = 0; i < Inventory.SlotCount; i++) if (!_inv.IsEmpty(i) && _inv.ItemAt(i) == oreEngine) { slot = i; break; }
        if (slot >= Inventory.HotbarSize) { _inv.Swap(slot, 1); slot = 1; }
        if (slot >= 0) SelectSlot(slot);
        _status = slot >= 0 ? $"+{_inv.Count(oreEngine)} {Items.Name(oreEngine)}" : _status;
        for (int i = 0; i < 30; i++) { camT.position = camM; yield return null; }
        Debug.Log($"[OreDemo] dug {dug} voxels around the deposit, credited {credited}; {Items.Name(oreEngine)} in slot {slot}: {(slot >= 0 ? _inv.Count(oreEngine) : 0)}; {InventoryLine()}");
        yield return DemoShot(dir, "2_hotbar_ore");
        InventoryOpen = true;
        for (int i = 0; i < 4; i++) yield return null;
        yield return DemoShot(dir, "3_inventory_ore");
        CloseInventory();
        SaveInventory();

        // ---- 4. the dev map's close slice through the deposit's height ----
        var dm = DevMap.Instance;
        if (dm != null && dm.IsTg2)
        {
            DevMap.Toggle();
            for (int i = 0; i < 10; i++) yield return null;
            dm.SetView(new Vector3(tgtM.x, tgtM.y, tgtM.z), 30f, 55f, 120f);
            dm.SetSliceY(ore.y);
            dm.StartSlice(true, true);
            float ts = Time.unscaledTime;
            while (Time.unscaledTime - ts < 120f && (dm.SlicePrepFraction < 1f || !dm.SliceShowing(ore.y))) yield return null;
            for (int i = 0; i < 10; i++) yield return null;
            Debug.Log($"[OreDemo] dev map close slice at Y {ore.y} ({(ore.y - spec.seaLevelY) * 0.1f:F1} m vs sea), {dm.SliceVoxelsPerPixel * 0.1f:F1} m per pixel, shown {dm.SliceShowing(ore.y)}");
            yield return DemoShot(dir, "4_devmap_close_slice");
            DevMap.Toggle();
        }
        Debug.Log("[OreDemo] done");
        Application.Quit();
    }
}
