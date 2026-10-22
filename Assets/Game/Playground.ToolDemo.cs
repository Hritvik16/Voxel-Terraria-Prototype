// Assets/Game/Playground.ToolDemo.cs
//
// -tooldemo (task 105): gameplay step 10's acceptance screenshots. An
// INTERACTIVE Survival run (run-play.sh EXTRA="-tooldemo"), so the HUD and the
// LMB dig path (SurvivalPickDig) are exactly what a player gets. Then quits.
//   1  an iron vein, COPPER pick: LMB refused (status line + the dig box's TOO HARD)
//   2  the same vein, GOLD pick, a few seconds of LMB: the vein half dug
//   3  the same, dug out: the hotbar holds the iron ore
// The vein is found on the TerrainGen side as -oredemo does (DetailField.Material),
// nearest the spawn first, in the IRON layer (deep stone, sea - 50 .. - 100 m).
// Iron that deep is rarely on a tunnel wall, so the demo carves a small viewing
// room in front of the deposit (Creative SetBox, demo set-up only) and lights it
// with torches: underground, there is no sun to put behind the camera.
// Shots: persistentDataPath/ToolDemo/.

using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public partial class Playground
{
    private IEnumerator ToolDemo()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 25f || _player == null || !_player.Ready) yield return null;
        SetGameMode(true);
        _showHelp = false;
        _demoHideState = true;
        SunCycle.SetHours(SunCycle.NoonHours);
        VoxelEngine.Rendering.LightingDriver.ClockPaused = true;
        string dir = System.IO.Path.Combine(Application.persistentDataPath, "ToolDemo");
        System.IO.Directory.CreateDirectory(dir);
        if (_cam == null && Camera.main != null) _cam = Camera.main.GetComponent<PlaygroundFlyCamera>();
        var src = Phase4Bootstrapper.Streamer.TerrainGenSource;
        if (src == null) { Debug.Log("[ToolDemo] not a tg2 world -- nothing to show"); Application.Quit(); yield break; }

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

        // ---- the search: iron-layer 32^3 ore cells in rings around the spawn ----
        int3 f = CoordMath.WorldToVoxel(_player.Motor.PositionM);
        bool found = false;
        int3 ore = default;
        int cellsSeen = 0, deposits = 0;
        int fcx = f.x >> TerrainGen.Ores.CELL_SHIFT, fcz = f.z >> TerrainGen.Ores.CELL_SHIFT;
        int cyLo = (spec.seaLevelY - TerrainGen.Ores.GOLD_TOP_BELOW_SEA) >> TerrainGen.Ores.CELL_SHIFT;
        int cyHi = (spec.seaLevelY - 500) >> TerrainGen.Ores.CELL_SHIFT;
        for (int r = 0; r <= 60 && !found; r++)
        {
            for (int dz = -r; dz <= r && !found; dz++)
            for (int dx = -r; dx <= r && !found; dx++)
            {
                if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                int cx = fcx + dx, cz = fcz + dz;
                var top = Col((cx << 5) + 16, (cz << 5) + 16);
                if (top.c.height < spec.seaLevelY) continue;                       // under land: the player can get there
                for (int cy = cyHi; cy >= cyLo && !found; cy--)                     // shallowest first
                {
                    cellsSeen++;
                    int layer = TerrainGen.Ores.Layer(in spec, (cy << 5) + 16, top.cd.deepOffset, top.cd.cavernOffset, top.cd.underOffset);
                    if (TerrainGen.Ores.OreOf(layer) != TerrainGen.Ores.Iron) continue;
                    int chance = TerrainGen.Ores.CellChance(layer, top.c.biome, top.cd.oreMul);
                    if ((int)(TerrainGen.Ores.Hash(spec.seed, cx, cy, cz) & 0xFFFFu) >= chance) continue;
                    deposits++;
                    for (int b = 0; b < 8 && !found; b++)
                    {
                        int3 v = new int3((cx << 5) + 8 + ((b & 1) << 3) + 3, (cy << 5) + 8 + (((b >> 1) & 1) << 3) + 3, (cz << 5) + 8 + (((b >> 2) & 1) << 3) + 3);
                        if (Mat(v) != TerrainGen.Ores.Iron) continue;
                        ore = v; found = true;
                    }
                }
            }
            if ((r & 1) == 0) yield return null;
        }
        Debug.Log($"[ToolDemo] search: {cellsSeen} cells, {deposits} iron deposits, " +
                  (found ? $"iron at {ore}, {math.length((float2)(ore.xz - f.xz)) * 0.1f:F0} m from the spawn, " +
                           $"{(ore.y - spec.seaLevelY) * 0.1f:F1} m vs sea, {(Col(ore.x, ore.z).c.height - ore.y) * 0.1f:F1} m under the top"
                         : "NO iron deposit within ~190 m"));
        if (!found) { Application.Quit(); yield break; }

        // The deposit's +x face (its last iron voxel along +x from the found one).
        int3 dOut = new int3(1, 0, 0);
        int k = 0;
        while (k < 12 && Mat(ore + dOut * (k + 1)) == TerrainGen.Ores.Iron) k++;
        int3 face = ore + dOut * k;
        byte iron = Materials.FromTg2(TerrainGen.Ores.Iron);

        // ---- the viewing room: 2.4 m deep, 2.0 m wide, 1.8 m tall, in front of the face ----
        int3 roomLo = face + new int3(1, -6, -10), roomHi = face + new int3(24, 12, 10);
        int3 camV = face + new int3(16, 3, 4);
        _mode = MoveMode.Fly;
        var camT = Camera.main.transform;
        float3 camM = new float3(camV.x + 0.5f, camV.y + 0.5f, camV.z + 0.5f) * 0.1f;
        float3 tgtM = new float3(face.x + 0.5f, face.y + 0.5f, face.z + 0.5f) * 0.1f;
        float3 look = tgtM - camM;
        camT.position = camM;
        _cam?.SetLook(math.degrees(math.atan2(look.x, look.z)), -math.degrees(math.atan2(look.y, math.length(look.xz))));
        bool Resident(int3 v) => _store.IsResident(CoordMath.VoxelToChunk(v));
        bool RoomResident() => Resident(roomLo) && Resident(roomHi) && Resident(new int3(roomLo.x, roomLo.y, roomHi.z)) &&
                               Resident(new int3(roomHi.x, roomHi.y, roomLo.z)) && Resident(face - dOut * 8) && _store.GetVoxel(face) == iron;
        float tw = Time.unscaledTime;
        while (Time.unscaledTime - tw < 90f && !RoomResident()) { camT.position = camM; yield return null; }
        bool loaded = RoomResident();
        int carved = _edits.SetBox(roomLo, roomHi, Materials.Air);
        int lit = 0;
        int3[] torches = { face + new int3(6, 9, -7), face + new int3(6, 9, 7), face + new int3(10, -5, 0), face + new int3(3, -5, -6) };
        foreach (var t in torches)
        {
            if (_store.GetVoxel(t) == Materials.Air && _edits.TrySetVoxel(t, Materials.Torch)) lit++;
        }
        for (int i = 0; i < 120; i++) { camT.position = camM; yield return null; }
        int veinBefore = CountNear(face, 12, iron);
        Debug.Log($"[ToolDemo] room: loaded {loaded}, carved {carved} voxels, {lit} torch(es); camera {camV} ({(camV.x - face.x) * 0.1f:F1} m from the face); " +
                  $"iron voxels within 1.2 m of the face: {veinBefore}; aimed {Items.Name(_targetMaterial)} at {_targetVoxel}");

        // Aim: the iron voxel nearest the camera (the crosshair follows it).
        int3 NearestIron()
        {
            int3 best = face; int bestD = int.MaxValue;
            for (int z = -12; z <= 12; z++) for (int y = -12; y <= 12; y++) for (int x = -12; x <= 12; x++)
            {
                int3 v = face + new int3(x, y, z);
                if (_store.GetVoxel(v) != iron) continue;
                int3 d = v - camV; int d2 = math.dot(d, d);
                if (d2 < bestD) { bestD = d2; best = v; }
            }
            return bestD == int.MaxValue ? new int3(int.MinValue) : best;
        }
        void AimAt(int3 v)
        {
            float3 l = new float3(v.x + 0.5f, v.y + 0.5f, v.z + 0.5f) * 0.1f - camM;
            _cam?.SetLook(math.degrees(math.atan2(l.x, l.z)), -math.degrees(math.atan2(l.y, math.length(l.xz))));
        }

        // ---- 1. copper pick: refused ----
        SetPick(1);
        int3 aim = NearestIron();
        AimAt(aim);
        SurvivalBuild.DigResult res = default;
        for (int i = 0; i < 30; i++)
        {
            camT.position = camM;
            yield return null;
            res = SurvivalPickDig(aim, Time.deltaTime);
        }
        Debug.Log($"[ToolDemo] copper pick at {Items.Name(_store.GetVoxel(aim))} {aim}: refused {res.Refused}, removed {res.Removed}; " +
                  $"iron left {CountNear(face, 12, iron)} of {veinBefore}; status \"{_status}\"");
        yield return DemoShot(dir, "1_iron_vein_copper_pick_refused");

        // ---- 2/3. gold pick: dug ----
        SetPick(2);
        int dug = 0, credited = 0, frames = 0;
        bool halfShot = false;
        float td = Time.unscaledTime;
        while (Time.unscaledTime - td < 40f)
        {
            aim = NearestIron();
            if (aim.x == int.MinValue) break;
            AimAt(aim);
            camT.position = camM;
            yield return null;
            res = SurvivalPickDig(aim, Time.deltaTime);
            dug += res.Removed; credited += res.Credited; frames++;
            if (!halfShot && CountNear(face, 12, iron) * 2 <= veinBefore)
            {
                halfShot = true;
                Debug.Log($"[ToolDemo] gold pick, half the vein: {dug} voxels dug in {Time.unscaledTime - td:F1} s, iron in the hotbar {_inv.Count(iron)}");
                yield return DemoShot(dir, "2_gold_pick_digging");
            }
        }
        float took = Time.unscaledTime - td;
        _cam?.SetLook(math.degrees(math.atan2(look.x, look.z)), -math.degrees(math.atan2(look.y, math.length(look.xz))));
        for (int i = 0; i < 20; i++) { camT.position = camM; yield return null; }
        Debug.Log($"[ToolDemo] gold pick: {dug} voxels dug ({credited} credited) in {took:F1} s / {frames} frames; iron left {CountNear(face, 12, iron)} of {veinBefore}; " +
                  $"iron ore in the inventory {_inv.Count(iron)}; status \"{_status}\"; {InventoryLine()}");
        yield return DemoShot(dir, "3_gold_pick_vein_dug");
        Debug.Log("[ToolDemo] done");
        Application.Quit();
    }

    private int CountNear(int3 c, int r, byte m)
    {
        int n = 0;
        for (int z = -r; z <= r; z++) for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
            if (_store.GetVoxel(c + new int3(x, y, z)) == m) n++;
        return n;
    }
}
